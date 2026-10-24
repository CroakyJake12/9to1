using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;
using NineToOne.Web.Productivity.Boards;

namespace Haven.Desktop.Tests;

/// <summary>Focused executable smoke cases over the actual shared Boards core, canonical
/// Notes filesystem repository and registered Home drawing handler. No native renderer,
/// original Home permission composition, platform acceptance or release readiness is claimed.</summary>
public sealed class BoardsSharedInkCoreTests
{
    [Fact]
    public async Task Shared_drawing_save_undo_redo_and_reopen_keep_stable_identity_and_complete_bundle()
    {
        await using var rig = new Rig();
        await using var core = new BoardsBrowserCore(rig.Repository);
        Assert.True((await core.CreateAsync("Shared Boards ink")).Succeeded);
        core.ConfigureSharedInk(true);
        var inserted = await core.InsertSharedDrawingAsync();
        Assert.True(inserted.Succeeded); Assert.NotEqual(Guid.Empty, inserted.Value);
        var expected = Snapshot(core);
        var layer = inserted.Value;
        Assert.Equal(layer, expected.Content.Block.Id);
        Assert.Equal(NotesBlockKind.Canvas, expected.Content.Block.Kind);
        Assert.Empty(expected.Content.Block.Canvas!.Strokes);
        Assert.Empty(expected.Content.Block.SharedProductivityBundles!);
        Assert.True((await core.SaveAsync()).Succeeded);
        expected = Snapshot(core);
        var engine = Engine();
        var bundle = Bundle(engine, expected);
        Assert.True((await core.AppendSharedDrawingAsync(expected, bundle,
            source => Canonical(engine, expected, source))).Succeeded);
        Assert.False(core.IsDirty);
        var savedRevision = core.DurableRevision;
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(Snapshot(core).Content.Bundles)));
        var stored = await rig.NewRepository().LoadAsync(core.DocumentId!.Value, default);
        Assert.NotNull(stored);
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(Drawing(stored, layer).SharedProductivityBundles!)));

        Assert.True((await core.UndoRichTextAsync()).Value);
        Assert.True(core.IsDirty); Assert.Equal(savedRevision, core.DurableRevision);
        Assert.Empty(Snapshot(core).Content.Bundles);
        Assert.True((await core.SaveAsync()).Succeeded);
        var undone = await rig.NewRepository().LoadAsync(core.DocumentId.Value, default);
        Assert.Equal(savedRevision + 1, undone!.Version);
        Assert.Empty(Drawing(undone, layer).SharedProductivityBundles!);
        Assert.True((await core.RedoRichTextAsync()).Value);
        Assert.True((await core.SaveAsync()).Succeeded);
        Assert.Equal(savedRevision + 2, core.DurableRevision);

        await using var reopened = new BoardsBrowserCore(rig.NewRepository());
        Assert.True((await reopened.OpenAsync(core.DocumentId.Value)).Succeeded);
        reopened.ConfigureSharedInk(true);
        Assert.True((await reopened.SelectSharedDrawingAsync(layer)).Succeeded);
        var restored = Snapshot(reopened);
        Assert.Equal(layer, restored.Content.Block.Id);
        Assert.Equal(core.DocumentId, restored.Content.Document.Id);
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(restored.Content.Bundles)));
        var parsed = engine.ParseBundle(bundle.GetRawText());
        Assert.Equal(parsed.Objects[0].ObjectId,
            engine.ParseBundle(Assert.Single(restored.Content.Bundles).GetRawText()).Objects[0].ObjectId);
        Assert.Contains("ownerOptional", Assert.Single(restored.Content.Bundles).GetRawText());
        Assert.Contains("asset:retained-reference", Assert.Single(restored.Content.Bundles).GetRawText());
    }

    [Fact]
    public async Task View_mode_and_missing_capability_refuse_mutation_while_both_layout_modes_accept_ink()
    {
        await using var rig = new Rig();
        await using var core = new BoardsBrowserCore(rig.Repository);
        Assert.True((await core.CreateAsync()).Succeeded);
        Assert.False((await core.InsertSharedDrawingAsync()).Succeeded);
        core.ConfigureSharedInk(true);
        var inserted = await core.InsertSharedDrawingAsync(); Assert.True(inserted.Succeeded);
        var layer = inserted.Value; var page = core.ActivePageId!.Value;
        Assert.True((await core.SaveAsync()).Succeeded);
        var engine = Engine();
        foreach (var layout in new[] { BoardsPageLayoutMode.Locked, BoardsPageLayoutMode.Unlocked })
        {
            Assert.True((await core.SetLayoutModeAsync(page, layout)).Succeeded);
            Assert.True((await core.SelectSharedDrawingAsync(layer)).Succeeded);
            var expected = Snapshot(core); var bundle = Bundle(engine, expected);
            Assert.True((await core.AppendSharedDrawingAsync(expected, bundle,
                source => Canonical(engine, expected, source))).Succeeded);
        }
        Assert.Equal(2, Snapshot(core).Content.Bundles.Count);
        Assert.True((await core.SetEditModeAsync(page, BoardsPageEditMode.View)).Succeeded);
        Assert.True((await core.SelectSharedDrawingAsync(layer)).Succeeded);
        var view = Snapshot(core); var original = JsonSerializer.Serialize(core.GetDocumentSnapshot());
        var called = false;
        Assert.False((await core.AppendSharedDrawingAsync(view, Bundle(engine, view), source => { called = true; return source; })).Succeeded);
        Assert.False((await core.InsertSharedDrawingAsync()).Succeeded);
        Assert.False(called); Assert.Equal(original, JsonSerializer.Serialize(core.GetDocumentSnapshot()));
        Assert.True((await core.SetEditModeAsync(page, BoardsPageEditMode.Edit)).Succeeded);
        Assert.True((await core.SelectSharedDrawingAsync(layer)).Succeeded);
        var capable = Snapshot(core);
        core.ConfigureSharedInk(false);
        Assert.False((await core.AppendSharedDrawingAsync(capable, Bundle(engine, capable), source => { called = true; return source; })).Succeeded);
        Assert.False(called);
        Assert.True((await core.SaveAsync()).Succeeded);
    }

    [Fact]
    public async Task Restored_selection_foreign_owner_and_page_switch_reject_old_strokes_before_validation()
    {
        await using var rig = new Rig();
        await using var core = new BoardsBrowserCore(rig.Repository);
        Assert.True((await core.CreateAsync()).Succeeded); core.ConfigureSharedInk(true);
        var inserted = await core.InsertSharedDrawingAsync(); Assert.True(inserted.Succeeded);
        var layer = inserted.Value; Assert.True((await core.SaveAsync()).Succeeded);
        var expected = Snapshot(core); var engine = Engine(); var bundle = Bundle(engine, expected);
        var paragraph = expected.Content.Owner.Blocks().First(block => block.Kind == NotesBlockKind.Paragraph);
        Assert.True((await core.SelectRichTextAsync(paragraph.Id, 0, 0)).Succeeded);
        Assert.True((await core.SelectRichTextAsync(layer, 0, 0)).Succeeded);
        var called = false;
        JsonElement Validate(JsonElement source) { called = true; return source; }
        Assert.False((await core.AppendSharedDrawingAsync(expected, bundle, Validate)).Succeeded);
        Assert.False(called); Assert.Empty(Snapshot(core).Content.Bundles);

        await using var foreign = new BoardsBrowserCore(rig.NewRepository());
        Assert.True((await foreign.OpenAsync(core.DocumentId!.Value)).Succeeded); foreign.ConfigureSharedInk(true);
        Assert.True((await foreign.SelectSharedDrawingAsync(layer)).Succeeded);
        Assert.False((await foreign.AppendSharedDrawingAsync(Snapshot(core), bundle, Validate)).Succeeded);
        Assert.False(called); Assert.Empty(Snapshot(foreign).Content.Bundles);
        expected = Snapshot(core);
        var document = core.GetDocumentSnapshot()!;
        var second = await core.AddPageAsync(document.Sections[0].Id, "Second page"); Assert.True(second.Succeeded);
        Assert.True((await core.SelectPageAsync(second.Value)).Succeeded);
        Assert.False((await core.AppendSharedDrawingAsync(expected, bundle, Validate)).Succeeded);
        Assert.False(called);
        Assert.True((await core.SaveAsync()).Succeeded);
    }

    [Fact]
    public async Task Reentrant_canonical_preparation_preserves_unrelated_edit_and_rejects_stale_ink()
    {
        await using var rig = new Rig();
        await using var core = new BoardsBrowserCore(rig.Repository);
        Assert.True((await core.CreateAsync()).Succeeded); core.ConfigureSharedInk(true);
        var inserted = await core.InsertSharedDrawingAsync(); Assert.True(inserted.Succeeded);
        Assert.True((await core.SaveAsync()).Succeeded);
        var expected = Snapshot(core); var engine = Engine(); var bundle = Bundle(engine, expected);
        var paragraph = expected.Content.Owner.Blocks().First(block => block.Kind == NotesBlockKind.Paragraph);
        var result = await core.AppendSharedDrawingAsync(expected, bundle, source =>
        {
            var canonical = Canonical(engine, expected, source);
            expected.Content.Owner.SelectBlock(paragraph.Id);
            Assert.True(expected.Content.Owner.InsertDocumentText("reentrant"));
            return canonical;
        });
        Assert.False(result.Succeeded); Assert.Equal("StaleDrawing", result.Code);
        var draft = core.GetDocumentSnapshot()!;
        Assert.Empty(Drawing(draft, inserted.Value).SharedProductivityBundles!);
        Assert.Contains("reentrant", string.Concat(draft.Sections.SelectMany(section => section.Pages)
            .SelectMany(page => page.Blocks).Single(block => block.Id == paragraph.Id).Runs.Select(run => run.Text)));
        Assert.True(core.IsDirty); Assert.True((await core.SaveAsync()).Succeeded);
    }

    [Fact]
    public async Task Real_repository_conflict_retains_undoable_local_stroke_and_exact_winning_bytes()
    {
        await using var rig = new Rig();
        // A conflicted core intentionally remains a preserved dirty owner; Dispose must refuse it.
        var core = new BoardsBrowserCore(rig.Repository);
        Assert.True((await core.CreateAsync("Original")).Succeeded); core.ConfigureSharedInk(true);
        var inserted = await core.InsertSharedDrawingAsync(); Assert.True(inserted.Succeeded);
        Assert.True((await core.SaveAsync()).Succeeded);
        var expected = Snapshot(core); var engine = Engine(); var bundle = Bundle(engine, expected);
        var otherRepository = rig.NewRepository();
        var winner = await otherRepository.LoadAsync(core.DocumentId!.Value, default);
        Assert.NotNull(winner); winner.Title = "External winner";
        var receipt = await otherRepository.SaveAsync(winner, "Concurrent canonical edit", default);
        var winningBytes = await File.ReadAllBytesAsync(receipt.CurrentPath);
        var rejected = await core.AppendSharedDrawingAsync(expected, bundle, source => Canonical(engine, expected, source));
        Assert.False(rejected.Succeeded); Assert.Equal("RevisionConflict", rejected.Code);
        Assert.True(core.IsDirty); Assert.False(core.HasUnknownCommitOutcome);
        Assert.Equal(expected.Content.Version, core.DurableRevision);
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(Snapshot(core).Content.Bundles)));
        Assert.Equal(winningBytes, await File.ReadAllBytesAsync(receipt.CurrentPath));
        var actualWinner = await otherRepository.LoadAsync(winner.Id, default);
        Assert.Equal("External winner", actualWinner!.Title);
        Assert.Empty(Drawing(actualWinner, inserted.Value).SharedProductivityBundles!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => core.DisposeAsync().AsTask());
        Assert.True((await core.UndoRichTextAsync()).Value);
        Assert.Empty(Snapshot(core).Content.Bundles); Assert.True(core.IsDirty);
        Assert.Equal(winningBytes, await File.ReadAllBytesAsync(receipt.CurrentPath));
    }

    private static BoardsSharedInkSnapshot Snapshot(BoardsBrowserCore core) =>
        core.CaptureSharedDrawing() ?? throw new InvalidOperationException("Select the current shared drawing.");
    private static NotesBlock Drawing(NotesDocument document, Guid id) => document.Sections
        .SelectMany(section => section.Pages).SelectMany(page => page.Blocks).Single(block => block.Id == id);
    private static HomeProductivityEngine Engine() => new(handlers: [new CanvasInkObjectHandler()]);
    private static HomeProductivityContext Context(BoardsSharedInkSnapshot expected) => new("boards",
        expected.Content.Document.Id.ToString("D"), expected.Content.Document.Version, [],
        new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
    private static JsonElement Canonical(HomeProductivityEngine engine, BoardsSharedInkSnapshot expected, JsonElement source) =>
        CanvasSharedInkStrokeFactory.CaptureBundle(engine, Context(expected), source);
    private static JsonElement Bundle(HomeProductivityEngine engine, BoardsSharedInkSnapshot expected)
    {
        var id = Guid.NewGuid();
        var stroke = new CanvasInkStroke { StrokeId = id, LayerId = expected.Content.Block.Id, RevisionId = Guid.NewGuid(),
            Samples = [new(10, 20, .4), new(35, 44, .8)],
            ResolvedBrushProperties = new CanvasBrushProperties { EngineParameters = new()
                { ["brushStyle"] = JsonSerializer.SerializeToElement("solid") } },
            ExtensionData = new() { ["ownerOptional"] = JsonSerializer.SerializeToElement(new { value = "retain", version = 7 }) } };
        var value = engine.CreateObject("drawing.ink", id, JsonSerializer.SerializeToElement(stroke)) with
            { Accessibility = JsonSerializer.SerializeToElement(new { label = "Owner annotation" }), AssetReferences = ["asset:retained-reference"] };
        var bundle = engine.SerializeSelection(Context(expected) with { SelectedObjectIds = [id] }, [value]);
        using var json = JsonDocument.Parse(engine.SerializeBundle(bundle));
        return Canonical(engine, expected, json.RootElement);
    }

    private sealed class Rig : IAsyncDisposable, IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "9to1-boards-shared-ink-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "fixture.sqlite");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        private readonly ProductionDiagnostics _diagnostics;
        internal NotesRepository Repository { get; }
        internal Rig() { Directory.CreateDirectory(DataDirectory); _diagnostics = new(this); Repository = NewRepository(); }
        internal NotesRepository NewRepository() => new(this, new NotesDocumentValidator(), _diagnostics);
        public async ValueTask DisposeAsync()
        { await _diagnostics.DisposeAsync(); if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
