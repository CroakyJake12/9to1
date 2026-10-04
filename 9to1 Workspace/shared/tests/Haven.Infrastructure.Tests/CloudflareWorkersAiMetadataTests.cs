using System.Net;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

// Transport/configuration are doubles. The sealed catalogue fixture is an actual
// authenticated read-only GET response; these tests never call inference.
public sealed class CloudflareWorkersAiMetadataTests
{
    private const string Endpoint = "https://api.cloudflare.com/client/v4/accounts/8aea00ca3c4635410861efcc6a5f593b/ai/v1/";
    private static string ActualCatalogue()
    {
        var assembly = typeof(CloudflareWorkersAiMetadataTests).Assembly;
        var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith("cloudflare-workers-ai-catalogue-20261004.json", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task Actual_catalogue_preserves_provider_model_identity_and_context_without_granting_capabilities()
    {
        var handler = new Transport(_ => ActualCatalogue());
        var provider = Create(handler);
        var model = Assert.Single(await provider.GetModelsAsync(CancellationToken.None));
        Assert.Equal("openai-compatible", model.ProviderId);
        Assert.Equal("@cf/meta/llama-3.1-8b-instruct-fp8", model.Name);
        Assert.Equal(32000, model.ContextWindow);
        Assert.False(model.IsLocal);
        Assert.Equal(ToolCapability.Text, Assert.Single(model.Capabilities));
        Assert.Single(handler.Calls); // actual total_count=321 is not the filtered page size
        AssertMetadataRequest(handler.Calls[0], 1);
    }

    [Fact]
    public async Task Actual_catalogue_health_is_read_only_metadata_not_inference()
    {
        var handler = new Transport(_ => ActualCatalogue());
        Assert.True((await Create(handler).CheckHealthAsync(CancellationToken.None)).IsHealthy);
        AssertMetadataRequest(Assert.Single(handler.Calls), 1);
    }

    [Theory]
    [InlineData("https://api.cloudflare.com.evil.example/client/v4/accounts/8aea00ca3c4635410861efcc6a5f593b/ai/v1/")]
    [InlineData("https://example.com/client/v4/accounts/8aea00ca3c4635410861efcc6a5f593b/ai/v1/")]
    [InlineData("https://api.cloudflare.com:8443/client/v4/accounts/8aea00ca3c4635410861efcc6a5f593b/ai/v1/")]
    [InlineData("https://api.cloudflare.com/client/v4/accounts/not-account/ai/v1/")]
    [InlineData("https://api.cloudflare.com/client/v4/accounts/8aea00ca3c4635410861efcc6a5f593b/ai/v1/extra/")]
    [InlineData("https://api.cloudflare.com/client/v4/accounts/8aea00ca3c4635410861efcc6a5f593b/ai/v1/?key=value")]
    public async Task Other_endpoints_keep_original_OpenAI_discovery(string endpoint)
    {
        var handler = new Transport(_ => "{\"data\":[{\"id\":\"original-model\",\"context_length\":4096}]}");
        Assert.Equal("original-model", Assert.Single(await Create(handler, endpoint).GetModelsAsync(CancellationToken.None)).Name);
        Assert.DoesNotContain("models/search", Assert.Single(handler.Calls).Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("{\"success\":false,\"result\":[]}")]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"success\":true,\"result\":{}}")]
    [InlineData("{\"success\":true,\"result\":[{\"id\":\"wrong-identity\"}]}")]
    public async Task Unconfirmed_or_invalid_native_catalogue_is_not_healthy(string body)
    {
        var handler = new Transport(_ => body);
        var provider = Create(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetModelsAsync(CancellationToken.None));
        Assert.False((await provider.CheckHealthAsync(CancellationToken.None)).IsHealthy);
    }

    [Fact]
    public async Task HTTP_denial_and_malformed_JSON_do_not_become_healthy()
    {
        var denied = new Transport(_ => "denied", HttpStatusCode.Forbidden);
        Assert.False((await Create(denied).CheckHealthAsync(CancellationToken.None)).IsHealthy);
        Assert.False((await Create(new Transport(_ => "not-json")).CheckHealthAsync(CancellationToken.None)).IsHealthy);
    }

    [Fact]
    public async Task Pagination_uses_actual_page_length_and_deduplicates_stable_names()
    {
        var page = JsonSerializer.Serialize(new { success = true, result = Enumerable.Range(0, 100).Select(i => new { name = "@cf/test/model-" + i }) });
        var handler = new Transport(n => n == 1 ? page : "{\"success\":true,\"result\":[{\"name\":\"@cf/test/model-0\"}]}");
        Assert.Equal(100, (await Create(handler).GetModelsAsync(CancellationToken.None)).Count);
        Assert.Equal(2, handler.Calls.Count);
        AssertMetadataRequest(handler.Calls[1], 2);
    }

    [Fact]
    public async Task Oversized_provider_page_refuses_catalogue_and_health_without_followup_requests()
    {
        var page = JsonSerializer.Serialize(new { success = true, result = Enumerable.Range(0, 101).Select(i => new { name = "@cf/test/model-" + i }) });
        var models = new Transport(_ => page);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(models).GetModelsAsync(CancellationToken.None));
        Assert.Single(models.Calls);
        Assert.Contains("bounded page size", error.Message);
        var health = new Transport(_ => page);
        Assert.False((await Create(health).CheckHealthAsync(CancellationToken.None)).IsHealthy);
        Assert.Single(health.Calls);
    }

