using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.UI;
using NineToOne.Web.Productivity.Present;

internal static class PresentStructuredRenderingChecks
{
    public static async Task Run(Window window, PresentRetainedSceneControl control, PresentBrowserSession session, Action<bool, string> check)
    {
        var editor = session.Editor!;
        var scene = control.GetVisualDescendants().OfType<HavenSceneControl>().Single();
        var source = (IHavenDrawCommandSource)scene.Root!;
        void Clear() { editor.SelectElements(editor.SelectedSlide.Elements.Select(element => element.Id)); editor.RemoveSelectedElements(); }
        void Place(PresentElement element)
        { editor.TransformSelection(.06 - element.X, .1 - element.Y, .88 - element.Width, .78 - element.Height, -element.RotationDegrees); }
        HavenDrawingContext Draw(string? frameName = null)
        {
            editor.SelectElements([]); control.SetEditor(editor); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            if (frameName is not null) frame!.Save(Path.Combine(Environment.GetEnvironmentVariable("HAVEN_DATA_DIR")!, frameName + ".png"));
            var context = new HavenDrawingContext(); source.Draw(context, 1); return context;
        }
        bool Text(HavenDrawingContext draw, string value) => draw.Commands.OfType<HavenTextCommand>().Any(command => command.Layout.Text == value);
        // Deliberately non-default document colours prove chart content is not chrome-themed.
        editor.Document.Theme.Colors.Accent1 = "#2477AA";
        editor.Document.Theme.Colors.Accent2 = "#B83552";
        editor.Document.Theme.BodyFontFamily = "Montserrat";
        var first = new HavenSolidBrush(255, 0x24, 0x77, 0xAA);
        var second = new HavenSolidBrush(255, 0xB8, 0x35, 0x52);
        bool SeriesBrush(HavenBrush? brush) => Equals(brush, first) || Equals(brush, second);
        Clear();
        var chartElement = editor.AddChart(editor.Selection.SlideId); Place(chartElement);
        editor.SetChartData(editor.Selection.SlideId, chartElement.Id, ["January", "B", "C", "Category 4"],
        [new() { Name = "Revenue", Values = [12, 5, 4, 0] }, new() { Name = "Costs", Values = [-3.5, 0, 0, 0] }]);
        var authored = chartElement.ReadChart(); authored.Title = "Revenue and costs"; chartElement.WriteChart(authored);
        editor.SetChartStyle(editor.Selection.SlideId, chartElement.Id, authored.Style, false);
        var serialized = JsonSerializer.Serialize(chartElement.ReadChart());
        var draw = Draw();
        check(serialized == JsonSerializer.Serialize(chartElement.ReadChart()), "drawing a chart never mutates its owner data or style");
        var bars = draw.Commands.OfType<HavenFillRoundedRectCommand>().Where(command => SeriesBrush(command.Brush)).ToArray();
        check(bars.Length == 4 && bars.Count(command => Equals(command.Brush, first)) == 3 && bars.Count(command => Equals(command.Brush, second)) == 1,
            "column chart renders every nonzero value of both series without inventing bars for zeros");
        var positive = bars.First(command => Equals(command.Brush, first)); var negative = bars.Single(command => Equals(command.Brush, second));
        check(Math.Abs(positive.Bounds.Bottom - negative.Bounds.Y) < .001 && Math.Abs(negative.Bounds.Height / positive.Bounds.Height - 3.5 / 12) < .001,
            "negative columns extend below the shared zero baseline with exact proportional height");
        check(Text(draw, "January") && Text(draw, "Category 4") && Text(draw, "Revenue and costs") && !Text(draw, "Revenue") && !Text(draw, "Costs"),
            "chart title and actual categories render while the disabled legend is absent");
        check(draw.Commands.OfType<HavenTextCommand>().Where(command => command.Layout.Text is "January" or "Revenue and costs").All(command => command.Layout.FontFamily == "Montserrat"),
            "structured document text follows the authored presentation font");

        foreach (var type in Enum.GetValues<PresentChartType>())
        {
            editor.SetChartType(editor.Selection.SlideId, chartElement.Id, type);
            editor.SetChartStyle(editor.Selection.SlideId, chartElement.Id, authored.Style, true);
            draw = Draw("chart-" + type.ToString().ToLowerInvariant());
            check(Text(draw, "Revenue and costs") && Text(draw, "January") && Text(draw, "Category 4"), type + " renders title and every named category");
            check(Text(draw, "Revenue") && Text(draw, "Costs"), type + " preserves both named series in the rendered document");
            var markers = draw.Commands.OfType<HavenEllipseCommand>().Where(command => SeriesBrush(command.Brush)).ToArray();
            var lines = draw.Commands.OfType<HavenLineCommand>().Where(command => SeriesBrush(command.Pen.Brush)).ToArray();
            var geometry = draw.Commands.OfType<HavenGeometryCommand>().ToArray();
            switch (type)
            {
                case PresentChartType.Column:
                    check(draw.Commands.OfType<HavenFillRoundedRectCommand>().Count(command => SeriesBrush(command.Brush)) == 6,
                        "column geometry contains four real bars and two series legend markers");
                    break;
                case PresentChartType.Bar:
                    var horizontalBars = draw.Commands.OfType<HavenFillRoundedRectCommand>().Where(command => SeriesBrush(command.Brush) && command.Bounds.Width != command.Bounds.Height).ToArray();
                    positive = horizontalBars.First(command => Equals(command.Brush, first)); negative = horizontalBars.Single(command => Equals(command.Brush, second));
                    check(horizontalBars.Length == 4 && Math.Abs(positive.Bounds.X - negative.Bounds.Right) < .001 && Math.Abs(negative.Bounds.Width / positive.Bounds.Width - 3.5 / 12) < .001,
                        "bar chart is horizontal and preserves the signed zero baseline and both series");
                    break;
                case PresentChartType.Line:
                    check(markers.Length == 8 && lines.Length == 6 && geometry.Length == 0,
                        "line chart renders all eight data points and independent connected paths for both series");
                    break;
                case PresentChartType.Area:
                    check(markers.Length == 8 && lines.Length == 6 && geometry.Length == 2 && geometry.All(command => command.Opacity == .28),
                        "area chart fills both series to zero and retains both line boundaries");
                    break;
                case PresentChartType.Scatter:
                    check(markers.Length == 8 && lines.Length == 0 && geometry.Length == 0,
                        "scatter chart plots all series as independent points without spurious connecting lines");
                    var positivePoint = markers.First(command => Equals(command.Brush, first)); var negativePoint = markers.First(command => Equals(command.Brush, second));
                    check(positivePoint.Bounds.Y < negativePoint.Bounds.Y && Math.Abs(positivePoint.Bounds.X - negativePoint.Bounds.X) < .001,
                        "scatter negative values are lower than positives at the same real category");
                    break;
                case PresentChartType.Pie:
                case PresentChartType.Doughnut:
                    check(geometry.Length == 4 && Text(draw, "-3.5") && Text(draw, "12"), type + " represents all nonzero values including signed labels in every series");
                    var reverseArcs = geometry.SelectMany(command => command.Geometry.Path.Figures).SelectMany(figure => figure.Segments).OfType<HavenArcSegment>().Count(arc => arc.SweepDirection == HavenSweepDirection.CounterClockwise);
                    check(reverseArcs == (type == PresentChartType.Doughnut ? 8 : 0), type + " uses actual sector geometry with the correct open or filled centre");
                    check(geometry.All(command => command.Geometry.Path.Figures.Single().Segments.OfType<HavenArcSegment>().Count() >= 2),
                        type + " renders a single-valued series as a full circle using two nondegenerate arcs");
                    break;
            }
            check(draw.Commands.All(command => double.IsFinite(command.Bounds.X) && double.IsFinite(command.Bounds.Y) && double.IsFinite(command.Bounds.Width) && double.IsFinite(command.Bounds.Height)),
                type + " emits finite geometry for the actual native drawing backend");
        }
        window.Width = 850; window.Height = 650; draw = Draw("chart-small-window");
        check(Text(draw, "Revenue") && Text(draw, "Costs") && draw.Commands.OfType<HavenEllipseCommand>().Count(command => SeriesBrush(command.Brush)) == 8,
            "the real chart retains both series and complete points when its editing viewport shrinks");
        window.Width = 1200; window.Height = 900;
        // Normalized finite extremes and a dense chart remain valid, including a
        // series whose value range would overflow if max-min was computed first.
        editor.SetChartType(editor.Selection.SlideId, chartElement.Id, PresentChartType.Column);
        editor.SetChartData(editor.Selection.SlideId, chartElement.Id, ["Low", "High"], [new() { Name = "Extremes", Values = [-double.MaxValue, double.MaxValue] }]);
        draw = Draw();
        check(draw.Commands.All(command => double.IsFinite(command.Bounds.X) && double.IsFinite(command.Bounds.Y) && double.IsFinite(command.Bounds.Width) && double.IsFinite(command.Bounds.Height)),
            "opposite finite double extremes cannot overflow chart scaling");
        editor.SetChartData(editor.Selection.SlideId, chartElement.Id, Enumerable.Range(1, 100).Select(index => "Category " + index),
            Enumerable.Range(1, 32).Select(index => new PresentChartSeries { Name = "Series " + index, Values = Enumerable.Range(1, 100).Select(value => (double)(index * value)).ToList() }));
        draw = Draw();
        check(Text(draw, "Series 32") && Text(draw, "Category 100") && draw.Commands.OfType<HavenFillRoundedRectCommand>().Count() >= 3232,
            "the model's full 32-series 100-category limit does not silently discard chart data or legend entries");
        editor.SetChartData(editor.Selection.SlideId, chartElement.Id, ["Only"], [new() { Name = "Empty", Values = [0] }]);
        editor.SetChartType(editor.Selection.SlideId, chartElement.Id, PresentChartType.Doughnut);
        draw = Draw("chart-zero");
        check(Text(draw, "0") && !draw.Commands.OfType<HavenGeometryCommand>().Any(), "all-zero radial data renders an honest zero instead of invented wedges");

        Clear();
        var tableElement = editor.AddTable(editor.Selection.SlideId, 3, 3); Place(tableElement);
        editor.ResizeTable(editor.Selection.SlideId, tableElement.Id, 4, 4);
        editor.SetTableCellText(editor.Selection.SlideId, tableElement.Id, 0, 0, "Milestone");
        editor.SetTableCellText(editor.Selection.SlideId, tableElement.Id, 1, 1, "Design review");
        editor.SetTableCellText(editor.Selection.SlideId, tableElement.Id, 3, 3, "A long cell value that must stay inside its own cell even when the table is narrowed");
        var table = tableElement.ReadTable(); table.GetCell(1, 1).FillColor = "#DDEEFF"; tableElement.WriteTable(table);
        draw = Draw("table-edited");
        check(Text(draw, "Milestone") && Text(draw, "Design review"), "the resized real table renders original owner-edited cell content");
        check(draw.Commands.OfType<HavenTextCommand>().Single(command => command.Layout.Text == "Milestone").Layout.FontWeight == 700
            && draw.Commands.OfType<HavenTextCommand>().Single(command => command.Layout.Text == "Design review").Layout.FontWeight == 400,
            "table header-row styling preserves the distinction from ordinary cell text");
        check(draw.Commands.OfType<HavenFillRoundedRectCommand>().Any(command => Equals(command.Brush, new HavenSolidBrush(255, 0xDD, 0xEE, 0xFF))),
            "the real table honours each cell's persisted fill colour");
        check(draw.Commands.OfType<HavenLineCommand>().Count() == 6 && draw.Commands.OfType<HavenPushClipCommand>().Count() >= 5,
            "a four by four table renders all six inner grid lines and clips cell content locally");
        window.Width = 850; window.Height = 650; draw = Draw("table-small-window");
        check(Text(draw, "Design review"), "structured content survives the actual narrower editing viewport");
        var saved = await session.SaveAsync();
        var id = session.DocumentId; var snapshot = JsonSerializer.Serialize(session.GetDocumentSnapshot());
        check(saved.Succeeded && !session.IsDirty, "structured owner data saves through the actual repository");
        await session.CloseAsync(); var reopened = await session.OpenAsync(id!.Value);
        check(reopened.Succeeded && snapshot == JsonSerializer.Serialize(session.GetDocumentSnapshot()), "structured rendering preserves complete owner data across repository save and reopen");
    }
}
