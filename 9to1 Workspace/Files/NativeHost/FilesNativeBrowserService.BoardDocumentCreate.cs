using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService
{
    private readonly object _boardDocumentCreateOwnerGate = new();
    private readonly object _boardDocumentCreateOwnerCreationKey = new();
    private BoardDocumentCreateOwner? _boardDocumentCreateOwner;

    /// <summary>Bind the single original Files publisher to its create-only board issuer.</summary>
    public BoardDocumentCreateOwner CreateOriginalBoardDocumentCreateOwner(
        IFilesOriginalBoardDocumentCreatePhysicalOwner actualPhysicalOwner)
    {
        ArgumentNullException.ThrowIfNull(actualPhysicalOwner);
        lock (_boardDocumentCreateOwnerGate)
        {
            if (_boardDocumentCreateOwner is { } existing)
            {
                if (!ReferenceEquals(existing.PhysicalOwner, actualPhysicalOwner))
                    throw new UnauthorizedAccessException("The original Files board creator cannot be rebound to another physical owner.");
                return existing;
            }
            var created = new BoardDocumentCreateOwner(this, actualPhysicalOwner,
                _mutationBroker ?? throw new NotSupportedException("The original Home Files create broker is unavailable."),
                _boardDocumentCreateOwnerCreationKey);
            _boardDocumentCreateOwner = created;
            return created;
        }
    }

    public sealed class BoardDocumentCreateOwner : IFilesOriginalBoardDocumentCreateSource
    {
        private sealed class CreateOperation
        {
            internal BoardDocumentCreateOwner Owner = null!;
            internal FilesNativeBrowserPage Page = null!;
            internal HostedItemMetadata Folder = null!;
            internal NativeFilesWorkspace Workspace = null!;
            internal AuthenticatedResourceActor Actor = null!;
            internal Func<bool> Lifetime = null!;
            internal string SessionId = string.Empty;
            internal string OperationKey = string.Empty;
            internal string DisplayName = string.Empty;
            internal string PayloadHash = string.Empty;
            internal byte[] Payload = [];
            internal Guid DocumentId;
            internal Guid FileId;
            internal Guid FileRevisionId;
            internal ResourceScope[] Scopes = [];
            internal JsonElement Arguments;
            internal IOriginalCanonicalWriteContext? WriteContext;
            internal HomePermissionAuthorization? Approval;
            internal HomeResourceExecutionCapability? Capability;
            internal HomeClaimedResourceCommitFence? Fence;
            internal FilesWorkspaceDirectoryResolver.FilesOriginalBindingCommitLease? Mapping;
            internal IFilesOriginalBoardDocumentMaterialization? Materialization;
            internal FilesResult<FilesRevision>? ActualResult;
            internal HomeExecutionOutcome? ActualOutcome;
            internal FilesOriginalBoardDocumentCreateReceipt? Receipt;
            internal Task<FilesOriginalBoardDocumentCreateReceipt>? Task;
            internal bool Claimed, Validated, AuditRecorded, Retired, ExactCommitReceipt;
            internal Action<Action> Scope = null!;
            internal Action<Task> Retain = null!;
            internal FilesOriginalReadSourceScope Source = null!;
            internal readonly object Gate = new();
        }

        private sealed class Destination(BoardDocumentCreateOwner owner, CreateOperation operation)
            : IFilesOriginalBoardDocumentCreateDestination
        {
            internal BoardDocumentCreateOwner Owner { get; } = owner;
            internal CreateOperation Operation { get; } = operation;
            internal FilesOriginalBoardDocumentCreateDescription Description = null!;
        }

        private readonly FilesNativeBrowserService _browser;
        private readonly HomeResourceOperationBroker _broker;
        private readonly IFilesOriginalBoardDocumentCreatePhysicalOwner _physicalOwner;
        private readonly ConditionalWeakTable<IFilesOriginalBoardDocumentCreateDestination, CreateOperation> _issued = new();
        private readonly ConditionalWeakTable<FilesOriginalBoardDocumentCreateReceipt, CreateOperation> _receipts = new();
        private readonly Dictionary<string, CreateOperation> _operations = new(StringComparer.Ordinal);
        private readonly List<Task<FilesOriginalBoardDocumentCreateReceipt>> _active = [];
        private readonly AsyncLocal<CreateOperation?> _executing = new();
        private readonly object _gate = new();
        private bool _retiring;
        private Task? _close;

        internal BoardDocumentCreateOwner(FilesNativeBrowserService browser,
            IFilesOriginalBoardDocumentCreatePhysicalOwner physicalOwner,
            HomeResourceOperationBroker broker, object creationKey)
        {
            ArgumentNullException.ThrowIfNull(browser); ArgumentNullException.ThrowIfNull(physicalOwner);
            ArgumentNullException.ThrowIfNull(broker);
            if (!ReferenceEquals(creationKey, browser._boardDocumentCreateOwnerCreationKey))
                throw new UnauthorizedAccessException("Create the original Files board creator through its owning browser.");
            _browser = browser; _physicalOwner = physicalOwner; _broker = broker;
            if (physicalOwner.OriginalBoardDocumentCreateSource is { } current && !ReferenceEquals(current, this))
                throw new UnauthorizedAccessException("Another original Files board-create issuer already owns the physical source.");
            physicalOwner.BindOriginalBoardDocumentCreateSource(this);
        }

        public IFilesOriginalBoardDocumentCreatePhysicalOwner PhysicalOwner => _physicalOwner;
        public Task? OriginalClose { get { lock (_gate) return _close; } }
        public void RequestRetirement() { lock (_gate) _retiring = true; }
        public void DemandExternalOriginalRetirementJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);

        public Task CloseAndDrainAsync()
        {
            DemandExternalOriginalRetirementJoin();
            TaskCompletionSource? start = null;
            Task original;
            lock (_gate)
            {
                if (_close is not null) return _close;
                _retiring = true;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                original = _close = DrainAsync(start.Task, _active.ToArray());
            }
            start.SetResult();
            return original;
        }

        private async Task DrainAsync(Task start, Task<FilesOriginalBoardDocumentCreateReceipt>[] active)
        {
            await start.ConfigureAwait(false);
            var errors = new List<Exception>();
            foreach (var task in active)
                try { await task.ConfigureAwait(false); }
                catch (Exception error) { CanonicalSqliteOriginalStoreOwner.Capture(errors, task, error); }
            lock (_gate)
                foreach (var operation in _operations.Values) CryptographicOperations.ZeroMemory(operation.Payload);
            CanonicalSqliteOriginalStoreOwner.Throw(errors);
        }

        /// <summary>Creates one new canonical .9to1board file from the exact current selected
        /// folder row. Home approval and a claimed write fence are required before bytes or Files
        /// metadata are published. The returned receipt is emitted only after terminal audit.</summary>
        public Task<FilesOriginalBoardDocumentCreateReceipt> CreateBoardDocumentWithinSourceAsync(
            FilesNativeBrowserPage samePage, HostedItemMetadata sameFolderRow,
            AuthenticatedResourceActor actualActor, string displayName,
            ReadOnlyMemory<byte> validatedDocument, Func<bool> originalLifetime,
            Func<HomePermissionAuthorization, CancellationToken, Task> presentReview,
            string sessionId, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(samePage); ArgumentNullException.ThrowIfNull(sameFolderRow);
            ArgumentNullException.ThrowIfNull(actualActor); ArgumentNullException.ThrowIfNull(originalLifetime);
            ArgumentNullException.ThrowIfNull(presentReview); ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(retain);
            token.ThrowIfCancellationRequested();
            if (!_browser._originalPages.TryGetValue(samePage, out var issuedPage) || issuedPage.Actor != actualActor ||
                issuedPage.Workspace.Configuration.StoreId != samePage.StoreID || samePage.IsTrash || samePage.StoreID == Guid.Empty ||
                samePage.ParentID != sameFolderRow.ParentId?.Value ||
                sameFolderRow.Kind != HostedItemKind.Folder || sameFolderRow.CurrentRevisionId is not { } folderRevision ||
                !samePage.Items.Any(row => ReferenceEquals(row, sameFolderRow)) || !originalLifetime())
                throw new UnauthorizedAccessException("Select the same current folder row from the original Files page.");
            if (actualActor.AccountId is not null || actualActor.OrganisationId is not null ||
                string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 256)
                throw new UnauthorizedAccessException("Board creation requires a current personal Home actor and bounded session identity.");
            var name = ValidateDisplayName(displayName);
            if (validatedDocument.Length is <= 0 or > MaximumBoardDocumentBytes)
                throw new InvalidDataException("The new board document must be nonempty and within the supported Files size limit.");
            var payload = validatedDocument.ToArray();
            var document = HavenBoardDocumentCodec.Deserialize(payload);
            using (var header = JsonDocument.Parse(payload))
                if (document.SchemaVersion != HavenBoardDocument.CurrentSchemaVersion || document.DocumentId == Guid.Empty ||
                    header.RootElement.EnumerateObject().Count(property => property.Name == "schemaVersion") != 1 ||
                    header.RootElement.GetProperty("schemaVersion").GetInt32() != HavenBoardDocument.CurrentSchemaVersion)
                    throw new InvalidDataException("Create a new board from a validated current-schema document with a stable identity.");
            var digest = Convert.ToHexString(SHA256.HashData(payload));
            var key = CreateKey(samePage.StoreID, sameFolderRow.Id.Value, folderRevision.Value, document.DocumentId, name, digest, sessionId);
            var operation = new CreateOperation
            {
                Owner = this, Page = samePage, Folder = sameFolderRow, Workspace = issuedPage.Workspace, Actor = actualActor,
                Lifetime = originalLifetime, SessionId = sessionId, OperationKey = key,
                DisplayName = name, PayloadHash = digest,
                Payload = payload, DocumentId = document.DocumentId, FileId = Guid.NewGuid(),
                FileRevisionId = Guid.NewGuid(), Scopes = [new("files.item", sameFolderRow.Id.ToString(), folderRevision.ToString(), ResourceAccess.Write)],
                Scope = scope, Retain = retain
            };
            var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
            originals.BindOriginalCallerCallback(scope);
            operation.Source = new FilesOriginalReadSourceScope(body => originals.Invoke(() => { body(); return true; }),
                actual => { originals.Track(actual); retain(actual); });
            TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<FilesOriginalBoardDocumentCreateReceipt> actual;
            Task<FilesOriginalBoardDocumentCreateReceipt>? existingTask = null;
            CreateOperation? retryOperation = null;
            lock (_gate)
            {
                if (_retiring) { CryptographicOperations.ZeroMemory(payload); throw new ObjectDisposedException(nameof(BoardDocumentCreateOwner)); }
                if (_operations.TryGetValue(key, out var existing))
                {
                    if (existing.Task is { IsCompleted: false } pending)
                    { existingTask = pending; CryptographicOperations.ZeroMemory(payload); }
                    else if (existing.ExactCommitReceipt && existing.Receipt is not null && !existing.AuditRecorded)
                    { retryOperation = existing; CryptographicOperations.ZeroMemory(payload); }
                    else if (existing.ExactCommitReceipt && existing.Receipt is { } committed && existing.AuditRecorded)
                    {
                        if (!_receipts.TryGetValue(committed, out _)) _receipts.Add(committed, existing);
                        existingTask = Task.FromResult(committed);
                        CryptographicOperations.ZeroMemory(payload);
                    }
                    else if (!existing.Claimed)
                        _operations.Remove(key); // denied/unclaimed review can be deliberately retried
                    else
                    {
                        existingTask = existing.Task ?? throw new InvalidOperationException("The original Files create task is not available for safe replay.");
                        CryptographicOperations.ZeroMemory(payload);
                    }
                }
                if (existingTask is null && retryOperation is null)
                {
                    if (_operations.Values.Any(item => item.ActualOutcome is not null && !item.AuditRecorded))
                    {
                        CryptographicOperations.ZeroMemory(payload);
                        throw new InvalidOperationException("Settle the existing Home Files board-create audit before another board.");
                    }
                    foreach (var settled in _operations.Where(pair => pair.Value.AuditRecorded &&
                                 pair.Value.Task?.IsCompleted == true).Select(pair => pair.Key).Take(32).ToArray())
                        _operations.Remove(settled);
                    if (_operations.Count >= 128)
                    {
                        CryptographicOperations.ZeroMemory(payload);
                        throw new InvalidOperationException("Settle retained original Files board creations before starting another.");
                    }
                    _active.RemoveAll(task => task.IsCompletedSuccessfully || task.IsFaulted || task.IsCanceled);
                    if (_active.Count >= 32)
                    { CryptographicOperations.ZeroMemory(payload); throw new InvalidOperationException("Settle original Files board creations before starting another."); }
                    actual = Run(start.Task);
                    operation.Task = actual;
                    _operations.Add(key, operation); _active.Add(actual);
                }
                else actual = null!;
            }
            if (existingTask is not null) { retain(existingTask); return existingTask; }
            if (retryOperation is not null)
            {
                var retryTask = RetryAuditAsync(retryOperation, scope, retain, token);
                retain(retryTask);
                return retryTask;
            }
            try { operation.Source.Run(() => retain(actual)); }
            finally { start.SetResult(); }
            return actual;

            async Task<FilesOriginalBoardDocumentCreateReceipt> Run(Task began)
            {
                await began.ConfigureAwait(false);
                var previous = _executing.Value; _executing.Value = operation;
                try
                {
                    var receipt = await originals.RunToOriginalSettlementAsync(() => ExecuteAsync(operation, presentReview, token)).ConfigureAwait(false);
                    lock (_gate) _receipts.Add(receipt, operation);
                    return receipt;
                }
                finally { CryptographicOperations.ZeroMemory(operation.Payload); _executing.Value = previous; }
            }
        }

        private async Task<FilesOriginalBoardDocumentCreateReceipt> RetryAuditAsync(CreateOperation operation,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            var receipt = operation.Receipt ?? throw new InvalidOperationException("No exact Files board-create receipt is available to settle.");
            var source = FilesOriginalParentSourceCallbacks.Create(scope, retain);
            var settled = await source.Observe(() => _broker.RetryCompletionAuditAsync(
                operation.Capability ?? throw new InvalidOperationException("The original Home create capability is unavailable."), token)).ConfigureAwait(false);
            if (!settled.Succeeded) throw new InvalidOperationException("Files created the board, but Home has not settled the create audit: " + settled.Message);
            operation.AuditRecorded = true;
            lock (_gate)
                if (!_receipts.TryGetValue(receipt, out _)) _receipts.Add(receipt, operation);
            return receipt;
        }

        /// <summary>UI eligibility only: returns true when this exact current issued folder row
        /// is the configured Boards root. It grants no create authority; Create repeats the check
        /// and obtains a fresh Home approval and claimed commit fence.</summary>
        public async Task<bool> CanCreateInFolderWithinSourceAsync(FilesNativeBrowserPage samePage,
            HostedItemMetadata sameFolderRow, AuthenticatedResourceActor actualActor,
            Func<bool> originalLifetime, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(samePage); ArgumentNullException.ThrowIfNull(sameFolderRow);
            ArgumentNullException.ThrowIfNull(actualActor); ArgumentNullException.ThrowIfNull(originalLifetime);
            ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
            if (!_browser._originalPages.TryGetValue(samePage, out var issued) || issued.Actor != actualActor ||
                samePage.IsTrash || !samePage.Items.Any(row => ReferenceEquals(row, sameFolderRow)) ||
                sameFolderRow.Kind != HostedItemKind.Folder || samePage.ParentID != sameFolderRow.ParentId?.Value ||
                !originalLifetime())
                throw new UnauthorizedAccessException("Use the same current original Files page and selected folder row.");
            var source = FilesOriginalParentSourceCallbacks.Create(scope, retain);
            var workspace = await source.Observe(() => _browser.OriginalBrowserWorkspaces.GetOriginalCurrentWithinSourceAsync(
                samePage.StoreID, source, token)).ConfigureAwait(false);
            var actor = await source.Observe(() => _browser.OriginalBrowserActors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false);
            if (workspace?.Actor != actualActor || actor != actualActor ||
                !ReferenceEquals(workspace.Provider, issued.Workspace.Provider) ||
                !ReferenceEquals(workspace.Directories, issued.Workspace.Directories) || !originalLifetime())
                throw new UnauthorizedAccessException("The current Home actor or Files workspace changed.");
            var evidence = await source.Observe(() => workspace.Provider.GetStoreEvidenceAsync(
                samePage.StoreID, source.OriginalSynchronousScope, source.RetainOriginalTask, token)).ConfigureAwait(false);
            if (evidence.StoreId != samePage.StoreID || evidence.Revision != samePage.StoreRevision)
                throw new InvalidOperationException("The selected Files folder page is stale.");
            var current = await source.Observe(() => workspace.Provider.GetForOriginalStoreAsync(
                samePage.StoreID, sameFolderRow.Id, token)).ConfigureAwait(false);
            if (!current.IsSuccess || current.Value != sameFolderRow ||
                current.Value is not { Kind: HostedItemKind.Folder, CurrentRevisionId: not null })
                throw new InvalidOperationException("The selected Files folder row is no longer current.");
            if (!workspace.Configuration.AppFolders.TryGetValue("boards", out var configuredBoards)) return false;
            var after = await source.Observe(() => _browser.OriginalBrowserWorkspaces.GetOriginalCurrentWithinSourceAsync(
                samePage.StoreID, source, token)).ConfigureAwait(false);
            var actorAfter = await source.Observe(() => _browser.OriginalBrowserActors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false);
            if (after?.Actor != actualActor || actorAfter != actualActor ||
                !ReferenceEquals(after.Provider, workspace.Provider) || !originalLifetime())
                throw new UnauthorizedAccessException("The Home actor or Files workspace changed during folder selection.");
            return sameFolderRow.Id == configuredBoards;
        }

        private async Task<FilesOriginalBoardDocumentCreateReceipt> ExecuteAsync(CreateOperation operation,
            Func<HomePermissionAuthorization, CancellationToken, Task> presentReview, CancellationToken token)
        {
            FilesOriginalBoardDocumentCreateReceipt? receipt = null;
            Exception? failure = null;
            var cleanup = new List<Exception>();
            try { receipt = await ExecuteCoreAsync(operation, presentReview, token).ConfigureAwait(false); }
            catch (Exception cause) { failure = cause; }
            var source = operation.Source;
            if (operation.Materialization is { } materialization)
                try { await source.ObserveVoid(() => materialization.CloseAndDrainAsync()).ConfigureAwait(false); }
                catch (Exception cause) { cleanup.Add(cause); }
            if (operation.Mapping is { } mapping)
                try { await source.ObserveVoid(() => mapping.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception cause) { cleanup.Add(cause); }
            if (operation.Fence is { } fence)
                try { await source.ObserveVoid(() => fence.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception cause) { cleanup.Add(cause); }
            try
            {
                if (operation.Claimed && operation.Capability is { } claimed)
                {
                    operation.ActualOutcome ??= operation.ExactCommitReceipt
                        ? new(HomePermissionRequestState.Succeeded, "FILES_BOARD_CREATED",
                            "The new board document was committed to the selected Files folder.", [new("files.item", operation.FileId.ToString("D"))])
                        : operation.ActualResult is { IsSuccess: false } rejected
                            ? new(HomePermissionRequestState.Failed, rejected.Error?.Code.ToString() ?? "FILES_BOARD_CREATE_FAILED",
                                rejected.Error?.Message ?? "Files rejected the new board document.", [])
                            : new(HomePermissionRequestState.PartiallyCompleted, "FILES_BOARD_CREATE_OUTCOME_UNKNOWN",
                                "The Files create effect is uncertain; do not replay it.", []);
                    var outcome = operation.ActualOutcome
                        ?? throw new InvalidOperationException("The Home board-create operation has no terminal outcome.");
                    var audit = await source.Observe(() => _broker.CompleteExecutionAsync(claimed,
                        outcome, CancellationToken.None)).ConfigureAwait(false);
                    operation.AuditRecorded = audit.Succeeded;
                    if (!audit.Succeeded) cleanup.Add(new InvalidOperationException(audit.Message));
                }
                else if (operation.Capability is { } unclaimed)
                    await source.Observe(() => _broker.AbortUnclaimedExecutionAsync(unclaimed, CancellationToken.None)).ConfigureAwait(false);
            }
            catch (Exception cause) { cleanup.Add(cause); }

            if (failure is not null)
            {
                if (cleanup.Count != 0) throw new AggregateException("The Files board create did not return a receipt and one or more settlement steps failed.", [failure, .. cleanup]);
                throw failure;
            }
            if (receipt is null) throw new InvalidOperationException("Files did not return the exact new-board receipt.");
            if (!operation.AuditRecorded)
                throw new InvalidOperationException("Files created the board, but Home has not settled its creation audit. Retry the same request without recreating the artifact.");
            // A confirmed Files receipt and settled Home audit remain authoritative even if
            // disposal reports an owner cleanup issue; the caller still revalidates before routing.
            return receipt;
        }

        private async Task<FilesOriginalBoardDocumentCreateReceipt> ExecuteCoreAsync(CreateOperation operation,
            Func<HomePermissionAuthorization, CancellationToken, Task> presentReview, CancellationToken token)
        {
            var source = operation.Source;
            async Task<NativeFilesWorkspace> RequireCurrentWorkspace(bool pageMustMatch, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                var captured = operation.Workspace;
                var current = await source.Observe(() => _browser.OriginalBrowserWorkspaces.GetOriginalCurrentWithinSourceAsync(
                    operation.Page.StoreID, source, ct)).ConfigureAwait(false);
                var actor = await source.Observe(() => _browser.OriginalBrowserActors.GetCurrentAsync(ct).AsTask()).ConfigureAwait(false);
                if (current?.Actor != operation.Actor || actor != operation.Actor ||
                    captured is not null && (!ReferenceEquals(current.Provider, captured.Provider) ||
                        !ReferenceEquals(current.Directories, captured.Directories) ||
                        !ReferenceEquals(current.Materializations, captured.Materializations)) ||
                    !source.Invoke(operation.Lifetime))
                    throw new UnauthorizedAccessException("The original Files/Home actor or create lifetime changed.");
                if (pageMustMatch)
                {
                    var evidence = await source.Observe(() => current.Provider.GetStoreEvidenceAsync(operation.Page.StoreID,
                        source.OriginalSynchronousScope, source.RetainOriginalTask, ct)).ConfigureAwait(false);
                    if (evidence.StoreId != operation.Page.StoreID || evidence.Revision != operation.Page.StoreRevision)
                        throw new InvalidOperationException("The displayed Files folder changed before board creation.");
                }
                return current;
            }

            var workspace = await RequireCurrentWorkspace(true, token).ConfigureAwait(false);
            operation.Workspace = workspace;
            var folderResult = await source.Observe(() => workspace.Provider.GetForOriginalStoreAsync(
                operation.Page.StoreID, operation.Folder.Id, token)).ConfigureAwait(false);
            if (!folderResult.IsSuccess || folderResult.Value != operation.Folder ||
                folderResult.Value is not { Kind: HostedItemKind.Folder, CurrentRevisionId: { } parentRevision })
                throw new InvalidOperationException("The selected Files create folder changed or is unavailable.");
            var currentFolder = folderResult.Value!;
            var resolver = _browser._originalFolders?.BorrowOriginalItemReadIssuer()
                ?? throw new NotSupportedException("The original Files board create resolver is unavailable.");
            operation.WriteContext = await source.Observe(() => resolver.CaptureOriginalBoardDocumentCreateAsync(
                workspace, currentFolder, operation.Lifetime, source, token)).ConfigureAwait(false);
            operation.Scopes = [new ResourceScope("files.item", currentFolder.Id.ToString(), parentRevision.ToString(), ResourceAccess.Write)];
            async ValueTask RequireWrite(CancellationToken ct)
            {
                var writeContext = operation.WriteContext
                    ?? throw new UnauthorizedAccessException("The original Files board-create folder observation is unavailable.");
                if (!source.Invoke(operation.Lifetime) || !_browser.OriginalBrowserResources.IsOriginalWriteContextForActor(
                    operation.Actor, writeContext, "files.board.create", operation.Scopes) ||
                    await source.Observe(() => _browser.OriginalBrowserResources.AuthorizeOriginalWriteForActorAsync(
                        operation.Actor, writeContext, "files.board.create", operation.Scopes, ct).AsTask()).ConfigureAwait(false) != operation.Actor)
                    throw new UnauthorizedAccessException("Current Home WRITE does not authorize creating a board in this Files folder.");
            }
            await RequireWrite(token).ConfigureAwait(false);

            var anchor = workspace.Configuration.AppFolders.TryGetValue("boards", out var configuredAnchor)
                ? configuredAnchor : throw new NotSupportedException("This Files workspace has no registered Boards content anchor.");
            var rootResult = await source.Observe(() => workspace.Provider.GetForOriginalStoreAsync(
                operation.Page.StoreID, anchor, token)).ConfigureAwait(false);
            if (!rootResult.IsSuccess || rootResult.Value is not { Kind: HostedItemKind.Folder, CurrentRevisionId: not null } rootFolder ||
                currentFolder.Id != rootFolder.Id || rootFolder.LocationId != currentFolder.LocationId)
                throw new UnauthorizedAccessException("Choose the current registered Boards folder for this new board document.");
            var profile = Guid.Parse(operation.Actor.ProfileId);
            var binding = await source.Observe(() => workspace.Directories.ResolveOriginalRegisteredFolderAsync(
                profile, "boards", anchor, workspace.Provider, operation.Page.StoreID, true,
                ct => new ValueTask(RequireWrite(ct).AsTask()), token)).ConfigureAwait(false);
            if (!binding.IsSuccess || binding.Value is not { } originalBinding || originalBinding.FolderId != anchor ||
                originalBinding.ProfileId != profile || originalBinding.LocationId != currentFolder.LocationId)
                throw new UnauthorizedAccessException("The registered Files Boards content anchor is unavailable for this Home profile.");

            var arguments = JsonSerializer.SerializeToElement(new
            {
                operationId = operation.FileRevisionId,
                storeId = operation.Page.StoreID,
                parentId = currentFolder.Id.Value,
                expectedParentRevision = parentRevision.Value,
                fileId = operation.FileId,
                fileRevisionId = operation.FileRevisionId,
                documentId = operation.DocumentId,
                displayName = operation.DisplayName,
                sizeBytes = operation.Payload.Length,
                sha256 = operation.PayloadHash,
                effect = "create-board-document"
            });
            operation.Arguments = arguments;
            var approval = await source.Observe(() => _broker.AuthorizeForActorAsync(
                operation.Actor, "files", "files.board.create", operation.Scopes, arguments,
                $"Create board ‘{operation.DisplayName}’ in the selected Files folder.", null,
                operation.SessionId, token)).ConfigureAwait(false);
            operation.Approval = approval;
            if (approval.RequestId.Length == 0)
                throw new InvalidOperationException("Home returned no retained Files board-create request.");
            if (approval.State == HomePermissionRequestState.PendingApproval)
                await source.ObserveVoid(() => presentReview(approval, token)).ConfigureAwait(false);
            approval = await source.Observe(() => _broker.GetAuthorizationAsync(approval.RequestId, token)).ConfigureAwait(false);
            operation.Approval = approval;
            if (!approval.IsAllowed) throw new UnauthorizedAccessException(approval.Message);

            var reviewed = await source.Observe(() => workspace.Provider.GetForOriginalStoreAsync(
                operation.Page.StoreID, operation.Folder.Id, token)).ConfigureAwait(false);
            if (!reviewed.IsSuccess || reviewed.Value is not { Kind: HostedItemKind.Folder, CurrentRevisionId: { } reviewedRevision } ||
                reviewed.Value != currentFolder || reviewedRevision != parentRevision ||
                await RequireCurrentWorkspace(true, token).ConfigureAwait(false) is not { } afterReview ||
                !ReferenceEquals(afterReview.Provider, workspace.Provider))
                throw new InvalidOperationException("The selected Files folder or Home actor changed while review was open.");
            await RequireWrite(token).ConfigureAwait(false);
            operation.Capability = await source.Observe(() => _broker.BeginExecutionCapabilityAsync(
                approval.RequestId, arguments, token)).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Home has not admitted this exact Files board create request.");
            var capability = operation.Capability;
            var claim = await source.Observe(() => _broker.ClaimExecutionObservedAsync(capability,
                "files", "files.board.create", operation.Scopes, arguments, token)).ConfigureAwait(false);
            if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != operation.Actor)
                throw new UnauthorizedAccessException("Home did not claim the same original Files board-create request.");
            operation.Claimed = true;
            operation.Fence = await source.Observe(() => _browser.OriginalBrowserWorkspaces.CaptureBrowserCommitFenceWithinOriginalSourceAsync(
                workspace, _broker, capability, operation.Lifetime, source.OriginalSynchronousScope,
                source.RetainOriginalTask, token, actual => operation.Fence = actual).AsTask()).ConfigureAwait(false);
            var exactFence = operation.Fence ?? throw new InvalidOperationException("Home returned no retained Files create fence.");
            operation.Mapping = await source.Observe(() => workspace.Directories.AcquireOriginalBindingCommitLeaseAsync(
                originalBinding, workspace.Provider, token)).ConfigureAwait(false);
            if (operation.Mapping?.IsHeld != true || !await source.Observe(() => exactFence.ValidateAsync(token).AsTask()).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The exact Home claim or registered Files Boards anchor is no longer current.");
            operation.Validated = true;

            var destination = IssueDestination(operation, originalBinding.DirectoryPath, currentFolder);
            operation.Materialization = await source.ObserveProduct(() => _physicalOwner.MaterializeOriginalBoardDocumentCreateWithinSourceAsync(
                destination, operation.Payload, source.OriginalSynchronousScope, source.RetainOriginalTask, token),
                actual => operation.Materialization = actual).ConfigureAwait(false);
            var exactMaterialization = operation.Materialization
                ?? throw new InvalidOperationException("The original physical Files owner returned no board-create materialization.");
            if (!_physicalOwner.IsIssuedOriginalBoardDocumentCreateMaterialization(exactMaterialization) ||
                exactMaterialization.OriginalRecord is not { } materialized ||
                materialized.FilesStoreId != operation.Page.StoreID || materialized.ParentFolderId != currentFolder.Id.Value ||
                materialized.FileId != operation.FileId || materialized.ExpectedRevisionId != Guid.Empty ||
                materialized.NewRevisionId != operation.FileRevisionId || materialized.DocumentId != operation.DocumentId ||
                materialized.SizeBytes != operation.Payload.Length || materialized.OriginalActor != operation.Actor ||
                !StringComparer.OrdinalIgnoreCase.Equals(materialized.Sha256, operation.PayloadHash))
                throw new InvalidDataException("The original physical Files owner returned another board-create materialization.");
            await source.ObserveVoid(() => _physicalOwner.RevalidateOriginalBoardDocumentCreateWithinSourceAsync(
                exactMaterialization, source.OriginalSynchronousScope, source.RetainOriginalTask, token)).ConfigureAwait(false);
            await RequireWrite(token).ConfigureAwait(false);
            var commit = new FilesUploadedContent(new(operation.FileId), currentFolder.Id, operation.DisplayName,
                "application/vnd.9to1.board+json", new(operation.FileRevisionId), null, operation.Actor.ActorId,
                DateTimeOffset.UtcNow, operation.Payload.LongLength, operation.PayloadHash,
                materialized.RelativeContentReference);
            var guard = new FilesCommitAuthorityGuard(operation.Actor.ActorId, async ct =>
            {
                DemandOriginalFilesBoardDocumentCreateCommit(destination);
                _physicalOwner.DemandOriginalBoardDocumentCreateCommit(exactMaterialization);
                return await source.Observe(() => exactFence.ValidateAsync(ct).AsTask()).ConfigureAwait(false);
            });
            var created = workspace.Provider.CommitUploadedContentAsync(commit,
                [new(currentFolder.Id, parentRevision)], operation.Page.StoreID, guard, token);
            try { operation.ActualResult = await source.Observe(() => created).ConfigureAwait(false); }
            catch when (created.IsFaulted) { throw created.Exception!; }
            if (operation.ActualResult is not { IsSuccess: true, Value: { } revision } ||
                revision.ItemId.Value != operation.FileId || revision.Id.Value != operation.FileRevisionId ||
                revision.ParentRevisionId is not null || revision.OwningAppId != "files" ||
                revision.OwningAppRevisionId != revision.Id.ToString())
                throw new InvalidOperationException(operation.ActualResult?.Error?.Message ?? "Files did not return the exact committed new-board receipt.");
            operation.ExactCommitReceipt = true;
            operation.Receipt = new(operation.Page.StoreID, currentFolder.Id.Value, operation.FileId,
                revision.Id.Value, revision.Id.Value, revision.OwningAppRevisionId!, operation.DocumentId,
                operation.DisplayName, operation.PayloadHash, operation.Payload.Length, operation.Actor);

            return operation.Receipt ?? throw new InvalidOperationException("Files did not retain the exact create receipt.");
        }

        public bool IsIssuedOriginalBoardDocumentCreateDestination(IFilesOriginalBoardDocumentCreateDestination originalDestination)
            => originalDestination is Destination destination && ReferenceEquals(destination.Owner, this) &&
                _issued.TryGetValue(originalDestination, out var operation) && ReferenceEquals(operation, destination.Operation);

        public FilesOriginalBoardDocumentCreateDescription GetOriginalBoardDocumentCreateDescription(
            IFilesOriginalBoardDocumentCreateDestination originalDestination)
            => originalDestination is Destination destination && IsIssuedOriginalBoardDocumentCreateDestination(destination)
                ? destination.Description : throw new UnauthorizedAccessException("Use the same private Files board-create destination.");

        public void DemandOriginalFilesBoardDocumentCreateCommit(IFilesOriginalBoardDocumentCreateDestination originalDestination)
        {
            if (originalDestination is not Destination destination || !IsIssuedOriginalBoardDocumentCreateDestination(destination) ||
                !ReferenceEquals(_executing.Value, destination.Operation) || !destination.Operation.Claimed ||
                !destination.Operation.Validated || destination.Operation.Fence is null || destination.Operation.Mapping?.IsHeld != true ||
                destination.Operation.Retired || !destination.Operation.Lifetime())
                throw new UnauthorizedAccessException("Only the exact claimed Files board create with its held Home and folder-anchor fences may publish bytes.");
        }

        public bool IsIssuedCreateReceipt(FilesOriginalBoardDocumentCreateReceipt receipt)
            => receipt is not null && _receipts.TryGetValue(receipt, out var operation) &&
                ReferenceEquals(operation.Owner, this) && ReferenceEquals(operation.Receipt, receipt);

        /// <summary>Revalidates the same in-memory create receipt against the current actor,
        /// Files store, parent folder and exact current artifact/content revision before Home routes it.</summary>
        public async Task RevalidateCreatedBoardWithinSourceAsync(FilesOriginalBoardDocumentCreateReceipt sameReceipt,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(sameReceipt); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
            if (!IsIssuedCreateReceipt(sameReceipt) || !_receipts.TryGetValue(sameReceipt, out var operation))
                throw new UnauthorizedAccessException("Use the same privately issued Files board-create receipt.");
            var source = FilesOriginalParentSourceCallbacks.Create(scope, retain);
            var current = await source.Observe(() => _browser.OriginalBrowserWorkspaces.GetOriginalCurrentWithinSourceAsync(
                sameReceipt.FilesStoreId, source, token)).ConfigureAwait(false);
            if (current?.Actor != sameReceipt.OriginalActor || !ReferenceEquals(current.Provider, operation.Workspace.Provider) ||
                await source.Observe(() => _browser.OriginalBrowserActors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false) != sameReceipt.OriginalActor)
                throw new UnauthorizedAccessException("The Home actor or original Files store changed after board creation.");
            var parent = await source.Observe(() => current.Provider.GetForOriginalStoreAsync(sameReceipt.FilesStoreId,
                new(sameReceipt.ParentFolderId), token)).ConfigureAwait(false);
            var item = await source.Observe(() => current.Provider.GetForOriginalStoreAsync(sameReceipt.FilesStoreId,
                new(sameReceipt.FileId), token)).ConfigureAwait(false);
            var content = await source.Observe(() => current.Provider.GetCurrentArtifactContentForOriginalStoreAsync(
                sameReceipt.FilesStoreId, new(sameReceipt.FileId), item.IsSuccess ? item.Value!.CurrentRevisionId : null, token)).ConfigureAwait(false);
            if (!parent.IsSuccess || parent.Value is not { Kind: HostedItemKind.Folder } || !item.IsSuccess ||
                item.Value is not { Kind: HostedItemKind.File, ParentId: { } parentId, CurrentRevisionId: { } itemRevision } currentItem ||
                parentId.Value != sameReceipt.ParentFolderId || currentItem.Name != sameReceipt.DisplayName ||
                !FilesNativeBrowserService.IsBoardDocumentCandidate(currentItem) ||
                itemRevision.Value != sameReceipt.FilesCasRevisionId || !content.IsSuccess ||
                content.Value is not { } exactContent || exactContent.Revision.Id.Value != sameReceipt.FileRevisionId ||
                exactContent.Revision.OwningAppRevisionId != sameReceipt.OwningAppRevisionId ||
                !StringComparer.OrdinalIgnoreCase.Equals(exactContent.Revision.ContentHash, sameReceipt.ContentSha256) ||
                exactContent.Revision.SizeBytes != sameReceipt.SizeBytes ||
                !source.Invoke(operation.Lifetime))
                throw new InvalidOperationException("The created Files board is no longer the exact current receipt revision.");
        }

        private Destination IssueDestination(CreateOperation operation, string directoryPath,
            HostedItemMetadata folder)
        {
            var destination = new Destination(this, operation);
            var reference = ".9to1-board-" + operation.FileRevisionId.ToString("N") + ".content";
            destination.Description = new(directoryPath, operation.Page.StoreID, folder.Id.Value,
                operation.FileId, operation.FileRevisionId, operation.DocumentId, reference,
                operation.Payload.Length, operation.PayloadHash, operation.Actor);
            _issued.Add(destination, operation);
            return destination;
        }

        private static string ValidateDisplayName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var stem = name.EndsWith(".9to1board", StringComparison.OrdinalIgnoreCase)
                ? name[..^".9to1board".Length] : name;
            if (stem.Length is < 1 or > 120 || stem is "." or ".." ||
                stem.IndexOfAny(['/', '\\', '\0']) >= 0 || stem.Any(char.IsControl))
                throw new ArgumentException("Use a board name of 1–120 characters without path separators or control characters.", nameof(name));
            return stem + ".9to1board";
        }

        private static string CreateKey(Guid store, Guid folder, Guid revision, Guid document, string name, string hash, string session)
            => $"{store:D}\n{folder:D}\n{revision:D}\n{document:D}\n{name}\n{hash}\n{session}";
    }
}
