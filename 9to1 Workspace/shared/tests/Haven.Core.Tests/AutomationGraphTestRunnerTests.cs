using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class AutomationGraphTestRunnerTests
{
    [Fact]
    public async Task Device_node_is_simulated_with_trace_without_a_physical_executor()
    {
        var target = new DeviceTargetDescriptor("current", "This PC", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice, "test.device");
        var node = AutomationGraphNodeDefinition.FromDevice(new DeviceAutomationNodeDefinition(Guid.NewGuid(), target, "ui.snapshot", new Dictionary<string, string>()));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [node], []);
        var result = await AutomationGraphTestRunner.RunAsync(graph, CancellationToken.None);
        Assert.True(result.Succeeded); Assert.Equal(AutomationGraphRunMode.Test, result.Mode);
        var trace = Assert.Single(result.Trace); Assert.Equal(AutomationGraphTraceStatus.Succeeded, trace.Status); Assert.Contains("would execute", trace.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_graph_forwards_an_action_output_to_the_next_emit_node()
    {
        var source = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Action", null, null,
            new Dictionary<string, string> { ["action"] = "emit", ["value"] = "ready" });
        var forward = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Action", null, null,
            new Dictionary<string, string> { ["action"] = "emit", ["value"] = "{{input}}" });
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [source, forward], [new(source.Id, forward.Id)]);

        var result = await AutomationGraphTestRunner.RunAsync(graph, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(AutomationGraphRunMode.Test, result.Mode);
        var forwardTrace = Assert.Single(result.Trace, item => item.NodeId == forward.Id);
        Assert.Equal("ready", forwardTrace.Output);
        Assert.Equal("ready", forwardTrace.Inputs![source.Id]);
        Assert.Contains("forward", forwardTrace.Message, StringComparison.OrdinalIgnoreCase);
    }
}
