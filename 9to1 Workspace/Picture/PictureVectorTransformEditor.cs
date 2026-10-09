using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;

namespace HavenOS.Images;

/// <summary>Uses the shared shape editor while publishing only the intended
/// transform field into the exact original retained vector payload.</summary>
internal static class PictureVectorTransformEditor
{
    internal static HomeProductivityObject Rotate(HomeProductivityObject original, double clockwiseDegrees)
    {
        if (!double.IsFinite(clockwiseDegrees) || clockwiseDegrees is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(clockwiseDegrees), "Enter an angle from −180 to 180 degrees.");
        return Edit(original, "RotationDegrees", transform => transform.RotationDegrees = clockwiseDegrees);
    }

    internal static HomeProductivityObject Mirror(HomeProductivityObject original, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(original);
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
        var scale = horizontal ? shape.Transform.ScaleX : shape.Transform.ScaleY;
        // The maintained editor clamps larger magnitudes. Refuse that retained
        // case rather than silently resize an imported shape when mirroring it.
        if (Math.Abs(scale) > 1000 || Math.Abs(scale) <= .000001)
            throw new NotSupportedException("The selected shape’s retained scale cannot be mirrored without changing its size.");
        return Edit(original, horizontal ? "ScaleX" : "ScaleY", transform =>
        {
            if (horizontal) transform.ScaleX = -scale;
            else transform.ScaleY = -scale;
        });
    }

    private static HomeProductivityObject Edit(HomeProductivityObject original, string field,
        Action<DocumentVectorTransform> change)
    {
        ArgumentNullException.ThrowIfNull(original);
        var shape = HomeVectorShapeObjectHandler.ReadCanonical(original.Content, original.ObjectId);
        var editor = new DocumentVectorShapeEditor(DocumentVectorShapes.Clone(shape));
        var input = new DocumentVectorTransform
        {
            TranslateX = shape.Transform.TranslateX, TranslateY = shape.Transform.TranslateY,
            ScaleX = shape.Transform.ScaleX, ScaleY = shape.Transform.ScaleY,
            OriginX = shape.Transform.OriginX, OriginY = shape.Transform.OriginY,
            RotationDegrees = shape.Transform.RotationDegrees
        };
        change(input); editor.SetTransform(input);
        var edited = editor.Shape.Transform;
        var content = JsonNode.Parse(original.Content.GetRawText())!.AsObject();
        var transform = content["Transform"] as JsonObject
            ?? throw new InvalidDataException("The original shape transform is unavailable.");
        transform[field] = field switch
        {
            "RotationDegrees" => edited.RotationDegrees,
            "ScaleX" => edited.ScaleX,
            "ScaleY" => edited.ScaleY,
            _ => throw new InvalidOperationException("Unknown shared vector transform field.")
        };
        var result = original with { Content = JsonSerializer.SerializeToElement(content) };
        _ = HomeVectorShapeObjectHandler.ReadCanonical(result.Content, result.ObjectId);
        _ = new HomeVectorShapeObjectHandler().Render(result);
        return result;
    }
}
