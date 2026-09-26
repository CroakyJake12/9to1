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

    public Guid DocumentId { get; init; } = Guid.NewGuid();
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string? FileId { get; init; }
    public string? SourcePath { get; init; }
    public string? SourceRevision { get; init; }
    public int CanvasWidth { get; init; }
    public int CanvasHeight { get; init; }
    public IReadOnlyList<PictureOperation> Operations { get; init; } = [];
    public long Revision { get; init; }

    public static PictureDocument Create(int width, int height, string? fileId = null, string? sourceRevision = null, string? sourcePath = null)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Canvas dimensions must be positive.");
        return new PictureDocument { CanvasWidth = width, CanvasHeight = height, FileId = fileId, SourceRevision = sourceRevision, SourcePath = sourcePath };
    }

    public PictureDocument Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || (long)x + width > CanvasWidth || (long)y + height > CanvasHeight)
            throw new ArgumentOutOfRangeException(nameof(x), "Crop must be a positive rectangle within the current canvas.");
        return WithDocument(CanvasWidth: width, CanvasHeight: height,
            Operations: [.. Operations, new CropOperation(x, y, width, height)], Revision: checked(Revision + 1));
    }

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate();
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, this, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
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
        if (DocumentId == Guid.Empty || CanvasWidth <= 0 || CanvasHeight <= 0 || Revision < 0 || Operations is null)
            throw new InvalidDataException("The Picture document contains invalid identity, dimensions, revision, or operations.");
        foreach (var operation in Operations)
        {
            if (operation is not CropOperation crop || crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0)
                throw new InvalidDataException("The Picture document contains an unsupported or invalid operation.");
        }
    }

    private PictureDocument WithDocument(int CanvasWidth, int CanvasHeight, IReadOnlyList<PictureOperation> Operations, long Revision) => new()
    {
        DocumentId = DocumentId, SchemaVersion = SchemaVersion, FileId = FileId, SourcePath = SourcePath, SourceRevision = SourceRevision,
        CanvasWidth = CanvasWidth, CanvasHeight = CanvasHeight, Operations = Operations, Revision = Revision,
    };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CropOperation), "crop")]
public abstract record PictureOperation;

public sealed record CropOperation(int X, int Y, int Width, int Height) : PictureOperation;
