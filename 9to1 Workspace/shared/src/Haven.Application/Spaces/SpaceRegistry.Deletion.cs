namespace Haven.Application;

public enum SpaceDeletionStage { AwaitingConversationDetach = 0, Complete = 1 }

/// <summary>Durable progress in the canonical Spaces registry, not a second Space or conversation store.</summary>
public sealed record SpaceDeletionOperation(Guid OperationId, Guid SpaceId, long ArchivedRevision,
    DateTimeOffset RequestedAt, SpaceDeletionStage Stage, DateTimeOffset? CompletedAt = null);

public sealed partial class SpaceRegistry
{
    public async Task<SpaceDeletionOperation?> ReadDeletionAsync(Guid operationId, CancellationToken token = default)
    {
        var state = await _settings.GetAsync<SpaceRegistryState>(SettingsKey, token).ConfigureAwait(false);
        if (state is null) return null;
        if (state.Version is < 2 or > CurrentVersion) throw new InvalidDataException("The Space registry schema is unavailable.");
        ValidateDeletionState(state);
        return state.Deletions?.SingleOrDefault(item => item.OperationId == operationId);
    }

    public async Task<IReadOnlyList<SpaceDeletionOperation>> ReadPendingDeletionsAsync(CancellationToken token = default)
    {
        var state = await _settings.GetAsync<SpaceRegistryState>(SettingsKey, token).ConfigureAwait(false);
        if (state is null) return [];
        if (state.Version is < 2 or > CurrentVersion) throw new InvalidDataException("The Space registry schema is unavailable.");
        ValidateDeletionState(state);
        return state.Deletions?.Where(item => item.Stage == SpaceDeletionStage.AwaitingConversationDetach).ToArray() ?? [];
    }

    /// <summary>Atomically archives the exact current Space and records its resumable detach operation.
    /// SQL work follows separately; this method never claims the two stores committed atomically.</summary>
    public Task<SpaceDeletionOperation> BeginDeletionAsync(Guid spaceId, long expectedRevision, Guid operationId,
        CancellationToken token = default)
    {
        RequireGuardedDeletion();
        if (operationId == Guid.Empty || spaceId == Guid.Empty || expectedRevision < 1) throw new ArgumentException("Deletion requires exact canonical identities and revision.");
        return MutateAsync(state =>
        {
            var operations = state.Deletions ?? [];
            if (operations.SingleOrDefault(item => item.OperationId == operationId) is { } replay)
            {
                if (replay.SpaceId != spaceId) throw new InvalidOperationException("This deletion operation belongs to another Space.");
                return (state, replay);
            }
            if (operations.Any(item => item.SpaceId == spaceId && item.Stage == SpaceDeletionStage.AwaitingConversationDetach))
                throw new InvalidOperationException("Finish the existing Space deletion before starting another.");
            var existing = FindRequired(state.Spaces, spaceId);
            if (existing.IsBuiltIn) throw new InvalidOperationException("Built-in Spaces cannot be deleted.");
            if (existing.Revision != expectedRevision) throw new SpaceRevisionConflictException(spaceId, expectedRevision, existing.Revision);
            var observedNow = _clock();
            var now = existing.UpdatedAt > observedNow ? existing.UpdatedAt : observedNow;
            var archived = existing with { IsArchived = true, Revision = checked(existing.Revision + 1), UpdatedAt = now };
            var operation = new SpaceDeletionOperation(operationId, spaceId, archived.Revision, now, SpaceDeletionStage.AwaitingConversationDetach);
            return (state with { Spaces = state.Spaces.Select(item => item.Id == spaceId ? archived : item).ToArray(),
                Deletions = [.. operations, operation] }, operation);
        }, token, allowPendingDeletion: true);
    }

    /// <summary>Only the owning deletion workflow calls this after its canonical SQL detach has been
    /// acknowledged. A failed final settings admission leaves the operation pending and retryable.</summary>
    public Task<SpaceDeletionOperation> CompleteDeletionAsync(Guid operationId, long expectedArchivedRevision,
        CancellationToken token = default)
    {
        RequireGuardedDeletion();
        return MutateAsync(state =>
        {
            var operation = (state.Deletions ?? []).SingleOrDefault(item => item.OperationId == operationId)
                ?? throw new KeyNotFoundException("The Space deletion operation is unavailable.");
            if (operation.ArchivedRevision != expectedArchivedRevision)
                throw new SpaceRevisionConflictException(operation.SpaceId, expectedArchivedRevision, operation.ArchivedRevision);
            if (operation.Stage == SpaceDeletionStage.Complete) return (state, operation);
            var space = FindRequired(state.Spaces, operation.SpaceId);
            if (!space.IsArchived || space.Revision != expectedArchivedRevision)
                throw new SpaceRevisionConflictException(space.Id, expectedArchivedRevision, space.Revision);
            var now = _clock();
            var complete = operation with { Stage = SpaceDeletionStage.Complete, CompletedAt = now < operation.RequestedAt ? operation.RequestedAt : now };
            return (state with { Deletions = state.Deletions!.Select(item => item.OperationId == operationId ? complete : item).ToArray() }, complete);
        }, token, allowPendingDeletion: true);
    }

