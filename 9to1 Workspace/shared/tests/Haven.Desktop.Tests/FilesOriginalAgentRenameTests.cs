using System.Collections.Frozen;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dulche.Runtime.Agents;
using Haven.Application;
using Haven.Core;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Real Home-bound Files store, canonical provider commit and journal. The private
/// admission below is deliberately synthetic; these cases do not claim real Agent issuer/DI integration.</summary>
public sealed class FilesOriginalAgentRenameTests
{
    [Fact]
    public Task Original_rename_commits_one_canonical_revision_and_exact_receipt_rejects_copies() => WithFiles(async f =>
    {
        var (original, dispatch) = f.Calls("renamed");
        var demand = await f.Owner.ResolveAsync(f.Workspace.Actor, f.Reference, f.Step, original, dispatch, f.Token);
        Assert.NotNull(demand); Assert.Equal("files.agent.rename", demand.ActionId);
        Assert.Equal("files", demand.TargetAppId);
        Assert.Equal(demand.Policy, f.Owner.TryGet("files", demand.ActionId));
        Assert.Null(f.Owner.TryGet(HomeAgentExecutionActionPolicies.AppId, demand.ActionId));
        Assert.Equal(f.Workspace.Actor, await f.Resources.AuthorizeForActorAsync(f.Workspace.Actor, demand.ActionId, demand.ResourceScopes, f.Token));
        Assert.Null(await f.Resources.AuthorizeForActorAsync(f.Workspace.Actor, demand.ActionId,
            [Assert.Single(demand.ResourceScopes) with { Revision = Guid.NewGuid().ToString("D") }], f.Token));
        Assert.True(demand.Policy.RequiresPerActionApproval);
        Assert.Equal(f.Item.CurrentRevisionId!.Value.ToString(), Assert.Single(demand.ResourceScopes).Revision);
        f.Admission.Dispatch = dispatch;
        var local = new NoLocalWorkspaceTools();
        var runtime = new WorkspaceToolRuntime(local, originalTools: f.Owner);
        var pending = runtime.ExecuteAsync(null, dispatch, f.Token, conversationId: f.Admission.Conversation,
            originalExecutionAuthority: f.Admission.Authority, originalModelIdentity: "fixture-model");
        var repeated = runtime.ExecuteAsync(null, dispatch, f.Token, conversationId: f.Admission.Conversation,
            originalExecutionAuthority: f.Admission.Authority, originalModelIdentity: "fixture-model");
        WorkspaceToolResult? actual = null; Exception? primary = null;
        try { Assert.Same(pending, repeated); actual = await pending; }
        catch (Exception error) { primary = error; }
        finally { try { await pending; } catch (Exception error) { primary = Combine(primary, error); } }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        Assert.Equal(0, local.Calls);
        var current = (await f.Workspace.Provider.GetForOriginalStoreAsync(f.Workspace.Configuration.StoreId, f.Item.Id, f.Token)).Value!;
        Assert.Equal("renamed", current.Name); Assert.NotEqual(f.Item.CurrentRevisionId, current.CurrentRevisionId);
        var changes = await f.Workspace.Provider.GetChangesAsync(f.BeforeCursor, 100, f.Token);
        var change = Assert.Single(changes.Items); Assert.Equal("Rename", change.Kind);
        var receipt = Assert.IsType<FilesOriginalStructuralReceipt>(await f.Workspace.Provider.GetOriginalStructuralReceiptAsync(
            f.Workspace.Configuration.StoreId, change.OperationId!.Value, f.Token));
        Assert.Equal(current.CurrentRevisionId, receipt.Operation.ResultRevisionId);
        Assert.Equal(f.Item.CurrentRevisionId, receipt.Operation.BaseRevisionId); Assert.Equal("renamed", receipt.Change.Metadata!.Name);
        var outcome = await f.Owner.VerifyOriginalOutcomeAsync(f.Workspace.Actor, f.Reference, f.Step, original, dispatch,
            "exact-fixture-request", actual, null, f.Token);
        Assert.Equal(HomePermissionRequestState.Succeeded, outcome!.State);
        Assert.Null(await f.Owner.VerifyOriginalOutcomeAsync(f.Workspace.Actor, f.Reference, f.Step, original, dispatch,
            "exact-fixture-request", actual! with { }, null, f.Token));
        Assert.Null(await f.Owner.VerifyOriginalOutcomeAsync(f.Workspace.Actor, f.Reference, f.Step, original, dispatch with { },
            "exact-fixture-request", actual, null, f.Token));
        Assert.Null(await f.Owner.VerifyOriginalOutcomeAsync(f.Workspace.Actor, f.Reference, f.Step, original, dispatch,
            "another-request", actual, null, f.Token));
        Assert.Single((await f.Workspace.Provider.GetChangesAsync(f.BeforeCursor, 100, f.Token)).Items);
    });

