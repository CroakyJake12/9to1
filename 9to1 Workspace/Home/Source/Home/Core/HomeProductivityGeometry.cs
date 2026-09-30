using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace HavenOS.Home.Core;

/// <summary>Affine map x'=M11*x+M21*y+Dx; y'=M12*x+M22*y+Dy.
/// Coordinates are persisted document coordinates, never viewport zoom/pan or device DPI.</summary>
public sealed record HomeProductivityAffine(double M11, double M12, double M21, double M22, double Dx, double Dy)
{
    public static HomeProductivityAffine Identity { get; } = new(1, 0, 0, 1, 0, 0);
    [JsonIgnore] public bool IsValid => new[] { M11, M12, M21, M22, Dx, Dy }.All(double.IsFinite) &&
        new[] { M11, M12, M21, M22, Dx, Dy }.All(value => Math.Abs(value) <= 1e12);

    public (double X, double Y) Map(double x, double y)
    {
        if (!IsValid || !double.IsFinite(x) || !double.IsFinite(y)) throw new InvalidDataException("Invalid shared affine coordinates.");
        var mapped = (X: M11 * x + M21 * y + Dx, Y: M12 * x + M22 * y + Dy);
        if (!double.IsFinite(mapped.X) || !double.IsFinite(mapped.Y)) throw new InvalidDataException("Shared affine coordinates overflowed.");
        return mapped;
    }

    /// <summary>Returns this(first(point)): the argument is applied first.</summary>
    public HomeProductivityAffine Compose(HomeProductivityAffine first)
    {
        if (!IsValid || !first.IsValid) throw new InvalidDataException("Invalid shared affine transform.");
        var result = new HomeProductivityAffine(
            M11 * first.M11 + M21 * first.M12, M12 * first.M11 + M22 * first.M12,
            M11 * first.M21 + M21 * first.M22, M12 * first.M21 + M22 * first.M22,
            M11 * first.Dx + M21 * first.Dy + Dx, M12 * first.Dx + M22 * first.Dy + Dy);
        if (!result.IsValid) throw new InvalidDataException("The composed shared transform exceeds its supported bounds.");
        return result;
    }
}

public enum HomeProductivityTransformSpace { Local, Document }

/// <summary>Explicit owner-supplied geometry in a supported document unit. Width/height are the original
/// local bounds; rotation, translation, scale and mirroring remain non-destructive in LocalToDocument.</summary>
public sealed record HomeProductivityGeometry(int SchemaVersion, string Unit, double Width, double Height,
    HomeProductivityAffine LocalToDocument)
{
    [JsonIgnore] public bool IsValid => SchemaVersion == 1 && (Unit is "document-pixel" or "typographic-point") &&
        double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0 &&
        Width <= 1e9 && Height <= 1e9 && LocalToDocument is { IsValid: true };
}

/// <summary>Pure common geometry semantics only. This does not authorize writes, mint revisions, update an
/// owner graph or imply rendering support. An owner must explicitly project geometry and commit through its
/// existing shared action provider before exposing editing. Unsupported geometry stays preserved.</summary>
public static class HomeProductivityGeometryOperations
{
    public const string LayoutProperty = "sharedGeometry";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static HomeProductivityGeometry Read(HomeProductivityObject source)
    {
        if (source.Layout.ValueKind != JsonValueKind.Object || !source.Layout.TryGetProperty(LayoutProperty, out var value))
            throw new NotSupportedException("This shared object has no explicit canonical geometry projection.");
        var geometry = value.Deserialize<HomeProductivityGeometry>(Json);
        if (geometry is not { IsValid: true }) throw new NotSupportedException("The shared geometry version, unit or bounds are unsupported.");
        return geometry;
    }

    public static HomeProductivityObject Project(HomeProductivityObject source, HomeProductivityGeometry geometry)
    {
        if (source.ObjectId == Guid.Empty || !geometry.IsValid || source.Layout.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A geometry projection requires valid bounds and an existing shared object identity.");
        if (source.Layout.TryGetProperty(LayoutProperty, out _))
            throw new InvalidOperationException("An existing shared geometry projection must be transformed, not silently replaced.");
        var layout = JsonNode.Parse(source.Layout.GetRawText())!.AsObject();
        layout[LayoutProperty] = JsonSerializer.SerializeToNode(geometry, Json);
        return source with { Layout = JsonSerializer.SerializeToElement(layout, Json) };
    }

    public static HomeProductivityObject Transform(HomeProductivityObject source, HomeProductivityAffine operation,
        HomeProductivityTransformSpace space, double pivotX, double pivotY)
    {
        var geometry = Read(source);
        if (!operation.IsValid || !double.IsFinite(pivotX) || !double.IsFinite(pivotY) ||
            Math.Abs(pivotX) > 1e12 || Math.Abs(pivotY) > 1e12 || !Enum.IsDefined(space))
            throw new InvalidDataException("A shared transform requires a finite explicit pivot and supported composition space.");
        var toPivot = HomeProductivityAffine.Identity with { Dx = pivotX, Dy = pivotY };
        var fromPivot = HomeProductivityAffine.Identity with { Dx = -pivotX, Dy = -pivotY };
        var aroundPivot = toPivot.Compose(operation).Compose(fromPivot);
        var next = space == HomeProductivityTransformSpace.Document
            ? aroundPivot.Compose(geometry.LocalToDocument)
            : geometry.LocalToDocument.Compose(aroundPivot);
        // Edit only the known matrix. Preserve unknown geometry and layout extension values.
        var layout = JsonNode.Parse(source.Layout.GetRawText())!.AsObject();
        var matrix = layout[LayoutProperty]!.AsObject()["localToDocument"]!.AsObject();
        foreach (var property in JsonSerializer.SerializeToElement(next, Json).EnumerateObject())
            matrix[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        return source with { Layout = JsonSerializer.SerializeToElement(layout, Json) };
    }
}
