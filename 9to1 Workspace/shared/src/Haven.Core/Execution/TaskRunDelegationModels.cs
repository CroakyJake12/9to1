namespace Haven.Core;

/// <summary>Acknowledged lifecycle of one canonical delegation; this is not a permission or runtime state machine.</summary>
public enum TaskRunDelegationState
{
    IntentAcknowledged = 0,
    ChildLinked = 1,
    ChildCompleted = 2
}

/// <summary>Durable child-to-parent correlation only. A current private issuer must validate every child attempt.</summary>
public sealed record TaskRunParentDelegation(
    Guid ParentTaskId, Guid ParentContextId, Guid ParentExecutionId, Guid DelegationId);

/// <summary>Fixed child identities are acknowledged in the parent before child creation.
/// Digest and scopes record exact intent; they convey no actor, credential, effect, or egress authority.</summary>
public sealed record TaskRunDelegationIntent(
    Guid Id, Guid ParentTaskId, Guid ParentExecutionId, Guid CreatedByAttemptId,
    Guid ChildTaskId, Guid ChildContextId, Guid ChildExecutionId,
    string RequestKey, string OriginalRequestDigest, string PromptSummary,
    IReadOnlyList<string> RequestedPermissionScopes, TaskRunDelegationState State,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    long? AcknowledgedChildRevision = null)
{
    /// <summary>Current acknowledged child projection only; absence means no child owner observation exists.</summary>
    public TaskExecutionLifecycle? ObservedChildState { get; init; }
}
