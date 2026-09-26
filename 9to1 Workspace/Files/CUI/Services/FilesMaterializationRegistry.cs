using System.Security.Cryptography;

namespace HavenOS.Files;

public sealed class FilesMaterializationRegistryState
{
	public Dictionary<string, FilesMaterializedFile> ItemsByPath { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Persists explicit links between local materialisations and canonical hosted item/revision identities.</summary>
public sealed class FilesMaterializationRegistry
{
	private readonly string _syncRoot;
	private readonly VersionedJsonStateStore<FilesMaterializationRegistryState> _store;

	public FilesMaterializationRegistry(string syncRoot, string statePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(syncRoot);
		_syncRoot = Path.GetFullPath(syncRoot);
		_store = new VersionedJsonStateStore<FilesMaterializationRegistryState>(statePath, 1, static () => new FilesMaterializationRegistryState());
	}

	public Task<FilesMaterializationProof> RegisterValidatedAsync(
		string localPath,
		FilesMaterializationProof proof,
		SyncAvailability availability,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(proof);
		if (string.IsNullOrWhiteSpace(proof.ContentHash) || proof.SizeBytes < 0 || proof.ItemId.Value == Guid.Empty || proof.RemoteRevisionId.Value == Guid.Empty)
			throw new ArgumentException("Materialisation requires a stable item/revision ID, validated content hash and non-negative size.", nameof(proof));
		if (availability is not (SyncAvailability.AvailableOffline or SyncAvailability.AlwaysAvailable))
			throw new ArgumentOutOfRangeException(nameof(availability), "Local bytes become available only after validation and must have an offline-retention policy.");
		return RegisterCoreAsync(localPath, proof, availability, cancellationToken);
	}

	private async Task<FilesMaterializationProof> RegisterCoreAsync(
		string localPath,
		FilesMaterializationProof proof,
		SyncAvailability availability,
		CancellationToken cancellationToken)
	{
		string fullPath = ValidateLocalPath(localPath);
		string key = PathKey(fullPath);
		await using FileStream content = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
		if (content.Length != proof.SizeBytes)
			throw new InvalidDataException("Materialised content size does not match the validated transfer result.");
		string actualHash = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
		if (!string.Equals(actualHash, proof.ContentHash, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("Materialised content hash does not match the validated transfer result.");
		content.Position = 0;
		await _store.UpdateAsync(state =>
		{
			FilesMaterializedFile? existing = state.ItemsByPath.Values.FirstOrDefault(item => item.ItemId == proof.ItemId);
			if (existing is not null && !string.Equals(PathKey(existing.LocalPath), key, StringComparison.Ordinal))
				throw new InvalidOperationException("This hosted item is already materialised at another local path. Move its mapping explicitly to preserve identity.");
			if (state.ItemsByPath.TryGetValue(key, out FilesMaterializedFile? pathEntry) && pathEntry.ItemId != proof.ItemId)
				throw new IOException("The target local path is already bound to a different canonical Files item.");

			var materialization = new FilesMaterializedFile(fullPath, proof.ItemId, proof.RemoteRevisionId,
				proof.RemoteRevisionId, null, proof.ContentHash, proof.SizeBytes, availability, proof.VerifiedAt,
				availability is SyncAvailability.AlwaysAvailable);
			return new FilesMaterializationRegistryState
			{
				ItemsByPath = new Dictionary<string, FilesMaterializedFile>(state.ItemsByPath, StringComparer.Ordinal) { [key] = materialization },
			};
		}, cancellationToken).ConfigureAwait(false);
		return proof;
	}

	public async Task<FilesMaterializedFile?> GetByPathAsync(string localPath, CancellationToken cancellationToken = default)
	{
		string fullPath = ValidateLocalPath(localPath);
		FilesMaterializationRegistryState state = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
		if (!state.ItemsByPath.TryGetValue(PathKey(fullPath), out FilesMaterializedFile? item))
			return null;
		ValidateLocalPath(item.LocalPath);
		if (!string.Equals(PathKey(item.LocalPath), PathKey(fullPath), StringComparison.Ordinal))
			throw new InvalidDataException("Files materialisation path index does not match its stored canonical path.");
		return item;
	}

	public async Task<FilesMaterializedFile?> GetByItemIdAsync(HostedItemId itemId, CancellationToken cancellationToken = default)
	{
		FilesMaterializationRegistryState state = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
		FilesMaterializedFile? item = state.ItemsByPath.Values.SingleOrDefault(item => item.ItemId == itemId);
		if (item is not null)
			ValidateLocalPath(item.LocalPath);
		return item;
	}

	public async Task<FilesMaterializedFile> MoveMappingAsync(
		HostedItemId itemId,
		string destinationPath,
		CancellationToken cancellationToken = default)
	{
		string fullDestination = ValidateLocalPath(destinationPath);
		string destinationKey = PathKey(fullDestination);
		FilesMaterializedFile? moved = null;
		await _store.UpdateAsync(state =>
		{
			KeyValuePair<string, FilesMaterializedFile> pair = state.ItemsByPath.SingleOrDefault(entry => entry.Value.ItemId == itemId);
			if (pair.Value is null)
				throw new KeyNotFoundException($"Hosted Files item {itemId} has no local materialisation.");
			ValidateLocalPath(pair.Value.LocalPath);
			if (state.ItemsByPath.TryGetValue(destinationKey, out FilesMaterializedFile? destination) && destination.ItemId != itemId)
				throw new IOException("The destination local path is already bound to a different canonical Files item.");

			moved = pair.Value with { LocalPath = fullDestination };
			var items = new Dictionary<string, FilesMaterializedFile>(state.ItemsByPath, StringComparer.Ordinal);
			items.Remove(pair.Key);
			items[destinationKey] = moved;
			return new FilesMaterializationRegistryState { ItemsByPath = items };
		}, cancellationToken).ConfigureAwait(false);
		return moved!;
	}

	public async Task<FilesResult<FilesMaterializedFile>> MarkLocalChangesAsync(
		HostedItemId itemId,
		FilesRevisionId localRevisionId,
		string contentHash,
		long sizeBytes,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(contentHash) || sizeBytes < 0 || localRevisionId.Value == Guid.Empty)
			return Failure<FilesMaterializedFile>(FilesErrorCode.InvalidState, "A local revision requires a revision ID, content hash and non-negative size.", itemId);
		return await UpdateContentStateAsync(itemId, contentHash, sizeBytes, item => item with
		{
			LocalRevisionId = localRevisionId,
			ContentHash = contentHash,
			SizeBytes = sizeBytes,
			State = SyncAvailability.LocalChanges,
		}, cancellationToken).ConfigureAwait(false);
	}

	public async Task<FilesResult<FilesMaterializedFile>> MarkSyncedAsync(
		HostedItemId itemId,
		FilesRevisionId durableRemoteRevisionId,
		string contentHash,
		long sizeBytes,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(contentHash) || sizeBytes < 0 || durableRemoteRevisionId.Value == Guid.Empty)
			return Failure<FilesMaterializedFile>(FilesErrorCode.InvalidState, "A synced revision requires a durable revision ID, content hash and non-negative size.", itemId);
		return await UpdateContentStateAsync(itemId, contentHash, sizeBytes, item =>
		{
			SyncAvailability retainedPolicy = item.IsAlwaysAvailable ? SyncAvailability.AlwaysAvailable : SyncAvailability.AvailableOffline;
			return item with
			{
				BaseRemoteRevisionId = durableRemoteRevisionId,
				CurrentRemoteRevisionId = durableRemoteRevisionId,
				LocalRevisionId = null,
				ContentHash = contentHash,
				SizeBytes = sizeBytes,
				State = retainedPolicy,
			};
		}, cancellationToken).ConfigureAwait(false);
	}

	public async Task<FilesResult<FilesMaterializedFile>> CheckEvictionAsync(
		HostedItemId itemId,
		FilesRevisionId verifiedDurableCloudRevisionId,
		CancellationToken cancellationToken = default)
	{
		FilesMaterializedFile? item = await GetByItemIdAsync(itemId, cancellationToken).ConfigureAwait(false);
		if (item is null)
			return Failure<FilesMaterializedFile>(FilesErrorCode.ItemNotFound, "The item has no local materialisation.", itemId);
		if (item.IsAlwaysAvailable)
			return Failure<FilesMaterializedFile>(FilesErrorCode.InvalidState, "Always Available content cannot be removed by normal cache cleanup.", itemId);
		if (item.State is SyncAvailability.LocalChanges or SyncAvailability.Uploading or SyncAvailability.Conflict)
			return Failure<FilesMaterializedFile>(FilesErrorCode.SyncConflict, "Local content is not safely evictable while changes are pending or conflicted.", itemId);
		if (item.CurrentRemoteRevisionId != verifiedDurableCloudRevisionId || item.BaseRemoteRevisionId != verifiedDurableCloudRevisionId)
			return Failure<FilesMaterializedFile>(FilesErrorCode.RevisionConflict, "The durable cloud revision does not match the local base/current revision.", itemId);
		return FilesResult<FilesMaterializedFile>.Success(item);
	}

	private FilesMaterializationRegistryState UpdateItem(FilesMaterializationRegistryState state, HostedItemId itemId, Func<FilesMaterializedFile, FilesMaterializedFile> update)
	{
		KeyValuePair<string, FilesMaterializedFile> pair = state.ItemsByPath.SingleOrDefault(entry => entry.Value.ItemId == itemId);
		if (pair.Value is null)
			return state;
		ValidateLocalPath(pair.Value.LocalPath);
		return new FilesMaterializationRegistryState
		{
			ItemsByPath = new Dictionary<string, FilesMaterializedFile>(state.ItemsByPath, StringComparer.Ordinal) { [pair.Key] = update(pair.Value) },
		};
	}

	private async Task<FilesResult<FilesMaterializedFile>> UpdateContentStateAsync(
		HostedItemId itemId,
		string contentHash,
		long sizeBytes,
		Func<FilesMaterializedFile, FilesMaterializedFile> update,
		CancellationToken cancellationToken)
	{
		FilesMaterializedFile? current = await GetByItemIdAsync(itemId, cancellationToken).ConfigureAwait(false);
		if (current is null)
			return Failure<FilesMaterializedFile>(FilesErrorCode.ItemNotFound, "The item has no local materialisation.", itemId);

		try
		{
			await using FileStream content = new(current.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
			if (content.Length != sizeBytes)
				return Failure<FilesMaterializedFile>(FilesErrorCode.IntegrityFailed, "Local content size does not match the revision metadata.", itemId);
			string actualHash = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
			if (!string.Equals(actualHash, contentHash, StringComparison.OrdinalIgnoreCase))
				return Failure<FilesMaterializedFile>(FilesErrorCode.IntegrityFailed, "Local content hash does not match the revision metadata.", itemId);

			FilesMaterializedFile? updated = null;
			await _store.UpdateAsync(state => UpdateItem(state, itemId, item =>
			{
				if (!string.Equals(PathKey(item.LocalPath), PathKey(current.LocalPath), StringComparison.Ordinal))
					throw new InvalidOperationException("The materialisation moved while its content was being verified.");
				updated = update(item);
				return updated;
			}), cancellationToken).ConfigureAwait(false);
			return updated is null
				? Failure<FilesMaterializedFile>(FilesErrorCode.ItemNotFound, "The item has no local materialisation.", itemId)
				: FilesResult<FilesMaterializedFile>.Success(updated);
		}
		catch (IOException exception)
		{
			return FilesResult<FilesMaterializedFile>.Failure(new FilesError(FilesErrorCode.StorageUnavailable, exception.Message, "Files.Materialization", itemId.ToString(), true, true));
		}
		catch (UnauthorizedAccessException exception)
		{
			return FilesResult<FilesMaterializedFile>.Failure(new FilesError(FilesErrorCode.StorageUnavailable, exception.Message, "Files.Materialization", itemId.ToString(), true, true));
		}
		catch (InvalidOperationException exception)
		{
			return FilesResult<FilesMaterializedFile>.Failure(new FilesError(FilesErrorCode.RevisionConflict, exception.Message, "Files.Materialization", itemId.ToString(), true, false));
		}
	}

	private string ValidateLocalPath(string localPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
		string fullPath = Path.GetFullPath(localPath);
		string relative = Path.GetRelativePath(_syncRoot, fullPath);
		StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, comparison))
			throw new UnauthorizedAccessException("The local materialisation path is outside the approved sync root.");

		string current = _syncRoot;
		string[] segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
		foreach (string segment in segments)
		{
			current = Path.Combine(current, segment);
			if ((File.Exists(current) || Directory.Exists(current)) &&
				(File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
				throw new UnauthorizedAccessException("Local materialisations cannot traverse symbolic links or reparse points.");
		}
		return fullPath;
	}

	private static string PathKey(string path) => OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;

	private static FilesResult<T> Failure<T>(FilesErrorCode code, string message, HostedItemId itemId) =>
		FilesResult<T>.Failure(new FilesError(code, message, "Files.Materialization", itemId.ToString(), code is FilesErrorCode.RevisionConflict or FilesErrorCode.SyncConflict, false));
}
