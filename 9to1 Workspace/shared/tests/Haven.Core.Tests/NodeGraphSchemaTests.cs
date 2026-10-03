using System.Text.Json;
using Haven.Application.NodeGraph;
namespace Haven.Core.Tests;
public sealed class NodeGraphSchemaTests
{
    [Fact]
    public void Draft_history_uses_monotonic_revisions_and_rejects_active_or_stale_changes()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = false });
        var registry = new NodeGraphSchemaRegistry([new("node", 1, "files", "files.read", schema, [])],
            [new("profile", new HashSet<string> { "node" }, new HashSet<string> { "files.read" })]);
        var draft = new GraphDocument(1, Guid.NewGuid(), 1, "profile", GraphRevisionState.Draft, [], []);
        var editor = new NodeGraphDraftEditor(draft, registry);
        var node = new GraphNode(Guid.NewGuid(), "node", 1, "files", "files.read", null, JsonSerializer.SerializeToElement(new { }), []);
        Assert.True(editor.AddNode(1, node).Succeeded);
        Assert.False(editor.RemoveNode(1, node.NodeId).Succeeded);
        Assert.Single(editor.Current.Nodes);
        Assert.True(editor.Undo(2).Succeeded); Assert.Empty(editor.Current.Nodes); Assert.Equal(3, editor.Current.Revision);
        Assert.True(editor.Redo(3).Succeeded); Assert.Single(editor.Current.Nodes); Assert.Equal(4, editor.Current.Revision);
        Assert.False(editor.SetConfiguration(4, node.NodeId, JsonSerializer.SerializeToElement(new { unexpected = true })).Succeeded);
        Assert.Equal("graph.node.not-found", editor.RemoveNode(4, Guid.NewGuid()).Code);
        Assert.Equal("graph.connection.not-found", editor.Disconnect(4, Guid.NewGuid()).Code);
        Assert.Equal(4, editor.Current.Revision);
        Assert.False(new NodeGraphDraftEditor(draft with { State = GraphRevisionState.Active }, registry).AddNode(1, node).Succeeded);
    }
    [Fact]
    public void Connections_require_exact_registered_types_and_retired_schema_is_preserved_as_diagnostic()
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = false });
        var registry = new NodeGraphSchemaRegistry([
            new("source", 1, "files", "files.read", schema, [new("out", GraphPortDirection.Output, "image")]),
            new("sink", 1, "present", "present.edit", schema, [new("in", GraphPortDirection.Input, "text")])],
            [new("full", new HashSet<string> { "source", "sink" }, new HashSet<string> { "files.read", "present.edit" })]);
        var output = Guid.NewGuid(); var input = Guid.NewGuid();
        var graph = new GraphDocument(1, Guid.NewGuid(), 1, "full", GraphRevisionState.Draft,
            [new(Guid.NewGuid(), "source", 1, "files", "files.read", null, JsonSerializer.SerializeToElement(new { }), [new(output, "out", GraphPortDirection.Output, "image", false)]),
             new(Guid.NewGuid(), "sink", 1, "present", "present.edit", null, JsonSerializer.SerializeToElement(new { }), [new(input, "in", GraphPortDirection.Input, "text", false)])],
            [new(Guid.NewGuid(), output, input)]);
        Assert.Contains(registry.Validate(graph), diagnostic => diagnostic.Code == "graph.connection.type.invalid");
        Assert.Contains(registry.Validate(graph with { Nodes = [graph.Nodes[0] with { TypeVersion = 99 }, graph.Nodes[1]] }),
            diagnostic => diagnostic.Code == "graph.node.migration-or-permission-required");
        Assert.Empty(registry.ListNodeTypes("unregistered"));
        Assert.Contains(registry.Validate(graph with { SchemaVersion = 99 }), diagnostic => diagnostic.Code == "graph.schema.migration-required");
    }
}
