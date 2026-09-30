using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Current host context only; a held lease never attests that a peer was launched by trusted code.</summary>
public sealed class LinuxControlledLaunchGate(IAuthenticatedResourceActorSource actors,
    IHomeNativeControlledLaunchAuthority authority)
{
    private HomeNativeSessionLease? _lease;

    public void BindHeldLease(HomeNativeSessionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!lease.IsHeld || Interlocked.CompareExchange(ref _lease, lease, null) is not null)
            throw new InvalidOperationException("Controlled-launch context requires the single actually held Home lease.");
    }

    /// <summary>Calls a separately provisioned launch authority with fresh kernel observations, never IPC fields.</summary>
    public async ValueTask<bool> VerifyAsync(HomeNativeObservedPeer peer, string appId, string requiredRole, CancellationToken ct)
    {
        var lease = Volatile.Read(ref _lease);
        var actor = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        if (lease is null || !lease.IsHeld || actor?.ProfileId != lease.ProfileId || string.IsNullOrWhiteSpace(appId)) return false;
        var process = await LinuxProcessIdentity.ReadAsync(peer, ct).ConfigureAwait(false);
        if (process is null) return false;
        var observation = new HomeNativeControlledLaunchObservation(peer.ProcessId, peer.OperatingSystemPrincipalId,
            process.StartTime, process.ExecutableIdentity(peer.ProcessId), actor.ProfileId,
            lease.LeaseIdentity.ToString("N"), appId, requiredRole);
        if (!await authority.IsCurrentAsync(observation, ct).ConfigureAwait(false)) return false;
        return lease.IsHeld && ReferenceEquals(lease, Volatile.Read(ref _lease)) &&
            actor == await actors.GetCurrentAsync(ct).ConfigureAwait(false) &&
            process == await LinuxProcessIdentity.ReadAsync(peer, ct).ConfigureAwait(false);
    }
}
