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

    [Theory]
    [InlineData("VALUE", "value")]
    [InlineData("value", "VALUE")]
    [InlineData("VaLuE", "vAlUe")]
    public async Task Validated_port_casing_preserves_the_actual_typed_value(string outputPort, string edgePort)
    {
        var source = Node("Source", new AutomationGraphPortDefinition("value", "Value", AutomationGraphPortDirection.Output, "string"));
        var target = Node("Target", new AutomationGraphPortDefinition("value", "Value", AutomationGraphPortDirection.Input, "string"));
        var sourceExecutor = new ReturningExecutor("Source", _ => new AutomationGraphNodeExecutionResult(
            true, "Produced.", TypedOutputs: new Dictionary<string, AutomationGraphValue>
            { [outputPort] = AutomationGraphValue.FromString("canonical value") }));
        var targetExecutor = new ReturningExecutor("Target", context =>
        {
            var value = Assert.Single(context.TypedInputs!["value"]);
            Assert.Equal(AutomationGraphValueState.Value, value.State);
            Assert.True(value.TryGetString(out var actual));
            Assert.Equal("canonical value", actual);
            return new(true, "Observed value.");
        });
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [source, target],
            [new AutomationGraphEdgeDefinition(source.Id, target.Id) { FromPortId = edgePort, ToPortId = "VALUE" }]);

        var result = await new AutomationGraphRunner([sourceExecutor, targetExecutor])
            .RunAsync(graph, AutomationGraphRunMode.Test, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, sourceExecutor.ExecuteCount);
        Assert.Equal(1, targetExecutor.ExecuteCount);
    }

    [Fact]
    public async Task Same_port_name_on_distinct_source_nodes_keeps_outputs_isolated_by_node_id()
    {
        var first = Node("Source", new AutomationGraphPortDefinition("value", "Value", AutomationGraphPortDirection.Output, "string"));
        var second = Node("Source", new AutomationGraphPortDefinition("value", "Value", AutomationGraphPortDirection.Output, "string"));
        var target = Node("Target",
            new AutomationGraphPortDefinition("left", "Left", AutomationGraphPortDirection.Input, "string"),
            new AutomationGraphPortDefinition("right", "Right", AutomationGraphPortDirection.Input, "string"));
        var sourceExecutor = new ReturningExecutor("Source", context => new AutomationGraphNodeExecutionResult(
            true, "Produced.", TypedOutputs: new Dictionary<string, AutomationGraphValue>
            {
                ["value"] = AutomationGraphValue.FromString(context.Node.Id == first.Id ? "first" : "second")
            }));
        var targetExecutor = new ReturningExecutor("Target", context =>
        {
            Assert.True(Assert.Single(context.TypedInputs!["left"]).TryGetString(out var left));
            Assert.True(Assert.Single(context.TypedInputs["right"]).TryGetString(out var right));
            Assert.Equal("first", left);
            Assert.Equal("second", right);
            return new(true, "Observed independent outputs.");
        });
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second, target],
        [
            new AutomationGraphEdgeDefinition(first.Id, target.Id) { FromPortId = "value", ToPortId = "left" },
            new AutomationGraphEdgeDefinition(second.Id, target.Id) { FromPortId = "value", ToPortId = "right" }
        ]);

        var result = await new AutomationGraphRunner([sourceExecutor, targetExecutor])
            .RunAsync(graph, AutomationGraphRunMode.Test, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, sourceExecutor.ExecuteCount);
        Assert.Equal(1, targetExecutor.ExecuteCount);
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

    [Fact]
    public async Task Runtime_rejects_wrong_json_shape_from_typed_output_before_downstream_executor_runs()
    {
        using var document = JsonDocument.Parse("true");
        var source = Node("Source", new AutomationGraphPortDefinition("value", "Value", AutomationGraphPortDirection.Output, "number"));
        var target = Node("Target", new AutomationGraphPortDefinition("value", "Value", AutomationGraphPortDirection.Input, "number"));
        var sourceExecutor = new ReturningExecutor("Source", _ => new AutomationGraphNodeExecutionResult(
            true,
            "Produced.",
            TypedOutputs: new Dictionary<string, AutomationGraphValue>
            {
                ["value"] = AutomationGraphValue.FromJson("number", document.RootElement)
            }));
        var targetExecutor = new ReturningExecutor("Target", _ => new(true, "Must not execute."));
        var graph = new AutomationGraphDefinition(
            AutomationGraphDefinition.CurrentVersion,
            [source, target],
            [new AutomationGraphEdgeDefinition(source.Id, target.Id) { FromPortId = "value", ToPortId = "value" }]);

        var result = await new AutomationGraphRunner([sourceExecutor, targetExecutor])
            .RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("incompatible shape", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, sourceExecutor.ExecuteCount);
        Assert.Equal(0, targetExecutor.ExecuteCount);
        Assert.Contains(result.Trace, trace => trace.NodeId == source.Id && trace.Status == AutomationGraphTraceStatus.Failed);
    }

    [Theory]
    [InlineData(null, "Failure detail")]
    [InlineData("sample.failed", null)]
    [InlineData(" ", "Failure detail")]
    [InlineData("sample.failed", " ")]
    public async Task Malformed_error_sample_is_rejected_before_any_executor_runs(string? errorCode, string? message)
    {
        var trigger = Node("Trigger", new AutomationGraphPortDefinition("payload", "Payload", AutomationGraphPortDirection.Output, "string"));
        var action = Node("Target", new AutomationGraphPortDefinition("payload", "Payload", AutomationGraphPortDirection.Input, "string"));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, action],
            [new AutomationGraphEdgeDefinition(trigger.Id, action.Id) { FromPortId = "payload", ToPortId = "payload" }]);
        var triggerOutputs = new Dictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>
        {
            [trigger.Id] = new Dictionary<string, AutomationGraphValue>
            {
                ["payload"] = new AutomationGraphValue("string", AutomationGraphValueState.Error,
                    ErrorCode: errorCode, Message: message)
            }
        };
        var targetExecutor = new ReturningExecutor("Target", _ => new(true, "Must not execute."));

        var result = await new AutomationGraphRunner([targetExecutor]).RunAsync(
            graph, AutomationGraphRunMode.Test, CancellationToken.None, triggerOutputs);

        Assert.False(result.Succeeded);
        Assert.Contains(result.ValidationIssues, issue => issue.Code == "run.input.value.shape");
        Assert.Equal(0, targetExecutor.ExecuteCount);
        Assert.Empty(result.Trace);
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
