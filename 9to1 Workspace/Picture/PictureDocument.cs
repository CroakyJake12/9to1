using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;

namespace HavenOS.Images;

/// <summary>A versioned, editable Picture document that retains its source identity and operations.</summary>
public sealed class PictureDocument
{
    public const int CurrentSchemaVersion = 7;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    [JsonRequired]
    public Guid DocumentId { get; init; } = Guid.NewGuid();
    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string DisplayName { get; init; } = "Untitled picture";
    public string? FileId { get; init; }
    public string? SourcePath { get; init; }
    public string? SourceRevision { get; init; }
    /// <summary>Exact canonical shared visual graph bytes, encoded by CanvasArtifactCodec.
    /// This retained graph is not a Files/Home permission or a second editable bitmap store.</summary>
    private readonly byte[]? _compositionState;
    public byte[]? CompositionState { get => _compositionState?.ToArray(); init => _compositionState = value?.ToArray(); }
    private readonly ProductivitySnapshotHistory? _semanticHistory;
    /// <summary>Canonical app-neutral retained history, not a storage grant or
    /// serialized live selection. Owner snapshot payloads exclude this envelope.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProductivitySnapshotHistory? SemanticHistory
    {
        get => _semanticHistory;
        init => _semanticHistory = value is null ? null : value with
        { Undo = value.Undo is null ? null! : Array.AsReadOnly(value.Undo.ToArray()),
          Redo = value.Redo is null ? null! : Array.AsReadOnly(value.Redo.ToArray()) };
    }
    public int? InitialCanvasWidth { get; init; }
    public int? InitialCanvasHeight { get; init; }
    [JsonRequired]
    public int CanvasWidth { get; init; }
    [JsonRequired]
    public int CanvasHeight { get; init; }
    [JsonRequired]
    public IReadOnlyList<PictureOperation> Operations { get; init; } = [];
    [JsonRequired]
    public long Revision { get; init; }

    public static PictureDocument Create(int width, int height, string? fileId = null, string? sourceRevision = null, string? sourcePath = null)
    {
        if (width is < 1 or > 32768 || height is < 1 or > 32768 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "Canvas dimensions must be positive.");
        return new PictureDocument { CanvasWidth = width, CanvasHeight = height, InitialCanvasWidth = width, InitialCanvasHeight = height, FileId = fileId, SourceRevision = sourceRevision, SourcePath = sourcePath,
            DisplayName = string.IsNullOrWhiteSpace(sourcePath) ? "Untitled picture" : Path.GetFileNameWithoutExtension(sourcePath) };
    }

