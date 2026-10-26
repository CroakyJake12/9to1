using System.Runtime.CompilerServices;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Dashboard;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed class DashboardEditPlannerTests
{
    [Fact]
    public async Task Planner_accepts_only_bounded_operations_for_known_widgets()
    {
        var selection = new SelectionFixture();
        selection.Provider.Response = """
            {"summary":"Make Plan prominent","operations":[
              {"action":"move","key":"plan","column":0,"row":0},
              {"action":"resize","key":"plan","width":6,"height":2},
              {"action":"hide","key":"browse"}
            ]}
            """;
        var local = new FakeModelClient("unused");
        var model = new ProviderRoutingModelClient(local, selection.Providers, new PrivacyPreferencesStore());
        var planner = new DashboardEditPlanner(model, selection.Source);
        var result = await planner.PlanAsync("Make plan the main widget", "Home", Views(), Layout(), CancellationToken.None);
        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(result.Plan);
        Assert.Equal(3, result.Plan.Operations.Count);
        Assert.Equal(6, result.Plan.Operations[1].Width);
        Assert.Equal("route-model", selection.Provider.LastRequest?.Model); // Production bridge strips provider qualification only after exact provider resolution.
        Assert.Equal("medium", selection.Provider.LastRequest?.ReasoningId);
        Assert.Equal("medium", selection.Provider.LastReasoningId);
        Assert.Equal("test-provider", selection.Provider.LastRequest?.ReasoningTargetProviderId);
        Assert.Equal("route-model", selection.Provider.LastRequest?.ReasoningTargetModelName);
        Assert.Equal(false, selection.Provider.LastRequest!.Options!.RequestedRoutingConstraints!.AllowFallback);
        Assert.Equal(false, selection.Provider.LastRequest.Options.RequestedContextConstraints!.AllowPersistentMemoryRead);
        Assert.Equal(1, selection.Provider.NamedReasoningStreamCalls);
        Assert.Equal(0, selection.Provider.OrdinaryStreamCalls);
        Assert.Equal(0, selection.Provider.CompleteCalls);
        Assert.Equal(0, local.StreamCalls); // The local Ollama bridge was not selected.
    }

    [Fact]
    public void Parser_rejects_unknown_widgets_and_unsupported_actions()
    {
        var unknown = DashboardEditPlanner.ParseAndValidate(
            "{\"summary\":\"bad\",\"operations\":[{\"action\":\"move\",\"key\":\"missing\",\"column\":0,\"row\":0}]}", Views());
        Assert.False(unknown.Succeeded);

        var executable = DashboardEditPlanner.ParseAndValidate(
            "{\"summary\":\"bad\",\"operations\":[{\"action\":\"launch\",\"key\":\"plan\"}]}", Views());
        Assert.False(executable.Succeeded);
    }

    [Fact]
    public void Applier_combines_validated_operations_before_the_canvas_commit()
    {
        var definitions = Views().Select(view => view.Definition).ToArray();
        var plan = new DashboardEditPlan("Focus the page",
        [
            new DashboardEditOperation("move", "browse", Column: 0, Row: 3),
            new DashboardEditOperation("resize", "plan", Width: 6, Height: 2),
            new DashboardEditOperation("hide", "browse"),
            new DashboardEditOperation("rename-page", Title: "Today")
        ]);
        var applied = DashboardEditPlanApplier.Apply(plan, "Home", definitions, Layout());
        Assert.Equal("Today", applied.Title);
        Assert.Equal(6, applied.Layout.Single(item => item.Key == "plan").Width);
        Assert.False(applied.Layout.Single(item => item.Key == "browse").IsVisible);
        Assert.Equal(3, applied.Layout.Single(item => item.Key == "browse").Row);
    }

    [Fact]
    public async Task Planner_fails_without_a_configured_Home_selection_and_never_uses_an_arbitrary_model()
    {
        var model = new FakeModelClient("{}");
        var planner = new DashboardEditPlanner(model, new SelectionFixture(hasRoute: false).Source);
        var result = await planner.PlanAsync("Move Plan", "Home", Views(), Layout(), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Contains("Home model", result.Message);
        Assert.Equal(0, model.CompleteCalls);
        Assert.Equal(0, model.StreamCalls);
        Assert.Equal(0, model.CatalogueCalls);
    }

    [Fact]
    public async Task Planner_reports_an_unavailable_route_model_without_choosing_a_discovered_alternative()
    {
        var model = new FakeModelClient("{}");
        var selection = new SelectionFixture(hasRoute: true, hasSelectedModel: false);
        var planner = new DashboardEditPlanner(model, selection.Source);

        var result = await planner.PlanAsync("Move Plan", "Home", Views(), Layout(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("model discovery", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, model.CompleteCalls);
        Assert.Equal(0, model.StreamCalls);
        Assert.Equal(0, model.CatalogueCalls);
    }

    [Fact]
    public async Task Planner_rejects_the_plan_if_the_Home_route_changes_during_generation()
    {
        var selection = new SelectionFixture();
        var model = new FakeModelClient("""
            {"summary":"Move Plan","operations":[{"action":"move","key":"plan","column":1,"row":1}]}
            """, () => selection.ChangeModel("other-model"));
        var planner = new DashboardEditPlanner(model, selection.Source);

        var result = await planner.PlanAsync("Move Plan", "Home", Views(), Layout(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Plan);
        Assert.Contains("route changed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("test-provider:route-model", model.LastRequest?.Model);
    }

    [Fact]
    public async Task Planner_bounds_stream_accumulation_before_parsing()
    {
        var model = new FakeModelClient(new string('x', 65 * 1024));
        var planner = new DashboardEditPlanner(model, new SelectionFixture().Source);

        var result = await planner.PlanAsync("Move Plan", "Home", Views(), Layout(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Plan);
        Assert.Contains("size limit", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DashboardWidgetViewState> Views() =>
    [
        new(new DashboardTileDefinition("plan", "Plan", "Tasks", "plan", "plan", "plan"), new DashboardTileData("2", "Due"), DashboardWidgetDataState.Ready),
        new(new DashboardTileDefinition("browse", "Browse", "Browser", "browse", "action", "browse"), new DashboardTileData("Open", "Private"), DashboardWidgetDataState.Ready)
    ];

    private static IReadOnlyList<DashboardWidgetPlacement> Layout() =>
    [
        new("plan", 0, 0, 3, 2),
        new("browse", 3, 0, 3, 2)
    ];

    private sealed class SelectionFixture
    {
        private readonly AuthenticatedResourceActor _actor = new("dashboard-test-actor", "dashboard-test-profile", null, null, "auth-1");
        private ConfiguredModelRoute? _route;
        internal HomeModelSelectionSource Source { get; }

        internal SelectionFixture(bool hasRoute = true, bool hasSelectedModel = true)
        {
            _route = hasRoute ? CreateRoute("route-model", revision: 1) : null;
            Provider = hasSelectedModel
                ? new FakeProvider("test-provider", "catalog-first", "route-model", "other-model")
                : new FakeProvider("test-provider", "catalog-first", "other-model");
            Providers = new ModelProviderRegistry([Provider]);
            Source = new HomeModelSelectionSource(Providers, new InMemoryModelRouteRepository(),
                (category, _) => Task.FromResult(category == ModelCapabilityCategory.Chat ? _route : null),
                _ => Task.FromResult<AuthenticatedResourceActor?>(_actor),
                (_, _, _) => Task.FromResult<IReadOnlyList<string>?>(["low", "medium", "high"]));
        }

        internal IModelProviderRegistry Providers { get; }
        internal FakeProvider Provider { get; }

        internal void ChangeModel(string modelId) => _route = CreateRoute(modelId, revision: 2);

        private ConfiguredModelRoute CreateRoute(string modelId, long revision) =>
            new("dashboard-user-chat", revision, ModelRouteScope.User, _actor.ProfileId, ModelCapabilityCategory.Chat,
                [new(new ModelIdentity("test-provider", modelId), Enabled: true, Order: 0, ReasoningLevelId: "medium")],
                new ProviderPolicy());
    }

    private sealed class FakeProvider(string id, params string[] modelNames) : INamedReasoningProvider
    {
        private readonly IReadOnlyList<ProviderModelDescriptor> _models = modelNames.Select(name =>
            new ProviderModelDescriptor(id, true, new ModelDescriptor(name, 8192, "fixture", "1B", "Q4",
                new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch))).ToArray();

        public string Id => id;
        internal string Response { get; set; } = """{"summary":"Move Plan","operations":[{"action":"move","key":"plan","column":1,"row":1}]}""";
        internal OllamaChatRequest? LastRequest { get; private set; }
        internal string? LastReasoningId { get; private set; }
        internal int NamedReasoningStreamCalls { get; private set; }
        internal int OrdinaryStreamCalls { get; private set; }
        internal int CompleteCalls { get; private set; }
        public string DisplayName => id;
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => true;
        public bool CanManageModels => false;
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderHealthStatus(id, true, "Fixture", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_models);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        { OrdinaryStreamCalls++; await Task.Yield(); yield break; }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        { CompleteCalls++; return Task.FromException<string>(new InvalidOperationException("The provider bridge must use the named-reasoning stream.")); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromException<OllamaToolResponse>(new InvalidOperationException("The planner does not dispatch tools."));

        public async IAsyncEnumerable<string> StreamChatWithReasoningAsync(OllamaChatRequest request, string reasoningId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            NamedReasoningStreamCalls++;
            LastRequest = request;
            LastReasoningId = reasoningId;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var split = Response.Length / 2;
            yield return Response[..split];
            yield return Response[split..];
        }

        public Task<OllamaToolResponse> ChatWithToolsWithReasoningAsync(OllamaToolRequest request, string reasoningId,
            CancellationToken cancellationToken) =>
            Task.FromException<OllamaToolResponse>(new NotSupportedException("The planner does not dispatch tools."));
    }

    private sealed class FakeModelClient(string response, Action? onComplete = null) : IProviderModelClient
    {
        public OllamaChatRequest? LastRequest { get; private set; }
        public int CatalogueCalls { get; private set; }
        public int CompleteCalls { get; private set; }
        public int StreamCalls { get; private set; }
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            CountCatalogueCall();
        private Task<IReadOnlyList<ModelDescriptor>> CountCatalogueCall()
        {
            CatalogueCalls++;
            return Task.FromResult<IReadOnlyList<ModelDescriptor>>([
                new ModelDescriptor("arbitrary-first-model", 1, "test", "1B", "Q4", new HashSet<ToolCapability>(), DateTimeOffset.UtcNow)]);
        }
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastRequest = request;
            StreamCalls++;
            await Task.Yield();
            onComplete?.Invoke();
            yield return response;
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            CompleteCalls++;
            onComplete?.Invoke();
            return Task.FromResult(response);
        }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(string.Empty, []));
    }

    private sealed class PrivacyPreferencesStore : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
