using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace CakeOS.Canvas.Document;

/// <summary>
/// Reads and writes the Cake Canvas v1 package without extracting archive paths to disk.
/// Fixed per-board paths and engine digests make corruption/path confusion fail closed.
/// </summary>
public static class CanvasDocumentPackageCodec
{
    private const string ManifestPath = "manifest.json";
    private const long MaxManifestBytes = 1 * 1024 * 1024;
    private const long MaxBoardJsonBytes = 16 * 1024 * 1024;
    private const long MaxEnginePayloadBytes = 512L * 1024 * 1024;
    private const long MaxTotalReadBytes = 1024L * 1024 * 1024;
    private const int MaxBoards = 128;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static void Write(Stream output, CanvasDocumentPackage document)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        if (!output.CanWrite) throw new ArgumentException("Canvas package output stream is not writable.", nameof(output));
        Validate(document);

        var manifest = new PackageManifest
        {
            Schema = CanvasDocumentPackage.SchemaName,
            SchemaVersion = CanvasDocumentPackage.CurrentSchemaVersion,
            DocumentId = document.DocumentId,
            Title = document.Title,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            ActiveBoardId = document.ActiveBoardId,
            Extensions = document.Extensions,
        };

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var board in document.Boards)
        {
            var canonicalPath = CanonicalBoardPath(board.Id);
            WriteJsonEntry(archive, canonicalPath, board.State);

            EngineSnapshotManifest? engine = null;
            if (board.EnginePayload is { Length: > 0 } payload)
            {
                var enginePath = EngineBoardPath(board.Id);
                WriteBytesEntry(archive, enginePath, payload, CompressionLevel.NoCompression);
                engine = new EngineSnapshotManifest
                {
                    Kind = "rnote",
                    UpstreamRelease = board.EngineUpstreamRelease,
                    MediaType = "application/x-rnote",
                    Path = enginePath,
                    Sha256 = Sha256(payload),
                };
            }

            manifest.Boards.Add(new BoardManifest
            {
                Id = board.Id,
                Title = board.Title,
                CanonicalPath = canonicalPath,
                Engine = engine,
            });
        }

        WriteJsonEntry(archive, ManifestPath, manifest);
    }

    public static CanvasDocumentPackage Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek)
            throw new ArgumentException("Canvas package input stream must be readable and seekable.", nameof(input));

        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        ValidateArchiveEntries(archive);
        long totalRead = 0;

        var manifestEntry = archive.GetEntry(ManifestPath)
            ?? throw new InvalidDataException("Canvas package is missing manifest.json.");
        var manifest = Deserialize<PackageManifest>(ReadEntryBytes(manifestEntry, MaxManifestBytes, ref totalRead), ManifestPath);
        ValidateManifest(manifest);

        var document = new CanvasDocumentPackage
        {
            Schema = manifest.Schema,
            SchemaVersion = manifest.SchemaVersion,
            DocumentId = manifest.DocumentId,
            Title = manifest.Title,
            CreatedAt = manifest.CreatedAt,
            UpdatedAt = manifest.UpdatedAt,
            ActiveBoardId = manifest.ActiveBoardId,
            Extensions = manifest.Extensions ?? new(StringComparer.Ordinal),
        };

        foreach (var descriptor in manifest.Boards)
        {
            var expectedCanonicalPath = CanonicalBoardPath(descriptor.Id);
            if (!string.Equals(descriptor.CanonicalPath, expectedCanonicalPath, StringComparison.Ordinal))
                throw new InvalidDataException($"Canvas board {descriptor.Id} has an invalid canonical path.");

            var stateEntry = archive.GetEntry(expectedCanonicalPath)
                ?? throw new InvalidDataException($"Canvas package is missing {expectedCanonicalPath}.");
            var state = Deserialize<CanvasBoardState>(ReadEntryBytes(stateEntry, MaxBoardJsonBytes, ref totalRead), expectedCanonicalPath);

            byte[]? enginePayload = null;
            var upstreamRelease = "v0.14.2";
            if (descriptor.Engine is { } engine)
            {
                var expectedEnginePath = EngineBoardPath(descriptor.Id);
                if (!string.Equals(engine.Kind, "rnote", StringComparison.Ordinal)
                    || !string.Equals(engine.Path, expectedEnginePath, StringComparison.Ordinal)
                    || !string.Equals(engine.MediaType, "application/x-rnote", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Canvas board {descriptor.Id} has unsupported engine metadata.");
                }

                var engineEntry = archive.GetEntry(expectedEnginePath)
                    ?? throw new InvalidDataException($"Canvas package is missing {expectedEnginePath}.");
                enginePayload = ReadEntryBytes(engineEntry, MaxEnginePayloadBytes, ref totalRead);
                var digest = Sha256(enginePayload);
                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(digest),
                        ParseSha256(engine.Sha256, descriptor.Id)))
                {
                    throw new InvalidDataException($"Canvas board {descriptor.Id} engine snapshot digest does not match the manifest.");
                }
                upstreamRelease = engine.UpstreamRelease;
            }

            document.Boards.Add(new CanvasBoardPackage
            {
                Id = descriptor.Id,
                Title = descriptor.Title,
                State = state,
                EnginePayload = enginePayload,
                EngineUpstreamRelease = upstreamRelease,
            });
        }

        Validate(document);
        return document;
    }

    public static void Validate(CanvasDocumentPackage document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!string.Equals(document.Schema, CanvasDocumentPackage.SchemaName, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported Canvas schema '{document.Schema}'.");
        if (document.SchemaVersion != CanvasDocumentPackage.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported Canvas schema version {document.SchemaVersion}.");
        if (document.DocumentId == Guid.Empty) throw new InvalidDataException("Canvas document id must not be empty.");
        if (string.IsNullOrWhiteSpace(document.Title)) throw new InvalidDataException("Canvas document title must not be empty.");
        if (document.Boards.Count is < 1 or > MaxBoards)
            throw new InvalidDataException($"Canvas document must contain between 1 and {MaxBoards} boards.");

        var ids = new HashSet<Guid>();
        foreach (var board in document.Boards)
        {
            if (board.Id == Guid.Empty || !ids.Add(board.Id))
                throw new InvalidDataException("Canvas board ids must be non-empty and unique.");
            if (string.IsNullOrWhiteSpace(board.Title))
                throw new InvalidDataException($"Canvas board {board.Id} title must not be empty.");
            ValidateBoardState(board.Id, board.State);
            if (board.EnginePayload is { LongLength: > MaxEnginePayloadBytes })
                throw new InvalidDataException($"Canvas board {board.Id} engine snapshot exceeds the v1 size limit.");
            if (string.IsNullOrWhiteSpace(board.EngineUpstreamRelease))
                throw new InvalidDataException($"Canvas board {board.Id} engine upstream release must not be empty.");
        }

        if (!ids.Contains(document.ActiveBoardId))
            throw new InvalidDataException("Canvas active board id must identify one of the document boards.");
    }

    private static void ValidateManifest(PackageManifest manifest)
    {
        if (!string.Equals(manifest.Schema, CanvasDocumentPackage.SchemaName, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported Canvas package schema '{manifest.Schema}'.");
        if (manifest.SchemaVersion != CanvasDocumentPackage.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported Canvas package schema version {manifest.SchemaVersion}.");
        if (manifest.Boards.Count is < 1 or > MaxBoards)
            throw new InvalidDataException($"Canvas package manifest must contain between 1 and {MaxBoards} boards.");
        if (manifest.Boards.Select(board => board.Id).Distinct().Count() != manifest.Boards.Count)
            throw new InvalidDataException("Canvas package manifest contains duplicate board ids.");
    }

    private static void ValidateBoardState(Guid boardId, CanvasBoardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Finite(state.Viewport.CenterX) || !Finite(state.Viewport.CenterY)
            || !Finite(state.Viewport.Zoom) || state.Viewport.Zoom is < 0.05 or > 64)
        {
            throw new InvalidDataException($"Canvas board {boardId} has an invalid viewport.");
        }

        var strokeIds = new HashSet<Guid>();
        foreach (var stroke in state.InkStrokes)
        {
            if (stroke.Id == Guid.Empty || !strokeIds.Add(stroke.Id))
                throw new InvalidDataException($"Canvas board {boardId} ink stroke ids must be non-empty and unique.");
            if (string.IsNullOrWhiteSpace(stroke.Tool) || string.IsNullOrWhiteSpace(stroke.Colour))
                throw new InvalidDataException($"Canvas board {boardId} contains incomplete ink metadata.");
            if (!Finite(stroke.BaseWidth) || stroke.BaseWidth <= 0
                || !Finite(stroke.Opacity) || stroke.Opacity is < 0 or > 1
                || !Finite(stroke.RecognitionConfidence) || stroke.RecognitionConfidence is < 0 or > 1)
            {
                throw new InvalidDataException($"Canvas board {boardId} contains invalid ink styling/recognition metadata.");
            }

            foreach (var point in stroke.Points)
            {
                if (!Finite(point.X) || !Finite(point.Y) || !Finite(point.Pressure)
                    || point.Pressure is < 0 or > 1 || !Finite(point.TiltX) || !Finite(point.TiltY))
                {
                    throw new InvalidDataException($"Canvas board {boardId} contains an invalid ink point.");
                }
            }
        }
    }

    private static void ValidateArchiveEntries(ZipArchive archive)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!names.Add(entry.FullName))
                throw new InvalidDataException($"Canvas package contains duplicate archive entry '{entry.FullName}'.");
            if (!SafeArchivePath(entry.FullName))
                throw new InvalidDataException($"Canvas package contains unsafe archive entry '{entry.FullName}'.");
        }
    }

    private static bool SafeArchivePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith('/', StringComparison.Ordinal)
            || path.Contains('\\') || path.Contains(':')) return false;
        return path.Split('/').All(segment => segment is not ("" or "." or ".."));
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry, long maxBytes, ref long totalRead)
    {
        if (entry.Length < 0 || entry.Length > maxBytes)
            throw new InvalidDataException($"Canvas package entry '{entry.FullName}' exceeds its size limit.");
        totalRead = checked(totalRead + entry.Length);
        if (totalRead > MaxTotalReadBytes)
            throw new InvalidDataException("Canvas package exceeds the total v1 read limit.");

        using var source = entry.Open();
        using var buffer = new MemoryStream((int)entry.Length);
        source.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (bytes.LongLength != entry.Length)
            throw new InvalidDataException($"Canvas package entry '{entry.FullName}' was truncated while reading.");
        return bytes;
    }

    private static T Deserialize<T>(byte[] bytes, string path) where T : class =>
        JsonSerializer.Deserialize<T>(bytes, JsonOptions)
        ?? throw new InvalidDataException($"Canvas package entry '{path}' did not contain a valid {typeof(T).Name}.");

    private static void WriteJsonEntry<T>(ZipArchive archive, string path, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        WriteBytesEntry(archive, path, bytes, CompressionLevel.Optimal);
    }

    private static void WriteBytesEntry(ZipArchive archive, string path, byte[] bytes, CompressionLevel compression)
    {
        var entry = archive.CreateEntry(path, compression);
        using var destination = entry.Open();
        destination.Write(bytes);
    }

    private static byte[] ParseSha256(string value, Guid boardId)
    {
        try
        {
            var parsed = Convert.FromHexString(value);
            if (parsed.Length == 32) return parsed;
        }
        catch (FormatException)
        {
        }
        throw new InvalidDataException($"Canvas board {boardId} has an invalid SHA-256 digest.");
    }

    private static string Sha256(byte[] payload) => Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    private static string CanonicalBoardPath(Guid id) => $"boards/{id:N}/board.json";
    private static string EngineBoardPath(Guid id) => $"boards/{id:N}/engine.rnote";
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private sealed class PackageManifest
    {
        public string Schema { get; set; } = string.Empty;
        public int SchemaVersion { get; set; }
        public Guid DocumentId { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public Guid ActiveBoardId { get; set; }
        public List<BoardManifest> Boards { get; set; } = [];
        public Dictionary<string, JsonElement>? Extensions { get; set; }
    }

    private sealed class BoardManifest
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string CanonicalPath { get; set; } = string.Empty;
        public EngineSnapshotManifest? Engine { get; set; }
    }

    private sealed class EngineSnapshotManifest
    {
        public string Kind { get; set; } = string.Empty;
        public string UpstreamRelease { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
    }
}
