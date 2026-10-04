using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Files.NativeHost;

// Reconstructed owning source, not selected or compiled. The cross-owner final publication fence is held for review.
public sealed class FilesNativeHomeDomainOwner : IHomeNativeFilesPublicationOwner
{
    private readonly HomeNativeCoreApiSessions _issuer;
    private readonly NativeFilesWorkspaceAuthority _workspaces;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly FilesNativeBrowserService _browser;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomePermissionTrustService _permissions;
    private readonly IFilesNativeOriginalPublicationSource? _originalPublicationSource;
    public bool SupportsOriginalPublication => _originalPublicationSource is not null;
    private readonly ConcurrentDictionary<HomeNativeFilesOriginalConnection, State> _connections =
        new(ReferenceEqualityComparer.Instance);
    private readonly object _connectionGate = new();

    public FilesNativeHomeDomainOwner(HomeNativeCoreApiSessions issuer, NativeFilesWorkspaceAuthority workspaces,
        HomeLocalProfileIdentity profiles, FilesNativeBrowserService browser, ResourceAuthorizationService resources,
        HomeResourceOperationBroker broker, HomePermissionTrustService permissions,
        IFilesNativeOriginalPublicationSource? originalPublicationSource = null)
    {
        _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _originalPublicationSource = originalPublicationSource;
        if (!resources.IsBoundToActorSource(profiles) || !broker.IsBoundToOriginalComposition(resources, permissions))
            throw new UnauthorizedAccessException("Use the SAME canonical Home actor, resource and permission composition.");
    }

    private sealed class State(HomeNativeFilesOriginalConnection connection)
    {
        internal readonly object Sync = new();
        internal readonly CancellationTokenSource Lifetime =
            CancellationTokenSource.CreateLinkedTokenSource(connection.OriginalLifetime);
        internal readonly Dictionary<Guid, PageRead> Pages = [];
        internal readonly Dictionary<string, Plan> Pending = new(StringComparer.Ordinal);
        internal readonly ConditionalWeakTable<HomeNativeFilesReply, ReplyRead> Replies = new();
        internal readonly List<Task> Originals = [];
        internal readonly List<Audit> Audits = [];
        internal Task<HomeNativeFilesReply>? PendingTask;
        internal Task? Close;
        internal volatile bool Closing;
        internal Guid? BoundStore;
        internal bool Alive => !Closing && !Lifetime.IsCancellationRequested && !connection.OriginalLifetime.IsCancellationRequested;
    }
    private sealed record PageRead(Guid Handle, FilesNativeBrowserPage Source, int Offset, string Search,
        string Title, Func<CancellationToken, ValueTask<bool>> Current, NativeFilesWorkspace Workspace);
    private sealed record ReplyRead(Func<CancellationToken, ValueTask<bool>> Current,
        NativeFilesWorkspace Workspace, FilesNativeBrowserPage? Page, string StoreRevision);
    private sealed record Audit(Func<Task> Retry);
    private sealed class Plan(HomeNativeFilesRequest request, NativeFilesWorkspace workspace,
        Guid? parent, string search, string title, FilesNativeBrowserCursor? cursor,
        PageRead? retainedPage, int chunkOffset, ResourceScope[] scopes, JsonElement arguments,
        Func<CancellationToken, ValueTask<bool>> current)
    {
        internal HomeNativeFilesRequest Request { get; } = request;
        internal NativeFilesWorkspace Workspace { get; } = workspace;
        internal Guid? Parent { get; } = parent;
        internal string Search { get; } = search;
        internal string Title { get; } = title;
        internal FilesNativeBrowserCursor? Cursor { get; } = cursor;
        internal PageRead? RetainedPage { get; } = retainedPage;
        internal int ChunkOffset { get; } = chunkOffset;
        internal ResourceScope[] Scopes { get; } = scopes;
        internal JsonElement Arguments { get; } = arguments;
        internal Func<CancellationToken, ValueTask<bool>> Current { get; } = current;
        internal bool AuthorizationAttempted;
        internal string? RequestId;
    }