    public async Task<bool> ClearDeletedSelectionAsync(Guid operationId, CancellationToken token = default)
    {
        RequireGuardedDeletion();
        var admission = await CaptureAdmissionAsync(token).ConfigureAwait(false);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var snapshot = await _settings.ExportAsync(token).ConfigureAwait(false);
            snapshot.Settings.TryGetValue(SettingsKey, out var registryJson);
            if (registryJson is null) throw new KeyNotFoundException("The Space deletion operation is unavailable.");
            var state = System.Text.Json.JsonSerializer.Deserialize<SpaceRegistryState>(registryJson)
                ?? throw new InvalidDataException("The Space registry is invalid.");
            if (state.Version != CurrentVersion) throw new InvalidDataException("Deletion requires the current registry schema.");
            ValidateDeletionState(state);
            var operation = (state.Deletions ?? []).SingleOrDefault(item => item.OperationId == operationId)
                ?? throw new KeyNotFoundException("The Space deletion operation is unavailable.");
            snapshot.Settings.TryGetValue(CurrentSpaceSettingsKey, out var selectedJson);
            if (selectedJson is null || !Guid.TryParse(System.Text.Json.JsonSerializer.Deserialize<string>(selectedJson), out var selected) ||
                selected != operation.SpaceId) return false;
            if (await TryCommitAsync(CurrentSpaceSettingsKey, selectedJson, null,
                new Dictionary<string, string?> { [SettingsKey] = registryJson }, admission, token).ConfigureAwait(false)) return true;
        }
        throw new InvalidOperationException("The current Space selection kept changing; retry deletion recovery.");
    }

    private void RequireGuardedDeletion()
    {
        if (_captureAdmission is null || _settings is not IVersionedSettingsGuardedCompareExchange)
            throw new UnauthorizedAccessException("Resumable deletion requires the actual guarded Spaces owner.");
    }

    private static void ValidateDeletionState(SpaceRegistryState state)
    {
        var operations = state.Deletions ?? [];
        if (state.Spaces is null || operations.Count > 10000) throw new InvalidDataException("Space deletion progress exceeds the supported registry bounds.");
        if (state.Version < 4 && state.Spaces.Any(space => space.RevisionBank is not null))
            throw new InvalidDataException("Revision Bank metadata requires Spaces registry schema 4.");
        if (state.Version < 3 && operations.Count != 0)
            throw new InvalidDataException("Deletion progress requires Spaces registry schema 3.");
        var ids = new HashSet<Guid>(); var pending = new HashSet<Guid>();
        foreach (var operation in operations)
        {
            if (operation is null || operation.OperationId == Guid.Empty || operation.SpaceId == Guid.Empty || operation.ArchivedRevision < 1 ||
                !ids.Add(operation.OperationId) || !Enum.IsDefined(operation.Stage))
                throw new InvalidDataException("Space deletion progress has invalid or duplicate identities.");
            var space = state.Spaces.SingleOrDefault(item => item.Id == operation.SpaceId);
            if (space is null || space.IsBuiltIn) throw new InvalidDataException("Space deletion progress has no canonical user Space.");
            if (operation.Stage == SpaceDeletionStage.AwaitingConversationDetach &&
                (!pending.Add(operation.SpaceId) || !space.IsArchived || space.Revision != operation.ArchivedRevision || operation.CompletedAt is not null))
                throw new InvalidDataException("Pending Space deletion no longer matches its archived revision.");
            if (operation.Stage == SpaceDeletionStage.Complete && operation.CompletedAt is null)
                throw new InvalidDataException("Completed Space deletion is missing its acknowledgement.");
        }
    }

    private static void EnsurePendingDeletionsUnchanged(SpaceRegistryState before, SpaceRegistryState after)
    {
        foreach (var operation in before.Deletions ?? [])
        {
            if (operation.Stage != SpaceDeletionStage.AwaitingConversationDetach) continue;
            if (!ReferenceEquals(before.Spaces.Single(item => item.Id == operation.SpaceId),
                after.Spaces.SingleOrDefault(item => item.Id == operation.SpaceId)))
                throw new InvalidOperationException("Finish the pending conversation detach before editing or restoring this Space.");
        }
    }
}
