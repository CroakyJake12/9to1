using CakeOS.Cui.Themes;
using CakeOS.Apps.Boards.App;
using Haven.Browser;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Borrow the actual Home/Notes owners. Allocation creates no ownership, setup,
/// import, permission, model or Ready observation. Embedded and standalone entry points use
/// this same factory and shared Boards core/CUI; global services outlive each tab.</summary>
internal sealed class BoardsBrowseAppContentFactory : IBrowseAppContentFactory
{
    private readonly Func<BrowseAppContentRequest, Guid, CancellationToken, Task>? _openLocalBoard;
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly CancellationToken _appLifetime, _hostLifetime;
    private readonly Func<bool> _reduceMotion;
    private readonly CuiThemePreferenceContext? _theme;
    private readonly Func<Guid, Guid, Guid, Guid, Guid, NativeHomeFilesBoardReadLease>? _takePreparedBoardRead;
    private readonly Func<Guid, Guid, Guid, Guid, Guid, NativeHomeFilesBoardWriteLease>? _takePreparedBoardWrite;
    private readonly Func<BrowseAppContentRequest, BoardsBrowseAppContentAddress, CancellationToken, Task<NativeHomeFilesBoardReadLease>>? _restoreBoardRead;
    private readonly Func<BrowseAppContentRequest, BoardsBrowseAppContentAddress, CancellationToken, Task<NativeHomeFilesBoardWriteLease>>? _restoreBoardWrite;
    private readonly Func<BrowseAppContentRequest, BoardsHomeBoardCreationRequest, CancellationToken, Task<BoardsHomeBoardCreationResult>>? _createAndOpenBoard;
    private BoardsBrowseAppContentFactory(IServiceProvider provider, HomeNativeWindowsComposition home,
        CancellationToken appLifetime, CancellationToken hostLifetime, Func<bool> reduceMotion,
        CuiThemePreferenceContext? theme,
        Func<Guid, Guid, Guid, Guid, Guid, NativeHomeFilesBoardReadLease>? takePreparedBoardRead,
        Func<Guid, Guid, Guid, Guid, Guid, NativeHomeFilesBoardWriteLease>? takePreparedBoardWrite,
        Func<BrowseAppContentRequest, BoardsHomeBoardCreationRequest, CancellationToken, Task<BoardsHomeBoardCreationResult>>? createAndOpenBoard,
        Func<BrowseAppContentRequest, BoardsBrowseAppContentAddress, CancellationToken, Task<NativeHomeFilesBoardReadLease>>? restoreBoardRead,
        Func<BrowseAppContentRequest, BoardsBrowseAppContentAddress, CancellationToken, Task<NativeHomeFilesBoardWriteLease>>? restoreBoardWrite,
        Func<BrowseAppContentRequest, Guid, CancellationToken, Task>? openLocalBoard)
    { _provider = provider; _home = home; _appLifetime = appLifetime; _hostLifetime = hostLifetime;
        _reduceMotion = reduceMotion; _theme = theme; _takePreparedBoardRead = takePreparedBoardRead;
        _takePreparedBoardWrite = takePreparedBoardWrite; _createAndOpenBoard = createAndOpenBoard;
        _restoreBoardRead = restoreBoardRead; _restoreBoardWrite = restoreBoardWrite; _openLocalBoard = openLocalBoard; }

