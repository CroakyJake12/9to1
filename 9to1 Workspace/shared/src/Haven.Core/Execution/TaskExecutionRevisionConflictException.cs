namespace Haven.Core;

/// <summary>A stale task checkpoint cannot replace the current durable task or its identity.</summary>
public sealed class TaskExecutionRevisionConflictException(Guid taskId, long expectedRevision, long proposedRevision)
    : InvalidOperationException("The durable task changed before this checkpoint could be saved. Reload the current task before continuing.")
{
    public Guid TaskId { get; } = taskId;
    public long ExpectedRevision { get; } = expectedRevision;
    public long ProposedRevision { get; } = proposedRevision;
}
