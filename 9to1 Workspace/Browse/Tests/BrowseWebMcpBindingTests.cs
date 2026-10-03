using System.Text.Json;
using HavenOS.Apps.Browse;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class BrowseWebMcpBindingTests
{
    [Fact]
    public async Task Approved_arguments_remain_the_exact_dispatch_snapshot_after_caller_document_disposal()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var token = timeout.Token;
        var broker = new HoldingBroker(); var dispatches = 0; JsonElement? dispatched = null;
        var binding = new BrowseWebMcpBinding((script, _) =>
        {
            if (script.Contains("BrowserVersion: navigator.userAgent")) return Task.FromResult(Report(true));
            if (script.Contains("return JSON.stringify(navigator.modelContextTesting.listTools())")) return Task.FromResult(TypedTools());
            if (script.Contains("executeTool("))
            {
                dispatches++; const string marker = "const request = "; var start = script.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
                using var request = JsonDocument.Parse(script[start..script.IndexOf(';', start)]);
                dispatched = request.RootElement.GetProperty("Arguments").Clone(); return Task.FromResult("{\"started\":true}");
            }
            return Task.FromResult("{\"status\":\"completed\",\"result\":{\"saved\":true}}");
        }, broker);
        await binding.DiscoverAsync(token);
        var caller = JsonDocument.Parse("{\"value\":\"reviewed\"}");
        var pending = binding.InvokeAsync("write", caller.RootElement, token); await broker.Entered.Task.WaitAsync(token);
        caller.Dispose(); Assert.Equal("reviewed", broker.Request!.Arguments.GetProperty("value").GetString());
        broker.Release.TrySetResult(); Assert.True((await pending).GetProperty("saved").GetBoolean());
        Assert.Equal(1, dispatches); Assert.Equal(broker.Request!.Arguments.GetRawText(), dispatched!.Value.GetRawText());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"value\":7}")]
    [InlineData("{\"value\":\"valid\",\"extra\":true}")]
    [InlineData("\"not an object\"")]
    public async Task Invalid_discovered_schema_arguments_never_request_approval_or_dispatch(string json)
    {
        var broker = new Broker(true); var dispatches = 0;
        var binding = new BrowseWebMcpBinding((script, _) =>
        {
            if (script.Contains("BrowserVersion: navigator.userAgent")) return Task.FromResult(Report(true));
            if (script.Contains("return JSON.stringify(navigator.modelContextTesting.listTools())")) return Task.FromResult(TypedTools());
            dispatches++; return Task.FromResult("{\"started\":true}");
        }, broker);
        await binding.DiscoverAsync(); using var supplied = JsonDocument.Parse(json);
        await Assert.ThrowsAsync<InvalidDataException>(() => binding.InvokeAsync("write", supplied.RootElement));
        Assert.Null(broker.Request); Assert.Equal(0, dispatches);
    }

    private static string TypedTools() => "[{\"name\":\"write\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\"}},\"required\":[\"value\"],\"additionalProperties\":false}}]";
    private sealed class HoldingBroker : IWebMcpApprovalBroker
    {
        public WebMcpApprovalRequest? Request { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<bool> ApproveAsync(WebMcpApprovalRequest request, CancellationToken token)
        { Request = request; Entered.TrySetResult(); await Release.Task.WaitAsync(token); return true; }
    }

    [Fact]
    public async Task UnsupportedBrowserIsReportedWithoutDiscoveryOrInvocation()
    {
        var calls = 0;
        var binding = new BrowseWebMcpBinding((_, _) => { calls++; return Task.FromResult(Report(false)); }, new Broker(true));
        var result = await binding.DiscoverAsync();
        Assert.False(result.Document.Supported);
        Assert.Empty(result.Tools);
        Assert.Equal(1, calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => binding.InvokeAsync("write", Arguments()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task DeniedApprovalOrChangedDocumentCannotExecute(bool approved, bool changed)
    {
        var invoked = false;
        var document = "one";
        var broker = new Broker(approved, () => { if (changed) document = "two"; });
        var binding = new BrowseWebMcpBinding((script, _) =>
        {
            if (script.Contains("BrowserVersion: navigator.userAgent")) return Task.FromResult(Report(true, document));
            if (script.Contains("return JSON.stringify(navigator.modelContextTesting.listTools())")) return Task.FromResult("[{\"name\":\"write\",\"description\":\"untrusted\",\"inputSchema\":{\"type\":\"object\"}}]");
            invoked = true;
            return Task.FromResult("{\"started\":true}");
        }, broker);
        await binding.DiscoverAsync();
        if (approved) await Assert.ThrowsAsync<InvalidOperationException>(() => binding.InvokeAsync("write", Arguments()));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => binding.InvokeAsync("write", Arguments()));
        Assert.False(invoked);
        Assert.Equal("https://example.test", broker.Request!.Document.Origin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovedInvocationReturnsObservedResultAndRevocationBlocksFurtherCalls(bool schemaAsString)
    {
        var toolJson = JsonSerializer.Serialize(new[] { new { name = "write", inputSchema = schemaAsString ? (object)"{\"type\":\"object\"}" : JsonSerializer.SerializeToElement(new { type = "object" }) } });
        var binding = new BrowseWebMcpBinding((script, _) => Task.FromResult(
            script.Contains("BrowserVersion: navigator.userAgent") ? Report(true) : script.Contains("return JSON.stringify(navigator.modelContextTesting.listTools())")
                ? toolJson : script.Contains("executeTool(")
                    ? "{\"started\":true}" : "{\"status\":\"completed\",\"result\":{\"saved\":true}}"), new Broker(true));
        var discovery = await binding.DiscoverAsync();
        Assert.False(discovery.Document.CancellationSupported);
        Assert.True((await binding.InvokeAsync("write", Arguments())).GetProperty("saved").GetBoolean());
        await binding.DisconnectAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => binding.InvokeAsync("write", Arguments()));
    }

    private static JsonElement Arguments() => JsonSerializer.SerializeToElement(new { value = "example" });
    private static string Report(bool supported, string id = "one") => JsonSerializer.Serialize(
        new WebMcpDocument("https://example.test", id, "test-browser", "navigator.modelContextTesting", supported, false));
    private sealed class Broker(bool approve, Action? before = null) : IWebMcpApprovalBroker
    {
        public WebMcpApprovalRequest? Request { get; private set; }
        public Task<bool> ApproveAsync(WebMcpApprovalRequest request, CancellationToken cancellationToken)
        { Request = request; before?.Invoke(); return Task.FromResult(approve); }
    }
}
