using System.Net;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>Protects the explicit keyless loopback route without a real engine, model, or network.</summary>
public sealed class StrataLoopbackModelProviderTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8081/v1/")]
    [InlineData("https://127.0.0.1:8081/v1/")]
    [InlineData("http://[::1]:8081/v1/")]
    public void AcceptsOnlyExplicitLiteralLoopbackEndpoints(string endpoint) =>
        Assert.True(StrataLoopbackModelProvider.IsAllowedEndpoint(endpoint));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://localhost:8081/v1/")]
    [InlineData("http://127.0.0.2:8081/v1/")]
    [InlineData("http://0.0.0.0:8081/v1/")]
    [InlineData("http://192.168.1.2:8081/v1/")]
    [InlineData("http://127.0.0.1.example.org:8081/v1/")]
    [InlineData("http://user:secret@127.0.0.1:8081/v1/")]
    [InlineData("http://127.0.0.1:8081/v1/?key=secret")]
    [InlineData("http://127.0.0.1:8081/v1/#fragment")]
    [InlineData("http://127.0.0.1:8081/other/")]
    [InlineData("ftp://127.0.0.1:8081/v1/")]
    public void RejectsRemoteAmbiguousOrCredentialBearingEndpoints(string? endpoint) =>
        Assert.False(StrataLoopbackModelProvider.IsAllowedEndpoint(endpoint));

    [Fact]
    public async Task PassiveLoadedCatalogueIsLocalKeylessAndUsesActualContextWithoutClaimingTools()
    {
        using var fixture = new Fixture();
        var provider = fixture.Provider;
        var health = await provider.CheckHealthAsync(CancellationToken.None);
        var models = await provider.GetModelsAsync(CancellationToken.None);

        Assert.Equal("strata-local", provider.Id);
        Assert.True(provider.IsLocal);
        Assert.True(health.IsHealthy);
        var model = Assert.Single(models);
        Assert.Equal(fixture.Model, model.Name);
        Assert.True(model.IsLocal);
        Assert.Equal(32768, model.ContextWindow);
        Assert.DoesNotContain(ToolCapability.Tools, model.Capabilities);
        Assert.Equal(0, fixture.Secrets.Reads);
        Assert.All(fixture.Requests, request =>
        {
            Assert.Equal("GET", request.Method);
            Assert.Null(request.Authorization);
            Assert.StartsWith("/v1/", request.Path, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task UnloadedStatusDoesNotInventAReadyModelEvenWhenCatalogueRetainsAnEntry()
    {
        using var fixture = new Fixture { Loaded = false };
        var health = await fixture.Provider.CheckHealthAsync(CancellationToken.None);
        var models = await fixture.Provider.GetModelsAsync(CancellationToken.None);

        Assert.True(health.IsHealthy);
        Assert.Contains("unloaded", health.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(models);
        Assert.Equal(0, fixture.Secrets.Reads);
        Assert.DoesNotContain(fixture.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task MissingActualCatalogueDoesNotCreateAConfiguredPlaceholderModel()
    {
        using var fixture = new Fixture { EmptyCatalogue = true };
        Assert.Empty(await fixture.Provider.GetModelsAsync(CancellationToken.None));
        Assert.DoesNotContain(fixture.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task CancelledHealthReadPropagatesCancellationWithoutNetworkOrSecrets()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Provider.CheckHealthAsync(cancellation.Token));
        Assert.Empty(fixture.Requests);
        Assert.Equal(0, fixture.Secrets.Reads);
    }

    [Fact]
    public async Task UnsafeConfigurationCannotReachTheHttpFactoryOrSecretStore()
    {
        using var fixture = new Fixture("https://remote.example/v1/");
        Assert.False((await fixture.Provider.CheckHealthAsync(CancellationToken.None)).IsHealthy);
        Assert.Empty(fixture.Requests);
        Assert.Equal(0, fixture.Secrets.Reads);
    }

    [Fact]
    public void DedicatedTransportDisablesRedirectsProxiesAndCookies()
    {
        using var handler = StrataLoopbackModelProvider.CreateTransportHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.False(handler.UseCookies);
        Assert.NotNull(handler.ConnectCallback);
    }

    [Fact]
    public async Task ExplicitMatchingNonceProbeEnablesToolsUntilTheObservedServiceRestarts()
    {
        using var fixture = new Fixture { AllowProbe = true };
        await fixture.Provider.ProbeCapabilitiesAsync(CancellationToken.None);
        Assert.Contains(ToolCapability.Tools, Assert.Single(await fixture.Provider.GetModelsAsync(CancellationToken.None)).Capabilities);
        Assert.Single(fixture.Requests.Where(request => request.Method == "POST"));
        fixture.ServiceStarted++;
        Assert.DoesNotContain(ToolCapability.Tools, Assert.Single(await fixture.Provider.GetModelsAsync(CancellationToken.None)).Capabilities);
        Assert.Single(fixture.Requests.Where(request => request.Method == "POST"));
        Assert.Equal(0, fixture.Secrets.Reads);
    }

    [Fact]
    public async Task WrongNonceNeverAdvertisesTools()
    {
        using var fixture = new Fixture { AllowProbe = true, WrongNonce = true };
        await fixture.Provider.ProbeCapabilitiesAsync(CancellationToken.None);
        Assert.DoesNotContain(ToolCapability.Tools, Assert.Single(await fixture.Provider.GetModelsAsync(CancellationToken.None)).Capabilities);
        Assert.Single(fixture.Requests.Where(request => request.Method == "POST"));
    }

    [Fact]
    public async Task RestartDuringProbeCannotCommitStaleCapabilityEvidence()
    {
        using var fixture = new Fixture { AllowProbe = true, RestartAfterProbe = true };
        await fixture.Provider.ProbeCapabilitiesAsync(CancellationToken.None);
        Assert.DoesNotContain(ToolCapability.Tools, Assert.Single(await fixture.Provider.GetModelsAsync(CancellationToken.None)).Capabilities);
        Assert.Single(fixture.Requests.Where(request => request.Method == "POST"));
    }

    [Fact]
    public async Task MissingServiceFingerprintCannotStartOrCacheAnExplicitProbe()
    {
        using var fixture = new Fixture { AllowProbe = true, ServiceStarted = null };
        await fixture.Provider.ProbeCapabilitiesAsync(CancellationToken.None);
        Assert.DoesNotContain(ToolCapability.Tools, Assert.Single(await fixture.Provider.GetModelsAsync(CancellationToken.None)).Capabilities);
        Assert.DoesNotContain(fixture.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task BusyServiceCannotStartAnExplicitProbe()
    {
        using var fixture = new Fixture { AllowProbe = true, InFlight = 1 };
        Assert.False((await fixture.Provider.ProbeCapabilitiesAsync(CancellationToken.None)).IsHealthy);
        Assert.DoesNotContain(fixture.Requests, request => request.Method == "POST");
    }

    [Fact]
    public async Task ChangingTheConfiguredModelInvalidatesPriorToolEvidence()
    {
        using var fixture = new Fixture { AllowProbe = true };
        await fixture.Provider.ProbeCapabilitiesAsync(CancellationToken.None);
        Assert.Contains(ToolCapability.Tools, Assert.Single(await fixture.Provider.GetModelsAsync(CancellationToken.None)).Capabilities);
        fixture.Model = "different-actual-model";
        Assert.DoesNotContain(ToolCapability.Tools, Assert.Single(await fixture.Provider.GetModelsAsync(CancellationToken.None)).Capabilities);
        Assert.Single(fixture.Requests.Where(request => request.Method == "POST"));
    }

    private sealed record RequestRecord(string Method, string Path, string? Authorization, string Body);

    private sealed class Fixture : IHttpClientFactory, IDisposable
    {
        private readonly Handler _handler;
        public string Model { get; set; } = "actual-local-model";
        public bool Loaded { get; set; } = true;
        public bool EmptyCatalogue { get; set; }
        public int InFlight { get; set; }
        public bool AllowProbe { get; set; }
        public bool WrongNonce { get; set; }
        public bool RestartAfterProbe { get; set; }
        public long? ServiceStarted { get; set; } = 1792872000;
        public List<RequestRecord> Requests { get; } = [];
        public SecretStore Secrets { get; } = new();
        public StrataLoopbackModelProvider Provider { get; }

        public Fixture(string endpoint = "http://127.0.0.1:8081/v1/")
        {
            _handler = new Handler(this);
            Provider = new(this, new ConfigurationStore(endpoint), Secrets, new ProviderUsageCaptureBuffer());
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        public void Dispose() => _handler.Dispose();

        private sealed class Handler(Fixture owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = request.RequestUri?.AbsolutePath ?? throw new InvalidOperationException("Missing request URI");
                var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
                owner.Requests.Add(new(request.Method.Method, path, request.Headers.Authorization?.ToString(), body));
                if (request.Method == HttpMethod.Post && path == "/v1/chat/completions" && owner.AllowProbe)
                {
                    using var payload = JsonDocument.Parse(body);
                    Assert.Equal(owner.Model, payload.RootElement.GetProperty("model").GetString());
                    var tool = payload.RootElement.GetProperty("tools")[0].GetProperty("function");
                    Assert.Equal("echo_probe", tool.GetProperty("name").GetString());
                    var nonce = System.Text.RegularExpressions.Regex.Match(body, "[a-f0-9]{32}").Value;
                    Assert.NotEmpty(nonce);
                    var arguments = JsonSerializer.Serialize(new { marker = owner.WrongNonce ? "wrong-marker" : nonce });
                    if (owner.RestartAfterProbe) owner.ServiceStarted++;
                    return Json(new { choices = new[] { new { finish_reason = "tool_calls", message = new {
                        content = "", tool_calls = new[] { new { id = "actual-probe-call", type = "function",
                            function = new { name = "echo_probe", arguments } } } } } } });
                }
                if (request.Method != HttpMethod.Get)
                    throw new InvalidOperationException("Passive provider operation attempted generation");
                if (path == "/v1/status")
                {
                    var status = new Dictionary<string, object?> { ["service"] = "strata", ["model"] = owner.Model,
                        ["loaded"] = owner.Loaded, ["context"] = new { native = 32768 },
                        ["activity"] = new { requests = 0, in_flight = owner.InFlight } };
                    if (owner.ServiceStarted is { } started) status["started"] = started;
                    return Json(status);
                }
                if (path == "/v1/models")
                    return Json(new { data = owner.EmptyCatalogue ? Array.Empty<object>() :
                        new object[] { new { id = owner.Model, meta = new { n_ctx = 32768 } } } });
                throw new InvalidOperationException("Unexpected provider endpoint: " + path);
            }
        }
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    private sealed class ConfigurationStore(string endpoint) : IProviderConfigurationStore
    {
        private readonly ProviderConfiguration _configuration = new("strata-local", ModelProviderKind.OpenAICompatible,
            "Strata (local)", endpoint, true, true, false, new Dictionary<string, string>(), DateTimeOffset.UtcNow);
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<ProviderConfiguration>>([_configuration]); }
        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult<ProviderConfiguration?>(providerId == "strata-local" ? _configuration : null); }
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class SecretStore : IProviderSecretStore
    {
        public int Reads { get; private set; }
        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken token)
        { Reads++; throw new InvalidOperationException("A local Strata route must not request an API key"); }
        public Task SetAsync(string providerId, string secretName, string secret, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, string secretName, CancellationToken token) => throw new NotSupportedException();
    }
}
