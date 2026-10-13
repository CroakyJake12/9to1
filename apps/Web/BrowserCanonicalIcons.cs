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
    internal static void Register(CuiControlRegistry registry, Func<string, Control, IImage?>? additionalSource = null)
    {
        lock (RegistrationLock)
        {
            if (Registrations.TryGetValue(registry, out _)) return;
            registry.RegisterImageSourceResolver((source, target) => additionalSource?.Invoke(source, target) ?? Resolve(source, target));
            Registrations.Add(registry, new object());
        }
    }

    private static IImage? Resolve(string source, Control target)
    {
        // Colour samples are picker data, not an alternate application palette.
        if (source.StartsWith("color:", StringComparison.Ordinal) && Color.TryParse(source[6..], out var colour))
        {
            var outline = target.TryFindResource("CuiLineBrush", out var value) && value is IBrush line ? line : null;
            return new DrawingImage(new GeometryDrawing
            {
                Geometry = new RectangleGeometry(new Rect(3, 3, 18, 18), 3, 3),
                Brush = new SolidColorBrush(colour), Pen = outline is null ? null : new Pen(outline, 1)
            }) { Viewbox = new Rect(0, 0, 24, 24) };
        }
        var solid = source.StartsWith("icon-solid:", StringComparison.Ordinal);
        if (!solid && !source.StartsWith("icon:", StringComparison.Ordinal)) return null;
        var key = source[(solid ? 11 : 5)..];
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64 || key.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("A catalog icon requires a bounded symbolic key.", nameof(source));
        if (!target.Resources.TryGetResource("CuiTextBrush", null, out var resource) || resource is not IBrush brush)
            throw new InvalidOperationException("The icon's scoped CUI text brush is unavailable.");
        HavenGeometry sourceGeometry;
        if (solid)
        {
            if (!HavenIconCatalog.TryResolveSolid(key, out var filled))
                throw new ArgumentException($"The canonical icon '{key}' has no filled presentation.", nameof(source));
            sourceGeometry = filled;
        }
        else sourceGeometry = HavenIconCatalog.Resolve(key);
        var geometry = new StreamGeometry();
        using (var writer = geometry.Open())
        {
            writer.SetFillRule(sourceGeometry.Path.FillRule == HavenFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd);
            foreach (var figure in sourceGeometry.Path.Figures)
            {
                writer.BeginFigure(Point(figure.Start), solid);
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
            Brush = solid ? brush : null,
            Pen = solid ? null : new Pen(brush, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
        });
        if (sourceGeometry.ViewBox is { } viewBox)
            image.Viewbox = new Rect(viewBox.X, viewBox.Y, viewBox.Width, viewBox.Height);
        return image;
    }

    private static Point Point(HavenPoint point) => new(point.X, point.Y);
}
