using System.Text.Json;
using Avalonia;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Write;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Write.Tests;

public sealed class WriteSharedInkWorkflowTests
{
    [Fact]
    public void Mutation_epoch_is_current_before_the_owning_presentation_refresh()
    {
        var editor = new WriteDocumentEditor(NotesDocument.Create("Refresh ordering"));
        editor.InitializeSharedInkTracking();
        var drawing = editor.InsertSharedInk();
        WriteSharedInkSnapshot? shown = null;
        editor.Changed += (_, _) => shown = editor.CaptureSharedInk(drawing.Id);
        editor.SetTitle("Latest mutation");
        Assert.NotNull(shown);
        Assert.True(editor.IsSharedInkCurrent(shown));
    }

    [Fact]
    public void Blank_drawing_uses_the_existing_undo_history_without_a_sample_stroke()
    {
        var document = NotesDocument.Create("Real drawing");
        var editor = new WriteDocumentEditor(document);
        var before = editor.Blocks().Select(block => block.Id).ToArray();
        var drawing = editor.InsertSharedInk();
        Assert.Equal(NotesBlockKind.Canvas, drawing.Kind);
        Assert.Empty(drawing.Canvas!.Strokes);
        Assert.Empty(drawing.SharedProductivityBundles!);
        Assert.Equal(drawing.Id, editor.SelectedBlockId);
        Assert.True(editor.Undo());
        Assert.Equal(before, editor.Blocks().Select(block => block.Id));
        Assert.True(editor.Redo());
        Assert.Equal(drawing.Id, Assert.Single(editor.Blocks(), block => block.SharedProductivityBundles is not null).Id);
    }

