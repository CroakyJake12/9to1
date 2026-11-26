using System.Net;
using System.Text;
using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class DulcheSocketModelProviderTests
{
    [Fact]
    public async Task NativeCatalogueResolvesTheAuthorizedLocalIdAndStreamsItUnchanged()
    {
        using var handler = new ProtocolHandler();
        var provider = CreateProvider(handler);
        IModelProviderRegistry registry = new ModelProviderRegistry([provider]);
        var token = TestContext.Current.CancellationToken;
        var inventory = await registry.GetModelsAsync(new ModelCataloguePolicy(AllowRemote: false), token);
        Assert.Equal(new[] { "worker:cpu", "reviewer" }, inventory.Select(model => model.Name));
        var worker = inventory[0];
        Assert.True(worker.IsLocal);
        Assert.Equal(DulcheSocketModelProvider.ProviderId, worker.ProviderId);
        Assert.Equal("CPU worker", worker.Label);
        Assert.Equal(32768, worker.ContextWindow);
        Assert.True(worker.Supports(ToolCapability.Streaming));
        Assert.False(worker.Supports(ToolCapability.Tools));
        Assert.Null(inventory[1].ContextWindow);
        Assert.Equal(new[] { "/v1/status" }, handler.Paths);

        var selection = await new ModelRouteResolver(registry).ResolveAsync(new ModelRoute("local", 1,
            [new ModelIdentity(worker.ProviderId, worker.Name)], new ProviderPolicy(AllowRemote: false)),
            explicitModel: null, cancellationToken: token);
        Assert.True(selection.Succeeded);
        var nativeId = selection.Value!.Model.ModelId;
        var content = await provider.CompleteAsync(new(nativeId, [new("user", "Say hello")],
            EffortLevel.Medium, SystemPrompt: "Keep it short"), token);

        Assert.Equal("Hello, friend.", content);
        Assert.Equal(new[] { "/v1/status", "/v1/status", "/v1/chat/completions" }, handler.Paths);
        var payload = handler.LastCompletion;
        Assert.Equal("worker:cpu", payload.GetProperty("model").GetString());
        Assert.True(payload.GetProperty("stream").GetBoolean());
        Assert.Equal("system", payload.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("Keep it short", payload.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("Say hello", payload.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ClosingTheResponseStopsOnlyItsReportedRun()
    {
        using var handler = new ProtocolHandler();
        var provider = CreateProvider(handler);
        var token = TestContext.Current.CancellationToken;
        var response = provider.StreamChatAsync(new("reviewer", [new("user", "Hello")], EffortLevel.Medium), token);
        await using (var stream = response.GetAsyncEnumerator(token))
        {
            Assert.True(await stream.MoveNextAsync());
            Assert.Equal("Hello, ", stream.Current);
        }
        Assert.Equal(new[] { "/v1/chat/completions", "/v1/agents/run-local-1/stop" }, handler.Paths);
    }

    [Fact]
    public async Task ATruncatedOrUnsupportedResponseDoesNotReportSuccessOrDispatchTools()
    {
        using var handler = new ProtocolHandler { Stream = "data: [DONE]\n\n" };
        var provider = CreateProvider(handler);
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.CompleteAsync(new("reviewer", [new("user", "Hello")], EffortLevel.Medium), token));
        Assert.Equal(new[] { "/v1/chat/completions", "/v1/agents/run-local-1/stop" }, handler.Paths);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            provider.ChatWithToolsAsync(new("reviewer", [], [], EffortLevel.Medium), token));
        Assert.Equal(3, handler.Paths.Count); // Rich requests recheck the current advertised capability.
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"other\",\"state\":\"stopped\",\"settled\":true}")]
    [InlineData("{\"id\":\"run-local-1\",\"state\":\"running\",\"settled\":true}")]
    [InlineData("{\"id\":\"run-local-1\",\"state\":true,\"settled\":true}")]
    [InlineData("{\"id\":\"run-local-1\",\"state\":\"stopped\"}")]
    [InlineData("{\"id\":\"run-local-1\",\"state\":\"stopped\",\"settled\":false}")]
    [InlineData("{\"id\":\"run-local-1\",\"state\":\"stopped\",\"settled\":null}")]
    [InlineData("{\"id\":\"run-local-1\",\"state\":\"stopped\",\"settled\":\"true\"}")]
    [InlineData("{\"id\":\"run-local-1\",\"state\":\"stopped\",\"settled\":1}")]
    public async Task HttpSuccessWithoutExactSettledTerminalRunCannotAcknowledgeStop(string reply)
    {
        using var handler = new ProtocolHandler { StopReply = reply };
        var provider = CreateProvider(handler);
        var token = TestContext.Current.CancellationToken;
        var stream = provider.StreamChatAsync(new("reviewer", [new("user", "Hello")], EffortLevel.Medium), token)
            .GetAsyncEnumerator(token);
        Assert.True(await stream.MoveNextAsync());
        var failure = await Assert.ThrowsAsync<DulcheRunStopUnconfirmedException>(() => stream.DisposeAsync().AsTask());
        Assert.Equal("run-local-1", failure.RunId);
        Assert.True(handler.StopSawDisposedResponse);
        Assert.Equal(1, handler.CompletionStreamDisposals);
        Assert.Equal(new[] { "/v1/chat/completions", "/v1/agents/run-local-1/stop" }, handler.Paths);
    }

    [Theory]
    [InlineData("transport")]
    [InlineData("json")]
    [InlineData("http")]
    [InlineData("timeout")]
    public async Task FailedStopRemainsObservableAfterResponseCleanup(string kind)
    {
        var transport = new HttpRequestException("Controlled stop transport failure");
        using var handler = new ProtocolHandler
        {
            StopFailure = kind == "transport" ? transport : null,
            StopReply = kind == "json" ? "{invalid json" : "{\"id\":\"run-local-1\",\"state\":\"stopped\",\"settled\":true}",
            StopStatus = kind == "http" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
            WaitForStopTimeout = kind == "timeout"
        };
        var token = TestContext.Current.CancellationToken;
        var stream = CreateProvider(handler).StreamChatAsync(new("reviewer", [new("user", "Hello")], EffortLevel.Medium), token)
            .GetAsyncEnumerator(token);
        Assert.True(await stream.MoveNextAsync());
        var failure = await Assert.ThrowsAsync<DulcheRunStopUnconfirmedException>(() => stream.DisposeAsync().AsTask());
        Assert.Equal("run-local-1", failure.RunId);
        Assert.True(handler.StopSawDisposedResponse);
        Assert.Equal(1, handler.CompletionStreamDisposals);
        Assert.NotNull(failure.InnerException);
        if (kind == "transport") Assert.Same(transport, failure.InnerException);
        if (kind == "json") Assert.IsAssignableFrom<JsonException>(failure.InnerException);
        if (kind == "timeout") Assert.IsAssignableFrom<OperationCanceledException>(failure.InnerException);
        Assert.Equal(new[] { "/v1/chat/completions", "/v1/agents/run-local-1/stop" }, handler.Paths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrAmbiguousRunIdentityNeverInventsAStopTarget(bool ambiguous)
    {
        using var handler = new ProtocolHandler { RunIds = ambiguous ? ["run-local-1", "other"] : [] };
        var token = TestContext.Current.CancellationToken;
        var stream = CreateProvider(handler).StreamChatAsync(new("reviewer", [new("user", "Hello")], EffortLevel.Medium), token)
            .GetAsyncEnumerator(token);
        Assert.True(await stream.MoveNextAsync());
        var failure = await Assert.ThrowsAsync<DulcheRunStopUnconfirmedException>(() => stream.DisposeAsync().AsTask());
        Assert.Null(failure.RunId);
        Assert.Equal(new[] { "/v1/chat/completions" }, handler.Paths);
        Assert.Equal(1, handler.CompletionStreamDisposals);
    }

    [Fact]
    public async Task CancellationBeforeDispatchRemainsOrdinaryAndStartsNoRequest()
    {
        using var handler = new ProtocolHandler();
        using var generation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        generation.Cancel();
        await using var stream = CreateProvider(handler).StreamChatAsync(
            new("reviewer", [new("user", "Hello")], EffortLevel.Medium), generation.Token)
            .GetAsyncEnumerator(generation.Token);
        var move = stream.MoveNextAsync().AsTask();
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => move);
        Assert.Equal(generation.Token, failure.CancellationToken);
        Assert.True(move.IsCanceled);
        Assert.Empty(handler.Paths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAfterDispatchBeforeHeadersRetainsUnknownStopAndExactCause(bool canceled)
    {
        var token = TestContext.Current.CancellationToken;
        using var generation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var headers = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ProtocolHandler { CompletionBeforeHeaders = headers.Task };
        await using var stream = CreateProvider(handler).StreamChatAsync(
            new("reviewer", [new("user", "Hello")], EffortLevel.Medium), generation.Token)
            .GetAsyncEnumerator(generation.Token);
        var move = stream.MoveNextAsync().AsTask();
        try
        {
            await handler.CompletionDispatched.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.False(move.IsCompleted);
            if (canceled) generation.Cancel();
            Exception original = canceled
                ? new OperationCanceledException("Controlled cancellation before response headers", generation.Token)
                : new HttpRequestException("Controlled transport failure before response headers");
            headers.SetException(original);
            var failure = await Assert.ThrowsAsync<DulcheRunStopUnconfirmedException>(() => move);
            Assert.Null(failure.RunId);
            Assert.Same(original, failure.InnerException);
            Assert.True(move.IsFaulted); // Must not qualify as an acknowledged ordinary user stop.
            Assert.Equal(new[] { "/v1/chat/completions" }, handler.Paths);
            Assert.Equal(0, handler.CompletionStreamDisposals);
        }
        finally
        {
            headers.TrySetCanceled(token);
            _ = await Record.ExceptionAsync(() => move);
        }
    }

    [Fact]
    public async Task CanceledGenerationStillUsesIndependentBoundedStopRequest()
    {
        using var handler = new ProtocolHandler();
        using var generation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var stream = CreateProvider(handler).StreamChatAsync(new("reviewer", [new("user", "Hello")], EffortLevel.Medium), generation.Token)
            .GetAsyncEnumerator(generation.Token);
        Assert.True(await stream.MoveNextAsync());
        generation.Cancel();
        await stream.DisposeAsync();
        Assert.False(handler.StopTokenWasCanceled);
        Assert.True(handler.StopSawDisposedResponse);
        Assert.Equal(1, handler.CompletionStreamDisposals);
        Assert.Equal(new[] { "/v1/chat/completions", "/v1/agents/run-local-1/stop" }, handler.Paths);
    }

    [Fact]
    public async Task OriginalStreamFailureAndUnknownStopAreBothRetained()
    {
        var transport = new HttpRequestException("Controlled stop failure after malformed terminal stream");
        using var handler = new ProtocolHandler { Stream = "data: [DONE]\n\n", StopFailure = transport };
        var token = TestContext.Current.CancellationToken;
        var failure = await Assert.ThrowsAsync<AggregateException>(() => CreateProvider(handler)
            .CompleteAsync(new("reviewer", [new("user", "Hello")], EffortLevel.Medium), token));
        Assert.Contains(failure.InnerExceptions, item => item is InvalidDataException);
        var stop = Assert.Single(failure.InnerExceptions, item => item is DulcheRunStopUnconfirmedException);
        Assert.Same(transport, stop.InnerException);
        Assert.Equal(1, handler.CompletionStreamDisposals);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("length")]
    public async Task TerminalChoiceWithoutDoneStillRequiresExactRunStop(string reason)
    {
        using var handler = new ProtocolHandler
        {
            Stream = "data: {\"choices\":[{\"delta\":{\"content\":\"final\"},\"finish_reason\":\"" + reason + "\"}]}\n\n"
        };
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<EndOfStreamException>(() => CreateProvider(handler)
            .CompleteAsync(new("reviewer", [new("user", "Hello")], EffortLevel.Medium), token));
        Assert.Equal(new[] { "/v1/chat/completions", "/v1/agents/run-local-1/stop" }, handler.Paths);
        Assert.True(handler.StopSawDisposedResponse);
        Assert.Equal(1, handler.CompletionStreamDisposals);
    }

    [Fact]
    public async Task DetailedResponseWithoutServerRunIdentityDoesNotFabricateCustody()
    {
        using var handler = new ProtocolHandler { RunIds = [] };
        var client = CreateClient(handler);
        string? reported = null;
        var result = await client.StreamDetailedAsync(new("reviewer", [new("user", "Hello")]),
            null, id => reported = id, TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, result.RunId);
        Assert.Null(reported);
        Assert.Equal("Hello, friend.", result.Content);
        Assert.Equal(new[] { "/v1/chat/completions" }, handler.Paths);
    }

    private static DulcheSocketModelProvider CreateProvider(HttpMessageHandler handler) => new(CreateClient(handler));

    private static DulcheLocalClient CreateClient(HttpMessageHandler handler) =>
        new(new UserPreferencesService(new Paths(
            Path.Combine(Path.GetTempPath(), "dulche-provider-" + Guid.NewGuid().ToString("N")))), handler);

    private sealed class ProtocolHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public JsonElement LastCompletion { get; private set; }
        public string[] RunIds { get; init; } = ["run-local-1"];
        public string StopReply { get; init; } = "{\"id\":\"run-local-1\",\"state\":\"stopped\",\"settled\":true}";
        public HttpStatusCode StopStatus { get; init; } = HttpStatusCode.OK;
        public Exception? StopFailure { get; init; }
        public Task<HttpResponseMessage>? CompletionBeforeHeaders { get; init; }
        public TaskCompletionSource CompletionDispatched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WaitForStopTimeout { get; init; }
        public bool StopTokenWasCanceled { get; private set; }
        public bool StopSawDisposedResponse { get; private set; }
        public int CompletionStreamDisposals { get; private set; }
        public string Stream { get; init; } = """
            : heartbeat

            data: {"choices":[{"delta":{"content":"Hello, "}}]}

            data: {"choices":[{"delta":{"content":"friend."}}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]
            """;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (request.Method == HttpMethod.Get && path == "/v1/status")
                return Json("""
                    {"phase":"ready","stop_all":false,"models":[
                      {"id":"worker:cpu","name":"CPU worker","state":"loaded","context":32768,"slots":2},
                      {"id":"reviewer","state":"loaded"},
                      {"id":"retired","state":"unloaded","context":32768},
                      {"id":"loading","state":"loading"}]}
                    """);
            if (request.Method == HttpMethod.Post && path == "/v1/chat/completions")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                LastCompletion = body.RootElement.Clone();
                CompletionDispatched.TrySetResult();
                if (CompletionBeforeHeaders is { } held) return await held;
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new ObservedStream(Encoding.UTF8.GetBytes(Stream), () => CompletionStreamDisposals++)),
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                if (RunIds.Length != 0) response.Headers.TryAddWithoutValidation("X-Dulche-Run-Id", RunIds);
                return response;
            }
            if (request.Method == HttpMethod.Post && path == "/v1/agents/run-local-1/stop")
            {
                StopTokenWasCanceled = cancellationToken.IsCancellationRequested;
                StopSawDisposedResponse = CompletionStreamDisposals != 0;
                if (StopFailure is not null) throw StopFailure;
                if (WaitForStopTimeout) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new(StopStatus) { Content = new StringContent(StopReply, Encoding.UTF8, "application/json") };
            }
            throw new InvalidOperationException($"Unrecognized request {request.Method} {path}");
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private sealed class ObservedStream(byte[] bytes, Action disposed) : MemoryStream(bytes)
    {
        private int _released;
        protected override void Dispose(bool disposing)
        {
            // HttpContent may dispose its cached read wrapper as well as the source.
            // Observe the actual resource release once, independent of wrapper count.
            if (disposing && Interlocked.Exchange(ref _released, 1) == 0) disposed();
            base.Dispose(disposing);
        }
    }

    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "store.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
