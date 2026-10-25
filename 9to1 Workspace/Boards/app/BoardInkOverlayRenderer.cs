using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using CakeOS.Cui.Runtime;
using System.Globalization;

namespace CakeOS.Apps.Boards.App;

/// <summary>Native visual adapter for the canonical page drawing layer.</summary>
public static class BoardInkOverlayRenderer
{
    public static async Task RenderReadOnlyAsync(Panel overlay, BoardsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(viewModel);
        var pageId = viewModel.SelectedPageId;
        var panX = viewModel.InkPanX;
        var panY = viewModel.InkPanY;
        var zoom = viewModel.InkZoom;
        var strokes = await viewModel.GetInkStrokesAsync(pageId);
        if (!string.Equals(pageId, viewModel.SelectedPageId, StringComparison.Ordinal)) return;
        overlay.Children.Clear();
        overlay.IsHitTestVisible = false;
        AutomationProperties.SetName(overlay, "Page drawing layer");
        var requiredHeight = 0d;
        foreach (var stroke in strokes)
        {
            if (stroke.Points.Count == 0) continue;
            requiredHeight = Math.Max(requiredHeight,
                stroke.Points.Max(point => point.Y * zoom + panY) + stroke.Width + 48);
            AddRenderedStroke(overlay, stroke, panX, panY, zoom);
        }
        if (requiredHeight > overlay.Bounds.Height + 1) overlay.Height = requiredHeight;
        else if (overlay.Height > 0 && requiredHeight < overlay.Bounds.Height - 24) overlay.Height = double.NaN;
    }

    public static void AddRenderedStroke(Panel overlay, InkStrokeView stroke, double panX, double panY, double zoom)
    {
        if (stroke.Points.Count == 1)
        {
            var point = ToOverlayCoordinates(stroke.Points[0].X, stroke.Points[0].Y, panX, panY, zoom);
            var diameter = EffectiveStrokeWidth(stroke, stroke.Points) * 1.5;
            AddDot(overlay, point, diameter, BrushForInk(stroke.Color));
            return;
        }

        var points = stroke.Points.Select(point => ToOverlayCoordinates(point.X, point.Y, panX, panY, zoom)).ToArray();
        AddPolyline(overlay, points, BrushForInk(stroke.Color), EffectiveStrokeWidth(stroke, stroke.Points),
            string.Equals(stroke.Tool, "Highlighter", StringComparison.OrdinalIgnoreCase) ? 0.42 : 1);
        if (!stroke.Selected) return;
        foreach (var endpoint in new[] { stroke.Points[0], stroke.Points[^1] })
        {
            var point = ToOverlayCoordinates(endpoint.X, endpoint.Y, panX, panY, zoom);
            AddDot(overlay, point, 10, Brushes.White, BoardsTheme.AccentBrush, 2);
        }
    }

    public static void AddLiveDot(Panel overlay, Point point, double diameter, IBrush fill) =>
        AddDot(overlay, point, diameter, fill, tag: "live-ink");

    public static void AddLiveStroke(Panel overlay, IReadOnlyList<Point> points, IBrush brush,
        double width, double opacity) => AddPolyline(overlay, points, brush, width, opacity, "live-ink");

    private static void AddDot(Panel overlay, Point center, double diameter, IBrush fill,
        IBrush? stroke = null, double strokeThickness = 0, object? tag = null)
    {
        var dot = new Ellipse
        {
            Tag = tag,
            Width = diameter,
            Height = diameter,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        PlaceAtCenter(overlay, dot, center);
        overlay.Children.Add(dot);
    }

    private static void AddPolyline(Panel overlay, IReadOnlyList<Point> points, IBrush brush,
        double width, double opacity, object? tag = null)
    {
        if (points.Count < 2) return;
        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);
        var boundsWidth = Math.Max(1, maxX - minX);
        var boundsHeight = Math.Max(1, maxY - minY);
        var polyline = new Polyline
        {
            Tag = tag,
            Points = points.Select(point => new Point(point.X - minX, point.Y - minY)).ToList(),
            Stroke = brush,
            StrokeThickness = width,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Opacity = opacity,
            Width = boundsWidth,
            Height = boundsHeight,
            Stretch = Stretch.None,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        PlaceAtCenter(overlay, polyline, new Point(minX + boundsWidth / 2, minY + boundsHeight / 2));
        overlay.Children.Add(polyline);
    }

    private static void PlaceAtCenter(Panel overlay, Control element, Point center)
    {
        if (overlay is Canvas)
        {
            Canvas.SetLeft(element, center.X - element.Width / 2);
            Canvas.SetTop(element, center.Y - element.Height / 2);
            return;
        }

        // The authored Boards overlay is CUI's Absolute layout (CuiAbsolutePanel).
        // Its pixel coordinates are centre-origin, so this adapter maps view pixels
        // into the layout's canonical XPixel/YPixel properties.
        var x = center.X - overlay.Bounds.Width / 2;
        var y = overlay.Bounds.Height / 2 - center.Y;
        if (!CuiLayoutProperties.SetLiteral(element, "XPixel", x.ToString(CultureInfo.InvariantCulture),
                source: "Boards ink renderer") ||
            !CuiLayoutProperties.SetLiteral(element, "YPixel", y.ToString(CultureInfo.InvariantCulture),
                source: "Boards ink renderer"))
            throw new InvalidOperationException("The Boards drawing overlay does not expose CUI absolute pixel placement.");
    }

    private static Point ToOverlayCoordinates(double x, double y, double panX, double panY, double zoom) =>
        new(x * zoom + panX, y * zoom + panY);

    private static double EffectiveStrokeWidth(InkStrokeView stroke, IReadOnlyList<InkPointView> points) =>
        Math.Clamp(stroke.Width * Math.Clamp(points.Average(point => point.Pressure), 0.25, 1), 0.8, 64);

    private static IBrush BrushForInk(string color)
    {
        try { return new SolidColorBrush(Color.Parse(color)); }
        catch { return Brushes.Black; }
    }
}
