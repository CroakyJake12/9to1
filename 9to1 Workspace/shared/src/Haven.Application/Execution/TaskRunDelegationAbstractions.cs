using Haven.Core;

namespace Haven.Application;

/// <summary>Source-issued live Task-owned identity. Its fields are observations, never grants.
/// The issuing owner must verify the SAME original object on every use; persisted links cannot recreate it.</summary>
public interface ITaskRunDelegationOriginal
{
    Guid ParentTaskId { get; }
    Guid ParentContextId { get; }
    Guid ParentExecutionId { get; }
    TaskRunDelegationIntent OriginalIntent { get; }
}

/// <summary>Fresh genuine parent actor and narrowed child policy over the canonical acknowledged intent.</summary>
public interface ITaskRunDelegationAuthority : ITaskRunCommandAuthority
{
    ValueTask<ITaskRunDelegationOriginal> CaptureOriginalDelegationAsync(
        TaskRunAttemptAdmission sameOriginalParent, TaskExecutionSnapshot currentParent,
        TaskRunDelegationIntent acknowledgedIntent, TaskExecutionSnapshot proposedChild,
        CancellationToken cancellationToken);

    ValueTask<ITaskRunDelegationAdmission> AuthorizeOriginalChildAsync(
        ITaskRunDelegationOriginal sameTaskOwnedOriginal,
        TaskRunAttemptAdmission sameCurrentParent, TaskExecutionSnapshot currentParent,
        TaskExecutionSnapshot proposedChild, CancellationToken cancellationToken);
}

/// <summary>One creation scope borrows the freshly issued current parent lease. It never disposes that attempt.
/// Complete observations and revalidation precede the pure lifetime pin; only the finite child CAS is inside it.</summary>
public interface ITaskRunDelegationAdmission : IAsyncDisposable
{
    ITaskRunDelegationOriginal OriginalDelegation { get; }
    TaskRunAttemptAdmission OriginalParentAttempt { get; }
    TaskExecutionOwnerBinding ChildOwner { get; }
    ValueTask RevalidateAsync(CancellationToken cancellationToken);
    ValueTask<IAsyncDisposable?> AcquireOriginalParentCommitPinAsync(CancellationToken cancellationToken);
}

/// <summary>A detached acknowledged projection, never child dispatch authority or settlement evidence.</summary>
public sealed record TaskRunDelegatedChildResult(
    TaskExecutionSnapshot Parent, TaskExecutionSnapshot Child, TaskRunDelegationIntent Delegation);
