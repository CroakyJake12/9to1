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
        var element = Assert.Single(editor.SelectedSlide.Elements.Where(value => value.Id == id));
        Assert.Equal(-.001, element.X); Assert.Equal(.004, element.Width); Assert.Equal(.003, element.Height);
        Assert.True(JsonElement.DeepEquals(bundle, element.SharedProductivityBundle!.Value));
        Assert.Equal(id, Assert.Single(editor.Selection.ElementIds));
        Assert.True(editor.Undo()); Assert.Equal(initialIds, editor.SelectedSlide.Elements.Select(value => value.Id));
        Assert.False(editor.CanUndo); Assert.True(editor.CanRedo);
        Assert.True(editor.Redo()); var restored = Assert.Single(editor.SelectedSlide.Elements.Where(value => value.Id == id));
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
    public void Whole_stroke_erase_refuses_transformed_placements_and_foreign_layers_without_rebinding_source_ink()
    {
        var document = PresentDocument.Create("Ink placement guard");
        var editor = new PresentEditor(document); var slide = editor.SelectedSlide; var engine = Engine(); var context = Context(document);
        const double logicalWidth = 960, logicalHeight = 540;
        var bundle = CanvasSharedInkStrokeFactory.Capture(engine, context, slide.Id,
            [new(40, 40, .4), new(160, 40, .8)]);
        var id = engine.ParseBundle(bundle.GetRawText()).Objects.Single().ObjectId;
        var scene = CanvasSharedInkStrokeFactory.RenderScene(engine, context with { SelectedObjectIds = [id] }, slide.Id, [bundle]);
        Assert.True(editor.TryInsertPreparedSharedObject(document.Id, document.Version, slide.Id, bundle,
            new CanvasPresentInkBridge(engine, document.Id, document.Version), scene.X / logicalWidth, scene.Y / logicalHeight,
            scene.Width / logicalWidth, scene.Height / logicalHeight).Inserted);
        var element = Assert.Single(editor.SelectedSlide.Elements.Where(value => value.Id == id));
        var originalPayload = element.SharedProductivityBundle!.Value.Clone();
        Assert.Null(PresentPage.ResolveFreehandErasePlacementBlocker(element, slide.Id, slide.Id, scene, logicalWidth, logicalHeight));

        Assert.True(editor.MoveSelection(.1, 0));
        element = editor.SelectedSlide.Elements.Single(value => value.Id == id);
        Assert.Contains("moved or resized", PresentPage.ResolveFreehandErasePlacementBlocker(
            element, slide.Id, slide.Id, scene, logicalWidth, logicalHeight) ?? string.Empty, StringComparison.Ordinal);
        Assert.True(editor.Undo());
        Assert.True(editor.ResizeSelection(.1, 0));
        element = editor.SelectedSlide.Elements.Single(value => value.Id == id);
        Assert.Contains("moved or resized", PresentPage.ResolveFreehandErasePlacementBlocker(
            element, slide.Id, slide.Id, scene, logicalWidth, logicalHeight) ?? string.Empty, StringComparison.Ordinal);
        Assert.True(editor.Undo());
        Assert.True(editor.RotateSelection(45));
        element = editor.SelectedSlide.Elements.Single(value => value.Id == id);
        Assert.Contains("rotated", PresentPage.ResolveFreehandErasePlacementBlocker(
            element, slide.Id, slide.Id, scene, logicalWidth, logicalHeight) ?? string.Empty, StringComparison.Ordinal);
        Assert.True(JsonElement.DeepEquals(originalPayload, element.SharedProductivityBundle!.Value));

        var foreignLayer = Guid.NewGuid();
        var foreignContext = Context(editor.Document);
        var foreignBundle = CanvasSharedInkStrokeFactory.Capture(engine, foreignContext, foreignLayer,
            [new(40, 40, .4), new(160, 40, .8)]);
        var foreignId = engine.ParseBundle(foreignBundle.GetRawText()).Objects.Single().ObjectId;
        var foreignScene = CanvasSharedInkStrokeFactory.RenderScene(engine,
            foreignContext with { SelectedObjectIds = [foreignId] }, foreignLayer, [foreignBundle]);
        Assert.True(editor.TryInsertPreparedSharedObject(editor.Document.Id, editor.Document.Version, slide.Id,
            foreignBundle, new CanvasPresentInkBridge(engine, editor.Document.Id, editor.Document.Version),
            foreignScene.X / logicalWidth, foreignScene.Y / logicalHeight,
            foreignScene.Width / logicalWidth, foreignScene.Height / logicalHeight).Inserted);
        var foreignElement = editor.SelectedSlide.Elements.Single(value => value.Id == foreignId);
        Assert.Contains("different source LayerId", PresentPage.ResolveFreehandErasePlacementBlocker(
            foreignElement, slide.Id, foreignLayer, foreignScene, logicalWidth, logicalHeight) ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(foreignId, engine.ParseBundle(foreignElement.SharedProductivityBundle!.Value.GetRawText()).Objects.Single().ObjectId);
        Assert.True(JsonElement.DeepEquals(foreignBundle, foreignElement.SharedProductivityBundle!.Value));
    }

    [Fact]
    public async Task Shared_whole_stroke_erase_is_revalidated_and_committed_as_one_undoable_owner_mutation()
    {
        using var paths = new PhysicalPaths(); var repository = new PresentRepository(paths);
        var document = PresentDocument.Create("Erase annotation");
        var editor = new PresentEditor(document);
        var engine = Engine(); var slide = editor.Selection.SlideId;
        var otherSlide = editor.AddSlide(slide);
        Assert.True(editor.SelectSlide(slide));
        var first = CanvasSharedInkStrokeFactory.Capture(engine, Context(document), slide, [new(40, 40, .4), new(160, 40, .8)]);
        var second = CanvasSharedInkStrokeFactory.Capture(engine, Context(document), slide, [new(40, 220, .4), new(160, 220, .8)]);
        var firstId = engine.ParseBundle(first.GetRawText()).Objects.Single().ObjectId;
        var secondId = engine.ParseBundle(second.GetRawText()).Objects.Single().ObjectId;
        Assert.True(editor.TryInsertPreparedSharedObject(document.Id, document.Version, slide, first,
            new CanvasPresentInkBridge(engine, document.Id, document.Version), .1, .1, .2, .1).Inserted);
        Assert.True(editor.TryInsertPreparedSharedObject(document.Id, document.Version, slide, second,
            new CanvasPresentInkBridge(engine, document.Id, document.Version), .2, .4, .2, .1).Inserted);

        var currentBundles = editor.SelectedSlide.Elements.Where(value => value.Kind == PresentElementKind.SharedObject)
            .Select(value => value.SharedProductivityBundle!.Value.Clone()).ToArray();
        var context = Context(document) with { SelectedObjectIds = [firstId, secondId] };
        var owner = new CanvasSharedInkOwnerSnapshot(context, slide, "erase-test", currentBundles);
        var erase = CanvasSharedInkStrokeFactory.CaptureWholeErase(engine, owner,
            [new(100, 20, .5), new(100, 60, .5)], 20);
        var bridge = new CanvasPresentInkBridge(engine, document.Id, document.Version);
        var beforeRefusals = JsonSerializer.Serialize(editor.Document);
        var noOp = editor.TryApplySharedInkErase(document.Id, document.Version, slide, currentBundles, currentBundles, bridge);
        Assert.False(noOp.Changed); Assert.Equal("NoChanges", noOp.Code);
        Assert.Equal(beforeRefusals, JsonSerializer.Serialize(editor.Document));
        var changedSelection = editor.TryApplySharedInkErase(document.Id, document.Version, slide, currentBundles,
            erase.ReplacementBundles!, new SelectionChangingBridge(bridge, () => editor.SelectSlide(otherSlide.Id)));
        Assert.False(changedSelection.Changed); Assert.Equal("RevisionConflict", changedSelection.Code);
        Assert.Equal(otherSlide.Id, editor.Selection.SlideId);
        Assert.Equal(beforeRefusals, JsonSerializer.Serialize(editor.Document));
        Assert.True(editor.SelectSlide(slide));
        var stale = editor.TryApplySharedInkErase(document.Id, document.Version + 1, slide, currentBundles,
            erase.ReplacementBundles!, bridge);
        Assert.False(stale.Changed); Assert.Equal("RevisionConflict", stale.Code);
        Assert.Equal(beforeRefusals, JsonSerializer.Serialize(editor.Document));
        editor.SelectedSlide.Elements.Single(value => value.Id == firstId).Locked = true;
        var locked = editor.TryApplySharedInkErase(document.Id, document.Version, slide, currentBundles,
            erase.ReplacementBundles!, bridge);
        Assert.False(locked.Changed); Assert.Equal("ObjectLocked", locked.Code);
        editor.SelectedSlide.Elements.Single(value => value.Id == firstId).Locked = false;
        var finalReentrant = editor.TryApplySharedInkErase(document.Id, document.Version, slide, currentBundles,
            erase.ReplacementBundles!, bridge, () => { editor.SelectSlide(otherSlide.Id); return true; });
        Assert.False(finalReentrant.Changed); Assert.Equal("RevisionConflict", finalReentrant.Code);
        Assert.Equal(beforeRefusals, JsonSerializer.Serialize(editor.Document));
        Assert.True(editor.SelectSlide(slide));
        var finalGuardCalled = false;
        var reentrant = editor.TryApplySharedInkErase(document.Id, document.Version, slide, currentBundles,
            erase.ReplacementBundles!, bridge, () =>
            {
                finalGuardCalled = true;
                editor.SelectSlide(otherSlide.Id);
                return false;
            });
        Assert.True(finalGuardCalled); Assert.False(reentrant.Changed); Assert.Equal("RevisionConflict", reentrant.Code);
        Assert.Equal(beforeRefusals, JsonSerializer.Serialize(editor.Document));
        Assert.True(editor.SelectSlide(slide));
        var undoBeforeSlideReplacement = editor.CanUndo;
        var staleSlide = editor.SelectedSlide;
        var sameIdReplacement = JsonSerializer.Deserialize<PresentSlide>(JsonSerializer.Serialize(staleSlide))!;
        string? afterReplacement = null;
        var replacedSlide = editor.TryApplySharedInkErase(document.Id, document.Version, slide, currentBundles,
            erase.ReplacementBundles!, bridge, () =>
            {
                var index = editor.Document.Slides.FindIndex(value => ReferenceEquals(value, staleSlide));
                Assert.True(index >= 0);
                editor.Document.Slides[index] = sameIdReplacement;
                afterReplacement = JsonSerializer.Serialize(editor.Document);
                return true;
            });
        Assert.False(replacedSlide.Changed); Assert.Equal("RevisionConflict", replacedSlide.Code);
        Assert.NotNull(afterReplacement);
        Assert.Equal(afterReplacement, JsonSerializer.Serialize(editor.Document));
        Assert.Equal(undoBeforeSlideReplacement, editor.CanUndo);
        Assert.Contains(sameIdReplacement.Elements, value => value.Id == firstId);
        Assert.True(editor.SelectSlide(slide));

        var result = editor.TryApplySharedInkErase(document.Id, document.Version, slide, currentBundles,
            erase.ReplacementBundles!, bridge);

        Assert.True(result.Changed); Assert.Equal("PreparedInDocument", result.Code);
        Assert.Equal(new[] { firstId }, result.ChangedElementIds);
        Assert.DoesNotContain(editor.SelectedSlide.Elements, value => value.Id == firstId);
        Assert.Contains(editor.SelectedSlide.Elements, value => value.Id == secondId);
        Assert.True(editor.Undo());
        Assert.Contains(editor.SelectedSlide.Elements, value => value.Id == firstId);
        Assert.Equal(new[] { secondId }, editor.Selection.ElementIds);
        Assert.True(editor.Redo());
        Assert.DoesNotContain(editor.SelectedSlide.Elements, value => value.Id == firstId);
        Assert.Contains(editor.SelectedSlide.Elements, value => value.Id == secondId);
        await repository.SaveAsync(editor.Document, "Present whole-stroke erase regression", TestContext.Current.CancellationToken);
        var reopened = Assert.IsType<PresentDocument>(await repository.LoadAsync(document.Id, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(reopened.Slides.Single(value => value.Id == slide).Elements, value => value.Id == firstId);
        Assert.Contains(reopened.Slides.Single(value => value.Id == slide).Elements, value => value.Id == secondId);
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
        var stroke = Assert.Single(reopened.Slides[0].Elements.Where(value => value.Id == id));
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
        foreach (var command in new[] { "present.ink.pen", "present.ink.highlighter", "present.ink.eraser", "present.ink.color.blue", "present.ink.width.broad" })
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

    [AvaloniaFact]
    public async Task CUI_eraser_tool_routes_to_the_shared_whole_stroke_input_and_pen_restores_draw_mode()
    {
        using var paths = new PhysicalPaths(); using var bus = new HavenEventBus();
        var page = new PresentPage(bus, new PresentRepository(paths), new PresentPptxExportService(), null, null, Engine());
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var window = new Avalonia.Controls.Window { Width = 1280, Height = 800, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            var input = Assert.IsType<CanvasSharedInkInput>(page.FreehandInput);
            var commands = Assert.IsAssignableFrom<ICuiActionDispatcher>(page.FreehandCommands);
            var availability = Assert.IsAssignableFrom<ICuiActionAvailability>(page.FreehandCommands);
            Assert.True(availability.IsActionAvailable("present.ink.toggle"));
            await commands.DispatchAsync("present.ink.toggle", null, TestContext.Current.CancellationToken);
            Assert.True(input.CanDraw);
            Assert.True(input.EraserEnabled);
            Assert.True(availability.IsActionAvailable("present.ink.eraser"));
            await commands.DispatchAsync("present.ink.eraser", null, TestContext.Current.CancellationToken);
            Assert.True(input.EraseMode);
            await commands.DispatchAsync("present.ink.width.broad", null, TestContext.Current.CancellationToken);
            Assert.True(input.EraseMode);
            Assert.Equal(input.Style.BaseWidth * 4, input.EraserWidth);
            await commands.DispatchAsync("present.ink.pen", null, TestContext.Current.CancellationToken);
            Assert.False(input.EraseMode);
        }
        finally
        {
            await page.PrepareToCloseAsync(cancellationToken: TestContext.Current.CancellationToken);
            window.Close();
            await page.CloseAndDrainAsync();
        }
    }

    [AvaloniaFact]
    public async Task Moved_shared_ink_disables_only_the_unmapped_eraser_and_retains_its_canonical_payload()
    {
        using var paths = new PhysicalPaths(); using var bus = new HavenEventBus();
        var repository = new PresentRepository(paths); var engine = Engine();
        var document = PresentDocument.Create("Moved revision ink"); var editor = new PresentEditor(document);
        var slideId = editor.Selection.SlideId; var bundle = Bundle(document, slideId);
        var bridge = new CanvasPresentInkBridge(engine, document.Id, document.Version);
        var id = engine.ParseBundle(bundle.GetRawText()).Objects.Single().ObjectId;
        var scene = CanvasSharedInkStrokeFactory.RenderScene(engine, Context(document) with { SelectedObjectIds = [id] }, slideId, [bundle]);
        Assert.True(editor.TryInsertPreparedSharedObject(document.Id, document.Version, slideId, bundle, bridge,
            scene.X / 960, scene.Y / 540, scene.Width / 960, scene.Height / 540).Inserted);
        var elementId = editor.Selection.ElementIds.Single();
        var sourcePayload = editor.SelectedSlide.Elements.Single(value => value.Id == elementId).SharedProductivityBundle!.Value.Clone();
        await repository.SaveAsync(editor.Document, "Moved shared ink eraser capability regression", TestContext.Current.CancellationToken);

        var page = new PresentPage(bus, repository, new PresentPptxExportService(), null, null, engine);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var window = new Avalonia.Controls.Window { Width = 1280, Height = 800, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            var input = Assert.IsType<CanvasSharedInkInput>(page.FreehandInput);
            var commands = Assert.IsAssignableFrom<ICuiActionDispatcher>(page.FreehandCommands);
            var availability = Assert.IsAssignableFrom<ICuiActionAvailability>(page.FreehandCommands);
            var pageEditor = page.Editor!;
            pageEditor.SelectElements([elementId]);
            page.Route.SetPhase2Document(page.Document!, 0, pageEditor.Selection.ElementIds, pageEditor.CanUndo, pageEditor.CanRedo);
            await commands.DispatchAsync("present.ink.toggle", null, TestContext.Current.CancellationToken);

            Assert.True(input.CanDraw);
            Assert.True(input.EraserEnabled);
            Assert.True(availability.IsActionAvailable("present.ink.eraser"));
            await commands.DispatchAsync("present.ink.eraser", null, TestContext.Current.CancellationToken);
            Assert.True(input.EraseMode);

            Assert.True(pageEditor.MoveSelection(.1, 0));
            Assert.False(input.EraserEnabled);
            Assert.False(input.EraseMode);
            Assert.False(availability.IsActionAvailable("present.ink.eraser"));
            Assert.True(availability.IsActionAvailable("present.ink.pen"));
            Assert.True(JsonElement.DeepEquals(sourcePayload,
                page.Document!.Slides.Single(value => value.Id == slideId).Elements.Single(value => value.Id == elementId).SharedProductivityBundle!.Value));
        }
        finally
        {
            Assert.True(await page.PrepareToCloseAsync(cancellationToken: TestContext.Current.CancellationToken));
            window.Content = null; window.Close();
            await page.CloseAndDrainAsync();
        }
    }

    [AvaloniaFact]
    public async Task Foreign_layer_ink_keeps_pen_available_but_blocks_eraser_without_rebinding_the_source()
    {
        using var paths = new PhysicalPaths(); using var bus = new HavenEventBus();
        var repository = new PresentRepository(paths); var engine = Engine();
        var document = PresentDocument.Create("Foreign-layer revision ink"); var editor = new PresentEditor(document);
        var slideId = editor.Selection.SlideId; var foreignLayer = Guid.NewGuid();
        var bundle = CanvasSharedInkStrokeFactory.Capture(engine, Context(document), foreignLayer,
            [new(40, 40, .4), new(160, 40, .8)]);
        var id = engine.ParseBundle(bundle.GetRawText()).Objects.Single().ObjectId;
        var scene = CanvasSharedInkStrokeFactory.RenderScene(engine, Context(document) with { SelectedObjectIds = [id] }, foreignLayer, [bundle]);
        Assert.True(editor.TryInsertPreparedSharedObject(document.Id, document.Version, slideId, bundle,
            new CanvasPresentInkBridge(engine, document.Id, document.Version), scene.X / 960, scene.Y / 540,
            scene.Width / 960, scene.Height / 540).Inserted);
        var sourcePayload = editor.Document.Slides.Single(value => value.Id == slideId).Elements.Single(value => value.Id == id).SharedProductivityBundle!.Value.Clone();
        await repository.SaveAsync(editor.Document, "Foreign-layer ink eraser capability regression", TestContext.Current.CancellationToken);

        var page = new PresentPage(bus, repository, new PresentPptxExportService(), null, null, engine);
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var window = new Avalonia.Controls.Window { Width = 1280, Height = 800, Content = page };
        try
        {
            window.Show(); window.UpdateLayout();
            var input = Assert.IsType<CanvasSharedInkInput>(page.FreehandInput);
            var commands = Assert.IsAssignableFrom<ICuiActionDispatcher>(page.FreehandCommands);
            var availability = Assert.IsAssignableFrom<ICuiActionAvailability>(page.FreehandCommands);
            await commands.DispatchAsync("present.ink.toggle", null, TestContext.Current.CancellationToken);

            Assert.True(input.CanDraw);
            Assert.False(input.EraserEnabled);
            Assert.False(availability.IsActionAvailable("present.ink.eraser"));
            Assert.True(availability.IsActionAvailable("present.ink.pen"));
            Assert.Equal(id, engine.ParseBundle(page.Document!.Slides.Single(value => value.Id == slideId)
                .Elements.Single(value => value.Id == id).SharedProductivityBundle!.Value.GetRawText()).Objects.Single().ObjectId);
            Assert.True(JsonElement.DeepEquals(sourcePayload,
                page.Document.Slides.Single(value => value.Id == slideId).Elements.Single(value => value.Id == id).SharedProductivityBundle!.Value));
        }
        finally
        {
            Assert.True(await page.PrepareToCloseAsync(cancellationToken: TestContext.Current.CancellationToken));
            window.Content = null; window.Close();
            await page.CloseAndDrainAsync();
        }
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
