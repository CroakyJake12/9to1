using Haven.Core;

namespace Haven.Application;

public sealed partial class DocumentVectorShapeEditor
{
    /// <summary>Updates the existing canonical stroke through the same shape
    /// mutation/history engine. Invalid supplied style refuses before mutation.</summary>
    public void SetPathStroke(Guid pathId, DocumentVectorStroke stroke,
        DocumentOperationOrigin origin = DocumentOperationOrigin.User)
    {
        var captured = CaptureStroke(stroke);
        Edit("Style vector stroke", origin, shape =>
        {
            var path = shape.Paths.SingleOrDefault(path => path.Id == pathId)
                ?? throw new ArgumentOutOfRangeException(nameof(pathId));
            path.Stroke = captured;
        });
    }

    /// <summary>Applies one captured stroke preset to every path as a single reversible
    /// edit. Each path receives an independent style value; geometry and fills remain intact.</summary>
    public void SetShapeStroke(DocumentVectorStroke stroke, DocumentOperationOrigin origin = DocumentOperationOrigin.User)
    {
        var captured = CaptureStroke(stroke);
        Edit("Style vector shape stroke", origin, shape =>
        {
            foreach (var path in shape.Paths) path.Stroke = CaptureStroke(captured);
        });
    }

    /// <summary>Changes only stroke width across a shape as one reversible edit,
    /// retaining each path's color, enablement, opacity, cap and join.</summary>
    public void SetShapeStrokeWidth(double width, DocumentOperationOrigin origin = DocumentOperationOrigin.User)
    {
        if (!double.IsFinite(width) || width is < 0 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(width), "Stroke width must be finite and within the supported range.");
        Edit("Set vector shape stroke width", origin, shape =>
        {
            foreach (var path in shape.Paths)
                path.Stroke = CaptureStroke(new DocumentVectorStroke
                {
                    Enabled = path.Stroke.Enabled, Color = path.Stroke.Color, Width = width,
                    Opacity = path.Stroke.Opacity, Cap = path.Stroke.Cap, Join = path.Stroke.Join
                });
        });
    }

    private static DocumentVectorStroke CaptureStroke(DocumentVectorStroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        var captured = new DocumentVectorStroke
        {
            Enabled = stroke.Enabled, Color = stroke.Color, Width = stroke.Width,
            Opacity = stroke.Opacity, Cap = stroke.Cap, Join = stroke.Join
        };
        if (string.IsNullOrWhiteSpace(captured.Color)) throw new ArgumentException("Enter a stroke colour.", nameof(stroke));
        if (!double.IsFinite(captured.Width) || captured.Width is < 0 or > 10000 ||
            !double.IsFinite(captured.Opacity) || captured.Opacity is < 0 or > 1 ||
            !Enum.IsDefined(captured.Cap) || !Enum.IsDefined(captured.Join))
            throw new ArgumentOutOfRangeException(nameof(stroke), "Stroke width, opacity and line style must be valid.");
        captured.Color = captured.Color.Trim();
        return captured;
    }
}