    public PictureDocument Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || (long)x + width > CanvasWidth || (long)y + height > CanvasHeight)
            throw new ArgumentOutOfRangeException(nameof(x), "Crop must be a positive rectangle within the current canvas.");
        return WithDocument(CanvasWidth: width, CanvasHeight: height,
            Operations: [.. Operations, new CropOperation(x, y, width, height)], Revision: checked(Revision + 1));
    }

    public PictureDocument Rotate(int clockwiseQuarterTurns = 1)
    {
        var turns = ((clockwiseQuarterTurns % 4) + 4) % 4;
        if (turns == 0) return this;
        return WithDocument(turns % 2 == 0 ? CanvasWidth : CanvasHeight, turns % 2 == 0 ? CanvasHeight : CanvasWidth,
            [.. Operations, new RotateOperation(turns)], checked(Revision + 1));
    }

    /// <summary>Retains an arbitrary-angle transform against original pixels.
    /// ExpandCanvas keeps the entire rotated image; false deliberately clips
    /// its preview/export to the current canvas while source remains editable.</summary>
    public PictureDocument Straighten(double clockwiseDegrees, bool expandCanvas = true)
    {
        var operation = new StraightenOperation(clockwiseDegrees, expandCanvas);
        var dimensions = operation.OutputDimensions(CanvasWidth, CanvasHeight);
        if (clockwiseDegrees == 0) return this;
        return WithDocument(dimensions.Width, dimensions.Height, [.. Operations, operation], checked(Revision + 1));
    }

    public PictureDocument Flip(bool horizontal) => WithDocument(CanvasWidth, CanvasHeight,
        [.. Operations, new FlipOperation(horizontal)], checked(Revision + 1));

    public PictureDocument Resize(int width, int height)
    {
        if (width is < 1 or > 32768 || height is < 1 or > 32768 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "Resize dimensions exceed the supported full-quality raster limit.");
        if (width == CanvasWidth && height == CanvasHeight) return this;
        return WithDocument(width, height, [.. Operations, new ResizeOperation(width, height)], checked(Revision + 1));
    }

    /// <summary>Changes canvas bounds without resampling source pixels. The
    /// prior composition remains retained; pixels outside the new bounds can
    /// be recovered by editing/removing this operation or shared Undo.</summary>
    public PictureDocument ResizeCanvas(int width, int height, int offsetX = 0, int offsetY = 0)
    {
        if (width is < 1 or > 32768 || height is < 1 or > 32768 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "Canvas dimensions exceed the supported full-quality raster limit.");
        if (offsetX is < -32768 or > 32768 || offsetY is < -32768 or > 32768)
            throw new ArgumentOutOfRangeException(nameof(offsetX), "Canvas placement must be within 32768 source pixels of the new origin.");
        if (width == CanvasWidth && height == CanvasHeight && offsetX == 0 && offsetY == 0) return this;
        return WithDocument(width, height, [.. Operations, new CanvasResizeOperation(width, height, offsetX, offsetY)], checked(Revision + 1));
    }

    public PictureDocument AdjustColor(RasterColorAdjustment settings, PictureColorWorkingSpace workingSpace)
    {
        ArgumentNullException.ThrowIfNull(settings); settings.Validate();
        if (workingSpace != PictureColorWorkingSpace.Srgb8)
            throw new NotSupportedException("Choose the supported sRGB working colour space before applying adjustments.");
        if (settings.IsIdentity) return this;
        return WithDocument(CanvasWidth, CanvasHeight, [.. Operations, new ColorAdjustmentOperation(settings, workingSpace)], checked(Revision + 1));
    }

    public PictureDocument Blur(RasterBlur settings)
    {
        ArgumentNullException.ThrowIfNull(settings); settings.Validate(CanvasWidth, CanvasHeight);
        return WithDocument(CanvasWidth, CanvasHeight, [.. Operations, new BlurOperation(settings)], checked(Revision + 1));
    }

    public PictureDocument Pixelate(RasterPixelation settings)
    {
        ArgumentNullException.ThrowIfNull(settings); settings.Validate(CanvasWidth, CanvasHeight);
        return WithDocument(CanvasWidth, CanvasHeight, [.. Operations, new PixelationOperation(settings)], checked(Revision + 1));
    }

    public PictureDocument CreateCopy()
    {
        var copyId = Guid.NewGuid();
        return new()
        {
            DocumentId = copyId, SchemaVersion = CurrentSchemaVersion, DisplayName = DisplayName,
            SourcePath = SourcePath, SourceRevision = SourceRevision,
            InitialCanvasWidth = InitialCanvasWidth, InitialCanvasHeight = InitialCanvasHeight,
            CompositionState = CompositionState is null ? null : PictureCompositionAdapter.CopyForDocument(this, CompositionState, copyId),
            CanvasWidth = CanvasWidth, CanvasHeight = CanvasHeight, Operations = Operations.ToArray(), Revision = 0,
        };
    }

    internal PictureDocument WithRevision(long revision, int minimumSchema = 0) => WithDocument(CanvasWidth, CanvasHeight, Operations, revision, minimumSchema: minimumSchema);
    internal PictureDocument WithComposition(byte[] state) => WithDocument(CanvasWidth, CanvasHeight, Operations, Revision, state);
    internal PictureDocument RasterHistoryBase(int width, int height) => WithDocument(width, height, [], 0, minimumSchema: 3);

    public PictureDocument RevertToOriginal(int width, int height) =>
        WithDocument(width, height, [], checked(Revision + 1), clearComposition: true);

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var payload = Serialize();
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(payload, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    /// <summary>Validated editable state for the canonical Files adapter; this does not grant storage authority.</summary>
    public byte[] Serialize()
    {
        if (Operations is null) throw new InvalidDataException("The Picture document has no editable operation graph.");
        var operations = new List<PictureOperation>();
        foreach (var operation in Operations)
        {
            if (operations.Count == 1_000_000)
                throw new InvalidDataException("The Picture document exceeds the supported operation count.");
            operations.Add(operation);
        }
        var snapshot = WithDocument(CanvasWidth, CanvasHeight, operations.ToArray(), Revision, history: SemanticHistory);
        snapshot.Validate();
        return JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
    }

    public static PictureDocument Deserialize(ReadOnlySpan<byte> bytes)
    {
        var document = JsonSerializer.Deserialize<PictureDocument>(bytes, JsonOptions)
            ?? throw new InvalidDataException("The Picture document is empty.");
        document.Validate();
        return document;
    }

    public static async Task<PictureDocument> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var document = await JsonSerializer.DeserializeAsync<PictureDocument>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The Picture document is empty.");
        document.Validate();
        return document;
    }

    private void Validate()
    {
        if (SchemaVersion is not (1 or 2 or 3 or 4 or 5 or 6 or CurrentSchemaVersion))
            throw new InvalidDataException($"Unsupported Picture document schema version {SchemaVersion}.");
        if (DocumentId == Guid.Empty || string.IsNullOrWhiteSpace(DisplayName) || CanvasWidth is < 1 or > 32768 || CanvasHeight is < 1 or > 32768 || (long)CanvasWidth * CanvasHeight > 100_000_000 || Revision < 0 || Operations is null)
            throw new InvalidDataException("The Picture document contains invalid identity, dimensions, revision, or operations.");
        if (InitialCanvasWidth.HasValue != InitialCanvasHeight.HasValue ||
            InitialCanvasWidth is < 1 or > 32768 || InitialCanvasHeight is < 1 or > 32768 ||
            (long)(InitialCanvasWidth ?? 0) * (InitialCanvasHeight ?? 0) > 100_000_000 || Operations.Count > 1_000_000)
            throw new InvalidDataException("The Picture source dimensions or operation count exceed supported limits.");
        if (SchemaVersion == 1 && CompositionState is not null)
            throw new InvalidDataException("Legacy Picture schema1 has no shared composition field; upgrade through the owning edit action.");
        if (SchemaVersion < 3 && SemanticHistory is not null)
            throw new InvalidDataException("This legacy Picture schema has no retained outer history; upgrade through its owning session.");
        if (CompositionState is not null) PictureCompositionAdapter.Validate(this);
        if (SemanticHistory is not null) PictureHistoryAdapter.Validate(this);
        if (SchemaVersion < 4 && Operations.Any(operation => operation is CanvasResizeOperation or ColorAdjustmentOperation))
            throw new InvalidDataException("This legacy Picture schema has no retained canvas-resize or colour-adjustment operation; upgrade through its owning edit action.");
        if (SchemaVersion < 7 && Operations.Any(operation => operation is BlurOperation))
            throw new InvalidDataException("This legacy Picture schema has no retained blur operation; upgrade through its owning edit action.");
        if (SchemaVersion < 6 && Operations.Any(operation => operation is PixelationOperation))
            throw new InvalidDataException("This legacy Picture schema has no retained pixelation operation; upgrade through its owning edit action.");
        if (SchemaVersion < 5 && Operations.Any(operation => operation is StraightenOperation))
            throw new InvalidDataException("This legacy Picture schema has no retained arbitrary-angle operation; upgrade through its owning edit action.");
        foreach (var operation in Operations)
        {
            var valid = operation switch
            {
                CropOperation crop => crop.X >= 0 && crop.Y >= 0 && crop.Width > 0 && crop.Height > 0,
                RotateOperation rotate => rotate.ClockwiseQuarterTurns is >= 1 and <= 3,
                StraightenOperation angle => double.IsFinite(angle.ClockwiseDegrees) && angle.ClockwiseDegrees is >= -180 and <= 180,
                FlipOperation => true,
                ResizeOperation resize => resize.Width is >= 1 and <= 32768 && resize.Height is >= 1 and <= 32768
                    && (long)resize.Width * resize.Height <= 100_000_000,
                CanvasResizeOperation canvas => canvas.Width is >= 1 and <= 32768 && canvas.Height is >= 1 and <= 32768
                    && (long)canvas.Width * canvas.Height <= 100_000_000
                    && canvas.OffsetX is >= -32768 and <= 32768 && canvas.OffsetY is >= -32768 and <= 32768,
                ColorAdjustmentOperation color => IsValidColor(color),
                PixelationOperation pixels => IsValidPixelation(pixels),
                BlurOperation blur => IsValidBlur(blur),
                _ => false
            };
            if (!valid)
                throw new InvalidDataException("The Picture document contains an unsupported or invalid operation.");
        }
    }

    private static bool IsValidBlur(BlurOperation blur)
    {
        if (blur.Settings is null) return false;
        try { blur.Settings.ValidateGeometry(); return true; }
        catch (ArgumentException) { return false; }
    }
    private static bool IsValidPixelation(PixelationOperation pixels)
    {
        if (pixels.Settings is null) return false;
        // Exact preceding-canvas bounds are validated by Replay/render; this
        // persisted graph pass validates geometry without inventing a canvas.
        try { pixels.Settings.ValidateGeometry(); return true; }
        catch (ArgumentException) { return false; }
    }
    private static bool IsValidColor(ColorAdjustmentOperation color)
    {
        if (color.Settings is null || color.WorkingSpace != PictureColorWorkingSpace.Srgb8) return false;
        try { color.Settings.Validate(); return true; } catch (ArgumentException) { return false; }
    }

    internal PictureDocument WithoutHistory() => WithDocument(CanvasWidth, CanvasHeight, Operations, Revision);
    internal PictureDocument WithHistory(ProductivitySnapshotHistory history) =>
        WithDocument(CanvasWidth, CanvasHeight, Operations, Revision, history: history);
    internal PictureDocument WithSourceDimensions(int width, int height) => new()
    {
        DocumentId = DocumentId, SchemaVersion = SchemaVersion, DisplayName = DisplayName, FileId = FileId,
        SourcePath = SourcePath, SourceRevision = SourceRevision, CompositionState = CompositionState,
        InitialCanvasWidth = width, InitialCanvasHeight = height, CanvasWidth = CanvasWidth, CanvasHeight = CanvasHeight,
        Operations = Operations, Revision = Revision
    };

    internal byte[] SerializeSnapshot() => WithoutHistory().Serialize();
    internal static PictureDocument DeserializeSnapshot(ReadOnlyMemory<byte> bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        if (json.RootElement.TryGetProperty("semanticHistory", out var history) && history.ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("An owner history snapshot must not contain a nested outer history.");
        return Deserialize(bytes.Span);
    }

    private PictureDocument WithDocument(int CanvasWidth, int CanvasHeight, IReadOnlyList<PictureOperation> Operations, long Revision,
        byte[]? composition = null, bool clearComposition = false, ProductivitySnapshotHistory? history = null, int minimumSchema = 0) => new()
    {
        // Compatible persisted snapshots retain their exact original version and
        // checksum. New capability requirements promote forwards only; a forward-format
        // owner never drops below its owned version just because Undo exposes an older frame.
        DocumentId = DocumentId, SchemaVersion = Math.Max(SchemaVersion, Math.Max(minimumSchema,
            Operations.Any(operation => operation is BlurOperation) ? 7 :
            Operations.Any(operation => operation is PixelationOperation) ? 6 :
            Operations.Any(operation => operation is StraightenOperation) ? 5 :
            Operations.Any(operation => operation is CanvasResizeOperation or ColorAdjustmentOperation) ? 4 :
            history is not null ? 3 : composition is not null ? 2 : 1)),
        DisplayName = DisplayName, FileId = FileId, SourcePath = SourcePath, SourceRevision = SourceRevision,
        InitialCanvasWidth = InitialCanvasWidth, InitialCanvasHeight = InitialCanvasHeight,
        CompositionState = clearComposition ? null : composition ?? CompositionState, SemanticHistory = history,
        CanvasWidth = CanvasWidth, CanvasHeight = CanvasHeight, Operations = Operations, Revision = Revision,
    };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CropOperation), "crop")]
