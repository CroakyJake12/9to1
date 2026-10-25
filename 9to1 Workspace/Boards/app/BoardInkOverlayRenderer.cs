using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace CakeOS.Apps.Boards.App;

/// <summary>Native visual adapter for the canonical page drawing layer.</summary>
public static class BoardInkOverlayRenderer
{
    public static async Task RenderReadOnlyAsync(Canvas overlay, BoardsViewModel viewModel)
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

    public static void AddRenderedStroke(Canvas overlay, InkStrokeView stroke, double panX, double panY, double zoom)
    {
        if (stroke.Points.Count == 1)
        {
            var point = ToOverlayCoordinates(stroke.Points[0].X, stroke.Points[0].Y, panX, panY, zoom);
            var diameter = EffectiveStrokeWidth(stroke, stroke.Points) * 1.5;
            var dot = new Ellipse { Width = diameter, Height = diameter, Fill = BrushForInk(stroke.Color) };
            Canvas.SetLeft(dot, point.X - diameter / 2);
            Canvas.SetTop(dot, point.Y - diameter / 2);
            overlay.Children.Add(dot);
            return;
        }

        var polyline = new Polyline
        {
            Points = stroke.Points.Select(point => ToOverlayCoordinates(point.X, point.Y, panX, panY, zoom)).ToList(),
            Stroke = BrushForInk(stroke.Color),
            StrokeThickness = EffectiveStrokeWidth(stroke, stroke.Points),
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Opacity = string.Equals(stroke.Tool, "Highlighter", StringComparison.OrdinalIgnoreCase) ? 0.42 : 1,
        };
        overlay.Children.Add(polyline);
        if (!stroke.Selected) return;
        foreach (var endpoint in new[] { stroke.Points[0], stroke.Points[^1] })
        {
            var point = ToOverlayCoordinates(endpoint.X, endpoint.Y, panX, panY, zoom);
            var handle = new Ellipse { Width = 10, Height = 10, Fill = Brushes.White, Stroke = BoardsTheme.AccentBrush, StrokeThickness = 2 };
            Canvas.SetLeft(handle, point.X - 5);
            Canvas.SetTop(handle, point.Y - 5);
            overlay.Children.Add(handle);
        }
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
