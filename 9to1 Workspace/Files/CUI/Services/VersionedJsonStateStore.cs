using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace HavenOS.Files;

internal static class FilesStatePathLocks
{
	private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
		OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

	public static SemaphoreSlim Get(string fullPath) => Gates.GetOrAdd(fullPath, static _ => new SemaphoreSlim(1, 1));
}

/// <summary>
/// Small crash-conscious state store for Files-owned durable metadata. The envelope is versioned;
/// unknown versions are rejected instead of being interpreted as the current shape.
/// </summary>
public sealed class VersionedJsonStateStore<TState> where TState : class
{
	private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
	private readonly string _path;
	private readonly int _schemaVersion;
	private readonly Func<TState> _createInitialState;
	private readonly SemaphoreSlim _gate;

	public VersionedJsonStateStore(string path, int schemaVersion, Func<TState> createInitialState)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		if (schemaVersion <= 0)
			throw new ArgumentOutOfRangeException(nameof(schemaVersion));
		ArgumentNullException.ThrowIfNull(createInitialState);
		_path = Path.GetFullPath(path);
		_gate = FilesStatePathLocks.Get(_path);
		_schemaVersion = schemaVersion;
		_createInitialState = createInitialState;
	}

	public async Task<TState> ReadAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
            await using var processLease = await AcquireProcessLeaseAsync(cancellationToken).ConfigureAwait(false);
			return await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

    /// <summary>Reads only an already persisted state under the same process lease; never invokes the creation factory.</summary>
    public async Task<TState> ReadExistingAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) throw new FileNotFoundException("The existing Files state is unavailable.", _path);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var processLease = await AcquireProcessLeaseAsync(cancellationToken).ConfigureAwait(false);
            return await ReadCoreAsync(cancellationToken, requireExisting: true).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    // Owner-only existing snapshot lease. The caller already owns its Files metadata transaction;
    // never call provider, resource authorization or Home from this lease's held section.
    internal async Task<ExistingReadLease> AcquireExistingReadLeaseAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        FileStream? processLease = null;
        try
        {
            processLease = await AcquireProcessLeaseAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = await ReadCoreAsync(cancellationToken, requireExisting: true).ConfigureAwait(false);
            return new ExistingReadLease(snapshot, processLease, _gate);
        }
        catch
        {
            try { if (processLease is not null) await processLease.DisposeAsync().ConfigureAwait(false); }
            finally { _gate.Release(); }
            throw;
        }
    }

    internal sealed class ExistingReadLease : IAsyncDisposable
    {
        internal TState Snapshot { get; }
        private FileStream? _processLease;
        private readonly SemaphoreSlim _gate;
        internal ExistingReadLease(TState snapshot, FileStream processLease, SemaphoreSlim gate)
        { Snapshot = snapshot; _processLease = processLease; _gate = gate; }
        public async ValueTask DisposeAsync()
        {
            var processLease = Interlocked.Exchange(ref _processLease, null);
            if (processLease is null) return;
            try { await processLease.DisposeAsync().ConfigureAwait(false); }
            finally { _gate.Release(); }
        }
    }

	public Task<TState> UpdateAsync(Func<TState, TState> update, CancellationToken cancellationToken = default) =>
        UpdateAsync(update, null, cancellationToken);

    /// <summary>The optional authority validator runs under the metadata lease and must not reenter this store.</summary>
    public async Task<TState> UpdateAsync(Func<TState, TState> update,
        Func<CancellationToken, ValueTask>? validateCommitAuthority, CancellationToken cancellationToken)
    {
		ArgumentNullException.ThrowIfNull(update);
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
            await using var processLease = await AcquireProcessLeaseAsync(cancellationToken).ConfigureAwait(false);
			TState current = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
            if (validateCommitAuthority is not null) await validateCommitAuthority(cancellationToken).ConfigureAwait(false);
			TState next = update(current) ?? throw new InvalidOperationException("A Files state update cannot return null.");
			await WriteCoreAsync(next, cancellationToken, validateCommitAuthority).ConfigureAwait(false);
			return next;
		}
		finally
		{
			_gate.Release();
		}
	}

    private async Task<FileStream> AcquireProcessLeaseAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Files state has no parent directory.");
        Directory.CreateDirectory(directory);
        // This sidecar is persistent. Never unlink it: recreating a lock path can split contenders
        // across different inodes while an earlier process still owns the original lease.
        var lockPath = _path + ".lock";
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None,
                    BufferSize = 1, Options = FileOptions.Asynchronous
                };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                return new FileStream(lockPath, options);
            }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(30))
            { await Task.Delay(25, cancellationToken).ConfigureAwait(false); }
            catch (IOException error)
            { throw new IOException("Files metadata is locked or unavailable in another process.", error); }
        }
    }

	private async Task<TState> ReadCoreAsync(CancellationToken cancellationToken, bool requireExisting = false)
	{
		if (!File.Exists(_path))
        {
            if (requireExisting) throw new FileNotFoundException("The existing Files state is unavailable.", _path);
			return _createInitialState();
        }

		await using FileStream stream = new(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
		using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
		JsonElement root = document.RootElement;
		if (!root.TryGetProperty("schemaVersion", out JsonElement versionElement) ||
			!versionElement.TryGetInt32(out int version))
			throw new InvalidDataException("Files state is missing its schema version.");
		if (version != _schemaVersion)
			throw new InvalidDataException($"Files state schema {version} is not supported; expected {_schemaVersion}.");
		if (!root.TryGetProperty("state", out JsonElement stateElement))
			throw new InvalidDataException("Files state envelope is missing its state payload.");
		return stateElement.Deserialize<TState>(SerializerOptions)
			?? throw new InvalidDataException("Files state payload is empty or invalid.");
	}

	private async Task WriteCoreAsync(TState state, CancellationToken cancellationToken, Func<CancellationToken, ValueTask>? validateCommitAuthority)
	{
		string directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Files state path has no parent directory.");
		Directory.CreateDirectory(directory);
		string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
		try
		{
			await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
			{
				using (Utf8JsonWriter writer = new(stream))
				{
					writer.WriteStartObject();
					writer.WriteNumber("schemaVersion", _schemaVersion);
					writer.WritePropertyName("state");
					JsonSerializer.Serialize(writer, state, SerializerOptions);
					writer.WriteEndObject();
				}
				await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
				stream.Flush(flushToDisk: true);
			}
            if (validateCommitAuthority is not null) await validateCommitAuthority(cancellationToken).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();
			File.Move(temporaryPath, _path, overwrite: true);
		}
		finally
		{
			if (File.Exists(temporaryPath))
				File.Delete(temporaryPath);
		}
	}
}
