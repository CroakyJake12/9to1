namespace Haven.Application;

public sealed partial class TaskRunPermissionAuthority
{
    private readonly HashSet<RenewalWork> _originalTaskActorObservations = [];
    private void PruneOriginalTaskActorObservations()
    {
        lock (_sync)
        {
            foreach (var actual in _originalTaskActorObservations.Where(work =>
                work.Driver.IsCompletedSuccessfully && work.Errors.Count == 0 && !work.CleanupIncomplete &&
                work.Raw.All(raw => raw.IsCompletedSuccessfully)).ToArray())
            {
                _renewalWork.Remove(actual); _originalTaskActorObservations.Remove(actual);
            }
        }
    }
    public bool HasOriginalTaskActorSource(IAuthenticatedResourceActorSource sameSource) =>
        sameSource is not null && ReferenceEquals(_actors, sameSource);

    public Task<AuthenticatedResourceActor?> ObserveOriginalTaskActorAsync(Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        if (_actors is not ITaskRunOriginalTaskActorObservationSource actualSource)
            throw new InvalidOperationException("TASK_ACTOR_SOURCE_SCOPE_REQUIRED: the actual configured Task actor cannot supply nested source custody.");
        PruneOriginalTaskActorObservations(); // Only these marked successful observations; all other work is conserved.
        return StartRenewalWork<AuthenticatedResourceActor?>(null, cancellationToken, async work =>
        {
            lock (_sync) _originalTaskActorObservations.Add(work);
            var callback = new TaskRunOriginalResponseSourceCallbacks(originalSynchronousScope, retainOriginalTask);
            var source = new InferenceAdmissionCallbacks(this, work, callback.Run, retainOriginalTask);
            try
            {
                var actor = await source.ReadAsync(() => actualSource.GetOriginalCurrentWithinSourceAsync(source.Run, source.Retain, cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                lock (_sync) DemandRenewalOpen();
                return actor;
            }
            finally
            {
                foreach (var raw in source.Originals.ToArray()) await JoinRenewalTaskAsync(raw, work.Errors).ConfigureAwait(false);
            }
        });
    }
}
