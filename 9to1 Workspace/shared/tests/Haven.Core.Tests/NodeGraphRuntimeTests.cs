using System.Text.Json;
using Haven.Application.NodeGraph;
namespace Haven.Core.Tests;
public sealed class NodeGraphRuntimeTests
{
    [Fact]
    public async Task Reactive_cycle_uses_owner_adapter_and_shared_step_budget_without_DAG_coercion()
    {
        var owner = new Adapter(); var registry = Registry(owner);
        var result = await registry.ActivateAsync(owner.Request);
        Assert.False(result.Succeeded); Assert.Equal("step-limit", result.Code);
        Assert.Equal(3, result.StepsConsumed); Assert.Equal(3, result.Transitions.Count);
        Assert.Equal(1, owner.Executions);
    }
    [Fact]
    public async Task Stale_revision_denied_capability_unknown_event_and_duplicate_owner_never_execute()
    {
        var owner = new Adapter(); var registry = Registry(owner);
        Assert.False((await registry.ActivateAsync(owner.Request with { ExpectedRevision = 2 })).Succeeded);
        Assert.False((await registry.ActivateAsync(owner.Request with { EventType = "Unknown" })).Succeeded);
        Assert.False((await Registry(owner, owner).ActivateAsync(owner.Request)).Succeeded);
        owner.Deny = true;
        Assert.False((await registry.ActivateAsync(owner.Request)).Succeeded);
        Assert.Equal(0, owner.Executions);
    }
    private static NodeGraphRuntimeRegistry Registry(params Adapter[] adapters) => new(new NodeGraphSchemaRegistry(
        [new("react", 1, "forms", "forms.answer", JsonSerializer.SerializeToElement(new { type = "object" }),
            [new("in", GraphPortDirection.Input, "event"), new("out", GraphPortDirection.Output, "event")])],
        [new("forms", new HashSet<string> { "react" }, new HashSet<string> { "forms.answer" })]), adapters);
    private sealed class Adapter : INodeGraphRuntimeAdapter
    {
        public string GraphType => "forms.reactive";
        public IReadOnlySet<string> SupportedEvents { get; } = new HashSet<string> { "AnswerChanged" };
        public bool Deny;
        public int Executions;
        private readonly Guid _graphId = Guid.NewGuid(), _nodeId = Guid.NewGuid(), _input = Guid.NewGuid(), _output = Guid.NewGuid();
        public GraphActivationRequest Request => new(GraphType, _graphId, 1, _nodeId, "AnswerChanged", "session", "response", "participant", JsonSerializer.SerializeToElement(new { answer = 42 }), 3);
        public ValueTask<GraphRuntimeAdmission?> AdmitAsync(GraphActivationRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<GraphRuntimeAdmission?>(new(new(1, _graphId, 1, "forms", GraphRevisionState.Active,
                [new(_nodeId, "react", 1, "forms", "forms.answer", null, JsonSerializer.SerializeToElement(new { }),
                    [new(_input, "in", GraphPortDirection.Input, "event", false), new(_output, "out", GraphPortDirection.Output, "event", false)])],
                [new(Guid.NewGuid(), _output, _input)]), new("forms", new HashSet<string> { "react" },
                    Deny ? new HashSet<string>() : new HashSet<string> { "forms.answer" })));
        public ValueTask<GraphActivationResult> ExecuteAsync(GraphActivationRequest request, GraphRuntimeAdmission admission, GraphExecutionBudget budget, CancellationToken cancellationToken)
        {
            Executions++; var transitions = new List<GraphRuntimeTransition>();
            while (budget.TryConsume(cancellationToken)) transitions.Add(new(_nodeId, "reacting", request.Variables));
            return ValueTask.FromResult(new GraphActivationResult(false, "step-limit", transitions, budget.StepsConsumed));
        }
    }
}
