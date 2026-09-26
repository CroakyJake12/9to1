using System.Collections.Concurrent;
using System.Text.Json;

namespace HavenOS.Home;

/// <summary>Structured persistence failure surfaced by Home feature providers.</summary>
public sealed class HomeFeatureStoreException(string code, string message, Exception? innerException = null)
    : IOException(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// Stores one versioned Home feature state document. Updates are written to a sibling temporary
/// file and atomically replace the current document; a previous valid document is retained as a
/// backup. A corrupt or unknown-version document is rejected without being overwritten.
/// </summary>
internal sealed class HomeFeatureJsonStore<TState>(string path, int schemaVersion = 1)
    where TState : class
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PathLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
    };

    private readonly string _path = Path.GetFullPath(path);
    private readonly SemaphoreSlim _pathLock = PathLocks.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));

    public async Task<TState> LoadAsync(Func<TState> createEmpty, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(createEmpty);
        await _pathLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return createEmpty();

            try
            {
                await using var stream = new FileStream(
                    _path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var envelope = await JsonSerializer.DeserializeAsync<Envelope<TState>>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                if (envelope is null || envelope.State is null)
                    throw new HomeFeatureStoreException("HOME_STATE_INVALID", "The saved Home feature state is empty or invalid.");
                if (envelope.SchemaVersion != schemaVersion)
                    throw new HomeFeatureStoreException(
                        "HOME_STATE_VERSION_UNSUPPORTED",
                        $"Saved Home feature state uses schema version {envelope.SchemaVersion}; version {schemaVersion} is supported.");
                return envelope.State;
            }
            catch (HomeFeatureStoreException)
            {
                throw;
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                throw new HomeFeatureStoreException(
                    "HOME_STATE_CORRUPT",
                    "Saved Home feature state could not be read. The original file and its backup were preserved.",
                    exception);
            }
        }
        finally
        {
            _pathLock.Release();
        }
    }

    public async Task SaveAsync(TState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _pathLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new HomeFeatureStoreException("HOME_STATE_PATH_INVALID", "Home feature state path has no parent directory.");
            Directory.CreateDirectory(directory);
            temporaryPath = _path + ".tmp." + Guid.NewGuid().ToString("N");

            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, new Envelope<TState>(schemaVersion, state), JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            var backupPath = _path + ".bak";
            if (File.Exists(_path))
                File.Replace(temporaryPath, _path, backupPath, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, _path);
            temporaryPath = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new HomeFeatureStoreException(
                "HOME_STATE_WRITE_FAILED",
                "Home could not save feature state. The previous saved state was preserved.",
                exception);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _pathLock.Release();
        }
    }

    private sealed record Envelope<T>(int SchemaVersion, T State);
}
