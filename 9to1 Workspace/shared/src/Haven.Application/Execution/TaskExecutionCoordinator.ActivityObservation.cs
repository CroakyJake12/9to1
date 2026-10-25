using Haven.Core;

namespace Haven.Application;

/// <summary>Detached point-in-time presentation data. It grants no command, provider,
/// continuation, verification or permission authority and cannot reconstruct a producer.</summary>
public sealed record TaskRunActivityObservation(Guid TaskId, Guid ExecutionId, Guid ContextId,
    long PersistenceRevision, bool IsLive, DateTimeOffset ObservedAt);

public sealed partial class TaskExecutionCoordinator
{
    public Task<TaskRunActivityObservation> ObserveOriginalRunActivityAsync(Guid taskId, Guid expectedExecutionId,
        CancellationToken cancellationToken) => StartOriginalProcessStage("observe-original-run-activity", cancellationToken, async token =>
    {
        var before = await RequireOriginalProcessSnapshotAsync(taskId, token).ConfigureAwait(false);
        RequireRun(before, expectedExecutionId);
        // Reuse the actual current task-observation owner, not a new UI permission rule.
        await ValidateOriginalProcessCommandAsync(before, "task:observe-original-controls", token).ConfigureAwait(false);
        var current = await RequireOriginalProcessSnapshotAsync(taskId, token).ConfigureAwait(false);
        RequireRun(current, expectedExecutionId);
        await ValidateOriginalProcessCommandAsync(current, "task:observe-original-controls", token).ConfigureAwait(false);
        var original = TryGetOriginalRunControlSource(current);
        var active = !HasSealedOriginalProcessProducerAdmission && current.State == TaskExecutionLifecycle.Running &&
            current.Attempts.LastOrDefault()?.State == TaskRunAttemptState.Running &&
            original is { OriginalProviderInvocationInvoked: true, OwnedCleanupTerminal: false,
                OriginalProcessRetirementRequested: false, ReachedEnd: false, DisposeInvoked: false, TrackerDisposeInvoked: false } &&
            original.Causes.Count == 0 && original.OriginalProcessProducer?.HasLiveOriginalSource == true;
        return new TaskRunActivityObservation(current.TaskId, current.ExecutionId, current.ContextId,
            current.PersistenceRevision, active, DateTimeOffset.UtcNow);
    });
}
