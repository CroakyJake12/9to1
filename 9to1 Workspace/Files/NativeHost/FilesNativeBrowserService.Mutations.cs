using System.Text.Json;
using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Apps.Dev;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Files.NativeHost;

public sealed record FilesNativeBrowserMutationOutcome(FilesResult<FilesOperation>? Operation,
    bool AwaitingHomeReview, bool AuditRecorded, string Message);

public sealed partial class FilesNativeBrowserService
{
    private readonly HomeResourceOperationBroker? _mutationBroker;
    private sealed class ActualOperationObservation
    {
        private FilesResult<FilesOperation>? _result;
        internal FilesResult<FilesOperation>? Read() => Volatile.Read(ref _result);
        internal void Capture(FilesResult<FilesOperation> actualReturnedResult)
        {
            var prior = Interlocked.CompareExchange(ref _result, actualReturnedResult, null);
            if (prior is not null && !ReferenceEquals(prior, actualReturnedResult))
                throw new InvalidOperationException("The original actual Files result cannot be replaced.");
        }
    }
    // Readonly private issuer cell; no caller can fabricate a returned provider ACK.
    private readonly ConditionalWeakTable<PreparedMutation, ActualOperationObservation> _issuedNativeMutations = new();

    private FilesResult<FilesOperation>? ReadObservedOperation(PreparedMutation original)
    {
        DemandNativeMutation(original);
        return _issuedNativeMutations.GetValue(original, _ => throw new UnauthorizedAccessException()).Read();
    }

    private void DemandNativeMutation(PreparedMutation original)
    {
        if (!ReferenceEquals(original.Issuer, this) || !_issuedNativeMutations.TryGetValue(original, out _))
            throw new UnauthorizedAccessException("Only the SAME privately issued native Files mutation is available.");
    }

    public FilesNativeBrowserService(NativeFilesWorkspaceAuthority workspaces,
        IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources,
        ICompatibilityPackageContentSource packages, FilesOriginalChildFolderReadSource originalFolders,
        HomeResourceOperationBroker? mutationBroker) : this(workspaces, actors, resources, packages, originalFolders)
        => _mutationBroker = mutationBroker;

    // The shared native Dev composition keeps its SAME durable project store while
    // supplying this SAME optional Home broker. Legacy owners remain unchanged.
    public FilesNativeBrowserService(NativeFilesWorkspaceAuthority workspaces,
        IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources,
        ICompatibilityPackageContentSource packages, FilesOriginalChildFolderReadSource originalFolders,
        FileDeveloperWorkspaceStore originalDeveloperProjects, HomeResourceOperationBroker? mutationBroker)
        : this(workspaces, actors, resources, packages, originalFolders, originalDeveloperProjects)
        => _mutationBroker = mutationBroker;

    public bool HasNativeMutationOwner => _mutationBroker is not null;
    public bool CanRenameNativeSelection(FilesNativeBrowserPage page, HostedItemMetadata row) =>
        HasNativeMutationOwner && _originalPages.TryGetValue(page, out var original)
        && page.Items.Any(item => ReferenceEquals(item, row)) && row.CurrentRevisionId is not null
        && !original.Workspace.Configuration.AppFolders.Values.Contains(row.Id);

