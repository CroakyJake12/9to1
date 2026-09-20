using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Haven.UI;

namespace CakeOS.Hui.Renderer;

/// <summary>
/// Backend services for the shared Avalonia renderer: theme resolution plus
/// application-supplied image decoding (e.g. the live Canvas SVG frame).
/// </summary>
public sealed class HuiBackendServices
{
    public HuiBackendServices(CakeTheme theme, Func<string, IImage?> imageFor)
    {
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        ImageFor = imageFor ?? throw new ArgumentNullException(nameof(imageFor));
    }

    public CakeTheme Theme { get; }
    public Func<string, IImage?> ImageFor { get; }
}

/// <summary>
/// The ONE Avalonia backend for HUI draw commands, shared by every host.
/// Replaces the per-host preview renderers and their hard-coded token tables:
/// every brush resolves through <see cref="CakeTheme"/> (HUI/theme/tokens.json).
/// Unsupported editing-only commands fail fast with a clear message instead of
/// rendering something misleading.
/// </summary>
public static class HuiAvaloniaRenderer
{
    public static void Render(DrawingContext context, IReadOnlyList<HavenDrawCommand> commands, HuiBackendServices services)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(services);
        var scopes = new Stack<IDisposable>();
        try
        {
            foreach (var command in commands)
            {
                switch (command)
                {
                    case HavenPushTransformCommand push:
                    {
                        var transform = Matrix.CreateTranslation(-push.Origin.X, -push.Origin.Y)
                            * Matrix.CreateScale(push.Transform.ScaleX, push.Transform.ScaleY)
                            * Matrix.CreateRotation(push.Transform.RotationDegrees * Math.PI / 180d)
                            * Matrix.CreateTranslation(
                                push.Origin.X + push.Transform.TranslateX,
                                push.Origin.Y + push.Transform.TranslateY);
                        scopes.Push(context.PushTransform(transform));
                        continue;
                    }
                    case HavenPushClipCommand clip:
                        scopes.Push(context.PushClip(Rect(clip.Rect)));
                        continue;
                    case HavenPopTransformCommand or HavenPopClipCommand:
                        if (scopes.Count == 0)
                            throw new InvalidOperationException("HUI renderer emitted an unbalanced transform/clip pop.");
                        scopes.Pop().Dispose();
                        continue;
                    case HavenFillRoundedRectCommand fill:
                        context.DrawRectangle(
                            services.Theme.Brush(fill.Brush, fill.Opacity * fill.Alpha), null,
                            Rect(fill.Rect), fill.Radius, fill.Radius);
                        break;
                    case HavenStrokeRoundedRectCommand stroke:
                        context.DrawRectangle(null,
                            Pen(services, stroke.Pen, stroke.Opacity * stroke.Alpha),
                            Rect(stroke.Rect), stroke.Radius, stroke.Radius);
                        break;
                    case HavenTextCommand text:
                    {
                        var formatted = HuiMeasure.Format(text.Layout, services.Theme.Brush(text.Brush, text.Opacity * text.Alpha));
                        var y = text.Layout.CenterVertically
                            ? text.Rect.Y + Math.Max(0d, (text.Rect.Height - formatted.Height) / 2d)
                            : text.Rect.Y;
                        context.DrawText(formatted, new Point(text.Rect.X, y));
                        break;
                    }
                    case HavenTextSelectionCommand selection:
                        throw new NotSupportedException(
                            $"HUI text selection ({selection.SelectionLength} chars) requires an editing backend; this renderer draws text only.");
                    case HavenCaretCommand:
                        throw new NotSupportedException(
                            "HUI caret rendering requires an editing backend; this renderer draws text only.");
                    case HavenLineCommand line:
                        context.DrawLine(
                            Pen(services, line.Pen, line.Opacity * line.Alpha),
                            Point(line.Start), Point(line.End));
                        break;
                    case HavenEllipseCommand ellipse:
                        context.DrawEllipse(
                            services.Theme.Brush(ellipse.Brush, ellipse.Opacity * ellipse.Alpha),
                            ellipse.Pen is null ? null : Pen(services, ellipse.Pen, ellipse.Opacity * ellipse.Alpha),
                            Rect(ellipse.Rect));
                        break;
                    case HavenGeometryCommand geometry:
                        DrawGeometry(context, services, geometry);
                        break;
                    case HavenImageCommand image:
                        DrawImage(context, services, image);
                        break;
                    case HavenIconCommand icon:
                        DrawIcon(context, services, icon);
                        break;
                    case HavenShadowCommand shadow:
                        DrawEffect(context, services, shadow.Rect, shadow.Radius,
                            shadow.Shadow.Brush, shadow.Shadow.OffsetX, shadow.Shadow.OffsetY,
                            shadow.Shadow.Blur, shadow.Shadow.Spread, shadow.Shadow.Opacity);
                        break;
                    case HavenGlowCommand glow:
                        DrawEffect(context, services, glow.Rect, glow.Radius,
                            glow.Glow.Brush, 0d, 0d, glow.Glow.Blur, 0d, glow.Glow.Opacity);
                        break;
                    default:
                        throw new NotSupportedException($"Shared HUI renderer does not support {command.GetType().Name}.");
                }
            }

            if (scopes.Count != 0)
                throw new InvalidOperationException("HUI renderer emitted unbalanced transform/clip pushes.");
        }
        finally
        {
            while (scopes.Count > 0) scopes.Pop().Dispose();
        }
    }

    private static void DrawGeometry(DrawingContext context, HuiBackendServices services, HavenGeometryCommand command)
    {
        var geometry = Translate(command.Geometry, command.Rect);
        IPen? pen = command.Stroke is null ? null : Pen(services, command.Stroke, command.Opacity * command.Alpha);
        if (command.Fill is null && pen is null)
            return;
        IBrush? fill = command.Fill is null ? null : services.Theme.Brush(command.Fill, command.Opacity * command.Alpha);
        context.DrawGeometry(fill, pen, geometry);
    }

    private static void DrawIcon(DrawingContext context, HuiBackendServices services, HavenIconCommand icon)
    {
        var geometry = HavenIconCatalog.Resolve(icon.Key);
        var baked = Translate(new HavenGeometry(geometry.Path, geometry.ViewBox), icon.Rect);
        var width = Math.Max(1.2d, Math.Min(icon.Rect.Width, icon.Rect.Height) / 12d);
        var pen = new Pen(services.Theme.Brush(icon.Brush, icon.Opacity * icon.Alpha), width)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        context.DrawGeometry(null, pen, baked);
    }

    private static void DrawImage(DrawingContext context, HuiBackendServices services, HavenImageCommand image)
    {
        var bitmap = services.ImageFor(image.Image.Source);
        if (bitmap is null)
            return; // Nothing decoded for this source (e.g. a blank document).
        var target = Rect(image.Rect);
        var natural = bitmap.Size;
        if (natural.Width <= 0 || natural.Height <= 0)
            return;
        Rect source;
        Rect dest;
        switch (image.Layout)
        {
            case HavenImageLayout.Fill:
                source = new Rect(0, 0, natural.Width, natural.Height);
                dest = target;
                break;
            case HavenImageLayout.None:
                source = new Rect(0, 0, Math.Min(natural.Width, target.Width), Math.Min(natural.Height, target.Height));
                dest = new Rect(target.X, target.Y, source.Width, source.Height);
                break;
            case HavenImageLayout.Cover:
            {
                var scale = Math.Max(target.Width / natural.Width, target.Height / natural.Height);
                var w = target.Width / scale;
                var h = target.Height / scale;
                source = new Rect((natural.Width - w) / 2d, (natural.Height - h) / 2d, w, h);
                dest = target;
                break;
            }
            default: // Contain
            {
                var scale = Math.Min(target.Width / natural.Width, target.Height / natural.Height);
                var w = natural.Width * scale;
                var h = natural.Height * scale;
                source = new Rect(0, 0, natural.Width, natural.Height);
                dest = new Rect(target.X + (target.Width - w) / 2d, target.Y + (target.Height - h) / 2d, w, h);
                break;
            }
        }
        using var opacity = context.PushOpacity(Math.Clamp(image.Opacity * image.Alpha, 0d, 1d));
        using var clip = context.PushClip(target);
        context.DrawImage(bitmap, source, dest);
    }

    private static void DrawEffect(
        DrawingContext context,
        HuiBackendServices services,
        HavenRect bounds,
        double radius,
        HavenBrush brush,
        double offsetX,
        double offsetY,
        double blur,
        double spread,
        double opacity)
    {
        var shadows = new BoxShadows(new BoxShadow
        {
            OffsetX = offsetX,
            OffsetY = offsetY,
            Blur = Math.Max(0d, blur),
            Spread = spread,
            Color = ((SolidColorBrush)services.Theme.Brush(brush, opacity)).Color,
        });
        context.DrawRectangle(Brushes.Transparent, null, Rect(bounds), radius, radius, shadows);
    }

    private static Geometry Translate(HavenGeometry geometry, HavenRect target)
    {
        var viewBox = geometry.ViewBox ?? new HavenRect(0, 0, 24, 24);
        var scaleX = viewBox.Width > 0 ? target.Width / viewBox.Width : 1d;
        var scaleY = viewBox.Height > 0 ? target.Height / viewBox.Height : 1d;
        Point Map(HavenPoint p) => new(
            target.X + (p.X - viewBox.X) * scaleX,
            target.Y + (p.Y - viewBox.Y) * scaleY);
        var path = new PathGeometry { FillRule = geometry.Path.FillRule == HavenFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero };
        foreach (var figure in geometry.Path.Figures)
        {
            var start = Map(figure.Start);
            var segments = new PathSegments();
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case HavenLineSegment line:
                        segments.Add(new LineSegment { Point = Map(line.End) });
                        break;
                    case HavenQuadraticBezierSegment quad:
                        segments.Add(new QuadraticBezierSegment { Point1 = Map(quad.Control), Point2 = Map(quad.End) });
                        break;
                    case HavenCubicBezierSegment cubic:
                        segments.Add(new BezierSegment { Point1 = Map(cubic.Control1), Point2 = Map(cubic.Control2), Point3 = Map(cubic.End) });
                        break;
                    case HavenArcSegment arc:
                        segments.Add(new ArcSegment
                        {
                            Point = Map(arc.End),
                            Size = new Size(Math.Abs(arc.Radius.Width * scaleX), Math.Abs(arc.Radius.Height * scaleY)),
                            RotationAngle = arc.RotationDegrees,
                            IsLargeArc = arc.IsLargeArc,
                            SweepDirection = arc.SweepDirection == HavenSweepDirection.Clockwise
                                ? SweepDirection.Clockwise
                                : SweepDirection.CounterClockwise,
                        });
                        break;
                    default:
                        throw new NotSupportedException($"Shared HUI renderer does not support path segment {segment.GetType().Name}.");
                }
            }
            path.Figures.Add(new PathFigure { StartPoint = start, Segments = segments, IsClosed = figure.Closed });
        }
        return path;
    }

    private static IPen Pen(HuiBackendServices services, HavenPen pen, double opacity) =>
        new Pen(services.Theme.Brush(pen.Brush, opacity), pen.Thickness);

    private static Rect Rect(HavenRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    private static Point Point(HavenPoint point) => new(point.X, point.Y);
}
