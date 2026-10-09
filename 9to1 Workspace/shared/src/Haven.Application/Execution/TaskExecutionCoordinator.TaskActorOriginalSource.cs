using System.Runtime.CompilerServices;

namespace Haven.Application;

/// <summary>An observation from the configured Task actor source. It grants no
/// Task, recovery, inference or resource authority and does not replace a fresh read.</summary>
public sealed class TaskRunOriginalTaskActorObservation
{
    internal TaskRunOriginalTaskActorObservation(bool configured, AuthenticatedResourceActor? actor)
    { IsConfigured = configured; Actor = actor; }

    public bool IsConfigured { get; }
    public AuthenticatedResourceActor? Actor { get; }
}

public sealed partial class TaskExecutionCoordinator
{
    private readonly ConditionalWeakTable<TaskRunOriginalTaskActorObservation, OriginalTaskActorObservation>
        _originalTaskActorObservations = new();

    private sealed class OriginalTaskActorObservation(TaskRunProcessStageCustody stage,
        TaskRunPermissionAuthority? authority, Task<AuthenticatedResourceActor?>? raw)
    {
        internal readonly TaskRunProcessStageCustody Stage = stage;
        internal readonly TaskRunPermissionAuthority? Authority = authority;
        internal readonly Task<AuthenticatedResourceActor?>? Raw = raw;
    }

    /// <summary>Observes the SAME actual Task authority's scoped actor source in a
    /// finite process stage. Home membership reads remain separate. An unconfigured
    /// authority/scoped source is an issued unavailable observation, never a fallback.</summary>
    public Task<TaskRunOriginalTaskActorObservation> ObserveOriginalTaskActorWithinSourceAsync(
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        return StartOriginalProcessStage("observe-original-task-actor", cancellationToken,
            token => ObserveOriginalTaskActorBodyAsync(originalSynchronousScope, retainOriginalTask, token));
    }

    private async Task<TaskRunOriginalTaskActorObservation> ObserveOriginalTaskActorBodyAsync(
        Action<Action> callerScope, Action<Task> callerRetain, CancellationToken token)
    {
        var stage = RequireOriginalProcessStage();
        var callbacks = new InferenceAttemptCallbacks(stage, callerScope, callerRetain);
        token.ThrowIfCancellationRequested();
        var authority = callbacks.Invoke(() => _admissionAuthority as TaskRunPermissionAuthority);
        var configured = callbacks.Invoke(() => authority is not null && !authority.IsOriginalAdmissionSealed &&
            authority.OriginalTaskActors is ITaskRunOriginalTaskActorObservationSource);
        Task<AuthenticatedResourceActor?>? raw = null;
        AuthenticatedResourceActor? actor = null;
        if (configured)
        {
            void Run(Action body) => callbacks.Invoke(() => { body(); return true; });
            void Retain(Task actual)
            {
                // Deep raw custody precedes admission rechecks, the foreign retainer
                // and any postcallback refusal once this source has acquired a Task.
                stage.RetainSource(actual);
                callbacks.Invoke(() => { callerRetain(actual); return true; });
            }
            actor = await callbacks.ReadAsync(() => raw = authority!.ObserveOriginalTaskActorAsync(
                Run, Retain, token)).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        return callbacks.Invoke(() =>
        {
            if (!ReferenceEquals(_admissionAuthority, authority) && authority is not null ||
                configured && authority!.IsOriginalAdmissionSealed)
                throw new InvalidOperationException("The actual Task actor authority retired during observation.");
            var result = new TaskRunOriginalTaskActorObservation(configured, actor);
            _originalTaskActorObservations.Add(result, new(stage, authority, raw));
            return result;
        });
    }

    /// <summary>Pure provenance for this SAME completed finite source observation.
    /// It does not attest continued actor currentness or grant command authority.</summary>
    public bool IsIssuedOriginalTaskActorObservation(TaskRunOriginalTaskActorObservation same)
    {
        if (same is null || !_originalTaskActorObservations.TryGetValue(same, out var original) ||
            !ReferenceEquals(original.Stage.Owner, this) || !original.Stage.HealthyClosed ||
            original.Stage.ActualDriver is not Task<TaskRunOriginalTaskActorObservation> { IsCompletedSuccessfully: true } driver ||
            !ReferenceEquals(driver.Result, same)) return false;
        if (!same.IsConfigured) return original.Raw is null && same.Actor is null;
        return original.Authority is not null && ReferenceEquals(_admissionAuthority, original.Authority) &&
            original.Raw is { IsCompletedSuccessfully: true } raw && ReferenceEquals(raw.Result, same.Actor);
    }

    /// <summary>Pure pairing with the SAME actual configured Task actor source.
    /// This adds no currentness or authority to the completed observation.</summary>
    public bool IsIssuedOriginalTaskActorObservation(TaskRunOriginalTaskActorObservation same,
        IAuthenticatedResourceActorSource sameActualSource)
    {
        if (same is null || sameActualSource is null || !same.IsConfigured || !IsIssuedOriginalTaskActorObservation(same) ||
            !_originalTaskActorObservations.TryGetValue(same, out var original) || original.Authority is null)
            return false;
        return ReferenceEquals(original.Authority.OriginalTaskActors, sameActualSource);
    }

}
