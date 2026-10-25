using System.Text.Json;
using System.Text.Json.Serialization;

namespace CakeOS.Apps.Boards.Contract;

/// <summary>Bounded typed geometry for a generated, single-color section icon.</summary>
public sealed class HavenRichSectionGeneratedIcon
{
    public List<HavenRichSectionIconShape> Shapes { get; set; } = [];
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HavenRichSectionIconRoundRect), "roundRect")]
[JsonDerivedType(typeof(HavenRichSectionIconCircle), "circle")]
[JsonDerivedType(typeof(HavenRichSectionIconPolygon), "polygon")]
public abstract record HavenRichSectionIconShape;

public sealed record HavenRichSectionIconRoundRect(
    double X, double Y, double Width, double Height, double Radius) : HavenRichSectionIconShape;

public sealed record HavenRichSectionIconCircle(
    double CenterX, double CenterY, double Radius) : HavenRichSectionIconShape;

public sealed record HavenRichSectionIconPolygon(
    List<HavenRichSectionIconPoint> Points, bool IsCutout = false) : HavenRichSectionIconShape;

public readonly record struct HavenRichSectionIconPoint(double X, double Y);

/// <summary>Validation, defensive copying, and value comparison for generated icon geometry.</summary>
public static class HavenRichSectionIconGeometry
{
    public const double ViewBoxSize = 24;
    public const int MaximumShapes = 12;
    public const int MaximumPolygonVertices = 64;
    public const int MaximumVerticesPerPolygon = 12;
    public const int MaximumSerializedBytes = 8 * 1024;

    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);

    public static void Validate(HavenRichSectionGeneratedIcon? icon)
    {
        if (icon is null || icon.Shapes is null || icon.Shapes.Count is < 1 or > MaximumShapes)
            throw new InvalidOperationException($"Generated section icons must contain 1 to {MaximumShapes} shapes.");

        var polygonVertices = 0;
        var hasVisibleShape = false;
        foreach (var shape in icon.Shapes)
        {
            switch (shape)
            {
                case HavenRichSectionIconRoundRect rect:
                    RequireFinite(rect.X, rect.Y, rect.Width, rect.Height, rect.Radius);
                    if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
                        rect.X + rect.Width > ViewBoxSize || rect.Y + rect.Height > ViewBoxSize ||
                        rect.Radius < 0 || rect.Radius > Math.Min(rect.Width, rect.Height) / 2)
                        throw new InvalidOperationException("Generated rounded rectangles must have bounded positive geometry inside the 24x24 viewbox.");
                    hasVisibleShape = true;
                    break;
                case HavenRichSectionIconCircle circle:
                    RequireFinite(circle.CenterX, circle.CenterY, circle.Radius);
                    if (circle.Radius <= 0 || circle.CenterX - circle.Radius < 0 ||
                        circle.CenterY - circle.Radius < 0 || circle.CenterX + circle.Radius > ViewBoxSize ||
                        circle.CenterY + circle.Radius > ViewBoxSize)
                        throw new InvalidOperationException("Generated circles must have bounded positive geometry inside the 24x24 viewbox.");
                    hasVisibleShape = true;
                    break;
                case HavenRichSectionIconPolygon polygon:
                    if (polygon.Points is null || polygon.Points.Count is < 3 or > MaximumVerticesPerPolygon)
                        throw new InvalidOperationException($"Generated polygons must contain 3 to {MaximumVerticesPerPolygon} vertices.");
                    polygonVertices = checked(polygonVertices + polygon.Points.Count);
                    if (polygonVertices > MaximumPolygonVertices)
                        throw new InvalidOperationException($"Generated polygons may contain at most {MaximumPolygonVertices} vertices total.");
                    ValidatePolygon(polygon.Points);
                    hasVisibleShape |= !polygon.IsCutout;
                    break;
                default:
                    throw new InvalidOperationException("Generated icon contains an unsupported shape type.");
            }
        }

        if (!hasVisibleShape)
            throw new InvalidOperationException("Generated section icons require at least one non-cutout shape.");
        if (JsonSerializer.SerializeToUtf8Bytes(icon, CanonicalJson).Length > MaximumSerializedBytes)
            throw new InvalidOperationException($"Generated section icon geometry exceeds {MaximumSerializedBytes} serialized bytes.");
    }

    public static HavenRichSectionGeneratedIcon Copy(HavenRichSectionGeneratedIcon icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        static HavenRichSectionIconShape CopyShape(HavenRichSectionIconShape shape) => shape switch
        {
            HavenRichSectionIconRoundRect rect => new HavenRichSectionIconRoundRect(
                rect.X, rect.Y, rect.Width, rect.Height, rect.Radius),
            HavenRichSectionIconCircle circle => new HavenRichSectionIconCircle(
                circle.CenterX, circle.CenterY, circle.Radius),
            HavenRichSectionIconPolygon polygon => new HavenRichSectionIconPolygon(
                [.. polygon.Points], polygon.IsCutout),
            _ => throw new InvalidOperationException("Generated icon contains an unsupported shape type.")
        };
        return new HavenRichSectionGeneratedIcon
        {
            Shapes = icon.Shapes.Select(CopyShape).ToList()
        };
    }

    public static bool AreEqual(HavenRichSectionGeneratedIcon? left, HavenRichSectionGeneratedIcon? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Shapes.Count != right.Shapes.Count) return false;
        for (var i = 0; i < left.Shapes.Count; i++)
        {
            var leftShape = left.Shapes[i];
            var rightShape = right.Shapes[i];
            if (leftShape is HavenRichSectionIconRoundRect leftRect &&
                rightShape is HavenRichSectionIconRoundRect rightRect && leftRect == rightRect)
            {
                continue;
            }
            if (leftShape is HavenRichSectionIconCircle leftCircle &&
                rightShape is HavenRichSectionIconCircle rightCircle && leftCircle == rightCircle)
                continue;
            if (leftShape is HavenRichSectionIconPolygon leftPolygon &&
                rightShape is HavenRichSectionIconPolygon rightPolygon &&
                leftPolygon.IsCutout == rightPolygon.IsCutout && leftPolygon.Points.SequenceEqual(rightPolygon.Points))
                continue;
            return false;
        }
        return true;
    }

    private static void RequireFinite(params double[] values)
    {
        if (values.Any(value => !double.IsFinite(value)))
            throw new InvalidOperationException("Generated icon coordinates must be finite.");
    }

    private static void ValidatePolygon(IReadOnlyList<HavenRichSectionIconPoint> points)
    {
        foreach (var point in points)
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                point.X < 0 || point.X > ViewBoxSize || point.Y < 0 || point.Y > ViewBoxSize)
                throw new InvalidOperationException("Generated polygon vertices must be finite and inside the 24x24 viewbox.");

        for (var i = 0; i < points.Count; i++)
        {
            var next = (i + 1) % points.Count;
            if (points[i] == points[next])
                throw new InvalidOperationException("Generated polygons cannot contain duplicate adjacent vertices.");
        }

        var twiceArea = 0d;
        for (var i = 0; i < points.Count; i++)
        {
            var next = (i + 1) % points.Count;
            twiceArea += points[i].X * points[next].Y - points[next].X * points[i].Y;
        }
        if (Math.Abs(twiceArea) < 1e-9)
            throw new InvalidOperationException("Generated polygons must have nonzero area.");

        for (var first = 0; first < points.Count; first++)
        {
            var firstNext = (first + 1) % points.Count;
            for (var second = first + 1; second < points.Count; second++)
            {
                var secondNext = (second + 1) % points.Count;
                if (first == second || firstNext == second || secondNext == first) continue;
                if (SegmentsIntersect(points[first], points[firstNext], points[second], points[secondNext]))
                    throw new InvalidOperationException("Generated polygons cannot self-intersect.");
            }
        }
    }

    private static bool SegmentsIntersect(HavenRichSectionIconPoint a, HavenRichSectionIconPoint b,
        HavenRichSectionIconPoint c, HavenRichSectionIconPoint d)
    {
        static double Cross(HavenRichSectionIconPoint p, HavenRichSectionIconPoint q,
            HavenRichSectionIconPoint r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        static bool OnSegment(HavenRichSectionIconPoint p, HavenRichSectionIconPoint q,
            HavenRichSectionIconPoint r) => q.X >= Math.Min(p.X, r.X) && q.X <= Math.Max(p.X, r.X) &&
                                           q.Y >= Math.Min(p.Y, r.Y) && q.Y <= Math.Max(p.Y, r.Y);
        var abC = Cross(a, b, c);
        var abD = Cross(a, b, d);
        var cdA = Cross(c, d, a);
        var cdB = Cross(c, d, b);
        const double epsilon = 1e-9;
        if (((abC > epsilon && abD < -epsilon) || (abC < -epsilon && abD > epsilon)) &&
            ((cdA > epsilon && cdB < -epsilon) || (cdA < -epsilon && cdB > epsilon)))
            return true;
        return (Math.Abs(abC) <= epsilon && OnSegment(a, c, b)) ||
               (Math.Abs(abD) <= epsilon && OnSegment(a, d, b)) ||
               (Math.Abs(cdA) <= epsilon && OnSegment(c, a, d)) ||
               (Math.Abs(cdB) <= epsilon && OnSegment(c, b, d));
    }
}