    internal static BoardsBrowseAppContentFactory BindOriginalSameProcess(IServiceProvider originalProvider,
        HomeNativeWindowsComposition sameHome, CancellationToken originalAppLifetime,
        CancellationToken originalHostLifetime, Func<bool> actualReduceMotion,
        CuiThemePreferenceContext? actualThemePreference = null,
        Func<Guid, Guid, Guid, Guid, Guid, NativeHomeFilesBoardReadLease>? takePreparedBoardRead = null,
        Func<Guid, Guid, Guid, Guid, Guid, NativeHomeFilesBoardWriteLease>? takePreparedBoardWrite = null,
        Func<BrowseAppContentRequest, BoardsHomeBoardCreationRequest, CancellationToken, Task<BoardsHomeBoardCreationResult>>? createAndOpenBoard = null,
        Func<BrowseAppContentRequest, BoardsBrowseAppContentAddress, CancellationToken, Task<NativeHomeFilesBoardReadLease>>? restoreBoardRead = null,
        Func<BrowseAppContentRequest, BoardsBrowseAppContentAddress, CancellationToken, Task<NativeHomeFilesBoardWriteLease>>? restoreBoardWrite = null,
        Func<BrowseAppContentRequest, Guid, CancellationToken, Task>? openLocalBoard = null)
    {
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(originalProvider, sameHome);
        ArgumentNullException.ThrowIfNull(actualReduceMotion);
        if (!originalAppLifetime.CanBeCanceled || !originalHostLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual application and host lifetimes.");
        originalAppLifetime.ThrowIfCancellationRequested(); originalHostLifetime.ThrowIfCancellationRequested();
        return new(originalProvider, sameHome, originalAppLifetime, originalHostLifetime,
            actualReduceMotion, actualThemePreference, takePreparedBoardRead, takePreparedBoardWrite, createAndOpenBoard,
            restoreBoardRead, restoreBoardWrite, openLocalBoard);
    }

    /// <summary>
    /// Creates the same typed, per-tab Board creation/recovery host used by rich Boards sessions
    /// for a Home-owned empty-library action. The callback remains the original Home/Files owner;
    /// this adapter adds no creation authority or alternate result state.
    /// </summary>
    internal static IBoardsHomeBoardCreationHost CreateOriginalHomeBoardCreationHost(
        Func<BrowseAppContentRequest, BoardsHomeBoardCreationRequest, CancellationToken,
            Task<BoardsHomeBoardCreationResult>> originalCreateAndOpen,
        BrowseAppContentRequest actualRequest,
        CancellationToken actualHostLifetime)
    {
        ArgumentNullException.ThrowIfNull(originalCreateAndOpen);
        ArgumentNullException.ThrowIfNull(actualRequest);
        if (actualRequest.TabId == Guid.Empty || string.IsNullOrWhiteSpace(actualRequest.ProfileId))
            throw new UnauthorizedAccessException("Retain the original tab and actual Home profile.");
        if (actualRequest.Privacy != BrowserTabPrivacy.Standard)
            throw new NotSupportedException("An owned device Boards library is not a private browser session.");
        if (!actualHostLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual Home tab lifetime.", nameof(actualHostLifetime));
        actualHostLifetime.ThrowIfCancellationRequested();
        return new HomeBoardCreationHost(originalCreateAndOpen, actualRequest, actualHostLifetime);
    }

    public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _appLifetime.ThrowIfCancellationRequested(); _hostLifetime.ThrowIfCancellationRequested();
        if (request.TabId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProfileId))
            throw new UnauthorizedAccessException("Retain the original tab and actual Home profile.");
        if (request.Privacy != BrowserTabPrivacy.Standard)
            throw new NotSupportedException("An owned device Boards library is not a private browser session.");
        var address = BoardsBrowseAppContentAddress.Parse(request.Address);
        if (address.IsLocalDocument || !address.IsFilesDocument && address.DocumentId is null)
        {
            var localLifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _hostLifetime);
            BoardsLocalDocuments? documents = null;
            NativeCanvasBoardEmbeddedArtifactHost? embedded = null;
            try
            {
                documents = new BoardsLocalDocuments(request.ProfileId, Path.Combine(_provider.GetRequiredService<IAppPaths>().DataDirectory, "Boards"));
                embedded = CreateEmbeddedArtifactHost(request, localLifetime.Token);
                return new BoardsWritableRichDocumentBrowseAppContentSession(request, address, documents,
                    (id, token) => _openLocalBoard is { } open ? open(request, id, token)
                        : Task.FromException(new InvalidOperationException("The Boards navigation owner is unavailable.")),
                    localLifetime, embedded);
            }
            catch
            {
                if (embedded is not null) embedded.DisposeAsync().AsTask().GetAwaiter().GetResult();
                documents?.Dispose(); localLifetime.Dispose(); throw;
            }
        }
        if (address.IsFilesDocument)
        {
            if (address.ReadToken is null)
            {
                if (address.Writable && _restoreBoardWrite is null || !address.Writable && _restoreBoardRead is null)
                    throw new UnauthorizedAccessException("Reopen this saved board from Home Library; its current Files authority is unavailable.");
                var restoredLifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _hostLifetime);
                NativeCanvasBoardEmbeddedArtifactHost? restoredEmbedded = null;
                try
                {
                    restoredEmbedded = CreateEmbeddedArtifactHost(request, restoredLifetime.Token);
                    if (!address.Writable)
                        return new BoardsRichDocumentBrowseAppContentSession(request, address,
                            token => _restoreBoardRead!(request, address, token), restoredLifetime, restoredEmbedded);
                    var creation = _createAndOpenBoard is null ? null :
                        CreateOriginalHomeBoardCreationHost(_createAndOpenBoard, request, restoredLifetime.Token);
                    var modelClient = _provider.GetService<IProviderModelClient>();
                    var modelSelection = _provider.GetService<HomeModelSelectionSource>();
                    IBoardsSectionIconGenerationHost? icons = modelClient is null || modelSelection is null ? null :
                        new BoardsSectionIconSuggestionService(modelClient, modelSelection, request.ProfileId,
                            _appLifetime, restoredLifetime.Token, () => !restoredLifetime.IsCancellationRequested);
                    return new BoardsWritableRichDocumentBrowseAppContentSession(request, address,
                        token => _restoreBoardWrite!(request, address, token), restoredLifetime, restoredEmbedded, creation, icons);
                }
                catch
                {
                    // Neither constructor above publishes this owner or starts its Open/Select/Create
                    // operations. Both embedded-owner collections are still empty, so this exact
                    // allocation-failure cleanup completes inline; it cannot join live UI work.
                    if (restoredEmbedded is not null) restoredEmbedded.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    restoredLifetime.Dispose();
                    throw;
                }
            }
            if (address.Writable)
            {
                if (_takePreparedBoardWrite is null || address.StoreId is not { } writeStore ||
                    address.ParentFolderId is not { } writeParent || address.FileId is not { } writeFile ||
                    address.RevisionId is not { } writeRevision || address.ReadToken is not { } writeToken)
                    throw new UnauthorizedAccessException("A writable rich Files board requires its exact Home-prepared write lease.");
                var writeLease = _takePreparedBoardWrite(writeToken, writeStore, writeParent, writeFile, writeRevision);
                if (!writeLease.IsOpen || !StringComparer.Ordinal.Equals(writeLease.ProfileId, request.ProfileId))
                {
                    writeLease.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    throw new UnauthorizedAccessException("The selected writable board belongs to another original Home profile.");
                }
                var writeLifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _hostLifetime);
                NativeCanvasBoardEmbeddedArtifactHost? embeddedOwner = null;
                try
                {
                    embeddedOwner = CreateEmbeddedArtifactHost(request, writeLifetime.Token);
                    var boardCreationHost = _createAndOpenBoard is null ? null :
                        CreateOriginalHomeBoardCreationHost(_createAndOpenBoard, request, writeLifetime.Token);
                    var modelClient = _provider.GetService<IProviderModelClient>();
                    var modelSelection = _provider.GetService<HomeModelSelectionSource>();
                    IBoardsSectionIconGenerationHost? iconGenerationHost =
                        modelClient is null || modelSelection is null ? null :
                        new BoardsSectionIconSuggestionService(modelClient, modelSelection, request.ProfileId,
                            _appLifetime, writeLifetime.Token, () => !writeLifetime.IsCancellationRequested);
                    return new BoardsWritableRichDocumentBrowseAppContentSession(
                        request, address, writeLease, writeLifetime, embeddedOwner, boardCreationHost,
                        iconGenerationHost);
                }
                catch
                {
                    if (embeddedOwner is not null)
                        embeddedOwner.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    writeLifetime.Dispose();
                    writeLease.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    throw;
                }
            }
            if (_takePreparedBoardRead is null || address.StoreId is not { } store ||
                address.ParentFolderId is not { } parent || address.FileId is not { } file ||
                address.RevisionId is not { } revision || address.ReadToken is not { } readToken)
                throw new UnauthorizedAccessException("A rich Files board requires its exact Home-prepared read lease.");
            var lease = _takePreparedBoardRead(readToken, store, parent, file, revision);
            if (!lease.IsOpen || !StringComparer.Ordinal.Equals(lease.ProfileId, request.ProfileId))
            {
                lease.DisposeAsync().GetAwaiter().GetResult();
                throw new UnauthorizedAccessException("The selected rich board belongs to another original Home profile.");
            }
            var readLifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _hostLifetime);
            NativeCanvasBoardEmbeddedArtifactHost? readEmbeddedOwner = null;
            try
            {
                readEmbeddedOwner = CreateEmbeddedArtifactHost(request, readLifetime.Token);
                return new BoardsRichDocumentBrowseAppContentSession(
                    request, address, lease, readLifetime, readEmbeddedOwner);
            }
            catch
            {
                if (readEmbeddedOwner is not null)
                    readEmbeddedOwner.DisposeAsync().AsTask().GetAwaiter().GetResult();
                readLifetime.Dispose();
                lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw;
            }
        }
        // All borrowed resolution and identity checks precede the per-tab lifetime acquisition.
        var notes = _provider.GetRequiredService<NotesRepository>();
        var verified = _provider.GetRequiredService<VerifiedNotesRepository>();
        var admission = _provider.GetRequiredService<HomeNotesResourceAdmissionSource>();
        var approvals = _provider.GetRequiredService<HomeNativeApprovalWindowOwner>();
        var evidence = _provider.GetRequiredService<NotesLocalStoreEvidenceProvider>();
        var setup = _provider.GetRequiredService<BoardsOriginalStoreSetupBackend>();
        if (!admission.IsBoundToOriginalNotes(notes) ||
            !ReferenceEquals(_home.Services.GetService(typeof(NotesRepository)), notes) ||
            !ReferenceEquals(_home.Services.GetService(typeof(HomeNotesResourceAdmissionSource)), admission) ||
            !approvals.IsBoundToOriginalComposition(_home) || !setup.HasOriginalComposition(notes, evidence, _home, approvals))
            throw new UnauthorizedAccessException("Boards must borrow the SAME original Notes, verified store and Home admission/review owners.");
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _hostLifetime);
        try
        {
            var route = new NativeBoardsDesktopRoute(_provider, _home, notes, verified, admission,
                approvals, setup, request.ProfileId, "boards-tab:" + request.TabId.ToString("D"),
                _appLifetime, lifetime.Token);
            return new BoardsBrowseAppContentSession(request, address, route, lifetime, _reduceMotion, _theme);
        }
        catch { lifetime.Dispose(); throw; }
    }

    private NativeCanvasBoardEmbeddedArtifactHost CreateEmbeddedArtifactHost(
        BrowseAppContentRequest request, CancellationToken tabLifetime)
    {
        var writeOwner = new BoardEmbeddedWritePageOwner(_provider, _home, request.ProfileId,
            request.TabId, _appLifetime, tabLifetime,
            () => !tabLifetime.IsCancellationRequested, _theme);
        try
        {
            return new NativeCanvasBoardEmbeddedArtifactHost(writeOwner, _provider, _home,
                request.ProfileId, request.TabId, _appLifetime, tabLifetime,
                () => !tabLifetime.IsCancellationRequested, _theme);
        }
        catch
        {
            writeOwner.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    private sealed class HomeBoardCreationHost(
        Func<BrowseAppContentRequest, BoardsHomeBoardCreationRequest, CancellationToken, Task<BoardsHomeBoardCreationResult>> createAndOpen,
        BrowseAppContentRequest request,
        CancellationToken tabLifetime) : IBoardsHomeBoardCreationHost
    {
        public bool CanRetryOpenCreatedBoard => _hasCreatedBoardPendingOpen;
        public bool CanRetryCreationSettlement => _creationSettlementPending;
        public bool CanInspectUnknownCreation => _creationOutcomeUnknown;
        public bool CanOpenRecordedBoard => _uncertainReceiptAvailable;
        public bool HasUnresolvedCreation => HasUnresolvedState || Volatile.Read(ref _operationInProgress) != 0;
        private volatile bool _hasCreatedBoardPendingOpen;
        private volatile bool _creationSettlementPending;
        private volatile bool _creationOutcomeUnknown;
        private volatile bool _uncertainReceiptAvailable;
        private volatile bool _openedWithAuditWarning;
        private int _operationInProgress;

        private bool HasUnresolvedState => _hasCreatedBoardPendingOpen || _creationSettlementPending ||
            _creationOutcomeUnknown || _uncertainReceiptAvailable || _openedWithAuditWarning;

        private bool TryStartOperation() => Interlocked.CompareExchange(ref _operationInProgress, 1, 0) == 0;
        private void EndOperation() => Volatile.Write(ref _operationInProgress, 0);

        private BoardsHomeBoardCreationResult UnresolvedCreationResult() =>
            new(_hasCreatedBoardPendingOpen ? BoardsHomeBoardCreationOutcome.CreatedOpenFailed :
                _creationSettlementPending ? BoardsHomeBoardCreationOutcome.HomeAuditPending :
                _uncertainReceiptAvailable ? BoardsHomeBoardCreationOutcome.CommittedWithUncertainAudit :
                _openedWithAuditWarning ? BoardsHomeBoardCreationOutcome.OpenedWithAuditWarning :
                BoardsHomeBoardCreationOutcome.OutcomeUnknown,
                Message: "A previous Board creation is still being resolved.");

        private static BoardsHomeBoardCreationResult OperationInProgressResult() =>
            new(BoardsHomeBoardCreationOutcome.OperationInProgress, "OperationInProgress",
                "A Board operation is already in progress.");

        public async Task<BoardsHomeBoardCreationResult> CreateAndOpenAsync(ReadOnlyMemory<byte> validatedDraft,
            CancellationToken cancellationToken = default)
        {
            if (HasUnresolvedState) return UnresolvedCreationResult();
            if (!TryStartOperation()) return OperationInProgressResult();
            try
            {
                if (HasUnresolvedState) return UnresolvedCreationResult();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(tabLifetime, cancellationToken);
                linked.Token.ThrowIfCancellationRequested();
                try
                {
                    var result = await createAndOpen(request,
                        new BoardsHomeBoardCreationRequest(validatedDraft, BoardsHomeBoardCreationOperation.Create), linked.Token).ConfigureAwait(false);
                    RecordCreationResult(result);
                    return result;
                }
                catch
                {
                    // The callback may have crossed Files' commit boundary before failing.
                    // Block duplicate creation until Home reconciles the same attempt.
                    _creationOutcomeUnknown = true;
                    throw;
                }
            }
            finally { EndOperation(); }
        }

        public async Task<BoardsHomeBoardCreationResult> RetryCreationSettlementAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_creationSettlementPending)
                return new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown,
                    "NoPendingSettlement", "There is no retained Home audit settlement to retry.");
            if (!TryStartOperation()) return OperationInProgressResult();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(tabLifetime, cancellationToken);
                linked.Token.ThrowIfCancellationRequested();
                var result = await createAndOpen(request,
                    new BoardsHomeBoardCreationRequest(ReadOnlyMemory<byte>.Empty,
                        BoardsHomeBoardCreationOperation.RetrySettlement), linked.Token).ConfigureAwait(false);
                return RecordSettlementResult(result);
            }
            finally { EndOperation(); }
        }

        public async Task<BoardsHomeBoardCreationResult> RetryOpenCreatedBoardAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_hasCreatedBoardPendingOpen)
                return new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown,
                    "NoPendingCreatedBoard", "There is no committed Board waiting to open.");
            if (!TryStartOperation()) return OperationInProgressResult();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(tabLifetime, cancellationToken);
                linked.Token.ThrowIfCancellationRequested();
                var result = await createAndOpen(request,
                    new BoardsHomeBoardCreationRequest(ReadOnlyMemory<byte>.Empty,
                        BoardsHomeBoardCreationOperation.RetryOpenExisting), linked.Token).ConfigureAwait(false);
                return RecordOpenResult(result);
            }
            finally { EndOperation(); }
        }

        public async Task<BoardsHomeBoardCreationResult> InspectUnknownCreationAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_creationOutcomeUnknown)
                return new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown,
                    "NoUnknownAttempt", "There is no unresolved Board creation to inspect.");
            if (!TryStartOperation()) return OperationInProgressResult();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(tabLifetime, cancellationToken);
                linked.Token.ThrowIfCancellationRequested();
                var result = await createAndOpen(request,
                    new BoardsHomeBoardCreationRequest(ReadOnlyMemory<byte>.Empty,
                        BoardsHomeBoardCreationOperation.InspectOutcome), linked.Token).ConfigureAwait(false);
                return RecordInspectionResult(result);
            }
            finally { EndOperation(); }
        }

        public async Task<BoardsHomeBoardCreationResult> OpenRecordedBoardAsync(
            CancellationToken cancellationToken = default)
        {
            if (!_uncertainReceiptAvailable)
                return new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown,
                    "NoRecordedReceipt", "There is no corroborated Board receipt to open.");
            if (!TryStartOperation()) return OperationInProgressResult();
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(tabLifetime, cancellationToken);
                linked.Token.ThrowIfCancellationRequested();
                var result = await createAndOpen(request,
                    new BoardsHomeBoardCreationRequest(ReadOnlyMemory<byte>.Empty,
                        BoardsHomeBoardCreationOperation.OpenRecordedExisting), linked.Token).ConfigureAwait(false);
                return RecordRecordedOpenResult(result);
            }
            finally { EndOperation(); }
        }

        private void RecordCreationResult(BoardsHomeBoardCreationResult result)
        {
            _hasCreatedBoardPendingOpen = result.Outcome == BoardsHomeBoardCreationOutcome.CreatedOpenFailed;
            _creationSettlementPending = result.Outcome == BoardsHomeBoardCreationOutcome.HomeAuditPending;
            _creationOutcomeUnknown = result.Outcome is BoardsHomeBoardCreationOutcome.OutcomeUnknown or
                BoardsHomeBoardCreationOutcome.OperationInProgress;
            _uncertainReceiptAvailable = result.Outcome == BoardsHomeBoardCreationOutcome.CommittedWithUncertainAudit;
            _openedWithAuditWarning = result.Outcome == BoardsHomeBoardCreationOutcome.OpenedWithAuditWarning;
        }

        private BoardsHomeBoardCreationResult RecordSettlementResult(BoardsHomeBoardCreationResult result)
        {
            switch (result.Outcome)
            {
                case BoardsHomeBoardCreationOutcome.HomeAuditPending:
                    _creationSettlementPending = true; _creationOutcomeUnknown = false; _hasCreatedBoardPendingOpen = false; break;
                case BoardsHomeBoardCreationOutcome.OutcomeUnknown:
                    _creationSettlementPending = false; _creationOutcomeUnknown = true; _hasCreatedBoardPendingOpen = false; break;
                case BoardsHomeBoardCreationOutcome.CreatedOpenFailed:
                    _creationSettlementPending = false; _creationOutcomeUnknown = false; _hasCreatedBoardPendingOpen = true; break;
                case BoardsHomeBoardCreationOutcome.CommittedWithUncertainAudit:
                    _creationSettlementPending = false; _creationOutcomeUnknown = false; _hasCreatedBoardPendingOpen = false;
                    _uncertainReceiptAvailable = true; break;
                case BoardsHomeBoardCreationOutcome.OpenedWithAuditWarning:
                    _creationSettlementPending = false; _creationOutcomeUnknown = false; _hasCreatedBoardPendingOpen = false;
                    _uncertainReceiptAvailable = false; _openedWithAuditWarning = true; break;
                case BoardsHomeBoardCreationOutcome.Opened:
                case BoardsHomeBoardCreationOutcome.CreateFailedBeforeCommit:
                    _creationSettlementPending = false; _creationOutcomeUnknown = false; _hasCreatedBoardPendingOpen = false; break;
                case BoardsHomeBoardCreationOutcome.OperationInProgress:
                    // The exact prior attempt remains pending; this response did
                    // not replace it or authorize a new creation.
                    break;
                default:
                    // A settlement retry cannot be canceled before creation. Preserve
                    // the no-duplicate gate if Home returned an impossible outcome.
                    _creationSettlementPending = false; _creationOutcomeUnknown = true; _hasCreatedBoardPendingOpen = false;
                    result = result with { Outcome = BoardsHomeBoardCreationOutcome.OutcomeUnknown };
                    break;
            }
            return result;
        }

        private BoardsHomeBoardCreationResult RecordOpenResult(BoardsHomeBoardCreationResult result)
        {
            switch (result.Outcome)
            {
                case BoardsHomeBoardCreationOutcome.Opened:
                    _hasCreatedBoardPendingOpen = false; _creationSettlementPending = false; _creationOutcomeUnknown = false;
                    _uncertainReceiptAvailable = false; _openedWithAuditWarning = false;
                    return result;
                case BoardsHomeBoardCreationOutcome.HomeAuditPending:
                    _hasCreatedBoardPendingOpen = false; _creationSettlementPending = true; _creationOutcomeUnknown = false;
                    return result;
                case BoardsHomeBoardCreationOutcome.OutcomeUnknown:
                    _hasCreatedBoardPendingOpen = false; _creationSettlementPending = false; _creationOutcomeUnknown = true;
                    return result;
                case BoardsHomeBoardCreationOutcome.OpenedWithAuditWarning:
                    _hasCreatedBoardPendingOpen = false; _creationSettlementPending = false; _creationOutcomeUnknown = false;
                    _uncertainReceiptAvailable = false; _openedWithAuditWarning = true;
                    return result;
                case BoardsHomeBoardCreationOutcome.CommittedWithUncertainAudit:
                    _hasCreatedBoardPendingOpen = false; _creationSettlementPending = false; _creationOutcomeUnknown = false;
                    _uncertainReceiptAvailable = true; _openedWithAuditWarning = false;
                    return result;
                default:
                    _hasCreatedBoardPendingOpen = true; _creationSettlementPending = false; _creationOutcomeUnknown = false;
                    return result.Outcome == BoardsHomeBoardCreationOutcome.CreatedOpenFailed
                        ? result : result with { Outcome = BoardsHomeBoardCreationOutcome.CreatedOpenFailed };
            }
        }

        private BoardsHomeBoardCreationResult RecordInspectionResult(BoardsHomeBoardCreationResult result)
        {
            _hasCreatedBoardPendingOpen = false;
            _creationSettlementPending = false;
            _uncertainReceiptAvailable = false;
            _openedWithAuditWarning = false;
            switch (result.Outcome)
            {
                case BoardsHomeBoardCreationOutcome.HomeAuditPending:
                    _creationOutcomeUnknown = false; _creationSettlementPending = true; return result;
                case BoardsHomeBoardCreationOutcome.CommittedWithUncertainAudit:
                    _creationOutcomeUnknown = false; _uncertainReceiptAvailable = true; return result;
                case BoardsHomeBoardCreationOutcome.CreatedOpenFailed:
                    _creationOutcomeUnknown = false; _hasCreatedBoardPendingOpen = true; return result;
                case BoardsHomeBoardCreationOutcome.Opened:
                    _creationOutcomeUnknown = false; return result;
                case BoardsHomeBoardCreationOutcome.OpenedWithAuditWarning:
                    _creationOutcomeUnknown = false; _openedWithAuditWarning = true; return result;
                case BoardsHomeBoardCreationOutcome.CreateFailedBeforeCommit:
                    _creationOutcomeUnknown = false; return result;
                case BoardsHomeBoardCreationOutcome.OutcomeUnknown:
                    _creationOutcomeUnknown = true; return result;
                default:
                    _creationOutcomeUnknown = true;
                    return result with { Outcome = BoardsHomeBoardCreationOutcome.OutcomeUnknown };
            }
        }

        private BoardsHomeBoardCreationResult RecordRecordedOpenResult(BoardsHomeBoardCreationResult result)
        {
            switch (result.Outcome)
            {
                case BoardsHomeBoardCreationOutcome.OpenedWithAuditWarning:
                    _uncertainReceiptAvailable = false; _openedWithAuditWarning = true;
                    return result;
                case BoardsHomeBoardCreationOutcome.Opened:
                    _uncertainReceiptAvailable = false; _openedWithAuditWarning = false; _creationOutcomeUnknown = false;
                    return result;
                case BoardsHomeBoardCreationOutcome.OutcomeUnknown:
                    _uncertainReceiptAvailable = false; _creationOutcomeUnknown = true;
                    return result;
                default:
                    // The exact receipt remains retained so the user can retry its
                    // fresh owner-authorized open; no create operation is repeated.
                    _uncertainReceiptAvailable = true;
                    return result.Outcome == BoardsHomeBoardCreationOutcome.CommittedWithUncertainAudit
                        ? result : result with { Outcome = BoardsHomeBoardCreationOutcome.CommittedWithUncertainAudit };
            }
        }
    }
}
