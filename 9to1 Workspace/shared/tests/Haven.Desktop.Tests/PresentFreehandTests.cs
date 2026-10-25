using System.Text.Json;
using System.Text.Json.Nodes;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Present;
using Haven.Infrastructure;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed class PresentFreehandTests
{
    [Fact]
    public void Prepared_stroke_keeps_fresh_identity_precise_geometry_and_one_existing_undo_transaction()
    {
        var document = PresentDocument.Create("Freehand model"); var editor = new PresentEditor(document);
        var slideId = editor.Selection.SlideId; var initialIds = editor.SelectedSlide.Elements.Select(value => value.Id).ToArray();
        var bundle = Bundle(document, slideId); var engine = Engine(); var id = engine.ParseBundle(bundle.GetRawText()).Objects[0].ObjectId;
        var result = editor.TryInsertPreparedSharedObject(document.Id, document.Version, slideId, bundle,
            new CanvasPresentInkBridge(engine, document.Id, document.Version), -.001, .12, .004, .003);
        Assert.True(result.Inserted); Assert.Equal(id, result.ElementId);
        var element = Assert.Single(editor.SelectedSlide.Elements, value => value.Id == id);
        Assert.Equal(-.001, element.X); Assert.Equal(.004, element.Width); Assert.Equal(.003, element.Height);
        Assert.True(JsonElement.DeepEquals(bundle, element.SharedProductivityBundle!.Value));
        Assert.Equal(id, Assert.Single(editor.Selection.ElementIds));
        Assert.True(editor.Undo()); Assert.Equal(initialIds, editor.SelectedSlide.Elements.Select(value => value.Id));
        Assert.False(editor.CanUndo); Assert.True(editor.CanRedo);
        Assert.True(editor.Redo()); var restored = Assert.Single(editor.SelectedSlide.Elements, value => value.Id == id);
        Assert.Equal(.004, restored.Width); Assert.True(JsonElement.DeepEquals(bundle, restored.SharedProductivityBundle!.Value));
    }

    [Fact]
    public void Stale_revision_selected_slide_and_duplicate_identity_do_not_allocate_history()
    {
        var document = PresentDocument.Create(); document.Slides.Add(PresentSlide.Create(1)); document.Normalize();
        var editor = new PresentEditor(document); var slide = editor.Selection.SlideId; var bundle = Bundle(document, slide);
        var bridge = new CanvasPresentInkBridge(Engine(), document.Id, document.Version);
        var stale = editor.TryInsertPreparedSharedObject(document.Id, document.Version + 1, slide, bundle, bridge, .1, .1, .1, .1);
        Assert.False(stale.Inserted); Assert.Equal("RevisionConflict", stale.Code); Assert.False(editor.CanUndo);
        Assert.True(editor.SelectSlide(document.Slides[1].Id));
        var wrongSlide = editor.TryInsertPreparedSharedObject(document.Id, document.Version, slide, bundle, bridge, .1, .1, .1, .1);
        Assert.False(wrongSlide.Inserted); Assert.Equal("RevisionConflict", wrongSlide.Code); Assert.False(editor.CanUndo);
        Assert.True(editor.SelectSlide(slide));
        Assert.True(editor.TryInsertPreparedSharedObject(document.Id, document.Version, slide, bundle, bridge, .1, .1, .1, .1).Inserted);
        var repeated = editor.TryInsertPreparedSharedObject(document.Id, document.Version, slide, bundle, bridge, .1, .1, .1, .1);
        Assert.False(repeated.Inserted); Assert.Equal("ObjectIdentityMismatch", repeated.Code);
        Assert.True(editor.Undo()); Assert.False(editor.CanUndo);
    }

    [Fact]
    public void Invalid_geometry_and_a_bridge_selection_change_are_rejected_before_editor_mutation()
    {
        var document = PresentDocument.Create(); document.Slides.Add(PresentSlide.Create(1)); document.Normalize();
        var editor = new PresentEditor(document); var slide = editor.Selection.SlideId; var bundle = Bundle(document, slide);
        var actual = new CanvasPresentInkBridge(Engine(), document.Id, document.Version);
        var invalid = editor.TryInsertPreparedSharedObject(document.Id, document.Version, slide, bundle, actual, .1, .1, double.NaN, .1);
        Assert.Equal("InvalidGeometry", invalid.Code); Assert.False(editor.CanUndo);
        var bridge = new SelectionChangingBridge(actual, () => editor.SelectSlide(document.Slides[1].Id));
        var conflict = editor.TryInsertPreparedSharedObject(document.Id, document.Version, slide, bundle, bridge, .1, .1, .1, .1);
        Assert.False(conflict.Inserted); Assert.Equal("RevisionConflict", conflict.Code); Assert.False(editor.CanUndo);
        Assert.DoesNotContain(document.Slides.SelectMany(value => value.Elements), value => value.Kind == PresentElementKind.SharedObject);
    }

    [Fact]
    public void Present_logical_dimensions_support_portrait_without_changing_default_Data_Write_capture()
    {
        var engine = Engine(); var document = PresentDocument.Create(); var layer = document.Slides[0].Id;
        var context = Context(document); RnotePointerSample[] samples = [new(20, 700, .2), new(30, 720, .8)];
        Assert.Throws<InvalidDataException>(() => CanvasSharedInkStrokeFactory.Capture(engine, context, layer, samples));
        var bundle = CanvasSharedInkStrokeFactory.Capture(engine, context, layer, samples, null, 960, 1706);
        var owner = new CanvasSharedInkOwnerSnapshot(context, layer, "same", [bundle]) { LogicalWidth = 960, LogicalHeight = 1706 };
        Assert.Equal(1706, CanvasSharedInkStrokeFactory.Snapshot(engine, owner).LogicalHeight);
        Assert.False(CanvasSharedInkStrokeFactory.SameOwner(owner, owner with { LogicalHeight = 540 }));
        Assert.Throws<InvalidDataException>(() => CanvasSharedInkStrokeFactory.Snapshot(engine, owner with { LogicalWidth = double.PositiveInfinity }));
    }

    [Fact]
    public async Task Original_presentation_repository_reopens_the_same_stroke_geometry_notes_and_slide_identity()
    {
        using var paths = new PhysicalPaths(); var repository = new PresentRepository(paths);
        var document = PresentDocument.Create("Durable drawing"); document.SlideSize = PresentSlideSize.Portrait();
        var editor = new PresentEditor(document); var slide = editor.Selection.SlideId;
        editor.SelectedSlide.SpeakerNotes = "Keep genuine notes";
        var bundle = Bundle(document, slide); var id = Engine().ParseBundle(bundle.GetRawText()).Objects[0].ObjectId;
        Assert.True(editor.TryInsertPreparedSharedObject(document.Id, document.Version, slide, bundle,
            new CanvasPresentInkBridge(Engine(), document.Id, document.Version), .3, -.001, .002, .1).Inserted);
        await repository.SaveAsync(editor.Document, "Freehand owner persistence smoke", TestContext.Current.CancellationToken);
        var reopened = Assert.IsType<PresentDocument>(await repository.LoadAsync(document.Id, TestContext.Current.CancellationToken));
        Assert.Equal(document.Id, reopened.Id); Assert.Equal(slide, reopened.Slides[0].Id);
        Assert.Equal("Keep genuine notes", reopened.Slides[0].SpeakerNotes);
        var stroke = Assert.Single(reopened.Slides[0].Elements, value => value.Id == id);
        Assert.Equal(-.001, stroke.Y); Assert.Equal(.002, stroke.Width);
        Assert.True(JsonElement.DeepEquals(bundle, stroke.SharedProductivityBundle!.Value));
        Assert.Equal(PresentSlideSizePreset.Portrait9By16, reopened.SlideSize.Preset);
    }

    [AvaloniaFact]
    public async Task Missing_real_shared_handler_keeps_the_actual_Present_pen_unavailable_and_existing_document_intact()
    {
        using var paths = new PhysicalPaths(); using var bus = new HavenEventBus();
        var repository = new PresentRepository(paths);
        var page = new PresentPage(bus, repository, new PresentPptxExportService(), null, null, new HomeProductivityEngine());
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var input = Assert.IsType<CanvasSharedInkInput>(page.FreehandInput); Assert.False(input.CanDraw);
        Assert.IsType<NotSupportedException>(input.LastError);
        var commands = Assert.IsAssignableFrom<ICuiActionDispatcher>(page.FreehandCommands);
        var availability = Assert.IsAssignableFrom<ICuiActionAvailability>(page.FreehandCommands);
        var before = JsonSerializer.Serialize(page.Document);
        foreach (var command in new[] { "present.ink.pen", "present.ink.highlighter", "present.ink.color.blue", "present.ink.width.broad" })
        {
            Assert.False(availability.IsActionAvailable(command));
            await commands.DispatchAsync(command, null, TestContext.Current.CancellationToken);
        }
        Assert.Equal(before, JsonSerializer.Serialize(page.Document));
        Assert.DoesNotContain(page.Document!.Slides.SelectMany(value => value.Elements), value => value.Kind == PresentElementKind.SharedObject);
        Assert.True(await page.PrepareToCloseAsync(cancellationToken: TestContext.Current.CancellationToken));
        await page.CloseAndDrainAsync();
        Assert.Empty(input.OriginalSubmissions);
    }

    [Fact]
    public async Task Ink_tool_choices_capture_real_shared_style_and_persist_without_restyling_earlier_strokes()
    {
        using var paths = new PhysicalPaths(); var repository = new PresentRepository(paths);
        var document = PresentDocument.Create("Coloured revision annotations"); var editor = new PresentEditor(document);
        var engine = Engine(); var slide = editor.Selection.SlideId; var context = Context(document);
        var original = CanvasSharedInkStrokeFactory.Capture(engine, context, slide, [new(10, 20, .3), new(30, 40, .8)]);
        var originalText = original.GetRawText();
        var broad = Assert.IsType<CanvasRnoteInkStyle>(PresentPage.ResolveFreehandStyleCommand(CanvasRnoteInkStyle.Default, "present.ink.width.broad"));
        var blue = Assert.IsType<string>(PresentPage.FreehandColorForCommand("present.ink.color.blue"));
        broad = broad with { Color = blue };
        var coloured = CanvasSharedInkStrokeFactory.Capture(engine, context, slide, [new(40, 40, .4), new(60, 60, .9)], broad);
        var brush = engine.ParseBundle(coloured.GetRawText()).Objects.Single().Content.GetProperty("ResolvedBrushProperties");
        Assert.Equal(6, brush.GetProperty("BaseWidth").GetDouble()); Assert.Equal(blue, brush.GetProperty("Color").GetString());
        Assert.Equal(1, brush.GetProperty("Opacity").GetDouble()); Assert.Equal(originalText, original.GetRawText());
        var marker = Assert.IsType<CanvasRnoteInkStyle>(PresentPage.ResolveFreehandStyleCommand(broad, "present.ink.highlighter"));
        marker = Assert.IsType<CanvasRnoteInkStyle>(PresentPage.ResolveFreehandStyleCommand(marker, "present.ink.width.fine"));
        var highlighted = CanvasSharedInkStrokeFactory.Capture(engine, context, slide, [new(70, 70, .5), new(100, 70, .5)], marker);
        var highlightObject = engine.ParseBundle(highlighted.GetRawText()).Objects.Single();
        Assert.Equal("highlighter", highlightObject.Content.GetProperty("ToolDefinitionId").GetString());
        Assert.Equal(8, highlightObject.Content.GetProperty("ResolvedBrushProperties").GetProperty("BaseWidth").GetDouble());
        Assert.Equal(.4, highlightObject.Content.GetProperty("ResolvedBrushProperties").GetProperty("Opacity").GetDouble());
        foreach (var bundle in new[] { original, coloured, highlighted })
            Assert.True(editor.TryInsertPreparedSharedObject(document.Id, document.Version, slide, bundle,
                new CanvasPresentInkBridge(engine, document.Id, document.Version), .1, .2, .3, .2).Inserted);
        await repository.SaveAsync(editor.Document, "Actual shared ink styles", TestContext.Current.CancellationToken);
        var reopened = Assert.IsType<PresentDocument>(await repository.LoadAsync(document.Id, TestContext.Current.CancellationToken));
        foreach (var bundle in new[] { original, coloured, highlighted })
        {
            var id = engine.ParseBundle(bundle.GetRawText()).Objects.Single().ObjectId;
            Assert.True(JsonElement.DeepEquals(bundle, reopened.Slides.Single(sl => sl.Id == slide).Elements.Single(element => element.Id == id).SharedProductivityBundle!.Value));
        }
    }

    [Fact]
    public async Task Canonical_colour_action_preserves_geometry_identity_unknown_fields_and_original_undo_save()
    {
        using var paths = new PhysicalPaths(); var repository = new PresentRepository(paths);
        var document = PresentDocument.Create("Recolour one annotation"); var editor = new PresentEditor(document);
        var engine = Engine(); var slide = editor.Selection.SlideId;
        var source = JsonNode.Parse(Bundle(document, slide).GetRawText())!.AsObject();
        source["objects"]![0]!["futureOptionalObject"] = "keep";
        source["objects"]![0]!["content"]!["futureOptionalStroke"] = new JsonObject { ["value"] = 42 };
        using var extended = JsonDocument.Parse(source.ToJsonString()); var original = extended.RootElement.Clone();
        var bridge = new CanvasPresentInkBridge(engine, document.Id, document.Version);
        var id = engine.ParseBundle(original.GetRawText()).Objects.Single().ObjectId;
        Assert.True(editor.TryInsertPreparedSharedObject(document.Id, document.Version, slide, original, bridge, .2, .3, .25, .15).Inserted);
        var action = new HomeProductivityAction("ink.color", 1, "drawing.ink", [id], JsonSerializer.SerializeToElement(new { color = "#FFDC2626" }), document.Version);
        var recoloured = bridge.PrepareAction(original, action);
        Assert.True(editor.TryReplaceSharedObject(document.Id, document.Version, id, original, recoloured.Bundle, bridge).Changed);
        var target = editor.SelectedSlide.Elements.Single(element => element.Id == id);
        Assert.Equal((.2, .3, .25, .15), (target.X, target.Y, target.Width, target.Height));
        Assert.Equal(id, editor.Selection.ElementIds.Single());
        var current = engine.ParseBundle(target.SharedProductivityBundle!.Value.GetRawText()).Objects.Single();
        Assert.Equal(id, current.ObjectId); Assert.Equal("#FFDC2626", current.Content.GetProperty("ResolvedBrushProperties").GetProperty("Color").GetString());
        Assert.Equal(42, current.Content.GetProperty("futureOptionalStroke").GetProperty("value").GetInt32());
        Assert.Equal("keep", target.SharedProductivityBundle!.Value.GetProperty("objects")[0].GetProperty("futureOptionalObject").GetString());
        Assert.True(editor.Undo()); Assert.True(JsonElement.DeepEquals(original, editor.SelectedSlide.Elements.Single(element => element.Id == id).SharedProductivityBundle!.Value));
        Assert.True(editor.Redo()); var finalBundle = editor.SelectedSlide.Elements.Single(element => element.Id == id).SharedProductivityBundle!.Value.Clone();
        var undoBefore = editor.CanUndo;
        var stale = editor.TryReplaceSharedObject(editor.Document.Id, editor.Document.Version, id, original, recoloured.Bundle,
            new CanvasPresentInkBridge(engine, editor.Document.Id, editor.Document.Version));
        Assert.False(stale.Changed); Assert.Equal("PayloadConflict", stale.Code); Assert.Equal(undoBefore, editor.CanUndo);
        await repository.SaveAsync(editor.Document, "Actual recoloured shared annotation", TestContext.Current.CancellationToken);
        var reopened = Assert.IsType<PresentDocument>(await repository.LoadAsync(document.Id, TestContext.Current.CancellationToken));
        var restored = reopened.Slides.Single(sl => sl.Id == slide).Elements.Single(element => element.Id == id);
        Assert.True(JsonElement.DeepEquals(finalBundle, restored.SharedProductivityBundle!.Value));
        Assert.Equal((.2, .3, .25, .15), (restored.X, restored.Y, restored.Width, restored.Height));
    }

    private static HomeProductivityEngine Engine() => new(handlers: [new CanvasInkObjectHandler()]);
    private static HomeProductivityContext Context(PresentDocument document) => new("present", document.Id.ToString("D"), document.Version, [], new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
    private static JsonElement Bundle(PresentDocument document, Guid layer) => CanvasSharedInkStrokeFactory.Capture(Engine(), Context(document), layer, [new(10, 20, .5), new(40, 60, .8)]);
    // Real Home capture with a hostile reentrant selection change; never a native-ready substitute.
    private sealed class SelectionChangingBridge(IPresentSharedObjectBridge actual, Action change) : IPresentSharedObjectBridge
    {
        public PresentSharedObjectCapture Capture(JsonElement bundle) { var captured = actual.Capture(bundle); change(); return captured; }
        public PresentSharedObjectCapture CloneForPaste(JsonElement bundle) => actual.CloneForPaste(bundle);
        public PresentSharedObjectSupport GetSupport(JsonElement bundle) => actual.GetSupport(bundle);
    }
    private sealed class PhysicalPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "present-freehand-smoke-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "Browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
