using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Core;
using NineToOne.Web.Productivity.Present;

internal static class PresentUiAcceptanceChecks
{
    public static async Task Run(PresentBrowserSession session, Window window, Control root,
        Func<string, Func<bool>, Task> click, Action<bool, string> check, string evidenceDirectory)
    {
        void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame(); }
        Control Named(string name)
        {
            var matches = root.GetVisualDescendants().OfType<Control>().Where(control => control is TextBox or NumericUpDown
                && control.IsEffectivelyVisible && AutomationProperties.GetName(control) == name).ToArray();
            // A CUI numeric field delegates typing to its named template TextBox.
            // Target the authored field before descending into that template.
            return (Control?)matches.OfType<NumericUpDown>().SingleOrDefault() ?? matches.OfType<TextBox>().Single();
        }
        void Type(string name, string value)
        {
            var control = Named(name);
            control.BringIntoView(); Flush();
            var input = control is TextBox text ? text : control.GetVisualDescendants().OfType<TextBox>().First();
            var point = input.TranslatePoint(new Point(input.Bounds.Width / 2, input.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            window.KeyTextInput(value); Flush();
        }
        T Bound<T>(string name) { check(session.TryGetValue(name, out var value), "CUI exposes " + name); return (T)value!; }
        PresentElement Selected() => session.Editor!.SelectedElements.Single();
        PresentTable Table() => Selected().ReadTable();
        PresentChart Chart() => Selected().ReadChart();

        await click("Table", () => session.Editor!.SelectedElements is [{ Kind: PresentElementKind.Table }]);
        var tableId = Selected().Id;
        check(Table().Rows == 3 && Table().Columns == 3, "actual Insert Table creates one original typed table with editable cells");
        check(Bound<bool>("ShowInspector"), "inserting a table opens its actual data inspector");
        Type("Column 1", "Milestone");
        check(Table().GetCell(0, 0).Text == "Milestone", "physical CUI cell typing reaches the original table owner immediately");
        var staleCell = Bound<PresentContentField[]>("TableCells")[0];
        await click("Insert row below", () => Table().Rows == 4);
        check(!staleCell.TrySetValue("Text", "stale overwrite") && Table().GetCell(0, 0).Text == "Milestone",
            "a cell callback captured before table structure changed cannot overwrite current content");
        check(session.TrySetValue("TableRowIndex", 1), "row selection accepts a real table row");
        Type("Column 2", "Design review");
        check(Table().GetCell(1, 1).Text == "Design review", "editing a different row targets the intended real cell");
        await click("Add column", () => Table().Columns == 4);
        check(Table().GetCell(1, 1).Text == "Design review", "column insertion preserves existing cell content");
        await click("Undo", () => Table().Columns == 3);
        await click("Redo", () => Table().Columns == 4);
        Flush(); using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(evidenceDirectory, "present-table-data.png"));

        var beforeInvalidChart = session.Editor!.SelectedSlide.Elements.Count;
        await session.DispatchAsync("AddChart", "unsupported-chart");
        check(session.ErrorCode == "InvalidArgument" && session.Editor.SelectedSlide.Elements.Count == beforeInvalidChart,
            "an unsupported chart type is rejected rather than silently inserting a different chart");
        await click("Column chart", () => session.Editor!.SelectedElements is [{ Kind: PresentElementKind.Chart }]);
        var chartId = Selected().Id;
        Type("Series name", "Revenue"); Type("Category 1", "January"); Type("Value 1", "12");
        check(Chart().Series[0].Name == "Revenue" && Chart().Categories[0] == "January" && Chart().Series[0].Values[0] == 12,
            "physical chart fields edit canonical series, category and numeric data");
        var stalePoint = Bound<PresentChartDataRow[]>("ChartDataRows")[0];
        await click("Add series", () => Chart().Series.Count == 2);
        check(!stalePoint.TrySetValue("Value", 999), "stale chart data callbacks are rejected after a structural mutation");
        check(session.TrySetValue("ChartSeriesIndex", 1), "chart data inspector can select a second series");
        Type("Series name", "Costs"); Type("Value 1", "-3.5");
        check(Chart().Series[0].Values[0] == 12 && Chart().Series[1].Values[0] == -3.5,
            "editing the second series preserves the first and supports finite negative values");
        foreach (var type in Enum.GetValues<PresentChartType>())
        {
            check(session.TrySetValue("ChartTypeIndex", (int)type) && Chart().Type == type,
                "chart type changes retain owner data for " + type);
        }
        check(session.TrySetValue("ChartShowLegend", false) && !Chart().ShowLegend, "legend visibility persists in the original chart properties");
        await click("Add category", () => Chart().Categories.Count == 4);
        check(Chart().Series.All(series => series.Values.Count == 4), "adding a category keeps every series aligned");
        var chartBeforeInvalid = System.Text.Json.JsonSerializer.Serialize(Chart());
        var numericField = Bound<PresentChartDataRow[]>("ChartDataRows")[0];
        check(!numericField.TrySetValue("Value", double.NaN) && chartBeforeInvalid == System.Text.Json.JsonSerializer.Serialize(Chart()),
            "non-finite chart input is rejected without changing the document");
        Flush(); using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(evidenceDirectory, "present-chart-data.png"));

