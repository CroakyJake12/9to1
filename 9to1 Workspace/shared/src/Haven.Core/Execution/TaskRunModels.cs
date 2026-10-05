namespace Haven.Core;

/// <summary>Recorded provenance from the actual admission authority, never a capability or credential.</summary>
public sealed record TaskExecutionOwnerBinding(
    Guid TaskId, Guid ContextId, Guid ExecutionId,
    string ActorId, string ProfileId, Guid? AccountId, Guid? OrganisationId,
    string AuthenticationRevision, string AuthorizationReceiptReference);

/// <summary>Exact task-owned route observation from the actual selector; revision is not an invented external config revision. This record grants no access.</summary>
public sealed record TaskRunRouteCandidate(
    string RouteId, long RouteRevision, string ProviderId, string ModelId,
    string? ArtifactIdentity, bool UsesCloud, IReadOnlyList<string> RequiredCapabilities);

public enum TaskRunAttemptState
{
    Admitted = 0,
    Running = 1,
    Failed = 2,
    Suspended = 3,
    Completed = 4
}

/// <summary>A provider attempt belongs to the original Task/Execution; fallback never creates a new run.</summary>
public sealed record TaskRunAttempt(
    Guid Id, TaskRunRouteCandidate Candidate, TaskRunAttemptState State,
    string AdmissionReceiptReference, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    Guid? RetryOfAttemptId = null, ExecutionFailure? Failure = null);

/// <summary>Owner-validated acceptance, separate from a tool runtime's successful return.</summary>
public sealed record TaskActionAcceptance(
    Guid AttemptId, string OwnerReceiptReference, DateTimeOffset ObservedAt);

/// <summary>Bounded exact tool intent captured by the original owner; arguments and secrets are never persisted here.</summary>
public sealed record TaskOriginalToolIntent(
    string RuntimeKey, string ToolName, string? CanonicalWorkspaceRoot, string CallDigest);

/// <summary>Immutable observation carried on the original provider request; it conveys no authority.</summary>
public sealed record ProviderExecutionContext(
    Guid TaskId, Guid ContextId, Guid ExecutionId, Guid? AttemptId,
    long PersistenceRevision, Guid? ActionId = null)
{
    public TaskRunRouteCandidate? RequestedCandidate { get; init; }
    public TaskRunRouteCandidate? SelectedCandidate { get; init; }
}
