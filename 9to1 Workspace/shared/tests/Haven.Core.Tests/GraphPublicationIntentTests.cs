using System.Collections;
using System.Text.Json;
using Haven.Application;
using Haven.Application.NodeGraph;
using Xunit;

namespace Haven.Core.Tests;

public sealed class GraphPublicationIntentTests
{
    [Fact]
    public void Publication_detaches_caller_graph_and_complete_scope_tuple()
    {
        using var config = JsonDocument.Parse("{}");
        var node = new GraphNode(Guid.NewGuid(), "test", 1, "test", "test", null, config.RootElement, []);
        var nodes = new List<GraphNode> { node };
        var scopes = new List<ResourceScope> { new("automation.reusable-task", "original", "7", ResourceAccess.Write) };
        var graph = new GraphDocument(1, Guid.NewGuid(), 1, "test", GraphRevisionState.Draft, nodes, []);
        var owner = new GraphOwnerDescriptor("automations", Guid.NewGuid(), "automation.reusable-task", Guid.NewGuid());
        var actor = new AuthenticatedResourceActor("actor", "profile", null, null, "revision");
        var captured = GraphPublicationIntent.Capture(owner, actor, Guid.NewGuid(), GraphPublicationKind.SaveDraft, graph, 0, scopes, Schemas(), new(8192, 32));
        var arguments = captured.Arguments.GetRawText();
        nodes.Clear(); scopes[0] = new("different", "different", "8", ResourceAccess.Execute); config.Dispose();
        Assert.Equal(arguments, captured.Arguments.GetRawText());
        Assert.Equal("7", Assert.Single(captured.Scopes).Revision);
        Assert.Equal(ResourceAccess.Write, Assert.Single(captured.Scopes).Access);
        Assert.Single(captured.Graph.Nodes);
        Assert.Equal("automation.reusable-task/" + owner.StoreId.ToString("D") + "/" + owner.EntityId.ToString("D"), owner.CanonicalEntityId);
    }

    [Fact]
    public void Claimed_small_node_count_stops_at_actual_existing_schema_budget_sentinel()
    {
        var node = new GraphNode(Guid.NewGuid(), "test", 1, "test", "test", null, JsonSerializer.SerializeToElement(new { }), []);
        var nodes = new LyingNodes(node);
        var graph = new GraphDocument(1, Guid.NewGuid(), 1, "test", GraphRevisionState.Draft, nodes, []);
        Assert.Throws<ArgumentException>(() => GraphPublicationIntent.Capture(
            new("automations", Guid.NewGuid(), "automation.reusable-task", Guid.NewGuid()),
            new("actor", "profile", null, null, "revision"), Guid.NewGuid(), GraphPublicationKind.SaveDraft, graph, 0,
            [new("automation.reusable-task", "original", "7", ResourceAccess.Write)], Schemas(), new(1024 * 1024, 32)));
        Assert.Equal(10001, nodes.Consumed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Explicit_configuration_budget_rejects_oversized_scalar_or_aggregate_before_clone(bool oversizedScalar)
    {
        using var configuration = JsonDocument.Parse(JsonSerializer.Serialize(new { payload = oversizedScalar ? new string('x', 20000) : "12345678" }));
        var first = new GraphNode(Guid.NewGuid(), "test", 1, "test", "test", null, configuration.RootElement, []);
        var nodes = oversizedScalar ? new[] { first } : new[] { first, first with { NodeId = Guid.NewGuid() } };
        var graph = new GraphDocument(1, Guid.NewGuid(), 1, "test", GraphRevisionState.Draft, nodes, []);
        var schema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { payload = new { type = "string" } }, additionalProperties = false });
        var schemas = new NodeGraphSchemaRegistry([new("test", 1, "test", "test", schema, [])],
            [new("test", new HashSet<string> { "test" }, new HashSet<string> { "test" })]);
        Assert.Empty(schemas.Validate(graph)); // Schema itself allows these values; the explicit owner budget rejects them.
        Assert.Throws<ArgumentException>(() => GraphPublicationIntent.Capture(
            new("automations", Guid.NewGuid(), "automation.reusable-task", Guid.NewGuid()),
            new("actor", "profile", null, null, "revision"), Guid.NewGuid(), GraphPublicationKind.SaveDraft, graph, 0,
            [new("automation.reusable-task", "original", "7", ResourceAccess.Write)], schemas, new(30, 32)));
        Assert.Equal(oversizedScalar ? 20000 : 8, configuration.RootElement.GetProperty("payload").GetString()!.Length);
    }

    private static NodeGraphSchemaRegistry Schemas() => new(
        [new("test", 1, "test", "test", JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = false }), [])],
        [new("test", new HashSet<string> { "test" }, new HashSet<string> { "test" })]);
    private sealed class LyingNodes(GraphNode node) : IReadOnlyList<GraphNode>
    {
        public int Count => 1;
        public int Consumed { get; private set; }
        public GraphNode this[int index] => throw new NotSupportedException();
        public IEnumerator<GraphNode> GetEnumerator()
        {
            for (var index = 0; index < 1000000; index++) { Consumed++; yield return node; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
