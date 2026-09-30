using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Cui.AI;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeNativeActionPolicyTests
{
    [Fact]
    public async Task Native_catalogue_operates_without_model_session_and_duplicate_owner_denies()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-native-policy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            HomePermissionCallerIdentity caller = new("profile", "Native profile", "os", "session", true);
            HomePermissionRequestSubmission request = new(null, caller, "session", new("files", "files.save", [new("file", "file-1")]), HomePermissionImpactPreview.Unknown);
            var service = new HomeAppAiServices(new ModelProviderRegistry([]), store, caller, new Graph(), new Invocations(), [new Policy()]);
            var pending = await service.Permissions.AuthorizeAsync(request);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await service.Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            Assert.True((await service.Permissions.BeginExecutionAsync(pending.RequestId)).IsAllowed);
            var replay = await service.Permissions.BeginExecutionAsync(pending.RequestId);
            Assert.Equal(HomePermissionRequestState.Executing, replay.State);
            Assert.Equal("HOME_PERMISSION_NOT_AUTHORIZED", replay.Code);
            Assert.False(replay.IsAllowed);
            var duplicate = new HomeAppAiServices(new ModelProviderRegistry([]), store, caller, new Graph(), new Invocations(), [new Policy(), new Policy()]);
            Assert.Equal(HomePermissionRequestState.Denied, (await duplicate.Permissions.AuthorizeAsync(request)).State);
            Assert.Equal(HomePermissionRequestState.Denied, (await service.Permissions.AuthorizeAsync(request with
            { Scope = new("files", "invented.action", [new("file", "file-1")]) })).State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Local_only_home_factory_never_discovers_remote_catalogue()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-local-catalogue-" + Guid.NewGuid().ToString("N"));
        try
        {
            var remote = new RemoteProvider();
            var service = new HomeAppAiServices(new ModelProviderRegistry([remote]),
                new FileHomeCoreStateStore(Path.Combine(root, "home.json")),
                new("profile", "Profile", "os", "session", true), new Graph(), new Invocations());
            Assert.Empty(await service.GetModelsAsync(default));
            Assert.False(await service.SelectAsync("remote:model", default));
            Assert.Equal(0, remote.CatalogueCalls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class RemoteProvider : IModelProvider
    {
        public int CatalogueCalls;
        public string Id => "remote";
        public string DisplayName => "Remote";
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => false;
        public bool CanManageModels => false;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken ct)
        { CatalogueCalls++; return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Policy : IHomeActionPolicySource
    {
        public HomePermissionActionPolicy? TryGet(string app, string action) => app == "files" && action == "files.save"
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true) : null;
    }
    private sealed class Invocations : IInvocationResolver
    {
        public ValueTask<IReadOnlyList<InvocationToken>> ResolveAsync(IReadOnlyList<InvocationToken> tokens, CancellationToken ct) => ValueTask.FromResult(tokens);
    }
    private sealed class Graph : IExecutionEventRepository
    {
        public Task AppendAsync(IReadOnlyList<ExecutionEvent> events, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<ExecutionEvent>>([]);
        public Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<ExecutionSummary>>([]);
    }
}
