using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Trust is ready only after canonical durable state and current OS-bound authority are verified.</summary>
public sealed class HomePermissionsCoreService(HomePermissionTrustService permissions,
    IAuthenticatedResourceActorSource actors) : IHomeCoreService
{
    public HomeServiceDescriptor Descriptor { get; } = new("permissions.trust", HomeCoreServiceCatalog.CurrentContractVersion,
        HomeServiceLifecycleState.Stopped, false, "Permission state has not been verified.");
    public IReadOnlyList<string> Dependencies { get; } = ["home.core", "home.state"];
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("Home permission authority requires a verified current profile.");
        await permissions.GetSnapshotAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (actor != await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Home profile changed while permission state was loading.");
    }
    public Task StopAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
}
