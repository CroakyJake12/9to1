using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CakeOS.Cui.Runtime;
using Haven.UI;
using System.Runtime.CompilerServices;

namespace NineToOne.Web;

/// <summary>Resolves canonical Image sources from the maintained vector catalog.
/// This adapter performs no file/network access and owns no application controls.</summary>
internal static class BrowserCanonicalIcons
{
    private static readonly ConditionalWeakTable<CuiControlRegistry, object> Registrations = new();
    private static readonly object RegistrationLock = new();
    internal static void Register(CuiControlRegistry registry)
    {
        lock (RegistrationLock)
        {
            if (Registrations.TryGetValue(registry, out _)) return;
            registry.RegisterImageSourceResolver(Resolve);
            Registrations.Add(registry, new object());
        }
    }

    private static IImage? Resolve(string source, Control target)
    {
        if (!source.StartsWith("icon:", StringComparison.Ordinal)) return null;
        var key = source[5..];
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64 || key.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("A catalog icon requires a bounded symbolic key.", nameof(source));
        if (!target.Resources.TryGetResource("CuiTextBrush", null, out var resource) || resource is not IBrush brush)
            throw new InvalidOperationException("The icon's scoped CUI text brush is unavailable.");
        var sourceGeometry = HavenIconCatalog.Resolve(key);
        var geometry = new StreamGeometry();
        using (var writer = geometry.Open())
        {
            writer.SetFillRule(sourceGeometry.Path.FillRule == HavenFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd);
            foreach (var figure in sourceGeometry.Path.Figures)
            {
                writer.BeginFigure(Point(figure.Start), false);
                foreach (var segment in figure.Segments)
                {
                    switch (segment)
                    {
                        case HavenLineSegment line: writer.LineTo(Point(line.End), true); break;
                        case HavenQuadraticBezierSegment quadratic: writer.QuadraticBezierTo(Point(quadratic.Control), Point(quadratic.End), true); break;
                        case HavenCubicBezierSegment cubic: writer.CubicBezierTo(Point(cubic.Control1), Point(cubic.Control2), Point(cubic.End), true); break;
                        case HavenArcSegment arc: writer.ArcTo(Point(arc.End), new Size(arc.Radius.Width, arc.Radius.Height), arc.RotationDegrees, arc.IsLargeArc,
                            arc.SweepDirection == HavenSweepDirection.Clockwise ? SweepDirection.Clockwise : SweepDirection.CounterClockwise, true); break;
                        default: throw new InvalidOperationException($"Unsupported canonical icon segment: {segment.GetType().Name}.");
                    }
                }
                writer.EndFigure(figure.Closed);
            }
        }
        var image = new DrawingImage(new GeometryDrawing
        {
            Geometry = geometry,
            Pen = new Pen(brush, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
        });
        if (sourceGeometry.ViewBox is { } viewBox)
            image.Viewbox = new Rect(viewBox.X, viewBox.Y, viewBox.Width, viewBox.Height);
        return image;
    }

    private static Point Point(HavenPoint point) => new(point.X, point.Y);
}
