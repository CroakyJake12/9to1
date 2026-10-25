using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Files.NativeHost;

public sealed record FilesOriginalBoardDocumentWriteOutcome(bool Committed, Guid? NewRevisionId,
    string Code, string Message, bool Conflict = false);

public sealed partial class FilesNativeBrowserService
{
    private readonly object _boardDocumentWriteOwnerGate = new();
    private readonly object _boardDocumentWriteOwnerCreationKey = new();
    private BoardDocumentWriteOwner? _boardDocumentWriteOwner;

    /// <summary>Bind one original Files issuer to its exact physical owner. The same issuer serves
    /// every Home Library lease for this Files singleton; a second physical owner is refused.</summary>
    public BoardDocumentWriteOwner CreateOriginalBoardDocumentWriteOwner(
        IFilesOriginalBoardDocumentPhysicalOwner actualPhysicalOwner)
    {
        ArgumentNullException.ThrowIfNull(actualPhysicalOwner);
        lock (_boardDocumentWriteOwnerGate)
        {
            if (_boardDocumentWriteOwner is { } existing)
            {
                if (!ReferenceEquals(existing.PhysicalOwner, actualPhysicalOwner))
                    throw new UnauthorizedAccessException("The original Files board writer cannot be rebound to another physical owner.");
                return existing;
            }
            var created = new BoardDocumentWriteOwner(this, actualPhysicalOwner,
                _mutationBroker ?? throw new NotSupportedException("The original Home Files write broker is unavailable."),
                _boardDocumentWriteOwnerCreationKey);
            _boardDocumentWriteOwner = created;
            return created;
        }
    }

    public sealed class BoardDocumentWriteOwner : IFilesOriginalBoardDocumentWriteSource
    {
        private sealed class SaveOperation
        {
            internal BoardDocumentWriteOwner Owner = null!;
            internal FilesNativeBrowserPage Page = null!;
            internal HostedItemMetadata OriginalRow = null!;
            internal NativeFilesWorkspace Workspace = null!;
            internal AuthenticatedResourceActor Actor = null!;
            internal string SourceId = string.Empty;
            internal string OperationKey = string.Empty;
            internal string SessionId = string.Empty;
            internal string ExpectedRevision = string.Empty;
            internal string ExpectedSha256 = string.Empty;
            internal byte[] Payload = [];
            internal string PayloadSha256 = string.Empty;
            internal Guid DocumentId;
            internal Guid NewRevisionId;
            internal Guid ParentRevisionId;
            internal ResourceScope[] Scopes = [];
            internal JsonElement Arguments;
            internal IOriginalCanonicalWriteContext WriteContext = null!;
            internal HomePermissionAuthorization? Approval;
            internal HomeResourceExecutionCapability? Capability;
            internal HomeClaimedResourceCommitFence? Fence;
            internal FilesWorkspaceDirectoryResolver.FilesOriginalBindingCommitLease? Mapping;
            internal IFilesOriginalBoardDocumentMaterialization? Materialization;
            internal FilesResult<FilesRevision>? ActualResult;
            internal HomeExecutionOutcome? ActualOutcome;
            internal bool Claimed, Validated, AuditRecorded, Retired;
            internal bool ExactCommitReceipt;
            internal Func<bool> Lifetime = null!;
            internal FilesOriginalReadSourceScope Source = null!;
            internal FilesNativeBrowserService Browser = null!;
            internal readonly object Gate = new();
        }

        private sealed record FilesPageOwner(NativeFilesWorkspace Workspace, AuthenticatedResourceActor Actor);
        private sealed record LatestRevision(FilesNativeBrowserPage Page, HostedItemMetadata Row,
            AuthenticatedResourceActor Actor, string Revision, string Sha256, Guid DocumentId);
        private readonly FilesNativeBrowserService _browser;
        private readonly HomeResourceOperationBroker _broker;
        private readonly ConditionalWeakTable<IFilesOriginalBoardDocumentWriteDestination, SaveOperation> _issued = new();
        private readonly Dictionary<string, SaveOperation> _operations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, LatestRevision> _latest = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private readonly AsyncLocal<SaveOperation?> _executing = new();
        private readonly IFilesOriginalBoardDocumentPhysicalOwner _physicalOwner;
        private readonly List<Task<FilesOriginalBoardDocumentWriteOutcome>> _activeSaves = [];
        private bool _retiring;
        private Task? _close;

        internal BoardDocumentWriteOwner(FilesNativeBrowserService browser,
            IFilesOriginalBoardDocumentPhysicalOwner physicalOwner, HomeResourceOperationBroker broker,
            object originalCreationKey)
        {
            ArgumentNullException.ThrowIfNull(browser);
            ArgumentNullException.ThrowIfNull(physicalOwner);
            ArgumentNullException.ThrowIfNull(broker);
            if (!ReferenceEquals(originalCreationKey, browser._boardDocumentWriteOwnerCreationKey))
                throw new UnauthorizedAccessException("Create the original Files board writer through its owning browser.");
            _browser = browser; _physicalOwner = physicalOwner; _broker = broker;
            if (physicalOwner.OriginalBoardDocumentWriteSource is { } existing && !ReferenceEquals(existing, this))
                throw new UnauthorizedAccessException("Another original Files board writer already owns the physical source.");
            physicalOwner.BindOriginalBoardDocumentWriteSource(this);
        }