    /// <summary>Private original page/operation custody. Displayed request metadata is not
    /// an execution grant; only the SAME broker may begin, claim and fence its actual commit.</summary>
    public sealed class PreparedMutation
    {
        internal readonly FilesNativeBrowserService Issuer;
        internal readonly NativeFilesWorkspace Workspace;
        internal readonly FilesNativeBrowserPage Page;
        internal readonly AuthenticatedResourceActor Actor;
        internal readonly FilesOperation OriginalOperation;
        internal readonly string Name;
        internal readonly IReadOnlyList<ResourceScope> Scopes;
        internal readonly IReadOnlyList<FilesItemRevisionPrecondition> Parents;
        internal readonly JsonElement Arguments;
        internal readonly Func<bool> Lifetime;
        internal readonly object Gate = new();
        internal Task<FilesNativeBrowserMutationOutcome>? ActualApply;
        internal HomeResourceExecutionCapability? Capability;
        internal HomeExecutionOutcome? OriginalOutcome;
        internal int ReviewAttempts;
        internal bool Retired;
        internal PreparedMutation(FilesNativeBrowserService issuer, NativeFilesWorkspace workspace,
            FilesNativeBrowserPage page, AuthenticatedResourceActor actor, FilesOperation operation,
            string name, IReadOnlyList<ResourceScope> scopes, IReadOnlyList<FilesItemRevisionPrecondition> parents,
            JsonElement arguments, Func<bool> lifetime, HomePermissionAuthorization approval)
        { Issuer = issuer; Workspace = workspace; Page = page; Actor = actor; OriginalOperation = operation;
            Name = name; Scopes = scopes; Parents = parents; Arguments = arguments; Lifetime = lifetime; Approval = approval; }
        public HomePermissionAuthorization Approval { get; }
        /// <summary>SAME actual provider return, retained before cleanup/audit. Observation grants no effect replay.</summary>
        public FilesResult<FilesOperation>? ObservedOperation => Issuer.ReadObservedOperation(this);
        public string Description => OriginalOperation.Operation == "CreateFolder" ? $"New folder: {Name}" : $"Rename to: {Name}";
        public bool HasObservedOutcome { get { lock (Gate) return OriginalOutcome is not null; } }
    }

    public void RetireNativeMutationReview(PreparedMutation sameOriginal)
    {
        DemandNativeMutation(sameOriginal);
        lock (sameOriginal.Gate)
        {
            if (sameOriginal.Capability is not null || sameOriginal.ActualApply is { IsCompleted: false })
                throw new InvalidOperationException("The original execution must settle before review retirement.");
            sameOriginal.Retired = true; // No execution was acquired. This is not a Home approval or terminal audit.
        }
    }

