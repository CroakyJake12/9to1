using Haven.Core;

namespace Haven.Application;

public sealed partial class DocumentVectorShapeEditor
{
    /// <summary>Edits the existing canonical fill through the same mutation
    /// history. Scalar input is captured and validated before mutation.</summary>
    public void SetPathFill(Guid pathId, DocumentVectorFill fill, DocumentVectorFillRule? fillRule = null,
        DocumentOperationOrigin origin = DocumentOperationOrigin.User)
    {
        ArgumentNullException.ThrowIfNull(fill);
        var captured = new DocumentVectorFill { Kind = fill.Kind, Color = fill.Color, Opacity = fill.Opacity };
        var capturedRule = fillRule;
        if (!Enum.IsDefined(captured.Kind) || !double.IsFinite(captured.Opacity) || captured.Opacity is < 0 or > 1 ||
            (capturedRule is { } rule && !Enum.IsDefined(rule)))
            throw new ArgumentOutOfRangeException(nameof(fill), "Fill kind, opacity and contour rule must be valid.");
        if (string.IsNullOrWhiteSpace(captured.Color)) throw new ArgumentException("Enter a fill colour.", nameof(fill));
        captured.Color = captured.Color.Trim();
        Edit("Style vector fill", origin, shape =>
        {
            var path = shape.Paths.SingleOrDefault(path => path.Id == pathId)
                ?? throw new ArgumentOutOfRangeException(nameof(pathId));
            path.Fill = captured;
            if (capturedRule is { } rule) path.FillRule = rule;
        });
    }
}