        public IFilesOriginalBoardDocumentPhysicalOwner PhysicalOwner => _physicalOwner;
        public Task? OriginalClose { get { lock (_gate) return _close; } }
        public void RequestRetirement() { lock (_gate) _retiring = true; }
        public void DemandExternalOriginalRetirementJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        public Task CloseAndDrainAsync()
        {
            DemandExternalOriginalRetirementJoin();
            TaskCompletionSource start;
            Task actual;
            lock (_gate)
            {
                if (_close is not null) return _close;
                _retiring = true;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual = _close = DrainAsync(start.Task, _activeSaves.ToArray());
            }
            start.SetResult();
            return actual;
        }

        private async Task DrainAsync(Task start, Task<FilesOriginalBoardDocumentWriteOutcome>[] saves)
        {
            await start.ConfigureAwait(false);
            var errors = new List<Exception>();
            foreach (var save in saves)
                try { await save.ConfigureAwait(false); }
                catch (Exception error)
                {
                    if (save.Exception is { } failure) errors.AddRange(failure.InnerExceptions);
                    else errors.Add(error);
                }
            lock (_gate)
                foreach (var operation in _operations.Values)
                    CryptographicOperations.ZeroMemory(operation.Payload);
            if (errors.Count != 0)
                throw new AggregateException("Original Files board writes did not drain cleanly.", errors);
        }

        public Task<FilesOriginalBoardDocumentWriteOutcome> SaveSelectedWithinSourceAsync(
            FilesNativeBrowserPage samePage, HostedItemMetadata sameRow, AuthenticatedResourceActor sameActor,
            string sourceId, string expectedRevision, string expectedSha256, Guid documentId,
            ReadOnlyMemory<byte> validatedDocument, Func<bool> originalLifetime,
            Func<HomePermissionAuthorization, CancellationToken, Task> presentReview,
            string sessionId, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(samePage); ArgumentNullException.ThrowIfNull(sameRow);
            ArgumentNullException.ThrowIfNull(sameActor); ArgumentNullException.ThrowIfNull(originalLifetime);
            ArgumentNullException.ThrowIfNull(presentReview); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
            if (validatedDocument.Length is <= 0 or > FilesNativeBrowserService.MaximumBoardDocumentBytes ||
                documentId == Guid.Empty || !Guid.TryParseExact(expectedRevision, "D", out var expectedId) || expectedId == Guid.Empty ||
                !IsSha256(expectedSha256))
                throw new InvalidDataException("The selected board save requires a bounded document and exact current revision/hash.");

            var payload = validatedDocument.ToArray();
            var payloadHash = Convert.ToHexString(SHA256.HashData(payload));
            var key = OperationKey(sourceId, expectedRevision, payloadHash, documentId);
            var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
            originals.BindOriginalCallerCallback(scope);
            var source = new FilesOriginalReadSourceScope(body => originals.Invoke(() => { body(); return true; }),
                actual => { originals.Track(actual); retain(actual); });
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<FilesOriginalBoardDocumentWriteOutcome> actual;
            lock (_gate)
            {
                if (_retiring) { CryptographicOperations.ZeroMemory(payload); throw new ObjectDisposedException(nameof(BoardDocumentWriteOwner)); }
                _activeSaves.RemoveAll(task => task.IsCompletedSuccessfully || task.IsFaulted);
                if (_activeSaves.Count >= 64)
                { CryptographicOperations.ZeroMemory(payload); throw new InvalidOperationException("Settle original Files board saves before another revision."); }
                actual = Run(start.Task);
                _activeSaves.Add(actual);
            }
            start.SetResult();
            return actual;

            async Task<FilesOriginalBoardDocumentWriteOutcome> Run(Task began)
            {
                await began.ConfigureAwait(false);
                return await originals.RunToOriginalSettlementAsync(async () =>
                {
                    token.ThrowIfCancellationRequested();
                    var operation = source.Invoke(() => GetOrCreateOperation(key, samePage, sameRow, sameActor,
                        sourceId, expectedRevision, expectedSha256, documentId, payload, payloadHash,
                        originalLifetime, sessionId, source));
                    try
                    {
                        return await ExecuteOrObserveAsync(operation, presentReview, source, token).ConfigureAwait(false);
                    }
                    catch
                    {
                        operation.Retired = operation.Capability is not null || operation.Claimed;
                        if (operation.Capability is not null && operation.ActualOutcome is null)
                            operation.ActualOutcome = new(HomePermissionRequestState.PartiallyCompleted,
                                "FILES_BOARD_WRITE_OUTCOME_UNKNOWN",
                                "The original Files board write did not return a certain provider result. Do not replay it; reopen the current Files revision.", []);
                        throw;
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(payload);
                        if (operation.ActualOutcome is not null) CryptographicOperations.ZeroMemory(operation.Payload);
                    }
                }).ConfigureAwait(false);
            }
        }

