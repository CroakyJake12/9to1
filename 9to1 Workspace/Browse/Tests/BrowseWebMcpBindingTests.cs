using System.Text.Json;
using HavenOS.Apps.Browse;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class BrowseWebMcpBindingTests
{
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
        var toolJson = JsonSerializer.Serialize(new[] { new { name = "write", inputSchema = schemaAsString ? (object)"{}" : JsonSerializer.SerializeToElement(new { }) } });
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