    public Task<HomeNativeFilesReply> InvokeOriginalAsync(HomeNativeFilesOriginalConnection connection,
        HomeNativeFilesRequest request, CancellationToken cancellationToken)
    {
        DemandConnection(connection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Search is null || request.Search.Length > 256 || request.Search.Contains('\0') ||
            request.OriginalPage == Guid.Empty || request.SelectedItem == Guid.Empty || request.Offset is < 0 or > 100 ||
            request.Operation is not ("BrowseRoot" or "OpenFolder" or "Up" or "Refresh" or "NextPage" or "Revalidate"))
            throw new InvalidDataException("The original Files request exceeds its read-only bounds.");
        var captured = request with { };
        var state = GetState(connection);
        lock (state.Sync)
        {
            DemandAlive(state);
            if (state.PendingTask is { IsCompleted: false })
                throw new InvalidOperationException("The original Files operation is still pending.");
            if (state.Originals.Count == 64)
                throw new InvalidOperationException("The bounded original Files task custody is full.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = InvokeCoreAsync(start.Task, connection, state, captured, cancellationToken);
            state.Originals.Add(original);
            state.PendingTask = original;
            start.SetResult();
            return original;
        }
    }

    private async Task<HomeNativeFilesReply> InvokeCoreAsync(Task start, HomeNativeFilesOriginalConnection connection,
        State state, HomeNativeFilesRequest request, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        CancellationTokenSource? active = null;
        HomeNativeFilesReply? reply = null;
        Exception? primary = null;
        List<Exception> cleanup = [];
        HomeResourceExecutionCapability? capability = null;
        bool claimAttempted = false, claimed = false;
        HomeExecutionOutcome? outcome = null;
        Plan? plan = null;
        try
        {
            active = CancellationTokenSource.CreateLinkedTokenSource(state.Lifetime.Token, caller);
            var token = active.Token;
            await DemandActorAsync(connection, state, token).ConfigureAwait(false);
            if (request.Operation == "Revalidate")
            {
                var originalPage = Page(state, request.OriginalPage);
                await _browser.RevalidateAsync(originalPage.Source, connection.OriginalActor, token).ConfigureAwait(false);
                if (!await originalPage.Current(token).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The original Files page retired.");
                reply = await CopyPageAsync(connection, state, originalPage, token).ConfigureAwait(false);
            }
            else
            {
                var key = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
                if (!state.Pending.TryGetValue(key, out plan))
                {
                    if (state.Pending.Count == 16)
                        throw new InvalidOperationException("The bounded original Files approval custody is full.");
                    plan = await PrepareAsync(connection, state, request, token).ConfigureAwait(false);
                    state.Pending.Add(key, plan);
                }
                if (plan.Request != request || !await plan.Current(token).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("The original pending Files intent or binding changed.");
                HomePermissionAuthorization authorization;
                if (plan.RequestId is { } originalRequestId)
                    authorization = await _permissions.GetAuthorizationAsync(originalRequestId, token).ConfigureAwait(false);
                else
                {
                    if (plan.AuthorizationAttempted)
                        throw new InvalidOperationException("The original Home authorization outcome is unknown; it is not repeated.");
                    plan.AuthorizationAttempted = true;
                    authorization = await _broker.AuthorizeForActorAsync(connection.OriginalActor,
                        HomeNativeFilesActionPolicies.TargetAppId, HomeNativeFilesActionPolicies.ReadAction,
                        plan.Scopes, plan.Arguments, "Read this actual configured Files folder; no mutation or package execution.",
                        null, connection.OriginalSessionId, token).ConfigureAwait(false);
                    plan.RequestId = authorization.RequestId;
                }
                await DemandActorAsync(connection, state, token).ConfigureAwait(false);
                if (!await plan.Current(token).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("Files changed while Home reviewed this original request.");
                if (!authorization.IsAllowed)
                {
                    if (authorization.State != HomePermissionRequestState.PendingApproval) state.Pending.Remove(key);
                    reply = new(authorization.State == HomePermissionRequestState.PendingApproval ? "AwaitingApproval" : "Denied",
                        authorization.Code, authorization.Message, PermissionRequestId: authorization.RequestId);
                    state.Replies.Add(reply, new(plan.Current, plan.Workspace,
                        plan.RetainedPage?.Source, plan.Scopes[0].Revision));
                }
                else
                {
                    state.Pending.Remove(key);
                    try
                    {
                        capability = await _broker.BeginExecutionCapabilityAsync(plan.RequestId!, plan.Arguments, token).ConfigureAwait(false);
                    }
                    catch
                    {
                        var failedBeginRequestId = plan.RequestId!;
                        state.Audits.Add(new(async () =>
                        {
                            var recovered = await _broker.RetryRejectedBeginAuditAsync(failedBeginRequestId, CancellationToken.None).ConfigureAwait(false);
                            if (!recovered.Succeeded) throw new InvalidOperationException(recovered.Message);
                        }));
                        throw;
                    }
                    if (capability is null) throw new UnauthorizedAccessException("Home did not issue the original Files read capability.");
                    if (!await plan.Current(token).ConfigureAwait(false)) throw new UnauthorizedAccessException("Files retired before its original claim.");
                    claimAttempted = true;
                    var claim = await _broker.ClaimExecutionObservedAsync(capability,
                        HomeNativeFilesActionPolicies.TargetAppId, HomeNativeFilesActionPolicies.ReadAction,
                        plan.Scopes, plan.Arguments, token).ConfigureAwait(false);
                    claimed = claim.Disposition == HomeResourceClaimDisposition.Claimed;
                    if (!claimed || claim.Actor != connection.OriginalActor)
                        throw new UnauthorizedAccessException("The original Files read claim was refused.");
                    if (!await plan.Current(token).ConfigureAwait(false)) throw new UnauthorizedAccessException("Files retired during its original claim.");
                    var source = plan.RetainedPage?.Source ??
                        await _browser.ListAsync(connection.OriginalActor, plan.Parent, plan.Search, plan.Cursor,
                            token, plan.Workspace.Configuration.StoreId).ConfigureAwait(false);
                    await _browser.RevalidateAsync(source, connection.OriginalActor, token).ConfigureAwait(false);
                    if (!await plan.Current(token).ConfigureAwait(false)) throw new UnauthorizedAccessException("The original Files read changed.");
                    var sourceCheck = await _browser.CaptureOriginalPageReadCheckAsync(source, connection.OriginalActor,
                        () => state.Alive, token).ConfigureAwait(false);
                    if (state.Pages.Count == 16) throw new InvalidOperationException("The bounded original Files page custody is full.");
                    var page = new PageRead(Guid.NewGuid(), source, plan.ChunkOffset, plan.Search, plan.Title,
                        async ct => state.Alive && await plan.Current(ct).ConfigureAwait(false) &&
                            await sourceCheck(ct).ConfigureAwait(false) && state.Alive, plan.Workspace);
                    state.Pages.Add(page.Handle, page);
                    reply = await CopyPageAsync(connection, state, page, token).ConfigureAwait(false);
                    outcome = new(HomePermissionRequestState.Succeeded, "FilesOriginalReadSucceeded",
                        "The actual canonical Files metadata read completed.", plan.Scopes.Select(scope =>
                            new HomeObjectReference(scope.Kind, scope.Id)).ToArray());
                }
            }
        }
        catch (Exception error) { primary = error; }
        finally
        {
            if (capability is not null)
            {
                var originalCapability = capability;
                if (claimed)
                {
                    outcome ??= new(primary is OperationCanceledException ? HomePermissionRequestState.Cancelled : HomePermissionRequestState.Failed,
                        "FilesOriginalReadFailed", "The original Files read did not publish a result.", []);
                    try
                    {
                        var audited = await _broker.CompleteExecutionAsync(originalCapability, outcome, CancellationToken.None).ConfigureAwait(false);
                        if (!audited.Succeeded) throw new InvalidOperationException(audited.Message);
                    }
                    catch (Exception error)
                    {
                        Add(cleanup, error, primary);
                        state.Audits.Add(new(async () =>
                        {
                            var recovered = await _broker.RetryCompletionAuditAsync(originalCapability, CancellationToken.None).ConfigureAwait(false);
                            if (!recovered.Succeeded) throw new InvalidOperationException(recovered.Message);
                        }));
                    }
                }
                else
                {
                    Func<Task> retry = async () =>
                    {
                        var result = claimAttempted
                            ? await _broker.RetryRejectedClaimAuditAsync(originalCapability, CancellationToken.None).ConfigureAwait(false)
                            : await _broker.AbortUnclaimedExecutionAsync(originalCapability, CancellationToken.None).ConfigureAwait(false);
                        if (!result.Succeeded) throw new InvalidOperationException(result.Message);
                    };
                    try { await retry().ConfigureAwait(false); }
                    catch (Exception error) { Add(cleanup, error, primary); state.Audits.Add(new(retry)); }
                }
            }
            if (active is not null) try { active.Dispose(); } catch (Exception error) { Add(cleanup, error, primary); }
        }
        Throw(primary, cleanup);
        return reply!;
    }

    private async Task<Plan> PrepareAsync(HomeNativeFilesOriginalConnection connection, State state,
        HomeNativeFilesRequest request, CancellationToken token)
    {
        PageRead? original = request.OriginalPage is null ? null : Page(state, request.OriginalPage);
        if (original is not null)
        {
            await _browser.RevalidateAsync(original.Source, connection.OriginalActor, token).ConfigureAwait(false);
            if (!await original.Current(token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The privately issued Files page changed.");
        }
        var expectedStore = original?.Source.StoreID ?? state.BoundStore;
        var workspace = expectedStore is { } originalStore
            ? await _workspaces.GetCurrentAsync(originalStore, token).ConfigureAwait(false)
            : await _workspaces.GetCurrentAsync(token).ConfigureAwait(false);
        if (workspace?.Actor != connection.OriginalActor)
            throw new UnauthorizedAccessException("Configure the genuine current Files store in Home.");
        var store = workspace.Configuration.StoreId;
        state.BoundStore ??= store;
        if (state.BoundStore != store) throw new UnauthorizedAccessException("The original connection's Files store changed.");
        var evidence = await workspace.Provider.GetStoreEvidenceAsync(store, token).ConfigureAwait(false);
        var raw = await _workspaces.CaptureOriginalReadCheckAsync(workspace, () => state.Alive, token).ConfigureAwait(false);
        async ValueTask<bool> Current(CancellationToken ct) =>
            state.Alive && await _profiles.GetCurrentAsync(ct).ConfigureAwait(false) == connection.OriginalActor &&
            await raw(ct).ConfigureAwait(false) &&
            await workspace.Provider.GetStoreEvidenceAsync(store, ct).ConfigureAwait(false) == evidence &&
            await raw(ct).ConfigureAwait(false) && state.Alive;
        if (evidence.StoreId != store || !await Current(token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The actual original Files store evidence changed.");
        var scopes = new List<ResourceScope> { new(FilesNativeStoreReadResolver.Kind, store.ToString("D"), evidence.Revision, ResourceAccess.Read) };
        Guid? parent = original?.Source.ParentID;
        var search = original?.Search ?? request.Search;
        var title = original?.Title ?? "Files";
        FilesNativeBrowserCursor? cursor = null;
        PageRead? retained = null;
        var chunk = 0;
        switch (request.Operation)
        {
            case "BrowseRoot" when original is null && request.SelectedItem is null && request.Offset == 0:
                parent = null; search = request.Search; break;
            case "Refresh" when original is not null && request.SelectedItem is null && request.Offset == 0:
                search = request.Search; break;
            case "OpenFolder" when original is not null && request.SelectedItem is { } selected && request.Offset == 0 && request.Search.Length == 0:
                var row = original.Source.Items.Skip(original.Offset).Take(20).SingleOrDefault(item => item.Id.Value == selected)
                    ?? throw new UnauthorizedAccessException("Select the SAME privately issued original Files row.");
                if (row.Kind != HostedItemKind.Folder) throw new InvalidOperationException("This read-only route opens folders only.");
                parent = row.Id.Value; title = row.Name; search = "";
                scopes.Add(new("files.item", row.Id.ToString(), row.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Read));
                break;
            case "Up" when original is not null && request.Search.Length == 0 && request.SelectedItem is null && request.Offset == 0:
                var destination = await _browser.GetParentAsync(original.Source, connection.OriginalActor, token).ConfigureAwait(false);
                parent = destination.ID; title = destination.Title; search = ""; break;
            case "NextPage" when original is not null && request.Search.Length == 0 && request.SelectedItem is null:
                var next = original.Offset + 20;
                if (next < original.Source.Items.Count && request.Offset == next)
                { retained = original; chunk = next; }
                else if (next >= original.Source.Items.Count && original.Source.Next is not null && request.Offset == 0)
                    cursor = original.Source.Next;
                else throw new InvalidOperationException("Retain this page's exact next chunk or provider cursor.");
                break;
            default: throw new InvalidDataException("Unsupported or ambiguous original Files read operation.");
        }
        if (!await Current(token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files plan retired during preparation.");
        var arguments = JsonSerializer.SerializeToElement(new { request.Operation, request.OriginalPage, request.SelectedItem,
            StoreId = store, StoreRevision = evidence.Revision, Parent = parent, Search = search,
            ChunkOffset = chunk, ProviderCursor = cursor?.Offset });
        return new(request, workspace, parent, search, title, cursor, retained, chunk,
            scopes.ToArray(), arguments, Current);
    }

    private async Task<HomeNativeFilesReply> CopyPageAsync(HomeNativeFilesOriginalConnection connection, State state,
        PageRead page, CancellationToken token)
    {
        if (!await page.Current(token).ConfigureAwait(false)) throw new UnauthorizedAccessException("The original Files display retired.");
        var items = page.Source.Items.Skip(page.Offset).Take(20).Select(item =>
            new HomeNativeFilesItem(item.Id.Value, item.ParentId?.Value, item.Name, item.Kind.ToString(),
                item.CurrentRevisionId?.Value, item.SizeBytes, item.ContentType, item.CreatedAt, item.ModifiedAt,
                item.Availability.ToString(), item.IsShared, item.ContentHash)).ToArray();
        var observation = new HomeNativeFilesPage(page.Handle, page.Source.StoreID, page.Source.StoreRevision,
            page.Source.ParentID, page.Title, Array.AsReadOnly(items),
            page.Offset + items.Length < page.Source.Items.Count || page.Source.Next is not null,
            page.Offset + items.Length < page.Source.Items.Count ? page.Offset + items.Length :
                page.Source.Next is not null ? 0 : null);
        var reply = new HomeNativeFilesReply("Succeeded", "FilesOriginalReadSucceeded",
            "Actual configured Files metadata observed.", observation);
        if (!await page.Current(token).ConfigureAwait(false)) throw new UnauthorizedAccessException("The original Files display changed.");
        DemandAlive(state);
        state.Replies.Add(reply, new(page.Current, page.Workspace, page.Source, page.Source.StoreRevision));
        return reply;
    }

    public async Task DemandOriginalReplyCurrentAsync(HomeNativeFilesOriginalConnection connection,
        HomeNativeFilesReply originalReply, CancellationToken token)
    {
        DemandConnection(connection);
        if (!_connections.TryGetValue(connection, out var state) ||
            !state.Replies.TryGetValue(originalReply, out var original))
            throw new UnauthorizedAccessException("Retain the SAME owner-issued original Files reply.");
        if (!await original.Current(token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Files reply binding retired.");
        DemandAlive(state);
    }

    public async ValueTask<IHomeNativeFilesPublicationGuard?> AcquireOriginalReplyPublicationAsync(
        HomeNativeFilesOriginalConnection connection, HomeNativeFilesReply originalReply,
        CancellationToken token)
    {
        DemandConnection(connection);
        if (_originalPublicationSource is null) return null;
        if (!_connections.TryGetValue(connection, out var state) ||
            !state.Replies.TryGetValue(originalReply, out var read))
            throw new UnauthorizedAccessException("Retain the SAME privately issued Files read before publication.");
        DemandAlive(state);
        // The issuer already retains its guard. No ordinary Home/provider current reads here.
        IHomeNativeFilesPublicationGuard? retained = null;
        Exception? primary = null;
        List<Exception> cleanup = [];
        try
        {
            var originalRead = new FilesNativeOriginalPublicationRead(connection, originalReply,
                read.Workspace, read.Page, read.StoreRevision, state.Lifetime.Token);
            retained = await _originalPublicationSource.AcquireOriginalAsync(originalRead, token).ConfigureAwait(false);
            DemandAlive(state);
            token.ThrowIfCancellationRequested();
            if (retained is not null && !retained.IsHeld)
                throw new UnauthorizedAccessException("The original Files transaction was not retained.");
        }
        catch (Exception error) { primary = error; }
        if (primary is not null && retained is not null)
            try { await retained.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { Add(cleanup, error, primary); }
        Throw(primary, cleanup);
        return retained;
    }

    public Task CloseOriginalConnectionAsync(HomeNativeFilesOriginalConnection connection)
    {
        DemandIssuer(connection);
        if (!_connections.TryGetValue(connection, out var state)) return Task.CompletedTask;
        lock (state.Sync)
        {
            if (state.Close is not null) return state.Close;
            state.Closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Close = CloseCoreAsync(start.Task, connection, state, state.Originals.ToArray());
            start.SetResult();
            return state.Close;
        }
    }
    private async Task CloseCoreAsync(Task start, HomeNativeFilesOriginalConnection connection, State state, Task[] originals)
    {
        await start.ConfigureAwait(false);
        List<Exception> failures = [];
        try { state.Lifetime.Cancel(); } catch (Exception error) { Add(failures, error, null); }
        foreach (var original in originals)
            try { await original.ConfigureAwait(false); } catch (Exception error) { Add(failures, error, null); }
        foreach (var audit in state.Audits.ToArray())
            try { await audit.Retry().ConfigureAwait(false); } catch (Exception error) { Add(failures, error, null); }
        try { state.Lifetime.Dispose(); } catch (Exception error) { Add(failures, error, null); }
        Throw(null, failures);
        _connections.TryRemove(connection, out _); // Failed owners retain all original tasks and causes.
    }

    private State GetState(HomeNativeFilesOriginalConnection connection)
    {
        lock (_connectionGate)
        {
            if (_connections.TryGetValue(connection, out var original)) return original;
            if (_connections.Count == 128) throw new InvalidOperationException("The original Files connection custody is full.");
            var state = new State(connection);
            if (!_connections.TryAdd(connection, state)) throw new InvalidOperationException("Original Files connection publication conflicted.");
            return state;
        }
    }
    private static PageRead Page(State state, Guid? handle) =>
        handle is { } id && state.Pages.TryGetValue(id, out var page) ? page :
            throw new UnauthorizedAccessException("A wire page identifier has no original Files admission.");
    private void DemandIssuer(HomeNativeFilesOriginalConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.IsOriginalIssuer(_issuer) || connection.InstalledAppId != HomeNativeFilesActionPolicies.TargetAppId ||
            !connection.Declares(HomeNativeFilesActionPolicies.RequiredInstalledServiceId))
            throw new UnauthorizedAccessException("Use the SAME actually accepted installed Files connection.");
    }
    private void DemandConnection(HomeNativeFilesOriginalConnection connection)
    { DemandIssuer(connection); connection.OriginalLifetime.ThrowIfCancellationRequested(); }
    private async Task DemandActorAsync(HomeNativeFilesOriginalConnection connection, State state, CancellationToken token)
    {
        DemandConnection(connection); DemandAlive(state);
        if (await _profiles.GetCurrentAsync(token).ConfigureAwait(false) != connection.OriginalActor)
            throw new UnauthorizedAccessException("The original Home actor retired.");
        DemandAlive(state);
    }
    private static void DemandAlive(State state)
    { if (!state.Alive) throw new UnauthorizedAccessException("The original Files owner lifetime retired."); }
    private static void Add(List<Exception> errors, Exception error, Exception? primary)
    { if (!ReferenceEquals(primary, error) && !errors.Any(original => ReferenceEquals(original, error))) errors.Add(error); }
    private static void Throw(Exception? primary, List<Exception> errors)
    {
        if (primary is not null && errors.Count == 0) ExceptionDispatchInfo.Capture(primary).Throw();
        if (primary is null && errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (primary is not null || errors.Count != 0)
            throw new AggregateException("Original Files read and independent retirement/audit failed.",
                primary is null ? errors : new[] { primary }.Concat(errors));
    }
}
