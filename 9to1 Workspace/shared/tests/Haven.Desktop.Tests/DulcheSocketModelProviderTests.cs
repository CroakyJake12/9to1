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
        Assert.Equal(2, handler.Paths.Count);
    }

    private static DulcheSocketModelProvider CreateProvider(HttpMessageHandler handler) =>
        new(new DulcheLocalClient(new UserPreferencesService(new Paths(
            Path.Combine(Path.GetTempPath(), "dulche-provider-" + Guid.NewGuid().ToString("N")))), handler));

    private sealed class ProtocolHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public JsonElement LastCompletion { get; private set; }
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
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Stream, Encoding.UTF8, "text/event-stream"),
                };
                response.Headers.Add("X-Dulche-Run-Id", "run-local-1");
                return response;
            }
            if (request.Method == HttpMethod.Post && path == "/v1/agents/run-local-1/stop")
                return Json("""{"id":"run-local-1","state":"stopped"}""");
            throw new InvalidOperationException($"Unrecognized request {request.Method} {path}");
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
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