    public Task<PreparedMutation> PrepareNativeMutationAsync(FilesNativeBrowserPage samePage,
        HostedItemMetadata? sameRow, AuthenticatedResourceActor sameActor, string operation, string name,
        Func<bool> originalLifetime, string sessionId, CancellationToken token = default)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = PrepareAfterPublicationAsync(start.Task, samePage, sameRow, sameActor, operation,
            name, originalLifetime, sessionId, token);
        start.SetResult(); return actual;
    }

    private async Task<PreparedMutation> PrepareAfterPublicationAsync(Task start,
        FilesNativeBrowserPage page, HostedItemMetadata? row, AuthenticatedResourceActor actor,
        string operation, string name, Func<bool> lifetime, string sessionId, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(lifetime);
        var broker = _mutationBroker ?? throw new NotSupportedException("Home-reviewed Files editing is not configured.");
        if (operation is not ("CreateFolder" or "Rename") || string.IsNullOrWhiteSpace(name) || name.Length > 255
            || name is "." or ".." || name.Any(character => char.IsControl(character) || "/\\<>:\"|?*".Contains(character))
            || string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 256)
            throw new ArgumentException("Choose a supported operation and a single bounded file name.");
        if (!lifetime() || !_originalPages.TryGetValue(page, out var original) || original.Actor != actor)
            throw new UnauthorizedAccessException("Retain the SAME original displayed Files page and actor.");
        await RevalidateAsync(page, actor, token).ConfigureAwait(false);
        var workspace = original.Workspace;
        HostedItemMetadata scopeItem;
        var parents = new List<FilesItemRevisionPrecondition>();
        if (operation == "CreateFolder")
        {
            if (row is not null || page.ParentID is not { } parentId)
                throw new InvalidOperationException("Open a folder before creating a child folder.");
            var parent = await workspace.Provider.GetForOriginalStoreAsync(page.StoreID, new(parentId), token).ConfigureAwait(false);
            scopeItem = parent.IsSuccess && parent.Value is { Kind: HostedItemKind.Folder, CurrentRevisionId: not null }
                ? parent.Value : throw new InvalidOperationException("The original destination folder is unavailable.");
            parents.Add(new(scopeItem.Id, scopeItem.CurrentRevisionId));
        }
        else
        {
            if (row is null || !page.Items.Any(item => ReferenceEquals(item, row)) || row.CurrentRevisionId is null
                || workspace.Configuration.AppFolders.Values.Contains(row.Id))
                throw new InvalidOperationException("Select an original item; configured App folders keep their registered names.");
            scopeItem = row;
            if (page.ParentID is { } parentId)
            {
                var parent = await workspace.Provider.GetForOriginalStoreAsync(page.StoreID, new(parentId), token).ConfigureAwait(false);
                if (!parent.IsSuccess || parent.Value is not { Kind: HostedItemKind.Folder, CurrentRevisionId: not null })
                    throw new InvalidOperationException("The original parent folder is unavailable.");
                parents.Add(new(parent.Value.Id, parent.Value.CurrentRevisionId));
            }
        }
        var now = DateTimeOffset.UtcNow;
        var itemId = operation == "CreateFolder" ? HostedItemId.New() : scopeItem.Id;
        var intent = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, itemId, scopeItem.ParentId,
            operation == "CreateFolder" ? scopeItem.Id : null, operation,
            operation == "Rename" ? scopeItem.CurrentRevisionId : null, null, FilesOperationState.Pending, now, now, null, null);
        var scopes = Array.AsReadOnly(new[] { new ResourceScope("files.item", scopeItem.Id.ToString(),
            scopeItem.CurrentRevisionId!.Value.ToString(), ResourceAccess.Write) });
        var arguments = JsonSerializer.SerializeToElement(new { operationID = intent.Id.Value, storeID = page.StoreID,
            expectedStoreRevision = page.StoreRevision, itemID = itemId.Value,
            parentID = page.ParentID, expectedRevision = intent.BaseRevisionId?.ToString(), operation, name });
        var approval = await ObserveOriginalAsync(broker.AuthorizeForActorAsync(actor, "files", "9to1.Files." + operation,
            scopes, arguments, operation == "CreateFolder" ? $"Create folder ‘{name}’ in ‘{scopeItem.Name}’."
                : $"Rename ‘{scopeItem.Name}’ to ‘{name}’.", null, sessionId, token)).ConfigureAwait(false);
        await RevalidateAsync(page, actor, token).ConfigureAwait(false);
        if (!lifetime()) throw new ObjectDisposedException("Original Files view");
        var prepared = new PreparedMutation(this, workspace, page, actor, intent, name, scopes,
            Array.AsReadOnly(parents.ToArray()), arguments, lifetime, approval);
        _issuedNativeMutations.Add(prepared, new ActualOperationObservation());
        return prepared;
    }

    public Task<FilesNativeBrowserMutationOutcome> ApplyNativeMutationAsync(PreparedMutation sameOriginal,
        CancellationToken token = default)
    {
        DemandNativeMutation(sameOriginal);
        lock (sameOriginal.Gate)
        {
            if (sameOriginal.ActualApply is { } prior && !(prior.IsCompletedSuccessfully && prior.Result.AwaitingHomeReview)) return prior;
            if (sameOriginal.Retired || sameOriginal.Capability is not null || sameOriginal.ReviewAttempts++ >= 32)
                throw new InvalidOperationException("Inspect the original operation; another effect admission is unavailable.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = sameOriginal.ActualApply = ApplyAfterPublicationAsync(start.Task, sameOriginal, token);
            start.SetResult(); return actual;
        }
    }

    private async Task<FilesNativeBrowserMutationOutcome> ApplyAfterPublicationAsync(Task start,
        PreparedMutation original, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        var broker = _mutationBroker!;
        bool Alive() => !original.Retired && original.Lifetime();
        if (!Alive()) throw new ObjectDisposedException("Original Files view");
        await RevalidateAsync(original.Page, original.Actor, token).ConfigureAwait(false);
        var capability = await ObserveOriginalAsync(broker.BeginExecutionCapabilityAsync(original.Approval.RequestId, original.Arguments, token)).ConfigureAwait(false);
        if (capability is null) return new(null, true, false, "Home has not admitted execution. Check its current decision before choosing Apply.");
        lock (original.Gate) original.Capability = capability;
        HomeClaimedResourceCommitFence? fence = null;
        FilesResult<FilesOperation>? result = null;
        bool claimed = false;
        List<Exception> failures = [];
        try
        {
            if (!Alive()) throw new ObjectDisposedException("Original Files view");
            await RevalidateAsync(original.Page, original.Actor, token).ConfigureAwait(false);
            var claim = await ObserveOriginalAsync(broker.ClaimExecutionObservedAsync(capability, "files",
                "9to1.Files." + original.OriginalOperation.Operation, original.Scopes, original.Arguments, token)).ConfigureAwait(false);
            if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != original.Actor)
                throw new UnauthorizedAccessException("Home did not claim the SAME original Files operation.");
            claimed = true;
            fence = await ObserveOriginalAsync(workspaces.CaptureBrowserCommitFenceAsync(original.Workspace, broker, capability,
                Alive, token).AsTask()).ConfigureAwait(false);
            var guard = new FilesCommitAuthorityGuard(original.Actor.ActorId, fence.ValidateAsync);
            var actual = original.Workspace.Provider.CommitNativeBrowserStructureAsync(original.OriginalOperation,
                original.Name, original.Page.StoreID, original.Page.StoreRevision, original.Parents, guard, token);
            try
            {
                result = await actual.ConfigureAwait(false);
                // Only the actual provider Task return can populate this once-only ACK.
                // Capture BEFORE fence disposal and Home terminal audit can fault.
                _issuedNativeMutations.GetValue(original, _ => throw new UnauthorizedAccessException()).Capture(result);
            }
            catch when (actual.IsFaulted) { throw actual.Exception!; }
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            // Release the SAME held Home fence before any Home terminal audit.
            if (fence is not null)
                try { await ObserveOriginalAsync(fence.DisposeAsync().AsTask()).ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
        }
        if (!claimed)
        {
            try
            {
                var audit = await ObserveOriginalAsync(broker.AbortUnclaimedExecutionAsync(capability, CancellationToken.None)).ConfigureAwait(false);
                if (!audit.Succeeded && audit.Code != "HOME_EXECUTION_ABORT_NOT_OWNED")
                    failures.Add(new InvalidOperationException(audit.Message));
            }
            catch (Exception error) { failures.Add(error); }
            throw new AggregateException("Original Files admission refused; no replacement effect is available.", failures);
        }
        // A returned structured failure is known no commit. A fault/cancellation may be
        // post-publication: retain uncertainty and never admit another mutation on this original.
        var committed = result is { IsSuccess: true, Value.State: FilesOperationState.Committed };
        var state = committed ? HomePermissionRequestState.Succeeded
            : result is not null ? HomePermissionRequestState.Failed : HomePermissionRequestState.PartiallyCompleted;
        var outcome = new HomeExecutionOutcome(state,
            committed ? "FILES_STRUCTURE_COMMITTED" : result?.Error?.Code.ToString() ?? "FILES_STRUCTURE_OUTCOME_UNKNOWN",
            committed ? "The exact original Files operation committed." : result?.Error?.Message
                ?? "The original commit or cleanup failed; inspect the saved operation before further work.",
            committed ? [new("files.item", original.OriginalOperation.ItemId.ToString())] : []);
        lock (original.Gate) original.OriginalOutcome = outcome;
        HomePermissionOperationResult? recorded = null;
        try { recorded = await ObserveOriginalAsync(broker.CompleteExecutionAsync(capability, outcome, CancellationToken.None)).ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Original Files commit/audit/cleanup failed; the original cannot replay.", failures);
        return new(result, false, recorded?.Succeeded == true,
            committed ? recorded?.Succeeded == true ? "Files updated." : "Files updated; Home audit needs Retry audit."
                : result?.Error?.Message ?? "Inspect the original Files operation.");
    }

    /// <summary>Retries only the SAME detached terminal outcome. Never prepares, claims or mutates Files again.</summary>
    public Task<HomePermissionOperationResult> RetryNativeMutationAuditAsync(PreparedMutation sameOriginal,
        CancellationToken token = default)
    {
        DemandNativeMutation(sameOriginal);
        lock (sameOriginal.Gate)
        {
            if (sameOriginal.Capability is null || sameOriginal.OriginalOutcome is null)
                throw new InvalidOperationException("No source-owned observed mutation outcome is available for audit retry.");
            return ObserveOriginalAsync(_mutationBroker!.RetryCompletionAuditAsync(sameOriginal.Capability, token));
        }
    }

    // Await the SAME actual source task and retain every fault, including siblings that
    // an ordinary await alone would select down to one exception. No replacement work.
    private static async Task<T> ObserveOriginalAsync<T>(Task<T> actual)
    {
        try { return await actual.ConfigureAwait(false); }
        catch when (actual.IsFaulted) { throw actual.Exception!; }
    }
    private static async Task ObserveOriginalAsync(Task actual)
    {
        try { await actual.ConfigureAwait(false); }
        catch when (actual.IsFaulted) { throw actual.Exception!; }
    }
}
