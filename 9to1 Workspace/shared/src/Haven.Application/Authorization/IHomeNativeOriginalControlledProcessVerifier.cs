namespace Haven.Application;

/// <summary>Explicit original platform composition whose privileged controlled-process
/// owner retains and rechecks the actual signed image. For example, Root-owned Linux
/// credential namespaces prevent Home from reading a sibling child's /proc/exe.
/// The accepting transport still owns actual kernel credentials/pidfd/start identity;
/// this port must independently match them and the original actor/installed tuple
/// before and after dispatch. It grants no mode selection to an IPC caller and has
/// no unavailable/default implementation. Verifiers without it keep direct image pins.</summary>
public interface IHomeNativeOriginalControlledProcessVerifier : IHomeNativeInstalledPeerOriginalActorVerifier
{
    ValueTask<bool> IsOriginalControlledProcessCurrentAsync(HomeNativeObservedPeer actualPeer,
        AuthenticatedResourceActor originalActor, string actualKernelStartIdentity,
        HomeNativeInstalledPeer originalVerifiedPeer, CancellationToken cancellationToken);
}
