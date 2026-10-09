namespace Haven.Application;

/// <summary>Forwards original host-verification sources under the SAME caller lifetime.
/// This optional custody protocol adds no installed identity or action consent.</summary>
public interface IHomeOriginalScopedNativeSessionHostVerifier : IHomeNativeSessionHostVerifier
{
    Task<HomeNativeInstalledPeer?> VerifyHostWithinOriginalSourceAsync(HomeNativeObservedPeer observedPeer,
        HomeNativeSessionHostRequirement trustedRequirement, Action<Action> scope, Action<Task> retain,
        CancellationToken cancellationToken);
}
