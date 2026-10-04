namespace Haven.Application;

/// <summary>Verifies the actual observed installed peer for the caller's exact original
/// actor. Implementations must check that actor before and after their owner/process/resource
/// awaits; VerifyAsync discovery cannot substitute a newly captured ambient profile.
/// This interface does not attest a peer supplied in JSON or grant resource access.</summary>
public interface IHomeNativeInstalledPeerOriginalActorVerifier : IHomeNativeInstalledPeerVerifier
{
    ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observedPeer,
        AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken);
}
