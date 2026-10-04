using Haven.Application;

namespace HavenOS.Home.Core;

public enum HomeStateCommitPhase { Admission, Publication }

/// <summary>Trusted in-process guard. Runs under the Home writer lease and must not read or write that store.</summary>
public interface IHomeStateCommitActorGuard
{
    ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor,
        HomeStateCommitPhase phase, CancellationToken cancellationToken);
}
