namespace Haven.Application;

/// <summary>Observation of one original remotely held Home session. These fields neither hold
/// the lease nor grant installation, resource access, host authority or an actor-source alias.
/// The trusted issuer must retain the original root-supervisor/kernel observations privately.</summary>
public sealed record HomeNativeControlledLaunchSessionContext(
    string ProfileId,
    string SessionLeaseIdentity,
    HomeNativeObservedPeer OriginalHostPeer,
    string OriginalHostProcessStartIdentity,
    string OriginalHostExecutableIdentity);

/// <summary>Optional trusted platform composition for a separate installed owner process.
/// Missing context denies controlled-launch verification; there is no local-lease substitution.
/// The implementation must independently authenticate the actual root supervisor and connected
/// caller using kernel credentials, process start/executable and recorded controlled bootstrap.
/// A wire actor, endpoint locator, signed installation receipt or matching profile alone is insufficient.
/// The owner keeps its genuine local actor; the remote Home actor is observational and must never
/// become an authenticated resource actor source or a resource-authorization input.</summary>
public interface IHomeNativeControlledLaunchOriginalSessionContextSource
{
    /// <summary>Returns one issuer-bound observation only for the actual original local actor,
    /// independently matching the live recorded Home host and actually held session lease.
    /// Implementations must check the local original actor before and after all owner awaits.</summary>
    ValueTask<HomeNativeControlledLaunchSessionContext?> GetForActorAsync(
        AuthenticatedResourceActor expectedLocalActor, CancellationToken cancellationToken);

    /// <summary>Rechecks the SAME originally issued context; must not rebind to a replacement
    /// host, lease or session. Public record copies and wire fields do not establish issuance.
    /// Checks the actual root supervisor, recorded original host PID/principal/start/executable,
    /// current held lease/profile and actual original local caller/actor before and after awaits.</summary>
    ValueTask<bool> IsCurrentForActorAsync(HomeNativeControlledLaunchSessionContext originalContext,
        AuthenticatedResourceActor expectedLocalActor, CancellationToken cancellationToken);
}
