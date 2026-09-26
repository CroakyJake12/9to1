using System.Text.Json;
using System.Text.Json.Serialization;

namespace Haven.Application;

public enum CanvasArtifactFormatErrorCode
{
    InvalidDocument,
    UnsupportedFormat,
    UnsupportedSchemaVersion,
    ValidationFailed
}

public sealed record CanvasArtifactValidationIssue(string Code, string Path, string Message);

public sealed class CanvasArtifactFormatException : Exception
{
    public CanvasArtifactFormatException(
        CanvasArtifactFormatErrorCode code,
        string message,
        IReadOnlyList<CanvasArtifactValidationIssue>? issues = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Issues = issues ?? Array.Empty<CanvasArtifactValidationIssue>();
    }

    public CanvasArtifactFormatErrorCode Code { get; }
    public IReadOnlyList<CanvasArtifactValidationIssue> Issues { get; }
}

/// <summary>The JSON envelope stored in the native .9to1c file.</summary>
public sealed record CanvasArtifactFile
{
    public const string CanonicalFormat = "9to1.Canvas";
    public const string FileExtension = ".9to1c";

    [JsonRequired]
    public string Format { get; init; } = CanonicalFormat;
    [JsonRequired]
    public int SchemaVersion { get; init; } = CanvasArtifact.CurrentSchemaVersion;
    [JsonRequired]
    public CanvasArtifact? Artifact { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Versioned semantic codec. Unknown optional JSON fields are retained at each
/// modeled level; unknown format/schema versions fail explicitly without
/// attempting to reinterpret them.
/// </summary>
public static class CanvasArtifactCodec
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static byte[] Serialize(CanvasArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        EnsureValid(artifact);
        var file = new CanvasArtifactFile
        {
            SchemaVersion = CanvasArtifact.CurrentSchemaVersion,
            Artifact = artifact,
            ExtensionData = artifact.EnvelopeExtensionData
        };
        return JsonSerializer.SerializeToUtf8Bytes(file, Options);
    }

    public static CanvasArtifact Deserialize(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
            throw new CanvasArtifactFormatException(CanvasArtifactFormatErrorCode.InvalidDocument, "Canvas document is empty.");

        CanvasArtifactFile? file;
        try
        {
            using var document = JsonDocument.Parse(content.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Canvas document root must be an object.");
            file = JsonSerializer.Deserialize<CanvasArtifactFile>(content, Options);
        }
        catch (JsonException exception)
        {
            throw new CanvasArtifactFormatException(
                CanvasArtifactFormatErrorCode.InvalidDocument,
                "Canvas document is not valid .9to1c data.",
                innerException: exception);
        }

        if (file is null)
            throw new CanvasArtifactFormatException(CanvasArtifactFormatErrorCode.InvalidDocument, "Canvas document has no root value.");
        if (!string.Equals(file.Format, CanvasArtifactFile.CanonicalFormat, StringComparison.Ordinal))
            throw new CanvasArtifactFormatException(CanvasArtifactFormatErrorCode.UnsupportedFormat, $"Unsupported Canvas format '{file.Format}'.");
        if (file.SchemaVersion != CanvasArtifact.CurrentSchemaVersion)
            throw UnsupportedVersion("envelope", file.SchemaVersion);
        if (file.Artifact is null)
            throw new CanvasArtifactFormatException(CanvasArtifactFormatErrorCode.InvalidDocument, "Canvas document has no artifact payload.");
        if (file.Artifact.SchemaVersion != CanvasArtifact.CurrentSchemaVersion)
            throw UnsupportedVersion("artifact", file.Artifact.SchemaVersion);

        file.Artifact.EnvelopeExtensionData = file.ExtensionData;
        EnsureValid(file.Artifact);
        return file.Artifact;
    }

    public static IReadOnlyList<CanvasArtifactValidationIssue> Validate(CanvasArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var issues = new List<CanvasArtifactValidationIssue>();
        var entityIds = new HashSet<Guid>();

        void Require(bool condition, string code, string path, string message)
        {
            if (!condition) issues.Add(new CanvasArtifactValidationIssue(code, path, message));
        }

        void AddId(Guid id, string path)
        {
            Require(id != Guid.Empty, "empty_id", path, "Stable entity IDs cannot be empty.");
            if (id != Guid.Empty)
                Require(entityIds.Add(id), "duplicate_id", path, "Stable entity IDs must be unique within the artifact.");
        }

        Require(artifact.SchemaVersion == CanvasArtifact.CurrentSchemaVersion, "unsupported_schema", "schemaVersion", "The artifact schema version is not supported.");
        AddId(artifact.ArtifactId, "artifactId");
        Require(artifact.RevisionId != Guid.Empty, "empty_revision", "revisionId", "Revision IDs cannot be empty.");
        Require(!string.IsNullOrWhiteSpace(artifact.DisplayName), "missing_name", "displayName", "DisplayName cannot be empty.");
        Require(Enum.IsDefined(artifact.CanvasMode), "invalid_mode", "canvasMode", "Canvas mode is not supported.");
        Require(artifact.Pages is { Count: > 0 }, "missing_pages", "pages", "A Canvas artifact must contain at least one page.");
        Require(artifact.PageOrder is not null, "missing_page_order", "pageOrder", "Page order is required.");
        Require(artifact.SharedResources is not null, "missing_resources", "sharedResources", "SharedResources cannot be null.");
        Require(artifact.CollaborationMetadata is not null, "missing_collaboration_metadata", "collaborationMetadata", "Collaboration metadata cannot be null.");
        Require(artifact.DocumentSettings is not null, "missing_document_settings", "documentSettings", "Document settings cannot be null.");

        if (artifact.Pages is null || artifact.PageOrder is null)
            return issues;

        var pageIds = artifact.Pages.Where(page => page is not null).Select(page => page!.PageId).ToHashSet();
        Require(artifact.PageOrder.Count == pageIds.Count
            && artifact.PageOrder.Distinct().Count() == artifact.PageOrder.Count
            && pageIds.SetEquals(artifact.PageOrder),
            "invalid_page_order", "pageOrder", "PageOrder must contain each page ID exactly once.");

        for (var pageIndex = 0; pageIndex < artifact.Pages.Count; pageIndex++)
        {
            var page = artifact.Pages[pageIndex];
            var path = $"pages[{pageIndex}]";
            if (page is null)
            {
                issues.Add(new CanvasArtifactValidationIssue("null_page", path, "Pages cannot contain null entries."));
                continue;
            }
            AddId(page.PageId, $"{path}.pageId");
            Require(page.RevisionId != Guid.Empty, "empty_revision", $"{path}.revisionId", "Revision IDs cannot be empty.");
            Require(page.Background is not null, "missing_background", $"{path}.background", "Each page requires a background definition.");
            if (page.Background is not null)
            {
                Require(!string.IsNullOrWhiteSpace(page.Background.Kind), "missing_background_kind", $"{path}.background.kind", "Background kind is required.");
                Require(page.Background.Spacing is null || PositiveFinite(page.Background.Spacing.Value), "invalid_background_spacing", $"{path}.background.spacing", "Background spacing must be positive and finite.");
            }

            if (artifact.CanvasMode == CanvasDocumentMode.Paged)
            {
                Require(page.Bounds is not null, "missing_page_bounds", $"{path}.bounds", "Paged documents require explicit page bounds.");
                if (page.Bounds is { } bounds)
                    Require(ValidBounds(bounds), "invalid_page_bounds", $"{path}.bounds", "Page bounds and dimensions must be finite and dimensions must be positive.");
            }
            else
            {
                Require(page.Bounds is null, "infinite_page_bounds", $"{path}.bounds", "Infinite pages cannot introduce an artificial page boundary.");
            }

            var layers = page.Layers ?? [];
            var objects = page.Objects ?? [];
            var strokes = page.Strokes ?? [];
            Require(page.LayerOrder is not null
                && page.LayerOrder.Count == layers.Count
                && page.LayerOrder.Distinct().Count() == page.LayerOrder.Count
                && layers.Where(layer => layer is not null).Select(layer => layer!.LayerId).ToHashSet().SetEquals(page.LayerOrder),
                "invalid_layer_order", $"{path}.layerOrder", "LayerOrder must contain each layer ID exactly once.");
            Require(page.ObjectOrder is not null
                && page.ObjectOrder.Count == objects.Count
                && page.ObjectOrder.Distinct().Count() == page.ObjectOrder.Count
                && objects.Where(item => item is not null).Select(item => item!.ObjectId).ToHashSet().SetEquals(page.ObjectOrder),
                "invalid_object_order", $"{path}.objectOrder", "ObjectOrder must contain each object ID exactly once.");
            Require(page.StrokeOrder is not null
                && page.StrokeOrder.Count == strokes.Count
                && page.StrokeOrder.Distinct().Count() == page.StrokeOrder.Count
                && strokes.Where(stroke => stroke is not null).Select(stroke => stroke!.StrokeId).ToHashSet().SetEquals(page.StrokeOrder),
                "invalid_stroke_order", $"{path}.strokeOrder", "StrokeOrder must contain each stroke ID exactly once.");

            var layerIds = layers.Select(layer => layer.LayerId).ToHashSet();
            for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var layer = layers[layerIndex];
                if (layer is null)
                {
                    issues.Add(new CanvasArtifactValidationIssue("null_layer", $"{path}.layers[{layerIndex}]", "Layers cannot contain null entries."));
                    continue;
                }
                AddId(layer.LayerId, $"{path}.layers[{layerIndex}].layerId");
                Require(layer.RevisionId != Guid.Empty, "empty_revision", $"{path}.layers[{layerIndex}].revisionId", "Revision IDs cannot be empty.");
                Require(!string.IsNullOrWhiteSpace(layer.Name), "missing_layer_name", $"{path}.layers[{layerIndex}].name", "Layer names cannot be empty.");
            }

            for (var objectIndex = 0; objectIndex < objects.Count; objectIndex++)
            {
                var item = objects[objectIndex];
                var objectPath = $"{path}.objects[{objectIndex}]";
                if (item is null)
                {
                    issues.Add(new CanvasArtifactValidationIssue("null_object", objectPath, "Objects cannot contain null entries."));
                    continue;
                }
                AddId(item.ObjectId, $"{objectPath}.objectId");
                Require(item.RevisionId != Guid.Empty, "empty_revision", $"{objectPath}.revisionId", "Revision IDs cannot be empty.");
                Require(!string.IsNullOrWhiteSpace(item.ObjectTypeId), "missing_object_type", $"{objectPath}.objectTypeId", "Object type ID is required.");
                Require(layerIds.Contains(item.LayerId), "unknown_layer", $"{objectPath}.layerId", "Each object must reference a layer on its page.");
                Require(item.Geometry is not null && ValidRect(item.Geometry), "invalid_geometry", $"{objectPath}.geometry", "Object geometry must contain finite coordinates and non-negative dimensions.");
                Require(item.Transform is not null && ValidTransform(item.Transform), "invalid_transform", $"{objectPath}.transform", "Object transforms must contain finite values and non-zero scales.");
                if (item.SharedObjectRef is { } shared)
                {
                    Require(!string.IsNullOrWhiteSpace(shared.OwnerAppId), "missing_shared_owner", $"{objectPath}.sharedObjectRef.ownerAppId", "Shared object owner is required.");
                    Require(shared.ArtifactId != Guid.Empty && shared.ObjectId != Guid.Empty && shared.RevisionId != Guid.Empty,
                        "invalid_shared_reference", $"{objectPath}.sharedObjectRef", "Shared object references require stable artifact, object, and revision IDs.");
                    Require(!string.IsNullOrWhiteSpace(shared.ObjectTypeId), "missing_shared_type", $"{objectPath}.sharedObjectRef.objectTypeId", "Shared object type ID is required.");
                }
            }

            for (var strokeIndex = 0; strokeIndex < strokes.Count; strokeIndex++)
            {
                var stroke = strokes[strokeIndex];
                var strokePath = $"{path}.strokes[{strokeIndex}]";
                if (stroke is null)
                {
                    issues.Add(new CanvasArtifactValidationIssue("null_stroke", strokePath, "Strokes cannot contain null entries."));
                    continue;
                }
                AddId(stroke.StrokeId, $"{strokePath}.strokeId");
                Require(stroke.RevisionId != Guid.Empty, "empty_revision", $"{strokePath}.revisionId", "Revision IDs cannot be empty.");
                Require(layerIds.Contains(stroke.LayerId), "unknown_layer", $"{strokePath}.layerId", "Each stroke must reference a layer on its page.");
                Require(!string.IsNullOrWhiteSpace(stroke.ToolDefinitionId), "missing_tool", $"{strokePath}.toolDefinitionId", "Tool definition ID is required.");
                Require(stroke.Samples is { Count: > 0 }, "missing_samples", $"{strokePath}.samples", "A persisted stroke must contain at least one sample.");
                Require(stroke.ResolvedBrushProperties is not null && ValidBrush(stroke.ResolvedBrushProperties), "invalid_brush", $"{strokePath}.resolvedBrushProperties", "Resolved brush properties are invalid.");
                Require(stroke.Transform is not null && ValidTransform(stroke.Transform), "invalid_transform", $"{strokePath}.transform", "Stroke transforms must contain finite values and non-zero scales.");
                var samples = stroke.Samples ?? [];
                for (var sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
                {
                    var sample = samples[sampleIndex];
                    Require(sample is not null && double.IsFinite(sample.X) && double.IsFinite(sample.Y)
                        && double.IsFinite(sample.Pressure) && sample.Pressure is >= 0 and <= 1
                        && double.IsFinite(sample.TiltX) && double.IsFinite(sample.TiltY)
                        && sample.TimestampMilliseconds >= 0,
                        "invalid_sample", $"{strokePath}.samples[{sampleIndex}]", "Stroke sample coordinates, pressure, tilt, and timestamp must be valid.");
                }
            }
        }

        for (var index = 0; index < (artifact.SharedResources?.Count ?? 0); index++)
        {
            var resource = artifact.SharedResources![index];
            if (resource is null)
            {
                issues.Add(new CanvasArtifactValidationIssue("null_resource", $"sharedResources[{index}]", "Shared resources cannot contain null entries."));
                continue;
            }
            Require(resource.ResourceId != Guid.Empty, "empty_resource_id", $"sharedResources[{index}].resourceId", "Resource IDs cannot be empty.");
            Require(!string.IsNullOrWhiteSpace(resource.ResourceTypeId), "missing_resource_type", $"sharedResources[{index}].resourceTypeId", "Resource type ID is required.");
            Require(resource.SchemaVersion > 0, "invalid_resource_schema", $"sharedResources[{index}].schemaVersion", "Resource schema version must be positive.");
        }

        return issues;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static void EnsureValid(CanvasArtifact artifact)
    {
        var issues = Validate(artifact);
        if (issues.Count > 0)
            throw new CanvasArtifactFormatException(
                CanvasArtifactFormatErrorCode.ValidationFailed,
                "Canvas artifact failed semantic validation.",
                issues);
    }

    private static CanvasArtifactFormatException UnsupportedVersion(string scope, int version) => new(
        CanvasArtifactFormatErrorCode.UnsupportedSchemaVersion,
        $"Canvas {scope} schema version {version} is not supported; the current version is {CanvasArtifact.CurrentSchemaVersion}.");

    private static bool PositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private static bool ValidBounds(CanvasPageBounds bounds) =>
        double.IsFinite(bounds.X) && double.IsFinite(bounds.Y)
        && PositiveFinite(bounds.Width) && PositiveFinite(bounds.Height)
        && !string.IsNullOrWhiteSpace(bounds.Orientation);

    private static bool ValidRect(CanvasRect rect) =>
        double.IsFinite(rect.X) && double.IsFinite(rect.Y)
        && double.IsFinite(rect.Width) && double.IsFinite(rect.Height)
        && rect.Width >= 0 && rect.Height >= 0;

    private static bool ValidTransform(CanvasTransform transform) =>
        double.IsFinite(transform.TranslateX) && double.IsFinite(transform.TranslateY)
        && double.IsFinite(transform.ScaleX) && double.IsFinite(transform.ScaleY)
        && transform.ScaleX != 0 && transform.ScaleY != 0
        && double.IsFinite(transform.RotationDegrees);

    private static bool ValidBrush(CanvasBrushProperties brush) =>
        !string.IsNullOrWhiteSpace(brush.EngineId)
        && !string.IsNullOrWhiteSpace(brush.Color)
        && PositiveFinite(brush.BaseWidth)
        && double.IsFinite(brush.Opacity) && brush.Opacity is >= 0 and <= 1
        && (brush.PresetRevisionId is null || brush.PresetId is not null);
}
