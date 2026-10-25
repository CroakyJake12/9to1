using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

/// <summary>
/// One-document Write view over the original Notes owner. Reads and saves use distinct Write
/// Home actions, original Notes intents/outcomes and the same verified transaction fences as
/// Boards; this adapter cannot create, delete, browse to, or save a different document.
/// </summary>
public sealed class NotesOriginalWriteWorkspaceRepository : INotesRepository, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly NotesRepository _notes;
    private readonly VerifiedNotesRepository _verified;
    private readonly HomeNotesResourceAdmissionSource _home;
    private readonly Guid _documentId;
    private readonly string _sessionId;
    private readonly Func<HomeNotesLibraryReadReview, CancellationToken, Task> _presentRead;
    private readonly Func<HomeNotesSaveReview, CancellationToken, Task> _presentSave;
    private readonly bool _allowWrites;
    private readonly Action<Action> _originalScope;
    private readonly Action<Task> _retainOriginalTask;
    private readonly CancellationToken _lifetime;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private long _expectedVersion;
    private NotesDocument? _cachedRead;
    private bool _retiring, _unsettledSave;
    private Task? _close;

    private NotesOriginalWriteWorkspaceRepository(NotesRepository notes, VerifiedNotesRepository verified,
        HomeNotesResourceAdmissionSource home, Guid documentId, long expectedVersion, string sessionId,
        Func<HomeNotesLibraryReadReview, CancellationToken, Task> presentRead,
        Func<HomeNotesSaveReview, CancellationToken, Task> presentSave,
        Action<Action> originalScope, Action<Task> retainOriginalTask, CancellationToken lifetime, bool allowWrites)
    {
        _notes = notes; _verified = verified; _home = home; _documentId = documentId;
        _expectedVersion = expectedVersion; _sessionId = sessionId; _presentRead = presentRead; _allowWrites = allowWrites;
        _presentSave = presentSave; _originalScope = originalScope; _retainOriginalTask = retainOriginalTask; _lifetime = lifetime;
    }

    public static NotesOriginalWriteWorkspaceRepository BindOriginal(NotesRepository notes,
        VerifiedNotesRepository verified, HomeNotesResourceAdmissionSource home, Guid documentId,
        long expectedVersion, string sessionId,
        Func<HomeNotesLibraryReadReview, CancellationToken, Task> presentRead,
        Func<HomeNotesSaveReview, CancellationToken, Task> presentSave,
        Action<Action> originalScope, Action<Task> retainOriginalTask, CancellationToken lifetime,
        bool allowWrites = true)
    {
        ArgumentNullException.ThrowIfNull(notes); ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(home); ArgumentNullException.ThrowIfNull(presentRead);
        ArgumentNullException.ThrowIfNull(presentSave); ArgumentNullException.ThrowIfNull(originalScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lifetime.ThrowIfCancellationRequested();
        if (documentId == Guid.Empty || expectedVersion < 0 || !lifetime.CanBeCanceled ||
            !verified.IsBoundToOriginal(notes) || !home.IsBoundToOriginalNotes(notes))
            throw new UnauthorizedAccessException("Retain the original Write artifact, Notes/verified/Home owners and actual presentation lifetime.");
        return new(notes, verified, home, documentId, expectedVersion, sessionId,
            presentRead, presentSave, originalScope, retainOriginalTask, lifetime, allowWrites);
    }

    public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken) =>
        RunAsync(async token =>
        {
            var document = await ReadPinnedAsync(token).ConfigureAwait(false);
            return (IReadOnlyList<NotesDocumentSummary>)[NotesDocumentSummaryFor(document)];
        }, cancellationToken);

    public Task<NotesDocument?> LoadAsync(Guid documentId, CancellationToken cancellationToken)
    {
        DemandSelected(documentId);
        return RunAsync<NotesDocument?>(async token => await ReadPinnedAsync(token).ConfigureAwait(false), cancellationToken);
    }

    public Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        DemandSelected(document.Id);
        if (!_allowWrites) throw new UnauthorizedAccessException("This embedded Write page is read-only.");
        if (document.Version < 0 || document.Version != Volatile.Read(ref _expectedVersion) || IsBoardsDocument(document))
            throw new UnauthorizedAccessException("Save only the selected current Write document revision.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        var captured = JsonSerializer.Deserialize<NotesDocument>(bytes, Json)
            ?? throw new InvalidDataException("The selected Write document could not be detached for Home review.");
        return RunAsync(async token => await SavePinnedAsync(document, captured, bytes, reason, token).ConfigureAwait(false),
            cancellationToken);
    }

    private async Task<NotesSaveResult> SavePinnedAsync(NotesDocument target, NotesDocument captured,
        byte[] reviewedBytes, string reason, CancellationToken token)
    {
        if (_unsettledSave) throw new IOException("The previous Write publication or Home audit remains unresolved; do not retry it.");
        if (captured.Id != _documentId || captured.Version != _expectedVersion || IsBoardsDocument(captured))
            throw new UnauthorizedAccessException("The Write candidate no longer matches the selected original artifact revision.");
        var intent = await _notes.PrepareOriginalSaveAsync(captured, reason, token).ConfigureAwait(false);
        if (!_notes.IsIssuedOriginalSaveIntent(intent) || intent.DocumentId != _documentId ||
            intent.ExpectedVersion != _expectedVersion || intent.StoreObservation.Revision.Length == 0)
            throw new InvalidDataException("The original Notes owner prepared a different Write save intent.");
        var review = await _home.RequestWriteDocumentSaveWithinOriginalSourceAsync(intent, _sessionId,
            _originalScope, _retainOriginalTask, token).ConfigureAwait(false);
        var authorization = await _home.GetSaveAuthorizationWithinOriginalSourceAsync(review,
            _originalScope, _retainOriginalTask, token).ConfigureAwait(false);
        if (authorization.State == HomePermissionRequestState.PendingApproval)
            await _presentSave(review, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        authorization = await _home.GetSaveAuthorizationWithinOriginalSourceAsync(review,
            _originalScope, _retainOriginalTask, token).ConfigureAwait(false);
        if (!authorization.IsAllowed || authorization.State != HomePermissionRequestState.Approved)
            throw new UnauthorizedAccessException(authorization.Message.Length == 0
                ? "Home did not approve this Write document save." : authorization.Message);
        var admission = await _home.BeginSaveWithinOriginalSourceAsync(review,
            _originalScope, _retainOriginalTask, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original Write save still needs its genuine Home decision.");
        NotesSaveResult receipt;
        try { receipt = await _notes.SaveOriginalWithHomeAsync(intent, admission, _verified, token).ConfigureAwait(false); }
        catch
        {
            if (_notes.GetObservedOriginalSaveOutcome(intent) is { } outcome &&
                outcome.Disposition != NotesOriginalSaveDisposition.Refused)
                _unsettledSave = true;
            throw;
        }
        if (receipt.DocumentId != _documentId || receipt.Version != intent.ExpectedVersion + 1 ||
            !reviewedBytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(target, Json)) ||
            !_notes.TryApplyOriginalSaveAcknowledgement(intent, receipt, target))
        {
            _unsettledSave = true;
            throw new InvalidDataException("The original Notes acknowledgement does not match this Write candidate.");
        }
        Volatile.Write(ref _expectedVersion, receipt.Version);
        _cachedRead = Detach(target);
        return receipt;
    }

    private async Task<NotesDocument> ReadPinnedAsync(CancellationToken token)
    {
        if (_unsettledSave) throw new IOException("The previous Write publication or Home audit remains unresolved.");
        var expected = Volatile.Read(ref _expectedVersion);
        if (_cachedRead is { } cached && cached.Version == expected)
            return Detach(cached);
        var request = await _notes.PrepareOriginalLibraryReadAsync(_documentId, token).ConfigureAwait(false);
        if (!_notes.IsIssuedOriginalLibraryReadRequest(request) || request.DocumentId != _documentId ||
            request.Kind != NotesOriginalReadKind.LibrarySnapshot)
            throw new InvalidDataException("The original Notes owner issued a different Write read request.");
        var review = await _home.RequestWriteDocumentReadWithinOriginalSourceAsync(request, expected,
            _sessionId, _originalScope, _retainOriginalTask, token).ConfigureAwait(false);
        var authorization = await _home.GetLibrarySnapshotAuthorizationWithinOriginalSourceAsync(review,
            _originalScope, _retainOriginalTask, token).ConfigureAwait(false);
        if (authorization.State == HomePermissionRequestState.PendingApproval)
            await _presentRead(review, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        authorization = await _home.GetLibrarySnapshotAuthorizationWithinOriginalSourceAsync(review,
            _originalScope, _retainOriginalTask, token).ConfigureAwait(false);
        if (!authorization.IsAllowed || authorization.State != HomePermissionRequestState.Approved)
            throw new UnauthorizedAccessException(authorization.Message.Length == 0
                ? "Home did not approve this Write document read." : authorization.Message);
        var admission = await _home.BeginLibrarySnapshotWithinOriginalSourceAsync(review,
            _originalScope, _retainOriginalTask, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original Write read still needs its genuine Home decision.");
        var snapshot = await _notes.ReadOriginalLibraryWithHomeAsync(request, admission, _verified, token).ConfigureAwait(false);
        if (snapshot.AuditWarning is not null)
            throw new IOException("The Write read completed but its original Home audit needs recovery.");
        var document = snapshot.SelectedDocument;
        if (document is null || document.Id != _documentId || document.Version != expected || IsBoardsDocument(document))
            throw new InvalidOperationException("The selected Write artifact is no longer at its pinned owner revision.");
        _cachedRead = Detach(document);
        return Detach(document);
    }

    public Task DeleteAsync(Guid documentId, CancellationToken cancellationToken) =>
        Task.FromException(DenyOtherDocuments());
    public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid documentId, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<NotesVersionInfo>>(DenyOtherDocuments());
    public Task<NotesDocument?> LoadVersionAsync(Guid documentId, string versionId, CancellationToken cancellationToken) =>
        Task.FromException<NotesDocument?>(DenyOtherDocuments());
    public Task<NotesDocument?> RecoverLatestAsync(Guid documentId, CancellationToken cancellationToken) =>
        Task.FromException<NotesDocument?>(DenyOtherDocuments());
    public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<NotesSearchHit>>(DenyOtherDocuments());

    private Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> body, CancellationToken caller)
    {
        if (_retiring) throw new ObjectDisposedException(nameof(NotesOriginalWriteWorkspaceRepository));
        var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime, caller);
        return Run();

        async Task<T> Run()
        {
            using (linked)
            {
                await _operations.WaitAsync(linked.Token).ConfigureAwait(false);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (_retiring) throw new ObjectDisposedException(nameof(NotesOriginalWriteWorkspaceRepository));
                    return await body(linked.Token).ConfigureAwait(false);
                }
                finally { _operations.Release(); }
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_close is not null) return new(_close);
        _retiring = true;
        return new(_close = CloseAsync());
    }

    private async Task CloseAsync()
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try { }
        finally { _operations.Release(); _operations.Dispose(); }
    }

    private static NotesDocumentSummary NotesDocumentSummaryFor(NotesDocument document)
    {
        var blocks = document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks).ToArray();
        var words = blocks.Sum(block => string.IsNullOrWhiteSpace(block.PlainText)
            ? 0 : block.PlainText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        return new(document.Id, document.Title, document.UpdatedAt, document.Version,
            document.Sections.Count, blocks.Length, words, document.Recovery.HasUnsavedRecovery);
    }

    private static bool IsBoardsDocument(NotesDocument document) => BoardsWorkspaceService.IsBoardsNotebook(document);
    private static NotesDocument Detach(NotesDocument document) =>
        JsonSerializer.Deserialize<NotesDocument>(JsonSerializer.SerializeToUtf8Bytes(document, Json), Json)
        ?? throw new InvalidDataException("The Write document could not be detached from its original owner.");
    private void DemandSelected(Guid documentId)
    {
        if (documentId == Guid.Empty || documentId != _documentId)
            throw new UnauthorizedAccessException("This embedded Write owner is pinned to one canonical document.");
    }
    private static UnauthorizedAccessException DenyOtherDocuments() =>
        new("This embedded Write owner does not grant history, recovery, deletion or search over other documents.");
}
