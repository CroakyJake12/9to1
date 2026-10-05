using System.Collections.Frozen;

namespace Haven.Application;

/// <summary>Obtained from OS peer credentials on an accepted local transport, never from request JSON.</summary>
public sealed record HomeNativeObservedPeer(int ProcessId, string OperatingSystemPrincipalId);

/// <summary>Verified installed receipt plus observed running peer and controlled launch/runtime evidence. Discovery metadata grants no resource or approval authority.</summary>
public sealed record HomeNativeInstalledPeer(string AppId, Guid InstalledApplicationId, string InstallationRevision,
    string ExecutableIdentity, IReadOnlySet<string> AllowedServiceIds)
{
    public IReadOnlySet<string> Roles { get; init; } = FrozenSet<string>.Empty;
}

/// <summary>
/// Verifies observed PID/principal against an authenticated installation receipt and current executable/package.
/// Implementations must account for process exit/PID reuse and executable replacement between verification and dispatch.
/// A signed payload alone is insufficient when startup hooks, preload libraries or a mutable alternate runtime can inject code.
/// Verified identity also requires trusted controlled-launch/runtime attestation; missing evidence returns null.
/// Missing or unsupported platform/package evidence returns null; an unsigned authoring manifest cannot grant authority.
/// </summary>
public interface IHomeNativeInstalledPeerVerifier
{
    ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observedPeer,
        CancellationToken cancellationToken);
}

public sealed class UnavailableHomeNativeInstalledPeerVerifier : IHomeNativeInstalledPeerVerifier
{
    public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observedPeer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<HomeNativeInstalledPeer?>(null);
    }
}

/// <summary>Trusted platform composition declares the only installed package/app allowed to host its Home session.</summary>
public sealed record HomeNativeSessionHostRequirement(string AppId, string OperatingSystemApplicationId)
{
    public const string RequiredRole = "home.session-host";
}
public interface IHomeNativeSessionHostVerifier
{
    ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer observedPeer,
        HomeNativeSessionHostRequirement trustedRequirement, CancellationToken cancellationToken);
}
