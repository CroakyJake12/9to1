using Haven.Core;

namespace Haven.Application;

/// <summary>Detached point-in-time presentation data. It grants no command, provider,
/// continuation, verification or permission authority and cannot reconstruct a producer.</summary>
public sealed record TaskRunActivityObservation(Guid TaskId, Guid ExecutionId, Guid ContextId,
    long PersistenceRevision, bool IsLive, DateTimeOffset ObservedAt);

/// <summary>An invalidation hint from the SAME actual producer. It contains no status,
/// revision, permission or data grant; consumers must perform a fresh authorized read.</summary>
public sealed record TaskRunActivityInvalidation(Guid TaskId, Guid ExecutionId, Guid ContextId);

public sealed partial class TaskExecutionCoordinator
{
    public event EventHandler<TaskRunActivityInvalidation>? RunActivityInvalidated;
    internal void InvalidateOriginalRunActivity(TaskRunInvocationCustody original, CanonicalChatProcessProducer producer)
    {
        if (HasSealedOriginalProcessProducerAdmission || !ReferenceEquals(original.Issuer, this) ||
            !ReferenceEquals(original.OriginalSelf, original) || !ReferenceEquals(original.OriginalProcessProducer, producer) ||
            original.OriginalBinding is not { } binding || !_originalInvocations.TryGetValue(binding.TaskId, out var retained) ||
            !ReferenceEquals(retained, original)) return;
        var hint = new TaskRunActivityInvalidation(binding.TaskId, binding.ExecutionId, binding.ContextId);
        if (RunActivityInvalidated is { } handlers)
            foreach (EventHandler<TaskRunActivityInvalidation> handler in handlers.GetInvocationList())
                try { handler(this, hint); }
                catch (Exception observerFailure)
                {
                    // Match acknowledged snapshot observation isolation: a presentation
                    // listener cannot change original provider/cleanup/Task outcomes.
                    _observationFailures.Enqueue(new TaskObservationFailure(binding.TaskId, binding.ExecutionId,
                        binding.PersistenceRevision, "run-activity-observer", observerFailure));
                }
    }

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
