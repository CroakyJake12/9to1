using Haven.Core;

namespace Haven.Application;

/// <summary>An original process-local acknowledgment, minted only after actual terminal/successor task CAS.</summary>
public sealed class TaskRunOriginalRetirementAcknowledgment
{
    private readonly TaskRunOriginalRetirementAcknowledgment _originalIssuerObject;

    internal TaskRunOriginalRetirementAcknowledgment(
        TaskRunAttemptAdmission originalAdmission, TaskExecutionSnapshot acknowledgedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(originalAdmission);
        ArgumentNullException.ThrowIfNull(acknowledgedSnapshot);
        var previous = acknowledgedSnapshot.Attempts.FirstOrDefault(item => item.Id == originalAdmission.AttemptId);
        var current = acknowledgedSnapshot.Attempts.LastOrDefault();
        var terminal = current?.Id == originalAdmission.AttemptId && current.State == TaskRunAttemptState.Completed
            && acknowledgedSnapshot.State == TaskExecutionLifecycle.Completed;
        var successor = current is not null && current.Id != originalAdmission.AttemptId
            && current.RetryOfAttemptId == originalAdmission.AttemptId
            && current.State is TaskRunAttemptState.Admitted or TaskRunAttemptState.Running
            && previous?.State is TaskRunAttemptState.Failed or TaskRunAttemptState.Suspended;
        if (acknowledgedSnapshot.TaskId != originalAdmission.Snapshot.TaskId
            || acknowledgedSnapshot.ContextId != originalAdmission.Snapshot.ContextId
            || acknowledgedSnapshot.ExecutionId != originalAdmission.Snapshot.ExecutionId
            || acknowledgedSnapshot.OwnerBinding != originalAdmission.Lease.Owner
            || acknowledgedSnapshot.CreatedAt != originalAdmission.Snapshot.CreatedAt
            || acknowledgedSnapshot.PersistenceRevision <= originalAdmission.Snapshot.PersistenceRevision
            || previous is null || !terminal && !successor)
            throw new InvalidOperationException("No actual terminal/successor CAS acknowledges this same original attempt.");
        OriginalAdmission = originalAdmission;
        AcknowledgedSnapshot = acknowledgedSnapshot;
        _originalIssuerObject = this;
    }

    public TaskRunAttemptAdmission OriginalAdmission { get; }
    public TaskExecutionSnapshot AcknowledgedSnapshot { get; }
    public Guid TaskId => OriginalAdmission.Snapshot.TaskId;
    public Guid ContextId => OriginalAdmission.Snapshot.ContextId;
    public Guid ExecutionId => OriginalAdmission.Snapshot.ExecutionId;
    public Guid AttemptId => OriginalAdmission.AttemptId;
    public long AcknowledgedRevision => AcknowledgedSnapshot.PersistenceRevision;
    internal bool IsOriginalAcknowledgment => ReferenceEquals(_originalIssuerObject, this);
}
