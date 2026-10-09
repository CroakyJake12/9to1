using Haven.Core;

namespace Haven.Application;

/// <summary>Real actor/policy/credential/egress admission. Recorded scopes and client claims are not grants.</summary>
public interface ITaskRunAdmissionAuthority
{
    Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(
        TaskExecutionSnapshot proposed, CancellationToken cancellationToken);
    Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(
        TaskExecutionSnapshot snapshot, Guid proposedAttemptId, TaskRunRouteCandidate candidate,
        Guid? expectedPreviousAttemptId, CancellationToken cancellationToken);
    Task ValidateAcceptedActionAsync(
        TaskExecutionSnapshot snapshot, Guid attemptId, Guid actionId,
        string ownerReceiptReference, CancellationToken cancellationToken);
}

/// <summary>Fresh actor and task ownership for UI commands; persisted provenance is not a present authorization grant.</summary>
public interface ITaskRunCommandAuthority : ITaskRunAdmissionAuthority
{
    Task ValidateTaskCommandAsync(
        TaskExecutionSnapshot currentSnapshot, string command, CancellationToken cancellationToken);
}

/// <summary>Issuer-owned, process-local authority; never persisted or reconstructed from receipt text.</summary>
public interface ITaskRunAdmissionLease : IAsyncDisposable
{
    TaskExecutionOwnerBinding Owner { get; }
    Guid AttemptId { get; }
    TaskRunRouteCandidate Candidate { get; }
    string ReceiptReference { get; }
    ValueTask RevalidateAsync(CancellationToken cancellationToken);
}

/// <summary>Issuer-owned original attempt lifetime pin; shares the actual lease retirement boundary, never a Home or SQLite transaction lock.</summary>
public interface ITaskRunAdmissionCommitLease : ITaskRunAdmissionLease
{
    ValueTask<IAsyncDisposable?> AcquireOriginalCommitPinAsync(CancellationToken cancellationToken);
}

/// <summary>Joins the actual whole-body/finally work registered for this exact original attempt.</summary>
public interface ITaskRunRuntimeSettlement
{
    Task AwaitSettlementAsync(Guid taskId, Guid executionId, Guid attemptId, CancellationToken cancellationToken);
}

/// <summary>Observation from the original checkpoint service's actual execution scope, not an arbitrary same-context checkpoint ID.</summary>
public interface ICheckpointExecutionObservationSource
{
    Task<CheckpointInfo?> GetOriginalCheckpointAsync(
        Guid executionId, Guid checkpointId, CancellationToken cancellationToken);
}

/// <summary>The caller retains the original issuer lease for runtime/tool admission; disposal is its actual drain.</summary>
public sealed record TaskRunAttemptAdmission(
    TaskExecutionSnapshot Snapshot, Guid AttemptId, ITaskRunAdmissionLease Lease);

/// <summary>Exact observation failure retained separately from an acknowledged durable mutation.</summary>
public sealed record TaskObservationFailure(
    Guid TaskId, Guid ExecutionId, long AcknowledgedRevision,
    string ObservationStage, Exception OriginalException);

/// <summary>Releases only an exact healthy original after the task owner acknowledges its terminal or successor CAS.</summary>
public interface ITaskRunOriginalAttemptRetirement : ITaskRunRuntimeSettlement
{
    ValueTask RetireAcknowledgedOriginalAttemptAsync(
        TaskRunOriginalRetirementAcknowledgment acknowledgedRetirement, CancellationToken cancellationToken);
}

/// <summary>Explicit caller intent. Merely injecting a task coordinator never changes ordinary free/local chat.</summary>
public enum TaskRunExecutionIntent
{
    OrdinaryConversation = 0,
    CanonicalAgenticTask = 1
}
