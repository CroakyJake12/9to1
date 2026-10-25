using System.Runtime.CompilerServices;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Services;

/// <summary>Typed original Files metadata publication into the existing Home Library index.
/// References retain actual privately issued metadata. File preview targets are freshly resolved, never guessed.</summary>
internal sealed class NativeHomeFilesLibrarySource : IHomeOriginalLibrarySource
{
    // Native target metadata only, not a page, content read, permission or navigation grant.
    internal sealed record OriginalOpenTarget(Guid StoreId, Guid FolderId, Guid? FileId = null, Guid? RevisionId = null);
    internal sealed record NativeHomeFilesBoardCreateResult(
        FilesOriginalBoardDocumentCreateSettlementState State,
        FilesOriginalBoardDocumentCreateAttempt? SettlementAttempt,
        FilesOriginalBoardDocumentCreateReceipt? Receipt,
        HomeDeepLink? OpenLink,
        AppContentAddress? PreparedAddress,
        IReadOnlyList<Exception> CleanupFailures,
        string? OpenFailureCode = null,
        string? OpenFailureMessage = null);
    private sealed record Entry(HomeArtifactReference View, FilesNativeBrowserPage Page, HostedItemMetadata Row);
    private sealed record Operation(Task Task, CancellationToken Token);
    private readonly FilesNativeBrowserService _browser;
    private readonly FilesNativeBrowserService.BoardDocumentCreateOwner? _boardCreate;
    private readonly NativeHomeFilesBoardReadSource? _boardRead;
    private readonly NativeHomeFilesBoardWriteSource? _boardWrite;
    private readonly Func<HomePermissionAuthorization, CancellationToken, Task>? _boardWriteReview;
    private readonly Action<Action>? _boardWriteScope;
    private readonly Action<Task>? _boardWriteRetain;
    private readonly AuthenticatedResourceActor _actor;
    private readonly IHomeCoreStateStore _home;
    private readonly Func<CancellationToken, Task<bool>> _ownerCurrent;
    private readonly Action<Action> _parent;
    private readonly Func<bool> _alive;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Operation> _operations = [];
    private readonly ConditionalWeakTable<Exception, object> _refusals = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private HomeLibraryQuery? _query;
    private CloudflareOriginalTaskLedger? _currentOriginal;
    private bool _retiring;
    private Task? _close;
    internal NativeHomeFilesLibrarySource(FilesNativeBrowserService browser, AuthenticatedResourceActor actor,
        IHomeCoreStateStore home, Func<CancellationToken, Task<bool>> ownerCurrent,
        Action<Action> parent, Func<bool> alive, NativeHomeFilesBoardReadSource? boardRead = null,
        NativeHomeFilesBoardWriteSource? boardWrite = null,
        Func<HomePermissionAuthorization, CancellationToken, Task>? boardWriteReview = null,
        Action<Action>? boardWriteScope = null, Action<Task>? boardWriteRetain = null,
        FilesNativeBrowserService.BoardDocumentCreateOwner? boardCreate = null)
    { _browser = browser; _boardCreate = boardCreate; _boardRead = boardRead; _boardWrite = boardWrite; _actor = actor; _home = home;
        _ownerCurrent = ownerCurrent; _parent = parent; _alive = alive; _boardWriteReview = boardWriteReview;
        _boardWriteScope = boardWriteScope; _boardWriteRetain = boardWriteRetain; }
    internal bool HasWritableBoardSource => _boardWrite is not null && _boardWriteReview is not null &&
        _boardWriteScope is not null && _boardWriteRetain is not null;
    internal bool HasBoardCreateSource => _boardCreate is not null && _boardRead is not null &&
        _boardWrite is not null && _boardWriteReview is not null && _boardWriteScope is not null && _boardWriteRetain is not null;
    public string? AvailabilityNotice => _boardRead is null || !_browser.HasOriginalBoardDocumentReadOwner
        ? "Files shows folder and supported raw text/code references from its current metadata. Text preview requires an existing local content anchor."
        : HasWritableBoardSource
            ? "Files shows current folders, supported text/code references and .9to1board documents. Boards opens the exact selected Files revision; saving requests fresh Home review and creates a new immutable revision."
            : "Files shows current folders, supported text/code references and .9to1board documents. Board documents open read-only from their exact selected Files revision; text preview requires an existing local content anchor.";
    public HomeLibraryService CreateService(IHomeDeepLinkRouter router) => new(this, this, router, new HomeCoreLibraryPreferences(_home));
    internal HomeLibraryDestinationPage CreateBoardFolderPickerPage(Func<CancellationToken, Task> admit,
        CuiThemePreferenceContext? theme = null)
    {
        if (!HasBoardCreateSource) throw new NotSupportedException("The original Home Files board-create route is unavailable.");
        var page = new HomeLibraryDestinationPage(router => CreateService(router), admit,
            (_, _) => false, this, theme);
        page.SetFolderSelectionMode(SelectBoardCreateFolderAsync);
        return page;
    }
    public Task RevalidateAsync(CancellationToken token) => RunAsync(async (source, caller) =>
    { await RequireOwnerAsync(source, caller).ConfigureAwait(false); return true; }, token);
    public void DemandExternalOriginalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);

    public Task<IReadOnlyList<HomeArtifactReference>> SearchAsync(HomeLibraryQuery query, CancellationToken token) =>
        RunAsync<IReadOnlyList<HomeArtifactReference>>(async (source, caller) =>
        {
            if (query.Cursor is not null || query.Text?.Length > 256)
                throw Refuse("InvalidQuery", "Use a search of at most 256 characters and the current Library page.");
            var identity = query with { Offset = 0, Cursor = null };
            if (query.Offset != 0)
            {
                if (_query != identity) throw Refuse("LibraryPageChanged", "Refresh Files references before opening another Library page.");
                foreach (var page in _entries.Values.Select(entry => entry.Page).Distinct<FilesNativeBrowserPage>(ReferenceEqualityComparer.Instance))
                    await source.AwaitAsync(source.Invoke(() => ValidatePageAsync(page, caller))).ConfigureAwait(false);
                await RequireOwnerAsync(source, caller).ConfigureAwait(false);
                return ProjectEntries(query.Text);
            }
            _entries.Clear(); _query = null;
            var available = await source.AwaitAsync(source.Invoke(() =>
                _browser.ObserveOriginalHomeLibraryAvailabilityWithinSourceAsync(_actor, _alive,
                    Own, Retain, caller))).ConfigureAwait(false);
            if (!_browser.IsIssuedOriginalHomeLibraryAvailability(available, _actor))
                throw new InvalidDataException("The Files availability observation has a different original issuer.");
            if (!available.IsAvailable || available.StoreId is not { } store || store == Guid.Empty)
                throw Refuse(available.Code, available.Message);
            // Traverse only original canonical metadata pages. The existing shared Library
            // service owns text/app/type filtering, sorting, pins and final pagination.
            var folders = new Queue<(Guid? Id, string Label)>(); folders.Enqueue((null, "Files"));
            var seenFolders = new HashSet<Guid>();
            var pages = new List<FilesNativeBrowserPage>();
            string? revision = null;
            var rowsObserved = 0;
            while (folders.TryDequeue(out var folder))
            {
                FilesNativeBrowserCursor? cursor = null;
                var offsets = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    var page = await source.AwaitAsync(source.Invoke(() =>
                        _browser.ListOriginalHomeLibraryFolderPageWithinSourceAsync(_actor, store, folder.Id,
                            "", cursor, _alive, Own, Retain, caller))).ConfigureAwait(false);
                    if (page.StoreID != store || page.ParentID != folder.Id || page.IsTrash ||
                        revision is not null && revision != page.StoreRevision)
                        throw new InvalidDataException("The original Files Library page changed its store, parent or revision.");
                    revision ??= page.StoreRevision; pages.Add(page);
                    rowsObserved = checked(rowsObserved + page.Items.Count);
                    if (rowsObserved > 4096)
                        throw Refuse("LibraryQueryTooLarge", "This Files Library view exceeds its current 4,096-item metadata limit. Browse the original Files app for this store.");
                    foreach (var row in page.Items)
                    {
                        if (row.Id.Value == Guid.Empty || string.IsNullOrWhiteSpace(row.Name) || row.ParentId?.Value != folder.Id)
                            throw new InvalidDataException("The original Files owner returned invalid folder/item metadata.");
                        var isFolder = row.Kind == HostedItemKind.Folder;
                        if (isFolder)
                        {
                            if (!seenFolders.Add(row.Id.Value))
                                throw new InvalidDataException("The original Files folder hierarchy repeats a canonical identity.");
                            folders.Enqueue((row.Id.Value, row.Name));
                        }
                        var isText = row.Kind == HostedItemKind.File && folder.Id is not null &&
                            row.CurrentRevisionId is not null && _browser.HasOriginalTextPreviewOwner &&
                            FilesNativeBrowserService.IsTextPreviewCandidate(row);
                        var isBoard = row.Kind == HostedItemKind.File && folder.Id is not null &&
                            row.CurrentRevisionId is not null && _boardRead is not null && _browser.HasOriginalBoardDocumentReadOwner &&
                            FilesNativeBrowserService.IsBoardDocumentCandidate(row);
                        if (!isFolder && !isText && !isBoard) continue; // Other owning artifacts/binary viewers are not guessed.
                        var id = store.ToString("D") + "/" + row.Id.Value.ToString("D");
                        var view = new HomeArtifactReference("files", id, row.Name, isFolder ? "Files folder" : isBoard ? "9to1 board document" : "Text/code file",
                            folder.Label, row.OwnerPrincipalId, null, row.ModifiedAt, false, false, row.IsShared, false, [],
                            new(HomeRoute.Library, "files", id, "open", isFolder ? "files.folder" : isBoard ? "files.board" : "files.preview"));
                        if (!_entries.TryAdd(id, new(view, page, row)))
                            throw new InvalidDataException("The original Files cursor returned duplicate item identities.");
                    }
                    cursor = page.Next;
                    if (cursor is not null && (cursor.StoreID != store || cursor.StoreRevision != revision ||
                        cursor.ParentID != folder.Id || cursor.OriginalActor != _actor || cursor.IsTrash ||
                        cursor.Search != "" || !offsets.Add(cursor.Offset)))
                        throw new InvalidDataException("The original Files Library cursor changed or repeated.");
                } while (cursor is not null);
            }
            // Validate every actually traversed page, including pages without supported rows.
            foreach (var page in pages)
                await source.AwaitAsync(source.Invoke(() => ValidatePageAsync(page, caller))).ConfigureAwait(false);
            await RequireOwnerAsync(source, caller).ConfigureAwait(false);
            _query = identity;
            return ProjectEntries(query.Text);
        }, token);

    private IReadOnlyList<HomeArtifactReference> ProjectEntries(string? searchText)
    {
        var query = searchText?.Trim();
        return _entries.Values.Select(entry => entry.View).Where(item => string.IsNullOrEmpty(query) ||
            item.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            item.ArtifactType.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            item.Location?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true ||
            item.Owner?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true).ToArray();
    }

    public Task<HomeArtifactReference?> GetByIdAsync(string app, string id, CancellationToken token) =>
        RunAsync<HomeArtifactReference?>(async (source, caller) => app == "files" && _entries.TryGetValue(id, out var entry) &&
            await IsCurrentAsync(entry, source, caller).ConfigureAwait(false) ? entry.View : null, token);
    public Task<bool> CanReadAsync(string app, string id, CancellationToken token) =>
        RunAsync(async (source, caller) => app == "files" && _entries.TryGetValue(id, out var entry) &&
            await IsCurrentAsync(entry, source, caller).ConfigureAwait(false), token);
    public Task<bool> CanOpenAsync(HomeDeepLink link, CancellationToken token) =>
        RunAsync(async (source, caller) => link.OwnerApp == "files" && (link.TargetRouteId is "files.folder" or "files.preview" or "files.board") &&
            _entries.TryGetValue(link.ObjectId, out var entry) && ReferenceEquals(entry.View.OpenLink, link) &&
            await IsCurrentAsync(entry, source, caller).ConfigureAwait(false), token);
    private async Task<bool> IsCurrentAsync(Entry entry, CloudflareOriginalTaskLedger source, CancellationToken token)
    {
        if (!entry.Page.Items.Any(row => ReferenceEquals(row, entry.Row)) ||
            entry.Row.Kind != HostedItemKind.Folder && (entry.Row.Kind != HostedItemKind.File || entry.Page.ParentID is null ||
                entry.Row.CurrentRevisionId is null ||
                !FilesNativeBrowserService.IsTextPreviewCandidate(entry.Row) &&
                !FilesNativeBrowserService.IsBoardDocumentCandidate(entry.Row))) return false;
        await source.AwaitAsync(source.Invoke(() => ValidatePageAsync(entry.Page, token))).ConfigureAwait(false);
        await RequireOwnerAsync(source, token).ConfigureAwait(false);
        return true;
    }
    internal Task<OriginalOpenTarget?> ResolveOriginalOpenTargetAsync(HomeDeepLink link, CancellationToken token) =>
        RunAsync<OriginalOpenTarget?>(async (source, caller) =>
        {
            if (link.OwnerApp != "files" || link.Action != "open" || !_entries.TryGetValue(link.ObjectId, out var entry) ||
                !ReferenceEquals(entry.View.OpenLink, link) || !await IsCurrentAsync(entry, source, caller).ConfigureAwait(false)) return null;
            var store = entry.Page.StoreID;
            if (entry.Row.Kind == HostedItemKind.Folder && link.TargetRouteId == "files.folder")
                return new(store, entry.Row.Id.Value);
            if (entry.Row.Kind == HostedItemKind.File && link.TargetRouteId == "files.preview" &&
                entry.Page.ParentID is { } parent && entry.Row.CurrentRevisionId is { } revision)
                return new(store, parent, entry.Row.Id.Value, revision.Value);
            if (entry.Row.Kind == HostedItemKind.File && link.TargetRouteId == "files.board" &&
                entry.Page.ParentID is { } boardParent && entry.Row.CurrentRevisionId is { } boardRevision &&
                _boardRead is not null && _browser.HasOriginalBoardDocumentReadOwner && FilesNativeBrowserService.IsBoardDocumentCandidate(entry.Row))
                return new(store, boardParent, entry.Row.Id.Value, boardRevision.Value);
            return null;
        }, token);

    internal Task<Guid?> PrepareOriginalBoardReadAsync(HomeDeepLink link, CancellationToken token) =>
        RunAsync<Guid?>(async (source, caller) =>
        {
            if (link.OwnerApp != "files" || link.TargetRouteId != "files.board" ||
                !_entries.TryGetValue(link.ObjectId, out var entry) || !ReferenceEquals(entry.View.OpenLink, link) ||
                !await IsCurrentAsync(entry, source, caller).ConfigureAwait(false) ||
                entry.Row.Kind != HostedItemKind.File || !FilesNativeBrowserService.IsBoardDocumentCandidate(entry.Row)) return null;
            var boardRead = _boardRead ?? throw new NotSupportedException("The original Home selected-board reader is unavailable.");
            var lease = await source.AwaitAsync(source.Invoke(() => boardRead.ReadSelectedAsync(entry.Page, entry.Row,
                _alive, _ownerCurrent, Own, Retain, source, caller))).ConfigureAwait(false);
            try
            {
                if (!await IsCurrentAsync(entry, source, caller).ConfigureAwait(false) || !lease.IsOpen)
                    throw new UnauthorizedAccessException("The selected Home Library board row retired before its read was prepared.");
                return boardRead.RegisterPrepared(lease);
            }
            catch
            {
                await lease.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }, token);

    internal Task<Guid?> PrepareOriginalBoardWriteAsync(HomeDeepLink link, CancellationToken token) =>
        RunAsync<Guid?>(async (source, caller) =>
        {
            if (_boardWrite is null || _boardWriteReview is null || _boardWriteScope is null || _boardWriteRetain is null ||
                link.OwnerApp != "files" || link.TargetRouteId != "files.board" ||
                !_entries.TryGetValue(link.ObjectId, out var entry) || !ReferenceEquals(entry.View.OpenLink, link) ||
                !await IsCurrentAsync(entry, source, caller).ConfigureAwait(false) ||
                entry.Row.Kind != HostedItemKind.File || !FilesNativeBrowserService.IsBoardDocumentCandidate(entry.Row)) return null;
            var boardRead = _boardRead ?? throw new NotSupportedException("The original Home selected-board reader is unavailable.");
            NativeHomeFilesBoardReadLease? lease = null;
            var transferred = false;
            try
            {
                lease = await source.AwaitAsync(source.Invoke(() => boardRead.ReadSelectedAsync(entry.Page, entry.Row,
                    _alive, _ownerCurrent, Own, Retain, source, caller))).ConfigureAwait(false);
                if (!await IsCurrentAsync(entry, source, caller).ConfigureAwait(false) || !lease.IsOpen)
                    throw new UnauthorizedAccessException("The selected Home Library board row retired before its write was prepared.");
                var id = _boardWrite.RegisterPrepared(entry.Page, entry.Row, lease, _alive, _boardWriteReview,
                    "home-library-board:" + entry.Row.Id.Value.ToString("D"), _boardWriteScope, _boardWriteRetain);
                transferred = true;
                return id;
            }
            finally
            {
                if (!transferred && lease is not null) await lease.DisposeAsync().ConfigureAwait(false);
            }
        }, token);

    /// <summary>Accepts only the current Home Library link for the configured Boards folder.
    /// The returned link is metadata, not a folder grant; create rechecks the exact private row.</summary>
    internal Task<HomeDeepLink?> SelectBoardCreateFolderAsync(HomeDeepLink link, CancellationToken token) =>
        RunAsync<HomeDeepLink?>(async (source, caller) =>
        {
            if (!HasBoardCreateSource || link.OwnerApp != "files" || link.Action != "open" ||
                link.TargetRouteId != "files.folder" || !_entries.TryGetValue(link.ObjectId, out var entry) ||
                !ReferenceEquals(entry.View.OpenLink, link) || entry.Row.Kind != HostedItemKind.Folder ||
                !await IsCurrentAsync(entry, source, caller).ConfigureAwait(false)) return null;
            var canCreate = await source.AwaitAsync(source.Invoke(() => _boardCreate!.CanCreateInFolderWithinSourceAsync(
                entry.Page, entry.Row, _actor, _alive, Own, Retain, caller))).ConfigureAwait(false);
            await RequireOwnerAsync(source, caller).ConfigureAwait(false);
            return canCreate ? link : null;
        }, token);

    /// <summary>Creates in the exact selected configured Boards root. A terminal Files receipt
    /// is returned even when later read/address preparation fails, so Home can retry opening the
    /// same created artifact without repeating the creation effect.</summary>
    internal Task<NativeHomeFilesBoardCreateResult> CreateInSelectedFolderAsync(HomeDeepLink selectedFolder,
        string displayName, ReadOnlyMemory<byte> validatedDocument, CancellationToken token) =>
        RunAsync(async (source, caller) =>
        {
            if (!HasBoardCreateSource || selectedFolder.OwnerApp != "files" || selectedFolder.Action != "open" ||
                selectedFolder.TargetRouteId != "files.folder" || !_entries.TryGetValue(selectedFolder.ObjectId, out var entry) ||
                !ReferenceEquals(entry.View.OpenLink, selectedFolder) || entry.Row.Kind != HostedItemKind.Folder ||
                !await IsCurrentAsync(entry, source, caller).ConfigureAwait(false))
                throw new UnauthorizedAccessException("Choose the current configured Boards folder from this Home Library session.");

            var createOwner = _boardCreate!;
            var eligible = await source.AwaitAsync(source.Invoke(() => createOwner.CanCreateInFolderWithinSourceAsync(
                entry.Page, entry.Row, _actor, _alive, Own, Retain, caller))).ConfigureAwait(false);
            await RequireOwnerAsync(source, caller).ConfigureAwait(false);
            if (!eligible)
                throw Refuse("BoardFolderUnavailable", "The selected folder is not the current configured Boards folder.");

            // The Files owner performs its own exact row/store/revision check and separately
            // obtains files.board.create review/claim before any durable create effect.
            var attempt = default(FilesOriginalBoardDocumentCreateAttempt?);
            FilesOriginalBoardDocumentCreateSettlementResult settlement;
            var prepareOpen = true;
            try
            {
                var createTask = StartSettlementAwareTask(source, retain => createOwner.CreateBoardDocumentWithinSourceAsync(
                    entry.Page, entry.Row, _actor, displayName, validatedDocument, _alive, _boardWriteReview!,
                    "home-library-board-create:" + entry.Row.Id.Value.ToString("D"), Own, retain, caller));
                var receipt = await createTask.ConfigureAwait(false);
                settlement = new(FilesOriginalBoardDocumentCreateSettlementState.Committed, receipt, []);
            }
            catch (Exception error) when (FindCreateSettlementFailure(error) is not null)
            {
                var cause = FindCreateSettlementFailure(error)!;
                attempt = cause.Attempt;
                settlement = new(cause.State, cause.Receipt, cause.CleanupFailures);
                if (cause.State == FilesOriginalBoardDocumentCreateSettlementState.HomeAuditPending)
                {
                    try
                    {
                        var retryTask = StartSettlementAwareTask(source, retain => createOwner.RetryCreateSettlementWithinSourceAsync(
                            attempt, Own, retain, caller), cancellationIsPending: true);
                        settlement = await retryTask.ConfigureAwait(false);
                    }
                    catch (Exception retryError) when (FindCreateSettlementFailure(retryError) is { } retryFailure)
                    {
                        settlement = new(retryFailure.State, retryFailure.Receipt, retryFailure.CleanupFailures);
                    }
                    catch (OperationCanceledException)
                    {
                        // Preserve the owner-issued attempt when the observer cancels while
                        // terminal Home audit is still pending; only an audit-only retry may resume it.
                        settlement = new(FilesOriginalBoardDocumentCreateSettlementState.HomeAuditPending,
                            null, cause.CleanupFailures);
                    }
                }
                else if (cause.State == FilesOriginalBoardDocumentCreateSettlementState.OutcomeUnknown)
                {
                    // Reconciliation may corroborate the exact Files artifact after an
                    // uncertain effect. Keep that evidence separate from publication: the
                    // user must explicitly choose Open recorded board, which performs a new
                    // files.board.read review and exact receipt validation.
                    prepareOpen = false;
                    try
                    {
                        settlement = await ObserveCreateSettlementWithinSourceAsync(source, createOwner, attempt!,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        settlement = new(FilesOriginalBoardDocumentCreateSettlementState.OutcomeUnknown,
                            null, cause.CleanupFailures);
                    }
                }
            }
            if (settlement.Receipt is { } selectedReceipt &&
                (selectedReceipt.FilesStoreId != entry.Page.StoreID || selectedReceipt.ParentFolderId != entry.Row.Id.Value))
                throw new InvalidDataException("The original Files create settlement names another store or selected parent folder.");
            return await FinishCreatedBoardWithinSourceAsync(source, createOwner, settlement, attempt,
                CancellationToken.None, prepareOpen).ConfigureAwait(false);
        }, token);

    internal Task<NativeHomeFilesBoardCreateResult> RetryCreateSettlementAndPrepareAsync(
        FilesOriginalBoardDocumentCreateAttempt sameAttempt, CancellationToken token) =>
        RunAsync(async (source, caller) =>
        {
            var owner = _boardCreate ?? throw new NotSupportedException("The original Files board-create owner is unavailable.");
            FilesOriginalBoardDocumentCreateSettlementResult settlement;
            try
            {
                var retryTask = StartSettlementAwareTask(source, retain => owner.RetryCreateSettlementWithinSourceAsync(
                    sameAttempt, Own, retain, caller), cancellationIsPending: true);
                settlement = await retryTask.ConfigureAwait(false);
            }
            catch (Exception retryError) when (FindCreateSettlementFailure(retryError) is { } retryFailure)
            { settlement = new(retryFailure.State, retryFailure.Receipt, retryFailure.CleanupFailures); }
            catch (OperationCanceledException)
            { settlement = new(FilesOriginalBoardDocumentCreateSettlementState.HomeAuditPending, null, []); }
            return await FinishCreatedBoardWithinSourceAsync(source, owner, settlement, sameAttempt, caller).ConfigureAwait(false);
        }, token);

    internal Task<NativeHomeFilesBoardCreateResult> ObserveCreateSettlementAsync(
        FilesOriginalBoardDocumentCreateAttempt sameAttempt, CancellationToken token) =>
        RunAsync(async (source, caller) =>
        {
            var owner = _boardCreate ?? throw new NotSupportedException("The original Files board-create owner is unavailable.");
            var settlement = await ObserveCreateSettlementWithinSourceAsync(source, owner, sameAttempt, caller).ConfigureAwait(false);
            return await FinishCreatedBoardWithinSourceAsync(source, owner, settlement, sameAttempt, caller,
                prepareOpen: false).ConfigureAwait(false);
        }, token);

    private async Task<FilesOriginalBoardDocumentCreateSettlementResult> ObserveCreateSettlementWithinSourceAsync(
        CloudflareOriginalTaskLedger source, FilesNativeBrowserService.BoardDocumentCreateOwner owner,
        FilesOriginalBoardDocumentCreateAttempt attempt, CancellationToken token)
    {
        var observeTask = StartSettlementAwareTask(source, retain => owner.ObserveCreateSettlementWithinSourceAsync(
            attempt, Own, retain, token));
        return await observeTask.ConfigureAwait(false);
    }

    private async Task<NativeHomeFilesBoardCreateResult> FinishCreatedBoardWithinSourceAsync(
        CloudflareOriginalTaskLedger source, FilesNativeBrowserService.BoardDocumentCreateOwner owner,
        FilesOriginalBoardDocumentCreateSettlementResult settlement,
        FilesOriginalBoardDocumentCreateAttempt? attempt, CancellationToken token, bool prepareOpen = true)
    {
        var receipt = settlement.Receipt;
        if (receipt is null || settlement.State is FilesOriginalBoardDocumentCreateSettlementState.OutcomeUnknown or
            FilesOriginalBoardDocumentCreateSettlementState.NotCreated)
            return new(settlement.State, attempt, null, null, null, settlement.CleanupFailures);
        if (!owner.IsIssuedCreateReceipt(receipt) || receipt.OriginalActor != _actor ||
            receipt.FilesStoreId == Guid.Empty || receipt.ParentFolderId == Guid.Empty ||
            receipt.DocumentId == Guid.Empty || receipt.FileId == Guid.Empty || receipt.FilesCasRevisionId == Guid.Empty ||
            receipt.FileRevisionId == Guid.Empty || string.IsNullOrWhiteSpace(receipt.OwningAppRevisionId))
            throw new InvalidDataException("The original Files board-create receipt is not issued by this Home Files owner.");

        var link = new HomeDeepLink(HomeRoute.Library, "files",
            receipt.FilesStoreId.ToString("D") + "/" + receipt.FileId.ToString("D"), "open", "files.board");
        if (settlement.State == FilesOriginalBoardDocumentCreateSettlementState.HomeAuditPending)
            return new(settlement.State, attempt, receipt, link, null, settlement.CleanupFailures,
                "HomeAuditPending", "The exact Files board exists, but Home has not settled its create audit. Retry audit before opening it.");
        if (!prepareOpen)
            return new(settlement.State, attempt, receipt, link, null, settlement.CleanupFailures);
        try
        {
            var address = await PrepareCreatedBoardOpenWithinSourceAsync(source, owner, receipt, token).ConfigureAwait(false);
            return new(settlement.State, attempt, receipt, link, address, settlement.CleanupFailures);
        }
        catch (Exception openFailure)
        {
            // Creation is terminal. Preserve the exact receipt/attempt for retry-open; never
            // report a precommit failure or retry the create effect.
            return new(settlement.State, attempt, receipt, link, null, settlement.CleanupFailures,
                openFailure is OperationCanceledException ? "CreatedOpenCanceled" : "CreatedOpenFailed",
                "The board was created, but its rich editor could not be opened. Retry opening this same created board.");
        }
    }

    internal Task<AppContentAddress> PrepareCreatedBoardOpenAsync(FilesOriginalBoardDocumentCreateReceipt receipt,
        CancellationToken token) => RunAsync(async (source, caller) =>
        {
            var owner = _boardCreate ?? throw new NotSupportedException("The original Files board-create owner is unavailable.");
            if (!owner.IsIssuedCreateReceipt(receipt) || receipt.OriginalActor != _actor)
                throw new UnauthorizedAccessException("Retry opening only the exact Files board-create receipt from this Home owner.");
            return await PrepareCreatedBoardOpenWithinSourceAsync(source, owner, receipt, caller).ConfigureAwait(false);
        }, token);

    private async Task<AppContentAddress> PrepareCreatedBoardOpenWithinSourceAsync(CloudflareOriginalTaskLedger source,
        FilesNativeBrowserService.BoardDocumentCreateOwner createOwner,
        FilesOriginalBoardDocumentCreateReceipt receipt, CancellationToken token)
    {
        await source.AwaitAsync(source.Invoke(() => createOwner.RevalidateCreatedBoardWithinSourceAsync(
            receipt, Own, Retain, token))).ConfigureAwait(false);
        await RequireOwnerAsync(source, token).ConfigureAwait(false);

        FilesNativeBrowserCursor? cursor = null;
        var offsets = new HashSet<string>(StringComparer.Ordinal);
        var pages = new List<FilesNativeBrowserPage>();
        HostedItemMetadata? createdRow = null;
        FilesNativeBrowserPage? createdPage = null;
        string? storeRevision = null;
        do
        {
            var page = await source.AwaitAsync(source.Invoke(() => _browser.ListOriginalHomeLibraryFolderPageWithinSourceAsync(
                _actor, receipt.FilesStoreId, receipt.ParentFolderId, "", cursor, _alive, Own, Retain, token))).ConfigureAwait(false);
            if (page.StoreID != receipt.FilesStoreId || page.ParentID != receipt.ParentFolderId || page.IsTrash ||
                storeRevision is not null && page.StoreRevision != storeRevision)
                throw new InvalidDataException("The created board's current Files parent or store changed before it could be opened.");
            storeRevision ??= page.StoreRevision;
            pages.Add(page);
            foreach (var listedRow in page.Items)
            {
                if (listedRow.Id.Value != receipt.FileId) continue;
                if (createdRow is not null)
                    throw new InvalidDataException("The original Files folder repeated the created board identity.");
                createdRow = listedRow; createdPage = page;
            }
            cursor = page.Next;
            if (cursor is not null && (cursor.StoreID != receipt.FilesStoreId || cursor.StoreRevision != storeRevision ||
                cursor.ParentID != receipt.ParentFolderId || cursor.OriginalActor != _actor || cursor.IsTrash ||
                cursor.Search != "" || !offsets.Add(cursor.Offset)))
                throw new InvalidDataException("The created board's Files cursor changed or repeated.");
        } while (cursor is not null && createdRow is null);

        foreach (var page in pages)
            await source.AwaitAsync(source.Invoke(() => ValidatePageAsync(page, token))).ConfigureAwait(false);
        await source.AwaitAsync(source.Invoke(() => createOwner.RevalidateCreatedBoardWithinSourceAsync(
            receipt, Own, Retain, token))).ConfigureAwait(false);
        await RequireOwnerAsync(source, token).ConfigureAwait(false);
        var selectedRow = createdRow ?? throw new FileNotFoundException("The created board is not visible in its current Files parent.");
        var pageForRow = createdPage!;
        if (selectedRow.Kind != HostedItemKind.File || selectedRow.ParentId?.Value != receipt.ParentFolderId ||
            selectedRow.CurrentRevisionId?.Value != receipt.FilesCasRevisionId ||
            selectedRow.CurrentRevisionId?.Value != receipt.FileRevisionId || selectedRow.SizeBytes != receipt.SizeBytes ||
            !StringComparer.OrdinalIgnoreCase.Equals(selectedRow.ContentHash, receipt.ContentSha256) ||
            !FilesNativeBrowserService.IsBoardDocumentCandidate(selectedRow))
            throw new UnauthorizedAccessException("The current Files row no longer matches the exact created board receipt.");

        var boardRead = _boardRead ?? throw new NotSupportedException("The original Home Files board reader is unavailable.");
        NativeHomeFilesBoardReadLease? readLease = null;
        Guid? preparedWrite = null;
        var transferred = false;
        try
        {
            readLease = await source.AwaitAsync(source.Invoke(() => boardRead.ReadSelectedAsync(pageForRow, selectedRow,
                _alive, _ownerCurrent, Own, Retain, source, token))).ConfigureAwait(false);
            if (!readLease.IsOpen || readLease.StoreId != receipt.FilesStoreId || readLease.ParentFolderId != receipt.ParentFolderId ||
                readLease.FileId != receipt.FileId || readLease.RevisionId != receipt.FilesCasRevisionId)
                throw new UnauthorizedAccessException("The Home read lease differs from the exact created Files revision.");
            var document = HavenBoardDocumentCodec.Deserialize(readLease.Bytes);
            if (document.DocumentId != receipt.DocumentId)
                throw new InvalidDataException("The created board bytes do not match the Files owner's canonical document identity.");

            var writer = _boardWrite ?? throw new NotSupportedException("The original Home Files board writer is unavailable.");
            var presentReview = _boardWriteReview ?? throw new NotSupportedException("The original Home Board write review is unavailable.");
            var originalScope = _boardWriteScope ?? throw new NotSupportedException("The original Home Board write scope is unavailable.");
            var retain = _boardWriteRetain ?? throw new NotSupportedException("The original Home Board write task owner is unavailable.");
            preparedWrite = writer.RegisterPrepared(pageForRow, selectedRow, readLease, _alive, presentReview,
                "home-library-created-board:" + receipt.FileId.ToString("D"), originalScope, retain);
            var address = BoardsBrowseAppContentAddress.ForWritableFile(receipt.FilesStoreId, receipt.ParentFolderId,
                receipt.FileId, readLease.RevisionId, preparedWrite.Value);
            preparedWrite = null;
            readLease = null; // Ownership transferred into the registered Home writable lease.
            transferred = true;
            return address;
        }
        finally
        {
            if (!transferred && readLease is not null) await readLease.DisposeAsync().ConfigureAwait(false);
            if (preparedWrite is { } id && _boardWrite is { } write) await write.DiscardPreparedAsync(id).ConfigureAwait(false);
        }
    }

    private Task ValidatePageAsync(FilesNativeBrowserPage page, CancellationToken token) =>
        _browser.RevalidateOriginalHomeLibraryPageWithinSourceAsync(page, _actor, _alive, Own, Retain, token);
    private static FilesOriginalBoardDocumentCreateSettlementException? FindCreateSettlementFailure(Exception error)
    {
        if (error is FilesOriginalBoardDocumentCreateSettlementException settlement) return settlement;
        if (error is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                if (FindCreateSettlementFailure(inner) is { } found) return found;
        }
        return error.InnerException is { } cause ? FindCreateSettlementFailure(cause) : null;
    }
    private static Task<T> StartSettlementAwareTask<T>(CloudflareOriginalTaskLedger source,
        Func<Action<Task>, Task<T>> start, bool cancellationIsPending = false)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(start);
        Task<T>? actual = null;
        source.Invoke(() => { actual = start(task => source.Track(ObserveCreateSettlementAsync(task, cancellationIsPending))); return true; });
        return actual ?? throw new InvalidOperationException("The original Files board-create owner returned no retained operation.");
    }
    private static async Task ObserveCreateSettlementAsync(Task actual, bool cancellationIsPending)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception error) when (FindCreateSettlementFailure(error) is not null)
        {
            // The caller awaits the exact task and converts only the owner's typed outcome to
            // data. This successful wrapper preserves source custody without making a known
            // settlement state poison the enclosing Home Library source operation.
        }
        catch (OperationCanceledException) when (cancellationIsPending)
        {
            // The exact audit attempt remains owner-retained and the caller reports pending;
            // cancellation never causes the create effect to be replayed.
        }
    }
    private void DemandOwner() { if (!_alive()) throw new ObjectDisposedException(nameof(NativeHomeFilesLibrarySource)); }
    private async Task RequireOwnerAsync(CloudflareOriginalTaskLedger source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); DemandOwner();
        if (!await source.AwaitAsync(source.Invoke(() => _ownerCurrent(token))).ConfigureAwait(false))
            throw Refuse("LibraryProfileChanged", "Reopen Library for the actual Home profile and services.");
        token.ThrowIfCancellationRequested(); DemandOwner();
    }
    private CloudflareOriginalTaskLedger Current()
    { lock (_sync) return _currentOriginal ?? throw new InvalidOperationException("Retain the actual serialized Files Library source."); }
    private void Own(Action body) => Current().Invoke(() => { DemandOwner(); body(); return true; });
    private void Retain(Task actual) { _ = Current().Track(actual); }
    private HomeFeatureProviderException Refuse(string code, string message)
    { var error = new HomeFeatureProviderException(code, message); _refusals.Add(error, new()); return error; }
    public bool IsExpectedRefusal(Exception error) => error is AggregateException group
        ? group.InnerExceptions.Count != 0 && group.InnerExceptions.All(IsExpectedRefusal) : _refusals.TryGetValue(error, out _);
    private Task<T> RunAsync<T>(Func<CloudflareOriginalTaskLedger, CancellationToken, Task<T>> body, CancellationToken token)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_retiring, this); DemandOwner();
            _operations.RemoveAll(item => item.Task.IsCompletedSuccessfully || item.Task.IsFaulted && IsExpectedRefusal(item.Task.Exception!));
            if (_operations.Count >= 128) throw new InvalidOperationException("Settle the original Files Library failures first.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = Run(start.Task); _operations.Add(new(original, token)); start.SetResult(); return Expose(original);
        }
        async Task<T> Run(Task start)
        {
            await start.ConfigureAwait(false);
            var gate = _gate.WaitAsync(token); await gate.ConfigureAwait(false);
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(this); source.BindOriginalCallerCallback(_parent);
            lock (_sync) _currentOriginal = source;
            try { return await source.RunToOriginalSettlementAsync(async () =>
                { await RequireOwnerAsync(source, token).ConfigureAwait(false); return await body(source, token).ConfigureAwait(false); }).ConfigureAwait(false); }
            finally { lock (_sync) _currentOriginal = null; _gate.Release(); }
        }
    }
    private async Task<T> Expose<T>(Task<T> original)
    {
        try { return await original.ConfigureAwait(false); }
        catch
        {
            var error = original.Exception;
            if (error is not null && IsExpectedRefusal(error))
            { Exception first = error; while (first is AggregateException group) first = group.InnerExceptions[0]; System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw(); }
            throw; // Raw/mixed/faulted cancellation remains the actual retained source failure.
        }
    }
    public ValueTask DisposeAsync()
    {
        DemandExternalOriginalJoin();
        lock (_sync)
        {
            if (_close is not null) return new(_close);
            _retiring = true; var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseAsync(start.Task, _operations.ToArray()); start.SetResult(); return new(_close);
        }
    }
    private async Task CloseAsync(Task start, Operation[] operations)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        foreach (var item in operations)
            try { await item.Task.ConfigureAwait(false); }
            catch (OperationCanceledException cause) when (item.Task.IsCanceled && item.Token.IsCancellationRequested && cause.CancellationToken == item.Token) { }
            catch (Exception cause) { var error = item.Task.Exception ?? cause; if (!IsExpectedRefusal(error)) failures.Add(error); }
        _entries.Clear(); _gate.Dispose();
        if (failures.Count != 0) throw new AggregateException("Original Files Library sources failed.", failures);
    }
}
