using System.Net;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class OllamaClientStreamingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("{\"message\":{\"content\":\"partial\"},\"done\":false}\n")]
    [InlineData("{\"message\":{\"thinking\":\"partial reasoning\"}}\n\n")]
    public async Task PrematureEofDoesNotCompleteAnAssistantReply(string wire)
    {
        using var http = CreateHttp(wire);
        var usage = new ProviderUsageCaptureBuffer();
        var client = new OllamaClient(http, usage);
        var observed = new List<string>();
        var failure = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var chunk in client.StreamChatAsync(Request(), CancellationToken.None)) observed.Add(chunk);
        });
        Assert.Contains("completion", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(usage.ConsumeLastUsage());
        if (wire.Contains("partial reasoning")) Assert.Equal(["\0T:partial reasoning"], observed);
        else if (wire.Contains("partial")) Assert.Equal(["partial"], observed);
        else Assert.Empty(observed);
    }

    [Fact]
    public async Task OriginalDoneMarkerCompletesAndCapturesOnlyProviderUsage()
    {
        using var http = CreateHttp("{\"message\":{\"content\":\"hello\"},\"done\":false}\n" +
            "{\"message\":{\"content\":\" world\"},\"done\":true,\"prompt_eval_count\":7,\"eval_count\":2}\n");
        var usage = new ProviderUsageCaptureBuffer();
        var client = new OllamaClient(http, usage);
        var observed = new List<string>();
        await foreach (var chunk in client.StreamChatAsync(Request(), CancellationToken.None)) observed.Add(chunk);
        Assert.Equal(["hello", " world"], observed);
        var measured = Assert.IsType<ProviderUsageSnapshot>(usage.ConsumeLastUsage());
        Assert.Equal(7, measured.InputTokens); Assert.Equal(2, measured.OutputTokens);
    }

    [Fact]
    public async Task OriginalErrorFrameRemainsFailureAfterPartialContent()
    {
        using var http = CreateHttp("{\"message\":{\"content\":\"partial\"},\"done\":false}\n{\"error\":\"upstream disconnected\"}\n");
        var client = new OllamaClient(http, new());
        var observed = new List<string>();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var chunk in client.StreamChatAsync(Request(), CancellationToken.None)) observed.Add(chunk);
        });
        Assert.Equal("upstream disconnected", failure.Message); Assert.Equal(["partial"], observed);
    }

    private static OllamaChatRequest Request() => new("qwen3:1.7b", [new("user", "hello")], EffortLevel.Medium);
    private static HttpClient CreateHttp(string wire) => new(new WireHandler(wire)) { BaseAddress = new("http://original-provider.test/") };
    private sealed class WireHandler(string wire) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(wire) });
    }
}
