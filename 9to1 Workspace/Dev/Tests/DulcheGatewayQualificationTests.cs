using System.Net;
using System.Text;
using HavenOS.Apps.Dev.Autonomous;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DulcheGatewayQualificationTests
{
    [Fact]
    public async Task Missing_server_run_id_never_fabricates_cancellation_custody()
    {
        using var gateway = new DulcheUnixSocketGateway(new ReplyHandler("data: {\"choices\":[{\"delta\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n"));
        string? started = null;
        var result = await gateway.StreamCompletionAsync(new("model", [new("user", "hello")]), onRunStarted: id => started = id);
        Assert.Null(started);
        Assert.Equal("", result.RunId);
        Assert.Equal("ok", result.Content);
    }

    [Fact]
    public async Task Done_without_terminal_choice_does_not_accept_partial_action()
    {
        using var gateway = new DulcheUnixSocketGateway(new ReplyHandler("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\ndata: [DONE]\n\n"));
        await Assert.ThrowsAsync<EndOfStreamException>(() => gateway.StreamCompletionAsync(new("model", [new("user", "hello")])));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"other\",\"state\":\"stopped\",\"settled\":true}")]
    [InlineData("{\"id\":\"owned\",\"state\":\"running\",\"settled\":true}")]
    [InlineData("{\"id\":\"owned\",\"state\":\"stopped\"}")]
    [InlineData("{\"id\":\"owned\",\"state\":\"stopped\",\"settled\":false}")]
    [InlineData("{\"id\":\"owned\",\"state\":\"stopped\",\"settled\":\"true\"}")]
    [InlineData("{\"id\":\"owned\",\"state\":\"stopped\",\"settled\":null}")]
    [InlineData("{\"id\":\"owned\",\"state\":\"stopped\",\"settled\":1}")]
    public async Task Http_success_is_not_stop_confirmation(string body)
    {
        using var handler = new ReplyHandler(body);
        using var gateway = new DulcheUnixSocketGateway(handler);
        await Assert.ThrowsAsync<IOException>(() => gateway.StopRunAsync("owned"));
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("/v1/agents/owned/stop", handler.LastPath);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
    }

    [Theory]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("review")]
    public async Task Exact_settled_terminal_run_acknowledges_stop(string terminal)
    {
        using var handler = new ReplyHandler($"{{\"id\":\"owned\",\"state\":\"{terminal}\",\"settled\":true}}");
        using var gateway = new DulcheUnixSocketGateway(handler);
        await gateway.StopRunAsync("owned");
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("/v1/agents/owned/stop", handler.LastPath);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
    }

    [Theory]
    [InlineData(0, 100_000_000_000)]
    [InlineData(24000, 0)]
    public void Unknown_resource_metrics_do_not_bypass_admission(long memory, long disk)
    {
        var status = new DeveloperDulcheStatus("ready", false, [], new(memory, 96000, disk, null, []), 0);
        Assert.Throws<InvalidOperationException>(() => DulcheUnixSocketGateway.DemandHardwareSafeguards(status, 12000, 10));
    }

    private sealed class ReplyHandler(string body) : HttpMessageHandler
    {
        internal int RequestCount { get; private set; }
        internal string? LastPath { get; private set; }
        internal HttpMethod? LastMethod { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastPath = request.RequestUri!.AbsolutePath;
            LastMethod = request.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
