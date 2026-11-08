using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Routes a Mesh synchronization operation to the feature that owns that resource type. Everything Mesh can
/// journal for itself keeps using the generic store; anything canonical belongs to a declared owner and never
/// reaches the generic journal, so a board cannot be quietly kept in two places. An unowned type stays in the
/// journal, which is why the journal is not a copy of product data.
/// </summary>
public sealed class MeshMergeRouter(IMeshResourceMergeService journal, IReadOnlyList<IMeshResourceMergeOwner> owners)
    : IMeshResourceMergeService, IMeshResourceStateCapture
{
    private IMeshResourceMergeOwner? Find(string resourceType) =>
        owners.FirstOrDefault(owner => string.Equals(owner.ResourceType, resourceType, StringComparison.Ordinal));

    public Task<MeshResourceSnapshot?> GetCurrentAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        var target = (IMeshResourceMergeService?)Find(resourceType) ?? journal;
        return target.GetCurrentAsync(resourceType, resourceId, cancellationToken);
    }

    public Task<bool> TryApplyAsync(MeshSyncMutation mutation, CancellationToken cancellationToken)
    {
        var target = (IMeshResourceMergeService?)Find(mutation.ResourceType) ?? journal;
        return target.TryApplyAsync(mutation, cancellationToken);
    }

    public async Task<MeshSyncMutation?> CaptureAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        foreach (var owner in owners.OfType<IMeshResourceStateCapture>())
        {
            var captured = await owner.CaptureAsync(resourceId, cancellationToken).ConfigureAwait(false);
            if (captured is not null) return captured;
        }
        return null;
    }
}
