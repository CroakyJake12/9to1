using System.Text.Json;
using Haven.Application.Automations;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class AutomationGraphCodecTests
{
    [Fact]
    public void DeviceNodeRoundTrips()
    {
        var target = new DeviceTargetDescriptor("current", "This PC", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice);
        var source = new DeviceAutomationNodeDefinition(Guid.NewGuid(), target, "applications.launch", new Dictionary<string,string> { ["name"] = "Calculator" });
        var json = AutomationGraphCodec.Serialize(new(1, [AutomationGraphNodeDefinition.FromDevice(source)], []));
        Assert.True(AutomationGraphCodec.TryDeserialize(json, out var graph));
        var node = Assert.Single(graph.Nodes).ToDevice();
        Assert.NotNull(node);
        Assert.Equal(source.Id, node.Id);
        Assert.Equal(target, node.Target);
        Assert.Equal("Calculator", node.Parameters["name"]);
    }

    [Fact]
    public void MissingEdgeNodesAreRejected()
    {
        var graph = new AutomationGraphDefinition(1, [], [new(Guid.NewGuid(), Guid.NewGuid())]);
        Assert.Throws<InvalidOperationException>(() => AutomationGraphCodec.Serialize(graph));
    }

    [Fact]
    public void DuplicateConnectionIdsAreRejectedOnWriteAndRead()
    {
        var first = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Trigger", null, null,
            new Dictionary<string, string>());
        var second = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Action", null, null,
            new Dictionary<string, string>());
        var id = Guid.NewGuid();
        var graph = new AutomationGraphDefinition(1, [first, second],
        [
            new AutomationGraphEdgeDefinition(first.Id, second.Id) { Id = id },
            new AutomationGraphEdgeDefinition(first.Id, second.Id) { Id = id }
        ]);

        Assert.Throws<InvalidOperationException>(() => AutomationGraphCodec.Serialize(graph));
        var malformedJson = JsonSerializer.Serialize(graph, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(AutomationGraphCodec.TryDeserialize(malformedJson, out _));
    }

    [Fact]
    public void EmptyConnectionIdIsRejectedInsteadOfBeingReplacedByTheEditor()
    {
        var first = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Trigger", null, null,
            new Dictionary<string, string>());
        var second = new AutomationGraphNodeDefinition(Guid.NewGuid(), "Action", null, null,
            new Dictionary<string, string>());
        var graph = new AutomationGraphDefinition(1, [first, second],
            [new AutomationGraphEdgeDefinition(first.Id, second.Id) { Id = Guid.Empty }]);

        Assert.Throws<InvalidOperationException>(() => AutomationGraphCodec.Serialize(graph));
        var malformedJson = JsonSerializer.Serialize(graph, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(AutomationGraphCodec.TryDeserialize(malformedJson, out _));
    }

    [Fact]
    public void Persisted_connection_without_id_is_rejected_repeatedly_without_replacing_last_valid_graph()
    {
        var nodeId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var edgeId = Guid.NewGuid();
        var lastValidJson = $"{{\"version\":1,\"nodes\":[{{\"id\":\"{nodeId:D}\",\"category\":\"Trigger\",\"parameters\":{{}}}},{{\"id\":\"{targetId:D}\",\"category\":\"Action\",\"parameters\":{{}}}}],\"edges\":[{{\"id\":\"{edgeId:D}\",\"fromNodeId\":\"{nodeId:D}\",\"toNodeId\":\"{targetId:D}\"}}]}}";
        Assert.True(AutomationGraphCodec.TryDeserialize(lastValidJson, out var lastValid));

        const string persistedMissingConnectionId = "{\"version\":1,\"nodes\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"category\":\"Trigger\",\"parameters\":{}},{\"id\":\"22222222-2222-2222-2222-222222222222\",\"category\":\"Action\",\"parameters\":{}}],\"edges\":[{\"fromNodeId\":\"11111111-1111-1111-1111-111111111111\",\"toNodeId\":\"22222222-2222-2222-2222-222222222222\"}]}";
        var graphAfterFailedLoad = lastValid;
        if (AutomationGraphCodec.TryDeserialize(persistedMissingConnectionId, out var candidate))
            graphAfterFailedLoad = candidate;
        Assert.False(AutomationGraphCodec.TryDeserialize(persistedMissingConnectionId, out _));

        const string duplicateEdgeCollections = "{\"version\":1,\"nodes\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"category\":\"Trigger\",\"parameters\":{}},{\"id\":\"22222222-2222-2222-2222-222222222222\",\"category\":\"Action\",\"parameters\":{}}],\"edges\":[{\"id\":\"33333333-3333-3333-3333-333333333333\",\"fromNodeId\":\"11111111-1111-1111-1111-111111111111\",\"toNodeId\":\"22222222-2222-2222-2222-222222222222\"}],\"edges\":[{\"fromNodeId\":\"11111111-1111-1111-1111-111111111111\",\"toNodeId\":\"22222222-2222-2222-2222-222222222222\"}]}";
        Assert.False(AutomationGraphCodec.TryDeserialize(duplicateEdgeCollections, out _));

        Assert.Equal(nodeId, graphAfterFailedLoad.Nodes[0].Id);
        Assert.Equal(edgeId, Assert.Single(graphAfterFailedLoad.Edges).Id);
    }

    [Fact]
    public void Empty_node_id_is_rejected_on_write_and_decode()
    {
        var graph = new AutomationGraphDefinition(1,
            [new AutomationGraphNodeDefinition(Guid.Empty, "Trigger", null, null, new Dictionary<string, string>())], []);
        Assert.Throws<InvalidOperationException>(() => AutomationGraphCodec.Serialize(graph));

        const string persistedEmptyNodeId = "{\"version\":1,\"nodes\":[{\"id\":\"00000000-0000-0000-0000-000000000000\",\"category\":\"Trigger\",\"parameters\":{}}],\"edges\":[]}";
        Assert.False(AutomationGraphCodec.TryDeserialize(persistedEmptyNodeId, out _));
    }

    [Fact]
    public void Explicit_valid_node_and_connection_ids_round_trip_unchanged()
    {
        var nodeId = Guid.NewGuid(); var targetId = Guid.NewGuid(); var edgeId = Guid.NewGuid();
        var graph = new AutomationGraphDefinition(1,
            [new AutomationGraphNodeDefinition(nodeId, "Trigger", null, null, new Dictionary<string, string>()),
             new AutomationGraphNodeDefinition(targetId, "Action", null, null, new Dictionary<string, string>())],
            [new AutomationGraphEdgeDefinition(nodeId, targetId) { Id = edgeId }]);

        Assert.True(AutomationGraphCodec.TryDeserialize(AutomationGraphCodec.Serialize(graph), out var restored));
        Assert.Equal(new[] { nodeId, targetId }, restored.Nodes.Select(node => node.Id));
        Assert.Equal(edgeId, Assert.Single(restored.Edges).Id);
    }
}
