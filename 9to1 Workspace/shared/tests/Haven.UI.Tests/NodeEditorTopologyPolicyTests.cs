using Haven.UI.Components;
using Xunit;

namespace Haven.UI.Tests;

public sealed class NodeEditorTopologyPolicyTests
{
    [Fact]
    public void Default_dag_rejects_self_and_cycles_and_still_activates_valid_drafts()
    {
        var first = Node(); var second = Node();
        var editor = new NodeEditor { Document = new([first, second], []) };
        Assert.Equal(NodeEditorTopologyPolicy.ExecutableDag, editor.TopologyPolicy);
        Assert.False(editor.Connect(first.Id, "out", first.Id, "in"));
        Assert.True(editor.Connect(first.Id, "out", second.Id, "in"));
        Assert.False(editor.Connect(second.Id, "out", first.Id, "in"));
        Assert.True(editor.TryActivate(editor.Document.Revision, out _));
    }

    [Fact]
    public void Explicit_relational_authoring_preserves_self_and_cycle_edges_without_visual_activation()
    {
        var first = Node(); var second = Node();
        var editor = new NodeEditor { TopologyPolicy = NodeEditorTopologyPolicy.RelationalAuthoring,
            Document = new([first, second], []) };
        Assert.True(editor.Connect(first.Id, "out", first.Id, "in"));
        Assert.True(editor.Connect(first.Id, "out", second.Id, "in"));
        Assert.True(editor.Connect(second.Id, "out", first.Id, "in"));
        Assert.Empty(editor.ValidateDocument());
        var retained = editor.Document;
        Assert.False(editor.TryActivate(retained.Revision, out _));
        Assert.Same(retained, editor.Document);
        Assert.Equal(NodeEditorRevisionState.Draft, editor.Document.State);
        var defaultEditor = new NodeEditor { Document = retained };
        Assert.Contains(defaultEditor.ValidateDocument(), error => error.Code == "cycle");
        Assert.False(defaultEditor.TryActivate(retained.Revision, out _));
        Assert.Equal(3, defaultEditor.Document.Edges.Count);
    }

    [Fact]
    public void Relational_topology_retains_direction_type_duplicate_and_port_capacity_checks()
    {
        var first = Node(false); var second = Node();
        var editor = new NodeEditor { TopologyPolicy = NodeEditorTopologyPolicy.RelationalAuthoring,
            Document = new([first, second], []) };
        Assert.False(editor.Connect(first.Id, "in", second.Id, "out"));
        Assert.False(editor.Connect(first.Id, "out", second.Id, "text"));
        Assert.False(editor.Connect(first.Id, "absent", first.Id, "in"));
        Assert.True(editor.Connect(first.Id, "out", first.Id, "in"));
        Assert.False(editor.Connect(first.Id, "out", first.Id, "in"));
        Assert.False(editor.Connect(first.Id, "out", second.Id, "in"));
        Assert.Single(editor.Document.Edges);
    }

    private static NodeEditorNode Node(bool multiple = true) => new(Guid.NewGuid(), "Data.Table", "Table")
    {
        Ports = [new("in", "Reference", NodeEditorPortDirection.Input, "key", multiple),
            new("out", "Key", NodeEditorPortDirection.Output, "key", multiple),
            new("text", "Text", NodeEditorPortDirection.Input, "text")]
    };
}
