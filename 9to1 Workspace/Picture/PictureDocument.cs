using System.Text.Json;
using System.Text.Json.Serialization;

namespace HavenOS.Images;

/// <summary>A versioned, editable Picture document that retains its source identity and operations.</summary>
public sealed class PictureDocument
{
    public const int CurrentSchemaVersion = 1;
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
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Canvas dimensions must be positive.");
        return new PictureDocument { CanvasWidth = width, CanvasHeight = height, FileId = fileId, SourceRevision = sourceRevision, SourcePath = sourcePath,
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

    public PictureDocument Flip(bool horizontal) => WithDocument(CanvasWidth, CanvasHeight,
        [.. Operations, new FlipOperation(horizontal)], checked(Revision + 1));

    public PictureDocument Resize(int width, int height)
    {
        if (width is < 1 or > 32768 || height is < 1 or > 32768 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "Resize dimensions exceed the supported full-quality raster limit.");
        if (width == CanvasWidth && height == CanvasHeight) return this;
        return WithDocument(width, height, [.. Operations, new ResizeOperation(width, height)], checked(Revision + 1));
    }

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
        var snapshot = WithDocument(CanvasWidth, CanvasHeight, operations.ToArray(), Revision);
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
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported Picture document schema version {SchemaVersion}.");
        if (DocumentId == Guid.Empty || string.IsNullOrWhiteSpace(DisplayName) || CanvasWidth <= 0 || CanvasHeight <= 0 || Revision < 0 || Operations is null)
            throw new InvalidDataException("The Picture document contains invalid identity, dimensions, revision, or operations.");
        foreach (var operation in Operations)
        {
            var valid = operation switch
            {
                CropOperation crop => crop.X >= 0 && crop.Y >= 0 && crop.Width > 0 && crop.Height > 0,
                RotateOperation rotate => rotate.ClockwiseQuarterTurns is >= 1 and <= 3,
                FlipOperation => true,
                ResizeOperation resize => resize.Width is >= 1 and <= 32768 && resize.Height is >= 1 and <= 32768
                    && (long)resize.Width * resize.Height <= 100_000_000,
                _ => false
            };
            if (!valid)
                throw new InvalidDataException("The Picture document contains an unsupported or invalid operation.");
        }
    }

    private PictureDocument WithDocument(int CanvasWidth, int CanvasHeight, IReadOnlyList<PictureOperation> Operations, long Revision) => new()
    {
        DocumentId = DocumentId, SchemaVersion = SchemaVersion, DisplayName = DisplayName, FileId = FileId, SourcePath = SourcePath, SourceRevision = SourceRevision,
        CanvasWidth = CanvasWidth, CanvasHeight = CanvasHeight, Operations = Operations, Revision = Revision,
    };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CropOperation), "crop")]
[JsonDerivedType(typeof(RotateOperation), "rotate")]
[JsonDerivedType(typeof(FlipOperation), "flip")]
[JsonDerivedType(typeof(ResizeOperation), "resize")]
public abstract record PictureOperation;

public sealed record CropOperation(int X, int Y, int Width, int Height) : PictureOperation;
public sealed record RotateOperation(int ClockwiseQuarterTurns) : PictureOperation;
public sealed record FlipOperation(bool Horizontal) : PictureOperation;
public sealed record ResizeOperation(int Width, int Height) : PictureOperation;
