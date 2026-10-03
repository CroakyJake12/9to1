using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Android;

public sealed class RoutedKeyboardAiExecutorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-keyboard-routes-" + Guid.NewGuid().ToString("N"));
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly string _profileId;
    private readonly string _routeId;
    public RoutedKeyboardAiExecutorTests()
    {
        Directory.CreateDirectory(_root);
        _profiles = new(new FileHomeCoreStateStore(Path.Combine(_root, "home.json")), new Principal());
        _profileId = _profiles.GetCurrentAsync(default).AsTask().GetAwaiter().GetResult()!.ProfileId;
        _routeId = HomeModelPickerFeatureProvider.RouteId(_profileId, ModelCapabilityCategory.Active);
    }
    public void Dispose() => Directory.Delete(_root, true);
    private sealed class Principal : ITrustedHostPrincipalSource
    { public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) => ValueTask.FromResult<string?>("keyboard-test-os-principal"); }

    [Fact]
    public async Task LocalOnlyConsentFiltersRemoteBeforeDispatchAndHonoursOrder()
    {
        var remote = new Provider("remote", false);
        var local = new Provider("local", true);
        var routes = await Routes(new("remote", "model"), new("local", "model"));
        var executor = Executor([remote, local], routes, () => false);
        Assert.Equal("local:result", await executor.CompleteAsync("private text", default));
        Assert.Equal(0, remote.Calls);
        Assert.Equal(1, local.Calls);
    }

    [Fact]
    public async Task CloudConsentCannotWidenCanonicalRoutePrivacy()
    {
        var remote = new Provider("remote", false);
        var routes = await Routes(new ModelIdentity("remote", "model"));
        var route = (await routes.GetAsync(_routeId, default))!;
        await routes.TrySaveAsync(route with { Revision = 2, Policy = route.Policy with { AllowPrivateContextToCloud = false } }, 1, default);
        var executor = Executor([remote], routes, () => true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.CompleteAsync("private text", default));
        Assert.Equal(0, remote.Calls);
    }

    [Fact]
    public async Task LocalFailureCannotFallBackToCloudWithoutConsent()
    {
        var local = new Provider("local", true) { Failure = new HttpRequestException("unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable) };
        var remote = new Provider("remote", false);
        var executor = Executor([local, remote], await Routes(new("local", "model"), new("remote", "model")), () => false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.CompleteAsync("private text", default));
        Assert.Equal(1, local.Calls);
        Assert.Equal(0, remote.Calls);
    }

    [Fact]
    public async Task ClassifiedFailureFallsThroughToNextEligibleLocalModel()
    {
        var one = new Provider("one", true) { Failure = new HttpRequestException("unavailable", null, System.Net.HttpStatusCode.ServiceUnavailable) };
        var two = new Provider("two", true);
        var executor = Executor([one, two], await Routes(new("one", "model"), new("two", "model")), () => false);
        Assert.Equal("two:result", await executor.CompleteAsync("private text", default));
        Assert.Equal(1, one.Calls);
    }

    [Fact]
    public async Task AuthenticationFailureDoesNotFallThrough()
    {
        var one = new Provider("one", true) { Failure = new HttpRequestException("unauthorised", null, System.Net.HttpStatusCode.Unauthorized) };
        var two = new Provider("two", true);
        var executor = Executor([one, two], await Routes(new("one", "model"), new("two", "model")), () => false);
        await Assert.ThrowsAsync<HttpRequestException>(() => executor.CompleteAsync("private text", default));
        Assert.Equal(0, two.Calls);
    }

    [Fact]
    public async Task CanonicalSelectionIsRereadForEveryRequest()
    {
        var one = new Provider("one", true);
        var two = new Provider("two", true);
        var routes = await Routes(new ModelIdentity("one", "model"));
        var executor = Executor([one, two], routes, () => false);
        Assert.Equal("one:result", await executor.CompleteAsync("private text", default));
        var route = (await routes.GetAsync(_routeId, default))!;
        await routes.TrySaveAsync(route with { Revision = 2, Candidates = [new(new("two", "model"))] }, 1, default);
        Assert.Equal("two:result", await executor.CompleteAsync("private text", default));
    }

    [Fact]
    public async Task NetworkEndpointCannotPretendToBeDeviceLocal()
    {
        var provider = new Provider("local", true);
        var configurations = new Configurations(new("local", ModelProviderKind.OpenAICompatible, "Configured endpoint", "https://remote.example/", true, true, false, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch));
        var executor = Executor([provider], await Routes(new ModelIdentity("local", "model")), () => false, configurations);
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.CompleteAsync("private text", default));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task GlobalLocalOnlyPrivacyNarrowsCloudConsent()
    {
        var remote = new Provider("remote", false);
        var local = new Provider("local", true);
        var executor = Executor([remote, local], await Routes(new("remote", "model"), new("local", "model")), () => true,
            privacy: new Privacy { Current = PrivacyPreferences.Default with { LocalOnlyMode = true } });
        Assert.Equal("local:result", await executor.CompleteAsync("private text", default));
        Assert.Equal(0, remote.Calls);
    }

    [Fact]
    public async Task LegacyGlobalRouteCannotSupplyKeyboardSelection()
    {
        var provider = new Provider("local", true);
        var routes = new InMemoryModelRouteRepository();
        await routes.TrySaveAsync(new("home.active", 1, ModelRouteScope.User, "native-user", ModelCapabilityCategory.Active,
            [new(new("local", "model"))], new()), 0, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Executor([provider], routes, () => false).CompleteAsync("private", default));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task PersonalChatRouteTakesPrecedenceOverActive()
    {
        var active = new Provider("active", true); var chat = new Provider("chat", true);
        var routes = await Routes(new ModelIdentity("active", "model"));
        await routes.TrySaveAsync(new(HomeModelPickerFeatureProvider.RouteId(_profileId, ModelCapabilityCategory.Chat), 1,
            ModelRouteScope.User, _profileId, ModelCapabilityCategory.Chat, [new(new("chat", "model"))], new()), 0, default);
        Assert.Equal("chat:result", await Executor([active, chat], routes, () => false).CompleteAsync("private", default));
        Assert.Equal(0, active.Calls);
    }

    private RoutedKeyboardAiExecutor Executor(IModelProvider[] providers, IVersionedModelRouteRepository routes,
        Func<bool> cloud, Configurations? configurations = null, Privacy? privacy = null) =>
        new(new ModelProviderRegistry(providers), new HomePersonalModelRoutes(_profiles, routes,
            new ResourceAuthorizationService(_profiles, [new HomeModelRouteOwner(_profiles, routes), new HomeModelRouteProfileOwner(_profiles)])),
            configurations ?? new(), privacy ?? new(), cloud);

    private async Task<InMemoryModelRouteRepository> Routes(params ModelIdentity[] candidates)
    {
        var routes = new InMemoryModelRouteRepository();
        await routes.TrySaveAsync(new(_routeId, 1, ModelRouteScope.User, _profileId, ModelCapabilityCategory.Active,
            candidates.Select((candidate, index) => new ModelRouteCandidate(candidate, true, index)).ToArray(),
            new(AllowCloud: true, AllowPrivateContextToCloud: true)), 0, default);
        return routes;
    }

    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken) { Current = preferences; return Task.CompletedTask; }
    }

    private sealed class Configurations(ProviderConfiguration? configuration = null) : IProviderConfigurationStore
    {
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>(configuration is null ? [] : [configuration]);
        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken) => Task.FromResult(configuration?.Id == providerId ? configuration : null);
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Provider(string id, bool local, string[]? names = null) : IModelProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public ModelProviderKind Kind => ModelProviderKind.OpenAICompatible;
        public bool IsLocal => local;
        public bool CanManageModels => false;
        public int Calls { get; private set; }
        public string? LastModel { get; private set; }
        public Exception? Failure { get; init; }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>(
            (names ?? ["model"]).Select(name => new ProviderModelDescriptor(id, local, new(name, 0, "test", "", "", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch))).ToArray());
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken)
        {
            Calls++; LastModel = request.Model;
            return Failure is null ? Task.FromResult(id + ":result") : Task.FromException<string>(Failure);
        }
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
