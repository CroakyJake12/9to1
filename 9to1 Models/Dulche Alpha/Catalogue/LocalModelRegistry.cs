using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NineToOne.Dulche.Den;

/// <summary>Device-local installation evidence. Paths and installed state never enter portable Den records.</summary>
public sealed class LocalModelRegistry(string denRoot, string deviceId, long maximumModelWeightBytes = 8L * 1024 * 1024 * 1024)
{
    private readonly string _root = Path.Combine(Path.GetFullPath(denRoot), "local", "models", Validate(deviceId));
    private readonly string _deviceId = Validate(deviceId);
    private readonly long _maximumModelWeightBytes = maximumModelWeightBytes >= 0 ? maximumModelWeightBytes : throw new ArgumentOutOfRangeException(nameof(maximumModelWeightBytes));

    public async Task<LocalModelInstallation> RegisterAsync(string modelId, string absoluteModelPath,
        string artifactSha256, CancellationToken cancellationToken = default)
    {
        Validate(modelId);
        if (!Path.IsPathFullyQualified(absoluteModelPath)) throw new DenException(DenErrorCode.InvalidRecord, "A local model installation requires an absolute device path.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(artifactSha256, "^[A-Fa-f0-9]{64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new DenException(DenErrorCode.InvalidRecord, "The model artifact hash must be SHA-256.");
        var fullPath = Path.GetFullPath(absoluteModelPath);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) throw new DenException(DenErrorCode.NotFound, "The local model artifact does not exist.", recoverable: true);
        var isDirectory = Directory.Exists(fullPath);
        var (actual, length) = await HashArtifactAsync(fullPath, cancellationToken);
        if (length > _maximumModelWeightBytes) throw new DenException(DenErrorCode.RetentionBlocked, "The local model exceeds this device's configured model-weight quota.", recoverable: true);
        if (!string.Equals(actual, artifactSha256, StringComparison.OrdinalIgnoreCase)) throw new DenException(DenErrorCode.InvalidRecord, "The installed model hash does not match the model catalogue.");
        var installation = new LocalModelInstallation(modelId, deviceId, fullPath, actual, length, DateTime.UtcNow, isDirectory);
        var path = RecordPath(modelId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(output, installation, DenJson.Options, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Flush(true);
        }
        File.Move(temp, path, true);
        return installation;
    }

    public async Task<LocalModelInstallation?> GetVerifiedAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var path = RecordPath(modelId);
        if (!File.Exists(path)) return null;
        LocalModelInstallation installation;
        try
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            installation = await JsonSerializer.DeserializeAsync<LocalModelInstallation>(input, DenJson.Options, cancellationToken)
                ?? throw new DenException(DenErrorCode.InvalidRecord, "A local installation record is empty.");
        }
        catch (JsonException ex) { throw new DenException(DenErrorCode.InvalidRecord, $"A local installation record is invalid: {ex.Message}", recoverable: true); }
        if (installation.ModelId != modelId || installation.DeviceId != _deviceId || !installation.Enabled) return null;
        if (!File.Exists(installation.AbsolutePath) && !Directory.Exists(installation.AbsolutePath)) return null;
        var (currentHash, length) = await HashArtifactAsync(installation.AbsolutePath, cancellationToken);
        return length == installation.Length && string.Equals(currentHash, installation.Sha256, StringComparison.OrdinalIgnoreCase) ? installation with { LastVerifiedUtc = DateTime.UtcNow } : null;
    }

    public async Task RemoveAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var path = RecordPath(modelId);
        if (!File.Exists(path)) return;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var record = await JsonSerializer.DeserializeAsync<LocalModelInstallation>(input, DenJson.Options, cancellationToken);
        if (record is null) throw new DenException(DenErrorCode.InvalidRecord, "A local installation record is empty.");
        await WriteRecordAsync(path, record with { Enabled = false }, cancellationToken);
    }

    public async Task RestoreAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var path = RecordPath(modelId);
        if (!File.Exists(path)) throw new DenException(DenErrorCode.NotFound, "The local model registration does not exist.");
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var record = await JsonSerializer.DeserializeAsync<LocalModelInstallation>(input, DenJson.Options, cancellationToken)
            ?? throw new DenException(DenErrorCode.InvalidRecord, "A local installation record is empty.");
        await WriteRecordAsync(path, record with { Enabled = true }, cancellationToken);
    }

    public void PurgeRegistration(string modelId, bool explicitlyConfirmed)
    {
        if (!explicitlyConfirmed) throw new DenException(DenErrorCode.PurgeConfirmationRequired, "Permanent local model-registration purge requires explicit confirmation.");
        var path = RecordPath(modelId);
        if (File.Exists(path)) File.Delete(path);
    }

    public async Task<long> GetEnabledWeightUsageAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root)) return 0;
        long total = 0;
        foreach (var path in Directory.EnumerateFiles(_root, "*.json").Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var record = await JsonSerializer.DeserializeAsync<LocalModelInstallation>(input, DenJson.Options, cancellationToken)
                ?? throw new DenException(DenErrorCode.InvalidRecord, "A local model installation record is empty.");
            if (record.Enabled && await GetVerifiedAsync(record.ModelId, cancellationToken) is not null) total = checked(total + record.Length);
        }
        return total;
    }

    private async Task WriteRecordAsync(string path, LocalModelInstallation record, CancellationToken cancellationToken)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(output, record, DenJson.Options, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Flush(true);
        }
        File.Move(temp, path, true);
    }

    private static async Task<(string Hash, long Length)> HashArtifactAsync(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return (Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)), info.Length);
        }
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        var root = Path.GetFullPath(path);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(file => Path.GetRelativePath(root, file), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new DenException(DenErrorCode.InvalidRecord, "A model package cannot contain symbolic links or reparse points.");
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var nameBytes = Encoding.UTF8.GetBytes(relative);
            aggregate.AppendData(nameBytes);
            aggregate.AppendData([0]);
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var fileHash = await SHA256.HashDataAsync(input, cancellationToken);
            aggregate.AppendData(fileHash);
            length = checked(length + info.Length);
        }
        return (Convert.ToHexString(aggregate.GetHashAndReset()), length);
    }

    private string RecordPath(string modelId) => Path.Combine(_root, Validate(modelId) + ".json");
    private static string Validate(string value)
    {
        DenStore.ValidateSegment(value, "model or device ID");
        return value;
    }
}

public sealed record LocalModelInstallation(string ModelId, string DeviceId, string AbsolutePath,
    string Sha256, long Length, DateTime LastVerifiedUtc, bool IsDirectory, bool Enabled = true);
