using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using VectorPath = Avalonia.Controls.Shapes.Path;

namespace HavenOS.Home.NativeUI;

/// <summary>Actual native vector drawing over the existing canonical shape graph. This owns only
/// detached geometry and brushes; the app retains the document, selection and transaction lifetime.</summary>
internal static class HomeVectorObjectRenderer
{
    public static Control Render(JsonElement canonical, Guid objectId)
    {
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(canonical, objectId);
        if (shape.ClippingPathId is not null) throw new NotSupportedException("Canonical vector clipping is not supported by this surface yet.");
        var canvas = new Canvas { Width = shape.ViewBox.Width, Height = shape.ViewBox.Height };
        Avalonia.Automation.AutomationProperties.SetName(canvas,
            string.IsNullOrWhiteSpace(shape.AccessibilityDescription) ? shape.Name : shape.AccessibilityDescription);
        foreach (var path in shape.Paths)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.SetFillRule(path.FillRule == DocumentVectorFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);
                foreach (var subpath in path.Subpaths)
                {
                    context.BeginFigure(Point(shape, subpath.Nodes[0].Point), path.Fill.Kind != DocumentVectorFillKind.None);
                    for (var i = 1; i < subpath.Nodes.Count; i++)
                    {
                        var node = subpath.Nodes[i]; var end = Point(shape, node.Point);
                        switch (node.IncomingSegment)
                        {
                            case DocumentVectorSegmentKind.Line: context.LineTo(end); break;
                            case DocumentVectorSegmentKind.Quadratic: context.QuadraticBezierTo(Point(shape, node.Control1!), end); break;
                            case DocumentVectorSegmentKind.Cubic: context.CubicBezierTo(Point(shape, node.Control1!), Point(shape, node.Control2!), end); break;
                            default: throw new InvalidDataException("Unknown canonical vector segment.");
                        }
                    }
                    context.EndFigure(subpath.Closed);
                }
            }
            var control = new VectorPath
            {
                Data = geometry, Opacity = path.Opacity,
                Fill = path.Fill.Kind == DocumentVectorFillKind.Solid ? Brush(path.Fill.Color, path.Fill.Opacity) : null,
                Stroke = path.Stroke.Enabled ? Brush(path.Stroke.Color, path.Stroke.Opacity) : null,
                StrokeThickness = path.Stroke.Width,
                StrokeJoin = path.Stroke.Join switch { DocumentVectorLineJoin.Miter => PenLineJoin.Miter,
                    DocumentVectorLineJoin.Round => PenLineJoin.Round, DocumentVectorLineJoin.Bevel => PenLineJoin.Bevel,
                    _ => throw new InvalidDataException("Unknown vector stroke join.") },
                StrokeLineCap = path.Stroke.Cap switch { DocumentVectorLineCap.Butt => PenLineCap.Flat,
                    DocumentVectorLineCap.Round => PenLineCap.Round, DocumentVectorLineCap.Square => PenLineCap.Square,
                    _ => throw new InvalidDataException("Unknown vector stroke cap.") }
            };
            Avalonia.Automation.AutomationProperties.SetAutomationId(control, path.Id.ToString("D"));
            canvas.Children.Add(control);
        }
        return new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
    }
    private static Point Point(DocumentVectorShape shape, DocumentVectorPoint point)
    {
        var transformed = DocumentVectorShapes.TransformPoint(shape, point);
        var x = transformed.X - shape.ViewBox.X; var y = transformed.Y - shape.ViewBox.Y;
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 1e9 || Math.Abs(y) > 1e9)
            throw new InvalidDataException("Vector display coordinates exceed the native surface bound.");
        return new(x, y);
    }
    private static SolidColorBrush Brush(string value, double opacity) => new(Color.Parse(value), opacity);
}
