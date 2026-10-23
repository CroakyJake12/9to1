using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Data;
using Haven.Infrastructure;
using Haven.UI;
using Haven.UI.Components;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;
using HavenButton = Haven.UI.Components.Button;

namespace Haven.Desktop.Tests;

public sealed class DataSharedInkTests
{
    [Fact]
    public void Capture_uses_canonical_identity_preserves_optional_object_fields_and_honours_Home_bundle_admission()
    {
        var engine = Engine(); var context = Context(Guid.NewGuid()); var layer = Guid.NewGuid();
        var bundle = CanvasSharedInkStrokeFactory.Capture(engine, context, layer,
            [new(10, 20, .2, -3, 4), new(30, 40, .8, 5, -6)], new(CanvasRnoteInkKind.Marker, "#FF102030", 7, .4));
        var parsed = engine.ParseBundle(bundle.GetRawText()); var value = Assert.Single(parsed.Objects);
        Assert.Equal("drawing.ink", value.ObjectType); Assert.Equal(1, value.SchemaVersion);
        Assert.Equal(value.ObjectId, value.Content.GetProperty("StrokeId").GetGuid());
        Assert.Equal(layer, value.Content.GetProperty("LayerId").GetGuid());
        Assert.Equal(.2, value.Content.GetProperty("Samples")[0].GetProperty("Pressure").GetDouble());
        Assert.Equal("highlighter", value.Content.GetProperty("ToolDefinitionId").GetString());
        var extended = JsonNode.Parse(bundle.GetRawText())!.AsObject();
        extended["objects"]![0]!["futureOptionalObject"] = new JsonObject { ["value"] = 42 };
        extended["objects"]![0]!["content"]!["futureOptionalStroke"] = true;
        var captured = CanvasSharedInkStrokeFactory.CaptureBundle(engine, context, JsonSerializer.SerializeToElement(extended));
        Assert.Equal(42, captured.GetProperty("objects")[0].GetProperty("futureOptionalObject").GetProperty("value").GetInt32());
        Assert.True(captured.GetProperty("objects")[0].GetProperty("content").GetProperty("futureOptionalStroke").GetBoolean());
        // Home deliberately refuses unknown envelope extensions for admission. Retention must not
        // silently turn that refusal into permission or erase the original unsupported payload.
        extended["futureOptionalBundle"] = "retained";
        var unsupported = JsonSerializer.SerializeToElement(extended);
        var error = Assert.Throws<InvalidDataException>(() => CanvasSharedInkStrokeFactory.CaptureBundle(engine, context, unsupported));
        Assert.Contains("BundleExtensionsUnsupported", error.Message);
        Assert.Equal("retained", engine.ParseBundle(unsupported.GetRawText()).Extensions!["futureOptionalBundle"].GetString());
    }

    [Fact]
    public async Task Existing_workbook_repository_saves_and_reopens_drawings_with_cells_queries_and_shape_data()
    {
        using var paths = new PhysicalPaths(); var repository = new DataWorkbookRepository(paths);
        var workbook = DataWorkbook.Create("Ink and data"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "A retained cell"); workbook.Queries[0].Sql = "SELECT A FROM \"Sheet 1\";";
        var editor = new DataSheetDrawingEditor(sheet); editor.AddCustomShape(DocumentVectorShapes.CreateEditableStarter());
        var bundle = Bundle(workbook.Id, sheet.Id); var objectId = Engine().ParseBundle(bundle.GetRawText()).Objects[0].ObjectId;
        editor.AddSharedInk(objectId, bundle);
        await repository.SaveAsync(workbook, "Shared drawing smoke", TestContext.Current.CancellationToken);
        var reopened = Assert.IsType<DataWorkbook>(await repository.LoadAsync(workbook.Id, TestContext.Current.CancellationToken));
        Assert.Equal(workbook.Id, reopened.Id); Assert.Equal(sheet.Id, reopened.Sheets[0].Id);
        Assert.Equal("A retained cell", reopened.Sheets[0].GetCell(0, 0)?.Value);
        Assert.Equal(workbook.Queries[0].Sql, reopened.Queries[0].Sql);
        Assert.Equal(2, reopened.Sheets[0].Drawings.Count); Assert.NotNull(reopened.Sheets[0].Drawings[0].VectorShape);
        var drawing = reopened.Sheets[0].Drawings[1]; Assert.Equal(objectId, drawing.Id);
        Assert.Equal(DataDrawingKind.SharedInk, drawing.Kind);
        Assert.True(JsonElement.DeepEquals(bundle, drawing.SharedProductivityBundle!.Value));
        Assert.Equal(DataWorkbook.CurrentSchemaVersion, reopened.SchemaVersion);
    }

