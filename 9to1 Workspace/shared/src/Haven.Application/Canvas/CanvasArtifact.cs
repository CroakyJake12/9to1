using System.Text.Json;
using System.Text.Json.Serialization;

namespace Haven.Application;

/// <summary>The two persisted Canvas document modes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CanvasDocumentMode>))]
public enum CanvasDocumentMode
{
    Infinite,
    Paged
}

/// <summary>
/// Versioned, app-owned Canvas document data. Storage identity is independent
/// of display names, page order, and file locations.
/// </summary>
public sealed class CanvasArtifact
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public Guid ArtifactId { get; set; } = Guid.NewGuid();
    [JsonRequired]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [JsonRequired]
    public Guid RevisionId { get; set; } = Guid.NewGuid();
    [JsonRequired]
    public string DisplayName { get; set; } = "Untitled canvas";
    [JsonRequired]
    public CanvasDocumentMode CanvasMode { get; set; } = CanvasDocumentMode.Infinite;
    [JsonRequired]
    public List<Guid> PageOrder { get; set; } = [];
    [JsonRequired]
    public List<CanvasPage> Pages { get; set; } = [];
    [JsonRequired]
    public List<CanvasResourceReference> SharedResources { get; set; } = [];
    [JsonRequired]
    public CanvasCollaborationMetadata CollaborationMetadata { get; set; } = new();
    [JsonRequired]
    public CanvasDocumentSettings DocumentSettings { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public Dictionary<string, JsonElement>? EnvelopeExtensionData { get; set; }

    public static CanvasArtifact Create(string? displayName = null, CanvasDocumentMode mode = CanvasDocumentMode.Infinite)
    {
        var layer = new CanvasLayer();
        var page = new CanvasPage
        {
            Bounds = mode == CanvasDocumentMode.Paged ? new CanvasPageBounds(0, 0, 794, 1123) : null,
            Layers = [layer],
            LayerOrder = [layer.LayerId]
        };
        return new CanvasArtifact
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Untitled canvas" : displayName.Trim(),
            CanvasMode = mode,
            PageOrder = [page.PageId],
            Pages = [page]
        };
    }
}

/// <summary>A logical Canvas page; null bounds denote the unbounded infinite surface.</summary>
public sealed record CanvasPage
{
    [JsonRequired]
    public Guid PageId { get; init; } = Guid.NewGuid();
    public CanvasPageBounds? Bounds { get; init; }
    [JsonRequired]
    public CanvasBackgroundDefinition Background { get; init; } = new();
    [JsonRequired]
    public List<Guid> LayerOrder { get; init; } = [];
    [JsonRequired]
    public List<Guid> ObjectOrder { get; init; } = [];
    [JsonRequired]
    public List<Guid> StrokeOrder { get; init; } = [];
    [JsonRequired]
    public List<CanvasLayer> Layers { get; init; } = [];
    [JsonRequired]
    public List<CanvasObject> Objects { get; init; } = [];
    [JsonRequired]
    public List<CanvasInkStroke> Strokes { get; init; } = [];
    [JsonRequired]
    public Guid RevisionId { get; init; } = Guid.NewGuid();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record CanvasPageBounds(double X, double Y, double Width, double Height, string Orientation = "Portrait");

public sealed record CanvasBackgroundDefinition
{
    public string Kind { get; init; } = "solid";
    public string Color { get; init; } = "#FFFFFFFF";
    public string? PatternId { get; init; }
    public double? Spacing { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record CanvasLayer
{
    [JsonRequired]
    public Guid LayerId { get; init; } = Guid.NewGuid();
    [JsonRequired]
    public string Name { get; init; } = "Layer 1";
    public bool IsVisible { get; init; } = true;
    public bool IsLocked { get; init; }
    [JsonRequired]
    public Guid RevisionId { get; init; } = Guid.NewGuid();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>
/// Spatial wrapper for a registered shared object. SharedPayload is retained
/// as JSON so Canvas does not reinterpret another app's semantic schema.
/// </summary>
public sealed record CanvasObject
{
    [JsonRequired]
    public Guid ObjectId { get; init; } = Guid.NewGuid();
    [JsonRequired]
    public string ObjectTypeId { get; init; } = "";
    [JsonRequired]
    public Guid LayerId { get; init; }
    [JsonRequired]
    public CanvasRect Geometry { get; init; } = new(0, 0, 1, 1);
    [JsonRequired]
    public CanvasTransform Transform { get; init; } = new();
    [JsonRequired]
    public CanvasObjectStyle Style { get; init; } = new();
    [JsonRequired]
    public CanvasAccessibilityMetadata Accessibility { get; init; } = new();
    public CanvasSharedObjectReference? SharedObjectRef { get; init; }
    public JsonElement? SharedPayload { get; init; }
    [JsonRequired]
    public Guid RevisionId { get; init; } = Guid.NewGuid();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record CanvasSharedObjectReference(
    string OwnerAppId,
    Guid ArtifactId,
    Guid ObjectId,
    string ObjectTypeId,
    Guid RevisionId);

public sealed record CanvasRect(double X, double Y, double Width, double Height);

public sealed record CanvasTransform
{
    public double TranslateX { get; init; }
    public double TranslateY { get; init; }
    public double ScaleX { get; init; } = 1;
    public double ScaleY { get; init; } = 1;
    public double RotationDegrees { get; init; }
}

public sealed record CanvasObjectStyle
{
    public Dictionary<string, JsonElement> Properties { get; init; } = new(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record CanvasAccessibilityMetadata
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? AltText { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

/// <summary>Structured, editable ink independent of any one renderer.</summary>
public sealed record CanvasInkStroke
{
    [JsonRequired]
    public Guid StrokeId { get; init; } = Guid.NewGuid();
    [JsonRequired]
    public Guid LayerId { get; init; }
    [JsonRequired]
    public string ToolDefinitionId { get; init; } = "pen";
    [JsonRequired]
    public List<CanvasStrokeSample> Samples { get; init; } = [];
    [JsonRequired]
    public CanvasBrushProperties ResolvedBrushProperties { get; init; } = new();
    [JsonRequired]
    public CanvasTransform Transform { get; init; } = new();
    [JsonRequired]
    public Guid RevisionId { get; init; } = Guid.NewGuid();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record CanvasStrokeSample(
    double X,
    double Y,
    double Pressure = 0.5,
    double TiltX = 0,
    double TiltY = 0,
    long TimestampMilliseconds = 0);

public sealed record CanvasBrushProperties
{
    [JsonRequired]
    public string EngineId { get; init; } = "rnote";
    [JsonRequired]
    public string Color { get; init; } = "#FF000000";
    [JsonRequired]
    public double BaseWidth { get; init; } = 2.5;
    [JsonRequired]
    public double Opacity { get; init; } = 1;
    public Guid? PresetId { get; init; }
    public Guid? PresetRevisionId { get; init; }
    [JsonRequired]
    public Dictionary<string, JsonElement> EngineParameters { get; init; } = new(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record CanvasResourceReference(string ResourceTypeId, Guid ResourceId, int SchemaVersion);

public sealed record CanvasCollaborationMetadata
{
    public string? CollaborationId { get; init; }
    public Dictionary<string, JsonElement> Properties { get; init; } = new(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record CanvasDocumentSettings
{
    public Dictionary<string, JsonElement> Properties { get; init; } = new(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
