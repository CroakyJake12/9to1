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
    [InlineData("{\"id\":\"other\",\"state\":\"stopped\"}")]
    [InlineData("{\"id\":\"owned\",\"state\":\"running\"}")]
    public async Task Http_success_is_not_stop_confirmation(string body)
    {
        using var gateway = new DulcheUnixSocketGateway(new ReplyHandler(body));
        await Assert.ThrowsAsync<IOException>(() => gateway.StopRunAsync("owned"));
    }

    [Fact]
    public async Task Exact_terminal_run_acknowledges_stop()
    {
        using var gateway = new DulcheUnixSocketGateway(new ReplyHandler("{\"id\":\"owned\",\"state\":\"stopped\"}"));
        await gateway.StopRunAsync("owned");
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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
