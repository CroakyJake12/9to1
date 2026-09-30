using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Android;

public sealed class RoutedKeyboardAiExecutorTests
{
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
        var route = (await routes.GetAsync("home.active", default))!;
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
        var route = (await routes.GetAsync("home.active", default))!;
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
    public async Task LegacySelectedModelIsLiveAndHandlesColonInLocalModelName()
    {
        var provider = new Provider("ollama", true, ["qwen3:8b", "llama3:8b"]);
        var selected = "qwen3:8b";
        var executor = Executor([provider], new InMemoryModelRouteRepository(), () => false, current: () => selected);
        await executor.CompleteAsync("private text", default);
        Assert.Equal("qwen3:8b", provider.LastModel);
        selected = "llama3:8b";
        await executor.CompleteAsync("private text", default);
        Assert.Equal("llama3:8b", provider.LastModel);
    }

    private static RoutedKeyboardAiExecutor Executor(IModelProvider[] providers, IVersionedModelRouteRepository routes,
        Func<bool> cloud, Configurations? configurations = null, Privacy? privacy = null, Func<string?>? current = null) =>
        new(new ModelProviderRegistry(providers), routes, configurations ?? new(), privacy ?? new(), current ?? (() => null), cloud);

    private static async Task<InMemoryModelRouteRepository> Routes(params ModelIdentity[] candidates)
    {
        var routes = new InMemoryModelRouteRepository();
        await routes.TrySaveAsync(new("home.active", 1, ModelRouteScope.User, "native-user", ModelCapabilityCategory.Active,
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