        private SaveOperation GetOrCreateOperation(string key, FilesNativeBrowserPage page, HostedItemMetadata row,
            AuthenticatedResourceActor actor, string sourceId, string expectedRevision, string expectedSha256,
            Guid documentId, byte[] payload, string payloadHash, Func<bool> lifetime, string sessionId,
            FilesOriginalReadSourceScope source)
        {
            if (_retiring || !lifetime() || !_browser._originalPages.TryGetValue(page, out var original) ||
                original.Actor != actor || page.StoreID != original.Workspace.Configuration.StoreId ||
                !page.Items.Any(item => ReferenceEquals(item, row)) || !IsBoardDocumentCandidate(row) ||
                row.ParentId is not { } selectedParent || page.ParentID != selectedParent.Value ||
                sourceId != GetSourceId(page.StoreID, selectedParent.Value, row.Id.Value) ||
                sessionId.Length is 0 or > 256)
                throw new UnauthorizedAccessException("Save the same current selected Files board through its original Home session.");
            if (!Guid.TryParse(actor.ProfileId, out var profile) || profile == Guid.Empty)
                throw new UnauthorizedAccessException("A current Home local profile is required for Files board saving.");
            lock (_gate)
            {
                var currentOnPage = HasExpectedRevision(row.CurrentRevisionId, expectedRevision) &&
                    NormalizeSha256(row.ContentHash) == NormalizeSha256(expectedSha256);
                var followsCommittedSave = _latest.TryGetValue(sourceId, out var latest) &&
                    ReferenceEquals(latest.Page, page) && ReferenceEquals(latest.Row, row) && latest.Actor == actor &&
                    latest.DocumentId == documentId && latest.Revision == expectedRevision && latest.Sha256 == NormalizeSha256(expectedSha256);
                if (!currentOnPage && !followsCommittedSave)
                    throw new UnauthorizedAccessException("Save only the selected current revision or its confirmed revision from this same Home board session.");
                if (_operations.TryGetValue(key, out var prior))
                {
                    if (!ReferenceEquals(prior.Page, page) || !ReferenceEquals(prior.OriginalRow, row) || prior.Actor != actor ||
                        prior.ExpectedSha256 != NormalizeSha256(expectedSha256) || prior.DocumentId != documentId || prior.Retired)
                        throw new UnauthorizedAccessException("A pending Files board save cannot adopt another selection or payload.");
                    prior.Source = source;
                    return prior;
                }
                if (_operations.Values.Any(operation => operation.ActualOutcome is not null && !operation.AuditRecorded))
                    throw new InvalidOperationException("Settle the existing Home Files board-save audit before another board revision.");
                foreach (var settled in _operations.Where(pair => pair.Value.ActualOutcome is not null)
                    .Select(pair => pair.Key).ToArray()) _operations.Remove(settled);
                if (_operations.Count >= 64) throw new InvalidOperationException("Settle original Files board saves before another revision.");
                var operation = new SaveOperation
                {
                    Owner = this, Browser = _browser, Page = page, OriginalRow = row, OperationKey = key,
                    Workspace = original.Workspace, Actor = actor, SourceId = sourceId, SessionId = sessionId,
                    ExpectedRevision = expectedRevision,
                    ExpectedSha256 = NormalizeSha256(expectedSha256)
                        ?? throw new InvalidDataException("The selected board save requires a valid SHA-256 digest."),
                    Payload = payload.ToArray(), PayloadSha256 = payloadHash, DocumentId = documentId,
                    NewRevisionId = Guid.NewGuid(), Lifetime = lifetime, Source = source
                };
                _operations.Add(key, operation);
                return operation;
            }
        }

