namespace Haven.Core;

public enum MeshDeviceClass
{
    Desktop,
    Laptop,
    Phone,
    Tablet,
    Tv,
    Headset,
    Other
}

public enum MeshPeerTrustState
{
    Pending,
    Trusted,
    Revoked
}

public enum MeshPresenceState
{
    Available,
    Busy,
    Offline,
    Stale
}

public enum MeshConnectionState
{
    Disconnected,
    Pairing,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

public enum MeshTaskStatus
{
    Queued,
    Sending,
    Accepted,
    Running,
    Succeeded,
    Failed,
    CancelRequested,
    Cancelled
}

public enum MeshSyncOperationKind
{
    Upsert,
    Delete
}

public enum MeshSyncDecisionKind
{
    Apply,
    Duplicate,
    IgnoreStale,
    Conflict,
    /// <summary>Refused by a policy gate on one of the two devices before any merge rule was consulted.</summary>
    Rejected
}

public enum MeshHandoffStatus
{
    Created,
    Sent,
    Accepted,
    Rejected,
    Failed
}

public sealed record MeshLocalIdentity(
    Guid DeviceId,
    string DisplayName,
    MeshDeviceClass DeviceClass,
    CapabilityPlatform Platform,
    string PublicKeyFingerprint,
    DateTimeOffset CreatedAt);

public sealed record MeshPeerRecord(
    Guid DeviceId,
    string DisplayName,
    MeshDeviceClass DeviceClass,
    CapabilityPlatform Platform,
    string PublicKeyFingerprint,
    MeshPeerTrustState TrustState,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? TrustedAt,
    DateTimeOffset? RevokedAt,
    string? LastKnownEndpoint = null,
    IReadOnlyList<string>? AllowedRemoteCapabilities = null);

public sealed record MeshCapabilityDescriptor(
    string Key,
    string Name,
    CapabilityPlatform Platform,
    CapabilityRiskClass RiskClass,
    IReadOnlyList<string> SemanticActions,
    bool SupportsTaskDelegation = false,
    bool ProvidesModels = false,
    bool ProvidesAgents = false);

public sealed record MeshPresenceSnapshot(
    Guid DeviceId,
    MeshPresenceState Presence,
    MeshConnectionState Connection,
    DateTimeOffset ObservedAt,
    DateTimeOffset? LastSeenAt,
    IReadOnlyList<MeshCapabilityDescriptor> Capabilities,
    int ActiveRemoteTasks = 0,
    string? Activity = null);

public sealed record MeshPeerSnapshot(
    MeshPeerRecord Peer,
    MeshPresenceSnapshot Presence);

/// <summary>Nearby discovery data is intentionally untrusted until explicit pairing succeeds.</summary>
public sealed record MeshDiscoveryCandidate(
    Guid DeviceId,
    string DisplayName,
    MeshDeviceClass DeviceClass,
    CapabilityPlatform Platform,
    string PublicKeyFingerprint,
    string Endpoint,
    DateTimeOffset ObservedAt);

public sealed record MeshPairingChallenge(
    Guid Id,
    string VerificationCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string LocalFingerprint);

public sealed record MeshPairingOffer(
    Guid ChallengeId,
    string Endpoint,
    string VerificationCode,
    string DeviceFingerprint,
    DateTimeOffset ExpiresAt);

public sealed record MeshPairingEvidence(
    Guid ChallengeId,
    Guid RemoteDeviceId,
    string RemoteDisplayName,
    MeshDeviceClass RemoteDeviceClass,
    CapabilityPlatform RemotePlatform,
    string RemoteFingerprint,
    string RemoteEndpoint,
    bool VerificationCodeMatched);

public sealed record MeshPairingResult(
    bool Succeeded,
    string Message,
    MeshPeerRecord? Peer = null);

public sealed record MeshTaskReference(string Kind, string Id, string? DisplayName = null);

public sealed record MeshTaskEnvelope(
    Guid TaskId,
    Guid SourceDeviceId,
    Guid TargetDeviceId,
    string Instruction,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<MeshTaskReference> References,
    string IdempotencyKey,
    DateTimeOffset CreatedAt,
    HavenSurface SourceSurface = HavenSurface.Tasks);

public sealed record MeshTaskReceipt(
    Guid TaskId,
    MeshTaskStatus Status,
    DateTimeOffset UpdatedAt,
    string Message,
    string? Result = null,
    string? FailureCode = null);

public sealed record MeshResourceSnapshot(
    Guid ResourceId,
    string ResourceType,
    long Revision,
    string ContentHash,
    Guid OriginDeviceId,
    DateTimeOffset ModifiedAt,
    string? Payload = null);

public sealed record MeshSyncMutation(
    Guid OperationId,
    Guid ResourceId,
    string ResourceType,
    MeshSyncOperationKind Kind,
    long BaseRevision,
    long Revision,
    string ContentHash,
    Guid OriginDeviceId,
    DateTimeOffset CreatedAt,
    string? Payload = null);

public sealed record MeshSyncDecision(
    MeshSyncDecisionKind Kind,
    string Reason,
    MeshResourceSnapshot? Current,
    MeshSyncMutation Incoming);

/// <summary>
/// The receiving device's typed answer to one synchronization mutation. It reports the policy outcome
/// separately from the merge outcome, so a peer can tell "refused by permission" from "merged" from
/// "kept as a conflict" instead of inferring it from silence.
/// </summary>
public sealed record MeshSyncAcknowledgement(
    Guid OperationId,
    Guid ResourceId,
    string ResourceType,
    MeshSyncDecisionKind Decision,
    string Reason,
    long LocalRevision,
    Guid AcknowledgingDeviceId,
    DateTimeOffset AcknowledgedAt);

public enum MeshSyncDeliveryState
{
    /// <summary>The peer is not connected; the operation stays queued for a later acknowledged attempt.</summary>
    QueuedOffline,
    Sent,
    Acknowledged,
    Rejected,
    ConflictReported,
    /// <summary>No answer inside the acknowledgement window; the mutation was sent but not confirmed.</summary>
    Unconfirmed
}

/// <summary>
/// Per-destination state for one synchronization operation. Each device keeps its own row per peer so a
/// partial fan-out cannot be reported as a completed sync, and so an offline peer stays visibly pending.
/// </summary>
public sealed record MeshSyncDelivery(
    Guid OperationId,
    Guid ResourceId,
    string ResourceType,
    long Revision,
    Guid PeerDeviceId,
    MeshSyncDeliveryState State,
    string Detail,
    DateTimeOffset UpdatedAt);

/// <summary>
/// One local apply plus its fan-out. A partial fan-out is reported per destination rather than collapsed
/// into a single "synced" boolean, because some peers may be offline and others may refuse by permission.
/// </summary>
public sealed record MeshSyncPublishResult(
    MeshSyncDecisionKind LocalDecision,
    string Reason,
    IReadOnlyList<MeshSyncDelivery> Deliveries);

public sealed record MeshConflict(
    Guid Id,
    Guid ResourceId,
    string ResourceType,
    MeshResourceSnapshot Current,
    MeshSyncMutation Incoming,
    DateTimeOffset DetectedAt,
    string Reason);

public sealed record MeshHandoffEnvelope(
    Guid HandoffId,
    Guid SourceDeviceId,
    Guid TargetDeviceId,
    string ResourceType,
    string ResourceId,
    HavenSurface Surface,
    string? TaskId,
    string? ActivityContext,
    IReadOnlyList<MeshTaskReference> References,
    DateTimeOffset CreatedAt);

public sealed record MeshHandoffReceipt(
    Guid HandoffId,
    MeshHandoffStatus Status,
    DateTimeOffset UpdatedAt,
    string Message);

public sealed record MeshPersistentState(
    int Version,
    MeshLocalIdentity? LocalIdentity,
    IReadOnlyList<MeshPeerRecord> TrustedPeers,
    IReadOnlyList<MeshTaskReceipt> RemoteTasks,
    IReadOnlyList<Guid> AppliedSyncOperations,
    IReadOnlyList<MeshConflict> Conflicts,
    IReadOnlyList<MeshWorkMember>? WorkMembers = null,
    IReadOnlyList<MeshWorkMessage>? WorkMessages = null,
    IReadOnlyList<MeshWorkItem>? WorkItems = null)
{
    public const int CurrentVersion = 2;

    public static MeshPersistentState Empty { get; } = new(
        CurrentVersion,
        null,
        [],
        [],
        [],
        [],
        [],
        [],
        []);
}