    [Fact]
    public async Task Xlsx_export_refuses_shared_ink_before_touching_an_existing_destination()
    {
        using var paths = new PhysicalPaths(); Directory.CreateDirectory(paths.DataDirectory);
        var workbook = DataWorkbook.Create("Keep native ink"); var sheet = workbook.Sheets[0];
        var bundle = Bundle(workbook.Id, sheet.Id); var id = Engine().ParseBundle(bundle.GetRawText()).Objects[0].ObjectId;
        new DataSheetDrawingEditor(sheet).AddSharedInk(id, bundle);
        var destination = Path.Combine(paths.DataDirectory, "existing.xlsx"); var originalBytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(destination, originalBytes, TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => new DataXlsxFormatService().ExportAsync(workbook, destination, TestContext.Current.CancellationToken));
        Assert.Contains("shared drawing", error.Message); Assert.Equal(originalBytes, await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.True(JsonElement.DeepEquals(bundle, Assert.Single(sheet.Drawings).SharedProductivityBundle!.Value));
    }

    [Fact]
    public void Missing_real_handler_and_invalid_pressure_or_geometry_do_not_create_shared_ink()
    {
        var context = Context(Guid.NewGuid()); var layer = Guid.NewGuid();
        Assert.Throws<NotSupportedException>(() => CanvasSharedInkStrokeFactory.Capture(new HomeProductivityEngine(), context, layer, [new(1, 1, .5), new(2, 2, .5)]));
        Assert.Throws<ArgumentException>(() => CanvasSharedInkStrokeFactory.Capture(Engine(), context, layer, [new(1, 1, .5), new(2, 2, double.NaN)]));
        Assert.Throws<InvalidDataException>(() => CanvasSharedInkStrokeFactory.Capture(Engine(), context, layer, [new(1, 1, .5), new(961, 2, .5)]));
        var wrongLayer = CanvasSharedInkStrokeFactory.Capture(Engine(), context, Guid.NewGuid(), [new(1, 1, .5), new(2, 2, .5)]);
        Assert.Throws<InvalidDataException>(() => CanvasSharedInkStrokeFactory.Snapshot(Engine(), new(context, layer, "current", [wrongLayer])));
    }

    [AvaloniaFact]
    public async Task Selected_sheet_epoch_conflict_rejects_the_stroke_before_any_native_work_or_mutation()
    {
        using var paths = new PhysicalPaths(); var repository = new DataWorkbookRepository(paths);
        var original = DataWorkbook.Create("Current sheet"); await repository.SaveAsync(original, "Seed", TestContext.Current.CancellationToken);
        using var bus = new HavenEventBus();
        using var page = new DataPage(bus, repository, new DataXlsxFormatService(), new DataWorkbookQueryService(), null, null, Engine());
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var sheet = page.Workbook!.Sheets[0]; var expected = new CanvasSharedInkOwnerSnapshot(Context(page.Workbook.Id) with { Revision = page.Workbook.Version }, sheet.Id, "stale-unsaved-epoch", []);
        var outcome = await page.CommitSharedInkStroke(new(expected, Bundle(page.Workbook.Id, sheet.Id)), TestContext.Current.CancellationToken);
        Assert.False(outcome.Succeeded); Assert.Equal("RevisionConflict", outcome.Code);
        Assert.Empty(sheet.Drawings); Assert.False(page.IsDirty);
    }

    [AvaloniaFact]
    public async Task Original_drawing_delete_and_spreadsheet_undo_redo_restore_the_same_canonical_bundle()
    {
        using var paths = new PhysicalPaths(); var repository = new DataWorkbookRepository(paths);
        var original = DataWorkbook.Create("Undo"); var sheet = original.Sheets[0]; sheet.SetCell(0, 0, "Keep this cell");
        var bundle = Bundle(original.Id, sheet.Id); var id = Engine().ParseBundle(bundle.GetRawText()).Objects[0].ObjectId;
        new DataSheetDrawingEditor(sheet).AddSharedInk(id, bundle);
        await repository.SaveAsync(original, "Seed", TestContext.Current.CancellationToken);
        using var bus = new HavenEventBus();
        // No shared renderer is supplied: this verifies the real existing owner/history path without claiming native rendering.
        using var page = new DataPage(bus, repository, new DataXlsxFormatService(), new DataWorkbookQueryService());
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        Invoke(page.Route.DeleteDrawingButton); Assert.Empty(page.Workbook!.Sheets[0].Drawings);
        Invoke(page.SceneRoot.DescendantsAndSelf().OfType<HavenButton>().Single(value => value.Name == "Data.Grid.Undo"));
        Assert.Equal(id, Assert.Single(page.Workbook.Sheets[0].Drawings).Id);
        Assert.True(JsonElement.DeepEquals(bundle, page.Workbook.Sheets[0].Drawings[0].SharedProductivityBundle!.Value));
        Assert.Equal("Keep this cell", page.Workbook.Sheets[0].GetCell(0, 0)?.Value);
        Invoke(page.SceneRoot.DescendantsAndSelf().OfType<HavenButton>().Single(value => value.Name == "Data.Grid.Redo"));
        Assert.Empty(page.Workbook.Sheets[0].Drawings); Assert.True(await page.SaveAsync("Delete after redo", TestContext.Current.CancellationToken));
        Assert.Empty((await repository.LoadAsync(page.Workbook.Id, TestContext.Current.CancellationToken))!.Sheets[0].Drawings);
    }

    [AvaloniaFact]
    public void Missing_handler_disables_the_actual_shared_input_before_a_native_library_is_requested()
    {
        var snapshot = new CanvasSharedInkOwnerSnapshot(Context(Guid.NewGuid()), Guid.NewGuid(), "current", []);
        var calls = 0;
        using var input = new CanvasSharedInkInput(new HomeProductivityEngine(), () => snapshot, (_, _) =>
        { ++calls; return ValueTask.FromResult(new CanvasSharedInkCommitResult(true, "Unexpected", "No mutation is allowed.")); });
        Assert.False(input.Refresh()); Assert.False(input.CanDraw); Assert.IsType<NotSupportedException>(input.LastError);
        Assert.Equal(0, calls); Assert.Null(input.PendingSubmission);
    }

    private static HomeProductivityEngine Engine() => new(handlers: [new CanvasInkObjectHandler()]);
    private static HomeProductivityContext Context(Guid workbook) => new("data", workbook.ToString("D"), 0, [], new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
    private static JsonElement Bundle(Guid workbook, Guid layer) => CanvasSharedInkStrokeFactory.Capture(Engine(), Context(workbook), layer, [new(10, 20, .5), new(40, 60, .8)]);
    private static void Invoke(HavenButton button)
    { var input = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None); button.KeyDown(input); button.KeyUp(input); }
    private sealed class PhysicalPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "data-shared-ink-smoke-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "Browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
