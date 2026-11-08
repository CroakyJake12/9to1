using Haven.Core;

namespace Haven.Application;

/// <summary>
/// A canonical feature that owns one resource type. Mesh may only reach that feature's real store through an
/// owner it declares, and only for that type: a feature store is not writable by Mesh simply because a peer
/// is trusted. This is the explicit adapter the generic Mesh resource journal's own documentation requires
/// before Mesh may write feature data.
/// </summary>
public interface IMeshResourceMergeOwner : IMeshResourceMergeService
{
    /// <summary>The canonical resource type this owner is authoritative for, matched exactly.</summary>
    string ResourceType { get; }
}

/// <summary>
/// Lets the Mesh side ask an owner for the complete current state of one of its resources as a publishable
/// mutation. The owner reports the payload, its content hash and the revision taken from its own version
/// fields; the calling device stamps its own identity onto the origin, because an owner must never claim to
/// know which device is sending.
/// </summary>
public interface IMeshResourceStateCapture
{
    Task<MeshSyncMutation?> CaptureAsync(Guid resourceId, CancellationToken cancellationToken);
}
