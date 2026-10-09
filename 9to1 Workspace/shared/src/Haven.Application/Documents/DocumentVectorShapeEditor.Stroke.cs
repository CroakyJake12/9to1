using Haven.Core;

namespace Haven.Application;

public sealed partial class DocumentVectorShapeEditor
{
    /// <summary>Updates the existing canonical stroke through the same shape
    /// mutation/history engine. Invalid supplied style refuses before mutation.</summary>
    public void SetPathStroke(Guid pathId, DocumentVectorStroke stroke,
        DocumentOperationOrigin origin = DocumentOperationOrigin.User)
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
        Edit("Style vector stroke", origin, shape =>
        {
            var path = shape.Paths.SingleOrDefault(path => path.Id == pathId)
                ?? throw new ArgumentOutOfRangeException(nameof(pathId));
            path.Stroke = captured;
        });
    }
}
