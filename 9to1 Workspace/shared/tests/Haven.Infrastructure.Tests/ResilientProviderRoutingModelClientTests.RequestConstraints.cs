using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed partial class ResilientProviderRoutingModelClientTests
{
    [Theory]
    [InlineData("complete")]
    [InlineData("stream")]
    [InlineData("tools")]
    public async Task Request_cloud_refusal_precedes_remote_catalogue_and_selected_dispatch(string transport)
    {
        var cloud = FakeProvider.Completing("first", "model-a", "must not dispatch");
        var client = CreateClient([cloud], new Dictionary<string, ProviderConfiguration>
            { ["first"] = Configuration("first", true, "") });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => InvokeAsync(client, transport, "first:model-a", new(false, true)));
        Assert.Equal(0, cloud.ModelDiscoveryCalls);
        Assert.Equal(0, cloud.CompletionCalls + cloud.StreamCalls + cloud.ToolCalls);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("stream")]
    [InlineData("tools")]
    public async Task Selected_only_request_does_not_discover_or_dispatch_declared_fallback(string transport)
    {
        var first = FakeProvider.Failing("first", "model-a");
        var second = FakeProvider.Completing("second", "model-b", "must not dispatch");
        var client = CreateClient([first, second], new Dictionary<string, ProviderConfiguration>
            { ["first"] = Configuration("first", true, "second:model-b"), ["second"] = Configuration("second", true, "") });
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(client, transport, "first:model-a", new(null, false)));
        Assert.Equal(1, first.ModelDiscoveryCalls);
        Assert.Equal(1, first.CompletionCalls + first.StreamCalls + first.ToolCalls);
        Assert.Equal(0, second.ModelDiscoveryCalls);
        Assert.Equal(0, second.CompletionCalls + second.StreamCalls + second.ToolCalls);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("stream")]
    [InlineData("tools")]
    public async Task Local_request_never_discovers_or_falls_back_to_cloud_even_when_provider_allows_it(string transport)
    {
        var local = FakeProvider.Failing("ollama", "local-model", isLocal: true);
        var cloud = FakeProvider.Completing("second", "model-b", "must not dispatch");
        var client = CreateClient([local, cloud], new Dictionary<string, ProviderConfiguration>
            { ["ollama"] = Configuration("ollama", true, "second:model-b"), ["second"] = Configuration("second", true, "") });
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(client, transport, "local-model", new(false, true)));
        Assert.Equal(1, local.CompletionCalls + local.StreamCalls + local.ToolCalls);
        Assert.Equal(0, cloud.ModelDiscoveryCalls);
        Assert.Equal(0, cloud.CompletionCalls + cloud.StreamCalls + cloud.ToolCalls);
    }

    [Fact]
    public async Task Request_true_does_not_override_existing_local_only_privacy()
    {
        var cloud = FakeProvider.Completing("first", "model-a", "must not dispatch");
        var registry = new ModelProviderRegistry([cloud]);
        var privacy = new TestPrivacy(localOnly: true);
        var client = new ResilientProviderRoutingModelClient(new ProviderRoutingModelClient(new FakeLocalClient(), registry, privacy),
            registry, new FakeConfigurationStore(new Dictionary<string, ProviderConfiguration>
                { ["first"] = Configuration("first", true, "") }), privacy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(client, "complete", "first:model-a", new(true, true)));
        Assert.Equal(0, cloud.ModelDiscoveryCalls);
        Assert.Equal(0, cloud.CompletionCalls);
    }

    [Fact]
    public async Task Prior_tool_transcript_cannot_bypass_original_cloud_refusal()
    {
        var cloud = FakeProvider.Completing("first", "model-a", "must not dispatch");
        var client = CreateClient([cloud], new Dictionary<string, ProviderConfiguration>
            { ["first"] = Configuration("first", true, "") });
        var request = new OllamaToolRequest("first:model-a", [new OllamaToolTurn("tool", "previous", ToolName: "read_file")],
            [], EffortLevel.Medium, Options: new GenerationOptions { RequestedRoutingConstraints = new(false, false) });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.ChatWithToolsAsync(request, default));
        Assert.Equal(0, cloud.ModelDiscoveryCalls);
        Assert.Equal(0, cloud.ToolCalls);
    }

    [Fact]
    public void Absent_request_constraints_preserve_original_options_json_and_cold_defaults()
    {
        const string previous = "{\"Temperature\":0.7,\"ContextLimit\":32768,\"ActionLimit\":24}";
        Assert.Equal(previous, System.Text.Json.JsonSerializer.Serialize(new GenerationOptions()));
        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<GenerationOptions>(previous)!.RequestedRoutingConstraints);
    }

    private static async Task InvokeAsync(ResilientProviderRoutingModelClient client, string transport, string model,
        ModelRequestRoutingConstraints constraints)
    {
        var options = new GenerationOptions { RequestedRoutingConstraints = constraints };
        if (transport == "tools")
            await client.ChatWithToolsAsync(new(model, [new("user", "hello")], [], EffortLevel.Medium, Options: options), default);
        else if (transport == "stream")
            await foreach (var _ in client.StreamChatAsync(new(model, [new("user", "hello")], EffortLevel.Medium, Options: options), default)) { }
        else await client.CompleteAsync(new(model, [new("user", "hello")], EffortLevel.Medium, Options: options), default);
    }
}
