using Haven.Core;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Present;

internal enum PresentTransformHandle
{
    None = 0,
    NorthWest,
    NorthEast,
    SouthWest,
    SouthEast,
    Rotate,
    North,
    South,
    West,
    East
}

internal sealed partial class PresentSlideCanvas
{
    private const double TransformHandleRadius = 4.5;
    private const double TransformHandleHitRadius = 10.5;
    private PresentTransformHandle _activeTransformHandle;
    private bool _marqueeSelecting;

    public event Action<IReadOnlyCollection<Guid>>? SelectionSetRequested;
    public event Action<double, double, double, double, double>? TransformSelectionRequested;

    public HavenCursor GetCursor(HavenPoint local)
    {
        if (_insertionPreset is not null) return HavenCursor.Crosshair;
        if (_gestureActivated && _dragElementId is not null) return HavenCursor.Grabbing;
        var handle = _activeTransformHandle != PresentTransformHandle.None ? _activeTransformHandle : HitTransformHandle(local);
        if (handle == PresentTransformHandle.Rotate) return HavenCursor.Rotate;
        if (handle is not null && handle != PresentTransformHandle.None)
        {
            var rotation = _slide?.Elements.FirstOrDefault(element => _selectedIds.Contains(element.Id))?.RotationDegrees ?? 0;
            var angle = rotation + (handle switch
            {
                PresentTransformHandle.North or PresentTransformHandle.South => 90,
                PresentTransformHandle.NorthWest or PresentTransformHandle.SouthEast => 45,
                PresentTransformHandle.NorthEast or PresentTransformHandle.SouthWest => 135,
                _ => 0
            });
            return (((int)Math.Round(angle / 45) % 4 + 4) % 4) switch
            {
                0 => HavenCursor.ResizeHorizontal, 1 => HavenCursor.ResizeNorthWestSouthEast,
                2 => HavenCursor.ResizeVertical, _ => HavenCursor.ResizeNorthEastSouthWest
            };
        }
        if (IsTextEditing && !HitEditingBorder(local) && TryEditingRect(out var rect, out var textRotation)
            && rect.Contains(InverseRotatePoint(local, rect, textRotation))) return HavenCursor.Text;
        return HitElement(local) is { } hovered ? hovered.Locked ? HavenCursor.NotAllowed : HavenCursor.Move : HavenCursor.Default;
    }

    private bool HitEditingBorder(HavenPoint local)
    {
        if (!IsTextEditing || !TryEditingRect(out var rect, out var rotation)) return false;
        var point = InverseRotatePoint(local, rect, rotation);
        return point.X >= rect.X - 5 && point.X <= rect.Right + 5 && point.Y >= rect.Y - 5 && point.Y <= rect.Bottom + 5
            && (Math.Abs(point.X - rect.X) <= 5 || Math.Abs(point.X - rect.Right) <= 5
                || Math.Abs(point.Y - rect.Y) <= 5 || Math.Abs(point.Y - rect.Bottom) <= 5);
    }

    private double PreviewRotation(PresentElement element) => element.RotationDegrees +
        (_gestureActivated && _activeTransformHandle == PresentTransformHandle.Rotate && IsSelectedForTransform(element)
            ? BuildDirectTransform(_activeTransformHandle, 0, 0).DeltaRotation : 0);

    private void ResetDirectGesture()
    {
        _dragElementId = null;
        _dragVectorHandle = null;
        _activeTransformHandle = PresentTransformHandle.None;
        _marqueeSelecting = false;
        _gestureActivated = false;
        _gestureModifiers = HavenKeyModifiers.None;
    }

    private PresentTransformHandle? HitTransformHandle(HavenPoint local)
    {
        if (_slide is null || _selectedIds.Count != 1) return null;
        var element = _slide.Elements.FirstOrDefault(value => _selectedIds.Contains(value.Id) && value.Visible && !value.Locked);
        if (element is null) return null;
        var rect = ElementRect(element, SlideRectLocal(), false);
        // Enlarged corner targets must not consume the entire body of a tiny
        // object. Keep its central grab region movable even when the visual
        // corner circles overlap at a small zoom.
        var unrotated = InverseRotatePoint(local, rect, element.RotationDegrees);
        if ((rect.Width < 24 || rect.Height < 24)
            && Math.Abs(unrotated.X - Center(rect).X) < rect.Width / 4
            && Math.Abs(unrotated.Y - Center(rect).Y) < rect.Height / 4) return null;
        var nearest = HandlePoints(rect, element.RotationDegrees)
            .Select(pair => (pair.Handle, Distance: DistanceSquared(local, pair.Point)))
            .OrderBy(pair => pair.Distance).First();
        return nearest.Distance <= TransformHandleHitRadius * TransformHandleHitRadius ? nearest.Handle : null;
    }