    [Fact]
    public Task Original_definition_observations_require_current_private_admission_and_actual_tools_model() => WithFiles(async f =>
    {
        var capabilities = new[] { new ActiveCapability("write-file", "Files", "files", "", "files", "files") };
        var definitions = await f.Owner.GetOriginalDefinitionsAsync(f.Admission.Authority, f.Admission.Conversation,
            "fixture-model", capabilities, f.Token);
        Assert.Equal(FilesAgentOriginalToolDispatcher.ToolName, Assert.Single(definitions).Name);
        var context = new ToolAvailabilityContext(HavenMode.Chat, null, capabilities, PermissionMode.Ask, PermissionMode.Ask,
            PermissionMode.Ask, false, false, false, false);
        var plan = ToolAvailabilityPlanner.Default.Create(context, new([], [], [], [], [], [], OriginalWorkspace: definitions));
        var text = new ModelDescriptor("fixture-model", 1, "fixture", "1", "fixture", new HashSet<ToolCapability>([ToolCapability.Text]), DateTimeOffset.UnixEpoch);
        Assert.DoesNotContain(plan.RestrictToModel(text).Definitions, item => item.Name == FilesAgentOriginalToolDispatcher.ToolName);
        var tools = text with { Capabilities = new HashSet<ToolCapability>([ToolCapability.Text, ToolCapability.Tools]) };
        Assert.Contains(plan.RestrictToModel(tools).Definitions, item => item.Name == FilesAgentOriginalToolDispatcher.ToolName);
        Assert.Empty(await f.Owner.GetOriginalDefinitionsAsync(f.Admission.Authority, f.Admission.Conversation, "fixture-model", [], f.Token));
        f.Admission.Lifetime.Cancel();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Owner.GetOriginalDefinitionsAsync(f.Admission.Authority,
            f.Admission.Conversation, "fixture-model", capabilities, f.Token).AsTask());
    });

    [Fact]
    public Task Cancellation_after_real_commit_retains_original_partial_receipt_and_refuses_next_dispatch() => WithFiles(async f =>
    {
        var (original, dispatch) = f.Calls("committed-before-retirement");
        Assert.NotNull(await f.Owner.ResolveAsync(f.Workspace.Actor, f.Reference, f.Step, original, dispatch, f.Token));
        f.Admission.Dispatch = dispatch; f.Admission.RetireAtCommitCheck = 4;
        var failure = await Assert.ThrowsAsync<WorkspaceOriginalCommittedObservationException>(() =>
            f.Owner.ExecuteOriginalAsync(f.Admission.Authority, f.Admission.Conversation, "fixture-model", dispatch, f.Token).AsTask());
        Assert.True(f.Admission.Lifetime.IsCancellationRequested); Assert.IsType<OperationCanceledException>(failure.InnerException);
        var change = Assert.Single((await f.Workspace.Provider.GetChangesAsync(f.BeforeCursor, 100, f.Token)).Items);
        Assert.Equal("committed-before-retirement", change.Metadata!.Name);
        Assert.Equal(change.OperationId!.Value.ToString(), failure.OperationId);
        var settled = await f.Owner.VerifyOriginalOutcomeAsync(f.Workspace.Actor, f.Reference, f.Step, original, dispatch,
            "original-retired-request", null, failure, CancellationToken.None);
        Assert.Equal(HomePermissionRequestState.PartiallyCompleted, settled!.State);
        Assert.Equal("Files.RenameCommittedObservationFailed", settled.Code);
        Assert.Null(await f.Owner.VerifyOriginalOutcomeAsync(f.Workspace.Actor, f.Reference, f.Step, original, dispatch,
            "original-retired-request", null, new WorkspaceOriginalCommittedObservationException("files", failure.OperationId, failure.InnerException!), CancellationToken.None));
        var (nextOriginal, nextDispatch) = f.Calls("must-not-write", change.Metadata);
        Assert.NotNull(await f.Owner.ResolveAsync(f.Workspace.Actor, f.Reference, f.Step, nextOriginal, nextDispatch, f.Token));
        f.Admission.Dispatch = nextDispatch;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Owner.ExecuteOriginalAsync(f.Admission.Authority,
            f.Admission.Conversation, "fixture-model", nextDispatch, f.Token).AsTask());
        Assert.Single((await f.Workspace.Provider.GetChangesAsync(f.BeforeCursor, 100, f.Token)).Items);
        Assert.Equal("committed-before-retirement", (await f.Workspace.Provider.GetForOriginalStoreAsync(
            f.Workspace.Configuration.StoreId, f.Item.Id, f.Token)).Value!.Name);
    });

    [Fact]
    public Task Revocation_at_final_owning_check_preserves_original_bytes_and_has_no_journal_commit() => WithFiles(async f =>
    {
        var (original, dispatch) = f.Calls("must-not-commit");
        Assert.NotNull(await f.Owner.ResolveAsync(f.Workspace.Actor, f.Reference, f.Step, original, dispatch, f.Token));
        f.Admission.Dispatch = dispatch; f.Admission.RetireAtCommitCheck = 3;
        var before = await File.ReadAllBytesAsync(f.StatePath, f.Token);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Owner.ExecuteOriginalAsync(f.Admission.Authority,
            f.Admission.Conversation, "fixture-model", dispatch, f.Token).AsTask());
        Assert.Equal(before, await File.ReadAllBytesAsync(f.StatePath, f.Token));
        Assert.Empty((await f.Workspace.Provider.GetChangesAsync(f.BeforeCursor, 100, f.Token)).Items);
        var settled = await f.Owner.VerifyOriginalOutcomeAsync(f.Workspace.Actor, f.Reference, f.Step, original, dispatch,
            "original-cancelled-request", null, error, CancellationToken.None);
        Assert.Equal(HomePermissionRequestState.Cancelled, settled!.State);
    });

    [Fact]
    public Task Current_revision_and_unknown_or_copied_route_are_denied_before_real_provider_mutation() => WithFiles(async f =>
    {
        var (original, dispatch) = f.Calls("expected");
        Assert.Null(await f.Owner.ResolveAsync(f.Workspace.Actor, f.Reference, f.Step, original,
            dispatch with { Name = "write_file" }, f.Token));
        Assert.Null(await f.Owner.ResolveAsync(f.Workspace.Actor, f.Reference, f.Step, original, original, f.Token));
        Assert.NotNull(await f.Owner.ResolveAsync(f.Workspace.Actor, f.Reference, f.Step, original, dispatch, f.Token));
        f.Admission.Dispatch = dispatch;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Owner.ExecuteOriginalAsync(f.Admission.Authority,
            f.Admission.Conversation, "fixture-model", dispatch with { }, f.Token).AsTask());
        var now = DateTimeOffset.UtcNow;
        var external = await f.Workspace.Provider.MutateAsync(new(new(Guid.NewGuid()), f.Workspace.Actor.ActorId, f.Item.Id,
            f.Item.ParentId, null, "Rename", f.Item.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null),
            "changed-by-owning-provider", f.Token);
        Assert.True(external.IsSuccess);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owner.ExecuteOriginalAsync(f.Admission.Authority,
            f.Admission.Conversation, "fixture-model", dispatch, f.Token).AsTask());
        Assert.Contains("RevisionConflict", error.Message);
        Assert.Single((await f.Workspace.Provider.GetChangesAsync(f.BeforeCursor, 100, f.Token)).Items);
        Assert.Equal("changed-by-owning-provider", (await f.Workspace.Provider.GetForOriginalStoreAsync(
            f.Workspace.Configuration.StoreId, f.Item.Id, f.Token)).Value!.Name);
    });

    [Fact]
    public Task SafeMode_at_final_original_commit_preserves_real_store_and_refuses_current_routes() => WithFiles(async f =>
    {
        Assert.False(RuntimeSafetyState.IsSafeMode);
        try
        {
            var (original, dispatch) = f.Calls("must-not-commit-in-safe-mode");
            Assert.NotNull(await f.Owner.ResolveAsync(f.Workspace.Actor, f.Reference, f.Step, original, dispatch, f.Token));
            f.Admission.Dispatch = dispatch; f.Admission.SafeModeAtCommitCheck = 3;
            var before = await File.ReadAllBytesAsync(f.StatePath, f.Token);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owner.ExecuteOriginalAsync(
                f.Admission.Authority, f.Admission.Conversation, "fixture-model", dispatch, f.Token).AsTask());
            Assert.Contains("PermissionDenied", failure.Message); Assert.True(RuntimeSafetyState.IsSafeMode);
            Assert.Equal(3, f.Admission.Checks);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.StatePath, f.Token));
            Assert.Empty((await f.Workspace.Provider.GetChangesAsync(f.BeforeCursor, 100, f.Token)).Items);
            Assert.Equal(HomePermissionRequestState.Failed, (await f.Owner.VerifyOriginalOutcomeAsync(
                f.Workspace.Actor, f.Reference, f.Step, original, dispatch, "safe-mode-refused-original", null, failure, CancellationToken.None))!.State);
            Assert.Empty(await f.Owner.GetOriginalDefinitionsAsync(f.Admission.Authority, f.Admission.Conversation,
                "fixture-model", [new ActiveCapability("write-file", "Files", "files", "", "files", "files")], f.Token));
            var (nextOriginal, nextDispatch) = f.Calls("must-not-admit");
            Assert.Null(await f.Owner.ResolveAsync(f.Workspace.Actor, f.Reference, f.Step, nextOriginal, nextDispatch, f.Token));
        }
        finally { RuntimeSafetyState.DisableSafeMode(); }
    });

    private static async Task WithFiles(Func<Fixture, Task> body)
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-files-agent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ServiceProvider? graph = null; SyntheticOriginalAdmission? admission = null; Exception? primary = null;
        try
        {
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var services = new ServiceCollection();
            services.AddSingleton<IHomeCoreStateStore>(home); services.AddSingleton(profiles);
            services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
            services.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
            services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
            services.AddSingleton<HomeLocalStoreOwnership>();
            services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
            services.AddSingleton<ResourceAuthorizationService>(); services.AddFilesNativeHost();
            graph = services.BuildServiceProvider();
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            var configured = await graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
            var authority = graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
            var workspace = Assert.IsType<NativeFilesWorkspace>(await authority.GetCurrentAsync(configured.Configuration.StoreId, token));
            var item = (await workspace.Provider.GetForOriginalStoreAsync(workspace.Configuration.StoreId,
                workspace.Configuration.AppFolders["write"], token)).Value!;
            var changes = await workspace.Provider.GetChangesAsync(null, 100, token);
            admission = new(workspace.Actor);
            await body(new(workspace, new(authority, admission), admission, item, changes.Items[^1].Cursor,
                Path.Combine(chosen, ".9to1-files", "drive.json"), graph.GetRequiredService<ResourceAuthorizationService>(), token));
        }
        catch (Exception error) { primary = error; }
        finally
        {
            try { admission?.Dispose(); } catch (Exception error) { primary = Combine(primary, error); }
            try { graph?.Dispose(); } catch (Exception error) { primary = Combine(primary, error); }
            try { Directory.Delete(root, true); } catch (Exception error) { primary = Combine(primary, error); }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private sealed record Fixture(NativeFilesWorkspace Workspace, FilesAgentOriginalToolDispatcher Owner,
        SyntheticOriginalAdmission Admission, HostedItemMetadata Item, FilesChangeCursor BeforeCursor, string StatePath, ResourceAuthorizationService Resources, CancellationToken Token)
    {
        internal DenAgentReference Reference { get; } = new("synthetic-den-observation", "personal", Guid.NewGuid().ToString("D"), 1);
        internal AgentExecutionStep Step { get; } = new("synthetic-original-run", "synthetic-original-attempt", "rename", "synthetic-caller",
            "synthetic-session", new("ollama", "synthetic-session", "fixture-model", "ollama", new HashSet<string>(), true),
            new HashSet<string>(["files.write"]), new HashSet<string>(), new(), [], new HashSet<string>(), 1, null);
        internal (OllamaToolCall Original, OllamaToolCall Dispatch) Calls(string name, HostedItemMetadata? item = null)
        {
            item ??= Item;
            var args = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["store_id"] = JsonSerializer.SerializeToElement(Workspace.Configuration.StoreId.ToString("D")),
                ["file_id"] = JsonSerializer.SerializeToElement(item.Id.ToString()),
                ["expected_revision"] = JsonSerializer.SerializeToElement(item.CurrentRevisionId!.Value.ToString()),
                ["new_name"] = JsonSerializer.SerializeToElement(name)
            };
            return (new(FilesAgentOriginalToolDispatcher.ToolName, args),
                new(FilesAgentOriginalToolDispatcher.ToolName, args.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)));
        }
    }

    private sealed class SyntheticOriginalAdmission(AuthenticatedResourceActor actor) : IChatExecutionAdmission, IDisposable
    {
        internal object Authority { get; } = new();
        private object Commit { get; } = new();
        internal Guid Conversation { get; } = Guid.NewGuid();
        internal CancellationTokenSource Lifetime { get; } = new();
        internal OllamaToolCall? Dispatch;
        internal int RetireAtCommitCheck, SafeModeAtCommitCheck, Checks;
        public ValueTask DemandCurrentAsync(object authority, Guid conversation, string model, OllamaToolCall? call, CancellationToken token = default)
        { Require(authority, conversation, model, token); return ValueTask.CompletedTask; }
        public ValueTask<CancellationToken> GetOriginalLifetimeAsync(object authority, CancellationToken token = default)
        { Require(authority, Conversation, "fixture-model", token); return ValueTask.FromResult(Lifetime.Token); }
        public ValueTask<object> GetOriginalCommitAdmissionAsync(object authority, Guid conversation, string model, OllamaToolCall dispatch, CancellationToken token = default)
        {
            Require(authority, conversation, model, token);
            if (!ReferenceEquals(dispatch, Dispatch)) throw new UnauthorizedAccessException("Synthetic private dispatch refused.");
            return ValueTask.FromResult(Commit);
        }
        public ValueTask<AuthenticatedResourceActor> DemandOriginalCommitCurrentAsync(object admission, CancellationToken token = default)
        {
            if (!ReferenceEquals(admission, Commit)) throw new UnauthorizedAccessException("Synthetic private commit refused.");
            var check = Interlocked.Increment(ref Checks);
            if (check == RetireAtCommitCheck) Lifetime.Cancel();
            if (check == SafeModeAtCommitCheck) RuntimeSafetyState.EnableSafeMode("Synthetic final-admission scheduling witness");
            token.ThrowIfCancellationRequested(); Lifetime.Token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(actor);
        }
        private void Require(object authority, Guid conversation, string model, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(authority, Authority) || conversation != Conversation || model != "fixture-model" || Lifetime.IsCancellationRequested)
                throw new UnauthorizedAccessException("Synthetic original execution refused.");
        }
        public void Dispose() => Lifetime.Dispose();
    }
    private sealed class NoLocalWorkspaceTools : IWorkspaceToolService
    {
        internal int Calls;
        private Exception Refuse() { Calls++; return new InvalidOperationException("Original canonical Files dispatch must not use a fabricated local root."); }
        public string ResolveWorkspacePath(string root, string path) => throw Refuse();
        public Task<string> ReadTextAsync(string root, string path, CancellationToken token) => throw Refuse();
        public Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token) => throw Refuse();
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) => throw Refuse();
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) => throw Refuse();
    }
    private static Exception Combine(Exception? primary, Exception error) => primary is null ? error :
        ReferenceEquals(primary, error) ? primary : new AggregateException(primary, error);
}
