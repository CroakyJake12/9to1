namespace HavenOS.Files;

public sealed record FilesSyncCursorState(FilesChangeCursor? AcknowledgedCursor);

/// <summary>Persists the last fully applied Files change-feed cursor so reconnects resume from a durable point.</summary>
public sealed class FilesSyncCursorStore
{
	private readonly VersionedJsonStateStore<FilesSyncCursorState> _store;

	public FilesSyncCursorStore(string path) =>
		_store = new VersionedJsonStateStore<FilesSyncCursorState>(path, 1, static () => new FilesSyncCursorState(null));

	public async Task<FilesChangeCursor?> GetAsync(CancellationToken cancellationToken = default) =>
		(await _store.ReadAsync(cancellationToken).ConfigureAwait(false)).AcknowledgedCursor;

	public async Task<FilesChangeCursor> AcknowledgeAsync(FilesChangeCursor cursor, CancellationToken cancellationToken = default)
	{
		if (cursor.Revision < 0)
			throw new ArgumentOutOfRangeException(nameof(cursor));

		FilesChangeCursor? acknowledged = null;
		await _store.UpdateAsync(state =>
		{
			if (state.AcknowledgedCursor is { } current && cursor.Revision < current.Revision)
				throw new InvalidOperationException($"Files change cursor cannot move backwards from {current.Revision} to {cursor.Revision}.");
			acknowledged = state.AcknowledgedCursor is { } existing && existing.Revision > cursor.Revision ? existing : cursor;
			return new FilesSyncCursorState(acknowledged);
		}, cancellationToken).ConfigureAwait(false);
		return acknowledged!.Value;
	}
}
