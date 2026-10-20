using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Home.Core;

/// <summary>Reads the packaged Dulche model store (the same content-addressed manifest/blob store
/// owned by the packaged broker and model manager). It mirrors the package verification contract:
/// a model is Verified only when its manifest is valid, its blob is a regular file of the recorded
/// size, and its bytes re-hash to the manifest's SHA-256. It never fabricates a model or a hash,
/// and it never claims an unverified or non-hex record is verified.</summary>
public sealed class HomeDulchePackagedModelEvidenceSource : IHomeDulcheModelEvidenceSource
{
    public const string DefaultModelRootRelative = "haven/models";
    private readonly string _modelRoot;
    private readonly string? _preferredModelId;

    public HomeDulchePackagedModelEvidenceSource(string? modelRoot = null, string? preferredModelId = null)
    {
        _modelRoot = modelRoot ?? DefaultModelRoot();
        _preferredModelId = preferredModelId;
    }

    public async ValueTask<HomeDulcheModelEvidence?> FindEligibleAsync(CancellationToken cancellationToken)
    {
        var manifestsDir = Path.Combine(_modelRoot, "manifests");
        if (!Directory.Exists(manifestsDir)) return null;

        foreach (var manifestPath in Directory.EnumerateFiles(manifestsDir, "*.json").Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            if (manifest is null) continue;
            if (_preferredModelId is not null &&
                !string.Equals(manifest.ModelId, _preferredModelId, StringComparison.Ordinal)) continue;
            var evidence = await VerifyBlobAsync(manifest, cancellationToken).ConfigureAwait(false);
            if (evidence is not null) return evidence;
        }
        return null;
    }

    private static string DefaultModelRoot()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var baseDir = string.IsNullOrWhiteSpace(dataHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : dataHome;
        return Path.Combine(baseDir, DefaultModelRootRelative);
    }

    private sealed record Manifest(string ModelId, string DisplayName, string Sha256, long Size, string Blob, string License, string Source);

    private static async Task<Manifest?> ReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            using var document = await JsonDocument.ParseAsync(input, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var display = root.TryGetProperty("displayName", out var displayProp) ? displayProp.GetString() : null;
            var sha = root.TryGetProperty("sha256", out var shaProp) ? shaProp.GetString() : null;
            var blob = root.TryGetProperty("blob", out var blobProp) ? blobProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(display)
                || !HomeDulcheHash.IsValidSha256Hex(sha) || string.IsNullOrWhiteSpace(blob)) return null;
            if (!root.TryGetProperty("size", out var sizeProp) || !sizeProp.TryGetInt64(out var size) || size < 8) return null;
            var license = root.TryGetProperty("license", out var licenseProp) ? licenseProp.GetString() : null;
            var source = root.TryGetProperty("source", out var sourceProp) ? sourceProp.GetString() : null;
            return new Manifest(id, display, sha!, size, blob!, license ?? "", source ?? "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async ValueTask<HomeDulcheModelEvidence?> VerifyBlobAsync(Manifest manifest, CancellationToken cancellationToken)
    {
        var blobPath = BlobPath(manifest.Sha256, manifest.Blob);
        if (blobPath is null) return null;
        var info = new FileInfo(blobPath);
        if (!info.Exists || info.Length != manifest.Size) return null;
        var actual = await ComputeSha256Async(blobPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actual, manifest.Sha256, StringComparison.Ordinal)) return null;
        return new HomeDulcheModelEvidence(manifest.ModelId, "llamacpp", manifest.Sha256, manifest.Size, DateTimeOffset.UtcNow);
    }

    /// <summary>Resolves the manifest's canonical content-address blob path under the configured
    /// model store and refuses anything that escapes it, mirroring the packaged broker's
    /// requirement. The blob path is canonical (first-two-hex/sha256.gguf), so a manifest that
    /// names any other path is refused outright.</summary>
    private string? BlobPath(string sha256, string blob)
    {
        var blobRoot = Path.GetFullPath(Path.Combine(_modelRoot, "blobs", "sha256"));
        var expected = Path.Combine(sha256[..2], sha256 + ".gguf").Replace(Path.DirectorySeparatorChar, '/');
        if (!string.Equals(blob, expected, StringComparison.Ordinal)) return null;
        var candidate = Path.GetFullPath(Path.Combine(blobRoot, blob.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = blobRoot + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, StringComparison.Ordinal) ? candidate : null;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }
}
