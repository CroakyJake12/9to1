using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Versioned .9to1w package storage. The document body uses the current structured
/// NotesDocument schema; optional package parts and unknown manifest properties are
/// retained through the existing Write repository metadata.
/// </summary>
public sealed class WriteNativeDocumentPackageStore : IWriteNativeDocumentPackageStore
{
    public const string Extension = WriteNativeDocumentPackageMetadata.Extension;
    public const string FormatId = "org.9to1.write";
    public const int CurrentFormatVersion = 1;

    private const string ManifestEntryName = "manifest.json";
    private const string DocumentEntryName = "document.json";
    private const int MaximumEntryCount = 1024;
    private const long MaximumDocumentBytes = 64L * 1024 * 1024;
    private const long MaximumPackageBytes = 128L * 1024 * 1024;
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumEntryNameLength = 240;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    public async Task<WriteNativeDocumentPackageResult<NotesDocument>> OpenAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return Failure<NotesDocument>(WriteNativeDocumentPackageErrorCode.InvalidPath,
                "Choose a .9to1w document to open.", "9to1.Write.Open", recoverable: true);
        }

        try
        {
            await using var file = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
            var entries = ValidateEntries(archive);
            var manifestBytes = await ReadEntryAsync(entries[ManifestEntryName], MaximumManifestBytes, cancellationToken)
                .ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<PackageManifest>(manifestBytes, JsonOptions)
                ?? throw Fault(WriteNativeDocumentPackageErrorCode.InvalidPackage, "The package manifest is empty.");

            if (!string.Equals(manifest.Format, FormatId, StringComparison.Ordinal))
                throw Fault(WriteNativeDocumentPackageErrorCode.ImportUnsupported, "This file is not a supported 9to1 Write document.");
            if (manifest.Version != CurrentFormatVersion)
                throw Fault(WriteNativeDocumentPackageErrorCode.UnsupportedDocumentVersion,
                    $"This .9to1w document uses format version {manifest.Version}; this version of Write supports {CurrentFormatVersion}.");
            if (manifest.DocumentId == Guid.Empty || string.IsNullOrWhiteSpace(manifest.DocumentSha256))
                throw Fault(WriteNativeDocumentPackageErrorCode.InvalidPackage, "The package manifest is missing its document identity or integrity value.");

            var documentBytes = await ReadEntryAsync(entries[DocumentEntryName], MaximumDocumentBytes, cancellationToken)
                .ConfigureAwait(false);
            if (!HasSha256(documentBytes, manifest.DocumentSha256))
                throw Fault(WriteNativeDocumentPackageErrorCode.IntegrityCheckFailed,
                    "The document contents do not match the package integrity record.");

            var document = JsonSerializer.Deserialize<NotesDocument>(documentBytes, JsonOptions)
                ?? throw Fault(WriteNativeDocumentPackageErrorCode.InvalidPackage, "The document contents are empty.");
            if (document.SchemaVersion != NotesDocument.CurrentSchemaVersion)
                throw Fault(WriteNativeDocumentPackageErrorCode.UnsupportedDocumentVersion,
                    $"The document data uses schema version {document.SchemaVersion}; this version of Write supports {NotesDocument.CurrentSchemaVersion}.");
            if (document.Id == Guid.Empty || document.Id != manifest.DocumentId)
                throw Fault(WriteNativeDocumentPackageErrorCode.DocumentIdMismatch,
                    "The package manifest and document contain different document identities.");

            document.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var parts = await ReadOptionalPartsAsync(entries, manifest, cancellationToken).ConfigureAwait(false);
            var preserved = PreservedPackageState.Create(parts, manifest.AdditionalProperties);
            if (parts.Count > 0 || preserved.ManifestProperties.Count > 0)
                document.Metadata[WriteNativeDocumentPackageMetadata.PreservedPackageStateKey] =
                    JsonSerializer.Serialize(preserved, JsonOptions);
            else
                document.Metadata.Remove(WriteNativeDocumentPackageMetadata.PreservedPackageStateKey);

            return WriteNativeDocumentPackageResult<NotesDocument>.Success(document);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PackageFault fault)
        {
            return Failure<NotesDocument>(fault.Code, fault.Message, "9to1.Write.Open", recoverable: true);
        }
        catch (FileNotFoundException)
        {
            return Failure<NotesDocument>(WriteNativeDocumentPackageErrorCode.DocumentNotFound,
                "The selected document could not be found.", "9to1.Write.Open", recoverable: true);
        }
        catch (DirectoryNotFoundException)
        {
            return Failure<NotesDocument>(WriteNativeDocumentPackageErrorCode.DocumentNotFound,
                "The selected document location could not be found.", "9to1.Write.Open", recoverable: true);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure<NotesDocument>(WriteNativeDocumentPackageErrorCode.PermissionDenied,
                "Write cannot access this document. Check its file permissions and try again.", "9to1.Write.Open", recoverable: true);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException or ArgumentException)
        {
            return Failure<NotesDocument>(WriteNativeDocumentPackageErrorCode.InvalidPackage,
                "This .9to1w document is invalid or damaged. The original file was not changed.",
                "9to1.Write.Open", recoverable: true);
        }
    }

    public async Task<WriteNativeDocumentPackageResult<string>> SaveAsync(
        NotesDocument document,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return Failure<string>(WriteNativeDocumentPackageErrorCode.InvalidPath,
                "Choose a location for the .9to1w document.", "9to1.Write.Export", recoverable: true);
        }
        if (!string.Equals(Path.GetExtension(destinationPath), Extension, StringComparison.OrdinalIgnoreCase))
        {
            return Failure<string>(WriteNativeDocumentPackageErrorCode.ExportUnsupported,
                "The native Write package must use the .9to1w extension.", "9to1.Write.Export", recoverable: true);
        }
        if (document.Id == Guid.Empty || document.SchemaVersion != NotesDocument.CurrentSchemaVersion)
        {
            return Failure<string>(WriteNativeDocumentPackageErrorCode.UnsupportedDocumentVersion,
                "Write cannot save this document because its identity or document schema is unsupported.",
                "9to1.Write.Export", recoverable: true);
        }

        var temporary = string.Empty;
        try
        {
            var destination = Path.GetFullPath(destinationPath);
            var parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            {
                return Failure<string>(WriteNativeDocumentPackageErrorCode.InvalidPath,
                    "The selected destination folder is unavailable.", "9to1.Write.Export", recoverable: true);
            }

            temporary = Path.Combine(parent, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
            var documentBytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            if (documentBytes.LongLength > MaximumDocumentBytes)
                throw Fault(WriteNativeDocumentPackageErrorCode.PackageTooLarge, "The document exceeds the .9to1w package size limit.");

            var preserved = ReadPreservedState(document);
            var parts = DecodePreservedParts(preserved);
            var manifest = new PackageManifest
            {
                Format = FormatId,
                Version = CurrentFormatVersion,
                DocumentId = document.Id,
                DocumentSha256 = Sha256(documentBytes),
                Parts = parts.ToDictionary(pair => pair.Key, pair => Sha256(pair.Value), StringComparer.Ordinal),
                AdditionalProperties = preserved.ManifestProperties.Count == 0 ? null : preserved.ManifestProperties
            };
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
            if (manifestBytes.Length > MaximumManifestBytes)
                throw Fault(WriteNativeDocumentPackageErrorCode.PackageTooLarge, "The .9to1w package manifest exceeds its size limit.");

            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await WriteEntryAsync(archive, ManifestEntryName, manifestBytes, cancellationToken).ConfigureAwait(false);
                    await WriteEntryAsync(archive, DocumentEntryName, documentBytes, cancellationToken).ConfigureAwait(false);
                    foreach (var (name, bytes) in parts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        ValidateEntryName(name);
                        if (name.Equals(ManifestEntryName, StringComparison.OrdinalIgnoreCase)
                            || name.Equals(DocumentEntryName, StringComparison.OrdinalIgnoreCase))
                        {
                            throw Fault(WriteNativeDocumentPackageErrorCode.UnsafePackageEntry,
                                "An optional package part uses a reserved entry name.");
                        }
                        await WriteEntryAsync(archive, name, bytes, cancellationToken).ConfigureAwait(false);
                    }
                }
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            return WriteNativeDocumentPackageResult<string>.Success(destination);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PackageFault fault)
        {
            TryDelete(temporary);
            return Failure<string>(fault.Code, fault.Message, "9to1.Write.Export", recoverable: true);
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return Failure<string>(WriteNativeDocumentPackageErrorCode.PermissionDenied,
                "Write cannot save to this location. Check the folder permissions and try again.",
                "9to1.Write.Export", recoverable: true);
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException)
        {
            TryDelete(temporary);
            return Failure<string>(WriteNativeDocumentPackageErrorCode.SaveFailed,
                "Write could not save the .9to1w document. The existing destination was left intact.",
                "9to1.Write.Export", recoverable: true, retryable: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateEntries(ZipArchive archive)
    {
        if (archive.Entries.Count is < 2 or > MaximumEntryCount)
            throw Fault(WriteNativeDocumentPackageErrorCode.InvalidPackage, "The package contains an unsupported number of entries.");

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long declaredBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                continue;
            ValidateEntryName(entry.FullName);
            var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixMode == 0xA000)
                throw Fault(WriteNativeDocumentPackageErrorCode.UnsafePackageEntry, "The package contains a symbolic link.");
            if (!entries.TryAdd(entry.FullName, entry))
                throw Fault(WriteNativeDocumentPackageErrorCode.InvalidPackage, "The package contains duplicate entry names.");

            var entryLimit = entry.FullName.Equals(ManifestEntryName, StringComparison.OrdinalIgnoreCase)
                ? MaximumManifestBytes
                : entry.FullName.Equals(DocumentEntryName, StringComparison.OrdinalIgnoreCase)
                    ? MaximumDocumentBytes
                    : MaximumPackageBytes;
            if (entry.Length < 0 || entry.Length > entryLimit)
                throw Fault(WriteNativeDocumentPackageErrorCode.PackageTooLarge, "A package entry exceeds its size limit.");
            declaredBytes = checked(declaredBytes + entry.Length);
            if (declaredBytes > MaximumPackageBytes)
                throw Fault(WriteNativeDocumentPackageErrorCode.PackageTooLarge, "The package exceeds its total size limit.");
        }

        if (!entries.ContainsKey(ManifestEntryName) || !entries.ContainsKey(DocumentEntryName))
            throw Fault(WriteNativeDocumentPackageErrorCode.InvalidPackage, "The package is missing its manifest or document data.");
        return entries;
    }

    private static async Task<Dictionary<string, byte[]>> ReadOptionalPartsAsync(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        PackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var total = 0L;
        foreach (var entry in entries.Values)
        {
            if (entry.FullName.Equals(ManifestEntryName, StringComparison.OrdinalIgnoreCase)
                || entry.FullName.Equals(DocumentEntryName, StringComparison.OrdinalIgnoreCase))
                continue;

            var bytes = await ReadEntryAsync(entry, MaximumPackageBytes, cancellationToken).ConfigureAwait(false);
            total = checked(total + bytes.LongLength);
            if (total > MaximumPackageBytes - MaximumDocumentBytes)
                throw Fault(WriteNativeDocumentPackageErrorCode.PackageTooLarge, "Optional package parts exceed their size limit.");
            if ((manifest.Parts ?? []).TryGetValue(entry.FullName, out var expectedHash) && !HasSha256(bytes, expectedHash))
                throw Fault(WriteNativeDocumentPackageErrorCode.IntegrityCheckFailed,
                    $"Package part '{entry.FullName}' does not match its integrity record.");
            parts.Add(entry.FullName, bytes);
        }

        foreach (var name in manifest.Parts is null ? Enumerable.Empty<string>() : manifest.Parts.Keys)
        {
            ValidateEntryName(name);
            if (!parts.ContainsKey(name))
                throw Fault(WriteNativeDocumentPackageErrorCode.InvalidPackage,
                    $"The manifest references missing package part '{name}'.");
        }
        return parts;
    }

    private static async Task<byte[]> ReadEntryAsync(
        ZipArchiveEntry entry,
        long limit,
        CancellationToken cancellationToken)
    {
        if (entry.Length > limit)
            throw Fault(WriteNativeDocumentPackageErrorCode.PackageTooLarge, "A package entry exceeds its size limit.");
        await using var source = entry.Open();
        using var output = new MemoryStream((int)Math.Min(entry.Length, 1024 * 1024));
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            if (output.Length + read > limit)
                throw Fault(WriteNativeDocumentPackageErrorCode.PackageTooLarge, "A package entry expands beyond its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        if (output.Length != entry.Length)
            throw Fault(WriteNativeDocumentPackageErrorCode.InvalidPackage, "A package entry length is inconsistent.");
        return output.ToArray();
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string name,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using var destination = entry.Open();
        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumEntryNameLength
            || name[0] is '/' or '\\' || name.Contains('\\') || name.Contains(':')
            || name.Contains('\0') || name.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw Fault(WriteNativeDocumentPackageErrorCode.UnsafePackageEntry,
                "The package contains an unsafe or invalid entry name.");
        }
    }

    private static PreservedPackageState ReadPreservedState(NotesDocument document)
    {
        if (document.Metadata is null
            || !document.Metadata.TryGetValue(WriteNativeDocumentPackageMetadata.PreservedPackageStateKey, out var json)
            || string.IsNullOrWhiteSpace(json))
            return PreservedPackageState.Empty;

        try
        {
            return JsonSerializer.Deserialize<PreservedPackageState>(json, JsonOptions) ?? PreservedPackageState.Empty;
        }
        catch (JsonException)
        {
            throw Fault(WriteNativeDocumentPackageErrorCode.InvalidPackage,
                "Preserved native package data is invalid; export stopped to avoid dropping it.");
        }
    }

    private static Dictionary<string, byte[]> DecodePreservedParts(PreservedPackageState state)
    {
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var (name, encoded) in state.PartsBase64 ?? [])
        {
            ValidateEntryName(name);
            if (name.Equals(ManifestEntryName, StringComparison.OrdinalIgnoreCase)
                || name.Equals(DocumentEntryName, StringComparison.OrdinalIgnoreCase))
                throw Fault(WriteNativeDocumentPackageErrorCode.UnsafePackageEntry, "Preserved package data uses a reserved entry name.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(encoded); }
            catch (FormatException) { throw Fault(WriteNativeDocumentPackageErrorCode.InvalidPackage, "Preserved package data is invalid."); }
            total = checked(total + bytes.LongLength);
            if (total > MaximumPackageBytes - MaximumDocumentBytes)
                throw Fault(WriteNativeDocumentPackageErrorCode.PackageTooLarge, "Preserved package data exceeds its size limit.");
            parts.Add(name, bytes);
        }
        return parts;
    }

    private static bool HasSha256(byte[] bytes, string expected)
    {
        if (expected is null || expected.Length != 64)
            return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(Sha256(bytes)),
                Convert.FromHexString(expected));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static WriteNativeDocumentPackageResult<T> Failure<T>(
        WriteNativeDocumentPackageErrorCode code,
        string message,
        string target,
        bool recoverable,
        bool retryable = false) =>
        WriteNativeDocumentPackageResult<T>.Failure(new WriteNativeDocumentPackageError(
            code, message, target, recoverable, retryable));

    private static PackageFault Fault(WriteNativeDocumentPackageErrorCode code, string message) => new(code, message);

    private static void TryDelete(string path)
    {
        try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class PackageManifest
    {
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; }
        public Guid DocumentId { get; set; }
        public string DocumentSha256 { get; set; } = string.Empty;
        public Dictionary<string, string> Parts { get; set; } = new(StringComparer.Ordinal);

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }

    private sealed record PreservedPackageState(
        Dictionary<string, string> PartsBase64,
        Dictionary<string, JsonElement> ManifestProperties)
    {
        public static PreservedPackageState Empty { get; } = new(
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal));

        public static PreservedPackageState Create(
            Dictionary<string, byte[]> parts,
            Dictionary<string, JsonElement>? manifestProperties) =>
            new(parts.ToDictionary(pair => pair.Key, pair => Convert.ToBase64String(pair.Value), StringComparer.Ordinal),
                manifestProperties ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal));
    }

    private sealed class PackageFault(WriteNativeDocumentPackageErrorCode code, string message) : Exception(message)
    {
        public WriteNativeDocumentPackageErrorCode Code { get; } = code;
    }
}
