using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeVectorShapeObjectTests
{
    [Fact]
    public void Canonical_shape_projects_same_ids_and_pure_fill_retains_unknown_fields_and_geometry()
    {
        var shape = DocumentVectorShapes.CreateEditableStarter();
        var original = JsonSerializer.SerializeToNode(shape)!;
        original["Paths"]![0]!["futurePaint"] = new JsonObject { ["ownerValue"] = 42 };
        var content = JsonSerializer.SerializeToElement(original);
        var engine = new HomeProductivityEngine(); var handler = new HomeVectorShapeObjectHandler();
        var value = engine.CreateObject("drawing.vector", shape.Id, content);
        var rendered = engine.RenderObject(value);
        var bound = Assert.Single(rendered.VectorBindings);
        Assert.Equal(shape.Id, bound.ObjectId);
        Assert.Equal(content.GetRawText(), bound.CanonicalShape.GetRawText());
        Assert.Contains("Content.Paths[0].futurePaint", rendered.RetainedUnsupportedProperties);
        var updated = handler.Transform(value, new("vector.fill", 1, "drawing.vector", [shape.Id],
            JsonSerializer.SerializeToElement(new { pathId = shape.Paths[0].Id, color = "#FF00FF00" }), 1));
        Assert.Equal(shape.Id, updated.ObjectId);
        var next = updated.Content.GetProperty("Paths")[0];
        Assert.Equal(shape.Paths[0].Id, next.GetProperty("Id").GetGuid());
        Assert.Equal("#FF00FF00", next.GetProperty("Fill").GetProperty("Color").GetString());
        Assert.Equal(42, next.GetProperty("futurePaint").GetProperty("ownerValue").GetInt32());
        Assert.Equal(content.GetProperty("Paths")[0].GetProperty("Subpaths").GetRawText(), next.GetProperty("Subpaths").GetRawText());
        Assert.Throws<InvalidDataException>(() => handler.CloneForPaste(value, Guid.NewGuid()));
    }

    [Fact]
    public void Insertion_uses_canonical_id_mapping_without_normalizing_retained_geometry_or_names()
    {
        var shape = DocumentVectorShapes.CreateEditableStarter(); shape.Name = "  Keep exact name  ";
        shape.Transform.RotationDegrees = 725; shape.ClippingPathId = shape.Paths[0].Id;
        var handler = new HomeVectorShapeObjectHandler();
        var source = handler.Create(shape.Id, JsonSerializer.SerializeToElement(shape));
        var newId = Guid.NewGuid(); var cloned = handler.CloneForPaste(source, newId);
        var restored = HomeVectorShapeObjectHandler.ReadCanonical(cloned.Content, newId);
        Assert.Equal(newId, restored.Id); Assert.Equal(shape.Name, restored.Name);
        Assert.Equal(725, restored.Transform.RotationDegrees);
        Assert.NotEqual(shape.Paths[0].Id, restored.Paths[0].Id);
        Assert.Equal(restored.Paths[0].Id, restored.ClippingPathId);
        Assert.NotEqual(shape.Paths[0].Subpaths[0].Nodes[0].Id, restored.Paths[0].Subpaths[0].Nodes[0].Id);
        Assert.Equal(shape.Paths[0].Subpaths[0].Nodes[0].X, restored.Paths[0].Subpaths[0].Nodes[0].X);
        Assert.Throws<NotSupportedException>(() => handler.Render(cloned));
    }

    [Fact]
    public void Missing_nested_identity_foreign_root_unknown_enum_and_nonfinite_geometry_are_not_repaired()
    {
        var shape = DocumentVectorShapes.CreateEditableStarter(); var handler = new HomeVectorShapeObjectHandler();
        var content = JsonSerializer.SerializeToElement(shape);
        Assert.Throws<InvalidDataException>(() => handler.Create(Guid.NewGuid(), content));
        var missing = JsonSerializer.SerializeToNode(shape)!;
        missing["Paths"]![0]!["Subpaths"]![0]!["Nodes"]![0]!.AsObject().Remove("Id");
        Assert.Throws<InvalidDataException>(() => handler.Create(shape.Id, JsonSerializer.SerializeToElement(missing)));
        var unknown = JsonSerializer.SerializeToNode(shape)!; unknown["Paths"]![0]!["FillRule"] = 99;
        Assert.Throws<InvalidDataException>(() => handler.Create(shape.Id, JsonSerializer.SerializeToElement(unknown)));
        var invalid = JsonSerializer.SerializeToNode(shape)!; invalid["Transform"]!["ScaleX"] = 0;
        Assert.Throws<InvalidDataException>(() => handler.Create(shape.Id, JsonSerializer.SerializeToElement(invalid)));
    }
}
