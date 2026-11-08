namespace Haven.Core;

/// <summary>
/// Applies deterministic, duplicate-safe ordering rules before feature-specific merge logic is considered.
/// </summary>
public sealed class MeshSyncEngine
{
    public MeshSyncDecision Evaluate(
        MeshResourceSnapshot? current,
        MeshSyncMutation incoming,
        IReadOnlySet<Guid> appliedOperationIds)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(appliedOperationIds);
        Validate(incoming);

        if (appliedOperationIds.Contains(incoming.OperationId))
            return new(MeshSyncDecisionKind.Duplicate, "This synchronization operation was already applied.", current, incoming);

        if (incoming.Kind == MeshSyncOperationKind.Replace)
        {
            if (current is null)
                return new(MeshSyncDecisionKind.Apply, "This device held no copy of the resource, so the complete incoming state was adopted as the whole resource.", null, incoming);
            if (string.Equals(current.ContentHash, incoming.ContentHash, StringComparison.OrdinalIgnoreCase))
                return new(MeshSyncDecisionKind.Duplicate, "The incoming content already matches the local resource.", current, incoming);
            if (incoming.Revision <= current.Revision)
                return new(MeshSyncDecisionKind.IgnoreStale, "A newer local revision already exists; the older whole-state payload was kept only as synchronization evidence.", current, incoming);
            if (incoming.BaseRevision < current.Revision)
                return new(
                    MeshSyncDecisionKind.Conflict,
                    "Both devices changed this resource from different revisions, so a visible conflict keeps both copies rather than letting one device win on arrival order.",
                    current,
                    incoming);
            return new(MeshSyncDecisionKind.Apply, "The incoming whole-state revision follows the local revision directly.", current, incoming);
        }

        if (current is null)
        {
            return incoming.BaseRevision <= 0
                ? new(MeshSyncDecisionKind.Apply, "The resource does not exist locally and the mutation starts from an empty base.", null, incoming)
                : new(MeshSyncDecisionKind.Conflict, "The incoming mutation depends on a local base revision that is not present.", null, incoming);
        }

        if (!string.Equals(current.ResourceType, incoming.ResourceType, StringComparison.Ordinal))
            return new(MeshSyncDecisionKind.Conflict, "The resource type does not match the current local resource.", current, incoming);

        if (string.Equals(current.ContentHash, incoming.ContentHash, StringComparison.OrdinalIgnoreCase))
            return new(MeshSyncDecisionKind.Duplicate, "The incoming content already matches the local resource.", current, incoming);

        if (incoming.Revision <= current.Revision)
            return new(MeshSyncDecisionKind.IgnoreStale, "A newer local revision already exists; the older mutation was retained only as synchronization evidence.", current, incoming);

        if (incoming.BaseRevision == current.Revision)
            return new(MeshSyncDecisionKind.Apply, "The incoming mutation is the direct successor of the current local revision.", current, incoming);

        return new(
            MeshSyncDecisionKind.Conflict,
            "The local resource changed after the incoming mutation's base revision. A feature-specific merge or user decision is required.",
            current,
            incoming);
    }

    private static void Validate(MeshSyncMutation incoming)
    {
        if (incoming.OperationId == Guid.Empty) throw new ArgumentException("A synchronization operation ID is required.", nameof(incoming));
        if (incoming.ResourceId == Guid.Empty) throw new ArgumentException("A synchronization resource ID is required.", nameof(incoming));
        if (string.IsNullOrWhiteSpace(incoming.ResourceType)) throw new ArgumentException("A synchronization resource type is required.", nameof(incoming));
        if (incoming.BaseRevision < 0) throw new ArgumentOutOfRangeException(nameof(incoming), "Base revision cannot be negative.");
        if (incoming.Revision <= incoming.BaseRevision) throw new ArgumentOutOfRangeException(nameof(incoming), "Incoming revision must advance beyond its base revision.");
        if (string.IsNullOrWhiteSpace(incoming.ContentHash)) throw new ArgumentException("A content hash is required.", nameof(incoming));
        if (incoming.OriginDeviceId == Guid.Empty) throw new ArgumentException("An origin device ID is required.", nameof(incoming));
    }
}
