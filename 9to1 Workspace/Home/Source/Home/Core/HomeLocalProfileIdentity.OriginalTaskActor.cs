using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Preserves the actual Home source's nested custody when this SAME
/// instance is explicitly configured as a Task actor. It creates no equivalence
/// with a native OS Task actor, owner renewal or resource permission.</summary>
public sealed partial class HomeLocalProfileIdentity : ITaskRunOriginalTaskActorObservationSource
{
    public Task<AuthenticatedResourceActor?> GetOriginalCurrentWithinSourceAsync(
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken) => GetCurrentWithinOriginalSourceAsync(
            originalSynchronousScope, retainOriginalTask, cancellationToken);
}
