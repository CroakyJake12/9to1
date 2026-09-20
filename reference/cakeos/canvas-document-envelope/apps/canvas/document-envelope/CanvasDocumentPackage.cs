using System.Text.Json;
using System.Text.Json.Serialization;

namespace CakeOS.Canvas.Document;

/// <summary>
/// Cake-owned Canvas document identity and board list. Rnote payloads are opaque
/// per-board engine snapshots; the semantic document remains owned by CakeOS.
/// </summary>
public sealed class CanvasDocumentPackage
{
    public const string SchemaName = "cake.canvas.document";
    public const int CurrentSchemaVersion = 1;

    public string Schema { get; set; } = SchemaName;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid DocumentId { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled canvas";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid ActiveBoardId { get; set; }
    public List<CanvasBoardPackage> Boards { get; set; } = [];
    public Dictionary<string, JsonElement> Extensions { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>One Cake-owned board. EnginePayload is intentionally excluded from canonical JSON.</summary>
public sealed class CanvasBoardPackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Board";
    public CanvasBoardState State { get; set; } = new();

    /// <summary>Opaque Rnote engine snapshot. It is not the canonical Cake document model.</summary>
    [JsonIgnore]
    public byte[]? EnginePayload { get; set; }

    [JsonIgnore]
    public string EngineUpstreamRelease { get; set; } = "v0.14.2";
}

/// <summary>
/// Canonical Cake board semantics. Typed ink preserves data Rnote 0.14.2 cannot,
/// including tilt. Structured overlays remain lossless JSON during staged migration.
/// </summary>
public sealed class CanvasBoardState
{
    public CanvasViewportState Viewport { get; set; } = new();
    public bool Infinite { get; set; } = true;
    public List<CanvasInkStroke> InkStrokes { get; set; } = [];
    public List<JsonElement> StructuredObjects { get; set; } = [];
    public List<JsonElement> GhostLayers { get; set; } = [];
    public Dictionary<string, JsonElement> Extensions { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CanvasViewportState
{
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Zoom { get; set; } = 1;
}

public sealed class CanvasInkStroke
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Tool { get; set; } = "pen";
    public string Colour { get; set; } = "#FF2F80ED";
    public double BaseWidth { get; set; } = 2.5;
    public double Opacity { get; set; } = 1;
    public bool IsGhost { get; set; }
    public Guid? GhostLayerId { get; set; }
    public string RecognitionText { get; set; } = string.Empty;
    public double RecognitionConfidence { get; set; }
    public List<CanvasInkPoint> Points { get; set; } = [];
}

public sealed class CanvasInkPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Pressure { get; set; } = 0.5;
    public double TiltX { get; set; }
    public double TiltY { get; set; }
    public long TimestampMilliseconds { get; set; }
}
