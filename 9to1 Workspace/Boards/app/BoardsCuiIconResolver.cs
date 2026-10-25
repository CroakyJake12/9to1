using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Haven.UI;

namespace CakeOS.Apps.Boards.App;

/// <summary>Resolves the Boards-owned folder artwork used by authored CUI.</summary>
public static class BoardsCuiIconResolver
{
    public static IImage? Resolve(string source, Control target)
    {
        if (!source.StartsWith("icon:", StringComparison.Ordinal)) return null;
        if (!target.TryFindResource("CuiTextBrush", out var resource) || resource is not IBrush brush)
            throw new InvalidOperationException("A Boards catalog icon requires the scoped CUI text brush.");
        return CreateIcon(source[5..], brush);
    }

    public static DrawingImage CreateIcon(string key, IBrush brush)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(brush);
        var source = HavenIconCatalog.Resolve(key);
        var geometry = new StreamGeometry();
        using (var writer = geometry.Open())
        {
            writer.SetFillRule(source.Path.FillRule == HavenFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd);
            foreach (var figure in source.Path.Figures)
            {
                writer.BeginFigure(new Point(figure.Start.X, figure.Start.Y), false);
                foreach (var segment in figure.Segments)
                {
                    switch (segment)
                    {
                        case HavenLineSegment line:
                            writer.LineTo(new Point(line.End.X, line.End.Y), true);
                            break;
                        case HavenQuadraticBezierSegment quadratic:
                            writer.QuadraticBezierTo(new Point(quadratic.Control.X, quadratic.Control.Y),
                                new Point(quadratic.End.X, quadratic.End.Y), true);
                            break;
                        case HavenCubicBezierSegment cubic:
                            writer.CubicBezierTo(new Point(cubic.Control1.X, cubic.Control1.Y),
                                new Point(cubic.Control2.X, cubic.Control2.Y), new Point(cubic.End.X, cubic.End.Y), true);
                            break;
                        case HavenArcSegment arc:
                            writer.ArcTo(new Point(arc.End.X, arc.End.Y),
                                new Size(arc.Radius.Width, arc.Radius.Height), arc.RotationDegrees, arc.IsLargeArc,
                                arc.SweepDirection == HavenSweepDirection.Clockwise
                                    ? SweepDirection.Clockwise
                                    : SweepDirection.CounterClockwise, true);
                            break;
                        default:
                            throw new InvalidOperationException($"Unsupported Boards icon segment '{segment.GetType().Name}'.");
                    }
                }
                writer.EndFigure(figure.Closed);
            }
        }
        return new DrawingImage(new GeometryDrawing
        {
            Geometry = geometry,
            Pen = new Pen(brush, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
        })
        {
            Viewbox = source.ViewBox is { } viewBox
                ? new Rect(viewBox.X, viewBox.Y, viewBox.Width, viewBox.Height)
                : new Rect(0, 0, 24, 24),
        };
    }
}
