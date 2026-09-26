namespace HavenOS.Files;

public sealed class FilesOperationJournalState
{
	public Dictionary<string, FilesOperation> Operations { get; init; } = new(StringComparer.Ordinal);
	public long NextSequence { get; init; } = 1;
}

/// <summary>Durable operation journal used to recover and replay offline structural mutations safely.</summary>
public sealed class FilesOperationJournal
{
	private readonly VersionedJsonStateStore<FilesOperationJournalState> _store;

	public FilesOperationJournal(string path) =>
		_store = new VersionedJsonStateStore<FilesOperationJournalState>(path, 2, static () => new FilesOperationJournalState());

	public async Task<FilesOperation> EnqueueAsync(FilesOperation operation, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(operation);
		if (operation.State is not FilesOperationState.Pending)
			throw new ArgumentException("New journal operations must begin Pending.", nameof(operation));
		if (string.IsNullOrWhiteSpace(operation.ActorId) || string.IsNullOrWhiteSpace(operation.Operation))
			throw new ArgumentException("A journal operation requires actor and operation identities.", nameof(operation));

		FilesOperation? accepted = null;
		await _store.UpdateAsync(state =>
		{
			string key = operation.Id.ToString();
			if (state.Operations.TryGetValue(key, out FilesOperation? existing))
			{
				if (!IsSameRequest(existing, operation))
					throw new InvalidDataException($"Operation ID {key} was replayed with different arguments.");
				accepted = existing;
				return state;
			}

			accepted = operation with { Sequence = state.NextSequence };
			return new FilesOperationJournalState
			{
				Operations = new Dictionary<string, FilesOperation>(state.Operations, StringComparer.Ordinal) { [key] = accepted },
				NextSequence = checked(state.NextSequence + 1),
			};
		}, cancellationToken).ConfigureAwait(false);
		return accepted!;
	}

	public async Task<FilesOperation> TransitionAsync(
		FilesOperationId operationId,
		FilesOperationState nextState,
		DateTimeOffset updatedAt,
		FilesRevisionId? resultRevisionId = null,
		FilesError? error = null,
		CancellationToken cancellationToken = default)
	{
		FilesOperation? updated = null;
		await _store.UpdateAsync(state =>
		{
			string key = operationId.ToString();
			if (!state.Operations.TryGetValue(key, out FilesOperation? current))
				throw new KeyNotFoundException($"Files operation {key} was not found.");
			if (!CanTransition(current.State, nextState))
				throw new InvalidOperationException($"Files operation cannot transition from {current.State} to {nextState}.");

			updated = current with
			{
				State = nextState,
				UpdatedAt = updatedAt,
				ResultRevisionId = resultRevisionId ?? current.ResultRevisionId,
				Error = error,
			};
			return new FilesOperationJournalState
			{
				Operations = new Dictionary<string, FilesOperation>(state.Operations, StringComparer.Ordinal) { [key] = updated },
			};
		}, cancellationToken).ConfigureAwait(false);
		return updated!;
	}

	public async Task<IReadOnlyList<FilesOperation>> ListPendingAsync(CancellationToken cancellationToken = default)
	{
		FilesOperationJournalState state = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
		return state.Operations.Values
			.Where(static operation => operation.State is FilesOperationState.Pending or FilesOperationState.Running)
			.OrderBy(static operation => operation.Sequence)
			.ToArray();
	}

	private static bool IsSameRequest(FilesOperation first, FilesOperation second) =>
		first.Id == second.Id &&
		first.ActorId == second.ActorId &&
		first.ItemId == second.ItemId &&
		first.SourceParentId == second.SourceParentId &&
		first.DestinationParentId == second.DestinationParentId &&
		first.Operation == second.Operation &&
		first.BaseRevisionId == second.BaseRevisionId &&
		first.InversePayload == second.InversePayload;

	private static bool CanTransition(FilesOperationState current, FilesOperationState next) => current switch
	{
		FilesOperationState.Pending => next is FilesOperationState.Running or FilesOperationState.Committed or FilesOperationState.Rejected or FilesOperationState.Conflict or FilesOperationState.Cancelled,
		FilesOperationState.Running => next is FilesOperationState.Committed or FilesOperationState.Rejected or FilesOperationState.Conflict or FilesOperationState.Pending or FilesOperationState.Cancelled,
		_ => false,
	};
}
