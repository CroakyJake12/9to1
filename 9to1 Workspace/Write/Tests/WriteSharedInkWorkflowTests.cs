using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
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
