using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Core.Tests;

public sealed class DocumentVectorSubpathEditingTests
{
    [Fact]
    public void Actual_open_close_changes_only_selected_closure_and_retains_same_history_origin_and_ids()
    {
        var shape = DocumentVectorShapes.CreateEditableStarter("Closure");
        shape.Paths.Add(DocumentVectorShapes.CreateEditableStarter("Untouched").Paths[0]);
        var owner = new DocumentVectorShapeEditor(shape); var path = owner.Shape.Paths[0]; var subpath = path.Subpaths[0];
        var before = JsonSerializer.Serialize(owner.Shape); var other = JsonSerializer.Serialize(owner.Shape.Paths[1]);
        var nodes = JsonSerializer.Serialize(subpath.Nodes); var stroke = JsonSerializer.Serialize(path.Stroke); var fill = JsonSerializer.Serialize(path.Fill);
        owner.SetSubpathClosed(path.Id, subpath.Id, false, DocumentOperationOrigin.System);
        Assert.False(owner.Shape.Paths[0].Subpaths[0].Closed); Assert.Equal(nodes, JsonSerializer.Serialize(owner.Shape.Paths[0].Subpaths[0].Nodes));
        Assert.Equal(stroke, JsonSerializer.Serialize(owner.Shape.Paths[0].Stroke)); Assert.Equal(fill, JsonSerializer.Serialize(owner.Shape.Paths[0].Fill));
        Assert.Equal(other, JsonSerializer.Serialize(owner.Shape.Paths[1])); Assert.Equal(subpath.Id, owner.Shape.Paths[0].Subpaths[0].Id);
        Assert.Equal(DocumentOperationOrigin.System, owner.LastOperation!.Origin); Assert.Equal("Open vector subpath", owner.LastOperation.Name);
        var opened = JsonSerializer.Serialize(owner.Shape); Assert.True(owner.Undo()); Assert.Equal(before, JsonSerializer.Serialize(owner.Shape));
        Assert.True(owner.Redo()); Assert.Equal(opened, JsonSerializer.Serialize(owner.Shape));
        owner.SetSubpathClosed(path.Id, subpath.Id, true); Assert.True(owner.Shape.Paths[0].Subpaths[0].Closed);
    }

    [Fact]
    public void Scoped_subpath_id_handles_reused_other_path_id_without_editing_its_original_geometry()
    {
        var shape = DocumentVectorShapes.CreateEditableStarter(); var first = shape.Paths[0];
        var other = DocumentVectorShapes.CreateEditableStarter("Other").Paths[0]; other.Subpaths[0].Id = first.Subpaths[0].Id; shape.Paths.Add(other);
        var owner = new DocumentVectorShapeEditor(shape); var before = JsonSerializer.Serialize(owner.Shape.Paths[1]);
        owner.SetSubpathClosed(first.Id, first.Subpaths[0].Id, false);
        Assert.False(owner.Shape.Paths[0].Subpaths[0].Closed); Assert.True(owner.Shape.Paths[1].Subpaths[0].Closed);
        Assert.Equal(before, JsonSerializer.Serialize(owner.Shape.Paths[1]));
    }

    [Fact]
    public void Same_closure_missing_scope_and_invalid_origin_leave_current_and_history_exact()
    {
        var owner = new DocumentVectorShapeEditor(DocumentVectorShapes.CreateEditableStarter()); var path = owner.Shape.Paths[0]; var subpath = path.Subpaths[0];
        var before = JsonSerializer.Serialize(owner.Shape); owner.SetSubpathClosed(path.Id, subpath.Id, subpath.Closed);
        Assert.Equal(before, JsonSerializer.Serialize(owner.Shape)); Assert.False(owner.CanUndo); Assert.Null(owner.LastOperation);
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.SetSubpathClosed(Guid.NewGuid(), subpath.Id, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.SetSubpathClosed(path.Id, Guid.NewGuid(), false));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.SetSubpathClosed(path.Id, subpath.Id, false, (DocumentOperationOrigin)999));
        Assert.Equal(before, JsonSerializer.Serialize(owner.Shape)); Assert.False(owner.CanUndo); Assert.Null(owner.LastOperation);
    }
}
