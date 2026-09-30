using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Authenticates the real designated installed Home host before accepting native service readiness.</summary>
public static class HomeUnixDiscoveryClient
{
    public static async ValueTask<HomeNativeDiscoveryResponse> DiscoverAsync(Socket connected,
        IHomeNativeSessionHostVerifier verifier, HomeNativeSessionHostRequirement trustedHost,
        IReadOnlyList<HomeServiceRequirement> requirements, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connected); ArgumentNullException.ThrowIfNull(verifier);
        if (trustedHost is null || string.IsNullOrWhiteSpace(trustedHost.AppId) ||
            string.IsNullOrWhiteSpace(trustedHost.OperatingSystemApplicationId) || requirements is null || requirements.Count is 0 or > 100 || requirements.Any(requirement => requirement is null))
            return Unavailable();
        requirements = requirements.Select(requirement => requirement with { }).ToArray();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var observed = HomeNativePeerObservation.FromAcceptedUnixSocket(connected);
        if (observed is null) return Unavailable();
        var host = await verifier.VerifyHostAsync(observed, trustedHost, deadline.Token).ConfigureAwait(false);
        if (!Valid(host, trustedHost)) return Unavailable();
        using var stream = new NetworkStream(connected, ownsSocket: false);
        var payload = JsonSerializer.SerializeToUtf8Bytes(new HomeNativeDiscoveryRequest(requirements));
        if (payload.Length > 64 * 1024) return Unavailable();
        await HomeUnixDiscoveryTransport.WriteFrameAsync(stream, payload, deadline.Token).ConfigureAwait(false);
        HomeNativeDiscoveryResponse? response;
        try { response = JsonSerializer.Deserialize<HomeNativeDiscoveryResponse>(
            await HomeUnixDiscoveryTransport.ReadFrameAsync(stream, deadline.Token).ConfigureAwait(false), new JsonSerializerOptions { MaxDepth = 24 }); }
        catch (JsonException) { return Unavailable(); }
        catch (InvalidDataException) { return Unavailable(); }
        if (response?.Code != "HomeDiscoveryReady" || response.Snapshot?.Services is null) return Unavailable();
        foreach (var requirement in requirements)
        {
            if (requirement is null) return Unavailable();
            var services = response.Snapshot.Services.Where(service => service is not null && service.ServiceId == requirement.ServiceId).ToArray();
            if (services.Length == 0 && !requirement.Required) continue;
            if (services.Length != 1 || !services[0].IsAvailable || services[0].State != HomeServiceLifecycleState.Ready ||
                !requirement.Accepts(services[0].ContractVersion)) return Unavailable();
        }
        if (response.Snapshot.Services.Any(service => service is null || !requirements.Any(requirement => requirement.ServiceId == service.ServiceId))) return Unavailable();
        var current = await verifier.VerifyHostAsync(observed, trustedHost, deadline.Token).ConfigureAwait(false);
        if (!Valid(current, trustedHost) || current!.InstalledApplicationId != host!.InstalledApplicationId ||
            current.InstallationRevision != host.InstallationRevision || current.ExecutableIdentity != host.ExecutableIdentity) return Unavailable();
        return response;
    }
    private static bool Valid(HomeNativeInstalledPeer? peer, HomeNativeSessionHostRequirement required) => peer is not null &&
        peer.AppId == required.AppId && peer.InstalledApplicationId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(peer.InstallationRevision) && !string.IsNullOrWhiteSpace(peer.ExecutableIdentity) &&
        peer.Roles is not null && peer.Roles.Contains(HomeNativeSessionHostRequirement.RequiredRole);
    private static HomeNativeDiscoveryResponse Unavailable() => new("HomeHostIdentityOrServiceUnavailable", null);
}
