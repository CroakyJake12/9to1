using Avalonia;
using Avalonia.Headless;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;
using NineToOne.Web.Productivity.Boards;

namespace Haven.Desktop.Tests;

/// <summary>Focused executable smoke cases over the actual shared Boards core, canonical
/// Notes filesystem repository and registered Home drawing handler. Explicit genuine
/// donor/input cases require Rnote and raster prerequisites; none establish original
/// Home permission composition, native product acceptance or release readiness.</summary>
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

    [Fact]
    public async Task Page_layer_append_extent_undo_redo_and_independent_reopen_preserve_block_and_stroke_identity()
    {
        await using var rig = new Rig(); await using var core = new BoardsBrowserCore(rig.Repository);
        Assert.True((await core.CreateAsync("Page ink")).Succeeded); core.ConfigureSharedInk(true);
        var initial = PageSnapshot(core); var pageId = initial.Content.Page.Id;
        var blockIds = initial.Content.Page.Blocks.Select(block => block.Id).ToArray();
        Assert.Null(initial.Content.Layer); Assert.Equal(initial.Content.LayerId, PageSnapshot(core).Content.LayerId);
        var engine = Engine(); var bundle = PageBundle(engine, initial);
        Assert.True((await core.ReplacePageSharedDrawingAsync(initial, [bundle], source => CanonicalPage(engine, initial, source),
            width: 1400, height: 1800)).Succeeded);
        var saved = PageSnapshot(core); Assert.False(core.IsDirty);
        Assert.Equal(blockIds, saved.Content.Page.Blocks.Select(block => block.Id));
        Assert.Equal(1400, saved.Content.Page.CanvasWidth); Assert.Equal(1800, saved.Content.Page.CanvasHeight);
        Assert.True(saved.Content.Layer!.Locked); Assert.Equal(0, saved.Content.Layer.X); Assert.Equal(0, saved.Content.Layer.Y);
        var layerId = saved.Content.LayerId;
        var loaded = (await rig.NewRepository().LoadAsync(core.DocumentId!.Value, default))!;
        // Optional Root evidence export: all assertions still execute without it.
        // Frozen old-writer probe requires these exact new-produced canonical bytes.
        var compatibilityOutput = Environment.GetEnvironmentVariable("NINETOONE_BOARDS_OLD_WRITER_FIXTURE_OUTPUT");
        if (!string.IsNullOrWhiteSpace(compatibilityOutput))
        {
            Assert.True(Path.IsPathFullyQualified(compatibilityOutput)); Assert.False(File.Exists(compatibilityOutput));
            File.Copy(core.LastAcknowledgedSave!.CurrentPath, compatibilityOutput, overwrite: false);
        }
        var stored = loaded.Sections.SelectMany(section => section.Pages).Single(page => page.Id == pageId);
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(NotesSharedInkLayer.Read(loaded, stored.Id)!.Bundles)));
        Assert.True((await core.UndoRichTextAsync()).Value); Assert.True(core.IsDirty);
        Assert.Null(PageSnapshot(core).Content.Layer); Assert.Equal(900, PageSnapshot(core).Content.Page.CanvasHeight);
        Assert.True((await core.SaveAsync()).Succeeded);
        Assert.True((await core.RedoRichTextAsync()).Value); Assert.True((await core.SaveAsync()).Succeeded);
        await using var reopened = new BoardsBrowserCore(rig.NewRepository());
        Assert.True((await reopened.OpenAsync(core.DocumentId.Value)).Succeeded); reopened.ConfigureSharedInk(true);
        var restored = PageSnapshot(reopened);
        Assert.Equal(layerId, restored.Content.LayerId); Assert.Equal(pageId, restored.Content.Page.Id);
        Assert.Equal(1800, restored.Content.Height); Assert.Equal(blockIds, restored.Content.Page.Blocks.Select(block => block.Id));
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(restored.Content.Bundles)));
    }

    [Fact]
    public async Task Page_ink_works_in_both_layout_modes_but_View_and_cancellation_deny_mutations()
    {
        await using var rig = new Rig(); await using var core = new BoardsBrowserCore(rig.Repository);
        Assert.True((await core.CreateAsync()).Succeeded); core.ConfigureSharedInk(true); var engine = Engine();
        foreach (var layout in new[] { BoardsPageLayoutMode.Locked, BoardsPageLayoutMode.Unlocked })
        {
            Assert.True((await core.SetLayoutModeAsync(core.ActivePageId!.Value, layout)).Succeeded);
            var expected = PageSnapshot(core); var next = PageBundle(engine, expected);
            Assert.True((await core.ReplacePageSharedDrawingAsync(expected, [.. expected.Content.Bundles, next],
                source => CanonicalPage(engine, expected, source))).Succeeded);
        }
        Assert.Equal(2, PageSnapshot(core).Content.Bundles.Count);
        Assert.True((await core.SetEditModeAsync(core.ActivePageId!.Value, BoardsPageEditMode.View)).Succeeded);
        var view = PageSnapshot(core); var before = JsonSerializer.Serialize(core.GetDocumentSnapshot()); var called = false;
        Assert.False((await core.ReplacePageSharedDrawingAsync(view, [], values => { called = true; return values; })).Succeeded);
        Assert.False(called); Assert.Equal(before, JsonSerializer.Serialize(core.GetDocumentSnapshot()));
        Assert.True((await core.SetEditModeAsync(core.ActivePageId.Value, BoardsPageEditMode.Edit)).Succeeded);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => core.ReplacePageSharedDrawingAsync(PageSnapshot(core), [],
            values => { called = true; return values; }, stop.Token));
        Assert.False(called); Assert.Equal(2, PageSnapshot(core).Content.Bundles.Count);
        Assert.True((await core.SaveAsync()).Succeeded);
    }

    [Fact]
    public async Task Page_erase_is_one_original_undo_boundary_and_keeps_full_surviving_bundle()
    {
        await using var rig = new Rig(); await using var core = new BoardsBrowserCore(rig.Repository);
        Assert.True((await core.CreateAsync()).Succeeded); core.ConfigureSharedInk(true); var engine = Engine();
        var initial = PageSnapshot(core); var first = PageBundle(engine, initial); var second = PageBundle(engine, initial, 220);
        Assert.True((await core.ReplacePageSharedDrawingAsync(initial, [first, second], values => CanonicalPage(engine, initial, values))).Succeeded);
        var expected = PageSnapshot(core); var layer = expected.Content.LayerId;
        Assert.True((await core.ReplacePageSharedDrawingAsync(expected, [second], values => CanonicalPage(engine, expected, values))).Succeeded);
        Assert.True(JsonElement.DeepEquals(second, Assert.Single(PageSnapshot(core).Content.Bundles)));
        Assert.True((await core.UndoRichTextAsync()).Value); Assert.Equal(2, PageSnapshot(core).Content.Bundles.Count);
        Assert.Equal(layer, PageSnapshot(core).Content.LayerId); Assert.True((await core.SaveAsync()).Succeeded);
        Assert.True((await core.RedoRichTextAsync()).Value); Assert.True((await core.SaveAsync()).Succeeded);
        var stored = (await rig.NewRepository().LoadAsync(core.DocumentId!.Value, default))!;
        var retained = NotesSharedInkLayer.Read(stored, Assert.Single(stored.Sections.SelectMany(section => section.Pages)).Id)!;
        Assert.True(JsonElement.DeepEquals(second, Assert.Single(retained.Bundles)));
    }

    [Fact]
    public async Task Page_capture_rejects_returned_selection_page_and_reentrant_owner_changes()
    {
        await using var rig = new Rig(); await using var core = new BoardsBrowserCore(rig.Repository);
        Assert.True((await core.CreateAsync()).Succeeded); core.ConfigureSharedInk(true); var engine = Engine();
        var old = PageSnapshot(core); var original = old.Content.Page.Blocks[0];
        Assert.True((await core.SelectRichTextAsync(original.Id, 0, 0)).Succeeded);
        var called = false;
        Assert.False((await core.ReplacePageSharedDrawingAsync(old, [PageBundle(engine, old)], values => { called = true; return values; })).Succeeded);
        Assert.False(called);
        var current = PageSnapshot(core); var bundle = PageBundle(engine, current);
        var reentrant = await core.ReplacePageSharedDrawingAsync(current, [bundle], values =>
        {
            var captured = CanonicalPage(engine, current, values);
            current.Content.Owner.InsertDocumentText("same original draft"); return captured;
        });
        Assert.False(reentrant.Succeeded); Assert.Null(PageSnapshot(core).Content.Layer); Assert.True(core.IsDirty);
        Assert.Contains("same original draft", core.NativeRichEditor!.Blocks().First().PlainText);
        Assert.True((await core.SaveAsync()).Succeeded);
        var expected = PageSnapshot(core); var doc = core.GetDocumentSnapshot()!;
        var second = await core.AddPageAsync(doc.Sections[0].Id, "Next page"); Assert.True(second.Succeeded);
        Assert.True((await core.SelectPageAsync(second.Value)).Succeeded);
        Assert.False((await core.ReplacePageSharedDrawingAsync(expected, [bundle], values => { called = true; return values; })).Succeeded);
        Assert.False(called); Assert.True((await core.SaveAsync()).Succeeded);
    }

    [Fact]
    public void Page_layer_envelope_preserves_unknown_fields_and_refuses_future_or_malformed_version()
    {
        var page = Guid.NewGuid(); var layer = Guid.NewGuid();
        var raw = System.Text.Json.Nodes.JsonNode.Parse(NotesSharedInkLayer.Create(page, layer, []).ToJson().GetRawText())!.AsObject();
        raw["ownerOptional"] = System.Text.Json.Nodes.JsonNode.Parse("{\"keep\":[1,2,3]}");
        var captured = NotesSharedInkLayer.Capture(JsonSerializer.SerializeToElement(raw));
        var document = NotesDocument.Create(); var pageValue = document.Sections[0].Pages[0]; pageValue.Id = page;
        pageValue.CanvasObjects.Add(new NotesCanvasObject { Id = layer, Kind = NotesCanvasObjectKind.Ink, Locked = true });
        var metadata = captured.ToJson().GetRawText(); document.Metadata[NotesSharedInkLayer.MetadataKey(page)] = metadata;
        var roundTrip = JsonSerializer.Deserialize<NotesDocument>(JsonSerializer.Serialize(document))!;
        Assert.Equal(metadata, roundTrip.Metadata[NotesSharedInkLayer.MetadataKey(page)]);
        var parsed = NotesSharedInkLayer.Read(roundTrip, page)!;
        Assert.Equal(page, parsed.PageId); Assert.Equal(layer, parsed.LayerId);
        Assert.True(JsonElement.DeepEquals(captured.ToJson(), parsed.ToJson()));
        Assert.True(parsed.WithBundles([]).ToJson().TryGetProperty("ownerOptional", out _));
        raw["version"] = 2;
        Assert.Throws<InvalidDataException>(() => NotesSharedInkLayer.Capture(JsonSerializer.SerializeToElement(raw)));
        raw["version"] = "1";
        Assert.Throws<InvalidDataException>(() => NotesSharedInkLayer.Capture(JsonSerializer.SerializeToElement(raw)));
    }

    [Fact]
    public async Task Real_repository_conflict_retains_undoable_page_erase_and_exact_winning_bytes()
    {
        await using var rig = new Rig(); var core = new BoardsBrowserCore(rig.Repository);
        Assert.True((await core.CreateAsync()).Succeeded); core.ConfigureSharedInk(true); var engine = Engine();
        var initial = PageSnapshot(core); var bundle = PageBundle(engine, initial);
        Assert.True((await core.ReplacePageSharedDrawingAsync(initial, [bundle], values => CanonicalPage(engine, initial, values))).Succeeded);
        var expected = PageSnapshot(core); var other = rig.NewRepository(); var winner = (await other.LoadAsync(core.DocumentId!.Value, default))!;
        winner.Title = "Concurrent winner"; var receipt = await other.SaveAsync(winner, "Concurrent edit", default);
        var bytes = await File.ReadAllBytesAsync(receipt.CurrentPath);
        var refused = await core.ReplacePageSharedDrawingAsync(expected, [], values => values);
        Assert.False(refused.Succeeded); Assert.Equal("RevisionConflict", refused.Code); Assert.True(core.IsDirty);
        Assert.Empty(PageSnapshot(core).Content.Bundles); Assert.Equal(bytes, await File.ReadAllBytesAsync(receipt.CurrentPath));
        await Assert.ThrowsAsync<InvalidOperationException>(() => core.DisposeAsync().AsTask());
        Assert.True((await core.UndoRichTextAsync()).Value);
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(PageSnapshot(core).Content.Bundles)));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(receipt.CurrentPath));
    }

    [Fact]
    public void Genuine_Rnote_whole_eraser_uses_saved_geometry_and_keeps_exact_nonintersecting_object_identity()
    {
        var engine = Engine(); var context = new HomeProductivityContext("boards", Guid.NewGuid().ToString("D"), 1, [],
            new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" }); var layer = Guid.NewGuid();
        var first = CanvasSharedInkStrokeFactory.Capture(engine, context, layer, [new(40, 40, .4), new(160, 40, .8)]);
        var second = CanvasSharedInkStrokeFactory.Capture(engine, context, layer, [new(40, 220, .4), new(160, 220, .8)]);
        var owner = new CanvasSharedInkOwnerSnapshot(context, layer, "original-page", [first, second]);
        var erased = CanvasSharedInkStrokeFactory.CaptureWholeErase(engine, owner, [new(100, 20, .5), new(100, 60, .5)], 20);
        Assert.NotNull(erased.Erasure); Assert.True(JsonElement.DeepEquals(second, Assert.Single(erased.ReplacementBundles!)));
        Assert.Equal(engine.ParseBundle(second.GetRawText()).Objects[0].ObjectId,
            engine.ParseBundle(Assert.Single(erased.ReplacementBundles!).GetRawText()).Objects[0].ObjectId);
        var again = CanvasSharedInkStrokeFactory.CaptureWholeErase(engine, owner, erased.Erasure.Samples, erased.Erasure.Width);
        Assert.True(JsonElement.DeepEquals(Assert.Single(erased.ReplacementBundles!), Assert.Single(again.ReplacementBundles!)));
        var originals = engine.ParseBundle(first.GetRawText()).Objects.Concat(engine.ParseBundle(second.GetRawText()).Objects).ToArray();
        var grouped = engine.SerializeSelection(context with { SelectedObjectIds = originals.Select(value => value.ObjectId).ToArray() }, originals) with
        { AssetReferences = ["asset:retained-group-reference"], Accessibility = JsonSerializer.SerializeToElement(new { label = "Original drawing group" }) };
        using var document = JsonDocument.Parse(engine.SerializeBundle(grouped));
        var groupedOwner = owner with { Bundles = [document.RootElement.Clone()] };
        var groupedErase = CanvasSharedInkStrokeFactory.CaptureWholeErase(engine, groupedOwner, erased.Erasure.Samples, erased.Erasure.Width);
        var survivor = engine.ParseBundle(Assert.Single(groupedErase.ReplacementBundles!).GetRawText());
        Assert.Equal(grouped.AssetReferences, survivor.AssetReferences);
        Assert.True(JsonElement.DeepEquals(grouped.Accessibility, survivor.Accessibility));
        Assert.Equal(originals[1].ObjectId, Assert.Single(survivor.Objects).ObjectId);
        Assert.True(JsonElement.DeepEquals(grouped.Objects[1].Content, survivor.Objects[0].Content));
        Assert.NotEqual(grouped.PortableFallback.GetRawText(), survivor.PortableFallback.GetRawText());
    }

    [Fact]
    public async Task Lost_real_save_acknowledgement_fences_page_ink_replay_and_preserves_actual_saved_bundle()
    {
        await using var rig = new Rig(); var repository = new LostPageAcknowledgementRepository(rig.Repository);
        var core = new BoardsBrowserCore(repository, unknownOutcome: error => error is LostPageAcknowledgement);
        Assert.True((await core.CreateAsync()).Succeeded); core.ConfigureSharedInk(true);
        var expected = PageSnapshot(core); var engine = Engine(); var bundle = PageBundle(engine, expected);
        repository.LoseNext = true;
        var result = await core.ReplacePageSharedDrawingAsync(expected, [bundle], values => CanonicalPage(engine, expected, values));
        Assert.False(result.Succeeded); Assert.Equal("CommitOutcomeUnknown", result.Code); Assert.True(core.IsDirty);
        Assert.True(core.HasUnknownCommitOutcome); Assert.Null(core.CapturePageSharedDrawing());
        var saved = (await rig.NewRepository().LoadAsync(core.DocumentId!.Value, default))!;
        var layer = NotesSharedInkLayer.Read(saved, Assert.Single(saved.Sections.SelectMany(section => section.Pages)).Id)!;
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(layer.Bundles)));
        var count = repository.SaveCount; Assert.False((await core.SaveAsync()).Succeeded); Assert.Equal(count, repository.SaveCount);
        var called = false;
        Assert.False((await core.ReplacePageSharedDrawingAsync(expected, [bundle], values => { called = true; return values; })).Succeeded);
        Assert.False(called); Assert.Equal(count, repository.SaveCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => core.DisposeAsync().AsTask());
        // This deliberately retained unknown owner is not certified closed by a test fixture.
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task Genuine_page_input_keeps_document_units_cancels_changed_viewport_and_joins_its_actual_held_submission()
    {
        var engine = Engine(); var layer = Guid.NewGuid();
        var context = new HomeProductivityContext("boards", Guid.NewGuid().ToString("D"), 1, [], new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
        var owner = new CanvasSharedInkOwnerSnapshot(context, layer, "retained-original", []) { LogicalWidth = 1200, LogicalHeight = 900 };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CanvasSharedInkStrokeRequest? captured = null;
        async ValueTask<CanvasSharedInkCommitResult> Observe(CanvasSharedInkStrokeRequest request, CancellationToken token)
        {
            captured = request; entered.TrySetResult(); await release.Task.WaitAsync(token);
            return new(true, "ObservedHostBoundary", "The actual held host callback settled; no artifact success is asserted here.");
        }
        var input = new CanvasSharedInkInput(engine, () => owner, Observe) { DocumentCoordinates = true, EraserEnabled = true };
        var window = new Avalonia.Controls.Window { Width = 800, Height = 600, Content = input };
        var failures = new List<Exception>();
        try
        {
            window.Show(); window.UpdateLayout(); Assert.True(input.Refresh());
            Avalonia.Point At(double x, double y) => input.TranslatePoint(new(x, y), window)!.Value;
            window.MouseDown(At(40, 40), Avalonia.Input.MouseButton.Left);
            window.MouseMove(At(60, 60), Avalonia.Input.RawInputModifiers.LeftMouseButton);
            window.Height = 350; window.UpdateLayout();
            window.MouseUp(At(80, 80), Avalonia.Input.MouseButton.Left);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            Assert.Null(captured);
            var canceledOriginal = Assert.Single(input.OriginalSubmissions);
            Assert.True(canceledOriginal.IsCompletedSuccessfully); // Original custody is retained even without an owner mutation.
            var clipped = new Avalonia.Controls.Panel { ClipToBounds = false,
                Clip = new Avalonia.Media.RectangleGeometry(new Rect(0, 0, 80, 80)) };
            window.Content = null; clipped.Children.Add(input); window.Content = clipped; window.UpdateLayout(); Assert.True(input.Refresh());
            window.MouseDown(At(40, 40), Avalonia.Input.MouseButton.Left);
            window.MouseMove(At(90, 90), Avalonia.Input.RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(90, 90), Avalonia.Input.MouseButton.Left);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            Assert.Null(captured); Assert.Same(canceledOriginal, Assert.Single(input.OriginalSubmissions));
            clipped.Clip = new Avalonia.Media.RectangleGeometry(new Rect(0, 0, 200, 200));
            Assert.True(input.Refresh());
            window.MouseDown(At(40, 40), Avalonia.Input.MouseButton.Left);
            window.MouseMove(At(60, 60), Avalonia.Input.RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(80, 80), Avalonia.Input.MouseButton.Left);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var request = Assert.IsType<CanvasSharedInkStrokeRequest>(captured);
            var stroke = Assert.Single(engine.ParseBundle(request.Bundle.GetRawText()).Objects).Content.Deserialize<CanvasInkStroke>()!;
            Assert.Equal(40, stroke.Samples[0].X); Assert.Equal(40, stroke.Samples[0].Y);
            Assert.Equal(80, stroke.Samples[^1].X); Assert.Equal(80, stroke.Samples[^1].Y);
            Assert.Equal(2, input.OriginalSubmissions.Count); Assert.Same(canceledOriginal, input.OriginalSubmissions[0]);
            var original = input.OriginalSubmissions[1]; Assert.Same(original, input.PendingSubmission); Assert.False(original.IsCompleted);
            var close = input.CloseAndDrainAsync(); Assert.False(close.IsCompleted);
            release.TrySetResult(); await close.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(original.IsCompletedSuccessfully); Assert.Same(close, input.CloseAndDrainAsync());
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            release.TrySetResult();
            try { await input.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception failure) { failures.Add(failure); }
            try { window.Content = null; window.Close(); } catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Page-input assertion and original cleanup failed.", failures);
    }

    private sealed class LostPageAcknowledgement : IOException { }
    private sealed class LostPageAcknowledgementRepository(INotesRepository original) : INotesRepository
    {
        internal bool LoseNext; internal int SaveCount;
        public async Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken token)
        {
            ++SaveCount; var receipt = await original.SaveAsync(document, reason, token);
            if (LoseNext) { LoseNext = false; throw new LostPageAcknowledgement(); }
            return receipt;
        }
        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token) => original.ListAsync(token);
        public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token) => original.LoadAsync(id, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => original.DeleteAsync(id, token);
        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token) => original.GetVersionsAsync(id, token);
        public Task<NotesDocument?> LoadVersionAsync(Guid id, string version, CancellationToken token) => original.LoadVersionAsync(id, version, token);
        public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) => original.RecoverLatestAsync(id, token);
        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) => original.SearchAsync(query, token);
    }

    [Fact]
    public async Task Incompatible_retained_page_layer_diagnostic_does_not_replace_genuine_text_or_save_outcome()
    {
        await using var rig = new Rig(); await using var core = new BoardsBrowserCore(rig.Repository);
        Assert.True((await core.CreateAsync()).Succeeded); core.ConfigureSharedInk(true);
        var owner = core.NativeRichEditor!; var page = owner.Document.Sections.SelectMany(section => section.Pages).Single();
        var id = Guid.NewGuid();
        page.CanvasObjects.Add(new NotesCanvasObject { Id = id, Kind = NotesCanvasObjectKind.Ink, Locked = true });
        // A future page extension is an opaque KNOWN metadata string to old writers;
        // only affected drawing is unavailable, while ordinary text/save preserves it.
        var raw = NotesSharedInkLayer.Create(page.Id, id, []).ToJson().GetRawText().Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal);
        owner.Document.Metadata[NotesSharedInkLayer.MetadataKey(page.Id)] = raw;
        core.PropertyChanged += (sender, args) => { core.CapturePageSharedDrawing(); };
        Assert.Null(core.CapturePageSharedDrawing()); Assert.NotNull(core.PageInkDiagnostic); Assert.Null(core.ErrorCode);
        Assert.True((await core.InsertRichTextAsync("Unrelated valid text")).Succeeded);
        Assert.Null(core.ErrorCode); Assert.True(core.IsDirty);
        Assert.True((await core.SaveAsync()).Succeeded); Assert.Null(core.ErrorCode); Assert.False(core.IsDirty); Assert.Equal("Saved", core.Status);
        var stored = (await rig.NewRepository().LoadAsync(core.DocumentId!.Value, default))!;
        Assert.Single(Assert.Single(stored.Sections.SelectMany(section => section.Pages)).CanvasObjects);
        Assert.Equal(raw, stored.Metadata[NotesSharedInkLayer.MetadataKey(page.Id)]);
        Assert.Null(core.CapturePageSharedDrawing()); Assert.NotNull(core.PageInkDiagnostic);
    }

    [Fact]
    public async Task Unchanged_schema1_writer_clone_migration_repository_and_package_paths_preserve_opaque_page_extension()
    {
        await using var rig = new Rig(); await using var core = new BoardsBrowserCore(rig.Repository);
        Assert.True((await core.CreateAsync()).Succeeded); core.ConfigureSharedInk(true); var engine = Engine();
        var expected = PageSnapshot(core); var bundle = PageBundle(engine, expected);
        Assert.True((await core.ReplacePageSharedDrawingAsync(expected, [bundle], values => CanonicalPage(engine, expected, values))).Succeeded);
        Assert.Null(typeof(NotesCanvasObject).GetProperty("SharedInkLayer")); // No new unknown DTO property for old writers to drop.
        var id = core.DocumentId!.Value; var pageId = core.ActivePageId!.Value; var key = NotesSharedInkLayer.MetadataKey(pageId);
        var oldKnownDto = (await rig.NewRepository().LoadAsync(id, default))!;
        var raw = oldKnownDto.Metadata[key]; var layerId = NotesSharedInkLayer.Read(oldKnownDto, pageId)!.LayerId;
        var oldWriter = new WriteDocumentEditor(oldKnownDto, pageId);
        Assert.True(oldWriter.InsertDocumentText("ordinary old editor edit")); Assert.True(oldWriter.Undo()); Assert.Equal(raw, oldKnownDto.Metadata[key]);
        Assert.True(oldWriter.Redo()); Assert.Equal(raw, oldKnownDto.Metadata[key]);
        await rig.NewRepository().SaveAsync(oldKnownDto, "Known schema1 writer path", default);
        var independentlyLoaded = (await rig.NewRepository().LoadAsync(id, default))!;
        Assert.Equal(1, independentlyLoaded.SchemaVersion); Assert.Equal(raw, independentlyLoaded.Metadata[key]);
        Assert.Equal(layerId, Assert.Single(independentlyLoaded.Sections.SelectMany(section => section.Pages).Single(page => page.Id == pageId).CanvasObjects).Id);
        var migrated = new NotesDocumentMigrator().Migrate(independentlyLoaded, 1).Document;
        Assert.Equal(raw, migrated.Metadata[key]); Assert.Equal(1, migrated.SchemaVersion);
        var package = new WriteNativeDocumentPackageStore(); var path = Path.Combine(rig.DataDirectory, "retained.9to1w");
        Assert.True((await package.SaveAsync(migrated, path)).IsSuccess);
        var opened = await package.OpenAsync(path); Assert.True(opened.IsSuccess); Assert.Equal(raw, opened.Value!.Metadata[key]);
        await using var newReader = new BoardsBrowserCore(rig.NewRepository());
        Assert.True((await newReader.OpenAsync(id)).Succeeded); newReader.ConfigureSharedInk(true);
        Assert.Equal(layerId, PageSnapshot(newReader).Content.LayerId);
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(PageSnapshot(newReader).Content.Bundles)));
        // This runs unchanged production paths. OLD65-COMPAT-PROBE separately binds
        // actual frozen old assembly hashes; this test alone is not an old-binary PASS.
    }

    [Fact]
    public void Future_orphan_mismatched_and_malformed_metadata_is_never_reinterpreted_as_an_empty_layer()
    {
        foreach (var failure in new[] { "future", "orphan", "mismatch", "malformed" })
        {
            var document = NotesDocument.Create(); var page = document.Sections[0].Pages[0];
            var layer = Guid.NewGuid(); var raw = NotesSharedInkLayer.Create(failure == "mismatch" ? Guid.NewGuid() : page.Id, layer, []).ToJson().GetRawText();
            if (failure == "future") raw = raw.Replace("\"version\":1", "\"version\":99", StringComparison.Ordinal);
            if (failure == "malformed") raw = "{not valid JSON";
            var key = NotesSharedInkLayer.MetadataKey(page.Id); document.Metadata[key] = raw;
            var editor = new WriteDocumentEditor(document, page.Id);
            var error = Record.Exception(() => editor.CapturePageSharedInk(page.Id));
            Assert.True(error is InvalidDataException or JsonException, "Unsupported extension must report its actual incompatibility.");
            Assert.Equal(raw, document.Metadata[key]); Assert.Empty(page.CanvasObjects); Assert.False(editor.CanUndo);
        }
    }

    [Fact]
    public void Actual_escaped_UTF8_body_budget_refuses_ink_before_history_or_document_mutation()
    {
        var document = NotesDocument.Create(); var page = document.Sections[0].Pages[0];
        // This known opaque legacy metadata exceeds the native body cap after
        // default JSON Unicode escaping; raw character counts alone are unsafe.
        var existing = new string('\u2603', 11 * 1024 * 1024); document.Metadata["existing.owner.payload"] = existing;
        var editor = new WriteDocumentEditor(document, page.Id); var expected = editor.CapturePageSharedInk(page.Id)!;
        Assert.Throws<InvalidDataException>(() => editor.ReplacePageSharedInk(expected, [], values => values));
        Assert.Same(existing, document.Metadata["existing.owner.payload"]); Assert.Empty(page.CanvasObjects);
        Assert.False(document.Metadata.ContainsKey(NotesSharedInkLayer.MetadataKey(page.Id))); Assert.False(editor.CanUndo);
        Assert.Equal(1, document.SchemaVersion);
    }

    [Fact]
    public void Authored_update_preserves_the_actual_single_metadata_key_spelling_in_an_ordinal_dictionary()
    {
        var document = NotesDocument.Create(); document.Metadata = new(document.Metadata, StringComparer.Ordinal);
        var page = document.Sections[0].Pages[0]; var layer = Guid.NewGuid();
        var canonical = NotesSharedInkLayer.MetadataKey(page.Id); var originalKey = canonical.ToUpperInvariant();
        document.Metadata[originalKey] = NotesSharedInkLayer.Create(page.Id, layer, []).ToJson().GetRawText();
        document.Metadata["other.owner"] = "retain exact value";
        page.CanvasObjects.Add(new NotesCanvasObject { Id = layer, Kind = NotesCanvasObjectKind.Ink, Locked = true });
        var editor = new WriteDocumentEditor(document, page.Id); var expected = editor.CapturePageSharedInk(page.Id)!;
        Assert.Equal(originalKey, expected.MetadataKey);
        var originalRaw = document.Metadata[originalKey];
        var changed = System.Text.Json.Nodes.JsonNode.Parse(originalRaw)!.AsObject(); changed["ownerOptional"] = "changed and observed";
        document.Metadata[originalKey] = changed.ToJsonString(); Assert.NotNull(editor.CapturePageSharedInk(page.Id));
        document.Metadata[originalKey] = originalRaw;
        Assert.False(editor.IsPageSharedInkCurrent(expected)); // Observed away-and-back metadata cannot revive a capture.
        foreach (var invalid in new[] { "{not JSON", originalRaw.Replace("\"version\":1", "\"version\":99", StringComparison.Ordinal) })
        {
            expected = editor.CapturePageSharedInk(page.Id)!; document.Metadata[originalKey] = invalid;
            var failure = Record.Exception(() => editor.CapturePageSharedInk(page.Id)); Assert.True(failure is JsonException or InvalidDataException);
            document.Metadata[originalKey] = originalRaw; Assert.False(editor.IsPageSharedInkCurrent(expected));
        }
        expected = editor.CapturePageSharedInk(page.Id)!;
        Assert.True(editor.ReplacePageSharedInk(expected, [], values => values));
        Assert.True(document.Metadata.ContainsKey(originalKey)); Assert.False(document.Metadata.ContainsKey(canonical));
        Assert.Equal(2, document.Metadata.Count); Assert.Equal("retain exact value", document.Metadata["other.owner"]);
        Assert.NotNull(editor.CapturePageSharedInk(page.Id));
    }

    [Fact]
    public void Metadata_for_an_absent_page_remains_raw_and_refuses_document_wide_ink_identity_admission()
    {
        var document = NotesDocument.Create(); var absent = Guid.NewGuid();
        var raw = NotesSharedInkLayer.Create(absent, Guid.NewGuid(), []).ToJson().GetRawText();
        var key = NotesSharedInkLayer.MetadataKey(absent); document.Metadata[key] = raw;
        Assert.Throws<InvalidDataException>(() => NotesSharedInkLayer.ReadAll(document));
        Assert.Equal(raw, document.Metadata[key]);
        Assert.True(new NotesDocumentValidator().Validate(document).IsValid); // Old schema1 writer preserves opaque extension.
    }

    private static BoardsPageSharedInkSnapshot PageSnapshot(BoardsBrowserCore core) =>
        core.CapturePageSharedDrawing() ?? throw new InvalidOperationException("The current page drawing must be available.");
    private static HomeProductivityContext PageContext(BoardsPageSharedInkSnapshot expected) => new("boards", expected.Content.Document.Id.ToString("D"),
        expected.Content.Document.Version, [], new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
    private static IReadOnlyList<JsonElement> CanonicalPage(HomeProductivityEngine engine, BoardsPageSharedInkSnapshot expected, IReadOnlyList<JsonElement> values) =>
        values.Select(value => CanvasSharedInkStrokeFactory.CaptureBundle(engine, PageContext(expected), value)).ToArray();
    private static JsonElement PageBundle(HomeProductivityEngine engine, BoardsPageSharedInkSnapshot expected, double y = 40)
    {
        var id = Guid.NewGuid();
        var stroke = new CanvasInkStroke { StrokeId = id, LayerId = expected.Content.LayerId, RevisionId = Guid.NewGuid(),
            Samples = [new(10, y, .4, -10, 20), new(50, y + 20, .8, 10, -20)],
            ResolvedBrushProperties = new CanvasBrushProperties { EngineParameters = new() { ["brushStyle"] = JsonSerializer.SerializeToElement("solid") } },
            ExtensionData = new() { ["ownerOptional"] = JsonSerializer.SerializeToElement(new { retained = true }) } };
        var value = engine.CreateObject("drawing.ink", id, JsonSerializer.SerializeToElement(stroke)) with { AssetReferences = ["asset:page-owner-reference"] };
        using var json = JsonDocument.Parse(engine.SerializeBundle(engine.SerializeSelection(PageContext(expected) with { SelectedObjectIds = [id] }, [value])));
        return CanvasSharedInkStrokeFactory.CaptureBundle(engine, PageContext(expected), json.RootElement);
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