    private DirectTransform BuildDirectTransform(PresentTransformHandle handle, double dx, double dy)
    {
        if (handle == PresentTransformHandle.Rotate && _slide is not null && _selectedIds.Count == 1)
        {
            var element = _slide.Elements.First(value => _selectedIds.Contains(value.Id));
            var paper = SlideRectLocal();
            var center = new HavenPoint(paper.X + (element.X + element.Width / 2) * paper.Width,
                paper.Y + (element.Y + element.Height / 2) * paper.Height);
            var start = Math.Atan2(_pointerStart.Y - center.Y, _pointerStart.X - center.X);
            var current = Math.Atan2(_pointerCurrent.Y - center.Y, _pointerCurrent.X - center.X);
            var degrees = (current - start) * 180d / Math.PI;
            while (degrees > 180) degrees -= 360;
            while (degrees < -180) degrees += 360;
            if (_gestureModifiers.HasFlag(HavenKeyModifiers.Shift)) degrees = Math.Round(degrees / 15) * 15;
            return new DirectTransform(0, 0, 0, 0, degrees);
        }

        if (_slide is null || _selectedIds.Count != 1) return default;
        var selected = _slide.Elements.First(value => _selectedIds.Contains(value.Id));
        var slide = SlideRectLocal();
        var radians = selected.RotationDegrees * Math.PI / 180;
        var cos = Math.Cos(radians); var sin = Math.Sin(radians);
        var localX = cos * dx * slide.Width + sin * dy * slide.Height;
        var localY = -sin * dx * slide.Width + cos * dy * slide.Height;
        var west = handle is PresentTransformHandle.West or PresentTransformHandle.NorthWest or PresentTransformHandle.SouthWest;
        var east = handle is PresentTransformHandle.East or PresentTransformHandle.NorthEast or PresentTransformHandle.SouthEast;
        var north = handle is PresentTransformHandle.North or PresentTransformHandle.NorthWest or PresentTransformHandle.NorthEast;
        var south = handle is PresentTransformHandle.South or PresentTransformHandle.SouthWest or PresentTransformHandle.SouthEast;
        var width = Math.Max(.01 * slide.Width, selected.Width * slide.Width + (west ? -localX : east ? localX : 0));
        var height = Math.Max(.01 * slide.Height, selected.Height * slide.Height + (north ? -localY : south ? localY : 0));
        if (_gestureModifiers.HasFlag(HavenKeyModifiers.Shift) && (west || east) && (north || south))
        {
            var scale = Math.Max(width / (selected.Width * slide.Width), height / (selected.Height * slide.Height));
            width = selected.Width * slide.Width * scale; height = selected.Height * slide.Height * scale;
        }
        var deltaWidth = width - selected.Width * slide.Width;
        var deltaHeight = height - selected.Height * slide.Height;
        var centreX = (west ? -deltaWidth : deltaWidth) / 2;
        var centreY = (north ? -deltaHeight : deltaHeight) / 2;
        return new DirectTransform((cos * centreX - sin * centreY - deltaWidth / 2) / slide.Width,
            (sin * centreX + cos * centreY - deltaHeight / 2) / slide.Height,
            deltaWidth / slide.Width, deltaHeight / slide.Height, 0);
    }

    private IReadOnlyCollection<Guid> HitElementsInMarquee(HavenPoint start, HavenPoint end)
    {
        if (_slide is null) return Array.Empty<Guid>();
        var marquee = NormalizeRect(start, end);
        var slide = SlideRectLocal();
        return _slide.Elements
            .Where(element => element.Visible && element.Kind != PresentElementKind.Group && Intersects(marquee, RotatedBounds(ElementRect(element, slide, false), element.RotationDegrees)))
            .Select(element => SelectionRoot(element).Id).Distinct()
            .ToArray();
    }

