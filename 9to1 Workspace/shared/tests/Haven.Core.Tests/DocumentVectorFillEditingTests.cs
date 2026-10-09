using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Core.Tests;

public sealed class DocumentVectorFillEditingTests
{
    [Fact]
    public void Actual_fill_edit_keeps_other_path_geometry_stroke_and_uses_same_history_and_origin()
    {
        var shape = DocumentVectorShapes.CreateEditableStarter("Fill");
        var other = DocumentVectorShapes.CreateEditableStarter("Other").Paths[0]; shape.Paths.Add(other);
        var owner = new DocumentVectorShapeEditor(shape); var original = JsonSerializer.Serialize(owner.Shape);
        var id = owner.Shape.Paths[0].Id; var untouched = JsonSerializer.Serialize(owner.Shape.Paths[1]);
        var nodes = JsonSerializer.Serialize(owner.Shape.Paths[0].Subpaths); var stroke = JsonSerializer.Serialize(owner.Shape.Paths[0].Stroke);
        var request = new DocumentVectorFill { Kind = DocumentVectorFillKind.None, Color = "  #FF00FF00  ", Opacity = .375 };
        owner.SetPathFill(id, request, DocumentVectorFillRule.NonZero, DocumentOperationOrigin.System);
        request.Color = "#000000"; request.Opacity = 0;
        Assert.Equal("#FF00FF00", owner.Shape.Paths[0].Fill.Color); Assert.Equal(.375, owner.Shape.Paths[0].Fill.Opacity);
        Assert.Equal(DocumentVectorFillKind.None, owner.Shape.Paths[0].Fill.Kind); Assert.Equal(DocumentVectorFillRule.NonZero, owner.Shape.Paths[0].FillRule);
        Assert.Equal(untouched, JsonSerializer.Serialize(owner.Shape.Paths[1])); Assert.Equal(nodes, JsonSerializer.Serialize(owner.Shape.Paths[0].Subpaths));
        Assert.Equal(stroke, JsonSerializer.Serialize(owner.Shape.Paths[0].Stroke)); Assert.Equal(DocumentOperationOrigin.System, owner.LastOperation!.Origin);
        var edited = JsonSerializer.Serialize(owner.Shape); Assert.True(owner.Undo()); Assert.Equal(original, JsonSerializer.Serialize(owner.Shape));
        Assert.True(owner.Redo()); Assert.Equal(edited, JsonSerializer.Serialize(owner.Shape));
    }

    [Fact]
    public void Invalid_fill_and_missing_path_refuse_before_any_shape_or_history_change()
    {
        var owner = new DocumentVectorShapeEditor(DocumentVectorShapes.CreateEditableStarter()); var id = owner.Shape.Paths[0].Id;
        var before = JsonSerializer.Serialize(owner.Shape);
        foreach (var opacity in new[] { double.NaN, double.PositiveInfinity, -.001, 1.001 })
            Assert.Throws<ArgumentOutOfRangeException>(() => owner.SetPathFill(id, new() { Opacity = opacity }));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.SetPathFill(id, new() { Kind = (DocumentVectorFillKind)999 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.SetPathFill(id, new(), (DocumentVectorFillRule)999));
        Assert.Throws<ArgumentException>(() => owner.SetPathFill(id, new() { Color = "  " }));
        Assert.Throws<ArgumentOutOfRangeException>(() => owner.SetPathFill(Guid.NewGuid(), new()));
        Assert.Equal(before, JsonSerializer.Serialize(owner.Shape)); Assert.False(owner.CanUndo); Assert.Null(owner.LastOperation);
    }

    [Fact]
    public void Omitted_rule_retains_original_contour_semantics_and_existing_colour_style_API_still_works()
    {
        var shape = DocumentVectorShapes.CreateEditableStarter(); shape.Paths[0].FillRule = DocumentVectorFillRule.NonZero;
        var owner = new DocumentVectorShapeEditor(shape); var id = owner.Shape.Paths[0].Id;
        owner.SetPathFill(id, new() { Kind = DocumentVectorFillKind.Solid, Color = "#FF0000", Opacity = .25 });
        Assert.Equal(DocumentVectorFillRule.NonZero, owner.Shape.Paths[0].FillRule);
        owner.SetPathStyle(id, "#0000FF", "#00FF00", 7);
        Assert.Equal("#0000FF", owner.Shape.Paths[0].Fill.Color); Assert.Equal(.25, owner.Shape.Paths[0].Fill.Opacity);
        Assert.Equal(DocumentVectorFillRule.NonZero, owner.Shape.Paths[0].FillRule); Assert.Equal(7, owner.Shape.Paths[0].Stroke.Width);
    }
}
