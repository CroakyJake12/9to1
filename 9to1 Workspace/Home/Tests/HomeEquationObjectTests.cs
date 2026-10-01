using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Core;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeEquationObjectTests
{
    [Fact]
    public void Canonical_equation_projection_and_source_edit_retain_ids_unknowns_and_noncache_fields()
    {
        var block = NotesBlock.EquationBlock();
        block.Equation!.Source = @"\frac{x^2}{\sqrt{y}}";
        block.Equation.AccessibleAlternative = "x squared divided by square root of y";
        block.Equation.Numbered = true; block.Equation.Number = 4; block.Equation.Label = "energy";
        block.Equation.RenderedText = "stale cached preview";
        block.Equation.Macros["\\energy"] = "E";
        block.Equation.References.Add("existing-equation-label");
        var payload = JsonSerializer.SerializeToNode(block)!.AsObject();
        payload["UnrecognisedBlock"] = "retained"; payload["Equation"]!["UnrecognisedEquation"] = 42;
        var handler = new HomeEquationObjectHandler();
        var value = handler.Create(block.Id, JsonSerializer.SerializeToElement(payload));
        var before = value.Content.GetRawText();
        var rendered = handler.Render(value);
        Assert.Equal(block.Id, Assert.Single(rendered.EquationBindings).ObjectId);
        Assert.Equal(before, value.Content.GetRawText());
        Assert.Contains("content.Equation.UnrecognisedEquation", rendered.RetainedUnsupportedProperties);
        var updated = handler.Transform(value, new("equation.source", 1, "math.equation", [block.Id],
            JsonSerializer.SerializeToElement(new { source = @"\frac{a}{b}", alternative = "a divided by b" }), 1));
        var actual = HomeEquationObjectHandler.ReadCanonical(updated.Content, block.Id);
        Assert.Equal(block.Id, updated.ObjectId); Assert.Equal(@"\frac{a}{b}", actual.Source);
        Assert.Equal(string.Empty, actual.RenderedText);
        Assert.Equal("energy", actual.Label); Assert.Equal(4, actual.Number);
        Assert.Equal("E", actual.Macros["\\energy"]);
        Assert.Equal("retained", updated.Content.GetProperty("UnrecognisedBlock").GetString());
        Assert.Equal(42, updated.Content.GetProperty("Equation").GetProperty("UnrecognisedEquation").GetInt32());
        Assert.Equal(before, value.Content.GetRawText());
    }
    [Fact]
    public void Nonempty_visual_graph_cannot_be_silently_discarded_by_source_edit_or_generic_clone()
    {
        var block = NotesBlock.EquationBlock(); block.Equation!.VisualStructureJson = "{\"existingNode\":42}";
        var handler = new HomeEquationObjectHandler(); var value = HomeEquationObjectHandler.Project(block);
        Assert.Throws<NotSupportedException>(() => handler.Transform(value, new("equation.source", 1, "math.equation", [block.Id],
            JsonSerializer.SerializeToElement(new { source = "x", alternative = "x" }), 1)));
        Assert.Throws<NotSupportedException>(() => handler.CloneForPaste(value, Guid.NewGuid()));
        Assert.Equal(block.Equation.VisualStructureJson, HomeEquationObjectHandler.ReadCanonical(value.Content, block.Id).VisualStructureJson);
    }
    [Fact]
    public void Canonical_identity_source_and_bounds_are_required_without_deserializing_new_ids()
    {
        var block = NotesBlock.EquationBlock(); var handler = new HomeEquationObjectHandler();
        var content = JsonSerializer.SerializeToNode(block)!.AsObject(); content.Remove("Id");
        Assert.Throws<InvalidDataException>(() => handler.Create(block.Id, JsonSerializer.SerializeToElement(content)));
        Assert.Throws<InvalidDataException>(() => handler.Create(Guid.NewGuid(), JsonSerializer.SerializeToElement(block)));
        block.Equation!.Source = new string('x', 32769);
        Assert.Throws<InvalidDataException>(() => HomeEquationObjectHandler.Project(block));
    }
}
