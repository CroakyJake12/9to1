using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Haven.Application;
using Haven.Core;
using NineToOne.Cui.AI;

namespace HavenOS.Home.Core;

/// <summary>Detached canonical vector graph, not a second editable store. ViewBox gives the local bounds;
/// DocumentVectorTransform remains the existing owning graph's transform in those coordinates.</summary>
public sealed record HomeProductivityVectorBinding(string ControlId, Guid ObjectId, JsonElement CanonicalShape);

public sealed class HomeVectorShapeObjectHandler : IHomeProductivityObjectHandler, IHomeProductivityObjectCloneHandler
{
    private static JsonElement Json(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }
    public HomeProductivityObjectSchema Schema { get; } = new("drawing.vector", 1, false,
        Json("{\"type\":\"object\",\"required\":[\"SchemaVersion\",\"Id\",\"ViewBox\",\"Transform\",\"Paths\"],\"additionalProperties\":true}"),
        new[] { "object.create", "object.render", "vector.fill" }.ToFrozenSet(StringComparer.Ordinal));
    public IReadOnlyList<HomeProductivityObjectActionDescriptor> Actions { get; } = [new("vector.fill", 1,
        Json("{\"type\":\"object\",\"properties\":{\"pathId\":{\"type\":\"string\"},\"color\":{\"type\":\"string\"}},\"required\":[\"pathId\",\"color\"],\"additionalProperties\":false}"))];

