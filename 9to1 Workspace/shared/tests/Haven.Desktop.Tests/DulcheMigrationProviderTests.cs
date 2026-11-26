using System.Net;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class DulcheMigrationProviderTests
{
    [Fact]
    public async Task InstalledModelIsDiscoverableWithoutStartingItsEngine()
    {
        using var handler = new Wire();
        var models = await Provider(handler).GetModelsAsync(TestContext.Current.CancellationToken);
        var model = Assert.Single(models);
        Assert.Equal("alora", model.Name);
        Assert.Equal(524288, model.ContextWindow);
        Assert.True(model.Supports(ToolCapability.Tools));
        Assert.True(model.Supports(ToolCapability.Vision));
        Assert.Equal(new[] { "/v1/status" }, handler.Paths);
    }

    [Fact]
    public async Task VisionContentSurvivesTheNativeTransport()
    {
        using var handler = new Wire();
        var result = await Provider(handler).CompleteAsync(new("alora",
            [new("user", "Describe the attachment", ["aW1hZ2U="])], EffortLevel.Medium), TestContext.Current.CancellationToken);
        Assert.Equal("Ready", result);
        var parts = handler.Payload.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("Describe the attachment", parts[0].GetProperty("text").GetString());
        Assert.EndsWith("aW1hZ2U=", parts[1].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Equal(new[] { "/v1/status", "/v1/chat/completions" }, handler.Paths);
    }

    [Fact]
    public async Task ToolArgumentsAreAssembledBeforeReturningTheIssuedCall()
    {
        using var handler = new Wire { Stream = ToolStream("tool_calls") };
        var result = await Provider(handler).ChatWithToolsAsync(Request(), TestContext.Current.CancellationToken);
        var call = Assert.Single(result.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("read_file", call.Name);
        Assert.Equal("README.md", call.Arguments["path"].GetString());
        Assert.Equal("read_file", handler.Payload.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ATruncatedToolResponseNeverReturnsExecutableCalls()
    {
        using var handler = new Wire { Stream = ToolStream("length") };
        await Assert.ThrowsAsync<InvalidDataException>(() => Provider(handler)
            .ChatWithToolsAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal(new[] { "/v1/status", "/v1/chat/completions", "/v1/agents/run-migration/stop" }, handler.Paths);
    }

    [Fact]
    public async Task UnadvertisedVisionIsRefusedBeforeCompletionDispatch()
    {
        using var handler = new Wire { Capabilities = "[\"text\",\"streaming\"]" };
        await Assert.ThrowsAsync<NotSupportedException>(() => Provider(handler).CompleteAsync(new("alora",
            [new("user", "Describe it", ["aW1hZ2U="])], EffortLevel.Medium), TestContext.Current.CancellationToken));
        Assert.Equal(new[] { "/v1/status" }, handler.Paths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalSpecialistIdentityReachesTextAndToolRequestsWithoutLeakingIntoNextSend(bool tools)
    {
        var specialist = Guid.Parse("6f2b2c3d-4e5f-4678-9abc-def012345678");
        var defaults = new GenerationOptions();
        var selected = defaults with { SpecialistId = specialist };
        using var handler = new Wire { Stream = tools ? ToolStream("tool_calls") :
            "data: {\"choices\":[{\"delta\":{\"content\":\"Ready\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n" };
        var provider = Provider(handler);
        var token = TestContext.Current.CancellationToken;
        if (tools)
            await provider.ChatWithToolsAsync(Request() with { Options = selected }, token);
        else
            await provider.CompleteAsync(new("alora", [new("user", "Hello")], EffortLevel.Medium, Options: selected), token);
        Assert.Equal(specialist.ToString("D"), handler.Payload.GetProperty("specialist_id").GetString());
        Assert.Null(defaults.SpecialistId);
        if (tools)
            await provider.ChatWithToolsAsync(Request() with { Options = defaults }, token);
        else
            await provider.CompleteAsync(new("alora", [new("user", "Hello again")], EffortLevel.Medium, Options: defaults), token);
        Assert.Equal(JsonValueKind.Null, handler.Payload.GetProperty("specialist_id").ValueKind);
    }

    private static OllamaToolRequest Request() => new("alora", [new("user", "Read the project readme")],
        [new("read_file", "Read one project file", new Dictionary<string, object>
            { ["path"] = new { type = "string" } }, ["path"])], EffortLevel.Medium);

    private static string ToolStream(string finish) =>
        "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"read_\",\"arguments\":\"{\\\"path\\\":\"}}]}}]}\n\n" +
        "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"name\":\"file\",\"arguments\":\"\\\"README.md\\\"}\"}}]}}]}\n\n" +
        "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"" + finish + "\"}]}\n\ndata: [DONE]\n\n";

    private static DulcheSocketModelProvider Provider(HttpMessageHandler handler) => new(new DulcheLocalClient(
        new UserPreferencesService(new Paths(Path.Combine(Path.GetTempPath(), "dulche-migration-" + Guid.NewGuid().ToString("N")))), handler));

    private sealed class Wire : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public JsonElement Payload { get; private set; }
        public string Capabilities { get; init; } = "[\"text\",\"streaming\",\"tools\",\"vision\"]";
        public string Stream { get; init; } = "data: {\"choices\":[{\"delta\":{\"content\":\"Ready\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path == "/v1/status") return Json("{\"phase\":\"ready\",\"stop_all\":false,\"models\":[" +
                "{\"id\":\"alora\",\"name\":\"ALORA Multimodal\",\"state\":\"unloaded\",\"can_start\":true,\"context\":524288,\"capabilities\":" + Capabilities + "}," +
                "{\"id\":\"manual-stop\",\"state\":\"unloaded\",\"can_start\":false}," +
                "{\"id\":\"legacy\",\"state\":\"unloaded\"}]}");
            if (path == "/v1/chat/completions")
            {
                using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Payload = document.RootElement.Clone();
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(Stream, Encoding.UTF8, "text/event-stream") };
                response.Headers.Add("X-Dulche-Run-Id", "run-migration");
                return response;
            }
            if (path == "/v1/agents/run-migration/stop") return Json("{\"id\":\"run-migration\",\"state\":\"completed\",\"settled\":true}");
            throw new InvalidOperationException("Unexpected path " + path);
        }
        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };
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
