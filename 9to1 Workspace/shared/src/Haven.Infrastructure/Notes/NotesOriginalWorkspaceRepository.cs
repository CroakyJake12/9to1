using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Thin same-owner adapter for the existing Boards core, not another document engine.
/// Trusted native composition supplies the actual Home review presenter and route lifetime.
/// A presenter returning does not grant permission: the original broker must acknowledge the
/// real decision before every protected read/save. Construction performs no IO or setup.</summary>
public sealed class NotesOriginalWorkspaceRepository : INotesRepository, IBoardsNotebookSummarySource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly NotesRepository _notes;
    private readonly VerifiedNotesRepository _verified;
    private readonly HomeNotesResourceAdmissionSource _home;
    private readonly string _sessionId;
    private readonly Func<HomeNotesLibraryReadReview, CancellationToken, Task> _presentRead;
    private readonly Func<HomeNotesSaveReview, CancellationToken, Task> _presentSave;
    private readonly CancellationToken _lifetime;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private (INotesOriginalSaveIntent Intent, HomeNotesSaveAdmission Home)? _retainedSave;
    private (INotesOriginalLibraryReadRequest Request, HomeNotesLibraryReadAdmission Home)? _retainedRead;
    private (INotesOriginalSaveIntent Intent, HomeNotesSaveReview Review, byte[] Input, string Reason)? _pendingSaveReview;
    private (INotesOriginalLibraryReadRequest Request, HomeNotesLibraryReadReview Review)? _pendingReadReview;
    public string? LastReadAuditWarning { get; private set; }

    private NotesOriginalWorkspaceRepository(NotesRepository notes, VerifiedNotesRepository verified,
        HomeNotesResourceAdmissionSource home, string sessionId,
        Func<HomeNotesLibraryReadReview, CancellationToken, Task> presentRead,
        Func<HomeNotesSaveReview, CancellationToken, Task> presentSave, CancellationToken lifetime)
    { _notes = notes; _verified = verified; _home = home; _sessionId = sessionId;
        _presentRead = presentRead; _presentSave = presentSave; _lifetime = lifetime; }

    public static NotesOriginalWorkspaceRepository BindOriginal(NotesRepository notes, VerifiedNotesRepository verified,
        HomeNotesResourceAdmissionSource home, string sessionId,
        Func<HomeNotesLibraryReadReview, CancellationToken, Task> presentRead,
        Func<HomeNotesSaveReview, CancellationToken, Task> presentSave, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(notes); ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(home); ArgumentNullException.ThrowIfNull(presentRead);
        ArgumentNullException.ThrowIfNull(presentSave); ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lifetime.ThrowIfCancellationRequested();
        if (!lifetime.CanBeCanceled || !verified.IsBoundToOriginal(notes) || !home.IsBoundToOriginalNotes(notes))
            throw new UnauthorizedAccessException("Retain the original Notes/verified/Home composition and actual route lifetime.");
        return new(notes, verified, home, sessionId, presentRead, presentSave, lifetime);
    }

    public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken)
        => RunAsync(async token => (await ReadAsync(null, token).ConfigureAwait(false)).Snapshot.Documents, cancellationToken);

    public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(bool deleted, CancellationToken cancellationToken)
        => RunAsync(async token =>
        {
            var read = await ReadAsync(null, token).ConfigureAwait(false);
            return await _notes.CreateOriginalBoardsSummarySource(read.Request, read.Snapshot, read.Home)
                .ListAsync(deleted, token).ConfigureAwait(false);
        }, cancellationToken);

    public Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("A canonical Notes document ID is required.", nameof(documentId));
        return RunAsync(async token => (await ReadAsync(documentId, token).ConfigureAwait(false)).Snapshot.SelectedDocument,
            cancellationToken);
    }

    public Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!BoardsWorkspaceService.IsBoardsNotebook(document))
            throw new UnauthorizedAccessException("The original Boards workspace saves genuine canonical Boards documents only.");
        // Before any await: pending UI edits cannot change the candidate reviewed by Home.
        var originalBytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        var captured = JsonSerializer.Deserialize<NotesDocument>(originalBytes, Json)
            ?? throw new InvalidDataException("The original Boards candidate could not be detached.");
        return RunAsync(async token =>
        {
            if (_pendingReadReview is not null)
                throw new UnauthorizedAccessException("Resolve the retained original read review before starting a save.");
            if (_pendingSaveReview is { } pending &&
                (!pending.Input.AsSpan().SequenceEqual(originalBytes) || pending.Reason != reason))
                throw new UnauthorizedAccessException("The retained original review belongs to an earlier draft; resolve it before reviewing a changed candidate.");
            INotesOriginalSaveIntent intent; HomeNotesSaveReview review;
            if (_pendingSaveReview is { } retained) { intent = retained.Intent; review = retained.Review; }
            else
            {
                intent = await _notes.PrepareOriginalSaveAsync(captured, reason, token).ConfigureAwait(false);
                review = await _home.RequestSaveAsync(intent, _sessionId, token).ConfigureAwait(false);
                _pendingSaveReview = (intent, review, originalBytes, reason);
            }
            // Retain before ANY borrowed presenter/Begin callback, including task faults.
            await PresentOriginalAsync(() => _presentSave(review, token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var home = await _home.BeginSaveAsync(review, token).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The original save still needs its genuine Home decision.");
            _pendingSaveReview = null; _retainedSave = (intent, home);
            NotesSaveResult receipt;
            try { receipt = await _notes.SaveOriginalWithHomeAsync(intent, home, _verified, token).ConfigureAwait(false); }
            catch (Exception error) when (_notes.GetObservedOriginalSaveOutcome(intent) is
                { Disposition: NotesOriginalSaveDisposition.Uncertain })
            { throw new UncertainOriginalPublication(this, intent, error); }
            // Canonical acknowledgement fields come only from the actual owning receipt.
            // Content is never overwritten with the reviewed draft if newer edits exist.
            if (originalBytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(document, Json)))
            {
                if (!_notes.TryApplyOriginalSaveAcknowledgement(intent, receipt, document))
                    throw new InvalidDataException("The actual original receipt did not match its retained candidate acknowledgement.");
            }
            return receipt;
        }, cancellationToken);
    }

    /// <summary>Used by the SAME Boards core's existing uncertain-commit path. Exception type
    /// alone, arbitrary IO errors and cancellation flags cannot fabricate an unknown commit.</summary>
    public bool IsUnknownCommitOutcome(Exception error) => error is UncertainOriginalPublication original &&
        ReferenceEquals(original.Issuer, this) && _notes.GetObservedOriginalSaveOutcome(original.Intent) is
        { Disposition: NotesOriginalSaveDisposition.Uncertain };

    private sealed class UncertainOriginalPublication(NotesOriginalWorkspaceRepository issuer,
        INotesOriginalSaveIntent intent, Exception cause)
        : IOException("The original publication has no confirmed outcome; inspect saved state before retrying.", cause)
    {
        internal NotesOriginalWorkspaceRepository Issuer { get; } = issuer;
        internal INotesOriginalSaveIntent Intent { get; } = intent;
    }

    /// <summary>Audit-only recovery of the retained original observations. Never repeats a
    /// read or mutation and never fabricates a successful result from an absent outcome.</summary>
    public Task<IReadOnlyList<HavenOS.Home.PermissionsTrustNotifications.HomePermissionOperationResult>> RetryObservedAuditsAsync(
        CancellationToken cancellationToken) => RunAsync(SettleRetainedAuditsAsync, cancellationToken, settlePrevious: false);

    private async Task<IReadOnlyList<HavenOS.Home.PermissionsTrustNotifications.HomePermissionOperationResult>> SettleRetainedAuditsAsync(
        CancellationToken token)
    {
        var results = new List<HavenOS.Home.PermissionsTrustNotifications.HomePermissionOperationResult>();
        if (_pendingSaveReview is { } saveReview)
        {
            if (saveReview.Review.WasDispatchAttempted)
            {
                var audit = await _home.RetrySaveDispatchAuditAsync(saveReview.Review, token).ConfigureAwait(false);
                results.Add(audit); if (audit.Succeeded) _pendingSaveReview = null;
            }
            else if (IsRefused((await _home.GetSaveAuthorizationAsync(saveReview.Review, token).ConfigureAwait(false)).State))
                _pendingSaveReview = null;
        }
        if (_pendingReadReview is { } readReview)
        {
            if (readReview.Review.WasDispatchAttempted)
            {
                var audit = await _home.RetryLibrarySnapshotDispatchAuditAsync(readReview.Review, token).ConfigureAwait(false);
                results.Add(audit); if (audit.Succeeded) _pendingReadReview = null;
            }
            else if (IsRefused((await _home.GetLibrarySnapshotAuthorizationAsync(readReview.Review, token).ConfigureAwait(false)).State))
                _pendingReadReview = null;
        }

        if (_retainedSave is { } save)
        {
            if (_notes.GetObservedOriginalSaveOutcome(save.Intent) is null)
                throw new IOException("The previous original save has no retained observed outcome; preserve its admission for recovery.");
            var result = await _notes.RetryOriginalSaveAuditAsync(save.Intent, save.Home, token).ConfigureAwait(false);
            results.Add(result); if (result.Succeeded) _retainedSave = null;
        }
        if (_retainedRead is { } read)
        {
            var result = await _notes.RetryOriginalLibraryReadAuditAsync(read.Request, read.Home, token).ConfigureAwait(false);
            results.Add(result); if (result.Succeeded) { _retainedRead = null; LastReadAuditWarning = null; }
        }
        return results.AsReadOnly();
    }

    private static bool IsRefused(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState state)
        => state is HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.Denied or
            HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.Blocked or
            HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequestState.Cancelled;

    private async Task<(INotesOriginalLibraryReadRequest Request, NotesOriginalLibrarySnapshot Snapshot,
        HomeNotesLibraryReadAdmission Home)> ReadAsync(Guid? id, CancellationToken token)
    {
        if (_pendingSaveReview is not null)
            throw new UnauthorizedAccessException("Resolve the retained original save review before reading another snapshot.");
        if (_pendingReadReview is { } previous && previous.Request.DocumentId != id)
            throw new UnauthorizedAccessException("Resolve the retained original document review before selecting another document.");
        INotesOriginalLibraryReadRequest request; HomeNotesLibraryReadReview review;
        if (_pendingReadReview is { } retained) { request = retained.Request; review = retained.Review; }
        else
        {
            request = await _notes.PrepareOriginalLibraryReadAsync(id, token).ConfigureAwait(false);
            review = await _home.RequestLibrarySnapshotAsync(request, _sessionId, token).ConfigureAwait(false);
            _pendingReadReview = (request, review);
        }
        await PresentOriginalAsync(() => _presentRead(review, token)).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var home = await _home.BeginLibrarySnapshotAsync(review, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original read still needs its genuine Home decision.");
        _pendingReadReview = null; _retainedRead = (request, home);
        var snapshot = await _notes.ReadOriginalLibraryWithHomeAsync(request, home, _verified, token).ConfigureAwait(false);
        LastReadAuditWarning = snapshot.AuditWarning;
        if (snapshot.AuditWarning is not null)
            throw new IOException("The original read completed, but its retained Home audit needs recovery before opening this workspace.");
        _retainedRead = null; // Actual successful audit: do not retain every selected body in memory.
        return (request, snapshot, home);
    }

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken caller, bool settlePrevious = true)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime, caller);
        await _operations.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            if (settlePrevious && (await SettleRetainedAuditsAsync(linked.Token).ConfigureAwait(false)).Any(result => !result.Succeeded))
                throw new IOException("The previous original Home audit is still unresolved; its admission was preserved.");
            return await operation(linked.Token).ConfigureAwait(false);
        }
        finally { _operations.Release(); }
    }

    private static async Task PresentOriginalAsync(Func<Task> present)
    {
        Task? actual = null;
        try
        {
            actual = present() ?? throw new InvalidOperationException("The original Home presenter returned no task.");
            await actual.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            // Faulted/synchronous OCE is not a canceled callback task. Retain the actual
            // original failure objects through the same Notes task-observation mechanism.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                NotesRepository.OriginalBoundaryFailure(actual, error)).Throw();
        }
    }

    // These operations need their own genuine original recovery/history/delete admissions.
    // Refuse before IO; absence is a full-scope acceptance blocker, never empty/success data.
    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken)
        => Task.FromException(new NotSupportedException("Original physical delete admission is not implemented."));
    public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid documentId, CancellationToken cancellationToken)
        => Task.FromException<IReadOnlyList<NotesVersionInfo>>(new NotSupportedException("Original version-history read admission is not implemented."));
    public Task<NotesDocument?> LoadVersionAsync(Guid documentId, string versionId, CancellationToken cancellationToken)
        => Task.FromException<NotesDocument?>(new NotSupportedException("Original version read admission is not implemented."));
    public Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken)
        => Task.FromException<NotesDocument?>(new NotSupportedException("Original recovery needs separate mutation authority."));
    public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
        => Task.FromException<IReadOnlyList<NotesSearchHit>>(new NotSupportedException("Original full-content search read admission is not implemented."));
}