    public HomeProductivityObject Project(DocumentVectorShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return Create(shape.Id, JsonSerializer.SerializeToElement(shape));
    }
    public HomeProductivityObject Create(Guid objectId, JsonElement content)
    {
        _ = ReadCanonical(content, objectId);
        return new(objectId, Schema.ObjectType, Schema.SchemaVersion, content.Clone(), Json("{}"), Json("{}"), [], Json("{}"));
    }
    public HomeProductivityObject Transform(HomeProductivityObject source, HomeProductivityAction action)
    {
        _ = Validate(source);
        if (action.ActionId != "vector.fill" || action.Version != 1 || !action.ObjectIds.Contains(source.ObjectId) ||
            !ActionJsonSchemaValidator.Validate(Actions[0].ArgumentSchema.GetRawText(), action.Arguments, out _) ||
            !Guid.TryParse(action.Arguments.GetProperty("pathId").GetString(), out var pathId) ||
            !Color(action.Arguments.GetProperty("color").GetString()))
            throw new InvalidDataException("The vector fill action requires an exact canonical path and supported color.");
        var content = JsonNode.Parse(source.Content.GetRawText())!.AsObject();
        var path = content["Paths"]!.AsArray().SingleOrDefault(item => item!["Id"]!.GetValue<Guid>() == pathId)
            ?? throw new InvalidDataException("The selected vector path no longer exists.");
        path["Fill"]!["Kind"] = (int)DocumentVectorFillKind.Solid;
        path["Fill"]!["Color"] = action.Arguments.GetProperty("color").GetString();
        return source with { Content = JsonSerializer.SerializeToElement(content) };
    }
    public HomeProductivityObjectRenderResult Render(HomeProductivityObject source)
    {
        var shape = Validate(source);
        if (shape.ClippingPathId is not null)
            throw new NotSupportedException("This shared vector renderer does not yet support canonical clipping paths; content is preserved.");
        var controlId = "vector-" + source.ObjectId.ToString("N");
        var unsupported = Unknown(source.Content, JsonSerializer.SerializeToElement(shape), "Content").ToList();
        unsupported.AddRange(source.Formatting.EnumerateObject().Select(value => "Formatting." + value.Name));
        unsupported.AddRange(source.Layout.EnumerateObject().Select(value => "Layout." + value.Name));
        unsupported.AddRange(source.Accessibility.EnumerateObject().Select(value => "Accessibility." + value.Name));
        if (source.AssetReferences.Count > 0) unsupported.Add("AssetReferences");
        if (source.Extensions is not null) unsupported.AddRange(source.Extensions.Keys.Select(key => "Extensions." + key));
        return new($"<Object id=\"{controlId}\" type=\"spe.vector\" />", unsupported)
        { VectorBindings = [new(controlId, source.ObjectId, source.Content.Clone())] };
    }
    public HomeProductivityObject CloneForPaste(HomeProductivityObject source, Guid newObjectId)
    {
        var original = Validate(source);
        if (newObjectId == Guid.Empty || newObjectId == source.ObjectId ||
            Unknown(source.Content, JsonSerializer.SerializeToElement(original), "Content").Any())
            throw new InvalidDataException("Vector cloning requires a new identity and understood canonical references.");
        // Use the existing owner's insertion identity mapping, then apply only those ID changes to the
        // original JSON. Normalize inside that helper cannot rewrite retained geometry or metadata here.
        var identities = DocumentVectorShapes.CloneForInsertion(original);
        var content = JsonNode.Parse(source.Content.GetRawText())!.AsObject();
        content["Id"] = newObjectId;
        var paths = content["Paths"]!.AsArray();
        for (var p = 0; p < paths.Count; p++)
        {
            paths[p]!["Id"] = identities.Paths[p].Id;
            var subpaths = paths[p]!["Subpaths"]!.AsArray();
            for (var s = 0; s < subpaths.Count; s++)
            {
                subpaths[s]!["Id"] = identities.Paths[p].Subpaths[s].Id;
                var nodes = subpaths[s]!["Nodes"]!.AsArray();
                for (var n = 0; n < nodes.Count; n++) nodes[n]!["Id"] = identities.Paths[p].Subpaths[s].Nodes[n].Id;
            }
        }
        if (content["ConnectorPoints"] is JsonArray points)
            for (var i = 0; i < points.Count; i++) points[i]!["Id"] = identities.ConnectorPoints[i].Id;
        if (original.ClippingPathId is not null) content["ClippingPathId"] = identities.ClippingPathId;
        var result = source with { ObjectId = newObjectId, Content = JsonSerializer.SerializeToElement(content) };
        _ = Validate(result);
        return result;
    }
    private DocumentVectorShape Validate(HomeProductivityObject source)
    {
        if (source.ObjectType != Schema.ObjectType || source.SchemaVersion != 1 ||
            source.Formatting.ValueKind != JsonValueKind.Object || source.Layout.ValueKind != JsonValueKind.Object ||
            source.Accessibility.ValueKind != JsonValueKind.Object || source.AssetReferences is null)
            throw new InvalidDataException("Unsupported shared vector object schema; content is preserved.");
        return ReadCanonical(source.Content, source.ObjectId);
    }
    public static DocumentVectorShape ReadCanonical(JsonElement content, Guid expectedObjectId)
    {
        try
        {
            if (content.GetRawText().Length > 4 * 1024 * 1024 || expectedObjectId == Guid.Empty ||
                Id(content) != expectedObjectId || content.GetProperty("SchemaVersion").GetInt32() != 1)
                throw new InvalidDataException("Invalid canonical vector identity/schema.");
            _ = content.GetProperty("ViewBox"); _ = content.GetProperty("Transform");
            foreach (var path in content.GetProperty("Paths").EnumerateArray())
            {
                _ = Id(path); _ = path.GetProperty("Fill"); _ = path.GetProperty("Stroke");
                foreach (var subpath in path.GetProperty("Subpaths").EnumerateArray())
                { _ = Id(subpath); foreach (var node in subpath.GetProperty("Nodes").EnumerateArray()) { _ = Id(node); _ = node.GetProperty("X"); _ = node.GetProperty("Y"); } }
            }
            if (content.TryGetProperty("ConnectorPoints", out var points)) foreach (var point in points.EnumerateArray()) _ = Id(point);
            var shape = content.Deserialize<DocumentVectorShape>() ?? throw new InvalidDataException("Missing vector graph.");
            if (!DocumentVectorShapeValidator.Validate(shape).IsValid || !Enum.IsDefined(shape.SourceKind) ||
                shape.ViewBox.Width > 100000 || shape.ViewBox.Height > 100000 || shape.ConnectorPoints is null ||
                shape.ConnectorPoints.Count > 20000 || shape.ConnectorPoints.Select(point => point.Id).Distinct().Count() != shape.ConnectorPoints.Count ||
                shape.Paths.Any(path => !Enum.IsDefined(path.FillRule) || !Enum.IsDefined(path.Fill.Kind) ||
                    !Enum.IsDefined(path.Stroke.Join) || !Enum.IsDefined(path.Stroke.Cap) || path.Stroke.Width > 10000 ||
                    (path.Fill.Kind == DocumentVectorFillKind.Solid && !Color(path.Fill.Color)) ||
                    (path.Stroke.Enabled && !Color(path.Stroke.Color)) ||
                    path.Subpaths.Any(subpath => subpath.Nodes.Any(node => !Enum.IsDefined(node.Kind) || !Enum.IsDefined(node.IncomingSegment)))))
                throw new InvalidDataException("Unsupported or invalid canonical vector geometry; content is preserved.");
            return shape;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new InvalidDataException("Invalid canonical vector graph; no IDs were minted or repaired.", error); }
    }
    private static Guid Id(JsonElement value) => value.GetProperty("Id").TryGetGuid(out var id) && id != Guid.Empty
        ? id : throw new InvalidDataException("Canonical vector identifiers must be explicitly preserved.");
    private static bool Color(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$", RegexOptions.CultureInvariant);
    private static IEnumerable<string> Unknown(JsonElement source, JsonElement known, string path)
    {
        if (source.ValueKind == JsonValueKind.Object && known.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in source.EnumerateObject())
            {
                if (!known.TryGetProperty(property.Name, out var expected)) { yield return path + "." + property.Name; continue; }
                foreach (var nested in Unknown(property.Value, expected, path + "." + property.Name)) yield return nested;
            }
        }
        else if (source.ValueKind == JsonValueKind.Array && known.ValueKind == JsonValueKind.Array)
        {
            var originals = source.EnumerateArray().ToArray(); var expected = known.EnumerateArray().ToArray();
            for (var i = 0; i < Math.Min(originals.Length, expected.Length); i++)
                foreach (var nested in Unknown(originals[i], expected[i], path + "[" + i + "]")) yield return nested;
        }
    }
}
