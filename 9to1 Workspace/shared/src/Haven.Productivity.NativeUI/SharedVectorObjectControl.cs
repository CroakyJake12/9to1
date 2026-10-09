using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace Haven.Productivity.NativeUI;

/// <summary>Portable native drawing of the SAME Home canonical vector binding.
/// The binding is detached display data, never an artifact mutation or resource grant.</summary>
public sealed class SharedVectorObjectControl : Control
{
    private readonly SharedVectorDrawing _drawing;
    public HomeProductivityVectorBinding OriginalBinding { get; }

    public SharedVectorObjectControl(HomeProductivityVectorBinding originalBinding)
    {
        OriginalBinding = originalBinding ?? throw new ArgumentNullException(nameof(originalBinding));
        _drawing = new(originalBinding);
        AutomationProperties.SetName(this, _drawing.AccessibleName);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Min(_drawing.LocalBounds.Width, availableSize.Width), Math.Min(_drawing.LocalBounds.Height, availableSize.Height));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width > 0 && Bounds.Height > 0) _drawing.Draw(context, new Rect(Bounds.Size));
    }

    /// <summary>Register typed Object Type="spe.vector" over the supplied original bindings.
    /// No engine, document, actor, authorization or replacement object identity is minted.</summary>
    public static void Register(CuiControlRegistry registry, IReadOnlyList<HomeProductivityVectorBinding> originalBindings)
    {
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(originalBindings);
        if (originalBindings.Count > 10000 || originalBindings.Any(binding => binding is null ||
            string.IsNullOrWhiteSpace(binding.ControlId) || binding.ObjectId == Guid.Empty))
            throw new InvalidDataException("The original shared vector bindings are invalid or oversized.");
        var captured = originalBindings.ToDictionary(binding => binding.ControlId, StringComparer.Ordinal);
        registry.RegisterObjectRenderer("spe.vector", component => component.Name is { } id && captured.TryGetValue(id, out var actual)
            ? new SharedVectorObjectControl(actual)
            : throw new InvalidDataException("This CUI vector has no corresponding original Home display binding."));
    }
}

/// <summary>Native projection of existing DocumentVectorShape semantics. Local shape transform
/// is applied exactly once by the shared TransformPoint; target placement is a separate map.</summary>
public sealed class SharedVectorDrawing
{
    private readonly List<(Geometry Geometry, IBrush? Fill, IPen? Stroke, double Opacity)> _paths = [];
    public Rect LocalBounds { get; }
    public string AccessibleName { get; }

    public SharedVectorDrawing(HomeProductivityVectorBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(binding.CanonicalShape, binding.ObjectId);
        if (shape.ClippingPathId is not null)
            throw new NotSupportedException("Canonical vector clipping requires its shared clipping renderer; retained content was not flattened.");
        LocalBounds = new(shape.ViewBox.X, shape.ViewBox.Y, shape.ViewBox.Width, shape.ViewBox.Height);
        AccessibleName = string.IsNullOrWhiteSpace(shape.AccessibilityDescription) ? shape.Name : shape.AccessibilityDescription;
        foreach (var path in shape.Paths)
        {
            var geometry = new StreamGeometry();
            using (var writer = geometry.Open())
            {
                writer.SetFillRule(path.FillRule == DocumentVectorFillRule.NonZero ? FillRule.NonZero : FillRule.EvenOdd);
                foreach (var subpath in path.Subpaths.Where(subpath => subpath.Nodes.Count > 0))
                {
                    writer.BeginFigure(Point(shape, subpath.Nodes[0].Point), path.Fill.Kind != DocumentVectorFillKind.None);
                    foreach (var node in subpath.Nodes.Skip(1))
                    {
                        var end = Point(shape, node.Point);
                        switch (node.IncomingSegment)
                        {
                            case DocumentVectorSegmentKind.Line: writer.LineTo(end); break;
                            case DocumentVectorSegmentKind.Quadratic when node.Control1 is { } control:
                                writer.QuadraticBezierTo(Point(shape, control), end); break;
                            case DocumentVectorSegmentKind.Cubic when node.Control1 is { } first && node.Control2 is { } second:
                                writer.CubicBezierTo(Point(shape, first), Point(shape, second), end); break;
                            default: throw new InvalidDataException("The canonical vector segment has no valid required controls.");
                        }
                    }
                    writer.EndFigure(subpath.Closed);
                }
            }
            var fill = path.Fill.Kind == DocumentVectorFillKind.None ? null : new SolidColorBrush(Color.Parse(path.Fill.Color), path.Fill.Opacity);
            IPen? stroke = path.Stroke.Enabled && path.Stroke.Width > 0 ? new Pen(
                new SolidColorBrush(Color.Parse(path.Stroke.Color), path.Stroke.Opacity), path.Stroke.Width,
                lineCap: path.Stroke.Cap switch { DocumentVectorLineCap.Round => PenLineCap.Round, DocumentVectorLineCap.Square => PenLineCap.Square, _ => PenLineCap.Flat },
                lineJoin: path.Stroke.Join switch { DocumentVectorLineJoin.Round => PenLineJoin.Round, DocumentVectorLineJoin.Bevel => PenLineJoin.Bevel, _ => PenLineJoin.Miter }) : null;
            _paths.Add((geometry, fill, stroke, path.Opacity));
        }
    }

    public void Draw(DrawingContext context, Rect targetBounds)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!double.IsFinite(targetBounds.X) || !double.IsFinite(targetBounds.Y) || !double.IsFinite(targetBounds.Width) ||
            !double.IsFinite(targetBounds.Height) || targetBounds.Width <= 0 || targetBounds.Height <= 0)
            throw new InvalidDataException("Native vector placement requires finite positive bounds.");
        var sx = targetBounds.Width / LocalBounds.Width;
        var sy = targetBounds.Height / LocalBounds.Height;
        using var placement = context.PushTransform(new Matrix(sx, 0, 0, sy, targetBounds.X - LocalBounds.X * sx, targetBounds.Y - LocalBounds.Y * sy));
        foreach (var path in _paths)
        {
            using var opacity = context.PushOpacity(path.Opacity);
            context.DrawGeometry(path.Fill, path.Stroke, path.Geometry);
        }
    }

    private static Point Point(DocumentVectorShape shape, DocumentVectorPoint point)
    {
        var actual = DocumentVectorShapes.TransformPoint(shape, point);
        return new(actual.X, actual.Y);
    }
}
