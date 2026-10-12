using System.Globalization;
using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Present;

internal sealed partial class PresentSlideCanvas
{
    internal HavenRect GetElementEditorBounds(PresentElement element) => ElementRect(element, SlideRectLocal(), false);

    // These are document objects, so their authored palette/fonts must not follow
    // the application chrome theme. The same commands render editable slides and previews.
    private HavenBrush DocumentBackground => Brush(_document?.Theme.Colors.Background, "#FFFFFF");
    private HavenBrush DocumentForeground => Brush(_document?.Theme.Colors.Foreground, "#1F1F1F");

    private void DrawTable(HavenDrawingContext context, PresentTable table, HavenRect rect, double opacity)
    {
        context.Add(new HavenPushClipCommand(rect));
        context.Add(new HavenFillRoundedRectCommand(rect, DocumentBackground, 0, opacity));
        var pen = new HavenPen(DocumentForeground, .75);
        var cellWidth = rect.Width / table.Columns;
        var cellHeight = rect.Height / table.Rows;
        var inset = Math.Min(5, Math.Min(cellWidth, cellHeight) * .12);
        for (var row = 0; row < table.Rows; row++)
        for (var column = 0; column < table.Columns; column++)
        {
            var cell = table.GetCell(row, column);
            var bounds = new HavenRect(rect.X + column * cellWidth, rect.Y + row * cellHeight, cellWidth, cellHeight);
            if (!string.IsNullOrWhiteSpace(cell.FillColor))
                context.Add(new HavenFillRoundedRectCommand(bounds, Brush(cell.FillColor, _document?.Theme.Colors.Background ?? "#FFFFFF"), 0, opacity));
            if (string.IsNullOrEmpty(cell.Text)) continue;
            var textRect = new HavenRect(bounds.X + inset, bounds.Y + inset, Math.Max(.01, bounds.Width - inset * 2), Math.Max(.01, bounds.Height - inset * 2));
            context.Add(new HavenPushClipCommand(bounds));
            StructuredText(context, cell.Text, textRect, Math.Min(18, cellHeight * .3), table.HeaderRow && row == 0 ? 700 : 400, opacity, ellipsis: false);
            context.Add(new HavenPopClipCommand(bounds));
        }
        for (var column = 1; column < table.Columns; column++)
        {
            var x = rect.X + cellWidth * column;
            context.Add(new HavenLineCommand(new(x, rect.Y), new(x, rect.Bottom), pen, opacity));
        }
        for (var row = 1; row < table.Rows; row++)
        {
            var y = rect.Y + cellHeight * row;
            context.Add(new HavenLineCommand(new(rect.X, y), new(rect.Right, y), pen, opacity));
        }
        context.Add(new HavenStrokeRoundedRectCommand(rect, pen, 0, opacity));
        context.Add(new HavenPopClipCommand(rect));
    }

