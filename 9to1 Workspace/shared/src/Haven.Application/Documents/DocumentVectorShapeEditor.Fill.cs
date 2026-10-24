using Haven.Core;

namespace Haven.Application;

public sealed partial class DocumentVectorShapeEditor
{
    /// <summary>Edits the existing canonical fill through the same mutation
    /// history. Scalar input is captured and validated before mutation.</summary>
    public void SetPathFill(Guid pathId, DocumentVectorFill fill, DocumentVectorFillRule? fillRule = null,
        DocumentOperationOrigin origin = DocumentOperationOrigin.User)
    {
        var captured = CaptureFill(fill);
        var capturedRule = fillRule;
        if (capturedRule is { } rule && !Enum.IsDefined(rule))
            throw new ArgumentOutOfRangeException(nameof(fillRule), "The contour rule must be valid.");
        Edit("Style vector fill", origin, shape =>
        {
            var path = shape.Paths.SingleOrDefault(path => path.Id == pathId)
                ?? throw new ArgumentOutOfRangeException(nameof(pathId));
            path.Fill = captured;
            if (capturedRule is { } rule) path.FillRule = rule;
        });
    }

    /// <summary>Applies one captured fill preset to every path as a single reversible
    /// edit. Each path receives its own style value; path geometry and contour rules remain intact.</summary>
    public void SetShapeFill(DocumentVectorFill fill, DocumentOperationOrigin origin = DocumentOperationOrigin.User)
    {
        var captured = CaptureFill(fill);
        Edit("Style vector shape fill", origin, shape =>
        {
            foreach (var path in shape.Paths) path.Fill = CaptureFill(captured);
        });
    }

    private static DocumentVectorFill CaptureFill(DocumentVectorFill fill)
    {
        ArgumentNullException.ThrowIfNull(fill);
        var captured = new DocumentVectorFill { Kind = fill.Kind, Color = fill.Color, Opacity = fill.Opacity };
        if (!Enum.IsDefined(captured.Kind) || !double.IsFinite(captured.Opacity) || captured.Opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(fill), "Fill kind and opacity must be valid.");
        if (string.IsNullOrWhiteSpace(captured.Color)) throw new ArgumentException("Enter a fill colour.", nameof(fill));
        captured.Color = captured.Color.Trim();
        return captured;
    }
}
