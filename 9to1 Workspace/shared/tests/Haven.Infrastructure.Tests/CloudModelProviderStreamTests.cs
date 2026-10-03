using System.Net;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

// Local transport fixtures exercise the production provider class; these tests
// do not establish credentialed provider or deployed gateway acceptance.
public sealed class CloudModelProviderStreamTests
{
    private const string Chunk = "data: {\"choices\":[{\"delta\":{\"content\":\"partial text\"}}]}\n\n";
    private static readonly OllamaChatRequest Request = new("test-model", [], EffortLevel.Medium);

    [Fact]
    public async Task Completion_marker_preserves_content_and_ignores_trailing_transport_data()
    {
        var factory = new Factory(Chunk + "data: [DONE]\n\ndata: invalid-json\n");
        var output = new List<string>();
        await foreach (var text in Provider(factory).StreamChatAsync(Request, CancellationToken.None)) output.Add(text);
        Assert.Equal(["partial text"], output);
        Assert.Equal(1, factory.SendCount);
        Assert.True(factory.Disposed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(Chunk)]
    [InlineData("data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n")]
    public async Task Eof_without_completion_is_failure_without_repeating_request(string body)
    {
        var factory = new Factory(body);
        var output = new List<string>();
        var failure = await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var text in Provider(factory).StreamChatAsync(Request, CancellationToken.None)) output.Add(text);
        });
        Assert.Contains("incomplete", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(body == Chunk ? new[] { "partial text" } : [], output);
        Assert.Equal(1, factory.SendCount);
        Assert.True(factory.Disposed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(Chunk)]
    public async Task Provider_error_frame_is_failure_and_does_not_expose_error_payload(string prefix)
    {
        const string privateDetail = "secret-and-private-prompt";
        var factory = new Factory(prefix + "data: {\"error\":{\"message\":\"" + privateDetail + "\"}}\n\ndata: [DONE]\n");
        var output = new List<string>();
        var failure = await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var text in Provider(factory).StreamChatAsync(Request, CancellationToken.None)) output.Add(text);
        });
        Assert.DoesNotContain(privateDetail, failure.ToString(), StringComparison.Ordinal);
        Assert.Equal(prefix == Chunk ? new[] { "partial text" } : [], output);
        Assert.Equal(1, factory.SendCount);
        Assert.True(factory.Disposed);
    }

    [Fact]
    public async Task Cancellation_during_stream_is_propagated_and_disposes_response()
    {
        using var cancellation = new CancellationTokenSource();
        var factory = new Factory(Chunk + "data: [DONE]\n");
        await using var enumerator = Provider(factory).StreamChatAsync(Request, cancellation.Token).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("partial text", enumerator.Current);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
        Assert.Equal(1, factory.SendCount);
        Assert.True(factory.Disposed);
    }

    [Fact]
    public async Task Null_error_and_empty_usage_frame_do_not_break_completed_stream()
    {
        var factory = new Factory("data: {\"error\":null,\"choices\":[]}\n\n" + Chunk + "data: [DONE]\n");
        var output = new List<string>();
        await foreach (var text in Provider(factory).StreamChatAsync(Request, CancellationToken.None)) output.Add(text);
        Assert.Equal(["partial text"], output);
        Assert.Equal(1, factory.SendCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirmed_usage_is_preserved_for_completed_and_interrupted_responses(bool completed)
    {
        var capture = new ProviderUsageCaptureBuffer();
        var factory = new Factory(Chunk + "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":3}}\n\n" + (completed ? "data: [DONE]\n" : ""));
        var provider = new OpenAiModelProvider(factory, new Store(), new Secrets(), capture);
        async Task Read()
        {
            await foreach (var text in provider.StreamChatAsync(Request, CancellationToken.None)) Assert.Equal("partial text", text);
        }
        if (completed) await Read();
        else await Assert.ThrowsAsync<IOException>(Read);
        var usage = Assert.IsType<ProviderUsageSnapshot>(capture.Consume("openai", "test-model"));
        Assert.Equal(11, usage.InputTokens);
        Assert.Equal(3, usage.OutputTokens);
        Assert.Null(usage.CachedTokens);
        Assert.Null(usage.ReasoningTokens);
        Assert.Equal(UsageMeasurementKind.ProviderConfirmed, usage.Measurement);
        Assert.Null(capture.Consume("openai", "test-model"));
    }

    private static OpenAiModelProvider Provider(Factory factory) => new(factory, new Store(), new Secrets(), new ProviderUsageCaptureBuffer());

    private sealed class Factory(string body) : IHttpClientFactory
    {
        public int SendCount { get; private set; }
        public bool Disposed { get; private set; }
        public HttpClient CreateClient(string name) => new(new Handler(this, body));

        private sealed class Handler(Factory owner, string body) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.SendCount++;
                Assert.Equal("https://provider.test/v1/chat/completions", request.RequestUri!.AbsoluteUri);
                Assert.Equal(HttpMethod.Post, request.Method);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new TrackedContent(owner, body) });
            }
        }

        private sealed class TrackedContent(Factory owner, string body) : StringContent(body, System.Text.Encoding.UTF8, "text/event-stream")
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing) owner.Disposed = true;
                base.Dispose(disposing);
            }
        }
    }

    private sealed class Store : IProviderConfigurationStore
    {
        private static readonly ProviderConfiguration Configuration = new("openai", ModelProviderKind.OpenAI, "OpenAI", "https://provider.test/v1/", true, false, false, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>([Configuration]);
        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken cancellationToken) => Task.FromResult<ProviderConfiguration?>(Configuration);
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Secrets : IProviderSecretStore
    {
        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken) => Task.FromResult<string?>("local-fixture-key");
        public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