    private void DrawChart(HavenDrawingContext context, PresentChart chart, HavenRect rect, double opacity)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        context.Add(new HavenPushClipCommand(rect));
        context.Add(new HavenFillRoundedRectCommand(rect, DocumentBackground, 0, opacity));
        var unit = Math.Clamp(Math.Min(rect.Width / 600, rect.Height / 350), .02, 1);
        var padding = 12 * unit;
        var titleHeight = string.IsNullOrWhiteSpace(chart.Title) ? 0 : 34 * unit;
        if (titleHeight > 0)
            StructuredText(context, chart.Title, new(rect.X + padding, rect.Y + padding, rect.Width - padding * 2, titleHeight), 24 * unit, 700, opacity);
        var content = new HavenRect(rect.X + padding, rect.Y + padding + titleHeight, Math.Max(.01, rect.Width - padding * 2), Math.Max(.01, rect.Height - padding * 2 - titleHeight));
        var radial = chart.Type is PresentChartType.Pie or PresentChartType.Doughnut;
        var legendNames = radial ? chart.Categories : chart.Series.Select(series => series.Name).ToList();
        if (chart.ShowLegend)
        {
            var columns = Math.Max(1, (int)(content.Width / (160 * unit)));
            columns = Math.Min(columns, legendNames.Count);
            var rows = (int)Math.Ceiling(legendNames.Count / (double)columns);
            var legendHeight = Math.Min(content.Height * .3, rows * 28 * unit);
            DrawChartLegend(context, legendNames, new(content.X, content.Bottom - legendHeight, content.Width, legendHeight), columns, opacity);
            content = content with { Height = Math.Max(.01, content.Height - legendHeight - 8 * unit) };
        }
        if (radial) DrawRadialChart(context, chart, content, unit, opacity);
        else DrawCartesianChart(context, chart, content, unit, opacity);
        context.Add(new HavenPopClipCommand(rect));
    }

    private void DrawCartesianChart(HavenDrawingContext context, PresentChart chart, HavenRect content, double unit, double opacity)
    {
        var horizontal = chart.Type == PresentChartType.Bar;
        var plot = new HavenRect(content.X + (horizontal ? 120 : 76) * unit, content.Y + 8 * unit,
            Math.Max(.01, content.Width - (horizontal ? 132 : 88) * unit), Math.Max(.01, content.Height - 50 * unit));
        var values = chart.Series.SelectMany(series => series.Values).ToArray();
        // Scale before subtracting: finite opposite extremes otherwise overflow the
        // range, and tiny finite values must not be rounded away by a fixed epsilon.
        var magnitude = values.Max(value => Math.Abs(value));
        if (magnitude == 0) magnitude = 1;
        var min = Math.Min(0, values.Min() / magnitude);
        var max = Math.Max(0, values.Max() / magnitude);
        if (max == min) max = 1;
        double Ratio(double value) => (value / magnitude - min) / (max - min);
        double X(double value) => plot.X + Ratio(value) * plot.Width;
        double Y(double value) => plot.Bottom - Ratio(value) * plot.Height;
        var axis = new HavenPen(DocumentForeground, .8 * unit);
        for (var tick = 0; tick <= 4; tick++)
        {
            var ratio = tick / 4d;
            var zeroRatio = -min / (max - min);
            if (tick is > 0 and < 4 && Math.Abs(ratio - zeroRatio) < .1) ratio = zeroRatio;
            var label = ratio == zeroRatio ? "0" : ChartNumber((min + (max - min) * ratio) * magnitude);
            if (horizontal)
            {
                var x = plot.X + plot.Width * ratio;
                context.Add(new HavenLineCommand(new(x, plot.Y), new(x, plot.Bottom), axis, opacity * .18));
                StructuredText(context, label, new(x - 36 * unit, plot.Bottom + 5 * unit, 72 * unit, 24 * unit), 16 * unit, 400, opacity);
            }
            else
            {
                var y = plot.Bottom - plot.Height * ratio;
                context.Add(new HavenLineCommand(new(plot.X, y), new(plot.Right, y), axis, opacity * .18));
                StructuredText(context, label, new(content.X, y - 12 * unit, 70 * unit, 24 * unit), 16 * unit, 400, opacity);
            }
        }
        var zero = horizontal ? X(0) : Y(0);
        context.Add(horizontal
            ? new HavenLineCommand(new(zero, plot.Y), new(zero, plot.Bottom), axis, opacity)
            : new HavenLineCommand(new(plot.X, zero), new(plot.Right, zero), axis, opacity));
        var count = chart.Categories.Count;
        // The current owner model stores categorical X values, including Scatter.
        // Every series shares those categories; scatter draws markers without lines.
        var slot = (horizontal ? plot.Height : plot.Width) / count;
        for (var category = 0; category < count; category++)
        {
            var labelRect = horizontal
                ? new HavenRect(content.X, plot.Y + category * slot, 114 * unit, slot)
                : new HavenRect(plot.X + category * slot + 2 * unit, plot.Bottom + 5 * unit, Math.Max(.01, slot - 4 * unit), 30 * unit);
            StructuredText(context, chart.Categories[category], labelRect, Math.Min(16 * unit, horizontal ? slot * .7 : 16 * unit), 400, opacity);
        }
        var dataClip = new HavenRect(plot.X - 3 * unit, plot.Y - 3 * unit, plot.Width + 6 * unit, plot.Height + 6 * unit);
        context.Add(new HavenPushClipCommand(dataClip));
        for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            var brush = ChartBrush(seriesIndex);
            if (chart.Type is PresentChartType.Column or PresentChartType.Bar)
            {
                var group = slot * .76;
                var thickness = group / chart.Series.Count;
                for (var index = 0; index < count; index++)
                {
                    var value = series.Values[index];
                    if (value == 0) continue; // Zero has no area; do not invent a minimum-sized bar.
                    var position = (horizontal ? plot.Y : plot.X) + index * slot + (slot - group) / 2 + seriesIndex * thickness;
                    var end = horizontal ? X(value) : Y(value);
                    var bar = horizontal
                        ? new HavenRect(Math.Min(zero, end), position, Math.Abs(end - zero), thickness * .9)
                        : new HavenRect(position, Math.Min(zero, end), thickness * .9, Math.Abs(end - zero));
                    context.Add(new HavenFillRoundedRectCommand(bar, brush, 0, opacity));
                }
                continue;
            }
            var points = series.Values.Select((value, index) => new HavenPoint(plot.X + (index + .5) * slot, Y(value))).ToArray();
            if (chart.Type == PresentChartType.Area)
            {
                var polygon = new List<HavenPoint> { new(points[0].X, zero) };
                polygon.AddRange(points);
                polygon.Add(new(points[^1].X, zero));
                context.Add(new HavenGeometryCommand(plot, new HavenGeometry(new HavenPath(polygon, true)), brush, null, opacity * .28));
            }
            for (var index = 0; index < points.Length; index++)
            {
                var point = points[index];
                if (index > 0 && chart.Type != PresentChartType.Scatter)
                    context.Add(new HavenLineCommand(points[index - 1], point, new HavenPen(brush, 2 * unit), opacity));
                context.Add(new HavenEllipseCommand(new(point.X - 3 * unit, point.Y - 3 * unit, 6 * unit, 6 * unit), brush, null, opacity));
            }
        }
        context.Add(new HavenPopClipCommand(dataClip));
    }

    private void DrawRadialChart(HavenDrawingContext context, PresentChart chart, HavenRect content, double unit, double opacity)
    {
        // One labelled pie/ring per series preserves every series in the existing
        // model, rather than silently dropping all but the first.
        var columns = Math.Clamp((int)Math.Ceiling(Math.Sqrt(chart.Series.Count * content.Width / content.Height)), 1, chart.Series.Count);
        var rows = (int)Math.Ceiling(chart.Series.Count / (double)columns);
        var width = content.Width / columns;
        var height = content.Height / rows;
        for (var seriesIndex = 0; seriesIndex < chart.Series.Count; seriesIndex++)
        {
            var series = chart.Series[seriesIndex];
            var cell = new HavenRect(content.X + seriesIndex % columns * width, content.Y + seriesIndex / columns * height, width, height);
            var headingHeight = Math.Min(28 * unit, height * .16);
            StructuredText(context, series.Name, new(cell.X, cell.Y, cell.Width, headingHeight), Math.Min(18 * unit, headingHeight * .7), 600, opacity);
            var radius = Math.Max(.01, Math.Min(width * .44, (height - headingHeight) * .44));
            var centre = new HavenPoint(cell.X + width / 2, cell.Y + headingHeight + (height - headingHeight) / 2);
            var bounds = new HavenRect(centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
            var magnitude = series.Values.Max(value => Math.Abs(value));
            var total = magnitude == 0 ? 0 : series.Values.Sum(value => Math.Abs(value) / magnitude);
            if (total == 0)
            {
                context.Add(new HavenEllipseCommand(bounds, DocumentBackground, new HavenPen(DocumentForeground, unit), opacity));
                StructuredText(context, "0", new(centre.X - 8 * unit, centre.Y - 10 * unit, 16 * unit, 20 * unit), 12 * unit, 400, opacity);
                continue;
            }
            var start = -Math.PI / 2;
            for (var category = 0; category < chart.Categories.Count; category++)
            {
                var value = series.Values[category];
                if (value == 0) continue;
                // As in ordinary pie charts, sectors encode magnitudes. Signed
                // labels retain negative source values instead of relabelling them positive.
                var sweep = Math.Abs(value) / magnitude / total * Math.PI * 2;
                var inner = chart.Type == PresentChartType.Doughnut ? radius * .52 : 0;
                var path = ChartSector(centre, radius, inner, start, sweep);
                context.Add(new HavenGeometryCommand(bounds, new HavenGeometry(path), ChartBrush(category), new HavenPen(DocumentBackground, unit), opacity));
                var labelRadius = radius * (inner > 0 ? .76 : .64);
                var middle = start + sweep / 2;
                var labelWidth = Math.Min(60 * unit, radius * .72);
                var labelHeight = Math.Min(22 * unit, radius * .3);
                var labelRect = new HavenRect(centre.X + Math.Cos(middle) * labelRadius - labelWidth / 2, centre.Y + Math.Sin(middle) * labelRadius - labelHeight / 2, labelWidth, labelHeight);
                // The document background provides contrast on every authored accent.
                context.Add(new HavenFillRoundedRectCommand(labelRect, DocumentBackground, 2 * unit, opacity * .88));
                StructuredText(context, ChartNumber(value), labelRect, Math.Min(16 * unit, labelHeight * .75), 600, opacity);
                start += sweep;
            }
        }
    }

    private static HavenPath ChartSector(HavenPoint centre, double radius, double inner, double start, double sweep)
    {
        HavenPoint Point(double r, double angle) => new(centre.X + r * Math.Cos(angle), centre.Y + r * Math.Sin(angle));
        var middle = start + sweep / 2;
        var end = start + sweep;
        // Two arcs also render a full-circle single-valued series; one arc with
        // equal endpoints is empty on native rendering backends.
        var segments = new List<HavenPathSegment>
        {
            new HavenArcSegment(Point(radius, middle), new(radius, radius)),
            new HavenArcSegment(Point(radius, end), new(radius, radius))
        };
        if (inner > 0)
        {
            segments.Add(new HavenLineSegment(Point(inner, end)));
            segments.Add(new HavenArcSegment(Point(inner, middle), new(inner, inner), SweepDirection: HavenSweepDirection.CounterClockwise));
            segments.Add(new HavenArcSegment(Point(inner, start), new(inner, inner), SweepDirection: HavenSweepDirection.CounterClockwise));
        }
        else segments.Add(new HavenLineSegment(centre));
        return new HavenPath([new HavenPathFigure(Point(radius, start), segments, true)]);
    }

    private void DrawChartLegend(HavenDrawingContext context, IReadOnlyList<string> names, HavenRect rect, int columns, double opacity)
    {
        var rows = (int)Math.Ceiling(names.Count / (double)columns);
        var height = rect.Height / rows;
        var width = rect.Width / columns;
        var marker = Math.Min(12, height * .55);
        for (var index = 0; index < names.Count; index++)
        {
            var x = rect.X + index % columns * width;
            var y = rect.Y + index / columns * height;
            context.Add(new HavenFillRoundedRectCommand(new(x, y + (height - marker) / 2, marker, marker), ChartBrush(index), 0, opacity));
            StructuredText(context, names[index], new(x + marker * 1.5, y, Math.Max(.01, width - marker * 2), height), Math.Min(16, height * .65), 400, opacity);
        }
    }

    private HavenBrush ChartBrush(int index)
    {
        var colors = _document?.Theme.Colors ?? new PresentThemeColors();
        var palette = new[] { colors.Accent1, colors.Accent2, colors.Accent3, colors.Accent4, colors.Accent5, colors.Accent6 };
        var brush = (HavenSolidBrush)Brush(palette[index % palette.Length], "#4F7CAC");
        if (index < palette.Length) return brush;
        // Additional series keep the document palette with deterministic shades.
        var factor = 1 - (index / palette.Length % 5 + 1) * .12;
        return brush with { R = (byte)(brush.R * factor), G = (byte)(brush.G * factor), B = (byte)(brush.B * factor) };
    }

    private void StructuredText(HavenDrawingContext context, string text, HavenRect rect, double size, int weight, double opacity, bool ellipsis = true)
    {
        context.Add(new HavenTextCommand(rect, new HavenTextLayout(text, _document?.Theme.BodyFontFamily ?? "Aptos", Math.Max(.01, size), weight, rect.Width, true)
        { SingleLineEllipsis = ellipsis }, DocumentForeground, opacity));
    }

    private static string ChartNumber(double value) => value.ToString("G5", CultureInfo.InvariantCulture);
}
