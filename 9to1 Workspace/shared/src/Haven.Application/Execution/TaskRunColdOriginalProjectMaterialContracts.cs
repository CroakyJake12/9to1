using Haven.Core;

namespace Haven.Application;

/// <summary>Observations from the SAME journal's private authenticated project claim.
/// Public fields or an implementation of this interface never establish issuance.</summary>
public interface ITaskRunColdOriginalProjectMaterial
{
    ITaskRunColdJournalClaim OriginalClaim { get; }
    ITaskRunColdContextLease OriginalContext { get; }
    TaskExecutionSnapshot OriginalExpected { get; }
    TaskRunColdChatInput OriginalInput { get; }
    TaskRunColdProjectIdentity OriginalProjectIdentity { get; }
}

/// <summary>The actual configured journal issues and recognizes material only after
/// protected current claim/row/input reads. The Home producer must use this SAME source;
/// captured root/context strings and previous actor or permission fields grant nothing.</summary>
public interface ITaskRunColdOriginalProjectClaimSource
{
    Task<ITaskRunColdOriginalProjectMaterial> GetOriginalProjectMaterialWithinSourceAsync(
        ITaskRunColdJournalClaim sameClaim, ITaskRunColdContextLease sameContext,
        TaskExecutionSnapshot actualExpected, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    // Historical reference custody, including after the resource/context close. Not current use.
    bool IsIssuedOriginalProjectMaterial(ITaskRunColdOriginalProjectMaterial sameMaterial,
        ITaskRunColdJournalClaim sameClaim, ITaskRunColdContextLease sameContext,
        TaskExecutionSnapshot sameExpected);
    Task ValidateOriginalProjectMaterialWithinSourceAsync(ITaskRunColdOriginalProjectMaterial sameMaterial,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    // Authenticate the exact private ACK/current row/input and successful owning closes.
    // Never reopen the sticky Claim or recreate an old lease/permission/native pin.
    Task ValidateOriginalClosedProjectMaterialWithinSourceAsync(
        ITaskRunColdOriginalProjectMaterial sameMaterial, ITaskRunColdJournalAcknowledgment sameAcknowledgment,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}

/// <summary>Optional SAME-journal bridge consumed by the actual cold owner authority.
/// It requires a NEW privately issued Home READ/root lease on the SAME provisional
/// Context. Context owns its native CAS custody and independently closes it before
/// identity activation. Ordinary model/tool/Dev effect authority remains separate.</summary>
public interface ITaskRunColdOriginalProjectBoundarySource
{
    Task ValidateOriginalProjectBoundaryWithinSourceAsync(ITaskRunColdJournalClaim sameClaim,
        ITaskRunColdContextLease sameContext, TaskExecutionSnapshot actualExpected,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    Task ValidateOriginalClosedProjectBoundaryWithinSourceAsync(ITaskRunColdContextLease sameContext,
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    // SAME held native/Home custody only. No profile/policy lookup or new grant under CAS.
    void DemandOriginalProjectBoundaryCommit(ITaskRunColdJournalClaim sameClaim,
        ITaskRunColdContextLease sameContext, TaskExecutionSnapshot sameExpected);
}
