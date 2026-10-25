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
		_store = new VersionedJsonStateStore<FilesOperationJournalState>(path, 3, static () => new FilesOperationJournalState());

	public async Task<FilesOperation> EnqueueAsync(FilesOperation operation, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(operation);
		if (operation.State is not FilesOperationState.Pending)
			throw new ArgumentException("New journal operations must begin Pending.", nameof(operation));
		if (operation.Id.Value == Guid.Empty || operation.ItemId.Value == Guid.Empty ||
			string.IsNullOrWhiteSpace(operation.ActorId) || string.IsNullOrWhiteSpace(operation.Operation))
			throw new ArgumentException("A journal operation requires stable operation/item IDs, actor and operation identities.", nameof(operation));

		FilesOperation? accepted = null;
		await _store.UpdateAsync(state =>
		{
			long nextSequence = GetNextSequence(state);
			string key = operation.Id.ToString();
			if (state.Operations.TryGetValue(key, out FilesOperation? existing))
			{
				if (!IsSameRequest(existing, operation))
					throw new InvalidDataException($"Operation ID {key} was replayed with different arguments.");
				accepted = existing;
				return state;
			}

			accepted = operation with { Sequence = nextSequence };
			return new FilesOperationJournalState
			{
				Operations = new Dictionary<string, FilesOperation>(state.Operations, StringComparer.Ordinal) { [key] = accepted },
				NextSequence = checked(nextSequence + 1),
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
			long nextSequence = GetNextSequence(state);
			string key = operationId.ToString();
			if (!state.Operations.TryGetValue(key, out FilesOperation? current))
				throw new KeyNotFoundException($"Files operation {key} was not found.");
			if (!CanTransition(current.State, nextState))
				throw new InvalidOperationException($"Files operation cannot transition from {current.State} to {nextState}.");
			if (updatedAt < current.UpdatedAt)
				throw new InvalidOperationException("Files operation timestamps cannot move backwards.");
			if (nextState is FilesOperationState.Rejected or FilesOperationState.Conflict && error is null)
				throw new ArgumentException("Rejected and conflicted Files operations require a structured error.", nameof(error));
			if (nextState is FilesOperationState.Committed && error is not null)
				throw new ArgumentException("A committed Files operation cannot retain an error.", nameof(error));

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
				NextSequence = nextSequence,
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
		first.InversePayload == second.InversePayload &&
		first.PermissionInheritancePolicy == second.PermissionInheritancePolicy &&
		first.Payload == second.Payload;

	/// <summary>
	/// Recovers a stale allocation cursor from persisted operations without changing any
	/// operation's identity or sequence. Corrupt historical assignments fail closed: they
	/// cannot be repaired by renumbering work that may already have been acknowledged.
	/// </summary>
	private static long GetNextSequence(FilesOperationJournalState state)
	{
		if (state.NextSequence <= 0)
			throw new InvalidDataException("Files operation journal has an invalid next sequence.");

		var sequences = new HashSet<long>();
		long maxSequence = 0;
		foreach (FilesOperation operation in state.Operations.Values)
		{
			if (operation.Sequence <= 0)
				throw new InvalidDataException($"Files operation {operation.Id} has a missing or regressed sequence; journal writes are blocked to preserve its history.");
			if (!sequences.Add(operation.Sequence))
				throw new InvalidDataException($"Files operation journal contains duplicate sequence {operation.Sequence}; journal writes are blocked to preserve operation history.");
			maxSequence = Math.Max(maxSequence, operation.Sequence);
		}

		long firstUnused = checked(maxSequence + 1);
		return Math.Max(state.NextSequence, firstUnused);
	}

	private static bool CanTransition(FilesOperationState current, FilesOperationState next) => current switch
	{
		FilesOperationState.Pending => next is FilesOperationState.Running or FilesOperationState.Committed or FilesOperationState.Rejected or FilesOperationState.Conflict or FilesOperationState.Cancelled,
		FilesOperationState.Running => next is FilesOperationState.Committed or FilesOperationState.Rejected or FilesOperationState.Conflict or FilesOperationState.Pending or FilesOperationState.Cancelled,
		_ => false,
	};
}