[JsonDerivedType(typeof(RotateOperation), "rotate")]
[JsonDerivedType(typeof(FlipOperation), "flip")]
[JsonDerivedType(typeof(StraightenOperation), "straighten")]
[JsonDerivedType(typeof(ResizeOperation), "resize")]
[JsonDerivedType(typeof(CanvasResizeOperation), "canvasResize")]
[JsonDerivedType(typeof(ColorAdjustmentOperation), "colorAdjustment")]
[JsonDerivedType(typeof(PixelationOperation), "pixelation")]
[JsonDerivedType(typeof(BlurOperation), "blur")]
public abstract record PictureOperation;

public sealed record CropOperation(int X, int Y, int Width, int Height) : PictureOperation;
public sealed record RotateOperation(int ClockwiseQuarterTurns) : PictureOperation;
public sealed record FlipOperation(bool Horizontal) : PictureOperation;
public sealed record ResizeOperation(int Width, int Height) : PictureOperation;

public sealed record CanvasResizeOperation(int Width, int Height, int OffsetX, int OffsetY) : PictureOperation;

public sealed record BlurOperation([property: JsonRequired] RasterBlur Settings) : PictureOperation;

public sealed record PixelationOperation([property: JsonRequired] RasterPixelation Settings) : PictureOperation;

