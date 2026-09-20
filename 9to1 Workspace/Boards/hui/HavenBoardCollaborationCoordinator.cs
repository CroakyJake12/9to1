using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.Hui;

public sealed record HavenBoardAppliedCollaborationBatch(
    Guid MutationId,
    string ActorId,
    HavenBoardSnapshot Before,
    HavenBoardSnapshot After);

/// <summary>
/// Application boundary for inbound collaboration batches.
///
/// It owns no network transport. Every caller-supplied batch is reconstructed through the neutral
/// validator, checked against the open board/version, authorised command-by-command, previewed fully,
/// and only then committed through the session's atomic persist-before-publish transaction.
/// </summary>
public sealed class HavenBoardCollaborationCoordinator
{
    public const int MaxRememberedMutationIds = 4096;

    private readonly IHavenBoardPermissionProvider _permissions;
    private readonly object _gate = new();
    private readonly HashSet<Guid> _appliedMutationIds = [];
    private readonly Queue<Guid> _appliedMutationOrder = new();

    public HavenBoardCollaborationCoordinator(IHavenBoardPermissionProvider? permissions = null)
    {
        _permissions = permissions ?? new DenyAllHavenBoardPermissionProvider();
    }

    public async Task<HavenBoardAppliedCollaborationBatch> ApplyInboundAsync(
        HavenBoardsHuiSession session,
        HavenBoardCollaborationBatch incoming,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(incoming);

        // Positional records are public DTOs, so never trust a caller to have used Create().
        var batch = HavenBoardCollaborationBatch.Create(
            incoming.BoardId,
            incoming.BaseVersion,
            incoming.ActorId,
            incoming.Commands,
            incoming.MutationId);

        if (!string.Equals(batch.BoardId, session.Snapshot.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Collaboration batch targets a different board.");

        lock (_gate)
        {
            if (_appliedMutationIds.Contains(batch.MutationId))
                throw new InvalidOperationException("Collaboration mutation was already applied.");
        }

        if (batch.BaseVersion != session.Snapshot.Version)
        {
            throw new InvalidOperationException(
                $"Collaboration conflict: batch base {batch.BaseVersion} does not match board version {session.Snapshot.Version}.");
        }

        var preview = session.Snapshot;
        foreach (var command in batch.Commands)
        {
            var decision = await _permissions.AuthorizeAsync(
                preview,
                batch.ActorId,
                command,
                cancellationToken).ConfigureAwait(false);
            if (!decision.Allowed)
            {
                throw new UnauthorizedAccessException(
                    $"Collaboration command '{command.GetType().Name}' was denied: {decision.Reason}");
            }

            preview = HavenBoardReducer.Apply(preview, command);
        }

        var before = session.Snapshot;
        var after = await session.ExecuteBatchAsync(
            batch.Commands,
            batch.BaseVersion,
            cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            if (_appliedMutationIds.Add(batch.MutationId))
                _appliedMutationOrder.Enqueue(batch.MutationId);
            while (_appliedMutationIds.Count > MaxRememberedMutationIds && _appliedMutationOrder.Count > 0)
            {
                var oldest = _appliedMutationOrder.Dequeue();
                _appliedMutationIds.Remove(oldest);
            }
        }

        return new HavenBoardAppliedCollaborationBatch(
            batch.MutationId,
            batch.ActorId,
            before,
            after);
    }
}
