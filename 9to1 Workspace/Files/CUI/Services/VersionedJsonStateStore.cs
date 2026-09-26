using System.Text.Json;

namespace HavenOS.Files;

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
	private readonly SemaphoreSlim _gate = new(1, 1);

	public VersionedJsonStateStore(string path, int schemaVersion, Func<TState> createInitialState)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		if (schemaVersion <= 0)
			throw new ArgumentOutOfRangeException(nameof(schemaVersion));
		ArgumentNullException.ThrowIfNull(createInitialState);
		_path = Path.GetFullPath(path);
		_schemaVersion = schemaVersion;
		_createInitialState = createInitialState;
	}

	public async Task<TState> ReadAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			return await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<TState> UpdateAsync(Func<TState, TState> update, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(update);
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			TState current = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
			TState next = update(current) ?? throw new InvalidOperationException("A Files state update cannot return null.");
			await WriteCoreAsync(next, cancellationToken).ConfigureAwait(false);
			return next;
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<TState> ReadCoreAsync(CancellationToken cancellationToken)
	{
		if (!File.Exists(_path))
			return _createInitialState();

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

	private async Task WriteCoreAsync(TState state, CancellationToken cancellationToken)
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