        var editor = session.Editor!;
        editor.SelectElements([tableId]); session.RefreshSelection();
        var oldTableBinding = Bound<PresentContentField[]>("TableCells")[0];
        editor.SelectElements([chartId]); session.RefreshSelection();
        check(!oldTableBinding.TrySetValue("Text", "wrong selection"), "table fields reject callbacks after selecting a different object");
        Selected().Locked = true; session.RefreshSelection();
        check(session.IsActionAvailable("EditChart") == false && !session.TrySetValue("ChartTypeIndex", 0),
            "locked charts expose no editable data capability");
        Selected().Locked = false; session.RefreshSelection();

        var documentId = session.DocumentId!.Value;
        var title = session.GetDocumentSnapshot()!.Title;
        await click("Save", () => !session.IsDirty);
        await click("Close", () => session.DocumentId is null);
        await click(title, () => session.DocumentId == documentId);
        var reopened = session.GetDocumentSnapshot()!;
        var persistedTable = reopened.Slides.SelectMany(slide => slide.Elements).Single(item => item.Id == tableId).ReadTable();
        var persistedChart = reopened.Slides.SelectMany(slide => slide.Elements).Single(item => item.Id == chartId).ReadChart();
        check(persistedTable.Rows == 4 && persistedTable.Columns == 4 && persistedTable.GetCell(0, 0).Text == "Milestone"
            && persistedTable.GetCell(1, 1).Text == "Design review", "table dimensions, identity and cell data survive real save, close and reopen");
        check(persistedChart.Type == PresentChartType.Scatter && persistedChart.Categories[0] == "January"
            && persistedChart.Series[0].Name == "Revenue" && persistedChart.Series[0].Values[0] == 12
            && persistedChart.Series[1].Name == "Costs" && persistedChart.Series[1].Values[0] == -3.5 && !persistedChart.ShowLegend,
            "chart type, categories, independent series values and legend survive save, close and reopen");

        await click("Add text", () => session.Editor!.SelectedElements is [{ Kind: PresentElementKind.Text }]);
        foreach (var width in new[] { 800d, 1000, 1200, 1440 })
        {
            window.Width = width; Flush();
            var toolbar = root.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "present-toolbar");
            var buttons = toolbar.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible
                && control is Button or ComboBox or NumericUpDown or MenuItem && control.Bounds.Width > 0).ToArray();
            check(buttons.All(control => control.TranslatePoint(new Point(control.Bounds.Width, 0), toolbar) is { } edge && edge.X <= toolbar.Bounds.Width + 1),
                "single toolbar controls remain inside its available width at " + width);
            using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(evidenceDirectory, $"present-toolbar-{width:0}.png"));
        }

        window.Width = 1280; Flush();
        await click("Add shape", () => session.Editor!.SelectedElements is [{ Kind: PresentElementKind.Shape }]);
        var shapeId = Selected().Id;
        var sceneControl = root.GetVisualDescendants().OfType<PresentRetainedSceneControl>().Single();
        var scene = sceneControl.GetVisualDescendants().OfType<Haven.Desktop.HavenUI.Backend.HavenSceneControl>().Single();
        var paper = (Haven.UI.HavenRect)scene.Root!.GetType().GetMethod("SlideRectLocal",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(scene.Root, null)!;
        var shape = Selected();
        var textPoint = scene.TranslatePoint(new Point(paper.X + (shape.X + shape.Width / 2) * paper.Width,
            paper.Y + (shape.Y + shape.Height / 2) * paper.Height), window)!.Value;
        window.MouseDown(textPoint, MouseButton.Left); window.MouseUp(textPoint, MouseButton.Left);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter"); Flush();
        check(sceneControl.IsTextEditing && Bound<bool>("ShowTextToolbar") && !Bound<bool>("ShowShapeToolbar"),
            "entering actual shape text swaps the single toolbar to text formatting without a duplicate toolbar");
        Type("Font size", "30");
        await click("Bold", () => Selected().TextStyle.Bold);
        check(Selected().Id == shapeId && Selected().TextStyle.FontSizePoints == 30 && Selected().TextStyle.Bold,
            "physical shape text formatting updates its existing text style and preserves object identity");
        await click("Undo", () => !Selected().TextStyle.Bold);
        check(Selected().TextStyle.FontSizePoints == 30, "undo shape bold retains the preceding exact point size");
        window.MouseDown(textPoint, MouseButton.Left); window.MouseUp(textPoint, MouseButton.Left);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "Escape");
        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "Escape"); Flush();
        check(!sceneControl.IsTextEditing && Bound<bool>("ShowShapeToolbar") && !Bound<bool>("ShowTextToolbar"),
            "leaving shape text restores the original object appearance controls");
        Type("Selected object text", "Shape inspector text");
        check(Selected().Text == "Shape inspector text" && Bound<string>("SelectedText") == "Shape inspector text",
            "the actual Properties text field reads and writes the same shape text owner");
        Selected().Locked = true; session.RefreshSelection();
        check(!Bound<bool>("CanEditSelectedText") && !session.TrySetValue("SelectedText", "Forbidden change")
            && Selected().Text == "Shape inspector text", "locked shape text is disabled and rejects inspector writeback");
        Selected().Locked = false; session.RefreshSelection();
    }
}
