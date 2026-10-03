using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces;

namespace Haven.Desktop.Services;

/// <summary>Authenticated profile resource authority. A local Space's existence alone is not a grant.</summary>
public interface IPluginSidebarSpaceAccess
{
    Task<bool> MayReadAsync(Guid spaceId, CancellationToken cancellationToken);
    Task<bool> MayManageAsync(Guid spaceId, CancellationToken cancellationToken);
}

/// <summary>Typed host routing with current resource permission checks; never interpret a target as a command.</summary>
public interface IPluginSidebarTargetHost
{
    Task<bool> MayOpenAsync(Guid spaceId, PluginSidebarContribution contribution, CancellationToken cancellationToken);
    Task OpenAsync(Guid spaceId, PluginSidebarContribution contribution, CancellationToken cancellationToken);
}

/// <summary>Adapts the existing enabled package manifests to the existing per-Space visibility registry.</summary>
public sealed class HomePluginSidebarAuthority(IExtensionRepository extensions,
    IPluginSidebarSpaceAccess? access = null, IPluginSidebarTargetHost? host = null) : IPluginSidebarAuthority
{
    public async Task<IReadOnlyList<PluginSidebarContribution>> GetDeclaredAsync(Guid spaceId, CancellationToken cancellationToken)
    {
        if (spaceId == Guid.Empty || access is null || !await access.MayReadAsync(spaceId, cancellationToken).ConfigureAwait(false)) return [];
        var scope = "space:" + spaceId.ToString("D");
        var result = new List<PluginSidebarContribution>();
        foreach (var package in await extensions.GetInstalledAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!package.IsEnabled || package.State is not (ExtensionInstallState.Installed or ExtensionInstallState.Enabled or ExtensionInstallState.UpdateAvailable) ||
                package.Manifest.PackageType is not (ExtensionPackageType.Plugin or ExtensionPackageType.PluginAndSkills) ||
                package.EnabledScopes?.Contains(scope, StringComparer.OrdinalIgnoreCase) != true ||
                (package.GrantedPermissions & package.Manifest.RequestedPermissions) != package.Manifest.RequestedPermissions) continue;
            foreach (var item in package.Manifest.SidebarContributions ?? [])
            {
                if (!Enum.IsDefined(item.TargetKind) || string.IsNullOrWhiteSpace(item.SidebarItemId) ||
                    string.IsNullOrWhiteSpace(item.Label) || string.IsNullOrWhiteSpace(item.TargetId)) continue;
                result.Add(new(package.Manifest.PackageId, item.SidebarItemId, item.Label, item.IconKey,
                    (PluginSidebarTargetKind)(int)item.TargetKind, item.TargetId));
            }
        }
        if (!await access.MayReadAsync(spaceId, cancellationToken).ConfigureAwait(false)) return [];
        // Duplicate package identity cannot select an arbitrary installed manifest.
        return result.GroupBy(item => (item.PluginId, item.SidebarItemId)).Where(group => group.Count() == 1)
            .Select(group => group.Single()).ToArray();
    }

    public Task<bool> MayManageAsync(Guid spaceId, CancellationToken cancellationToken) =>
        spaceId != Guid.Empty && access is not null ? access.MayManageAsync(spaceId, cancellationToken) : Task.FromResult(false);

    public async Task<bool> MayOpenAsync(Guid spaceId, PluginSidebarContribution contribution, CancellationToken cancellationToken) =>
        host is not null && (await GetDeclaredAsync(spaceId, cancellationToken).ConfigureAwait(false)).Contains(contribution) &&
        await host.MayOpenAsync(spaceId, contribution, cancellationToken).ConfigureAwait(false);

    public async Task OpenAsync(Guid spaceId, PluginSidebarContribution contribution, CancellationToken cancellationToken)
    {
        if (!await MayOpenAsync(spaceId, contribution, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Plugin sidebar target is unavailable or outside the current Space grants.");
        await host!.OpenAsync(spaceId, contribution, cancellationToken).ConfigureAwait(false);
    }
}