        private async Task<FilesOriginalBoardDocumentWriteOutcome> ExecuteAdmittedAsync(SaveOperation operation,
            Func<HomePermissionAuthorization, CancellationToken, Task> presentReview,
            FilesOriginalReadSourceScope source, CancellationToken token)
        {
            await _saveGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                bool retryAudit, cannotReplay;
                lock (operation.Gate)
                {
                    retryAudit = operation.ActualOutcome is not null && operation.Capability is not null;
                    cannotReplay = !retryAudit && (operation.Capability is not null || operation.Retired);
                }
                if (retryAudit) return await RetryAuditAndReceiptAsync(operation, source, token).ConfigureAwait(false);
                if (cannotReplay) return new(false, null, "FILES_BOARD_WRITE_OUTCOME_UNKNOWN",
                    "Inspect or reopen the exact current Files revision; this operation cannot be replayed.", true);

                var workspace = await source.Observe(() => _browser.OriginalBrowserWorkspaces.GetOriginalCurrentWithinSourceAsync(
                    operation.Page.StoreID, source, token)).ConfigureAwait(false);
                if (workspace?.Actor != operation.Actor || !ReferenceEquals(workspace.Provider, operation.Workspace.Provider) ||
                    !ReferenceEquals(workspace.Directories, operation.Workspace.Directories) ||
                    !ReferenceEquals(workspace.Materializations, operation.Workspace.Materializations) ||
                    await source.Observe(() => _browser.OriginalBrowserActors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false) != operation.Actor)
                    throw new UnauthorizedAccessException("The original Files/Home workspace or actor changed.");
                if (!source.Invoke(operation.Lifetime)) throw new ObjectDisposedException("Original Home board session");

                var current = await source.Observe(() => workspace.Provider.GetForOriginalStoreAsync(
                    operation.Page.StoreID, operation.OriginalRow.Id, token)).ConfigureAwait(false);
                if (!current.IsSuccess || current.Value is not { Kind: HostedItemKind.File } row ||
                    row.Id != operation.OriginalRow.Id || row.ParentId != operation.OriginalRow.ParentId ||
                    row.Name != operation.OriginalRow.Name || !IsBoardDocumentCandidate(row) ||
                    !HasExpectedRevision(row.CurrentRevisionId, operation.ExpectedRevision))
                    return new(false, null, "FilesRevisionConflict", "The selected Files board changed. Reopen its current revision before saving.", true);
                if (!Guid.TryParseExact(operation.ExpectedRevision, "D", out var expectedRevision) ||
                    NormalizeSha256(row.ContentHash) != operation.ExpectedSha256 || row.CurrentRevisionId?.Value != expectedRevision)
                    return new(false, null, "FilesRevisionConflict", "The selected Files board revision or content hash changed.", true);
                if (row.ParentId is not { } selectedParent)
                    return new(false, null, "FilesParentUnavailable", "The selected Files board has no canonical parent folder.", true);

                var parentResult = await source.Observe(() => workspace.Provider.GetForOriginalStoreAsync(
                    operation.Page.StoreID, selectedParent, token)).ConfigureAwait(false);
                if (!parentResult.IsSuccess || parentResult.Value is not { Kind: HostedItemKind.Folder, CurrentRevisionId: { } parentRevision })
                    return new(false, null, "FilesParentUnavailable", "The selected Files parent folder is unavailable.", true);
                var parentFolder = parentResult.Value
                    ?? throw new InvalidDataException("The Files parent-folder result omitted its canonical row.");

                var issuer = _browser._originalFolders?.BorrowOriginalItemReadIssuer()
                    ?? throw new NotSupportedException("The original Files board write issuer is unavailable.");
                operation.WriteContext = await source.Observe(() => issuer.CaptureOriginalBoardDocumentWriteAsync(
                    workspace, row, operation.Lifetime, source, token)).ConfigureAwait(false);
                // The Home scope must be the exact revision token retained by the original
                // Files read issuer. The user-facing lease carries a D-form Guid for CAS, while
                // FilesRevisionId.ToString() is N-form; rebuilding the scope from that caller
                // token made a valid selected revision fail its issuer equality check.
                var writeContext = operation.WriteContext
                    ?? throw new UnauthorizedAccessException("The original Files board-write observation is unavailable.");
                operation.Scopes = [writeContext.OriginalScope];
                operation.ParentRevisionId = parentRevision.Value;

                var content = await source.Observe(() => workspace.Provider.GetCurrentArtifactContentForOriginalStoreAsync(
                    operation.Page.StoreID, row.Id, row.CurrentRevisionId, token)).ConfigureAwait(false);
                if (!content.IsSuccess || content.Value is not { } currentContent ||
                    currentContent.Revision.ItemId != row.Id || currentContent.Revision.Id != row.CurrentRevisionId ||
                    NormalizeSha256(currentContent.Revision.ContentHash) != operation.ExpectedSha256 ||
                    currentContent.Revision.SizeBytes != row.SizeBytes || currentContent.UploadAnchorFolderId != selectedParent ||
                    string.IsNullOrWhiteSpace(currentContent.ProviderContentReference))
                    return new(false, null, "FilesBoardSourceUnavailable", "This Files board has no verified immutable source revision to update.", true);
                if (currentContent.UploadAnchorFolderId is not { } anchor)
                    return new(false, null, "FilesBoardAnchorUnavailable", "The selected board has no registered content anchor.", true);

                var profile = Guid.Parse(operation.Actor.ProfileId);
                var anchors = workspace.Configuration.AppFolders.Where(pair => pair.Value == anchor).Take(2).ToArray();
                if (anchors.Length != 1)
                    return new(false, null, "FilesBoardAnchorUnavailable", "The current Files board has no unique registered content anchor.", true);
                async ValueTask RequireWrite(CancellationToken ct)
                {
                    if (!source.Invoke(operation.Lifetime) || !_browser.OriginalBrowserResources.IsOriginalWriteContextForActor(
                        operation.Actor, operation.WriteContext, "files.board.write", operation.Scopes) ||
                        await source.Observe(() => _browser.OriginalBrowserResources.AuthorizeOriginalWriteForActorAsync(
                            operation.Actor, operation.WriteContext, "files.board.write", operation.Scopes, ct).AsTask()).ConfigureAwait(false) != operation.Actor)
                        throw new UnauthorizedAccessException("The current Home WRITE does not authorize this Files board revision.");
                }
                await RequireWrite(token).ConfigureAwait(false);
                var binding = await source.Observe(() => workspace.Directories.ResolveOriginalRegisteredFolderAsync(
                    profile, anchors[0].Key, anchor, workspace.Provider,
                    operation.Page.StoreID, true, ct => RequireWrite(ct), token)).ConfigureAwait(false);
                if (!binding.IsSuccess || binding.Value is not { } originalBinding ||
                    originalBinding.FolderId != anchor || originalBinding.ProfileId != profile ||
                    originalBinding.LocationId != row.LocationId)
                    return new(false, null, "FilesBoardAnchorUnavailable", "The selected board content anchor is no longer registered for this Home profile.", true);

                var arguments = JsonSerializer.SerializeToElement(new
                {
                    operationId = operation.NewRevisionId,
                    storeId = operation.Page.StoreID,
                    itemId = row.Id.Value,
                    parentId = row.ParentId!.Value.Value,
                    expectedRevision = operation.ExpectedRevision,
                    expectedParentRevision = operation.ParentRevisionId,
                    newRevision = operation.NewRevisionId,
                    documentId = operation.DocumentId,
                    sizeBytes = operation.Payload.Length,
                    sha256 = operation.PayloadSha256,
                    effect = "save-selected-board-document"
                });
                operation.Arguments = arguments;
                operation.Approval ??= await source.Observe(() => _broker.AuthorizeOriginalWriteForActorAsync(
                    operation.Actor, writeContext, "files", "files.board.write", operation.Scopes, arguments,
                    $"Save board ‘{row.Name}’ to its selected Files revision.", null, operation.SessionId, token)).ConfigureAwait(false);
                if (operation.Approval.RequestId.Length == 0)
                    throw new InvalidOperationException("Home returned no retained Files board save request.");
                if (operation.Approval.State == HomePermissionRequestState.PendingApproval)
                    await source.ObserveVoid(() => presentReview(operation.Approval, token)).ConfigureAwait(false);
                operation.Approval = await source.Observe(() => _broker.GetAuthorizationAsync(
                    operation.Approval.RequestId, token)).ConfigureAwait(false);
                if (!operation.Approval.IsAllowed)
                {
                    if (operation.Approval.State is HomePermissionRequestState.Denied or
                        HomePermissionRequestState.Blocked or HomePermissionRequestState.Cancelled)
                    {
                        lock (_gate)
                            if (_operations.TryGetValue(operation.OperationKey, out var retained) && ReferenceEquals(retained, operation))
                                _operations.Remove(operation.OperationKey);
                        CryptographicOperations.ZeroMemory(operation.Payload);
                        operation.Retired = true;
                    }
                    return new(false, null, operation.Approval.Code, operation.Approval.Message);
                }

                // Home review may complete after another Files owner has advanced this same
                // item. Observe the canonical row again before re-authorizing its old revision
                // scope, so the user receives a truthful conflict rather than a generic ACL
                // failure. This is still pre-claim: no execution capability, byte leaf or Files
                // mutation exists for the approved stale intent.
                var reviewedCurrent = await source.Observe(() => workspace.Provider.GetForOriginalStoreAsync(
                    operation.Page.StoreID, operation.OriginalRow.Id, token)).ConfigureAwait(false);
                if (!reviewedCurrent.IsSuccess || reviewedCurrent.Value is not { Kind: HostedItemKind.File } latestRow ||
                    latestRow.Id != operation.OriginalRow.Id || latestRow.ParentId != operation.OriginalRow.ParentId ||
                    latestRow.Name != operation.OriginalRow.Name || !IsBoardDocumentCandidate(latestRow) ||
                    !HasExpectedRevision(latestRow.CurrentRevisionId, operation.ExpectedRevision) ||
                    NormalizeSha256(latestRow.ContentHash) != operation.ExpectedSha256)
                {
                    lock (_gate)
                    {
                        if (_operations.TryGetValue(operation.OperationKey, out var retained) && ReferenceEquals(retained, operation))
                            _operations.Remove(operation.OperationKey);
                        operation.Retired = true;
                    }
                    CryptographicOperations.ZeroMemory(operation.Payload);
                    return new(false, null, "FilesRevisionConflict",
                        "The selected Files board changed while Home review was open. Reopen its current revision before saving.", true);
                }
                await RequireWrite(token).ConfigureAwait(false);

                var capability = await source.Observe(() => _broker.BeginExecutionCapabilityAsync(
                    operation.Approval.RequestId, arguments, token)).ConfigureAwait(false);
                if (capability is null)
                    return new(false, null, "HomeExecutionNotAdmitted", "Home has not admitted this exact selected-board save. Review the same request before retrying.");
                operation.Capability = capability;

                HomeClaimedResourceCommitFence? fence = null;
                FilesResult<FilesRevision>? result = null;
                var exactCommitReceipt = false;
                List<Exception> failures = [];
                try
                {
                    await RequireWrite(token).ConfigureAwait(false);
                    var claim = await source.Observe(() => _broker.ClaimExecutionObservedAsync(capability,
                        "files", "files.board.write", operation.Scopes, arguments, token)).ConfigureAwait(false);
                    if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != operation.Actor)
                        throw new UnauthorizedAccessException("Home did not claim the same original Files board save.");
                    operation.Claimed = true;
                    fence = await source.Observe(() => _browser.OriginalBrowserWorkspaces.CaptureBrowserCommitFenceWithinOriginalSourceAsync(
                        workspace, _broker, capability, operation.Lifetime, source.OriginalSynchronousScope, source.RetainOriginalTask,
                        token, actual => operation.Fence = actual).AsTask()).ConfigureAwait(false);
                    var exactFence = fence ?? throw new InvalidOperationException("Home returned no retained Files commit fence.");
                    operation.Fence = exactFence;
                    operation.Mapping = await source.Observe(() => workspace.Directories.AcquireOriginalBindingCommitLeaseAsync(
                        originalBinding, workspace.Provider, token)).ConfigureAwait(false);
                    if (operation.Mapping?.IsHeld != true)
                        throw new UnauthorizedAccessException("The exact registered Files content anchor could not be retained.");
                    if (!await source.Observe(() => exactFence.ValidateAsync(token).AsTask()).ConfigureAwait(false))
                        throw new UnauthorizedAccessException("The claimed Home Files write fence is no longer current.");
                    operation.Validated = true;
                    var destination = IssueDestination(operation, originalBinding.DirectoryPath, row, new(expectedRevision),
                        operation.NewRevisionId, operation.DocumentId, operation.Payload.Length, operation.PayloadSha256);
                    operation.Materialization = await source.ObserveProduct(
                        () => _physicalOwner.MaterializeOriginalBoardDocumentWithinSourceAsync(destination, operation.Payload,
                            source.OriginalSynchronousScope, source.RetainOriginalTask, token),
                        actual => operation.Materialization = actual).ConfigureAwait(false);
                    if (!_physicalOwner.IsIssuedOriginalBoardDocumentMaterialization(operation.Materialization) ||
                        operation.Materialization.OriginalRecord is not { } materialized ||
                        materialized.FilesStoreId != operation.Page.StoreID || materialized.ParentFolderId != selectedParent.Value ||
                        materialized.FileId != row.Id.Value || materialized.ExpectedRevisionId != expectedRevision ||
                        materialized.NewRevisionId != operation.NewRevisionId || materialized.DocumentId != operation.DocumentId ||
                        materialized.SizeBytes != operation.Payload.Length ||
                        !string.Equals(NormalizeSha256(materialized.Sha256), NormalizeSha256(operation.PayloadSha256), StringComparison.Ordinal) ||
                        materialized.OriginalActor != operation.Actor || materialized.RelativeContentReference !=
                            ".9to1-board-" + operation.NewRevisionId.ToString("N") + ".content")
                        throw new InvalidDataException("The original Files physical owner returned another board materialization.");
                    var exactMaterialization = operation.Materialization
                        ?? throw new InvalidOperationException("The physical Files owner returned no retained materialization.");
                    await source.ObserveVoid(() => _physicalOwner.RevalidateOriginalBoardDocumentWithinSourceAsync(
                        exactMaterialization, source.OriginalSynchronousScope, source.RetainOriginalTask, token)).ConfigureAwait(false);
                    await RequireWrite(token).ConfigureAwait(false);

                    var uploaded = new FilesUploadedContent(row.Id, selectedParent, row.Name, row.ContentType,
                        new(operation.NewRevisionId), new(expectedRevision), operation.Actor.ActorId, DateTimeOffset.UtcNow,
                        operation.Payload.Length, operation.PayloadSha256, materialized.RelativeContentReference);
                    var guard = new FilesCommitAuthorityGuard(operation.Actor.ActorId, async ct =>
                    {
                        // Files holds its metadata lease here. The Home fence already owns its raw
                        // claimed operation/currentness lease; never re-enter actor, Files or Home services.
                        DemandOriginalFilesBoardDocumentCommit(destination);
                        _physicalOwner.DemandOriginalBoardDocumentCommit(exactMaterialization);
                        return await source.Observe(() => exactFence.ValidateAsync(ct).AsTask()).ConfigureAwait(false);
                    });
                    var commit = workspace.Provider.CommitUploadedContentAsync(uploaded,
                        [new(row.Id, new(expectedRevision)), new(parentFolder.Id, parentRevision)],
                        operation.Page.StoreID, guard, token);
                    try { result = await source.Observe(() => commit).ConfigureAwait(false); }
                    catch when (commit.IsFaulted) { throw commit.Exception!; }
                    operation.ActualResult = result;
                    if (result is { IsSuccess: true, Value: { } committedRevision } &&
                        (committedRevision.Id.Value != operation.NewRevisionId || committedRevision.ItemId != row.Id ||
                         committedRevision.ParentRevisionId?.Value != expectedRevision))
                        throw new InvalidOperationException("Files returned a success receipt for another board revision; the effect is uncertain.");
                    exactCommitReceipt = result is { IsSuccess: true, Value: { } };
                    operation.ExactCommitReceipt = exactCommitReceipt;
                    if (exactCommitReceipt)
                    {
                        lock (_gate) _latest[operation.SourceId] = new(operation.Page, operation.OriginalRow,
                            operation.Actor, operation.NewRevisionId.ToString("D"), operation.PayloadSha256, operation.DocumentId);
                    }
                }
                catch (Exception error) { failures.Add(error); }
                finally
                {
                    if (operation.Materialization is { } materialization)
                        try { await source.ObserveVoid(() => materialization.CloseAndDrainAsync()).ConfigureAwait(false); }
                        catch (Exception error) { failures.Add(error); }
                    if (operation.Mapping is { } mapping)
                        try { await source.ObserveVoid(() => mapping.DisposeAsync().AsTask()).ConfigureAwait(false); }
                        catch (Exception error) { failures.Add(error); }
                    if (fence is not null)
                        try { await source.ObserveVoid(() => fence.DisposeAsync().AsTask()).ConfigureAwait(false); }
                        catch (Exception error) { failures.Add(error); }
                }

