namespace Haven.Application;

/// <summary>Optional actual Task identity leaf. This supplies source custody for current
/// actor observations, never a Task lease, Home resource identity or execution grant.</summary>
public interface ITaskRunOriginalTaskActorObservationSource : IAuthenticatedResourceActorSource
{
    Task<AuthenticatedResourceActor?> GetOriginalCurrentWithinSourceAsync(Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
}