    [Fact]
    public async Task Repeated_full_pages_refuse_partial_catalogue_at_bound()
    {
        var page = JsonSerializer.Serialize(new { success = true, result = Enumerable.Range(0, 100).Select(i => new { name = "@cf/test/model-" + i }) });
        var handler = new Transport(_ => page);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(handler).GetModelsAsync(CancellationToken.None));
        Assert.Equal(32, handler.Calls.Count);
    }

    [Fact]
    public async Task Cancellation_and_disabled_configuration_prevent_transport()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var handler = new Transport(_ => ActualCatalogue());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(handler).GetModelsAsync(cancelled.Token));
        Assert.Empty(handler.Calls);
        Assert.Empty(await Create(handler, enabled: false).GetModelsAsync(CancellationToken.None));
        Assert.False((await Create(handler, enabled: false).CheckHealthAsync(CancellationToken.None)).IsHealthy);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Unknown_context_is_not_fabricated_and_price_does_not_grant_routing_capabilities()
    {
        var body = "{\"success\":true,\"result\":[{\"name\":\"@cf/test/model\",\"properties\":[{\"property_id\":\"context_window\",\"value\":\"unknown\"},{\"property_id\":\"price\",\"value\":0}]}]}";
        var model = Assert.Single(await Create(new Transport(_ => body)).GetModelsAsync(CancellationToken.None));
        Assert.Null(model.ContextWindow);
        Assert.Equal(ToolCapability.Text, Assert.Single(model.Capabilities));
    }

    private static void AssertMetadataRequest(Call call, int page)
    {
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Equal("https://api.cloudflare.com/client/v4/accounts/8aea00ca3c4635410861efcc6a5f593b/ai/models/search", call.Uri.GetLeftPart(UriPartial.Path));
        Assert.Contains("task=Text%20Generation", call.Uri.AbsoluteUri);
        Assert.Contains("per_page=100", call.Uri.Query);
        Assert.Contains("page=" + page, call.Uri.Query);
        Assert.Equal("Bearer local-test-placeholder", call.Authorization);
    }

    private static CustomOpenAiCompatibleModelProvider Create(Transport handler, string endpoint = Endpoint, bool enabled = true) =>
        new(new Factory(handler), new Store(new("openai-compatible", ModelProviderKind.OpenAICompatible,
            "Workers AI", endpoint, enabled, false, false, new Dictionary<string, string>(), DateTimeOffset.UtcNow)),
            new Secrets(), new ProviderUsageCaptureBuffer());
    private sealed record Call(HttpMethod Method, Uri Uri, string? Authorization);
    private sealed class Transport(Func<int, string> body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls.Add(new(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString()));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body(Calls.Count), Encoding.UTF8, "application/json") });
        }
    }
    private sealed class Factory(Transport handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class Store(ProviderConfiguration configuration) : IProviderConfigurationStore
    {
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult<ProviderConfiguration?>(configuration);
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>([configuration]);
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Secrets : IProviderSecretStore
    {
        public Task<string?> GetAsync(string id, string name, CancellationToken token) => Task.FromResult<string?>("local-test-placeholder");
        public Task SetAsync(string id, string name, string value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, string name, CancellationToken token) => throw new NotSupportedException();
    }
}
