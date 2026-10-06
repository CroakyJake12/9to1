using Haven.Application;
namespace HavenOS.Home.Core;

public sealed partial class HomeLocalProfileIdentity : IOriginalScopedResourceActorSource
{
    public Task<AuthenticatedResourceActor?> GetCurrentWithinOriginalSourceAsync(
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
        => GetCurrentAsync(originalSynchronousScope, retainOriginalTask, token).AsTask();
}
