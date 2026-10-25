using Haven.Application;
using System.Net.Sockets;

namespace HavenOS.Home.Core;

/// <summary>Discovery admission for an OS-observed installed peer. It exposes no app approval or mutation endpoint.</summary>
public sealed class HomeNativeDiscoverySession(IHomeNativeInstalledPeerVerifier verifier,
    IAuthenticatedResourceActorSource actors, HomeCoreRuntime runtime)
{
    public ValueTask<HomeCoreStateSnapshot?> DiscoverAsync(HomeNativeObservedPeer observed,
        IReadOnlyList<HomeServiceRequirement> requirements, CancellationToken cancellationToken = default)
        => DiscoverOriginalAsync(observed, requirements, null, cancellationToken);

    // Platform-owned socket capture, never selected by request fields or by an
    // executable-read error. Unconfigured/default verifiers keep the direct pin.
    internal HomeNativeLinuxPeerProcess? RetainOriginalUnixPeer(Socket actualAcceptedSocket)
        => verifier is IHomeNativeOriginalControlledProcessVerifier
            ? HomeNativeLinuxPeerProcess.CaptureControlledLifetime(actualAcceptedSocket)
            : HomeNativePeerObservation.RetainAcceptedUnixProcess(actualAcceptedSocket);
    internal ValueTask<HomeCoreStateSnapshot?> DiscoverOriginalUnixAsync(HomeNativeLinuxPeerProcess actualHeldProcess,
        IReadOnlyList<HomeServiceRequirement> requirements, CancellationToken token)
    {
        var observed = actualHeldProcess.Observe();
        return observed is null ? ValueTask.FromResult<HomeCoreStateSnapshot?>(null)
            : DiscoverOriginalAsync(observed, requirements, actualHeldProcess.OriginalStartIdentity, token);
    }
    private async ValueTask<HomeNativeInstalledPeer?> VerifyOriginalAsync(HomeNativeObservedPeer observed,
        AuthenticatedResourceActor actor, string? actualKernelStartIdentity, CancellationToken token)
    {
        if (verifier is not IHomeNativeOriginalControlledProcessVerifier root)
            return await verifier.VerifyAsync(observed, token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(actualKernelStartIdentity)) return null;
        var peer = await root.VerifyForActorAsync(observed, actor, token).ConfigureAwait(false);
        return peer is not null && await root.IsOriginalControlledProcessCurrentAsync(observed, actor,
            actualKernelStartIdentity, peer, token).ConfigureAwait(false) ? peer : null;
    }
    private async ValueTask<HomeCoreStateSnapshot?> DiscoverOriginalAsync(HomeNativeObservedPeer observed,
        IReadOnlyList<HomeServiceRequirement> requirements, string? actualKernelStartIdentity, CancellationToken cancellationToken)
    {
        if (observed is null || observed.ProcessId <= 0 || string.IsNullOrWhiteSpace(observed.OperatingSystemPrincipalId) ||
            requirements is null || requirements.Count == 0 || requirements.Count > 100 ||
            requirements.Any(requirement => requirement is null || string.IsNullOrWhiteSpace(requirement.ServiceId))) return null;
        requirements = requirements.Select(requirement => requirement with { }).ToArray();
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null) return null;
        var peer = await VerifyOriginalAsync(observed, actor, actualKernelStartIdentity, cancellationToken).ConfigureAwait(false);
        if (!Valid(peer) || requirements.Any(requirement => requirement.Required && !peer!.AllowedServiceIds.Contains(requirement.ServiceId))) return null;
        var snapshot = runtime.Current;
        var available = new List<HomeServiceDescriptor>();
        foreach (var requirement in requirements)
        {
            if (!peer!.AllowedServiceIds.Contains(requirement.ServiceId)) continue;
            var matches = snapshot.Services.Where(service => service.ServiceId == requirement.ServiceId).ToArray();
            if (matches.Length != 1 || !matches[0].IsAvailable || matches[0].State != HomeServiceLifecycleState.Ready ||
                !requirement.Accepts(matches[0].ContractVersion))
            { if (requirement.Required) return null; continue; }
            available.Add(matches[0]);
        }
        // Revalidation covers package revocation/replacement and session/profile changes across async calls.
        var currentPeer = await VerifyOriginalAsync(observed, actor, actualKernelStartIdentity, cancellationToken).ConfigureAwait(false);
        if (!Valid(currentPeer) || currentPeer!.AppId != peer!.AppId ||
            currentPeer.InstalledApplicationId != peer.InstalledApplicationId || currentPeer.InstallationRevision != peer.InstallationRevision ||
            currentPeer.ExecutableIdentity != peer.ExecutableIdentity ||
            available.Any(service => !currentPeer.AllowedServiceIds.Contains(service.ServiceId)) ||
            await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor || runtime.Current.Revision != snapshot.Revision) return null;
        return snapshot with { Services = available.ToArray() };
    }
    private static bool Valid(HomeNativeInstalledPeer? peer) => peer is not null &&
        !string.IsNullOrWhiteSpace(peer.AppId) && peer.InstalledApplicationId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(peer.InstallationRevision) && !string.IsNullOrWhiteSpace(peer.ExecutableIdentity) &&
        peer.AllowedServiceIds is not null;
}