public enum PictureColorWorkingSpace { Srgb8 = 1 }
public sealed record ColorAdjustmentOperation([property: JsonRequired] RasterColorAdjustment Settings,
    [property: JsonRequired] PictureColorWorkingSpace WorkingSpace) : PictureOperation;

/// <summary>One typed operation in the SAME Picture graph, not a flattened
/// alternate source. Geometry is evaluated against the actual preceding canvas.</summary>
public sealed record StraightenOperation([property: JsonRequired] double ClockwiseDegrees,
    [property: JsonRequired] bool ExpandCanvas) : PictureOperation
{
    internal (double Cos, double Sin) Rotation()
    {
        if (!double.IsFinite(ClockwiseDegrees) || ClockwiseDegrees is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(ClockwiseDegrees), "Choose a finite angle from −180° to 180°.");
        var radians = ClockwiseDegrees * Math.PI / 180;
        var cosine = Math.Cos(radians); var sine = Math.Sin(radians);
        // Exact quarter-turns retain exact integer pixel dimensions; avoid
        // floating-point epsilon inventing an extra transparent pixel row.
        if (ClockwiseDegrees is -180 or 180) return (-1, 0);
        if (ClockwiseDegrees == -90) return (0, -1);
        if (ClockwiseDegrees == 90) return (0, 1);
        if (ClockwiseDegrees == 0) return (1, 0);
        return (cosine, sine);
    }
    internal (int Width, int Height) OutputDimensions(int width, int height)
    {
        if (width is < 1 or > 32768 || height is < 1 or > 32768 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "The transform needs valid current raster dimensions.");
        var rotation = Rotation();
        var outputWidth = ExpandCanvas ? checked((int)Math.Ceiling(width * Math.Abs(rotation.Cos) + height * Math.Abs(rotation.Sin))) : width;
        var outputHeight = ExpandCanvas ? checked((int)Math.Ceiling(width * Math.Abs(rotation.Sin) + height * Math.Abs(rotation.Cos))) : height;
        if (outputWidth is < 1 or > 32768 || outputHeight is < 1 or > 32768 || (long)outputWidth * outputHeight > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(ClockwiseDegrees), "The rotated image exceeds supported canvas bounds. Keep the canvas or reduce its size first.");
        return (outputWidth, outputHeight);
    }
}
