using Haven.Core;

namespace Haven.Application;

/// <summary>Observation of one coordinator-owned intent CAS. This receipt alone grants no effect or permission.</summary>
public sealed class TaskRunOriginalActionAdmission
{
    internal readonly TaskExecutionCoordinator Issuer;
    internal readonly ITaskRunToolActionPreparation OriginalPreparation;
    internal readonly TaskRunAttemptAdmission OriginalAttempt;
    internal readonly object OriginalRegistration;
    internal readonly object OriginalSelf;
    internal TaskRunOriginalActionAdmission(TaskExecutionCoordinator issuer, ITaskRunToolActionPreparation preparation,
        TaskRunAttemptAdmission attempt, object registration, TaskExecutionSnapshot acknowledged)
    {
        Issuer = issuer; OriginalPreparation = preparation; OriginalAttempt = attempt;
        OriginalRegistration = registration; OriginalSelf = this;
        TaskId = acknowledged.TaskId; ContextId = acknowledged.ContextId; ExecutionId = acknowledged.ExecutionId;
        AttemptId = attempt.AttemptId; ActionId = preparation.ActionId; AcknowledgedRevision = acknowledged.PersistenceRevision;
    }
    public Guid TaskId { get; }
    public Guid ContextId { get; }
    public Guid ExecutionId { get; }
    public Guid AttemptId { get; }
    public Guid ActionId { get; }
    public long AcknowledgedRevision { get; }
}

/// <summary>Consumes only the SAME private preparation, actual issued attempt and original receipt.
/// Validate uses fresh current authority before the owner's pure pin. Demand is a synchronous
/// original-claim fence after that validation/pin; it is neither a policy read nor permission grant.</summary>
public interface ITaskRunOriginalActionAdmissionSource
{
    TaskRunOriginalActionAdmission RequireOriginalActionAdmission(
        ITaskRunToolActionPreparation samePreparation, TaskRunAttemptAdmission sameOriginalAttempt);
    Task ValidateOriginalActionAdmissionAsync(TaskRunOriginalActionAdmission originalReceipt,
        ITaskRunToolActionPreparation samePreparation, TaskRunAttemptAdmission sameOriginalAttempt, CancellationToken token);
    void DemandOriginalActionAdmission(TaskRunOriginalActionAdmission originalReceipt,
        ITaskRunToolActionPreparation samePreparation, TaskRunAttemptAdmission sameOriginalAttempt);
}
