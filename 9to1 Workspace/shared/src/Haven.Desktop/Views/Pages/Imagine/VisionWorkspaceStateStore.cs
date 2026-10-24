using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Haven.Desktop.Views.Pages.Imagine;

internal sealed record VisionWorkspaceState(
    string? SourcePath,
    string Question,
    string Response,
    string Model,
    string? AnalysisKey)
{
    public static VisionWorkspaceState Empty { get; } = new(null, string.Empty, string.Empty, string.Empty, null);
}

internal sealed class VisionWorkspaceStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private readonly string _assetDirectory;

    public VisionWorkspaceStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Haven",
            "vision-workspace.json");
        _assetDirectory = Path.Combine(Path.GetDirectoryName(_path)!, "Vision");
    }

    public async Task<VisionWorkspaceState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return VisionWorkspaceState.Empty;
            try
            {
                await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                return await JsonSerializer.DeserializeAsync<VisionWorkspaceState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                       ?? VisionWorkspaceState.Empty;
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                return VisionWorkspaceState.Empty;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(VisionWorkspaceState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("The Vision state path has no directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = _path + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, _path, true);
        }
        finally { _gate.Release(); }
    }

    public async Task<string> PersistSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("The Vision source image is unavailable.", sourcePath);
        Directory.CreateDirectory(_assetDirectory);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension)) extension = ".img";
        var temporaryPath = Path.Combine(_assetDirectory, ".vision-source-" + Guid.NewGuid().ToString("N") + ".tmp");
        Exception? importFailure = null;
        try
        {
            string digest;
            await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }

            // Source files are immutable, content-addressed assets. A later import must
            // never replace bytes still referenced by an earlier workspace/session.
            var target = Path.Combine(_assetDirectory, digest + extension);
            try { File.Move(temporaryPath, target, overwrite: false); }
            catch (IOException) when (File.Exists(target))
            {
                // Concurrent imports of identical content converge on the same asset.
                // Do not trust a pre-existing path without validating its content hash.
                var existingDigest = await ComputeFileHashAsync(target, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(existingDigest, digest, StringComparison.Ordinal))
                    throw new IOException("The Vision content-addressed source exists with unexpected contents.");
            }
            return target;
        }
        catch (Exception failure)
        {
            importFailure = failure;
            throw;
        }
        finally
        {
            CompleteImportCleanup(() => File.Delete(temporaryPath), importFailure);
        }
    }

    internal static void CompleteImportCleanup(Action cleanup, Exception? importFailure)
    {
        try { cleanup(); }
        catch (FileNotFoundException) { }
        catch (Exception cleanupFailure) when (importFailure is not null)
        {
            throw new AggregateException("Vision source import and temporary-file cleanup both failed.",
                importFailure, cleanupFailure);
        }
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static async Task<string> BuildAnalysisKeyAsync(string imagePath, string model, string prompt, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0) hash.AppendData(buffer, 0, read);
        hash.AppendData(Encoding.UTF8.GetBytes("\nmodel:" + model + "\nprompt:" + prompt));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

internal sealed record VisionHandoff(string SourcePath, string Response, string Model);
