using System.Text.Json;

namespace CakeOS.Apps.Boards.Contract;

/// <summary>
/// Local-first board store with same-directory temp writes and previous-version backup recovery.
/// Network access is never required by this store.
/// </summary>
public sealed class JsonFileHavenBoardStore : IHavenBoardStore, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public JsonFileHavenBoardStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("A board storage directory is required.", nameof(rootDirectory));

        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public async Task<HavenBoardSnapshot?> LoadAsync(
        string boardId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateBoardId(boardId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var primary = PrimaryPath(boardId);
            var snapshot = await TryReadAsync(primary, boardId, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
                return snapshot;

            return await TryReadAsync(BackupPath(boardId), boardId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        HavenBoardSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateBoardId(snapshot.Id);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temp = null;
        try
        {
            Directory.CreateDirectory(_rootDirectory);

            var primary = PrimaryPath(snapshot.Id);
            var backup = BackupPath(snapshot.Id);
            temp = primary + "." + Guid.NewGuid().ToString("N") + ".tmp";

            await using (var stream = new FileStream(
                temp,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                options: FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, Json, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            RestrictUnixPermissions(temp);

            // Once the new file is durable, finish the replacement without cancellation. If the process
            // stops after the first move, LoadAsync can still recover the previous version from .bak.
            if (File.Exists(primary))
                File.Move(primary, backup, overwrite: true);

            try
            {
                File.Move(temp, primary, overwrite: true);
                temp = null;
            }
            catch
            {
                if (!File.Exists(primary) && File.Exists(backup))
                    File.Move(backup, primary, overwrite: true);
                throw;
            }
        }
        finally
        {
            if (temp is not null)
            {
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch (IOException)
                {
                    // Preserve the original save failure; stale temp files are ignored by LoadAsync.
                }
                catch (UnauthorizedAccessException)
                {
                    // Preserve the original save failure; stale temp files are ignored by LoadAsync.
                }
            }

            _gate.Release();
        }
    }

    private async Task<HavenBoardSnapshot?> TryReadAsync(
        string path,
        string expectedBoardId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            var snapshot = await JsonSerializer.DeserializeAsync<HavenBoardSnapshot>(
                stream,
                Json,
                cancellationToken).ConfigureAwait(false);

            if (snapshot is null || !string.Equals(snapshot.Id, expectedBoardId, StringComparison.Ordinal))
                return null;

            return snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private string PrimaryPath(string boardId) => Path.Combine(_rootDirectory, boardId + ".json");
    private string BackupPath(string boardId) => Path.Combine(_rootDirectory, boardId + ".json.bak");

    private static void ValidateBoardId(string boardId)
    {
        if (string.IsNullOrWhiteSpace(boardId) || boardId.Length > 128)
            throw new ArgumentException("Board ID must contain 1 to 128 safe characters.", nameof(boardId));

        if (boardId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Board ID may contain only ASCII letters, digits, '-' and '_'.", nameof(boardId));
    }

    private static void RestrictUnixPermissions(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }
}
