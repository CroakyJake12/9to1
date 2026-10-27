using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;
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
        var requiredHeight = strokes
            .Where(stroke => stroke.Points.Count > 0)
            .Select(stroke => stroke.Points.Max(point => point.Y * zoom + panY) + stroke.Width + 48)
            .DefaultIfEmpty(0d)
            .Max();
        ApplyOverlayExtent(overlay, requiredHeight);
        foreach (var stroke in strokes)
        {
            if (stroke.Points.Count == 0) continue;
            AddRenderedStroke(overlay, stroke, panX, panY, zoom);
        }
    }

    /// <summary>Sizes the absolute ink layer to the page it covers. The authored layer has no
    /// intrinsic size, so it measures as an empty strip: strokes are clipped and the layer cannot
    /// receive pointer input, which made Draw look like it never activated. The page content is
    /// the floor and any stroke can extend past it.</summary>
    public static void ApplyOverlayExtent(Panel overlay, double strokeExtentHeight)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var contentHeight = 0d;
        var contentWidth = 0d;
        if (overlay.Parent is Panel siblings)
        {
            foreach (var sibling in siblings.Children)
            {
                if (ReferenceEquals(sibling, overlay) || sibling is not Control control)
                    continue;
                contentHeight = Math.Max(contentHeight, control.Bounds.Height);
                contentWidth = Math.Max(contentWidth, control.Bounds.Width);
            }
        }
        var required = Math.Max(Math.Max(contentHeight, strokeExtentHeight), 0d);
        if (required > overlay.Bounds.Height + 1)
        {
            overlay.Height = required;
            overlay.MinWidth = contentWidth > 0 ? contentWidth : double.NaN;
        }
        else if (overlay.Height > 0 && required < overlay.Bounds.Height - 24)
        {
            overlay.Height = double.NaN;
            overlay.MinWidth = double.NaN;
        }
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

        // The authored Boards overlay is CUI's Absolute layout (CuiAbsolutePanel). Its pixel
        // coordinates are centre-origin, but the centre belongs to the placement scope the panel
        // resolves - the nearest ancestor Container with an inherited axis, otherwise the
        // OUTERMOST container - and that scope's origin is added to the result. Assuming the
        // layer's own bounds made every stroke land away from the pointer, which the owner saw
        // as a constant pen offset. Resolve the same scope and convert through it.
        var (xScope, yScope) = PlacementScopes(overlay);
        var xOrigin = xScope is null ? default : xScope.TranslatePoint(default, overlay) ?? default;
        var yOrigin = yScope is null ? default : yScope.TranslatePoint(default, overlay) ?? default;
        var xLength = xScope?.Bounds.Width ?? overlay.Bounds.Width;
        var yLength = yScope?.Bounds.Height ?? overlay.Bounds.Height;
        var x = center.X - (xOrigin.X + xLength / 2);
        var y = (yOrigin.Y + yLength / 2) - center.Y;
        if (!CuiLayoutProperties.SetLiteral(element, "XPixel", x.ToString(CultureInfo.InvariantCulture),
                source: "Boards ink renderer") ||
            !CuiLayoutProperties.SetLiteral(element, "YPixel", y.ToString(CultureInfo.InvariantCulture),
                source: "Boards ink renderer"))
            throw new InvalidOperationException("The Boards drawing overlay does not expose CUI absolute pixel placement.");
    }

    private static readonly System.Reflection.PropertyInfo? InheritXProperty =
        typeof(CuiContainerControl).GetProperty("InheritX", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
    private static readonly System.Reflection.PropertyInfo? InheritYProperty =
        typeof(CuiContainerControl).GetProperty("InheritY", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

    /// <summary>Mirrors CuiAbsolutePanel.ArrangeOverride scope selection so the renderer and the
    /// runtime agree on which centre a pixel coordinate is measured from.</summary>
    private static (CuiContainerControl? X, CuiContainerControl? Y) PlacementScopes(Panel overlay)
    {
        var ancestors = overlay.GetVisualAncestors().OfType<CuiContainerControl>().ToArray();
        if (ancestors.Length == 0) return (null, null);
        return (
            ancestors.FirstOrDefault(container => AxisInherited(InheritXProperty, container)) ?? ancestors[^1],
            ancestors.FirstOrDefault(container => AxisInherited(InheritYProperty, container)) ?? ancestors[^1]);
    }

    private static bool AxisInherited(System.Reflection.PropertyInfo? property, CuiContainerControl container)
    {
        if (property?.GetValue(container) is bool inherited) return inherited;
        // Unknown axis flag: treat it as inherited so the nearest container wins, which is the
        // authored intent for the page surface and its drawing layer.
        return true;
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
