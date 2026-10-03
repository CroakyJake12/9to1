namespace Haven.Application;

/// <summary>Attests the observed Home host for the exact original authenticated actor.
/// Requirement comes only from trusted installed host composition, never the wire.
/// Implementations preserve that actor through installed receipt/process/role/resource
/// checks and deny if it changes; discovery VerifyHostAsync is not an original-actor fallback.</summary>
public interface IHomeNativeSessionHostOriginalActorVerifier : IHomeNativeSessionHostVerifier
{
    ValueTask<HomeNativeInstalledPeer?> VerifyHostForActorAsync(HomeNativeObservedPeer observedPeer,
        HomeNativeSessionHostRequirement trustedRequirement, AuthenticatedResourceActor expectedActor,
        CancellationToken cancellationToken);
}
