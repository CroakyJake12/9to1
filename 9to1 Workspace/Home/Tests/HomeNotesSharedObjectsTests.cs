using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Core;
using HavenOS.Home.Core;
using Xunit;
namespace HavenOS.Home.Tests;
public sealed class HomeNotesSharedObjectsTests
{
    [Fact]
    public void Rich_paragraph_version_coexists_with_legacy_without_flattening_or_implicit_upgrade()
    {
        var engine = new HomeProductivityEngine();
        var legacy = engine.CreateObject("text.paragraph", Guid.NewGuid(), JsonSerializer.SerializeToElement(new { text = "Legacy" }));
        Assert.Equal(1, legacy.SchemaVersion);
        Assert.Contains("Legacy", engine.RenderObject(legacy).CuiSource);
        var block = new NotesBlock { Kind = NotesBlockKind.Paragraph, Runs = [new() { Text = "Rich", Italic = true }, new() { Text = " second", Bold = true }] };
        var rich = HomeNotesSharedObjects.Project(block);
        Assert.Equal(2, rich.SchemaVersion);
        Assert.Equal(block.Id, rich.ObjectId);
        Assert.Equal(block.Runs[0].Id, HomeNotesSharedObjects.Read(rich).Runs[0].Id);
        var rendered = Assert.Single(engine.RenderObject(rich).NotesBindings);
        Assert.Equal(2, rendered.CanonicalBlock.GetProperty("Runs").GetArrayLength());
        Assert.True(rendered.CanonicalBlock.GetProperty("Runs")[0].GetProperty("Italic").GetBoolean());
        Assert.Equal(1, engine.Convert(legacy, "text.paragraph", JsonSerializer.SerializeToElement(new { })).SchemaVersion);
        Assert.Equal(2, engine.CreateObject("text.paragraph", block.Id, rich.Content, schemaVersion: 2).SchemaVersion);
        Assert.Throws<InvalidDataException>(() => engine.CreateObject("text.paragraph", block.Id, rich.Content));
        var context = new HomeProductivityContext("write", "doc", 1, [legacy.ObjectId, rich.ObjectId], new HashSet<string> { "text.paragraph" });
        var style = new NotesNamedStyle();
        engine.RegisterStyle(new("normal", 1, style.Name, HomeProductivityEngine.StyleScope(context), JsonSerializer.SerializeToElement(style)));
        var pasted = engine.Paste(engine.SerializeSelection(context, [legacy, rich]), context);
        Assert.Equal(1, pasted[0].SchemaVersion);
        Assert.Equal(2, pasted[1].SchemaVersion);
        Assert.NotEqual(rich.ObjectId, pasted[1].ObjectId);
        var cloned = HomeNotesSharedObjects.Read(pasted[1]);
        Assert.Equal(pasted[1].ObjectId, cloned.Id);
        Assert.NotEqual(block.Runs[0].Id, cloned.Runs[0].Id);
        Assert.True(cloned.Runs[0].Italic);
    }

    [Fact]
    public void Heading_keeps_canonical_ID_rich_runs_unknown_fields_and_typed_formatting()
    {
        var block = NotesBlock.Heading("fallback"); block.Runs = [new() { Text = "Rich", Italic = true }];
        var item = HomeNotesSharedObjects.Project(block);
        var content = JsonNode.Parse(item.Content.GetRawText())!.AsObject(); content["FutureRequiredByOwner"] = "retained";
        item = item with { Content = JsonSerializer.SerializeToElement(content) };
        var handler = new HomeNotesObjectHandler("text.heading");
        var changed = handler.Transform(item, new("text.run.bold", 1, item.ObjectType, [item.ObjectId],
            JsonSerializer.SerializeToElement(new { runId = block.Runs[0].Id, value = true }), 1));
        var read = HomeNotesSharedObjects.Read(changed);
        Assert.Equal(block.Id, read.Id); Assert.True(read.Runs[0].Bold); Assert.True(read.Runs[0].Italic);
        Assert.Equal("retained", changed.Content.GetProperty("FutureRequiredByOwner").GetString());
        var binding = Assert.Single(handler.Render(changed).NotesBindings);
        Assert.Equal(block.Id, binding.ObjectId); Assert.True(binding.CanonicalBlock.GetProperty("Runs")[0].GetProperty("Bold").GetBoolean());
    }
    [Fact]
    public void Table_and_checklist_edit_canonical_children_and_paste_remaps_nested_IDs_together()
    {
        var table = NotesBlock.TableBlock(2, 2); var value = HomeNotesSharedObjects.Project(table);
        var handler = new HomeNotesObjectHandler("table");
        var changed = handler.Transform(value, new("table.cell.text", 1, "table", [value.ObjectId],
            JsonSerializer.SerializeToElement(new { cellId = table.Table!.Rows[1].Cells[1].Id, text = "edited" }), 1));
        Assert.Equal("edited", HomeNotesSharedObjects.Read(changed).Table!.Rows[1].Cells[1].Text);
        var clone = handler.CloneForPaste(changed, Guid.NewGuid()); var read = HomeNotesSharedObjects.Read(clone);
        Assert.NotEqual(table.Id, read.Id); Assert.NotEqual(table.Table!.Rows[0].Id, read.Table!.Rows[0].Id);
        Assert.NotEqual(table.Table.Rows[1].Cells[1].Id, read.Table.Rows[1].Cells[1].Id);
        var list = new NotesBlock { Kind = NotesBlockKind.List, List = new() { Kind = NotesListKind.Checklist, Items = [new() { Text = "Task" }] } };
        value = HomeNotesSharedObjects.Project(list); handler = new("text.checklist");
        changed = handler.Transform(value, new("checklist.check", 1, "text.checklist", [value.ObjectId],
            JsonSerializer.SerializeToElement(new { itemId = list.List.Items[0].Id, @checked = true }), 1));
        Assert.True(HomeNotesSharedObjects.Read(changed).List!.Items[0].Checked);
    }
    [Fact]
    public void Engine_exposes_all_five_existing_Notes_families_and_rejects_identity_disagreement()
    {
        var engine = new HomeProductivityEngine();
        Assert.True(engine.GetCompatibility("write", "1", ["text.heading", "text.list", "text.checklist", "code.block", "table"]).Compatible);
        var heading = HomeNotesSharedObjects.Project(NotesBlock.Heading());
        Assert.Throws<InvalidDataException>(() => engine.CreateObject(heading.ObjectType, Guid.NewGuid(), heading.Content));
        var malformed = JsonNode.Parse(heading.Content.GetRawText())!.AsObject();
        malformed["Runs"] = new JsonArray(new JsonObject { ["Text"] = "No stable ID" });
        Assert.Throws<InvalidDataException>(() => engine.CreateObject(heading.ObjectType, heading.ObjectId, JsonSerializer.SerializeToElement(malformed)));
    }
}
