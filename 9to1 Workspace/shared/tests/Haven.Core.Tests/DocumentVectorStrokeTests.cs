using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class DocumentVectorStrokeTests
{
    [Fact]
    public void Shared_style_updates_exact_path_preserves_other_geometry_and_uses_original_history_and_origin()
    {
        var shape = DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Rectangle);
        var other = DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Line).Paths.Single(); shape.Paths.Add(other);
        var editor = new DocumentVectorShapeEditor(shape); var selected = editor.Shape.Paths[0].Id;
        var nodes = JsonSerializer.Serialize(editor.Shape.Paths[0].Subpaths); var beforeOther = JsonSerializer.Serialize(editor.Shape.Paths[1]);
        var before = JsonSerializer.Serialize(editor.Shape.Paths[0].Stroke);
        var input = new DocumentVectorStroke { Enabled = true, Color = " #FF0000 ", Width = 4.5, Opacity = .375,
            Cap = DocumentVectorLineCap.Square, Join = DocumentVectorLineJoin.Bevel };
        editor.SetPathStroke(selected, input, DocumentOperationOrigin.Ai); input.Width = 999;
        var actual = editor.Shape.Paths[0].Stroke;
        Assert.Equal(4.5, actual.Width); Assert.Equal("#FF0000", actual.Color); Assert.Equal(.375, actual.Opacity);
        Assert.Equal(DocumentVectorLineCap.Square, actual.Cap); Assert.Equal(DocumentVectorLineJoin.Bevel, actual.Join);
        Assert.Equal(nodes, JsonSerializer.Serialize(editor.Shape.Paths[0].Subpaths)); Assert.Equal(beforeOther, JsonSerializer.Serialize(editor.Shape.Paths[1]));
        Assert.Equal(DocumentOperationOrigin.Ai, editor.LastOperation!.Origin); Assert.True(editor.CanUndo);
        Assert.True(editor.Undo()); Assert.Equal(before, JsonSerializer.Serialize(editor.Shape.Paths[0].Stroke));
        Assert.True(editor.Redo()); Assert.Equal(4.5, editor.Shape.Paths[0].Stroke.Width);
        editor.SetPathStroke(selected, new() { Enabled = false, Color = "#0000FF", Width = 0, Opacity = 0 });
        Assert.False(editor.Shape.Paths[0].Stroke.Enabled); Assert.Equal(0, editor.Shape.Paths[0].Stroke.Width);
    }

    [Fact]
    public void Invalid_style_or_path_refuses_before_original_history_or_geometry_changes()
    {
        var editor = new DocumentVectorShapeEditor(DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Line));
        var id = editor.Shape.Paths[0].Id; var original = JsonSerializer.Serialize(editor.Shape);
        DocumentVectorStroke[] invalid = [new() { Width = double.NaN }, new() { Width = double.PositiveInfinity },
            new() { Width = -.01 }, new() { Width = 10000.01 }, new() { Opacity = double.NaN }, new() { Opacity = -.01 },
            new() { Opacity = 1.01 }, new() { Cap = (DocumentVectorLineCap)999 }, new() { Join = (DocumentVectorLineJoin)999 }];
        foreach (var value in invalid) Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetPathStroke(id, value));
        Assert.Throws<ArgumentException>(() => editor.SetPathStroke(id, new() { Color = " " }));
        Assert.Throws<ArgumentNullException>(() => editor.SetPathStroke(id, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetPathStroke(Guid.NewGuid(), new()));
        Assert.Equal(original, JsonSerializer.Serialize(editor.Shape)); Assert.False(editor.CanUndo);
        editor.SetPathStroke(id, new() { Width = 10000, Opacity = 1 }); Assert.Equal(10000, editor.Shape.Paths[0].Stroke.Width);
    }

    [Fact]
    public void Existing_path_style_API_keeps_its_fill_stroke_and_geometry_contract()
    {
        var editor = new DocumentVectorShapeEditor(DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Rectangle));
        var path = editor.Shape.Paths[0]; var nodes = JsonSerializer.Serialize(path.Subpaths);
        editor.SetPathStyle(path.Id, "#FF0000", "#0000FF", 3);
        Assert.Equal("#FF0000", editor.Shape.Paths[0].Fill.Color); Assert.Equal("#0000FF", editor.Shape.Paths[0].Stroke.Color);
        Assert.Equal(3, editor.Shape.Paths[0].Stroke.Width); Assert.Equal(nodes, JsonSerializer.Serialize(editor.Shape.Paths[0].Subpaths));
        Assert.True(editor.Undo()); Assert.True(editor.Redo());
    }
}