                var returnedFailure = operation.ActualResult is { IsSuccess: false };
                var committed = exactCommitReceipt;
                var known = returnedFailure || committed;
                operation.ActualOutcome = new(committed ? HomePermissionRequestState.Succeeded
                    : returnedFailure ? HomePermissionRequestState.Failed : HomePermissionRequestState.PartiallyCompleted,
                    committed ? "FILES_BOARD_REVISION_COMMITTED" : returnedFailure ? operation.ActualResult!.Error?.Code.ToString() ?? "FILES_BOARD_WRITE_FAILED"
                        : "FILES_BOARD_WRITE_OUTCOME_UNKNOWN",
                    committed ? "The selected rich board was committed as a new immutable Files revision."
                        : returnedFailure ? operation.ActualResult!.Error?.Message ?? "Files rejected the board save."
                        : "The Files board commit outcome is unknown; do not replay it.",
                    committed ? [new("files.item", row.Id.ToString())] : []);
                if (!operation.Claimed)
                {
                    if (operation.Capability is not null)
                        try { await source.Observe(() => _broker.AbortUnclaimedExecutionAsync(operation.Capability, CancellationToken.None)).ConfigureAwait(false); }
                        catch (Exception error) { failures.Add(error); }
                }
                else
                {
                    try
                    {
                        var audit = await source.Observe(() => _broker.CompleteExecutionAsync(
                            operation.Capability!, operation.ActualOutcome, CancellationToken.None)).ConfigureAwait(false);
                        operation.AuditRecorded = audit.Succeeded;
                        if (!audit.Succeeded) failures.Add(new InvalidOperationException(audit.Message));
                    }
                    catch (Exception error) { failures.Add(error); }
                }
                if (failures.Count != 0)
                {
                    if (committed && operation.ActualOutcome is not null)
                        return operation.AuditRecorded
                            ? new(true, operation.NewRevisionId, "SavedWithCleanupIssue",
                                "Files confirmed the new revision and Home recorded it, but an owner cleanup step reported an error.")
                            : new(false, null, "HomeAuditPending",
                                "Files returned the new revision, but Home has not confirmed its terminal audit. Retry this same save request to settle the audit; it will not write again.");
                    if (returnedFailure)
                        return new(false, null,
                            operation.AuditRecorded ? operation.ActualResult!.Error?.Code.ToString() ?? "FilesBoardWriteFailed" : "HomeAuditPending",
                            operation.AuditRecorded ? operation.ActualResult!.Error?.Message ?? "Files rejected this board revision."
                                : "Files rejected this board revision; Home has not confirmed its terminal audit. Retry the same request to settle the audit without writing again.",
                            operation.ActualResult!.Error?.Code == FilesErrorCode.RevisionConflict);
                    if (operation.Claimed)
                        return new(false, null, "FilesWriteOutcomeUnknown",
                            "The original Files write or cleanup did not return a certain result. Do not replay; reopen the current Files revision.", true);
                    throw new AggregateException("The original Files board save failed before claim; no Files write was admitted.", failures);
                }
                return committed
                    ? new(true, operation.NewRevisionId, "Saved", "Saved to the selected Files board revision.")
                    : new(false, null, operation.ActualResult?.Error?.Code.ToString() ?? "FilesWriteOutcomeUnknown",
                        operation.ActualResult?.Error?.Message ?? "The Files write result is unknown; reopen the current revision.", !known);
            }
            finally { _saveGate.Release(); }
        }

        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private async Task<FilesOriginalBoardDocumentWriteOutcome> RetryAuditAndReceiptAsync(
            SaveOperation operation, FilesOriginalReadSourceScope source, CancellationToken token)
        {
            if (!operation.AuditRecorded)
            {
                var audit = await source.Observe(() => _broker.RetryCompletionAuditAsync(operation.Capability!, token)).ConfigureAwait(false);
                operation.AuditRecorded = audit.Succeeded;
                if (!audit.Succeeded) return new(false, null, "HomeAuditPending", audit.Message);
            }
            if (operation.ExactCommitReceipt && operation.ActualResult is { IsSuccess: true })
                return new(true, operation.NewRevisionId, "Saved", "Saved to the selected Files board revision.");
            if (operation.ActualResult is { IsSuccess: false } failed)
                return new(false, null, failed.Error?.Code.ToString() ?? "FilesBoardWriteFailed",
                    failed.Error?.Message ?? "Files rejected the selected board revision.", true);
            return new(false, null, "FilesWriteOutcomeUnknown",
                "The original Files write has no exact committed receipt. Do not replay; reopen the current revision.", true);
        }

        private IFilesOriginalBoardDocumentWriteDestination IssueDestination(SaveOperation operation,
            string directoryPath, HostedItemMetadata row, FilesRevisionId expectedRevision, Guid newRevision,
            Guid documentId, long payloadBytes, string payloadSha256)
        {
            var anchor = row.ParentId?.Value ?? throw new InvalidDataException("The selected board has no canonical parent folder.");
            var destination = new Destination(this, operation);
            var reference = ".9to1-board-" + newRevision.ToString("N") + ".content";
            destination.Description = new(directoryPath, operation.Page.StoreID, anchor, row.Id.Value,
                expectedRevision.Value, newRevision, documentId, reference, payloadBytes, payloadSha256, operation.Actor);
            _issued.Add(destination, operation); return destination;
        }

        private sealed class Destination(BoardDocumentWriteOwner owner, SaveOperation operation)
            : IFilesOriginalBoardDocumentWriteDestination
        {
            internal BoardDocumentWriteOwner Owner { get; } = owner;
            internal SaveOperation Operation { get; } = operation;
            internal FilesOriginalBoardDocumentWriteDescription Description = null!;
        }

        public bool IsIssuedOriginalBoardDocumentWriteDestination(
            IFilesOriginalBoardDocumentWriteDestination originalDestination) =>
            originalDestination is Destination destination && ReferenceEquals(destination.Owner, this) &&
            _issued.TryGetValue(originalDestination, out var same) && ReferenceEquals(same, destination.Operation) &&
            ReferenceEquals(destination.Operation.Owner, this);

        public FilesOriginalBoardDocumentWriteDescription GetOriginalBoardDocumentWriteDescription(
            IFilesOriginalBoardDocumentWriteDestination originalDestination) =>
            originalDestination is Destination destination && IsIssuedOriginalBoardDocumentWriteDestination(destination)
                ? destination.Description
                : throw new UnauthorizedAccessException("Use the same private Files board-save destination.");

        public void DemandOriginalFilesBoardDocumentCommit(IFilesOriginalBoardDocumentWriteDestination originalDestination)
        {
            if (originalDestination is not Destination destination || !IsIssuedOriginalBoardDocumentWriteDestination(destination) ||
                !ReferenceEquals(_executing.Value, destination.Operation) || !destination.Operation.Claimed ||
                !destination.Operation.Validated || destination.Operation.Fence is null ||
                destination.Operation.Mapping?.IsHeld != true || destination.Operation.Retired ||
                !destination.Operation.Lifetime())
                throw new UnauthorizedAccessException("Only the exact claimed Files board write with its held Home and registered-folder fences may materialize bytes.");
        }

        private async Task<FilesOriginalBoardDocumentWriteOutcome> ExecuteOrObserveAsync(
            SaveOperation operation, Func<HomePermissionAuthorization, CancellationToken, Task> presentReview,
            FilesOriginalReadSourceScope source, CancellationToken token)
        {
            var previous = _executing.Value; _executing.Value = operation;
            try { return await ExecuteCoreAsync(operation, presentReview, source, token).ConfigureAwait(false); }
            finally { _executing.Value = previous; }
        }

        private async Task<FilesOriginalBoardDocumentWriteOutcome> ExecuteCoreAsync(
            SaveOperation operation, Func<HomePermissionAuthorization, CancellationToken, Task> presentReview,
            FilesOriginalReadSourceScope source, CancellationToken token)
        {
            // Core is split below to keep exact operation admission and physical/metadata effect
            // in one task; the same source/capability is retained across a pending Home review.
            return await ExecuteAdmittedAsync(operation, presentReview, source, token).ConfigureAwait(false);
        }

        private static string OperationKey(string sourceId, string revision, string hash, Guid documentId) =>
            sourceId + "\n" + revision + "\n" + hash + "\n" + documentId.ToString("D");
        private static bool IsSha256(string value) => NormalizeSha256(value) is not null;

        private static bool HasExpectedRevision(FilesRevisionId? actual, string expected) =>
            Guid.TryParseExact(expected, "D", out var parsed) && actual?.Value == parsed;

        internal static string? NormalizeSha256(string? value)
        {
            if (value?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true) value = value[7..];
            return value is { Length: 64 } && value.All(Uri.IsHexDigit) ? value.ToUpperInvariant() : null;
        }
        internal static string GetSourceId(Guid store, Guid parent, Guid file) =>
            $"files/{store:D}/{parent:D}/{file:D}";
    }
}
