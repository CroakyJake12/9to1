using System.Text.Json;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;

namespace Haven.Core.Tests;

public sealed class NodeGraphAutomationAdapterTests
{
    [Fact]
    public void Projection_retains_canonical_receipt_and_edge_port_identity_with_detached_parameters()
    {
        var (registry, graph, binding) = Fixture();
        var parameters = new Dictionary<string, string> { ["value"] = "original" };
        binding = binding with { Project = node => Project(node) with { Parameters = parameters } };
        var receipt = new NodeGraphAutomationAdapter(registry, [binding]).Compile(graph);
        Assert.Equal(graph.GraphId, receipt.GraphId);
        Assert.Equal(graph.Revision, receipt.SourceRevision);
        Assert.Equal(graph.SchemaVersion, receipt.SourceSchemaVersion);
        Assert.Equal(graph.CapabilityProfileId, receipt.CapabilityProfileId);
        Assert.Equal(64, receipt.SourceDigest.Length);
        var edge = Assert.Single(receipt.Definition.Edges);
        Assert.Equal(graph.Connections[0].ConnectionId, edge.Id);
        Assert.Equal(graph.Nodes[0].NodeId, edge.FromNodeId);
        Assert.Equal(graph.Nodes[1].NodeId, edge.ToNodeId);
        Assert.Equal("out", edge.FromPortId);
        Assert.Equal("in", edge.ToPortId);
        parameters["value"] = "changed after compile";
        Assert.All(receipt.Definition.Nodes, node => Assert.Equal("original", node.Parameters["value"]));
        var changed = graph with { Revision = graph.Revision + 1 };
        Assert.NotEqual(receipt.SourceDigest, new NodeGraphAutomationAdapter(registry, [binding]).Compile(changed).SourceDigest);
    }

    [Fact]
    public void Missing_ambiguous_entity_app_or_port_binding_never_produces_an_execution_projection()
    {
        var (registry, graph, binding) = Fixture();
        Assert.Throws<InvalidDataException>(() => new NodeGraphAutomationAdapter(registry, []).Compile(graph));
        Assert.Throws<InvalidDataException>(() => new NodeGraphAutomationAdapter(registry, [binding, binding]).Compile(graph));
        Assert.Throws<InvalidDataException>(() => new NodeGraphAutomationAdapter(registry,
            [binding with { SupportsEntity = _ => false }]).Compile(graph));
        Assert.Throws<InvalidDataException>(() => new NodeGraphAutomationAdapter(registry,
            [binding with { AppId = "unrelated" }]).Compile(graph));
        Assert.Throws<InvalidDataException>(() => new NodeGraphAutomationAdapter(registry,
            [binding with { Project = node => Project(node) with { Ports = [new("in", "In", AutomationGraphPortDirection.Input, "text"),
                new("out", "Out", AutomationGraphPortDirection.Output, "flow")] } }]).Compile(graph));
        Assert.Throws<InvalidDataException>(() => new NodeGraphAutomationAdapter(registry,
            [binding with { Project = node => Project(node) with { Id = Guid.NewGuid() } }]).Compile(graph));
    }

    private static AutomationGraphNodeDefinition Project(GraphNode node) =>
        new(node.NodeId, "Value", null, null, new Dictionary<string, string>())
        {
            Ports = [new("in", "In", AutomationGraphPortDirection.Input, "flow"),
                new("out", "Out", AutomationGraphPortDirection.Output, "flow")]
        };

    private static (NodeGraphSchemaRegistry Registry, GraphDocument Graph, AutomationNodeGraphBinding Binding) Fixture()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = false });
        var registry = new NodeGraphSchemaRegistry([new("automation.value", 1, "automations", "automation.value", schema,
                [new("in", GraphPortDirection.Input, "flow"), new("out", GraphPortDirection.Output, "flow")])],
            [new("automation", new HashSet<string> { "automation.value" }, new HashSet<string> { "automation.value" })]);
        GraphNode MakeNode() => new(Guid.NewGuid(), "automation.value", 1, "automations", "automation.value", null,
            JsonSerializer.SerializeToElement(new { }), [new(Guid.NewGuid(), "in", GraphPortDirection.Input, "flow", false),
                new(Guid.NewGuid(), "out", GraphPortDirection.Output, "flow", false)]);
        var first = MakeNode(); var second = MakeNode();
        var graph = new GraphDocument(1, Guid.NewGuid(), 8, "automation", GraphRevisionState.Active, [first, second],
            [new(Guid.NewGuid(), first.Ports[1].PortId, second.Ports[0].PortId)]);
        return (registry, graph, new("automation.value", 1, "automations", "automation.value", entity => entity is null, Project));
    }
}
