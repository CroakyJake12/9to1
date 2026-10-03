using System.Net;
using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

// Real production provider and routing implementations, isolated fake transport.
public sealed class CloudModelProviderRoutingFailureTests
{
    [Theory]
    [InlineData("")]
    [InlineData("data: {\"error\":{\"message\":\"private error detail\"}}\n\n")]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\ndata: {\"choices\":[],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":2}}\n\n")]
    public async Task Incomplete_actual_provider_stream_never_dispatches_configured_fallback(string body)
    {
        var firstTransport = new Transport(body);
        var fallbackTransport = new Transport("data: {\"choices\":[{\"delta\":{\"content\":\"must not run\"}}]}\n\ndata: [DONE]\n");
        var usage = new ProviderUsageCaptureBuffer();
        var store = new Store();
        var first = new OpenAiModelProvider(firstTransport, store, new Secrets(), usage);
        var second = new CustomOpenAiCompatibleModelProvider(fallbackTransport, store, new Secrets(), usage);
        var registry = new ModelProviderRegistry([first, second]);
        var privacy = new Privacy();
        var routing = new ResilientProviderRoutingModelClient(
            new ProviderRoutingModelClient(new LocalClient(), registry, privacy), registry, store, privacy);
        var chunks = new List<string>();
        var error = await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var chunk in routing.StreamChatAsync(new("openai:test-model", [], EffortLevel.Medium), CancellationToken.None))
                chunks.Add(chunk);
        });
        Assert.DoesNotContain("private error detail", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, firstTransport.Dispatches);
        Assert.Equal(0, fallbackTransport.Dispatches);
        Assert.True(fallbackTransport.Discoveries > 0); // Real eligible fallback was offered.
        if (body.Contains("prompt_tokens", StringComparison.Ordinal))
        {
            Assert.Equal(["partial"], chunks);
            var observed = Assert.IsType<ProviderUsageSnapshot>(usage.Consume("openai", "test-model"));
            Assert.Equal(5, observed.InputTokens);
            Assert.Equal(2, observed.OutputTokens);
            Assert.Equal(UsageMeasurementKind.ProviderConfirmed, observed.Measurement);
            Assert.Null(observed.CachedTokens);
            Assert.Null(observed.ReasoningTokens);
        }
        else
        {
            Assert.Empty(chunks);
            Assert.Null(usage.Consume("openai", "test-model"));
        }
        Assert.Null(usage.Consume("openai-compatible", "test-model"));
    }

    private sealed class Transport(string body) : IHttpClientFactory
    {
        public int Dispatches { get; private set; }
        public int Discoveries { get; private set; }
        public HttpClient CreateClient(string name) => new(new Handler(this, body));
        private sealed class Handler(Transport owner, string body) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (request.Method == HttpMethod.Get)
                {
                    Assert.EndsWith("/models", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                    owner.Discoveries++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"test-model\"}]}") });
                }
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.EndsWith("/chat/completions", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
                owner.Dispatches++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/event-stream") });
            }
        }
    }

    private sealed class Store : IProviderConfigurationStore
    {
        private static ProviderConfiguration Config(string id) => new(id, ModelProviderKind.OpenAICompatible, id, "https://provider.test/v1/", true, false, true,
            new Dictionary<string, string> { ["fallback-chain"] = "openai-compatible:test-model" }, DateTimeOffset.UnixEpoch);
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>([Config("openai"), Config("openai-compatible")]);
        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken) => Task.FromResult<ProviderConfiguration?>(Config(providerId));
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Secrets : IProviderSecretStore
    {
        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken) => Task.FromResult<string?>("isolated-fixture-only");
        public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current => PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class LocalClient : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return await Task.FromException<string>(new InvalidOperationException("Local dispatch must not occur in this remote route fixture."));
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
