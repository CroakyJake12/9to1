using System.Globalization;
using Haven.Core;

namespace Haven.Application;

public enum DocumentVectorPrimitive { Rectangle = 0, Ellipse = 1, Polygon = 2, Star = 3, Line = 4, Arrow = 5 }

/// <summary>Creates editable built-in geometry in the SAME shared vector model.
/// These are fresh creations, not an import normalization or a new shape format.</summary>
public static class DocumentVectorPrimitives
{
    public const int MaximumPointCount = 64;

    public static DocumentVectorShape Create(DocumentVectorPrimitive primitive, int pointCount = 5,
        double width = 100, double height = 100)
    {
        if (!Enum.IsDefined(primitive)) throw new ArgumentOutOfRangeException(nameof(primitive));
        if (pointCount is < 3 or > MaximumPointCount) throw new ArgumentOutOfRangeException(nameof(pointCount));
        if (!double.IsFinite(width) || width <= 0 || width > 100000) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height <= 0 || height > 100000) throw new ArgumentOutOfRangeException(nameof(height));
        DocumentVectorNode Node(double x, double y) => new() { X = x * width, Y = y * height };
        List<DocumentVectorNode> Radial(bool star)
        {
            var count = star ? pointCount * 2 : pointCount;
            return Enumerable.Range(0, count).Select(index =>
            {
                var angle = -Math.PI / 2 + index * 2 * Math.PI / count;
                var radius = star && index % 2 == 1 ? .22 : .5;
                return Node(.5 + radius * Math.Cos(angle), .5 + radius * Math.Sin(angle));
            }).ToList();
        }
        List<DocumentVectorNode> Ellipse()
        {
            // Four canonical cubic segments, including the final curved return
            // to the first position. Every editable endpoint retains its own ID.
            var k = 4 * (Math.Sqrt(2) - 1) / 3;
            DocumentVectorNode Curve(double x, double y, double x1, double y1, double x2, double y2) => new()
            {
                X = x * width, Y = y * height, IncomingSegment = DocumentVectorSegmentKind.Cubic,
                Kind = DocumentVectorNodeKind.Smooth,
                Control1 = new(x1 * width, y1 * height), Control2 = new(x2 * width, y2 * height)
            };
            return [Node(1, .5), Curve(.5, 1, 1, .5 + k / 2, .5 + k / 2, 1),
                Curve(0, .5, .5 - k / 2, 1, 0, .5 + k / 2),
                Curve(.5, 0, 0, .5 - k / 2, .5 - k / 2, 0),
                Curve(1, .5, .5 + k / 2, 0, 1, .5 - k / 2)];
        }
        var nodes = primitive switch
        {
            DocumentVectorPrimitive.Rectangle => new List<DocumentVectorNode> { Node(0, 0), Node(1, 0), Node(1, 1), Node(0, 1) },
            DocumentVectorPrimitive.Ellipse => Ellipse(),
            DocumentVectorPrimitive.Polygon => Radial(false),
            DocumentVectorPrimitive.Star => Radial(true),
            DocumentVectorPrimitive.Line => new List<DocumentVectorNode> { Node(.05, .5), Node(.95, .5) },
            DocumentVectorPrimitive.Arrow => new List<DocumentVectorNode> { Node(0, .35), Node(.6, .35), Node(.6, 0),
                Node(1, .5), Node(.6, 1), Node(.6, .65), Node(0, .65) },
            _ => throw new ArgumentOutOfRangeException(nameof(primitive))
        };
        var shape = new DocumentVectorShape
        {
            Name = primitive.ToString(), SourceKind = DocumentShapeSourceKind.BuiltIn,
            AccessibilityDescription = "Editable " + primitive.ToString().ToLowerInvariant(),
            ViewBox = new() { Width = width, Height = height },
            Paths = [new()
            {
                Fill = new() { Kind = primitive == DocumentVectorPrimitive.Line ? DocumentVectorFillKind.None : DocumentVectorFillKind.Solid, Color = "#FF006FB9" },
                Stroke = new() { Enabled = primitive == DocumentVectorPrimitive.Line, Color = "#FF006FB9",
                    Width = Math.Min(width, height) * .06, Cap = DocumentVectorLineCap.Round, Join = DocumentVectorLineJoin.Round },
                Subpaths = [new() { Closed = primitive != DocumentVectorPrimitive.Line, Nodes = nodes }]
            }],
            Metadata = new(StringComparer.Ordinal) { ["creation.primitive"] = primitive.ToString() }
        };
        if (primitive is DocumentVectorPrimitive.Polygon or DocumentVectorPrimitive.Star)
            shape.Metadata["creation.pointCount"] = pointCount.ToString(CultureInfo.InvariantCulture);
        // All values and IDs were just created here. Imported/shared payloads
        // never pass through this constructor or Normalize.
        shape.Normalize();
        var validation = DocumentVectorShapeValidator.Validate(shape);
        if (!validation.IsValid) throw new InvalidDataException("Built-in geometry is invalid: " +
            string.Join("; ", validation.Issues.Select(issue => issue.Message)));
        return shape;
    }
}