    [Fact]
    public void Full_Home_bundle_roundtrips_through_append_undo_and_redo()
    {
        var editor = new WriteDocumentEditor(NotesDocument.Create("Opaque drawing"));
        var drawing = editor.InsertSharedInk();
        var engine = Engine();
        var bundle = Bundle(engine, editor, drawing.Id);
        Assert.True(editor.AppendSharedInk(editor.CaptureSharedInk(drawing.Id)!, bundle,
            source => CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(editor), source)));
        var retained = Assert.Single(drawing.SharedProductivityBundles!);
        Assert.True(JsonElement.DeepEquals(bundle, retained));
        Assert.True(editor.Undo());
        Assert.Empty(editor.Blocks().Single(block => block.Id == drawing.Id).SharedProductivityBundles!);
        Assert.True(editor.Redo());
        Assert.True(JsonElement.DeepEquals(bundle,
            Assert.Single(editor.Blocks().Single(block => block.Id == drawing.Id).SharedProductivityBundles!)));
    }

    [Fact]
    public void A_stale_snapshot_cannot_be_reused_after_text_edit_and_undo()
    {
        var editor = new WriteDocumentEditor(NotesDocument.Create("Epoch check"));
        var drawing = editor.InsertSharedInk();
        var expected = editor.CaptureSharedInk(drawing.Id)!;
        editor.SetTitle("Intermediate title");
        Assert.True(editor.Undo());
        editor.SelectBlock(drawing.Id);
        var called = false;
        Assert.False(editor.AppendSharedInk(expected, JsonSerializer.SerializeToElement(new { }), source => { called = true; return source; }));
        Assert.False(called);
        Assert.Empty(editor.Blocks().Single(block => block.Id == drawing.Id).SharedProductivityBundles!);
    }

    [Fact]
    public void A_foreign_editor_or_selection_cannot_commit_a_pending_stroke()
    {
        var document = NotesDocument.Create("Owner check");
        var editor = new WriteDocumentEditor(document);
        var drawing = editor.InsertSharedInk();
        var expected = editor.CaptureSharedInk(drawing.Id)!;
        var other = new WriteDocumentEditor(document);
        other.SelectBlock(drawing.Id);
        Assert.False(other.AppendSharedInk(expected, JsonSerializer.SerializeToElement(new { }), value => value));
        editor.SelectBlock(editor.Blocks().First(block => block.Id != drawing.Id).Id);
        Assert.False(editor.AppendSharedInk(expected, JsonSerializer.SerializeToElement(new { }), value => value));
        Assert.Empty(drawing.SharedProductivityBundles!);
    }

    [Fact]
    public void Owner_is_checked_again_after_canonical_preparation()
    {
        var editor = new WriteDocumentEditor(NotesDocument.Create("Preparation race"));
        var drawing = editor.InsertSharedInk();
        var engine = Engine();
        var expected = editor.CaptureSharedInk(drawing.Id)!;
        Assert.False(editor.AppendSharedInk(expected, Bundle(engine, editor, drawing.Id), source =>
        {
            var captured = CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(editor), source);
            editor.SetTitle("Changed during preparation");
            return captured;
        }));
        Assert.Empty(drawing.SharedProductivityBundles!);
        Assert.Equal("Changed during preparation", editor.Document.Title);
    }

    [Fact]
    public void Canonical_refusal_does_not_change_the_document_or_erase_opaque_input()
    {
        var editor = new WriteDocumentEditor(NotesDocument.Create("Refused bundle"));
        var drawing = editor.InsertSharedInk();
        var engine = Engine();
        var json = JsonNode.Parse(Bundle(engine, editor, drawing.Id).GetRawText())!.AsObject();
        json["futureRequiredEnvelope"] = new JsonObject { ["version"] = 99 };
        using var source = JsonDocument.Parse(json.ToJsonString());
        var original = source.RootElement.Clone();
        var raw = original.GetRawText();
        Assert.Throws<InvalidDataException>(() => editor.AppendSharedInk(editor.CaptureSharedInk(drawing.Id)!, original,
            value => CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(editor), value)));
        Assert.Equal(raw, original.GetRawText());
        Assert.Empty(drawing.SharedProductivityBundles!);
    }

    [Fact]
    public void Legacy_canvas_is_preserved_and_cannot_be_reinterpreted_as_a_shared_drawing()
    {
        var document = NotesDocument.Create("Legacy canvas");
        var legacy = NotesBlock.CanvasBlock();
        legacy.Order = document.Sections[0].Pages[0].Blocks.Count;
        document.Sections[0].Pages[0].Blocks.Add(legacy);
        var editor = new WriteDocumentEditor(document);
        var original = JsonSerializer.Serialize(legacy);
        Assert.Null(editor.CaptureSharedInk(legacy.Id));
        Assert.Equal(original, JsonSerializer.Serialize(legacy));
        legacy.SharedProductivityBundles = [];
        legacy.Canvas!.Objects.Add(new NotesCanvasObject());
        Assert.False(new NotesDocumentValidator().Validate(document).IsValid);
        Assert.Null(editor.CaptureSharedInk(legacy.Id));
    }

    [Fact]
    public async Task Real_repository_and_native_package_preserve_shared_identity_and_complete_payload()
    {
        using var paths = new WriteInkPaths();
        var repository = new NotesRepository(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths));
        var document = NotesDocument.Create("Stored shared drawing");
        var editor = new WriteDocumentEditor(document);
        var drawing = editor.InsertSharedInk();
        var engine = Engine();
        var bundle = Bundle(engine, editor, drawing.Id);
        Assert.True(editor.AppendSharedInk(editor.CaptureSharedInk(drawing.Id)!, bundle,
            source => CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(editor), source)));
        await repository.SaveAsync(document, "Save real shared drawing", default);
        var loaded = await new NotesRepository(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths)).LoadAsync(document.Id, default);
        Assert.NotNull(loaded);
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(loaded.Sections.SelectMany(section => section.Pages)
            .SelectMany(page => page.Blocks).Single(block => block.Id == drawing.Id).SharedProductivityBundles!)));
        var packages = new WriteNativeDocumentPackageStore();
        var path = Path.Combine(paths.DataDirectory, "drawing.9to1w");
        Assert.True((await packages.SaveAsync(loaded, path)).IsSuccess);
        var opened = await packages.OpenAsync(path);
        Assert.True(opened.IsSuccess);
        Assert.Equal(document.Id, opened.Value!.Id);
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(opened.Value.Sections.SelectMany(section => section.Pages)
            .SelectMany(page => page.Blocks).Single(block => block.Id == drawing.Id).SharedProductivityBundles!)));
    }

    [Fact]
    public async Task Save_undo_save_and_reopen_preserve_the_current_repository_CAS_version()
    {
        using var paths = new WriteInkPaths();
        var repository = new NotesRepository(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths));
        var document = NotesDocument.Create("Undo persisted ink");
        await repository.SaveAsync(document, "Seed canonical revision", default);
        var editor = new WriteDocumentEditor(document);
        var drawing = editor.InsertSharedInk();
        await repository.SaveAsync(document, "Save blank drawing", default);
        var engine = Engine();
        var bundle = Bundle(engine, editor, drawing.Id);
        Assert.True(editor.AppendSharedInk(editor.CaptureSharedInk(drawing.Id)!, bundle,
            source => CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(editor), source)));
        await repository.SaveAsync(document, "Save stroke", default);
        var savedVersion = document.Version;
        Assert.True(editor.Undo());
        Assert.Equal(savedVersion, document.Version);
        Assert.Empty(editor.Blocks().Single(block => block.Id == drawing.Id).SharedProductivityBundles!);
        await repository.SaveAsync(document, "Save drawing undo", default);
        var reopened = await repository.LoadAsync(document.Id, default);
        Assert.Equal(savedVersion + 1, reopened!.Version);
        Assert.Empty(reopened.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
            .Single(block => block.Id == drawing.Id).SharedProductivityBundles!);
        Assert.True(editor.Redo());
        Assert.Equal(savedVersion + 1, document.Version);
        await repository.SaveAsync(document, "Save drawing redo", default);
        var redone = await repository.LoadAsync(document.Id, default);
        Assert.Equal(savedVersion + 2, redone!.Version);
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(redone.Sections.SelectMany(section => section.Pages)
            .SelectMany(page => page.Blocks).Single(block => block.Id == drawing.Id).SharedProductivityBundles!)));
    }

    [AvaloniaFact]
    public async Task Missing_Home_engine_keeps_existing_Write_scene_and_truthfully_disables_drawing()
    {
        using var paths = new WriteInkPaths();
        var diagnostics = new ProductionDiagnostics(paths);
        var validator = new NotesDocumentValidator();
        var repository = new NotesRepository(paths, validator, diagnostics);
        var document = NotesDocument.Create("No invented pen");
        await repository.SaveAsync(document, "Seed", default);
        await using var page = new WritePage(new HavenEventBus(), repository,
            new NotesImportExportService(validator, diagnostics), initialDocumentId: document.Id);
        await page.InitializeAsync();
        var scene = Assert.IsType<HavenSceneControl>(page.Content);
        var insert = Assert.Single(scene.Root!.DescendantsAndSelf().OfType<Button>(), value => value.Name == "Write.Tab.Insert");
        var router = new HavenInputRouter(scene.Root);
        router.Focus(insert);
        Assert.True(router.KeyDown(HavenKey.Enter));
        Assert.True(router.KeyUp(HavenKey.Enter));
        var drawing = Assert.Single(scene.Root.DescendantsAndSelf().OfType<Button>(), value => value.Name == "Write.Insert.Drawing");
        Assert.False(drawing.GetValue(HavenProperties.Enabled));
        Assert.Contains("unavailable", drawing.Accessibility.Description ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(page.Document!.Sections.SelectMany(section => section.Pages).SelectMany(value => value.Blocks),
            value => value.SharedProductivityBundles is not null);
        Assert.False(page.IsDirty);
    }

    [AvaloniaFact]
    public async Task Standalone_bootstrap_uses_the_same_real_Home_handler_without_inserting_demo_data()
    {
        using var paths = new WriteInkPaths();
        var diagnostics = new ProductionDiagnostics(paths);
        var validator = new NotesDocumentValidator();
        var repository = new NotesRepository(paths, validator, diagnostics);
        var document = NotesDocument.Create("Standalone shared engine");
        await repository.SaveAsync(document, "Seed", default);
        await using var host = WriteAppHost.Create(repository, new NotesImportExportService(validator, diagnostics), document.Id);
        await host.InitializeAsync();
        Assert.True(host.Page.SharedDrawingCompatibility!.Compatible);
        Assert.Empty(host.Page.SharedDrawingCompatibility.UnsupportedRequiredTypes);
        Assert.DoesNotContain(host.Document!.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks),
            block => block.SharedProductivityBundles is not null);
        Assert.False(host.Page.IsDirty);
        // Schema/handler composition is proven here; native Rnote/hardware has not been invoked.
    }

    [AvaloniaFact]
    public async Task Supplied_Home_engine_is_borrowed_and_an_incompatible_registry_is_never_replaced()
    {
        using var paths = new WriteInkPaths();
        var diagnostics = new ProductionDiagnostics(paths);
        var validator = new NotesDocumentValidator();
        var repository = new NotesRepository(paths, validator, diagnostics);
        var document = NotesDocument.Create("Actual borrowed registry");
        await repository.SaveAsync(document, "Seed", default);
        var supplied = new HomeProductivityEngine(); // No drawing handler was registered by its owner.
        await using (var host = WriteAppHost.Create(repository, new NotesImportExportService(validator, diagnostics),
            document.Id, null, null, null, null, null, supplied))
        {
            await host.InitializeAsync();
            Assert.False(host.Page.SharedDrawingCompatibility!.Compatible);
            Assert.Contains("drawing.ink", host.Page.SharedDrawingCompatibility.UnsupportedRequiredTypes);
            var scene = Assert.IsType<HavenSceneControl>(host.Page.Content);
            var insert = Assert.Single(scene.Root!.DescendantsAndSelf().OfType<Button>(), button => button.Name == "Write.Tab.Insert");
            var router = new HavenInputRouter(scene.Root);
            router.Focus(insert); router.KeyDown(HavenKey.Enter); router.KeyUp(HavenKey.Enter);
            Assert.False(Assert.Single(scene.Root.DescendantsAndSelf().OfType<Button>(), button => button.Name == "Write.Insert.Drawing")
                .GetValue(HavenProperties.Enabled));
            // A later owner registration is observed on that same engine, not a copied fallback.
            supplied.RegisterObjectHandler(new CanvasInkObjectHandler());
            Assert.True(host.Page.SharedDrawingCompatibility.Compatible);
            Assert.False(host.Page.IsDirty);
        }
        Assert.True(supplied.GetCompatibility("write", "development", ["drawing.ink"]).Compatible);
    }

    [Fact]
    public async Task Shared_whole_stroke_erasure_uses_normal_undo_and_preserves_survivors_through_repository_and_native_package()
    {
        using var paths = new WriteInkPaths(); var diagnostics = new ProductionDiagnostics(paths);
        var repository = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
        var editor = new WriteDocumentEditor(NotesDocument.Create("Undo eraser")); var drawing = editor.InsertSharedInk(); var engine = Engine();
        var first = HorizontalBundle(engine, editor, drawing.Id, 40); var second = HorizontalBundle(engine, editor, drawing.Id, 220);
        var firstStrokeId = engine.ParseBundle(first.GetRawText()).Objects.Single().ObjectId;
        var secondStrokeId = engine.ParseBundle(second.GetRawText()).Objects.Single().ObjectId;
        foreach (var bundle in new[] { first, second })
            Assert.True(editor.AppendSharedInk(editor.CaptureSharedInk(drawing.Id)!, bundle, source => CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(editor), source)));
        await repository.SaveAsync(editor.Document, "Seed erasure", default);
        var expected = editor.CaptureSharedInk(drawing.Id)!;
        var erased = CanvasSharedInkStrokeFactory.CaptureWholeErase(engine,
            new(Context(editor), drawing.Id, "exact original", expected.Bundles), [new(100, 20, .5), new(100, 60, .5)], 20);
        Assert.True(JsonElement.DeepEquals(second, Assert.Single(erased.ReplacementBundles!)));
        Assert.True(editor.ReplaceSharedInk(expected, erased.ReplacementBundles!, values => values));
        Assert.True(JsonElement.DeepEquals(second, Assert.Single(editor.Blocks().Single(block => block.Id == drawing.Id).SharedProductivityBundles!)));
        Assert.True(editor.Undo()); Assert.Equal(2, editor.Blocks().Single(block => block.Id == drawing.Id).SharedProductivityBundles!.Count);
        await repository.SaveAsync(editor.Document, "Save restored strokes after undo", default);
        // A separately constructed repository over the same physical paths checks durable ordering,
        // not merely a reload through the instance which performed the write.
        var undoReader = new NotesRepository(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths));
        var afterUndo = Assert.IsType<NotesDocument>(await undoReader.LoadAsync(editor.Document.Id, default));
        var restored = afterUndo.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
            .Single(block => block.Id == drawing.Id).SharedProductivityBundles!;
        Assert.Equal(2, restored.Count);
        Assert.Contains(restored, bundle => JsonElement.DeepEquals(first, bundle));
        Assert.Contains(restored, bundle => JsonElement.DeepEquals(second, bundle));
        Assert.Collection(restored,
            bundle => Assert.True(JsonElement.DeepEquals(first, bundle)),
            bundle => Assert.True(JsonElement.DeepEquals(second, bundle)));
        Assert.Equal(new[] { firstStrokeId, secondStrokeId }, restored.SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects)
            .Select(stroke => stroke.ObjectId).ToArray());
        Assert.True(editor.Redo()); await repository.SaveAsync(editor.Document, "Save erasure after redo", default);
        var redoReader = new NotesRepository(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths));
        var reopened = Assert.IsType<NotesDocument>(await redoReader.LoadAsync(editor.Document.Id, default));
        var erasedBundles = reopened.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
            .Single(block => block.Id == drawing.Id).SharedProductivityBundles!;
        Assert.Equal(1, erasedBundles.Count);
        Assert.Collection(erasedBundles, bundle => Assert.True(JsonElement.DeepEquals(second, bundle)));
        Assert.Equal(new[] { secondStrokeId }, erasedBundles.SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects)
            .Select(stroke => stroke.ObjectId).ToArray());
        Assert.True(JsonElement.DeepEquals(second, Assert.Single(reopened.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
            .Single(block => block.Id == drawing.Id).SharedProductivityBundles!)));
        var packages = new WriteNativeDocumentPackageStore(); var path = Path.Combine(paths.DataDirectory, "erased.9to1w");
        Assert.True((await packages.SaveAsync(reopened, path)).IsSuccess);
        var package = await packages.OpenAsync(path); Assert.True(package.IsSuccess);
        Assert.Equal(editor.Document.Id, package.Value!.Id);
        Assert.True(JsonElement.DeepEquals(second, Assert.Single(package.Value.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
            .Single(block => block.Id == drawing.Id).SharedProductivityBundles!)));
    }

    [Fact]
    public void Erasing_one_stroke_from_a_multi_object_bundle_retains_its_sibling_and_object_extensions()
    {
        var editor = new WriteDocumentEditor(NotesDocument.Create("Sibling stroke retention"));
        var drawing = editor.InsertSharedInk(); var engine = Engine();
        var first = HorizontalBundle(engine, editor, drawing.Id, 40);
        var second = HorizontalBundle(engine, editor, drawing.Id, 220);
        var firstBundle = engine.ParseBundle(first.GetRawText());
        var secondObject = engine.ParseBundle(second.GetRawText()).Objects.Single();
        var combinedJson = engine.SerializeBundle(firstBundle with { Objects = [.. firstBundle.Objects, secondObject] });
        var combinedNode = JsonNode.Parse(combinedJson)!.AsObject();
        combinedNode["objects"]![1]!["content"]!["ownerStrokeExtension"] =
            JsonSerializer.SerializeToNode(new { origin = "retained", revision = 4 });
        using var extendedCombinedDocument = JsonDocument.Parse(combinedNode.ToJsonString());
        var combined = CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(editor), extendedCombinedDocument.RootElement);
        Assert.True(editor.AppendSharedInk(editor.CaptureSharedInk(drawing.Id)!, combined,
            value => CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(editor), value)));

        var erased = CanvasSharedInkStrokeFactory.CaptureWholeErase(engine,
            new(Context(editor), drawing.Id, "sibling retention", [combined]), [new(100, 20, .5), new(100, 60, .5)], 20);
        var survivor = Assert.Single(erased.ReplacementBundles!);
        var retained = engine.ParseBundle(survivor.GetRawText());
        Assert.Single(retained.Objects);
        Assert.Equal(secondObject.ObjectId, retained.Objects[0].ObjectId);
        Assert.True(JsonElement.DeepEquals(firstBundle.Accessibility, retained.Accessibility));
        Assert.False(JsonElement.DeepEquals(firstBundle.PortableFallback, retained.PortableFallback));
        Assert.Contains("futureOptionalStroke", survivor.GetRawText(), StringComparison.Ordinal);
        Assert.True(JsonElement.DeepEquals(
            combined.GetProperty("objects")[1].GetProperty("content").GetProperty("ownerStrokeExtension"),
            survivor.GetProperty("objects")[0].GetProperty("content").GetProperty("ownerStrokeExtension")));
        Assert.True(editor.ReplaceSharedInk(editor.CaptureSharedInk(drawing.Id)!, erased.ReplacementBundles!, values => values));
        Assert.True(editor.Undo());
        Assert.Equal(2, engine.ParseBundle(Assert.Single(editor.Blocks().Single(block => block.Id == drawing.Id)
            .SharedProductivityBundles!).GetRawText()).Objects.Count);
        Assert.True(editor.Redo());
        var redone = Assert.Single(editor.Blocks().Single(block => block.Id == drawing.Id).SharedProductivityBundles!);
        Assert.True(JsonElement.DeepEquals(survivor, redone));
    }

    [Fact]
    public void Unsupported_bundle_envelope_is_preserved_but_not_admitted_as_erasable_ink()
    {
        var editor = new WriteDocumentEditor(NotesDocument.Create("Envelope admission"));
        var drawing = editor.InsertSharedInk(); var engine = Engine();
        var source = HorizontalBundle(engine, editor, drawing.Id, 40);
        var node = JsonNode.Parse(source.GetRawText())!.AsObject();
        node["ownerBundleExtension"] = JsonSerializer.SerializeToNode(new { origin = "retained", revision = 4 });
        var unsupported = JsonSerializer.SerializeToElement(node);
        var original = unsupported.GetRawText();
        var error = Assert.Throws<InvalidDataException>(() =>
            CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(editor), unsupported));
        Assert.Contains("BundleExtensionsUnsupported", error.Message);
        Assert.Equal(original, unsupported.GetRawText());
        Assert.Equal("retained", engine.ParseBundle(original).Extensions!["ownerBundleExtension"].GetProperty("origin").GetString());
    }

    [Fact]
    public void Shared_replacement_checks_owner_selection_and_epoch_again_after_preparation()
    {
        var editor = new WriteDocumentEditor(NotesDocument.Create("Current eraser")); var drawing = editor.InsertSharedInk();
        var expected = editor.CaptureSharedInk(drawing.Id)!; var called = false;
        editor.SelectBlock(editor.Blocks().First(block => block.Id != drawing.Id).Id);
        Assert.False(editor.ReplaceSharedInk(expected, [], values => { called = true; return values; })); Assert.False(called);
        editor.SelectBlock(drawing.Id); expected = editor.CaptureSharedInk(drawing.Id)!;
        Assert.False(editor.ReplaceSharedInk(expected, [], values => { editor.SetTitle("Changed during eraser validation"); return values; }));
        Assert.Equal("Changed during eraser validation", editor.Document.Title); Assert.Empty(drawing.SharedProductivityBundles!);
        var other = new WriteDocumentEditor(editor.Document); other.SelectBlock(drawing.Id);
        Assert.False(other.ReplaceSharedInk(editor.CaptureSharedInk(drawing.Id)!, [], values => values));
    }

    [Fact]
    public void Missed_eraser_has_no_undo_entry_and_original_validation_failure_is_preserved()
    {
        var editor = new WriteDocumentEditor(NotesDocument.Create("No change eraser")); var drawing = editor.InsertSharedInk(); var engine = Engine();
        var bundle = Bundle(engine, editor, drawing.Id);
        Assert.True(editor.AppendSharedInk(editor.CaptureSharedInk(drawing.Id)!, bundle, source => CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(editor), source)));
        var original = editor.CaptureSharedInk(drawing.Id)!;
        var missed = CanvasSharedInkStrokeFactory.CaptureWholeErase(engine,
            new(Context(editor), drawing.Id, "no intersection", original.Bundles), [new(700, 400, .5), new(760, 430, .5)], 20);
        Assert.True(editor.ReplaceSharedInk(original, missed.ReplacementBundles!, values => values));
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(editor.Blocks().Single(block => block.Id == drawing.Id).SharedProductivityBundles!)));
        var before = JsonSerializer.Serialize(editor.Document); var failure = new IOException("Original donor preparation failed");
        var observed = Assert.Throws<IOException>(() => editor.ReplaceSharedInk(editor.CaptureSharedInk(drawing.Id)!, [], _ => throw failure));
        Assert.Same(failure, observed); Assert.Equal(before, JsonSerializer.Serialize(editor.Document));
        var current = editor.CaptureSharedInk(drawing.Id)!;
        Assert.True(editor.ReplaceSharedInk(current, current.Bundles, values => values));
        Assert.Equal(before, JsonSerializer.Serialize(editor.Document));
        Assert.True(editor.Undo()); // The actual preceding append, not a no-op eraser entry.
        Assert.Empty(editor.Blocks().Single(block => block.Id == drawing.Id).SharedProductivityBundles!);
    }

    [AvaloniaFact]
    public async Task Actual_Write_CUI_tools_switch_pen_highlighter_and_eraser_then_save_a_real_pointer_erasure()
    {
        using var paths = new WriteInkPaths(); var diagnostics = new ProductionDiagnostics(paths); var validator = new NotesDocumentValidator();
        var repository = new NotesRepository(paths, validator, diagnostics); var engine = Engine();
        var seed = new WriteDocumentEditor(NotesDocument.Create("CUI eraser")); var drawing = seed.InsertSharedInk();
        var first = HorizontalBundle(engine, seed, drawing.Id, 40); var second = HorizontalBundle(engine, seed, drawing.Id, 220);
        var firstId = engine.ParseBundle(first.GetRawText()).Objects[0].ObjectId;
        var secondId = engine.ParseBundle(second.GetRawText()).Objects[0].ObjectId;
        foreach (var bundle in new[] { first, second })
            Assert.True(seed.AppendSharedInk(seed.CaptureSharedInk(drawing.Id)!, bundle, value => CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(seed), value)));
        await repository.SaveAsync(seed.Document, "Seed original UI drawing", default);
        using var bus = new HavenEventBus();
        await using var page = new WritePage(engine, bus, repository, new NotesImportExportService(validator, diagnostics), initialDocumentId: seed.Document.Id);
        await page.InitializeAsync();
        var window = new Avalonia.Controls.Window { Content = page, Width = 1400, Height = 1200 };
        try
        {
            window.Show(); window.UpdateLayout();
            var input = Assert.Single(window.GetVisualDescendants().OfType<CanvasSharedInkInput>());
            Assert.True(input.EraserEnabled); Assert.True(input.CanDraw);
            Avalonia.Point At(double x, double y) => input.TranslatePoint(new(x / 960 * input.Bounds.Width, y / 540 * input.Bounds.Height), window)!.Value;
            // Select the real drawing using its actual input; any authored selection-dot is retained.
            window.MouseDown(At(800, 400), Avalonia.Input.MouseButton.Left); window.MouseUp(At(800, 400), Avalonia.Input.MouseButton.Left);
            if (input.PendingSubmission is { } selection) await selection;
            window.UpdateLayout();
            async Task Click(string name)
            {
                var button = Assert.Single(window.GetVisualDescendants().OfType<Avalonia.Controls.Button>(), value => value.Name == name);
                Assert.True(button.IsEnabled); var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, Avalonia.Input.MouseButton.Left); window.MouseUp(point, Avalonia.Input.MouseButton.Left);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            }
            await Click("write-ink-marker"); Assert.Equal(CanvasRnoteInkKind.Marker, input.Style.Kind); Assert.False(input.EraseMode);
            await Click("write-ink-pen"); Assert.Equal(CanvasRnoteInkKind.Solid, input.Style.Kind); Assert.False(input.EraseMode);
            await Click("write-ink-erase"); Assert.True(input.EraseMode);
            var before = page.Document!.Sections.SelectMany(section => section.Pages).SelectMany(value => value.Blocks)
                .Single(block => block.Id == drawing.Id).SharedProductivityBundles!.Count;
            window.MouseDown(At(100, 20), Avalonia.Input.MouseButton.Left);
            window.MouseMove(At(100, 60), Avalonia.Input.RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(100, 60), Avalonia.Input.MouseButton.Left);
            Assert.NotNull(input.PendingSubmission); await input.PendingSubmission;
            var surviving = page.Document.Sections.SelectMany(section => section.Pages).SelectMany(value => value.Blocks)
                .Single(block => block.Id == drawing.Id).SharedProductivityBundles!;
            Assert.Equal(before - 1, surviving.Count);
            Assert.DoesNotContain(surviving.SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects), value => value.ObjectId == firstId);
            Assert.Contains(surviving, bundle => JsonElement.DeepEquals(second, bundle)); Assert.False(page.IsDirty);
            Assert.True(await page.PrepareToCloseAsync());
            window.Content = null;
            await page.DisposeAsync();

            // Exercise the actual Write launch path and owning handler composition again from durable state.
            // This is a fresh page/repository in-process, not a process-restart claim.
            var reopenRepository = new NotesRepository(paths, validator, new ProductionDiagnostics(paths));
            using var reopenBus = new HavenEventBus();
            await using var reopenedPage = new WritePage(engine, reopenBus, reopenRepository,
                new NotesImportExportService(validator, new ProductionDiagnostics(paths)), initialDocumentId: seed.Document.Id);
            await reopenedPage.InitializeAsync();
            Assert.Equal(seed.Document.Id, reopenedPage.Document!.Id);
            Assert.False(reopenedPage.IsDirty);
            Assert.True(reopenedPage.SharedDrawingCompatibility!.Compatible);
            Assert.IsType<HavenSceneControl>(reopenedPage.Content);
            window.Content = reopenedPage;
            window.UpdateLayout();
            Assert.Single(window.GetVisualDescendants().OfType<CanvasSharedInkInput>());
            var reopenedBundles = reopenedPage.Document.Sections.SelectMany(section => section.Pages)
                .SelectMany(value => value.Blocks).Single(block => block.Id == drawing.Id).SharedProductivityBundles!;
            Assert.Collection(reopenedBundles, value => Assert.True(JsonElement.DeepEquals(second, value)));
            Assert.Equal(secondId, engine.ParseBundle(Assert.Single(reopenedBundles).GetRawText()).Objects.Single().ObjectId);
        }
        finally { window.Content = null; window.Close(); }
    }

    private static JsonElement HorizontalBundle(HomeProductivityEngine engine, WriteDocumentEditor editor, Guid layer, double y)
    {
        var source = CanvasSharedInkStrokeFactory.Capture(engine, Context(editor), layer, [new(40, y, .4), new(160, y, .8)]);
        var extended = JsonNode.Parse(source.GetRawText())!.AsObject(); extended["objects"]![0]!["content"]!["futureOptionalStroke"] = "preserve";
        return CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(editor), JsonSerializer.SerializeToElement(extended));
    }

    private static HomeProductivityEngine Engine() => new(handlers: [new CanvasInkObjectHandler()]);
    private static HomeProductivityContext Context(WriteDocumentEditor editor) => new("write", editor.Document.Id.ToString("D"),
        editor.Document.Version, [], new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });

    private static JsonElement Bundle(HomeProductivityEngine engine, WriteDocumentEditor editor, Guid layer)
    {
        var id = Guid.NewGuid();
        var stroke = new CanvasInkStroke { StrokeId = id, LayerId = layer, RevisionId = Guid.NewGuid(),
            Samples = [new(10, 20, .4), new(35, 44, .8)],
            ResolvedBrushProperties = new CanvasBrushProperties { EngineParameters = new()
                { ["brushStyle"] = JsonSerializer.SerializeToElement("solid") } },
            ExtensionData = new() { ["ownerOptional"] = JsonSerializer.SerializeToElement(new { value = "retain", version = 7 }) } };
        var value = engine.CreateObject("drawing.ink", id, JsonSerializer.SerializeToElement(stroke)) with
            { Accessibility = JsonSerializer.SerializeToElement(new { label = "Owner annotation" }), AssetReferences = ["asset:retained-reference"] };
        var bundle = engine.SerializeSelection(Context(editor) with { SelectedObjectIds = [id] }, [value]);
        using var json = JsonDocument.Parse(engine.SerializeBundle(bundle));
        return CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(editor), json.RootElement);
    }

    private sealed class WriteInkPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-write-ink-tests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
