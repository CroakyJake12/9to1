using Haven.Application.Automations;

namespace Haven.Core.Tests;

public sealed class AutomationGraphRuntimeTests
{
    [Fact]
    public async Task Invalid_cycle_is_rejected_before_any_executor_side_effect()
    {
        var first = Node("Action"); var second = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second], [new(first.Id, second.Id), new(second.Id, first.Id)]);
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);
        Assert.False(result.Succeeded); Assert.Contains(result.ValidationIssues, issue => issue.Code == "graph.cycle"); Assert.Equal(0, executor.ExecuteCount); Assert.Empty(result.Trace);
    }

    [Fact]
    public async Task Condition_selects_only_matching_branch_and_marks_other_branch_skipped()
    {
        var trigger = Node("Trigger");
        var condition = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Condition", null, null, new Dictionary<string, string> { ["expression"] = "true" });
        var yes = Node("Action"); var no = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [trigger, condition, yes, no], [new AutomationGraphEdgeDefinition(trigger.Id, condition.Id), new AutomationGraphEdgeDefinition(condition.Id, yes.Id) { Branch = "true" }, new AutomationGraphEdgeDefinition(condition.Id, no.Id) { Branch = "false" }]);
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);
        Assert.True(result.Succeeded); Assert.Equal(1, executor.ExecuteCount); Assert.Equal(yes.Id, executor.ExecutedNodes.Single()); Assert.Contains(result.Trace, item => item.NodeId == condition.Id && item.Branch == "true"); Assert.Contains(result.Trace, item => item.NodeId == no.Id && item.Status == AutomationGraphTraceStatus.Skipped);
    }

    [Fact]
    public async Task Test_mode_is_forwarded_and_preserved_in_trace_result()
    {
        var action = Node("Action"); var executor = new RecordingExecutor("Action"); var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [action], []);
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Test, CancellationToken.None);
        Assert.True(result.Succeeded); Assert.Equal(AutomationGraphRunMode.Test, result.Mode); Assert.Equal(AutomationGraphRunMode.Test, executor.LastMode); Assert.Equal(AutomationGraphTraceStatus.Succeeded, Assert.Single(result.Trace).Status);
    }

    [Fact]
    public async Task Port_direction_errors_fail_validation_before_executor_runs()
    {
        var first = Node("Action") with { Ports = [new("in", "In", AutomationGraphPortDirection.Input, "flow", true)] }; var second = Node("Action"); var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second], [new AutomationGraphEdgeDefinition(first.Id, second.Id) { FromPortId = "in" }]);
        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Real, CancellationToken.None);
        Assert.False(result.Succeeded); Assert.Contains(result.ValidationIssues, issue => issue.Code == "edge.from-port.direction"); Assert.Equal(0, executor.ExecuteCount);
    }

    [Fact]
    public async Task Trace_captures_resolved_upstream_inputs_for_each_executed_node()
    {
        var first = Node("Action");
        var second = Node("Action");
        var executor = new RecordingExecutor("Action");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [first, second], [new(first.Id, second.Id)]);

        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Test, CancellationToken.None);

        Assert.True(result.Succeeded);
        var firstTrace = result.Trace.Single(trace => trace.NodeId == first.Id);
        var secondTrace = result.Trace.Single(trace => trace.NodeId == second.Id);
        Assert.Empty(firstTrace.Inputs ?? []);
        Assert.NotNull(secondTrace.Inputs);
        Assert.Equal(first.Id.ToString(), secondTrace.Inputs[first.Id]);
    }

    [Fact]
    public async Task Runtime_executes_150_node_chain_with_complete_ordered_trace()
    {
        const int count = 150;
        var nodes = Enumerable.Range(0, count).Select(_ => Node("StressAction")).ToArray();
        var edges = Enumerable.Range(0, count - 1).Select(index => new AutomationGraphEdgeDefinition(nodes[index].Id, nodes[index + 1].Id)).ToArray();
        var executor = new RecordingExecutor("StressAction");
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, nodes, edges);

        var result = await new AutomationGraphRunner([executor]).RunAsync(graph, AutomationGraphRunMode.Test, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(count, executor.ExecuteCount);
        Assert.Equal(count, result.Trace.Count);
        Assert.Equal(nodes.Select(node => node.Id), result.Trace.Select(trace => trace.NodeId));
        Assert.All(result.Trace, trace => Assert.Equal(AutomationGraphTraceStatus.Succeeded, trace.Status));
    }

    [Fact]
    public async Task Durable_wait_yields_snapshot_and_resume_completes_without_replaying_A()
    {
        var a = Node("Action");
        var wait = Node("Wait");
        var b = Node("Action");
        var executor = new RecordingExecutor("Action");
        // The Wait node is a durable yield via a recording Wait executor; its ResumeAt is in the past so
        // resume satisfies it immediately without sleeping.
        var waitExecutor = new RecordingWaitExecutor(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [a, wait, b],
            [new(a.Id, wait.Id), new(wait.Id, b.Id)]);
        var graphId = Guid.NewGuid();
        var digest = "abcdef0123456789";

        var first = await new AutomationGraphRunner([executor, waitExecutor])
            .ResumeAsync(graph, AutomationGraphRunMode.Real, null, graphId, 4, 1, digest, CancellationToken.None);
        var yield = Assert.IsType<AutomationGraphRunYield>(first);
        Assert.Equal(1, executor.ExecuteCount);
        Assert.Equal(a.Id, executor.ExecutedNodes.Single());
        Assert.Equal(AutomationGraphYieldKind.Wait, yield.Continuation.PendingYield!.Kind);
        Assert.Equal(wait.Id, yield.Continuation.PendingYield.YieldNodeId);
        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), yield.Continuation.PendingYield.ResumeAt);

        // Resume the SAME graph/continuation: A is never replayed, the wait is satisfied once, B runs.
        var resumed = await new AutomationGraphRunner([executor, waitExecutor])
            .ResumeAsync(graph, AutomationGraphRunMode.Real, yield.Continuation, graphId, 4, 1, digest, CancellationToken.None);
        var terminal = Assert.IsType<AutomationGraphRunTerminal>(resumed);
        Assert.True(terminal.Result.Succeeded);
        Assert.Equal(2, executor.ExecuteCount);
        Assert.Equal([a.Id, b.Id], executor.ExecutedNodes);
        Assert.Contains(terminal.Result.Trace, item => item.NodeId == wait.Id && item.Message.Contains("observed", StringComparison.Ordinal));

        // A durable wait before its due time must NOT recompute a moving deadline: it re-yields the
        // ORIGINAL ResumeAt. Use a future due and assert the persisted due is unchanged.
        var futureWaitExecutor = new RecordingWaitExecutor(DateTimeOffset.UtcNow.AddDays(2));
        var futureGraph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [a, wait, b],
            [new(a.Id, wait.Id), new(wait.Id, b.Id)]);
        var futureYield = (AutomationGraphRunYield)await new AutomationGraphRunner([executor, futureWaitExecutor])
            .ResumeAsync(futureGraph, AutomationGraphRunMode.Real, null, graphId, 4, 1, digest, CancellationToken.None);
        var originalDue = futureYield.Continuation.PendingYield!.ResumeAt;
        var reYield = (AutomationGraphRunYield)await new AutomationGraphRunner([executor, futureWaitExecutor])
            .ResumeAsync(futureGraph, AutomationGraphRunMode.Real, futureYield.Continuation, graphId, 4, 1, digest, CancellationToken.None);
        Assert.Equal(originalDue, reYield.Continuation.PendingYield!.ResumeAt);
    }

    [Fact]
    public async Task Resume_refuses_mismatched_pin_schema_or_frontier_before_any_side_effect()
    {
        var a = Node("Action");
        var wait = Node("Wait");
        var b = Node("Action");
        var executor = new RecordingExecutor("Action");
        var waitExecutor = new RecordingWaitExecutor(DateTimeOffset.UtcNow.AddDays(2));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, [a, wait, b],
            [new(a.Id, wait.Id), new(wait.Id, b.Id)]);
        var graphId = Guid.NewGuid();
        var digest = "abcdef0123456789";
        var first = (AutomationGraphRunYield)await new AutomationGraphRunner([executor, waitExecutor])
            .ResumeAsync(graph, AutomationGraphRunMode.Real, null, graphId, 4, 1, digest, CancellationToken.None);

        // A different pinned revision/schema/source digest must refuse before resuming.
        var mismatched = await new AutomationGraphRunner([executor, waitExecutor])
            .ResumeAsync(graph, AutomationGraphRunMode.Real, first.Continuation, graphId, 5, 1, digest, CancellationToken.None);
        var refused = Assert.IsType<AutomationGraphRunTerminal>(mismatched);
        Assert.False(refused.Result.Succeeded);
        Assert.Contains(refused.Result.ValidationIssues, issue => issue.Code == "continuation.revision.mismatch");

        // A corrupt/unknown version must refuse at the codec before resume.
        Assert.False(AutomationGraphContinuationCodec.TryDeserialize("haven:automation-graph-continuation:v1:{not-json", out _));
        // A validly-serialized continuation that violates a structural invariant (missing GraphID) must
        // also be refused by the codec; serialization round-trip is a consistency check, not trust.
        var missingIdentity = first.Continuation with { GraphId = Guid.Empty };
        Assert.False(AutomationGraphContinuationCodec.TryDeserialize(AutomationGraphContinuationCodec.Serialize(missingIdentity), out _));
    }

    private static AutomationGraphNodeDefinition Node(string category) => new(Guid.NewGuid(), category, null, null, new Dictionary<string, string>());
    private sealed class RecordingExecutor(string category) : IAutomationGraphNodeExecutor
    {
        public int ExecuteCount { get; private set; } public List<Guid> ExecutedNodes { get; } = []; public AutomationGraphRunMode? LastMode { get; private set; }
        public bool CanExecute(AutomationGraphNodeDefinition node) => string.Equals(node.Category, category, StringComparison.OrdinalIgnoreCase);
        public Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); ExecuteCount++; ExecutedNodes.Add(context.Node.Id); LastMode = context.Mode; return Task.FromResult(new AutomationGraphNodeExecutionResult(true, "Executed.", context.Node.Id.ToString())); }
    }
    private sealed class RecordingWaitExecutor(DateTimeOffset resumeAt) : IAutomationGraphNodeExecutor
    {
        public bool CanExecute(AutomationGraphNodeDefinition node) => string.Equals(node.Category, "Wait", StringComparison.OrdinalIgnoreCase);
        public Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AutomationGraphNodeExecutionResult(true, $"Will resume {resumeAt:O}.", null)
            {
                Yield = new AutomationGraphYield(AutomationGraphYieldKind.Wait, YieldNodeId: context.Node.Id, ResumeAt: resumeAt)
            });
        }
    }
}
