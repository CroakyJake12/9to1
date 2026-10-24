namespace HavenOS.Files;

public sealed class FilesFolderPresentationState
{
	public Dictionary<string, FilesFolderPresentation> Presentations { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Persists Files-owned folder colour overrides by stable FolderID rather than by display path.</summary>
public sealed class FilesFolderPresentationStore
{
	private readonly VersionedJsonStateStore<FilesFolderPresentationState> _store;

	public FilesFolderPresentationStore(string path) =>
		_store = new VersionedJsonStateStore<FilesFolderPresentationState>(path, 1, static () => new FilesFolderPresentationState());

	public async Task<FilesFolderPresentation?> GetAsync(HostedItemId folderId, CancellationToken cancellationToken = default)
	{
		ValidateFolderId(folderId);
		FilesFolderPresentationState state;
		try { state = await _store.ReadExistingAsync(cancellationToken).ConfigureAwait(false); }
		catch (FileNotFoundException) { return null; }
		return state.Presentations.TryGetValue(folderId.ToString(), out FilesFolderPresentation? presentation) ? presentation : null;
	}

	public async Task<FilesFolderPresentation?> SetAsync(
		HostedItemId folderId,
		string? colorHex,
		DateTimeOffset updatedAt,
		string updatedByPrincipalId,
		Func<CancellationToken, ValueTask>? validateCommitAuthority = null,
		CancellationToken cancellationToken = default)
	{
		ValidateFolderId(folderId);
		ArgumentException.ThrowIfNullOrWhiteSpace(updatedByPrincipalId);
		string? normalizedColor = colorHex is null ? null : FilesFolderPresentation.ValidateColor(colorHex);
		FilesFolderPresentation? result = null;
		await _store.UpdateAsync(state =>
		{
			var values = new Dictionary<string, FilesFolderPresentation>(state.Presentations, StringComparer.Ordinal);
			string key = folderId.ToString();
			if (normalizedColor is null)
			{
				values.Remove(key);
			}
			else
			{
				result = new FilesFolderPresentation(folderId, normalizedColor, updatedAt, updatedByPrincipalId);
				values[key] = result;
			}
			return new FilesFolderPresentationState { Presentations = values };
		}, validateCommitAuthority, cancellationToken).ConfigureAwait(false);
		return result;
	}

	private static void ValidateFolderId(HostedItemId folderId)
	{
		if (folderId.Value == Guid.Empty)
			throw new ArgumentException("Folder colour requires a stable non-empty FolderID.", nameof(folderId));
	}
}
