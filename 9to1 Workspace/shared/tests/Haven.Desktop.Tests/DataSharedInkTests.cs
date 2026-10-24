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
    public void Shared_vector_validation_rejects_undefined_styles_and_unstable_connector_identity()
    {
        var undefinedStyle = DocumentVectorShapes.CreateEditableStarter();
        undefinedStyle.SourceKind = (DocumentShapeSourceKind)int.MaxValue;
        undefinedStyle.Paths[0].FillRule = (DocumentVectorFillRule)int.MaxValue;
        undefinedStyle.Paths[0].Fill.Kind = (DocumentVectorFillKind)int.MaxValue;
        undefinedStyle.Paths[0].Stroke.Join = (DocumentVectorLineJoin)int.MaxValue;
        undefinedStyle.Paths[0].Subpaths[0].Nodes[1].IncomingSegment = (DocumentVectorSegmentKind)int.MaxValue;
        undefinedStyle.Paths[0].Subpaths[0].Nodes[1].Kind = (DocumentVectorNodeKind)int.MaxValue;
        var styleIssues = DocumentVectorShapeValidator.Validate(undefinedStyle).Issues;
        Assert.Contains(styleIssues, issue => issue.Code == "shape.source");
        Assert.Contains(styleIssues, issue => issue.Code == "path.fillRule");
        Assert.Contains(styleIssues, issue => issue.Code == "path.fill.kind");
        Assert.Contains(styleIssues, issue => issue.Code == "path.stroke.style");
        Assert.Contains(styleIssues, issue => issue.Code == "node.segment");
        Assert.Contains(styleIssues, issue => issue.Code == "node.kind");

        var undefinedCap = DocumentVectorShapes.CreateEditableStarter();
        undefinedCap.Paths[0].Stroke.Cap = (DocumentVectorLineCap)int.MaxValue;
        Assert.Contains(DocumentVectorShapeValidator.Validate(undefinedCap).Issues,
            issue => issue.Code == "path.stroke.style");

        var duplicateConnector = DocumentVectorShapes.CreateEditableStarter();
        duplicateConnector.ConnectorPoints[1].Id = duplicateConnector.ConnectorPoints[0].Id;
        Assert.Contains(DocumentVectorShapeValidator.Validate(duplicateConnector).Issues,
            issue => issue.Code == "shape.connector");
        var missingConnectorIdentity = DocumentVectorShapes.CreateEditableStarter();
        missingConnectorIdentity.ConnectorPoints[0].Id = Guid.Empty;
        Assert.Contains(DocumentVectorShapeValidator.Validate(missingConnectorIdentity).Issues,
            issue => issue.Code == "shape.connector");
    }

    [Fact]
    public void Shared_vector_validation_enforces_path_subpath_and_node_budgets()
    {
        static DocumentVectorSubpath TwoNodes() => new() { Nodes = [new(), new() { X = 1 }] };

        var tooManyPaths = DocumentVectorShapes.CreateEditableStarter();
        tooManyPaths.Paths = Enumerable.Range(0, DocumentVectorShapeValidator.MaximumPaths + 1)
            .Select(_ => new DocumentVectorPath { Subpaths = [TwoNodes()] }).ToList();
        Assert.Contains(DocumentVectorShapeValidator.Validate(tooManyPaths).Issues, issue => issue.Code == "shape.paths.limit");

        var tooManySubpaths = DocumentVectorShapes.CreateEditableStarter();
        tooManySubpaths.Paths = [new DocumentVectorPath
        {
            Subpaths = Enumerable.Range(0, DocumentVectorShapeValidator.MaximumSubpaths + 1).Select(_ => TwoNodes()).ToList()
        }];
        Assert.Contains(DocumentVectorShapeValidator.Validate(tooManySubpaths).Issues, issue => issue.Code == "shape.subpaths.limit");

        var tooManyNodes = DocumentVectorShapes.CreateEditableStarter();
        tooManyNodes.Paths = [new DocumentVectorPath
        {
            Subpaths = [new DocumentVectorSubpath
            {
                Nodes = Enumerable.Range(0, DocumentVectorShapeValidator.MaximumNodes + 1)
                    .Select(index => new DocumentVectorNode { X = index }).ToList()
            }]
        }];
        Assert.Contains(DocumentVectorShapeValidator.Validate(tooManyNodes).Issues, issue => issue.Code == "shape.nodes.limit");
    }

    [Fact]
    public async Task Shared_fill_edit_is_reversible_and_persists_on_the_same_Data_shape()
    {
        using var paths = new PhysicalPaths(); var repository = new DataWorkbookRepository(paths);
        var workbook = DataWorkbook.Create("Shared fill edit"); var sheet = workbook.Sheets[0];
        var dataEditor = new DataSheetDrawingEditor(sheet);
        var sourceShape = DocumentShapePresets.Create("hexagon");
        sourceShape.Paths.Add(DocumentShapePresets.Create("ellipse").Paths[0]); sourceShape.Normalize();
        var drawing = dataEditor.AddCustomShape(sourceShape);
        var originalShape = Assert.IsType<DocumentVectorShape>(drawing.VectorShape);
        var originalShapeId = originalShape.Id; var originalPathId = originalShape.Paths[0].Id;
        var originalFillRules = originalShape.Paths.Select(path => path.FillRule).ToArray();
        var originalDrawingId = drawing.Id;
        var expected = DocumentVectorShapes.Clone(originalShape);

        Assert.True(dataEditor.UpdateSelectedCustomShape(editor =>
        {
            var before = JsonSerializer.Serialize(editor.Shape);
            var beforeShape = DocumentVectorShapes.Clone(editor.Shape);
            Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetShapeFill(new DocumentVectorFill { Opacity = double.NaN }));
            Assert.Equal(before, JsonSerializer.Serialize(editor.Shape)); Assert.False(editor.CanUndo);
            var fillRequest = new DocumentVectorFill { Kind = DocumentVectorFillKind.Solid, Color = "#FF2865A8", Opacity = .65 };
            editor.SetShapeFill(fillRequest, DocumentOperationOrigin.User);
            fillRequest.Color = "#FF000000"; fillRequest.Opacity = 0;
            var after = JsonSerializer.Serialize(editor.Shape);
            Assert.NotEqual(before, after);
            Assert.True(editor.CanUndo); Assert.Equal("Style vector shape fill", editor.LastOperation!.Name);
            for (var index = 0; index < beforeShape.Paths.Count; index++)
            {
                Assert.Equal(beforeShape.Paths[index].FillRule, editor.Shape.Paths[index].FillRule);
                Assert.Equal(JsonSerializer.Serialize(beforeShape.Paths[index].Stroke), JsonSerializer.Serialize(editor.Shape.Paths[index].Stroke));
                Assert.Equal(JsonSerializer.Serialize(beforeShape.Paths[index].Subpaths), JsonSerializer.Serialize(editor.Shape.Paths[index].Subpaths));
            }
            Assert.True(editor.Undo()); Assert.Equal(before, JsonSerializer.Serialize(editor.Shape));
            Assert.True(editor.Redo()); Assert.Equal(after, JsonSerializer.Serialize(editor.Shape));
            expected = DocumentVectorShapes.Clone(editor.Shape);
        }));
        var editedShape = Assert.IsType<DocumentVectorShape>(drawing.VectorShape);
        Assert.Equal(originalDrawingId, drawing.Id); Assert.Equal(originalShapeId, editedShape.Id);
        Assert.Equal(originalFillRules, editedShape.Paths.Select(path => path.FillRule));

        await repository.SaveAsync(workbook, "Save shared fill", TestContext.Current.CancellationToken);
        var reopened = Assert.IsType<DataWorkbook>(await repository.LoadAsync(workbook.Id, TestContext.Current.CancellationToken));
        var reopenedDrawing = Assert.Single(reopened.Sheets[0].Drawings);
        var reopenedShape = Assert.IsType<DocumentVectorShape>(reopenedDrawing.VectorShape);
        Assert.Equal(originalDrawingId, reopenedDrawing.Id); Assert.Equal(originalShapeId, reopenedShape.Id);
        Assert.Equal(originalPathId, reopenedShape.Paths[0].Id);
        Assert.All(reopenedShape.Paths, path =>
        {
            Assert.Equal(DocumentVectorFillKind.Solid, path.Fill.Kind);
            Assert.Equal("#FF2865A8", path.Fill.Color);
            Assert.Equal(.65, path.Fill.Opacity);
        });
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected),
            JsonSerializer.SerializeToElement(reopenedShape)));
    }

    [AvaloniaFact]
    public async Task Shared_ink_CUI_shape_style_presets_use_selection_no_op_and_spreadsheet_undo_redo()
    {
        using var paths = new PhysicalPaths(); var repository = new DataWorkbookRepository(paths);
        var workbook = DataWorkbook.Create("CUI fill preset");
        var sourceShape = DocumentShapePresets.Create("hexagon");
        sourceShape.Paths.Add(DocumentShapePresets.Create("ellipse").Paths[0]);
        sourceShape.Paths[0].Stroke.Color = "#FFAA2200"; sourceShape.Paths[0].Stroke.Width = 1.5;
        sourceShape.Paths[1].Stroke.Color = "#FF00AA22"; sourceShape.Paths[1].Stroke.Width = 2.5;
        sourceShape.Normalize();
        var drawing = new DataSheetDrawingEditor(workbook.Sheets[0]).AddCustomShape(sourceShape);
        var shapeId = drawing.VectorShape!.Id;
        await repository.SaveAsync(workbook, "Seed fill preset", TestContext.Current.CancellationToken);
        using var bus = new HavenEventBus();
        using var page = new DataPage(bus, repository, new DataXlsxFormatService(), new DataWorkbookQueryService(), null, null, Engine());
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var activeWorkbook = Assert.IsType<DataWorkbook>(page.Workbook);
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var bindings = typeof(DataPage).GetField("_sharedInkBindings", fields)!.GetValue(page)!;
        var dispatcher = Assert.IsAssignableFrom<CakeOS.Cui.Runtime.ICuiActionDispatcher>(bindings);
        var availability = Assert.IsAssignableFrom<CakeOS.Cui.Runtime.ICuiActionAvailability>(bindings);
        Assert.True(availability.IsActionAvailable("data.ink.shape.fillPreset"));
        Assert.True(availability.IsActionAvailable("data.ink.shape.strokePreset"));

        // Build an independent redo branch while the selected shape already has the fill preset.
        Invoke(page.Route.AddShapeButton);
        Assert.Equal(2, activeWorkbook.Sheets[0].Drawings.Count);
        var addedDrawingId = activeWorkbook.Sheets[0].Drawings[1].Id;
        await dispatcher.DispatchAsync("data.ink.undo", null, TestContext.Current.CancellationToken);
        Assert.Single(activeWorkbook.Sheets[0].Drawings);
        Assert.True(availability.IsActionAvailable("data.ink.redo") == true);
        Assert.True(await page.SaveAsync("Save before same-fill no-op", TestContext.Current.CancellationToken));
        var sameFillShape = JsonSerializer.Serialize(activeWorkbook.Sheets[0].Drawings[0].VectorShape);
        await dispatcher.DispatchAsync("data.ink.shape.fillPreset", null, TestContext.Current.CancellationToken);
        Assert.Equal(sameFillShape, JsonSerializer.Serialize(activeWorkbook.Sheets[0].Drawings[0].VectorShape));
        Assert.False(page.IsDirty); Assert.False(availability.IsActionAvailable("data.ink.undo") == true);
        Assert.True(availability.IsActionAvailable("data.ink.redo") == true);
        activeWorkbook.Sheets[0].Drawings[0].VectorShape!.Paths[0].Fill.Kind = (DocumentVectorFillKind)int.MaxValue;
        var malformedShape = JsonSerializer.Serialize(activeWorkbook.Sheets[0].Drawings[0].VectorShape);
        await Assert.ThrowsAsync<InvalidDataException>(() => dispatcher.DispatchAsync(
            "data.ink.shape.fillPreset", null, TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(malformedShape, JsonSerializer.Serialize(activeWorkbook.Sheets[0].Drawings[0].VectorShape));
        Assert.True(availability.IsActionAvailable("data.ink.redo") == true);
        await dispatcher.DispatchAsync("data.ink.redo", null, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { shapeId, addedDrawingId }, activeWorkbook.Sheets[0].Drawings.Select(value => value.Id));

        var selectedShape = Assert.IsType<DocumentVectorShape>(activeWorkbook.Sheets[0].Drawings[0].VectorShape);
        Assert.All(selectedShape.Paths, path => Assert.Equal("#FF006FB9", path.Fill.Color));
        await dispatcher.DispatchAsync("data.ink.shape.strokePreset", null, TestContext.Current.CancellationToken);
        Assert.All(activeWorkbook.Sheets[0].Drawings[0].VectorShape!.Paths,
            path => Assert.Equal("#FF006FB9", path.Stroke.Color));
        Assert.True(page.IsDirty);
        await dispatcher.DispatchAsync("data.ink.undo", null, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "#FFAA2200", "#FF00AA22" }, activeWorkbook.Sheets[0].Drawings[0].VectorShape!.Paths.Select(path => path.Stroke.Color));
        await dispatcher.DispatchAsync("data.ink.redo", null, TestContext.Current.CancellationToken);
        Assert.All(activeWorkbook.Sheets[0].Drawings[0].VectorShape!.Paths,
            path => Assert.Equal("#FF006FB9", path.Stroke.Color));
        Assert.True(await page.SaveAsync("Save selected shape style presets", TestContext.Current.CancellationToken));

        activeWorkbook.Sheets[0].Drawings[0].Locked = true;
        Assert.False(availability.IsActionAvailable("data.ink.shape.fillPreset") == true);
        Assert.False(availability.IsActionAvailable("data.ink.shape.strokePreset") == true);
        var lockedShape = JsonSerializer.Serialize(activeWorkbook.Sheets[0].Drawings[0].VectorShape);
        await dispatcher.DispatchAsync("data.ink.shape.fillPreset", null, TestContext.Current.CancellationToken);
        await dispatcher.DispatchAsync("data.ink.shape.strokePreset", null, TestContext.Current.CancellationToken);
        Assert.Equal(lockedShape, JsonSerializer.Serialize(activeWorkbook.Sheets[0].Drawings[0].VectorShape));
        Assert.False(page.IsDirty);

        var reopened = Assert.IsType<DataWorkbook>(await repository.LoadAsync(activeWorkbook.Id, TestContext.Current.CancellationToken));
        Assert.Equal(new[] { shapeId, addedDrawingId }, reopened.Sheets[0].Drawings.Select(value => value.Id));
        var reopenedShape = Assert.IsType<DocumentVectorShape>(reopened.Sheets[0].Drawings[0].VectorShape);
        Assert.Equal(shapeId, reopenedShape.Id);
        Assert.All(reopenedShape.Paths, path =>
        {
            Assert.Equal("#FF006FB9", path.Fill.Color);
            Assert.Equal("#FF006FB9", path.Stroke.Color);
        });
    }

    [AvaloniaFact]
    public async Task Shared_ink_CUI_stroke_width_input_reports_mixed_validates_and_roundtrips()
    {
        using var paths = new PhysicalPaths(); var repository = new DataWorkbookRepository(paths);
        var workbook = DataWorkbook.Create("Stroke width precision");
        var sourceShape = DocumentShapePresets.Create("hexagon");
        sourceShape.Paths.Add(DocumentShapePresets.Create("ellipse").Paths[0]);
        sourceShape.Paths[0].Stroke.Color = "#FFAA2200"; sourceShape.Paths[0].Stroke.Width = 1.5;
        sourceShape.Paths[1].Stroke.Color = "#FF00AA22"; sourceShape.Paths[1].Stroke.Width = 3.25;
        sourceShape.Normalize();
        var drawing = new DataSheetDrawingEditor(workbook.Sheets[0]).AddCustomShape(sourceShape);
        var originalDataShape = Assert.IsType<DocumentVectorShape>(drawing.VectorShape);
        var shapeId = originalDataShape.Id;
        var originalShape = DocumentVectorShapes.Clone(originalDataShape);
        var before = JsonSerializer.SerializeToElement(originalDataShape);
        var strokeProperties = originalDataShape.Paths.Select(path =>
            (path.Stroke.Enabled, path.Stroke.Color, path.Stroke.Opacity, path.Stroke.Cap, path.Stroke.Join)).ToArray();
        await repository.SaveAsync(workbook, "Seed stroke precision", TestContext.Current.CancellationToken);

        using var bus = new HavenEventBus();
        using var page = new DataPage(bus, repository, new DataXlsxFormatService(), new DataWorkbookQueryService(), null, null, Engine());
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var activeWorkbook = Assert.IsType<DataWorkbook>(page.Workbook);
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var bindings = typeof(DataPage).GetField("_sharedInkBindings", fields)!.GetValue(page)!;
        var context = Assert.IsAssignableFrom<CakeOS.Cui.Runtime.ICuiBindingContext>(bindings);
        var writable = Assert.IsAssignableFrom<CakeOS.Cui.Runtime.ICuiWritableBindingContext>(bindings);
        var dispatcher = Assert.IsAssignableFrom<CakeOS.Cui.Runtime.ICuiActionDispatcher>(bindings);
        var availability = Assert.IsAssignableFrom<CakeOS.Cui.Runtime.ICuiActionAvailability>(bindings);

        Assert.True(context.TryGetValue("StrokeWidthSummary", out var initialSummary));
        Assert.Equal("Mixed stroke widths · 2 values", initialSummary);
        Assert.True(context.TryGetValue("StrokeWidthDraft", out var initialDraft));
        Assert.Equal(string.Empty, initialDraft);
        foreach (var invalid in new[] { "NaN", "Infinity", "-0.01", "10000.01" })
        {
            Assert.True(writable.TrySetValue("StrokeWidthDraft", invalid));
            Assert.False(availability.IsActionAvailable("data.ink.shape.strokeWidth.apply") == true);
            await dispatcher.DispatchAsync("data.ink.shape.strokeWidth.apply", null, TestContext.Current.CancellationToken);
            Assert.False(page.IsDirty); Assert.False(availability.IsActionAvailable("data.ink.undo") == true);
            Assert.True(JsonElement.DeepEquals(before,
                JsonSerializer.SerializeToElement(activeWorkbook.Sheets[0].Drawings[0].VectorShape)));
        }
        Assert.False(writable.TrySetValue("StrokeWidthDraft", new string('0', 33)));
        Assert.True(context.TryGetValue("StrokeWidthDraft", out var clearedDraft)); Assert.Equal(string.Empty, clearedDraft);
        Assert.False(availability.IsActionAvailable("data.ink.shape.strokeWidth.apply") == true);
        Assert.True(writable.TrySetValue("StrokeWidthDraft", "10000"));
        Assert.True(availability.IsActionAvailable("data.ink.shape.strokeWidth.apply") == true);
        Assert.True(writable.TrySetValue("StrokeWidthDraft", "4.25"));
        Assert.True(availability.IsActionAvailable("data.ink.shape.strokeWidth.apply") == true);

        await dispatcher.DispatchAsync("data.ink.shape.strokeWidth.apply", null, TestContext.Current.CancellationToken);
        var edited = Assert.IsType<DocumentVectorShape>(activeWorkbook.Sheets[0].Drawings[0].VectorShape);
        Assert.Equal(shapeId, edited.Id); Assert.All(edited.Paths, path => Assert.Equal(4.25, path.Stroke.Width));
        for (var index = 0; index < edited.Paths.Count; index++)
        {
            var stroke = edited.Paths[index].Stroke;
            Assert.Equal(originalShape.Paths[index].Id, edited.Paths[index].Id);
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(originalShape.Paths[index].Fill),
                JsonSerializer.SerializeToElement(edited.Paths[index].Fill)));
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(originalShape.Paths[index].Subpaths),
                JsonSerializer.SerializeToElement(edited.Paths[index].Subpaths)));
            Assert.Equal(strokeProperties[index], (stroke.Enabled, stroke.Color, stroke.Opacity, stroke.Cap, stroke.Join));
        }
        Assert.True(context.TryGetValue("StrokeWidthSummary", out var commonSummary));
        Assert.Equal("Stroke width · 4.25", commonSummary);
        Assert.True(page.IsDirty); Assert.True(availability.IsActionAvailable("data.ink.undo") == true);
        await dispatcher.DispatchAsync("data.ink.undo", null, TestContext.Current.CancellationToken);
        Assert.True(JsonElement.DeepEquals(before,
            JsonSerializer.SerializeToElement(activeWorkbook.Sheets[0].Drawings[0].VectorShape)));
        await dispatcher.DispatchAsync("data.ink.redo", null, TestContext.Current.CancellationToken);
        edited = Assert.IsType<DocumentVectorShape>(activeWorkbook.Sheets[0].Drawings[0].VectorShape);
        Assert.All(edited.Paths, path => Assert.Equal(4.25, path.Stroke.Width));
        Invoke(page.Route.AddShapeButton);
        var addedDrawingId = activeWorkbook.Sheets[0].Drawings[1].Id;
        await dispatcher.DispatchAsync("data.ink.undo", null, TestContext.Current.CancellationToken);
        Assert.True(availability.IsActionAvailable("data.ink.redo") == true);
        Assert.True(await page.SaveAsync("Save before same-width no-op", TestContext.Current.CancellationToken));
        var sameWidthShape = JsonSerializer.Serialize(activeWorkbook.Sheets[0].Drawings[0].VectorShape);
        Assert.True(writable.TrySetValue("StrokeWidthDraft", "4.25"));
        await dispatcher.DispatchAsync("data.ink.shape.strokeWidth.apply", null, TestContext.Current.CancellationToken);
        Assert.Equal(sameWidthShape, JsonSerializer.Serialize(activeWorkbook.Sheets[0].Drawings[0].VectorShape));
        Assert.False(page.IsDirty); Assert.True(availability.IsActionAvailable("data.ink.redo") == true);
        await dispatcher.DispatchAsync("data.ink.redo", null, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { shapeId, addedDrawingId }, activeWorkbook.Sheets[0].Drawings.Select(value => value.Id));
        edited = Assert.IsType<DocumentVectorShape>(activeWorkbook.Sheets[0].Drawings[0].VectorShape);
        Assert.True(await page.SaveAsync("Save precise stroke width", TestContext.Current.CancellationToken));

        var reopened = Assert.IsType<DataWorkbook>(await repository.LoadAsync(activeWorkbook.Id, TestContext.Current.CancellationToken));
        Assert.Equal(new[] { shapeId, addedDrawingId }, reopened.Sheets[0].Drawings.Select(value => value.Id));
        var reopenedShape = Assert.IsType<DocumentVectorShape>(reopened.Sheets[0].Drawings[0].VectorShape);
        Assert.Equal(shapeId, reopenedShape.Id); Assert.All(reopenedShape.Paths, path => Assert.Equal(4.25, path.Stroke.Width));
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(edited),
            JsonSerializer.SerializeToElement(reopenedShape)));
    }

    [Fact]
    public void Shared_shape_stroke_width_action_is_reused_by_Write_Present_Canvas_and_Data_adapters()
    {
        const double newWidth = 5.75;
        var source = DocumentShapePresets.Create("hexagon");
        source.Paths.Add(DocumentShapePresets.Create("ellipse").Paths[0]); source.Normalize();
        static DocumentVectorShape InsertedShape(DocumentVectorShape? value) => Assert.IsType<DocumentVectorShape>(value);
        static void AssertStyled(DocumentVectorShape actual, DocumentVectorShape before, double width)
        {
            Assert.Equal(before.Id, actual.Id);
            Assert.Equal(before.Paths.Select(path => path.Id), actual.Paths.Select(path => path.Id));
            Assert.Equal(before.Paths.SelectMany(path => path.Subpaths).SelectMany(path => path.Nodes).Select(node => node.Id),
                actual.Paths.SelectMany(path => path.Subpaths).SelectMany(path => path.Nodes).Select(node => node.Id));
            for (var index = 0; index < actual.Paths.Count; index++)
            {
                Assert.Equal(width, actual.Paths[index].Stroke.Width);
                Assert.Equal(before.Paths[index].Stroke.Color, actual.Paths[index].Stroke.Color);
                Assert.Equal(before.Paths[index].Stroke.Enabled, actual.Paths[index].Stroke.Enabled);
                Assert.Equal(before.Paths[index].Stroke.Opacity, actual.Paths[index].Stroke.Opacity);
                Assert.Equal(before.Paths[index].Stroke.Cap, actual.Paths[index].Stroke.Cap);
                Assert.Equal(before.Paths[index].Stroke.Join, actual.Paths[index].Stroke.Join);
                Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(before.Paths[index].Subpaths),
                    JsonSerializer.SerializeToElement(actual.Paths[index].Subpaths)));
            }
        }

        var writeEditor = new WriteDocumentEditor(NotesDocument.Create("Write shared stroke"));
        var writeBlock = writeEditor.InsertCustomShape(source);
        var writeBefore = DocumentVectorShapes.Clone(InsertedShape(writeBlock.VectorShape));
        Assert.True(writeEditor.UpdateSelectedCustomShape(editor => editor.SetShapeStrokeWidth(newWidth)));
        AssertStyled(InsertedShape(writeBlock.VectorShape), writeBefore, newWidth);

        var presentDocument = PresentDocument.Create("Present shared stroke");
        var presentEditor = new PresentEditor(presentDocument);
        var slideId = presentDocument.Slides[0].Id;
        var presentElement = presentEditor.AddCustomShape(slideId, source);
        var presentBefore = DocumentVectorShapes.Clone(InsertedShape(presentElement.VectorShape));
        Assert.True(presentEditor.UpdateCustomShape(slideId, presentElement.Id,
            editor => editor.SetShapeStrokeWidth(newWidth)));
        AssertStyled(InsertedShape(presentElement.VectorShape), presentBefore, newWidth);

        var canvasDocument = CanvasDocumentModel.Create("Canvas shared stroke");
        var canvasEditor = new CanvasInteractionController(CanvasDocumentModel.GetBoard(canvasDocument));
        var canvasObject = canvasEditor.AddCustomShape(source);
        var canvasBefore = DocumentVectorShapes.Clone(InsertedShape(canvasObject.VectorShape));
        Assert.True(canvasEditor.UpdateSelectedCustomShape(editor => editor.SetShapeStrokeWidth(newWidth)));
        AssertStyled(InsertedShape(canvasObject.VectorShape), canvasBefore, newWidth);

        var dataSheet = DataWorkbook.Create("Data shared stroke").Sheets[0];
        var dataEditor = new DataSheetDrawingEditor(dataSheet);
        var drawing = dataEditor.AddCustomShape(source);
        var dataBefore = DocumentVectorShapes.Clone(InsertedShape(drawing.VectorShape));
        Assert.True(dataEditor.UpdateSelectedCustomShape(editor => editor.SetShapeStrokeWidth(newWidth)));
        AssertStyled(InsertedShape(drawing.VectorShape), dataBefore, newWidth);
    }

    [Fact]
    public void Shared_vector_aggregate_budgets_cover_multiple_individually_valid_collections_and_exact_boundaries()
    {
        const int perSubpathBudget = DocumentVectorShapeValidator.MaximumNodes / 2;
        var tooManySubpaths = ShapeWithPaths(PathWithSubpaths(512), PathWithSubpaths(513));
        Assert.All(tooManySubpaths.Paths, path => Assert.True(path.Subpaths.Count <= DocumentVectorShapeValidator.MaximumSubpaths));
        Assert.All(tooManySubpaths.Paths, path => Assert.True(DocumentVectorShapeValidator.Validate(ShapeWithPaths(path)).IsValid));
        Assert.Contains(DocumentVectorShapeValidator.Validate(tooManySubpaths).Issues,
            issue => issue.Code == "shape.subpaths.limit");

        var exactSubpaths = ShapeWithPaths(PathWithSubpaths(512), PathWithSubpaths(512));
        Assert.Equal(DocumentVectorShapeValidator.MaximumSubpaths, exactSubpaths.Paths.Sum(path => path.Subpaths.Count));
        Assert.True(DocumentVectorShapeValidator.Validate(exactSubpaths).IsValid);
        exactSubpaths.Paths[^1].Subpaths[^1].Nodes[^1].X = double.NaN;
        var lastSubpathIssues = DocumentVectorShapeValidator.Validate(exactSubpaths).Issues;
        Assert.Contains(lastSubpathIssues, issue => issue.Code == "node.point");
        Assert.DoesNotContain(lastSubpathIssues, issue => issue.Code == "shape.subpaths.limit");

        var tooManyNodes = ShapeWithPaths(PathWithSubpaths(2, perSubpathBudget + 1));
        Assert.All(tooManyNodes.Paths[0].Subpaths,
            subpath => Assert.True(subpath.Nodes.Count <= DocumentVectorShapeValidator.MaximumNodes));
        Assert.All(tooManyNodes.Paths[0].Subpaths, subpath => Assert.True(DocumentVectorShapeValidator.Validate(
            ShapeWithPaths(new DocumentVectorPath { Subpaths = [subpath] })).IsValid));
        Assert.Contains(DocumentVectorShapeValidator.Validate(tooManyNodes).Issues,
            issue => issue.Code == "shape.nodes.limit");

        var exactNodes = ShapeWithPaths(PathWithSubpaths(2, perSubpathBudget));
        Assert.Equal(DocumentVectorShapeValidator.MaximumNodes,
            exactNodes.Paths.SelectMany(path => path.Subpaths).Sum(subpath => subpath.Nodes.Count));
        Assert.True(DocumentVectorShapeValidator.Validate(exactNodes).IsValid);
        exactNodes.Paths[^1].Subpaths[^1].Nodes[^1].X = double.NaN;
        var lastNodeIssues = DocumentVectorShapeValidator.Validate(exactNodes).Issues;
        Assert.Contains(lastNodeIssues, issue => issue.Code == "node.point");
        Assert.DoesNotContain(lastNodeIssues, issue => issue.Code == "shape.nodes.limit");
    }

    [Fact]
    public void Node_budget_rejection_preserves_editor_geometry_ids_and_existing_history()
    {
        var shape = ShapeWithPaths(PathWithSubpaths(2, DocumentVectorShapeValidator.MaximumNodes / 2));
        var editor = new DocumentVectorShapeEditor(shape);
        var firstNode = editor.Shape.Paths[0].Subpaths[0].Nodes[1];
        var initialX = firstNode.X;
        editor.MoveNode(firstNode.Id, firstNode.X + 1, firstNode.Y + 1, DocumentOperationOrigin.User);
        var snapshot = JsonSerializer.Serialize(editor.Shape);
        var previousOperation = editor.LastOperation;
        var targetSubpath = editor.Shape.Paths[0].Subpaths[1];

        Assert.Throws<InvalidDataException>(() => editor.AddNode(targetSubpath.Id, new DocumentVectorNode { X = 7, Y = 9 }));

        Assert.Equal(snapshot, JsonSerializer.Serialize(editor.Shape));
        Assert.Equal(previousOperation, editor.LastOperation);
        Assert.True(editor.CanUndo);
        Assert.False(editor.CanRedo);
        Assert.Equal(DocumentVectorShapeValidator.MaximumNodes,
            editor.Shape.Paths.SelectMany(path => path.Subpaths).Sum(subpath => subpath.Nodes.Count));
        Assert.True(editor.Undo());
        Assert.Equal(initialX, editor.Shape.Paths[0].Subpaths[0].Nodes[1].X);
        Assert.True(editor.CanRedo);
        Assert.True(editor.Redo());
        Assert.Equal(snapshot, JsonSerializer.Serialize(editor.Shape));
    }

    [Fact]
    public async Task Shared_fill_edit_is_reversible_and_persists_on_the_same_Data_shape()
    {
        using var paths = new PhysicalPaths(); var repository = new DataWorkbookRepository(paths);
        var workbook = DataWorkbook.Create("Shared fill edit"); var sheet = workbook.Sheets[0];
        var dataEditor = new DataSheetDrawingEditor(sheet);
        var drawing = dataEditor.AddCustomShape(DocumentShapePresets.Create("hexagon"));
        var originalShape = Assert.IsType<DocumentVectorShape>(drawing.VectorShape);
        var originalShapeId = originalShape.Id; var originalPathId = originalShape.Paths[0].Id;
        var originalDrawingId = drawing.Id;
        var expected = DocumentVectorShapes.Clone(originalShape);

        Assert.True(dataEditor.UpdateSelectedCustomShape(editor =>
        {
            var before = JsonSerializer.Serialize(editor.Shape);
            editor.SetPathFill(originalPathId,
                new DocumentVectorFill { Kind = DocumentVectorFillKind.Solid, Color = "#FF2865A8", Opacity = .65 },
                DocumentVectorFillRule.NonZero, DocumentOperationOrigin.User);
            var after = JsonSerializer.Serialize(editor.Shape);
            Assert.NotEqual(before, after);
            Assert.True(editor.CanUndo); Assert.Equal("Style vector fill", editor.LastOperation!.Name);
            Assert.True(editor.Undo()); Assert.Equal(before, JsonSerializer.Serialize(editor.Shape));
            Assert.True(editor.Redo()); Assert.Equal(after, JsonSerializer.Serialize(editor.Shape));
            expected = DocumentVectorShapes.Clone(editor.Shape);
        }));
        Assert.Equal(originalDrawingId, drawing.Id);
        Assert.Equal(originalShapeId, drawing.VectorShape!.Id);

        await repository.SaveAsync(workbook, "Save shared fill", TestContext.Current.CancellationToken);
        var reopened = Assert.IsType<DataWorkbook>(await repository.LoadAsync(workbook.Id, TestContext.Current.CancellationToken));
        var reopenedDrawing = Assert.Single(reopened.Sheets[0].Drawings);
        var reopenedShape = Assert.IsType<DocumentVectorShape>(reopenedDrawing.VectorShape);
        Assert.Equal(originalDrawingId, reopenedDrawing.Id); Assert.Equal(originalShapeId, reopenedShape.Id);
        Assert.Equal(originalPathId, reopenedShape.Paths[0].Id);
        Assert.Equal(DocumentVectorFillKind.Solid, reopenedShape.Paths[0].Fill.Kind);
        Assert.Equal("#FF2865A8", reopenedShape.Paths[0].Fill.Color);
        Assert.Equal(.65, reopenedShape.Paths[0].Fill.Opacity);
        Assert.Equal(DocumentVectorFillRule.NonZero, reopenedShape.Paths[0].FillRule);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected),
            JsonSerializer.SerializeToElement(reopenedShape)));
    }

    [Fact]
    public void Vector_node_validation_handles_missing_empty_single_and_two_node_subpaths()
    {
        var missing = DocumentVectorShapes.CreateEditableStarter();
        missing.Paths[0].Subpaths[0].Nodes = null!;
        Assert.True(DocumentVectorShapeValidator.Validate(missing, requireEditableGeometry: false).IsValid);
        Assert.Contains(DocumentVectorShapeValidator.Validate(missing).Issues, issue => issue.Code == "subpath.nodes");

        var empty = DocumentVectorShapes.CreateEditableStarter();
        empty.Paths[0].Subpaths[0].Nodes = [];
        Assert.True(DocumentVectorShapeValidator.Validate(empty, requireEditableGeometry: false).IsValid);
        Assert.Contains(DocumentVectorShapeValidator.Validate(empty).Issues, issue => issue.Code == "subpath.nodes");

        var oneNode = DocumentVectorShapes.CreateEditableStarter();
        oneNode.Paths[0].Subpaths[0].Nodes.RemoveRange(1, oneNode.Paths[0].Subpaths[0].Nodes.Count - 1);
        Assert.True(DocumentVectorShapeValidator.Validate(oneNode, requireEditableGeometry: false).IsValid);
        var nullNode = DocumentVectorShapes.CreateEditableStarter();
        nullNode.Paths[0].Subpaths[0].Nodes.RemoveRange(1, nullNode.Paths[0].Subpaths[0].Nodes.Count - 1);
        nullNode.Paths[0].Subpaths[0].Nodes[0] = null!;
        Assert.Contains(DocumentVectorShapeValidator.Validate(nullNode, requireEditableGeometry: false).Issues,
            issue => issue.Code == "node.null");
        var node = oneNode.Paths[0].Subpaths[0].Nodes[0];
        node.Id = Guid.Empty; node.Kind = (DocumentVectorNodeKind)int.MaxValue;
        node.IncomingSegment = (DocumentVectorSegmentKind)int.MaxValue; node.X = double.NaN;
        var result = DocumentVectorShapeValidator.Validate(oneNode, requireEditableGeometry: false);
        Assert.False(result.IsValid);
        foreach (var code in new[] { "node.id", "node.kind", "node.segment", "node.point" })
            Assert.Contains(result.Issues, issue => issue.Code == code);
        Assert.Contains(DocumentVectorShapeValidator.Validate(oneNode).Issues, issue => issue.Code == "subpath.nodes");

        var twoNodes = DocumentVectorShapes.CreateEditableStarter();
        twoNodes.Paths[0].Subpaths[0].Nodes.RemoveRange(2, twoNodes.Paths[0].Subpaths[0].Nodes.Count - 2);
        Assert.True(DocumentVectorShapeValidator.Validate(twoNodes).IsValid);
        Assert.True(DocumentVectorShapeValidator.Validate(twoNodes, requireEditableGeometry: false).IsValid);
    }

    [Fact]
    public void Shared_vector_primitives_keep_geometry_identity_and_editor_history_across_json_roundtrips()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentVectorPrimitives.Create((DocumentVectorPrimitive)int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Star, pointCount: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Polygon, pointCount: DocumentVectorPrimitives.MaximumPointCount + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Ellipse, width: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Ellipse, height: 0));

        foreach (var primitive in Enum.GetValues<DocumentVectorPrimitive>())
        {
            var created = DocumentVectorPrimitives.Create(primitive, pointCount: 7, width: 120, height: 80);
            Assert.True(DocumentVectorShapeValidator.Validate(created).IsValid, primitive.ToString());
            var reopened = Assert.IsType<DocumentVectorShape>(JsonSerializer.Deserialize<DocumentVectorShape>(JsonSerializer.Serialize(created)));
            Assert.True(DocumentVectorShapeValidator.Validate(reopened).IsValid, primitive.ToString());
            Assert.Equal(created.Id, reopened.Id);
            Assert.Equal(created.Paths.Select(path => path.Id), reopened.Paths.Select(path => path.Id));
            Assert.Equal(created.Paths.SelectMany(path => path.Subpaths).Select(subpath => subpath.Id),
                reopened.Paths.SelectMany(path => path.Subpaths).Select(subpath => subpath.Id));
            var createdGeometry = created.Paths.SelectMany(path => path.Subpaths).SelectMany(subpath => subpath.Nodes)
                .Select(node => (node.Id, node.X, node.Y, node.IncomingSegment, node.Control1, node.Control2)).ToArray();
            var reopenedGeometry = reopened.Paths.SelectMany(path => path.Subpaths).SelectMany(subpath => subpath.Nodes)
                .Select(node => (node.Id, node.X, node.Y, node.IncomingSegment, node.Control1, node.Control2)).ToArray();
            Assert.Equal(createdGeometry, reopenedGeometry);
            Assert.Equal(created.ConnectorPoints.Select(point => (point.Id, point.X, point.Y, point.DirectionDegrees)),
                reopened.ConnectorPoints.Select(point => (point.Id, point.X, point.Y, point.DirectionDegrees)));
        }

        var ellipse = DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Ellipse, width: 160, height: 90);
        ellipse.ClippingPathId = ellipse.Paths[0].Id;
        var inserted = DocumentVectorShapes.CloneForInsertion(ellipse);
        Assert.NotEqual(ellipse.Id, inserted.Id);
        Assert.NotEqual(ellipse.Paths[0].Id, inserted.Paths[0].Id);
        Assert.NotEqual(ellipse.Paths[0].Subpaths[0].Id, inserted.Paths[0].Subpaths[0].Id);
        Assert.NotEqual(ellipse.Paths[0].Subpaths[0].Nodes[1].Id, inserted.Paths[0].Subpaths[0].Nodes[1].Id);
        Assert.Equal(inserted.Paths[0].Id, inserted.ClippingPathId);
        Assert.NotEqual(ellipse.ConnectorPoints[0].Id, inserted.ConnectorPoints[0].Id);
        Assert.Equal(ellipse.Paths[0].Subpaths[0].Nodes[1].Control1, inserted.Paths[0].Subpaths[0].Nodes[1].Control1);

        var editor = new DocumentVectorShapeEditor(ellipse);
        var path = editor.Shape.Paths[0]; var node = path.Subpaths[0].Nodes[1];
        var originalControl = node.Control2!;
        var originalSegment = node.IncomingSegment;
        Assert.Throws<InvalidDataException>(() => editor.SetNodeSegment(node.Id, (DocumentVectorSegmentKind)int.MaxValue));
        Assert.Equal(originalSegment, editor.Shape.Paths[0].Subpaths[0].Nodes[1].IncomingSegment);
        Assert.False(editor.CanUndo);
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.MoveControlPoint(node.Id, 2,
            originalControl.X + 12, originalControl.Y - 6, (DocumentOperationOrigin)int.MaxValue));
        Assert.False(editor.CanUndo); Assert.Null(editor.LastOperation);
        editor.MoveControlPoint(node.Id, 2, originalControl.X + 12, originalControl.Y - 6, DocumentOperationOrigin.Ai);
        Assert.True(editor.CanUndo); Assert.Equal(DocumentOperationOrigin.Ai, editor.LastOperation!.Origin);
        var edited = Assert.IsType<DocumentVectorShape>(JsonSerializer.Deserialize<DocumentVectorShape>(JsonSerializer.Serialize(editor.Shape)));
        Assert.True(DocumentVectorShapeValidator.Validate(edited).IsValid);
        Assert.Equal(originalControl.X + 12, edited.Paths[0].Subpaths[0].Nodes[1].Control2!.X);
        Assert.True(editor.Undo());
        Assert.Equal(originalControl, editor.Shape.Paths[0].Subpaths[0].Nodes[1].Control2);
        Assert.True(editor.Redo());
        Assert.Equal(edited.Paths[0].Subpaths[0].Nodes[1].Control2, editor.Shape.Paths[0].Subpaths[0].Nodes[1].Control2);
    }

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
        var editor = new DataSheetDrawingEditor(sheet);
        var sourceShape = DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Ellipse, width: 140, height: 80);
        var insertedShape = editor.AddCustomShape(sourceShape).VectorShape!;
        var bundle = Bundle(workbook.Id, sheet.Id); var objectId = Engine().ParseBundle(bundle.GetRawText()).Objects[0].ObjectId;
        editor.AddSharedInk(objectId, bundle);
        await repository.SaveAsync(workbook, "Shared drawing smoke", TestContext.Current.CancellationToken);
        var reopened = Assert.IsType<DataWorkbook>(await repository.LoadAsync(workbook.Id, TestContext.Current.CancellationToken));
        Assert.Equal(workbook.Id, reopened.Id); Assert.Equal(sheet.Id, reopened.Sheets[0].Id);
        Assert.Equal("A retained cell", reopened.Sheets[0].GetCell(0, 0)?.Value);
        Assert.Equal(workbook.Queries[0].Sql, reopened.Queries[0].Sql);
        Assert.Equal(2, reopened.Sheets[0].Drawings.Count);
        var reopenedShape = Assert.IsType<DocumentVectorShape>(reopened.Sheets[0].Drawings[0].VectorShape);
        Assert.Equal(insertedShape.Id, reopenedShape.Id);
        Assert.Equal(insertedShape.Paths[0].Subpaths[0].Nodes[1].Control2, reopenedShape.Paths[0].Subpaths[0].Nodes[1].Control2);
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

    [AvaloniaFact]
    public async Task Whole_stroke_eraser_retains_cells_shapes_and_survivor_bundle_through_undo_save_and_reopen()
    {
        using var paths = new PhysicalPaths(); var repository = new DataWorkbookRepository(paths);
        var original = EraserWorkbook(); var originalSheet = original.Sheets[0];
        var shapeId = originalSheet.Drawings[0].Id; var erasedId = originalSheet.Drawings[1].Id;
        var shapeSnapshot = JsonSerializer.SerializeToElement(Assert.IsType<DocumentVectorShape>(originalSheet.Drawings[0].VectorShape));
        var survivor = originalSheet.Drawings[2].SharedProductivityBundle!.Value.Clone(); var survivorId = originalSheet.Drawings[2].Id;
        await repository.SaveAsync(original, "Seed eraser", TestContext.Current.CancellationToken);
        using var bus = new HavenEventBus();
        using var page = new DataPage(bus, repository, new DataXlsxFormatService(), new DataWorkbookQueryService(), null, null, Engine());
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(page.SharedInkInput!.EraserEnabled);
        var expected = Assert.IsType<CanvasSharedInkOwnerSnapshot>(page.CaptureSharedInkOwner());
        var request = EraseFirst(expected);
        var result = await page.CommitSharedInkStroke(request, TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded); Assert.Equal("Erased", result.Code); Assert.True(page.IsDirty);
        var sheet = page.Workbook!.Sheets[0];
        Assert.Equal(new[] { shapeId, survivorId }, sheet.Drawings.Select(drawing => drawing.Id));
        Assert.Equal("Keep this cell", sheet.GetCell(0, 0)!.Value);
        Assert.True(JsonElement.DeepEquals(survivor, sheet.Drawings[1].SharedProductivityBundle!.Value));
        // One existing workbook history entry restores the erased canonical object; no ink-only store/history.
        Invoke(page.SceneRoot.DescendantsAndSelf().OfType<HavenButton>().Single(value => value.Name == "Data.Grid.Undo"));
        sheet = page.Workbook.Sheets[0]; Assert.Equal(new[] { shapeId, erasedId, survivorId }, sheet.Drawings.Select(drawing => drawing.Id));
        Assert.True(JsonElement.DeepEquals(shapeSnapshot, JsonSerializer.SerializeToElement(Assert.IsType<DocumentVectorShape>(sheet.Drawings[0].VectorShape))));
        Invoke(page.SceneRoot.DescendantsAndSelf().OfType<HavenButton>().Single(value => value.Name == "Data.Grid.Redo"));
        Assert.Equal(new[] { shapeId, survivorId }, page.Workbook.Sheets[0].Drawings.Select(drawing => drawing.Id));
        Assert.True(await page.SaveAsync("Erase worksheet stroke", TestContext.Current.CancellationToken));
        var reopened = Assert.IsType<DataWorkbook>(await repository.LoadAsync(page.Workbook.Id, TestContext.Current.CancellationToken));
        Assert.Equal(new[] { shapeId, survivorId }, reopened.Sheets[0].Drawings.Select(drawing => drawing.Id));
        Assert.Equal("Keep this cell", reopened.Sheets[0].GetCell(0, 0)!.Value);
        Assert.True(JsonElement.DeepEquals(shapeSnapshot, JsonSerializer.SerializeToElement(Assert.IsType<DocumentVectorShape>(reopened.Sheets[0].Drawings[0].VectorShape))));
        Assert.True(JsonElement.DeepEquals(survivor, reopened.Sheets[0].Drawings[1].SharedProductivityBundle!.Value));
        var replay = await page.CommitSharedInkStroke(request, TestContext.Current.CancellationToken);
        Assert.False(replay.Succeeded); Assert.Equal("RevisionConflict", replay.Code); Assert.False(page.IsDirty);
    }

    [AvaloniaFact]
    public async Task Forged_replacement_or_replacement_without_an_eraser_gesture_cannot_remove_current_strokes()
    {
        using var paths = new PhysicalPaths(); var repository = new DataWorkbookRepository(paths); var original = EraserWorkbook();
        await repository.SaveAsync(original, "Seed eraser", TestContext.Current.CancellationToken);
        using var bus = new HavenEventBus();
        using var page = new DataPage(bus, repository, new DataXlsxFormatService(), new DataWorkbookQueryService(), null, null, Engine());
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var expected = Assert.IsType<CanvasSharedInkOwnerSnapshot>(page.CaptureSharedInkOwner());
        var actual = EraseFirst(expected); Assert.Single(actual.ReplacementBundles!);
        var forged = actual with { ReplacementBundles = [] };
        var refused = await page.CommitSharedInkStroke(forged, TestContext.Current.CancellationToken);
        Assert.False(refused.Succeeded); Assert.Equal("InvalidErasure", refused.Code);
        var absentGesture = actual with { Erasure = null };
        refused = await page.CommitSharedInkStroke(absentGesture, TestContext.Current.CancellationToken);
        Assert.False(refused.Succeeded); Assert.Equal("InvalidStroke", refused.Code);
        Assert.False(page.IsDirty); Assert.Equal(original.Sheets[0].Drawings.Select(drawing => drawing.Id), page.Workbook!.Sheets[0].Drawings.Select(drawing => drawing.Id));
        Invoke(page.SceneRoot.DescendantsAndSelf().OfType<HavenButton>().Single(value => value.Name == "Data.Grid.Undo"));
        Assert.False(page.IsDirty); Assert.Equal(3, page.Workbook.Sheets[0].Drawings.Count);
    }

    [AvaloniaFact]
    public async Task Eraser_cannot_remove_a_locked_stroke_and_a_miss_creates_no_history_or_dirty_write()
    {
        using var paths = new PhysicalPaths(); var repository = new DataWorkbookRepository(paths); var original = EraserWorkbook();
        original.Sheets[0].Drawings[1].Locked = true;
        await repository.SaveAsync(original, "Seed locked eraser", TestContext.Current.CancellationToken);
        using var bus = new HavenEventBus();
        using var page = new DataPage(bus, repository, new DataXlsxFormatService(), new DataWorkbookQueryService(), null, null, Engine());
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var expected = Assert.IsType<CanvasSharedInkOwnerSnapshot>(page.CaptureSharedInkOwner());
        var refused = await page.CommitSharedInkStroke(EraseFirst(expected), TestContext.Current.CancellationToken);
        Assert.False(refused.Succeeded); Assert.Equal("LockedDrawing", refused.Code);
        var miss = CanvasSharedInkStrokeFactory.CaptureWholeErase(Engine(), expected, [new(800, 400, .5), new(820, 420, .5)], 20);
        var unchanged = await page.CommitSharedInkStroke(miss, TestContext.Current.CancellationToken);
        Assert.True(unchanged.Succeeded); Assert.Equal("NoChange", unchanged.Code);
        Assert.False(page.IsDirty); Assert.Equal(3, page.Workbook!.Sheets[0].Drawings.Count);
        Assert.True(page.Workbook.Sheets[0].Drawings[1].Locked);
        Invoke(page.SceneRoot.DescendantsAndSelf().OfType<HavenButton>().Single(value => value.Name == "Data.Grid.Undo"));
        Assert.False(page.IsDirty); Assert.Equal(3, page.Workbook.Sheets[0].Drawings.Count);
    }

    [AvaloniaFact]
    public async Task Eraser_captured_on_another_selected_sheet_is_refused_before_any_owner_mutation()
    {
        using var paths = new PhysicalPaths(); var repository = new DataWorkbookRepository(paths); var original = EraserWorkbook();
        await repository.SaveAsync(original, "Seed sheet switch", TestContext.Current.CancellationToken);
        using var bus = new HavenEventBus();
        using var page = new DataPage(bus, repository, new DataXlsxFormatService(), new DataWorkbookQueryService(), null, null, Engine());
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        var firstSheet = page.Workbook!.Sheets[0];
        var request = EraseFirst(Assert.IsType<CanvasSharedInkOwnerSnapshot>(page.CaptureSharedInkOwner()));
        Invoke(page.Route.AddSheetButton);
        var current = Assert.IsType<CanvasSharedInkOwnerSnapshot>(page.CaptureSharedInkOwner());
        Assert.NotEqual(firstSheet.Id, current.LayerId);
        var refused = await page.CommitSharedInkStroke(request, TestContext.Current.CancellationToken);
        Assert.False(refused.Succeeded); Assert.Equal("RevisionConflict", refused.Code);
        Assert.Equal(3, firstSheet.Drawings.Count); Assert.Empty(page.Workbook.Sheets.Single(sheet => sheet.Id == current.LayerId).Drawings);
    }

    private static DataWorkbook EraserWorkbook()
    {
        var workbook = DataWorkbook.Create("Worksheet eraser"); var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Keep this cell"); var editor = new DataSheetDrawingEditor(sheet);
        editor.AddCustomShape(DocumentVectorPrimitives.Create(DocumentVectorPrimitive.Ellipse, width: 120, height: 80));
        foreach (var y in new[] { 40d, 220d })
        {
            var bundle = CanvasSharedInkStrokeFactory.Capture(Engine(), Context(workbook.Id), sheet.Id,
                [new(40, y, .4), new(160, y, .8)]);
            // Retained optional stroke data must survive an unrelated erasure byte-for-byte.
            var extended = JsonNode.Parse(bundle.GetRawText())!.AsObject();
            extended["objects"]![0]!["content"]!["futureOptionalStroke"] = "keep";
            bundle = CanvasSharedInkStrokeFactory.CaptureBundle(Engine(), Context(workbook.Id), JsonSerializer.SerializeToElement(extended));
            editor.AddSharedInk(Engine().ParseBundle(bundle.GetRawText()).Objects[0].ObjectId, bundle);
        }
        return workbook;
    }
    private static DocumentVectorShape ShapeWithPaths(params DocumentVectorPath[] paths)
    {
        var shape = DocumentVectorShapes.CreateEditableStarter();
        shape.Paths = paths.ToList();
        return shape;
    }
    private static DocumentVectorPath PathWithSubpaths(int subpathCount, int nodeCount = 2) => new()
    {
        Subpaths = Enumerable.Range(0, subpathCount).Select(_ => new DocumentVectorSubpath
        {
            Nodes = Enumerable.Range(0, nodeCount).Select(index => new DocumentVectorNode { X = index }).ToList()
        }).ToList()
    };
    private static CanvasSharedInkStrokeRequest EraseFirst(CanvasSharedInkOwnerSnapshot owner) =>
        CanvasSharedInkStrokeFactory.CaptureWholeErase(Engine(), owner, [new(100, 20, .5), new(100, 60, .5)], 20);

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
