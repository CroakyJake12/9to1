using System.Text.Json;
using Haven.Application.NodeGraph;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class NodeGraphDraftHistoryTests
{
    [Fact]
    public void Reaching_history_capacity_keeps_editing_and_latest_undo_redo_in_order()
    {
        var node = new GraphNode(Guid.NewGuid(), "test.counter", 1, "tests", "test.counter", null,
            JsonSerializer.SerializeToElement(new { value = 0 }), []);
        var graph = new GraphDocument(1, Guid.NewGuid(), 1, "test", GraphRevisionState.Draft, [node], []);
        var schemas = new NodeGraphSchemaRegistry(
            [new("test.counter", 1, "tests", "test.counter", JsonSerializer.SerializeToElement(new
            {
                type = "object", properties = new { value = new { type = "integer" } }, required = new[] { "value" }, additionalProperties = false
            }), [])],
            [new("test", new HashSet<string> { "test.counter" }, new HashSet<string> { "test.counter" })]);
        var editor = new NodeGraphDraftEditor(graph, schemas);
        for (var value = 1; value <= 260; value++)
            Assert.True(editor.SetConfiguration(editor.Current.Revision, node.NodeId,
                JsonSerializer.SerializeToElement(new { value })).Succeeded);

        var beforeInvalid = editor.Current.Revision;
        Assert.False(editor.SetConfiguration(beforeInvalid, node.NodeId, JsonSerializer.SerializeToElement(new { invalid = 1 })).Succeeded);
        Assert.Equal(beforeInvalid, editor.Current.Revision);
        for (var value = 259; value >= 4; value--)
        {
            var result = editor.Undo(editor.Current.Revision);
            Assert.True(result.Succeeded);
            Assert.Equal(value, result.Document.Nodes[0].Configuration.GetProperty("value").GetInt32());
            Assert.Equal(graph.GraphId, result.Document.GraphId);
            Assert.Equal(node.NodeId, result.Document.Nodes[0].NodeId);
        }
        Assert.Equal("graph.history.empty", editor.Undo(editor.Current.Revision).Code);
        for (var value = 5; value <= 260; value++)
        {
            var result = editor.Redo(editor.Current.Revision);
            Assert.True(result.Succeeded);
            Assert.Equal(value, result.Document.Nodes[0].Configuration.GetProperty("value").GetInt32());
        }
        Assert.Equal("graph.history.empty", editor.Redo(editor.Current.Revision).Code);
        Assert.Equal(773, editor.Current.Revision);
    }
}
