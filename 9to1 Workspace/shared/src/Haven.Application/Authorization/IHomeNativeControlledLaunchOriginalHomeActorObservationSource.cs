namespace Haven.Application;

/// <summary>Optional observational channel on the same trusted administrator session issuer.
/// The returned remote Home actor is used only to compare a capture frame against the
/// actual originally admitted Home child. It is never a local authenticated actor source,
/// backend/resource authorization input, profile substitution or playback grant.</summary>
public interface IHomeNativeControlledLaunchOriginalHomeActorObservationSource
    : IHomeNativeControlledLaunchOriginalSessionContextSource
{
    /// <summary>Requires the SAME privately issued original context and authentic original
    /// local owner actor. Public copies, wire fields and matching lease/profile metadata deny.
    /// The issuer brackets its original child/current-channel observation with actual local
    /// actor and original session checks, and never reacquires or adopts a replacement child.</summary>
    ValueTask<AuthenticatedResourceActor?> ReadOriginalHomeActorForActorAsync(
        HomeNativeControlledLaunchSessionContext originalContext,
        AuthenticatedResourceActor expectedLocalActor, CancellationToken cancellationToken);
}
