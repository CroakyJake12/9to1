using System.Runtime.CompilerServices;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class BoardsSectionIconSuggestionServiceTests
{
    private static AuthenticatedResourceActor Actor(string revision = "auth-1") =>
        new("actor-1", "profile-1", null, null, revision);

    private static ConfiguredModelRoute Route(long revision = 1) =>
        new(HomeModelPickerFeatureProvider.RouteId("profile-1", ModelCapabilityCategory.Chat), revision,
            ModelRouteScope.User, "profile-1", ModelCapabilityCategory.Chat,
            [new ModelRouteCandidate(new ModelIdentity("local-a", "m1"), Enabled: true, Order: 0,
                ReasoningLevelId: "reasoning-exact")], new ProviderPolicy());

    [Fact]
    public void Parser_accepts_a_bounded_set_of_roundrect_circle_and_cutout_polygon_options()
    {
        var geometry = BoardsSectionIconSuggestionService.ParseResponse("""
            {"icons":[
              {"shapes":[{"kind":"roundRect","x":3,"y":3,"width":18,"height":18,"radius":3}]},
              {"shapes":[{"kind":"circle","cx":12,"cy":12,"radius":4}]},
              {"shapes":[{"kind":"roundRect","x":3,"y":3,"width":18,"height":18,"radius":3},{"kind":"polygon","points":[[8,8],[16,8],[12,16]],"cutout":true}]}
            ]}
            """);

        Assert.Equal(3, geometry.Count);
        Assert.All(geometry, candidate => Assert.InRange(candidate.Shapes.Count, 1, 12));
    }

    [Theory]
    [InlineData("{\"icons\":[{\"shapes\":[{\"kind\":\"roundRect\",\"x\":-1,\"y\":3,\"width\":18,\"height\":18,\"radius\":3}]}]}")]
    [InlineData("{\"icons\":[{\"shapes\":[{\"kind\":\"roundRect\",\"x\":3,\"y\":3,\"width\":18,\"height\":18,\"radius\":3,\"url\":\"https://example.invalid\"}]}]}")]
    [InlineData("{\"icons\":[{\"shapes\":[{\"kind\":\"path\",\"d\":\"M0 0\"}]}]}")]
    [InlineData("{\"icons\":[]}")]
    [InlineData("{\"icons\":[{\"shapes\":[{\"kind\":\"roundRect\",\"x\":3,\"y\":3,\"width\":18,\"height\":18,\"radius\":3}]},{\"shapes\":[{\"kind\":\"circle\",\"cx\":12,\"cy\":12,\"radius\":4}]},{\"shapes\":[{\"kind\":\"circle\",\"cx\":12,\"cy\":12,\"radius\":4}]},{\"shapes\":[{\"kind\":\"circle\",\"cx\":12,\"cy\":12,\"radius\":4}]}]}")]
    public void Parser_rejects_unbounded_or_unapproved_model_output(string response)
    {
        Assert.Throws<InvalidDataException>(() => BoardsSectionIconSuggestionService.ParseResponse(response));
    }

    [Fact]
    public void Parser_rejects_oversized_output_before_json_parsing()
    {
        var response = "{" + new string(' ', 8192) + "}";
        Assert.Throws<InvalidDataException>(() => BoardsSectionIconSuggestionService.ParseResponse(response));
    }

    [Fact]
    public async Task Generation_uses_the_exact_home_selection_and_disables_memory_and_fallback()
    {
        var actor = Actor();
        var route = Route();
        var client = new CapturingModelClient();
        using var appLifetime = new CancellationTokenSource();
        using var tabLifetime = new CancellationTokenSource();
        var service = CreateService(client, () => actor, () => route, appLifetime.Token, tabLifetime.Token);

        var result = await service.GenerateAsync("Pure Mathematics", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("local-a", result.ProviderId);
        Assert.Equal("m1", result.ModelId);
        Assert.Single(result.Candidates);
        Assert.NotNull(client.Request);
        var request = client.Request!;
        Assert.Equal("local-a:m1", request.Model);
        Assert.Equal("reasoning-exact", request.ReasoningId);
        Assert.Equal("local-a", request.ReasoningTargetProviderId);
        Assert.Equal("m1", request.ReasoningTargetModelName);
        Assert.False(request.EnableTools);
        Assert.False(request.Options!.RequestedRoutingConstraints!.AllowFallback);
        Assert.False(request.Options.RequestedContextConstraints!.AllowPersistentMemoryRead);
        Assert.Equal(0, request.Options.ActionLimit);
        Assert.Equal("Create a filled section icon representing this section name: \"Pure Mathematics\"",
            Assert.Single(request.Messages).Content);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("route")]
    public async Task Generation_refuses_if_the_home_actor_or_route_changes_during_the_request(string change)
    {
        var actor = Actor();
        var route = Route();
        var client = new CapturingModelClient(() =>
        {
            if (change == "actor") actor = Actor("auth-2");
            else route = Route(2);
        });
        using var appLifetime = new CancellationTokenSource();
        using var tabLifetime = new CancellationTokenSource();
        var service = CreateService(client, () => actor, () => route, appLifetime.Token, tabLifetime.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync("Statistics", TestContext.Current.CancellationToken));

        Assert.NotNull(client.Request); // exactly one explicit local fake request; no fallback attempt
        Assert.False(client.Request!.Options!.RequestedRoutingConstraints!.AllowFallback);
        Assert.False(client.Request.Options.RequestedContextConstraints!.AllowPersistentMemoryRead);
    }

    private static BoardsSectionIconSuggestionService CreateService(CapturingModelClient client,
        Func<AuthenticatedResourceActor> actor, Func<ConfiguredModelRoute> route,
        CancellationToken appLifetime, CancellationToken tabLifetime)
    {
        var provider = new RouteModelProvider();
        var registry = new ModelProviderRegistry([provider]);
        var repository = new HomeVersionedModelRouteRepository(new FileHomeCoreStateStore(
            Path.Combine(Path.GetTempPath(), "boards-icon-route-" + Guid.NewGuid().ToString("N") + ".json")));
        var source = new HomeModelSelectionSource(registry, repository,
            (category, token) => Task.FromResult<ConfiguredModelRoute?>(
                category == ModelCapabilityCategory.Chat ? route() : null),
            _ => Task.FromResult<AuthenticatedResourceActor?>(actor()),
            (_, _, _) => Task.FromResult<IReadOnlyList<string>?>(["reasoning-exact"]));
        return new BoardsSectionIconSuggestionService(client, source, "profile-1", appLifetime,
            tabLifetime, () => !appLifetime.IsCancellationRequested && !tabLifetime.IsCancellationRequested);
    }

    private sealed class CapturingModelClient(Action? afterRequest = null) : IProviderModelClient
    {
        public OllamaChatRequest? Request { get; private set; }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            Empty(cancellationToken);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            afterRequest?.Invoke();
            return Task.FromResult("{\"icons\":[{\"shapes\":[{\"kind\":\"roundRect\",\"x\":3,\"y\":3,\"width\":18,\"height\":18,\"radius\":3}]}]}");
        }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Section icon suggestions do not use tools.");

        private static async IAsyncEnumerable<string> Empty([EnumeratorCancellation] CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class RouteModelProvider : IModelProvider
    {
        private static readonly IReadOnlyList<ProviderModelDescriptor> Models =
        [new("local-a", true, new ModelDescriptor("m1", 123, "test", "small", "Q4",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch))];

        public string Id => "local-a";
        public string DisplayName => "Local test model";
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => true;
        public bool CanManageModels => false;
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderHealthStatus(Id, true, "test", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Models);
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
