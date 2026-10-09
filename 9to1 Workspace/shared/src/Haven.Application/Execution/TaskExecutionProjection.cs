using Haven.Core;

namespace Haven.Application;

/// <summary>Derived from the one durable owner; this is not a second task, graph, or permission store.</summary>
public sealed record TaskExecutionProjection(
    Guid TaskId, Guid ContextId, Guid ExecutionId, long PersistenceRevision,
    TaskExecutionLifecycle State, Guid? CurrentAttemptId, Guid? LastAcceptedActionId,
    Guid? CheckpointId, IReadOnlyList<TaskAcceptedActionProjection> AcceptedActions,
    IReadOnlyList<TaskPlanNode> UnfinishedActions)
{
    public TaskRunRecoveryObservation? RecoveryObservation { get; init; }
    public IReadOnlyList<TaskRunDelegationIntent> ChildRuns { get; init; } = [];
    public TaskRunParentDelegation? ParentDelegation { get; init; }

    public static TaskExecutionProjection From(TaskExecutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var accepted = snapshot.Plan.Where(node => node.State == TaskPlanNodeState.Completed && node.Acceptance is not null)
            .Select(node => new TaskAcceptedActionProjection(node.ActionId, node.ParentActionId,
                node.Acceptance!.AttemptId, node.Acceptance.ObservedAt)).ToArray();
        return new(snapshot.TaskId, snapshot.ContextId, snapshot.ExecutionId, snapshot.PersistenceRevision,
            snapshot.State, snapshot.Attempts.LastOrDefault()?.Id,
            accepted.Any(action => action.ActionId == snapshot.LastCheckpointActionId) ? snapshot.LastCheckpointActionId : null,
            snapshot.CheckpointId, Array.AsReadOnly(accepted), Array.AsReadOnly(snapshot.Plan
                .Where(node => node.State is not (TaskPlanNodeState.Completed or TaskPlanNodeState.Superseded)).ToArray()))
        {
            RecoveryObservation = snapshot.RecoveryObservation,
            ChildRuns = Array.AsReadOnly(snapshot.Delegations.Select(intent => intent with
            {
                RequestedPermissionScopes = Array.AsReadOnly(intent.RequestedPermissionScopes.ToArray())
            }).ToArray()),
            ParentDelegation = snapshot.ParentDelegation
        };
    }
}

/// <summary>Only the canonical owning-service acceptance may produce this projection.</summary>
public sealed record TaskAcceptedActionProjection(
    Guid ActionId, Guid? ParentActionId, Guid AttemptId, DateTimeOffset ObservedAt);
