using Haven.Core;

namespace Haven.Application;

public sealed partial class PresentEditor
{
    public PresentElement AddShapeAt(Guid slideId, string presetId, double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)
            || width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var shape = DocumentVectorShapes.CloneForInsertion(DocumentShapePresets.Create(presetId));
        return AddElement(slideId, new PresentElement
        {
            Kind = PresentElementKind.Shape, ShapeType = presetId, VectorShape = shape,
            AlternativeText = shape.AccessibilityDescription,
            X = Math.Clamp(x, 0, 1), Y = Math.Clamp(y, 0, 1),
            Width = Math.Clamp(width, .01, 1), Height = Math.Clamp(height, .01, 1)
        });
    }
}
