using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class NotesRepository
{
    private sealed record OriginalSaveOutcome(NotesRepository Issuer, INotesOriginalSaveIntent Intent,
        NotesOriginalSaveDisposition Disposition, long? CommittedVersion, string? CommittedSha256,
        string DiagnosticCode) : INotesOriginalSaveOutcome;

    public bool IsIssuedOriginalSaveOutcome(INotesOriginalSaveOutcome outcome, INotesOriginalSaveIntent intent)
        => outcome is OriginalSaveOutcome actual && ReferenceEquals(actual.Issuer, this) &&
            ReferenceEquals(actual.Intent, intent) && intent is OriginalSaveIntent original &&
            ReferenceEquals(original.Issuer, this) && ReferenceEquals(original.Outcome, actual);

    /// <summary>Actual retained outcome only, not inferred from an exception or token flag.
    /// Observation grants no mutation, read, retry or Home approval.</summary>
    public INotesOriginalSaveOutcome? GetObservedOriginalSaveOutcome(INotesOriginalSaveIntent intent)
        => intent is OriginalSaveIntent original && ReferenceEquals(original.Issuer, this) ? original.Outcome : null;

    /// <summary>Executes exactly one private detached candidate through the original verified Notes
    /// save transaction. Home consent and ownership never replace Notes CAS/integrity/custody.
    /// This primitive exposes no raw repository or broad native Ready capability.</summary>
    public async Task<NotesSaveResult> SaveOriginalWithHomeAsync(INotesOriginalSaveIntent originalIntent,
        HomeNotesSaveAdmission originalHome, VerifiedNotesRepository originalVerified,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalHome); ArgumentNullException.ThrowIfNull(originalVerified);
        if (originalIntent is not OriginalSaveIntent intent || !ReferenceEquals(intent.Issuer, this) ||
            !originalHome.IsBoundToOriginalNotes(this, intent) || !originalVerified.IsBoundToOriginal(this))
            throw new UnauthorizedAccessException("Retain the same original Notes candidate, Home admission and verified owner.");
        if (Interlocked.CompareExchange(ref intent.ExecutionStarted, 1, 0) != 0)
            throw new InvalidOperationException("This original Notes save was already attempted; retry its audit only.");
        NotesSaveResult? receipt = null;
        Exception? primary = null;
        Task? originalStage = null;
        OriginalHomeTransaction? originalTransaction = null;
        HomeClaimedResourceCommitFence? fence = null;
        try
        {
            // Capture reads actual Home ownership BEFORE the Notes transaction. Validation,
            // however, occurs only after acquiring the canonical Notes physical lock below.
            var capture = originalHome.CaptureCommitFenceAsync(this, intent, cancellationToken).AsTask();
            originalStage = capture;
            fence = await capture.ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The original Notes Home claim is no longer current.");
            var transactionTask = WithTransactionCoreAsync(async transaction =>
            {
                var root = transaction.OriginalRoot ?? throw new InvalidOperationException("Original Notes root custody is absent.");
                if (!await fence.ValidateAsync(cancellationToken).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The original Home ownership, actor or save approval changed.");
                try
                {
                    // Even directory creation and the prepared journal remain under the actual
                    // Home fence. Retained descriptors prevent path replacement redirecting IO.
                    originalTransaction = new(root.OpenDocumentCustody(intent.DocumentId), fence, intent);
                    transaction.OriginalHome = originalTransaction;
                    var saved = await originalVerified.SaveWithinOriginalTransactionAsync(transaction,
                        intent.Candidate, intent.Reason, intent.DocumentId, intent.ExpectedVersion, cancellationToken).ConfigureAwait(false);
                    originalTransaction.Custody.DemandCurrent();
                    originalTransaction.Custody.FlushNames();
                    return saved;
                }
                finally
                {
                    // No diagnostics or Home completion audit while this Home lease is held.
                    await fence.DisposeAsync().ConfigureAwait(false);
                }
            }, cancellationToken, (OriginalObservation)intent.StoreObservation);
            originalStage = transactionTask;
            receipt = await transactionTask.ConfigureAwait(false);
            receipt = receipt with { CurrentPath = Path.Combine(_root, intent.DocumentId.ToString("D"), "current.haven-notes.json"),
                VersionPath = Path.Combine(_root, intent.DocumentId.ToString("D"), "Versions", Path.GetFileName(receipt.VersionPath)) };
        }
        catch (Exception error) { primary = OriginalBoundaryFailure(originalStage, error); }
        finally
        {
            // This is idempotent, including failed final admission or owner construction.
            if (fence is not null)
            {
                try { await fence.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { primary = primary is null ? error : new AggregateException(primary, error); }
            }
            try { originalTransaction?.Dispose(); }
            catch (Exception error) { primary = primary is null ? error : new AggregateException(primary, error); }
        }
        // The actual owner result, never a caller DTO, is retained before Home audit. A durable
        // commit stays committed if its audit or post-commit cleanup reports a failure.
        var disposition = receipt is not null ? NotesOriginalSaveDisposition.Committed :
            originalTransaction?.PublicationAttempted == true ? NotesOriginalSaveDisposition.Uncertain : NotesOriginalSaveDisposition.Refused;
        intent.Outcome = new(this, intent, disposition, receipt?.Version, receipt?.Sha256,
            disposition == NotesOriginalSaveDisposition.Committed ? "NOTES_ORIGINAL_COMMITTED" :
                disposition == NotesOriginalSaveDisposition.Uncertain ? "NOTES_ORIGINAL_OUTCOME_UNCERTAIN" : "NOTES_ORIGINAL_REFUSED");
        Exception? auditError = null;
        try
        {
            var audit = await originalHome.CompleteObservedAsync(this, intent.Outcome, CancellationToken.None).ConfigureAwait(false);
            if (!audit.Succeeded) auditError = new IOException("The actual Notes result needs Home audit recovery: " + audit.Code);
        }
        catch (Exception error) { auditError = error; }
        if (receipt is not null)
        {
            if (primary is not null || auditError is not null)
                receipt = receipt with { PostCommitWarning = JoinWarning(receipt.PostCommitWarning,
                    "The committed Notes save has retained owner/audit recovery; do not repeat the mutation.") };
            return receipt;
        }
        if (primary is not null && auditError is not null) throw new AggregateException("Original Notes save and its audit did not complete.", primary, auditError);
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        throw auditError ?? new IOException("The original Notes save produced no acknowledged commit receipt.");
    }

    /// <summary>Retries only the original retained terminal audit, never Notes IO or publication.</summary>
    public Task<HomePermissionOperationResult> RetryOriginalSaveAuditAsync(INotesOriginalSaveIntent originalIntent,
        HomeNotesSaveAdmission originalHome, CancellationToken cancellationToken = default)
    {
        if (originalIntent is not OriginalSaveIntent intent || !ReferenceEquals(intent.Issuer, this) ||
            intent.Outcome is not { } outcome)
            throw new UnauthorizedAccessException("No original Notes outcome is retained for this admission.");
        return originalHome.CompleteObservedAsync(this, outcome, cancellationToken);
    }

    internal sealed class OriginalHomeTransaction(OriginalDocumentCustody custody,
        HomeClaimedResourceCommitFence fence, INotesOriginalSaveIntent intent) : IDisposable
    {
        internal OriginalDocumentCustody Custody { get; } = custody;
        internal bool PublicationAttempted { get; set; }
        internal async ValueTask DemandPublicationAsync(Guid documentId, CancellationToken token)
        {
            if (documentId != intent.DocumentId) throw new UnauthorizedAccessException("The original Notes document changed.");
            custody.DemandCurrent();
            if (!await fence.ValidateAsync(token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The held original Home Notes fence is no longer current.");
            custody.DemandCurrent();
        }
        public void Dispose() => custody.Dispose();
    }

    private sealed record OriginalLibraryReadOutcome(NotesRepository Issuer, INotesOriginalLibraryReadRequest Request,
        bool Succeeded, int DocumentCount, bool DocumentWasRead, string? SnapshotSha256,
        string DiagnosticCode) : INotesOriginalLibraryReadOutcome;

    public bool IsIssuedOriginalLibraryReadOutcome(INotesOriginalLibraryReadOutcome outcome,
        INotesOriginalLibraryReadRequest request) => outcome is OriginalLibraryReadOutcome actual &&
        ReferenceEquals(actual.Issuer, this) && ReferenceEquals(actual.Request, request) &&
        request is OriginalLibraryReadRequest original && ReferenceEquals(original.Issuer, this) &&
        ReferenceEquals(original.Outcome, actual);

    /// <summary>Actual Home-authorised read-only snapshot under the original Notes lock. It does
    /// not invoke recovery-writing public Load/List and retains only summaries + requested content.</summary>
    public async Task<NotesOriginalLibrarySnapshot> ReadOriginalLibraryWithHomeAsync(
        INotesOriginalLibraryReadRequest originalRequest, HomeNotesLibraryReadAdmission originalHome,
        VerifiedNotesRepository originalVerified, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalHome); ArgumentNullException.ThrowIfNull(originalVerified);
        if (originalRequest is not OriginalLibraryReadRequest request || !ReferenceEquals(request.Issuer, this) ||
            !originalHome.IsBoundToOriginalNotes(this, request) || !originalVerified.IsBoundToOriginal(this))
            throw new UnauthorizedAccessException("Retain the same original Notes library request and Home/verified owner.");
        if (Interlocked.CompareExchange(ref request.ExecutionStarted, 1, 0) != 0)
            throw new InvalidOperationException("This original Notes read was already attempted; retry only its audit.");
        HomeClaimedResourceCommitFence? fence = null;
        NotesOriginalLibrarySnapshot? snapshot = null;
        Exception? primary = null;
        Task? originalStage = null;
        try
        {
            var capture = originalHome.CaptureCommitFenceAsync(this, request, cancellationToken).AsTask();
            originalStage = capture;
            fence = await capture.ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The original Notes read claim is no longer current.");
            var transactionTask = WithTransactionCoreAsync(async transaction =>
            {
                var root = transaction.OriginalRoot ?? throw new InvalidOperationException("Original Notes read root custody is absent.");
                if (!await fence.ValidateAsync(cancellationToken).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The original Home read actor, ownership or review changed.");
                try
                {
                    using var library = root.OpenLibraryCustody();
                    var libraryVersion = library.SnapshotVersion;
                    var summaries = new List<NotesDocumentSummary>();
                    var metadata = new List<NotesOriginalDocumentMetadata>();
                    var witnesses = new List<OriginalDocumentReadWitness>();
                    NotesDocument? selected = null;
                    foreach (var id in library.EnumerateDocumentIds())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        library.DemandCurrent();
                        OriginalDocumentCustody document;
                        try { document = library.OpenDocument(id); }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                        { throw new NotesOriginalReadRecoveryRequiredException(id,
                            "A canonical Notes library entry is not its retained owned document directory; it was preserved.", error); }
                        using var retainedDocument = document;
                        transaction.OriginalRead = document;
                        try
                        {
                            var before = document.CaptureReadWitness(id);
                            var current = await originalVerified.ReadWithinOriginalTransactionAsync(transaction, id,
                                cancellationToken).ConfigureAwait(false);
                            if (document.CaptureReadWitness(id) != before)
                                throw new IOException("The original Notes document changed during its authorised snapshot.");
                            witnesses.Add(before);
                            if (current is not null)
                            {
                                var summary = Summarize(current);
                                summaries.Add(summary);
                                current.Metadata.TryGetValue(BoardsWorkspaceService.ProductKey, out var product);
                                metadata.Add(new(summary, product, BoardsWorkspaceService.IsDeletedNotebook(current)));
                                if (id == request.DocumentId) selected = current;
                            }
                        }
                        finally { transaction.OriginalRead = null; }
                    }
                    library.DemandCurrent();
                    if (library.SnapshotVersion != libraryVersion)
                        throw new IOException("The original Notes library changed during its authorised snapshot.");
                    if (!await fence.ValidateAsync(cancellationToken).ConfigureAwait(false))
                        throw new UnauthorizedAccessException("The held original Home read fence changed.");
                    request.ObservedMetadata = Array.AsReadOnly(metadata.OrderByDescending(value => value.Summary.UpdatedAt).ToArray());
                    request.ReadWitness = new(libraryVersion, Array.AsReadOnly(witnesses.ToArray()));
                    request.ReadAdmission = originalHome;
                    return new NotesOriginalLibrarySnapshot(request.StoreObservation.StoreIdentity,
                        Array.AsReadOnly(summaries.OrderByDescending(value => value.UpdatedAt).ToArray()), selected)
                        { Metadata = request.ObservedMetadata };
                }
                finally { await fence.DisposeAsync().ConfigureAwait(false); }
            }, cancellationToken, (OriginalObservation)request.StoreObservation);
            originalStage = transactionTask;
            snapshot = await transactionTask.ConfigureAwait(false);
        }
        catch (Exception error) { primary = OriginalBoundaryFailure(originalStage, error); }
        finally
        {
            if (fence is not null)
                try { await fence.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { primary = primary is null ? error : new AggregateException(primary, error); }
        }
        request.Outcome = new(this, request, snapshot is not null, snapshot?.Documents.Count ?? 0,
            snapshot?.SelectedDocument is not null, snapshot is null ? null :
                Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions))),
            snapshot is null ? "NOTES_ORIGINAL_READ_REFUSED" : "NOTES_ORIGINAL_READ_OBSERVED");
        Exception? auditError = null;
        try
        {
            var audit = await originalHome.CompleteObservedAsync(this, request.Outcome, CancellationToken.None).ConfigureAwait(false);
            if (!audit.Succeeded) auditError = new IOException("The original Notes read needs Home audit recovery: " + audit.Code);
        }
        catch (Exception error) { auditError = error; }
        if (snapshot is not null)
        {
            request.ReturnedSnapshot = primary is not null || auditError is not null
                ? snapshot with { AuditWarning = "The original read completed; its retained Home audit needs recovery." } : snapshot;
            return request.ReturnedSnapshot;
        }
        if (primary is not null && auditError is not null)
            throw new AggregateException("Original Notes read and its audit did not complete.", primary, auditError);
        if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        throw auditError ?? new IOException("The original Notes read produced no authorised snapshot.");
    }

    /// <summary>Already-read data selection for the SAME Boards engine. Exact owner-issued
    /// snapshot identity is required; public snapshot metadata never grants IO or approval.
    /// Changed data needs a fresh genuine read review rather than an implicit cache refresh.</summary>
    public IBoardsNotebookSummarySource CreateOriginalBoardsSummarySource(INotesOriginalLibraryReadRequest originalRequest,
        NotesOriginalLibrarySnapshot originalSnapshot, HomeNotesLibraryReadAdmission originalHome)
    {
        ArgumentNullException.ThrowIfNull(originalHome); ArgumentNullException.ThrowIfNull(originalSnapshot);
        if (originalRequest is not OriginalLibraryReadRequest request || !ReferenceEquals(request.Issuer, this) ||
            !ReferenceEquals(request.ReturnedSnapshot, originalSnapshot) || !ReferenceEquals(request.ReadAdmission, originalHome) ||
            request.Outcome is not { Succeeded: true } || request.ReadWitness is null || request.ObservedMetadata is null ||
            originalSnapshot.AuditWarning is not null)
            throw new UnauthorizedAccessException("Retain the exact successful original Notes snapshot and Home read owner.");
        return new OriginalBoardsSummarySource(this, request, originalHome);
    }

    private sealed class OriginalBoardsSummarySource(NotesRepository owner, OriginalLibraryReadRequest request,
        HomeNotesLibraryReadAdmission home) : IBoardsNotebookSummarySource
    {
        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(bool deleted, CancellationToken cancellationToken)
            => owner.WithTransactionCoreAsync(async transaction =>
            {
                var witness = request.ReadWitness ?? throw new UnauthorizedAccessException("The original read witness is absent.");
                var root = transaction.OriginalRoot ?? throw new UnauthorizedAccessException("Original Notes custody is absent.");
                using var library = root.OpenLibraryCustody();
                if (library.SnapshotVersion != witness.Library ||
                    !library.EnumerateDocumentIds().SequenceEqual(witness.Documents.Select(value => value.Id)))
                    throw new IOException("The original Notes library changed; request a new reviewed snapshot.");
                foreach (var expected in witness.Documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var current = library.OpenDocument(expected.Id);
                    if (current.CaptureReadWitness(expected.Id) != expected ||
                        current.HasNamedEntry("current.publication.json") ||
                        current.HasNamedEntry("current.integrity.pending.json") || current.HasNamedEntry("current.recovery.json"))
                        throw new IOException("The original Notes document changed; request a new reviewed snapshot.");
                }
                // Notes lock first, then the actual Home actor/ownership/completed-review guard.
                // No content IO or nested Home/Notes evidence call occurs while this lease is held.
                await using var presentation = await home.CaptureObservedSnapshotLeaseAsync(owner, request,
                    request.Outcome!, cancellationToken).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The original Home snapshot context is no longer current.");
                library.DemandCurrent();
                if (library.SnapshotVersion != witness.Library || !await presentation.IsCurrentAsync(cancellationToken).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The original snapshot changed before presentation.");
                foreach (var expected in witness.Documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var current = library.OpenDocument(expected.Id);
                    if (current.CaptureReadWitness(expected.Id) != expected ||
                        current.HasNamedEntry("current.publication.json") || current.HasNamedEntry("current.integrity.pending.json") ||
                        current.HasNamedEntry("current.recovery.json"))
                        throw new IOException("The original snapshot became stale before presentation.");
                }
                return BoardsWorkspaceService.SelectNotebookSummaries(request.ObservedMetadata!, deleted);
            }, cancellationToken, (OriginalObservation)request.StoreObservation);
    }

    internal sealed record OriginalDocumentReadWitness(Guid Id, PhysicalVersion Directory,
        PhysicalVersion? Current, PhysicalVersion? Integrity, PhysicalVersion? Versions);
    private sealed record OriginalLibraryReadWitness(PhysicalVersion? Library,
        IReadOnlyList<OriginalDocumentReadWitness> Documents);

    public Task<HomePermissionOperationResult> RetryOriginalLibraryReadAuditAsync(INotesOriginalLibraryReadRequest originalRequest,
        HomeNotesLibraryReadAdmission originalHome, CancellationToken cancellationToken = default)
    {
        if (originalRequest is not OriginalLibraryReadRequest request || !ReferenceEquals(request.Issuer, this) ||
            request.Outcome is not { } outcome)
            throw new UnauthorizedAccessException("No original Notes library read outcome is retained.");
        return originalHome.CompleteObservedAsync(this, outcome, cancellationToken);
    }

    internal async Task DemandReadRecoveryClearAsync(Guid id, CancellationToken cancellationToken)
    {
        var custody = _currentTransaction?.OriginalRead
            ?? throw new UnauthorizedAccessException("Original read custody is required.");
        try
        {
            if (await ReadPublicationIntentAsync(id, cancellationToken).ConfigureAwait(false) is not null ||
                custody.HasNamedEntry("current.integrity.pending.json") ||
                custody.HasNamedEntry("current.recovery.json"))
                throw new NotesOriginalReadRecoveryRequiredException(id,
                    "The original Notes document has pending recovery/publication state; a read cannot repair it.");
        }
        catch (Exception error) when (error is not NotesOriginalReadRecoveryRequiredException &&
            error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { throw new NotesOriginalReadRecoveryRequiredException(id, "The original Notes recovery state could not be verified; it was preserved.", error); }
    }

    private static Exception OriginalBoundaryFailure(Task? original, Exception observed)
    {
        var errors = new List<Exception>();
        AddOriginalTaskFailures(errors, original, observed);
        return errors.Count == 1 ? errors[0] : new AggregateException("The original Notes boundary task faulted.", errors);
    }

    internal string DocumentDirectoryWithinOriginalTransaction(Guid id, string verifiedRoot)
    {
        if (!string.Equals(Path.GetFullPath(verifiedRoot), Path.GetFullPath(_root), StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The integrity owner belongs to another Notes root.");
        return DocumentDirectory(id);
    }

    internal PhysicalVersion? CaptureOriginalReadVersion(FileStream stream)
        => _currentTransaction?.PhysicalCustody is not null ? OriginalNotesRoot.Stat(stream.SafeFileHandle) : null;
    internal void DemandOriginalReadCurrent(string path, FileStream stream, PhysicalVersion? before)
    {
        if (before is null) return;
        var custody = _currentTransaction?.PhysicalCustody ?? throw new IOException("Original Notes read custody ended.");
        if (OriginalNotesRoot.Stat(stream.SafeFileHandle) != before)
            throw new IOException("The original Notes file changed while read.");
        using var named = custody.OpenFile(path, FileMode.Open, FileAccess.Read, 4096);
        if (OriginalNotesRoot.Stat(named.SafeFileHandle) != before)
            throw new IOException("The original Notes file was replaced while read.");
        custody.DemandCurrent();
    }

    internal FileStream OpenOriginalTransactionFile(string path, FileMode mode, FileAccess access,
        FileShare share = FileShare.Read, int bufferSize = 64 * 1024,
        FileOptions options = FileOptions.Asynchronous | FileOptions.SequentialScan)
    {
        if (_currentTransaction?.OriginalRead is not null && (mode != FileMode.Open || access != FileAccess.Read))
            throw new UnauthorizedAccessException("An original Notes read transaction cannot write files.");
        return _currentTransaction?.PhysicalCustody is { } original
            ? original.OpenFile(path, mode, access, bufferSize)
            : new FileStream(path, mode, access, share, bufferSize, options);
    }

    internal sealed class OriginalLibraryDirectory : IDisposable
    {
        private readonly OriginalNotesRoot _root;
        private readonly SafeFileHandle _notes;
        private readonly SafeFileHandle? _documents;
        private readonly PhysicalVersion? _identity;
        private readonly int _directoryFlag, _noFollow;
        internal PhysicalVersion? SnapshotVersion { get { DemandCurrent(); return _documents is null ? null : OriginalNotesRoot.Stat(_documents); } }
        internal OriginalLibraryDirectory(OriginalNotesRoot root, SafeFileHandle notes, int directoryFlag, int noFollow)
        {
            _root = root; _notes = notes; _directoryFlag = directoryFlag; _noFollow = noFollow;
            try { _documents = OriginalNotesRoot.OpenRelative(notes, "Documents", directoryFlag | noFollow | 524288); }
            catch (IOException error) when (error.InnerException is Win32Exception { NativeErrorCode: 2 }) { return; }
            try { _identity = OriginalNotesRoot.Stat(_documents); OriginalNotesRoot.DemandDirectory(_identity); DemandCurrent(); }
            catch { _documents.Dispose(); throw; }
        }
        internal IReadOnlyList<Guid> EnumerateDocumentIds()
        {
            DemandCurrent();
            return _documents is null ? [] : Directory.EnumerateFileSystemEntries(OriginalNotesRoot.Alias(_documents))
                .Select(Path.GetFileName).Where(leaf => Guid.TryParseExact(leaf, "D", out var id) &&
                    id != Guid.Empty && leaf == id.ToString("D"))
                .Select(leaf => Guid.ParseExact(leaf!, "D")).Order().ToArray();
        }
        internal OriginalDocumentCustody OpenDocument(Guid id)
        {
            DemandCurrent();
            if (_identity is null) throw new FileNotFoundException("The original Notes library is empty.");
            return new(_root, _notes, id, _directoryFlag, _noFollow, createDirectories: false, expectedDocuments: _identity);
        }
        internal void DemandCurrent()
        {
            _root.DemandCurrent();
            SafeFileHandle current;
            try { current = OriginalNotesRoot.OpenRelative(_notes, "Documents", _directoryFlag | _noFollow | 524288); }
            catch (IOException error) when (_identity is null && error.InnerException is Win32Exception { NativeErrorCode: 2 }) { return; }
            using (current)
                if (_identity is null || !SameNode(OriginalNotesRoot.Stat(current), _identity))
                    throw new IOException("The original Notes library directory changed while read.");
        }
        public void Dispose() => _documents?.Dispose();
    }

    internal sealed class OriginalDocumentCustody : IDisposable
    {
        private readonly OriginalNotesRoot _root;
        private readonly SafeFileHandle _notes;
        private readonly SafeFileHandle _documents, _document;
        private readonly SafeFileHandle? _versions;
        private readonly PhysicalVersion _documentsIdentity, _documentIdentity;
        private readonly PhysicalVersion? _versionsIdentity;
        private readonly string _id;
        private readonly int _directoryFlag, _noFollow;
        internal string DocumentPath => OriginalNotesRoot.Alias(_document);
        internal OriginalDocumentReadWitness CaptureReadWitness(Guid id)
        {
            if (id.ToString("D") != _id) throw new UnauthorizedAccessException("Foreign Notes read witness.");
            DemandCurrent();
            return new(id, OriginalNotesRoot.Stat(_document), ReadVersion("current.haven-notes.json"),
                ReadVersion("current.integrity.json"), _versions is null ? null : OriginalNotesRoot.Stat(_versions));

            PhysicalVersion? ReadVersion(string leaf)
            {
                try { using var file = OpenFile(Path.Combine(DocumentPath, leaf), FileMode.Open, FileAccess.Read, 4096);
                    return OriginalNotesRoot.Stat(file.SafeFileHandle); }
                catch (FileNotFoundException) { return null; }
            }
        }
        // Metadata presence is a no-follow native observation of the retained parent, not
        // File.Exists (which follows links and silently hides dangling pending artifacts).
        internal bool HasNamedEntry(string leaf)
        {
            if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or ".." || leaf.IndexOfAny(['/', (char)92]) >= 0)
                throw new ArgumentException("One canonical Notes leaf is required.", nameof(leaf));
            DemandCurrent();
            var metadata = new byte[256];
            if (NamedStat(OriginalNotesRoot.Fd(_document), leaf, 0x100, 1, metadata) == 0) return true;
            if (Marshal.GetLastPInvokeError() == 2) return false;
            throw Error("Observe original Notes pending metadata without following links");
        }
        internal bool HasVersionArtifacts => _versions is not null &&
            Directory.EnumerateFileSystemEntries(OriginalNotesRoot.Alias(_versions)).Any();
        internal string VersionsPath => _versions is null ? throw new IOException("The original Notes versions directory is absent.") : OriginalNotesRoot.Alias(_versions);
        internal OriginalDocumentCustody(OriginalNotesRoot root, SafeFileHandle notes, Guid id, int directoryFlag, int noFollow,
            bool createDirectories = true, PhysicalVersion? expectedDocuments = null)
        {
            _root = root; _notes = notes; _id = id.ToString("D"); _directoryFlag = directoryFlag; _noFollow = noFollow;
            _documents = OpenDirectory(notes, "Documents", createDirectories);
            try
            {
                if (expectedDocuments is not null && !SameNode(OriginalNotesRoot.Stat(_documents), expectedDocuments))
                    throw new IOException("The original Notes library directory was replaced.");
                _document = OpenDirectory(_documents, _id, createDirectories);
                try { _versions = OpenDirectory(_document, "Versions", createDirectories); }
                catch (IOException error) when (!createDirectories && error.InnerException is Win32Exception { NativeErrorCode: 2 }) { _versions = null; }
                catch { _document.Dispose(); throw; }
            }
            catch { _documents.Dispose(); throw; }
            try
            {
                _documentsIdentity = OriginalNotesRoot.Stat(_documents);
                _documentIdentity = OriginalNotesRoot.Stat(_document);
                _versionsIdentity = _versions is null ? null : OriginalNotesRoot.Stat(_versions);
                DemandCurrent();
            }
            catch { Dispose(); throw; }
        }
        internal string DirectoryFor(Guid id)
            => id.ToString("D") == _id ? DocumentPath : throw new UnauthorizedAccessException("Foreign Notes document path.");
        internal string VersionsFor(Guid id)
            => id.ToString("D") == _id ? VersionsPath : throw new UnauthorizedAccessException("Foreign Notes version path.");
        internal void DemandCurrent()
        {
            _root.DemandCurrent();
            using var documents = OpenDirectory(_notes, "Documents", create: false);
            using var document = OpenDirectory(documents, _id, create: false);
            using var versions = _versionsIdentity is null ? null : OpenDirectory(document, "Versions", create: false);
            if (!SameNode(OriginalNotesRoot.Stat(documents), _documentsIdentity) ||
                !SameNode(OriginalNotesRoot.Stat(document), _documentIdentity) ||
                (versions is not null && !SameNode(OriginalNotesRoot.Stat(versions), _versionsIdentity!)))
                throw new IOException("The original Notes document directory custody changed.");
        }
        private SafeFileHandle OpenDirectory(SafeFileHandle parent, string leaf, bool create)
        {
            if (create && MakeDirectory(OriginalNotesRoot.Fd(parent), leaf, 448) != 0 && Marshal.GetLastPInvokeError() != 17)
                throw Error("Create original Notes child directory");
            var result = OriginalNotesRoot.OpenRelative(parent, leaf, _directoryFlag | _noFollow | 524288);
            try { OriginalNotesRoot.DemandDirectory(OriginalNotesRoot.Stat(result)); return result; }
            catch { result.Dispose(); throw; }
        }
        internal FileStream OpenFile(string path, FileMode mode, FileAccess access, int bufferSize)
        {
            DemandCurrent();
            var parentPath = Path.GetDirectoryName(path);
            var parent = parentPath == DocumentPath ? _document : _versions is not null && parentPath == VersionsPath ? _versions :
                throw new UnauthorizedAccessException("Notes IO must remain inside its exact retained directory.");
            var leaf = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(leaf) || leaf is "." or "..") throw new UnauthorizedAccessException();
            if (mode is not (FileMode.Open or FileMode.Create or FileMode.CreateNew)) throw new InvalidOperationException("Unsupported protected Notes file mode.");
            var flags = (access == FileAccess.Read ? 0 : access == FileAccess.Write ? 1 : 2) | _noFollow | 524288 | 2048;
            if (mode == FileMode.CreateNew) flags |= 64 | 128;
            // Never O_TRUNC before stat: a hard-link substitution must be rejected before bytes change.
            SafeFileHandle handle;
            try { handle = OriginalNotesRoot.OpenRelative(parent, leaf, flags, 384); }
            catch (IOException error) when (error.InnerException is Win32Exception { NativeErrorCode: 2 })
            { throw new FileNotFoundException("The protected original Notes leaf is absent.", path, error); }
            try
            {
                var identity = OriginalNotesRoot.Stat(handle);
                if ((identity.Mode & 0xf000) != 0x8000 || identity.User != UserId() || identity.Links != 1 || (identity.Mode & 18) != 0)
                    throw new UnauthorizedAccessException("Protected Notes IO requires an owned regular file with no foreign write or hard links.");
                var stream = new FileStream(handle, access, bufferSize, isAsync: false);
                if (mode == FileMode.Create) stream.SetLength(0);
                return stream;
            }
            catch { handle.Dispose(); throw; }
        }
        internal void FlushNames()
        {
            foreach (var handle in new[] { _versions, _document, _documents }.OfType<SafeFileHandle>())
                if (SyncDirectory(OriginalNotesRoot.Fd(handle)) != 0) throw Error("Flush original Notes publication directory");
            _root.Flush();
        }
        public void Dispose() { _versions?.Dispose(); _document.Dispose(); _documents.Dispose(); }
        private static IOException Error(string operation) => new(operation + " failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
        [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true, CharSet = CharSet.Ansi)] private static extern int MakeDirectory(int fd, string leaf, uint mode);
        [DllImport("libc", EntryPoint = "statx", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern int NamedStat(int fd, string leaf, int flags, uint mask, [Out] byte[] metadata);
        [DllImport("libc", EntryPoint = "fsync", SetLastError = true)] private static extern int SyncDirectory(int fd);
        [DllImport("libc", EntryPoint = "geteuid")] private static extern uint UserId();
    }
}
