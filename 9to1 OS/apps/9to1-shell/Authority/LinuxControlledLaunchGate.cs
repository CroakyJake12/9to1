using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Current host context only; a held lease never attests that a peer was launched by trusted code.</summary>
public sealed class LinuxControlledLaunchGate(IAuthenticatedResourceActorSource actors,
    IHomeNativeControlledLaunchAuthority authority,
    IHomeNativeControlledLaunchOriginalSessionContextSource? remoteSessions)
{
    public LinuxControlledLaunchGate(IAuthenticatedResourceActorSource actors,
        IHomeNativeControlledLaunchAuthority authority) : this(actors, authority, null) { }
    private HomeNativeSessionLease? _lease;
    private readonly SemaphoreSlim _remoteGate = new(1, 1);
    private HomeNativeControlledLaunchSessionContext? _remoteContext;
    private AuthenticatedResourceActor? _remoteActor;

    public void BindHeldLease(HomeNativeSessionLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        // Construction selects one authority mode. A separate owner never acquires or adopts
        // Home's exclusive local lease, including while a remote verification is pending.
        if (remoteSessions is not null)
            throw new InvalidOperationException("An original remote-session gate cannot adopt a local Home lease.");
        if (!lease.IsHeld || Interlocked.CompareExchange(ref _lease, lease, null) is not null)
            throw new InvalidOperationException("Controlled-launch context requires the single actually held Home lease.");
    }

    /// <summary>Calls a separately provisioned launch authority with fresh kernel observations, never IPC fields.</summary>
    public async ValueTask<bool> VerifyAsync(HomeNativeObservedPeer peer, string appId, string requiredRole, CancellationToken ct)
    {
        var actor = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        return actor is null ? false : await VerifyForActorAsync(peer, actor, appId, requiredRole, ct).ConfigureAwait(false);
    }
    public async ValueTask<bool> VerifyForActorAsync(HomeNativeObservedPeer peer,
        AuthenticatedResourceActor actor, string appId, string requiredRole, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return false;
        var lease = Volatile.Read(ref _lease);
        if (string.IsNullOrWhiteSpace(appId)) return false;
        if (lease is null) return await VerifyRemoteForActorAsync(peer, actor, appId, requiredRole, ct).ConfigureAwait(false);
        if (!lease.IsHeld || actor.ProfileId != lease.ProfileId) return false;
        var process = await LinuxProcessIdentity.ReadAsync(peer, ct).ConfigureAwait(false);
        if (process is null || !lease.IsHeld || !ReferenceEquals(lease, Volatile.Read(ref _lease)) ||
            actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return false;
        var observation = new HomeNativeControlledLaunchObservation(peer.ProcessId, peer.OperatingSystemPrincipalId,
            process.StartTime, process.ExecutableIdentity(peer.ProcessId), actor.ProfileId,
            lease.LeaseIdentity.ToString("N"), appId, requiredRole);
        if (!await authority.IsCurrentAsync(observation, ct).ConfigureAwait(false) || !lease.IsHeld ||
            !ReferenceEquals(lease, Volatile.Read(ref _lease)) || actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return false;
        var currentProcess = await LinuxProcessIdentity.ReadAsync(peer, ct).ConfigureAwait(false);
        return process == currentProcess && lease.IsHeld && ReferenceEquals(lease, Volatile.Read(ref _lease)) &&
            actor == await actors.GetCurrentAsync(ct).ConfigureAwait(false);
    }
    private async ValueTask<bool> VerifyRemoteForActorAsync(HomeNativeObservedPeer peer,
        AuthenticatedResourceActor actor, string appId, string role, CancellationToken ct)
    {
        if (remoteSessions is null || actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return false;
        HomeNativeControlledLaunchSessionContext original;
        await _remoteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_remoteActor is not null && _remoteActor != actor) return false;
            if (_remoteContext is null)
            {
                var issued = await remoteSessions.GetForActorAsync(actor, ct).ConfigureAwait(false);
                if (issued is null || issued.ProfileId != actor.ProfileId ||
                    !Guid.TryParseExact(issued.SessionLeaseIdentity, "N", out var leaseId) || leaseId == Guid.Empty ||
                    actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return false;
                _remoteActor = actor; _remoteContext = issued;
            }
            original = _remoteContext ?? throw new InvalidOperationException("The original remote context was not retained.");
        }
        finally { _remoteGate.Release(); }
        // The root source must authenticate its OWN original issuance and the actual still-held
        // remote Home lease; public context copies and wire fields cannot satisfy this check.
        if (!await remoteSessions.IsCurrentForActorAsync(original, actor, ct).ConfigureAwait(false) ||
            actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return false;
        var process = await LinuxProcessIdentity.ReadAsync(peer, ct).ConfigureAwait(false);
        if (process is null || actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false) ||
            !await remoteSessions.IsCurrentForActorAsync(original, actor, ct).ConfigureAwait(false) ||
            actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return false;
        var observation = new HomeNativeControlledLaunchObservation(peer.ProcessId, peer.OperatingSystemPrincipalId,
            process.StartTime, process.ExecutableIdentity(peer.ProcessId), actor.ProfileId,
            original.SessionLeaseIdentity, appId, role);
        if (!await authority.IsCurrentAsync(observation, ct).ConfigureAwait(false) ||
            actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false) ||
            !await remoteSessions.IsCurrentForActorAsync(original, actor, ct).ConfigureAwait(false) ||
            actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return false;
        var current = await LinuxProcessIdentity.ReadAsync(peer, ct).ConfigureAwait(false);
        return current == process && actor == await actors.GetCurrentAsync(ct).ConfigureAwait(false) &&
            await remoteSessions.IsCurrentForActorAsync(original, actor, ct).ConfigureAwait(false) &&
            actor == await actors.GetCurrentAsync(ct).ConfigureAwait(false);
    }

}
