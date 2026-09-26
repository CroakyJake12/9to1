using System.Text.Json;
using Haven.Application.Automations;

namespace Haven.Core.Tests;

public sealed class AutomationGraphTypedValueTests
{
    [Theory]
    [InlineData(AutomationGraphValueState.Null)]
    [InlineData(AutomationGraphValueState.Unavailable)]
    [InlineData(AutomationGraphValueState.Redacted)]
    public async Task Typed_port_preserves_non_value_states(AutomationGraphValueState state)
    {
        var source = Node("Source", new AutomationGraphPortDefinition("value", "Value", AutomationGraphPortDirection.Output, "any"));
        var target = Node("Target", new AutomationGraphPortDefinition("value", "Value", AutomationGraphPortDirection.Input, "any"));
        var sourceExecutor = new ReturningExecutor("Source", _ => new AutomationGraphNodeExecutionResult(
            true,
            "Produced.",
            TypedOutputs: new Dictionary<string, AutomationGraphValue>
            {
                ["value"] = state switch
                {
                    AutomationGraphValueState.Null => AutomationGraphValue.Null("string"),
                    AutomationGraphValueState.Unavailable => AutomationGraphValue.Unavailable("string", "Provider has no value."),
                    AutomationGraphValueState.Redacted => AutomationGraphValue.Redacted("string", "Hidden by permission."),
                    _ => throw new ArgumentOutOfRangeException(nameof(state))
                }
            }));
        var targetExecutor = new ReturningExecutor("Target", context =>
        {
            Assert.True(context.TypedInputs!.TryGetValue("value", out var values));
            Assert.Equal(state, Assert.Single(values).State);
            return new(true, "Observed state.");
        });
        var graph = new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion,
            [source, target],
            [new AutomationGraphEdgeDefinition(source.Id, target.Id) { FromPortId = "value", ToPortId = "value" }]);

        var result = await new AutomationGraphRunner([sourceExecutor, targetExecutor])
            .RunAsync(graph, AutomationGraphRunMode.Test, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(state, Assert.Single(result.Trace.Single(trace => trace.NodeId == target.Id).TypedInputs!["value"]).State);
    }

    [Fact]
    public async Task Runtime_rejects_value_type_mismatch_before_target_executor_runs()
    {
        var source = Node("Source", new AutomationGraphPortDefinition("value", "Value", AutomationGraphPortDirection.Output, "any"));
        var target = Node("Target", new AutomationGraphPortDefinition("value", "Value", AutomationGraphPortDirection.Input, "string"));
        var sourceExecutor = new ReturningExecutor("Source", _ => new AutomationGraphNodeExecutionResult(
            true,
            "Produced.",
            TypedOutputs: new Dictionary<string, AutomationGraphValue>
            {
                ["value"] = AutomationGraphValue.FromJson("boolean", JsonDocument.Parse("true").RootElement)
            }));
        var targetExecutor = new ReturningExecutor("Target", _ => new(true, "Must not execute."));
        var graph = new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion,
            [source, target],
            [new AutomationGraphEdgeDefinition(source.Id, target.Id) { FromPortId = "value", ToPortId = "value" }]);

        var result = await new AutomationGraphRunner([sourceExecutor, targetExecutor])
            .RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("does not match input type", result.FailureMessage);
        Assert.Equal(0, targetExecutor.ExecuteCount);
        Assert.Contains(result.Trace, trace => trace.NodeId == target.Id && trace.Status == AutomationGraphTraceStatus.Failed);
    }

    private static AutomationGraphNodeDefinition Node(string category, params AutomationGraphPortDefinition[] ports) =>
        new(Guid.NewGuid(), category, null, null, new Dictionary<string, string>()) { Ports = ports };

    private sealed class ReturningExecutor(
        string category,
        Func<AutomationGraphNodeExecutionContext, AutomationGraphNodeExecutionResult> execute) : IAutomationGraphNodeExecutor
    {
        public int ExecuteCount { get; private set; }
        public bool CanExecute(AutomationGraphNodeDefinition node) => string.Equals(node.Category, category, StringComparison.OrdinalIgnoreCase);
        public Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecuteCount++;
            return Task.FromResult(execute(context));
        }
    }
}