    private void DrawDirectSelection(HavenDrawingContext context, PresentElement element, HavenRect rect, double opacity, bool showHandles)
    {
        var center = Center(rect);
        var rotation = PreviewRotation(element);
        if (Math.Abs(rotation) > .001)
            context.Add(new HavenPushTransformCommand(rect, new HavenTransform(RotationDegrees: rotation), center));
        context.Add(new HavenStrokeRoundedRectCommand(rect, new HavenPen(new HavenTokenBrush("Accent"), 2), 3, opacity));
        if (Math.Abs(rotation) > .001) context.Add(new HavenPopTransformCommand(rect));
        if (showHandles && !element.Locked)
        {
            var radius = HandleRadius(rect);
            foreach (var (_, point) in HandlePoints(rect, rotation))
                context.Add(new HavenEllipseCommand(new HavenRect(point.X - radius, point.Y - radius, radius * 2, radius * 2), new HavenTokenBrush("Surface"), new HavenPen(new HavenTokenBrush("Accent"), 1.5), opacity));
            var top = RotatePoint(new HavenPoint(center.X, rect.Y), center, rotation);
            var rotate = RotatePoint(new HavenPoint(center.X, rect.Y - 28), center, rotation);
            context.Add(new HavenLineCommand(top, rotate, new HavenPen(new HavenTokenBrush("Accent"), 1), opacity));
        }
    }

    private void DrawMarquee(HavenDrawingContext context, double opacity)
    {
        if (!_marqueeSelecting || !_gestureActivated) return;
        var rect = NormalizeRect(_pointerStart, _pointerCurrent);
        var absolute = new HavenRect(Bounds.X + rect.X, Bounds.Y + rect.Y, rect.Width, rect.Height);
        context.Add(new HavenFillRoundedRectCommand(absolute, new HavenTokenBrush("AccentSubtle"), 2, opacity * .35));
        context.Add(new HavenStrokeRoundedRectCommand(absolute, new HavenPen(new HavenTokenBrush("Accent"), 1.5), 2, opacity));
    }

    private IEnumerable<(PresentTransformHandle Handle, HavenPoint Point)> HandlePoints(HavenRect rect, double rotation)
    {
        var center = Center(rect);
        yield return (PresentTransformHandle.NorthWest, RotatePoint(new HavenPoint(rect.X, rect.Y), center, rotation));
        if (rect.Height >= 14 && rect.Width >= 14) yield return (PresentTransformHandle.NorthEast, RotatePoint(new HavenPoint(rect.Right, rect.Y), center, rotation));
        if (rect.Height >= 14 && rect.Width >= 14) yield return (PresentTransformHandle.SouthWest, RotatePoint(new HavenPoint(rect.X, rect.Bottom), center, rotation));
        yield return (PresentTransformHandle.SouthEast, RotatePoint(new HavenPoint(rect.Right, rect.Bottom), center, rotation));
        if (rect.Width >= 32) yield return (PresentTransformHandle.North, RotatePoint(new HavenPoint(center.X, rect.Y), center, rotation));
        if (rect.Width >= 32) yield return (PresentTransformHandle.South, RotatePoint(new HavenPoint(center.X, rect.Bottom), center, rotation));
        if (rect.Height >= 32) yield return (PresentTransformHandle.West, RotatePoint(new HavenPoint(rect.X, center.Y), center, rotation));
        if (rect.Height >= 32) yield return (PresentTransformHandle.East, RotatePoint(new HavenPoint(rect.Right, center.Y), center, rotation));
        yield return (PresentTransformHandle.Rotate, RotatePoint(new HavenPoint(rect.X + rect.Width / 2, rect.Y - 28), center, rotation));
    }

    private static double HandleRadius(HavenRect rect) => Math.Clamp(Math.Min(rect.Width, rect.Height) / 4, 2, TransformHandleRadius);

    private static HavenPoint Center(HavenRect rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    private static HavenPoint RotatePoint(HavenPoint point, HavenPoint center, double degrees)
    {
        if (Math.Abs(degrees) < .001) return point;
        var radians = degrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = point.X - center.X;
        var y = point.Y - center.Y;
        return new HavenPoint(x * cos - y * sin + center.X, x * sin + y * cos + center.Y);
    }

    private static HavenRect RotatedBounds(HavenRect rect, double rotation)
    {
        var center = Center(rect);
        var points = new[]
        {
            RotatePoint(new HavenPoint(rect.X, rect.Y), center, rotation),
            RotatePoint(new HavenPoint(rect.Right, rect.Y), center, rotation),
            RotatePoint(new HavenPoint(rect.X, rect.Bottom), center, rotation),
            RotatePoint(new HavenPoint(rect.Right, rect.Bottom), center, rotation)
        };
        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);
        return new HavenRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static HavenRect NormalizeRect(HavenPoint start, HavenPoint end) => new(
        Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));

    private static bool Intersects(HavenRect left, HavenRect right) =>
        left.Right >= right.X && right.Right >= left.X && left.Bottom >= right.Y && right.Bottom >= left.Y;

    private readonly record struct DirectTransform(double DeltaX, double DeltaY, double DeltaWidth, double DeltaHeight, double DeltaRotation);
}
