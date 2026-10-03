using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Discovery admission for an OS-observed installed peer. It exposes no app approval or mutation endpoint.</summary>
public sealed class HomeNativeDiscoverySession(IHomeNativeInstalledPeerVerifier verifier,
    IAuthenticatedResourceActorSource actors, HomeCoreRuntime runtime)
{
    public async ValueTask<HomeCoreStateSnapshot?> DiscoverAsync(HomeNativeObservedPeer observed,
        IReadOnlyList<HomeServiceRequirement> requirements, CancellationToken cancellationToken = default)
    {
        if (observed is null || observed.ProcessId <= 0 || string.IsNullOrWhiteSpace(observed.OperatingSystemPrincipalId) ||
            requirements is null || requirements.Count == 0 || requirements.Count > 100 ||
            requirements.Any(requirement => requirement is null || string.IsNullOrWhiteSpace(requirement.ServiceId))) return null;
        requirements = requirements.Select(requirement => requirement with { }).ToArray();
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null) return null;
        var peer = await verifier.VerifyAsync(observed, cancellationToken).ConfigureAwait(false);
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
        var currentPeer = await verifier.VerifyAsync(observed, cancellationToken).ConfigureAwait(false);
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
