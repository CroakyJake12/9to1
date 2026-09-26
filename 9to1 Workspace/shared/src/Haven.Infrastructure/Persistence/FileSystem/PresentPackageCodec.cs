using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Reads and writes the versioned, full-fidelity .9to1p presentation package.</summary>
internal static class PresentPackageCodec
{
    private const int PackageVersion = 1;
    private const long MaximumDocumentBytes = 64L * 1024 * 1024;
    private const long MaximumAssetBytes = 256L * 1024 * 1024;
    private const long MaximumTotalBytes = 768L * 1024 * 1024;
    private const int MaximumAssets = 4096;
    private const string ManifestPath = "manifest.json";
    private const string DocumentPath = "presentation.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task ExportAsync(
        PresentDocument document,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        document.Normalize();

        var assets = CollectAssets(document);
        if (assets.Count > MaximumAssets)
            throw new InvalidDataException($"A .9to1p package cannot contain more than {MaximumAssets} assets.");

        var fullDestination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestination)
            ?? throw new InvalidDataException("The native package destination has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullDestination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var records = new List<AssetRecord>(assets.Count);
            long totalAssetBytes = 0;
            await using (var output = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var asset in assets)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var info = new FileInfo(asset.Path);
                        if (!info.Exists)
                            throw new FileNotFoundException("A presentation asset referenced by the document is unavailable; the native package was not written.", asset.Path);
                        if (info.Length > MaximumAssetBytes || checked(totalAssetBytes + info.Length) > MaximumTotalBytes)
                            throw new InvalidDataException("Presentation assets exceed the .9to1p package size limit.");

                        totalAssetBytes += info.Length;
                        var extension = SafeExtension(info.Extension);
                        var entryPath = $"assets/{asset.Key}{extension}";
                        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
                        await using var entryStream = entry.Open();
                        await using var source = new FileStream(
                            asset.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        var copiedBytes = await CopyAndHashAsync(source, entryStream, sha, cancellationToken).ConfigureAwait(false);
                        records.Add(new AssetRecord(asset.Key, entryPath, copiedBytes, Convert.ToHexString(sha.GetHashAndReset())));
                    }

                    var manifest = new PackageManifest(PackageVersion, PresentDocument.CurrentSchemaVersion, DocumentPath, records);
                    await WriteJsonEntryAsync(archive, ManifestPath, manifest, cancellationToken).ConfigureAwait(false);
                    await WriteJsonEntryAsync(archive, DocumentPath, document, cancellationToken).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            ValidateExportedPackage(temporaryPath, assets.Count, totalAssetBytes);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullDestination, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public static async Task<PresentDocument> ImportAsync(
        string sourcePath,
        string dataDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The native 9to1 presentation was not found.", fullPath);

        var documentsRoot = Path.Combine(Path.GetFullPath(dataDirectory), "Present", "Documents");
        Directory.CreateDirectory(documentsRoot);
        var stagingDirectory = Path.Combine(documentsRoot, $".import-{Guid.NewGuid():N}");
        string? committedDirectory = null;
        try
        {
            await using var file = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
            var manifest = await ReadJsonEntryAsync<PackageManifest>(archive, ManifestPath, MaximumDocumentBytes, cancellationToken)
                .ConfigureAwait(false);
            ValidateManifest(archive, manifest);
            var document = await ReadJsonEntryAsync<PresentDocument>(archive, manifest.DocumentEntry, MaximumDocumentBytes, cancellationToken)
                .ConfigureAwait(false);
            ValidateDocument(document, manifest.SchemaVersion);

            var sourceId = document.Id;
            document.Id = Guid.NewGuid();
            document.Version = 0;
            document.CreatedAt = DateTimeOffset.UtcNow;
            document.UpdatedAt = document.CreatedAt;
            document.Recovery = new PresentRecoveryState();
            document.Metadata["importedFromPresentationId"] = sourceId.ToString("D");
            var requiredAssets = CollectAssetReferences(document);
            var recordMap = manifest.Assets.ToDictionary(asset => asset.ReferenceKey, StringComparer.Ordinal);
            foreach (var required in requiredAssets)
            {
                if (!recordMap.ContainsKey(required.Key))
                    throw new InvalidDataException("The native package is missing an embedded asset referenced by the presentation.");
            }
            if (manifest.Assets.Any(asset => !requiredAssets.ContainsKey(asset.ReferenceKey)))
                throw new InvalidDataException("The native package contains an asset that is not referenced by the presentation.");

            if (manifest.Assets.Count > 0)
            {
                Directory.CreateDirectory(stagingDirectory);
                foreach (var asset in manifest.Assets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = archive.GetEntry(asset.EntryPath)!;
                    var destination = Path.Combine(stagingDirectory, Path.GetFileName(asset.EntryPath));
                    await using var source = entry.Open();
                    await using var target = new FileStream(
                        destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                        FileOptions.Asynchronous | FileOptions.WriteThrough);
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var copied = await CopyAndHashAsync(source, target, sha, cancellationToken).ConfigureAwait(false);
                    var actualHash = Convert.ToHexString(sha.GetHashAndReset());
                    if (copied != asset.Length || !CryptographicOperations.FixedTimeEquals(
                            Convert.FromHexString(actualHash), Convert.FromHexString(asset.Sha256)))
                        throw new InvalidDataException("An embedded native presentation asset failed its integrity check.");
                    await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                var finalAssetDirectory = Path.Combine(documentsRoot, document.Id.ToString("D"), "assets");
                var finalDocumentDirectory = Path.GetDirectoryName(finalAssetDirectory)!;
                Directory.CreateDirectory(finalDocumentDirectory);
                Directory.Move(stagingDirectory, finalAssetDirectory);
                committedDirectory = finalDocumentDirectory;
                RewriteAssetReferences(document, recordMap, finalAssetDirectory);
            }

            document.Normalize();
            return document;
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            if (committedDirectory is not null)
                TryDeleteDirectory(committedDirectory);
            throw;
        }
    }

    private static List<AssetReference> CollectAssets(PresentDocument document)
    {
        var references = CollectAssetReferences(document);
        var assets = new List<AssetReference>(references.Count);
        foreach (var (key, reference) in references)
        {
            var path = ResolveLocalPath(reference);
            if (path is null)
                throw new InvalidDataException($"The .9to1p format requires every referenced image/media asset to be a local file. Asset reference {key} is not a local file.");
            assets.Add(new AssetReference(key, path));
        }
        return assets;
    }

    private static Dictionary<string, string> CollectAssetReferences(PresentDocument document)
    {
        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slide in document.Slides)
        {
            if (!string.IsNullOrWhiteSpace(slide.Background.AssetId))
                AddReference(slide.Background.AssetId);
            foreach (var element in slide.Elements)
            {
                if (element.Kind is PresentElementKind.Image or PresentElementKind.Media && !string.IsNullOrWhiteSpace(element.AssetId))
                    AddReference(element.AssetId);
            }
        }
        return references;

        void AddReference(string value) => references.TryAdd(ReferenceKey(value), value);
    }

    private static void RewriteAssetReferences(
        PresentDocument document,
        IReadOnlyDictionary<string, AssetRecord> records,
        string assetDirectory)
    {
        foreach (var slide in document.Slides)
        {
            if (!string.IsNullOrWhiteSpace(slide.Background.AssetId))
                slide.Background.AssetId = Resolve(slide.Background.AssetId);
            foreach (var element in slide.Elements)
                if (element.Kind is PresentElementKind.Image or PresentElementKind.Media && !string.IsNullOrWhiteSpace(element.AssetId))
                    element.AssetId = Resolve(element.AssetId);
        }

        string Resolve(string value)
        {
            var record = records[ReferenceKey(value)];
            return Path.Combine(assetDirectory, Path.GetFileName(record.EntryPath));
        }
    }

    private static string? ResolveLocalPath(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
            return Path.GetFullPath(uri.LocalPath);
        if (Path.IsPathFullyQualified(value))
            return Path.GetFullPath(value);
        return null;
    }

    private static string ReferenceKey(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static string SafeExtension(string extension)
    {
        if (extension.Length is < 2 or > 12 || extension[0] != '.' || extension.Skip(1).Any(ch => !char.IsAsciiLetterOrDigit(ch)))
            return ".bin";
        return extension.ToLowerInvariant();
    }

    private static void ValidateManifest(ZipArchive archive, PackageManifest manifest)
    {
        if (manifest.PackageVersion != PackageVersion)
            throw new InvalidDataException($"Unsupported .9to1p package version {manifest.PackageVersion}.");
        if (manifest.SchemaVersion <= 0 || manifest.SchemaVersion > PresentDocument.CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported presentation schema version {manifest.SchemaVersion}.");
        if (!string.Equals(manifest.DocumentEntry, DocumentPath, StringComparison.Ordinal))
            throw new InvalidDataException("The native package manifest has an unsupported document entry path.");
        if (manifest.Assets.Count > MaximumAssets)
            throw new InvalidDataException("The native package contains too many assets.");

        var seenEntries = new HashSet<string>(StringComparer.Ordinal) { ManifestPath, DocumentPath };
        long total = 0;
        foreach (var asset in manifest.Assets)
        {
            if (asset.ReferenceKey.Length != 64 || asset.ReferenceKey.Any(ch => !Uri.IsHexDigit(ch)) ||
                asset.Sha256.Length != 64 || asset.Sha256.Any(ch => !Uri.IsHexDigit(ch)))
                throw new InvalidDataException("The native package contains an invalid asset identity or digest.");
            if (asset.Length < 0 || asset.Length > MaximumAssetBytes || checked(total + asset.Length) > MaximumTotalBytes)
                throw new InvalidDataException("The native package assets exceed the supported size limits.");
            total += asset.Length;
            if (!asset.EntryPath.StartsWith("assets/", StringComparison.Ordinal) ||
                asset.EntryPath.Contains("..", StringComparison.Ordinal) ||
                asset.EntryPath.Contains('\\') || !seenEntries.Add(asset.EntryPath))
                throw new InvalidDataException("The native package contains an unsafe or duplicate asset entry path.");
            var entry = archive.GetEntry(asset.EntryPath)
                ?? throw new InvalidDataException("The native package is missing an asset listed in its manifest.");
            if (entry.Length != asset.Length)
                throw new InvalidDataException("A native package asset length does not match its manifest.");
        }

        foreach (var entry in archive.Entries)
        {
            if (!seenEntries.Contains(entry.FullName))
                throw new InvalidDataException($"The native package contains an undeclared entry: {entry.FullName}");
        }
    }

    private static void ValidateDocument(PresentDocument document, int manifestSchemaVersion)
    {
        if (document.SchemaVersion != manifestSchemaVersion)
            throw new InvalidDataException("The native package manifest and presentation schema versions do not match.");
        if (document.SchemaVersion > PresentDocument.CurrentSchemaVersion)
            throw new InvalidDataException($"This Present build cannot read schema version {document.SchemaVersion}.");
        if (document.SchemaVersion <= 0 || document.Id == Guid.Empty || document.Slides is null || document.Slides.Count == 0)
            throw new InvalidDataException("The native package does not contain a valid presentation document.");
    }

    private static void ValidateExportedPackage(string path, int expectedAssets, long expectedAssetBytes)
    {
        using var input = File.OpenRead(path);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = archive.GetEntry(ManifestPath) ?? throw new InvalidDataException("The native package is missing its manifest.");
        using var manifestStream = manifestEntry.Open();
        var manifest = JsonSerializer.Deserialize<PackageManifest>(manifestStream, JsonOptions)
            ?? throw new InvalidDataException("The native package manifest could not be verified.");
        ValidateManifest(archive, manifest);
        if (manifest.Assets.Count != expectedAssets || manifest.Assets.Sum(asset => asset.Length) != expectedAssetBytes)
            throw new InvalidDataException("The native package asset set changed during verification.");
        var documentEntry = archive.GetEntry(DocumentPath) ?? throw new InvalidDataException("The native package is missing its presentation.");
        if (documentEntry.Length > MaximumDocumentBytes)
            throw new InvalidDataException("The presentation document exceeds the native package size limit.");
        using var documentStream = documentEntry.Open();
        var document = JsonSerializer.Deserialize<PresentDocument>(documentStream, JsonOptions)
            ?? throw new InvalidDataException("The native package presentation could not be verified.");
        ValidateDocument(document, manifest.SchemaVersion);
    }

    private static async Task<T> ReadJsonEntryAsync<T>(
        ZipArchive archive,
        string path,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"The native package is missing {path}.");
        if (entry.Length <= 0 || entry.Length > maxBytes)
            throw new InvalidDataException($"The native package entry {path} exceeds its size limit.");
        await using var stream = entry.Open();
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return value ?? throw new InvalidDataException($"The native package entry {path} is empty or invalid.");
    }

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> CopyAndHashAsync(
        Stream source,
        Stream destination,
        IncrementalHash hash,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) return total;
            total = checked(total + read);
            if (total > MaximumAssetBytes) throw new InvalidDataException("A presentation asset exceeds the native package size limit.");
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record AssetReference(string Key, string Path);
    private sealed record AssetRecord(string ReferenceKey, string EntryPath, long Length, string Sha256);
    private sealed record PackageManifest(int PackageVersion, int SchemaVersion, string DocumentEntry, List<AssetRecord> Assets);
}
