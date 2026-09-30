using System.Text.Json;
using Haven.Core;
using HavenOS.Home.Core;
using Xunit;
namespace HavenOS.Home.Tests;

public sealed class HomeProductivityStyleTransferTests
{
    [Fact]
    public void Copy_keeps_only_referenced_definition_closure_and_document_local_identity()
    {
        var document = new NotesDocument { Styles = [new(), new() { Id = "custom", Name = "Custom", BasedOn = "normal" }, new() { Id = "unused" }] };
        var block = NotesBlock.Heading("Rich"); block.StyleId = "custom";
        var value = HomeNotesSharedObjects.Project(block);
        var context = new HomeProductivityContext("write", document.Id.ToString("D"), 1, [block.Id], new HashSet<string> { "text.heading" });
        var engine = new HomeProductivityEngine(); var fileId = Guid.NewGuid(); var filesRevision = Guid.NewGuid();
        foreach (var style in HomeNotesStyleProjection.Project(document, context, fileId, filesRevision, "imported-owner-token")) engine.RegisterStyle(style);
        var bundle = engine.SerializeSelection(context, [value]);
        Assert.Equal(new[] { "custom", "normal" }, bundle.Styles.Select(style => style.StyleId));
        Assert.All(bundle.Styles, style => { Assert.Equal(fileId, style.Source!.FileId); Assert.Equal(filesRevision, style.Source.FilesRevisionId); Assert.Equal("imported-owner-token", style.Source.OwningRevision); });
        Assert.Equal("normal", bundle.Styles[0].Properties.GetProperty("BasedOn").GetString());
        document.Styles[1].Character.Bold = true;
        Assert.False(bundle.Styles[0].Properties.GetProperty("Character").GetProperty("Bold").GetBoolean());
        Assert.True(engine.CanPaste(bundle, context, out _));
        var pasted = Assert.Single(engine.Paste(bundle, context));
        Assert.Equal("custom", HomeNotesSharedObjects.Read(pasted).StyleId);
        Assert.NotEqual(block.Id, pasted.ObjectId);

        var other = new NotesDocument { Styles = document.Styles };
        var target = context with { ArtifactId = other.Id.ToString("D") };
        Assert.False(engine.CanPaste(bundle, target, out var code)); Assert.Equal("StyleImportRequired", code);
        foreach (var style in HomeNotesStyleProjection.Project(other, target, Guid.NewGuid(), Guid.NewGuid(), "another-owner-token")) engine.RegisterStyle(style);
        Assert.Null(engine.GetStyle("normal")); // no arbitrary document's local style becomes a global definition
        Assert.False(engine.CanPaste(bundle, target, out code)); Assert.Equal("StyleDefinitionConflict", code);
        Assert.False(engine.CanPaste(bundle with { Styles = [] }, context, out code)); Assert.Equal("RequiredStyleMissing", code);
    }

    [Fact]
    public void Missing_or_cyclic_named_style_cannot_be_silently_dropped_from_copy()
    {
        var block = NotesBlock.Heading(); var value = HomeNotesSharedObjects.Project(block);
        var context = new HomeProductivityContext("write", Guid.NewGuid().ToString("D"), 1, [block.Id], new HashSet<string> { "text.heading" });
        var engine = new HomeProductivityEngine();
        Assert.Throws<InvalidDataException>(() => engine.SerializeSelection(context, [value]));
        engine.RegisterStyle(new("heading-1", 1, "Heading", HomeProductivityEngine.StyleScope(context), JsonSerializer.SerializeToElement(new { })) { BasedOnStyleIds = ["base"] });
        engine.RegisterStyle(new("base", 1, "Base", HomeProductivityEngine.StyleScope(context), JsonSerializer.SerializeToElement(new { })) { BasedOnStyleIds = ["heading-1"] });
        Assert.Throws<InvalidDataException>(() => engine.SerializeSelection(context, [value]));
        var cyclic = new HomeProductivityObjectBundle(1, [value], engine.ListStyles(), [],
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }));
        Assert.False(engine.CanPaste(cyclic, context, out var code));
        Assert.Equal("StyleDependencyCycle", code);
    }
}
