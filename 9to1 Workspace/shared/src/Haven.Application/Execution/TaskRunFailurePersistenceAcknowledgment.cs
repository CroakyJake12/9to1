using Haven.Core;

namespace Haven.Application;

/// <summary>A process-local acknowledgment issued after the canonical failure write; never reconstructed from durable text.</summary>
public sealed class TaskRunFailurePersistenceAcknowledgment
{
    internal TaskRunFailurePersistenceAcknowledgment(
        TaskRunOriginalFailureObservation originalObservation,
        TaskRunAttemptAdmission originalAdmission,
        TaskExecutionSnapshot acknowledgedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(originalObservation);
        ArgumentNullException.ThrowIfNull(originalAdmission);
        ArgumentNullException.ThrowIfNull(acknowledgedSnapshot);
        var failed = acknowledgedSnapshot.Attempts.LastOrDefault();
        if (acknowledgedSnapshot.TaskId != originalAdmission.Snapshot.TaskId
            || acknowledgedSnapshot.ExecutionId != originalAdmission.Snapshot.ExecutionId
            || failed is null || failed.Id != originalAdmission.AttemptId || failed.State != TaskRunAttemptState.Failed
            || acknowledgedSnapshot.PersistenceRevision <= originalAdmission.Snapshot.PersistenceRevision)
            throw new InvalidOperationException("The acknowledged failure does not bind the original task attempt.");
        OriginalObservation = originalObservation;
        OriginalAdmission = originalAdmission;
        AcknowledgedSnapshot = acknowledgedSnapshot;
    }

    public TaskRunOriginalFailureObservation OriginalObservation { get; }
    public TaskRunAttemptAdmission OriginalAdmission { get; }
    public TaskExecutionSnapshot AcknowledgedSnapshot { get; }
    public Guid TaskId => AcknowledgedSnapshot.TaskId;
    public Guid ExecutionId => AcknowledgedSnapshot.ExecutionId;
    public Guid AttemptId => OriginalAdmission.AttemptId;
    public long AcknowledgedRevision => AcknowledgedSnapshot.PersistenceRevision;
}
