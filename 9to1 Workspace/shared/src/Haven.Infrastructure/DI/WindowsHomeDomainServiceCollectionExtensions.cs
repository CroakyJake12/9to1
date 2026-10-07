using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>Registers the actual Windows process-owned Home tuple. This neither starts
/// Home nor supplies an installed-peer verifier, CAKE identity, store binding or approval.
/// The caller retains Home and closes it only after its actual business borrowers drain.</summary>
public static class WindowsHomeDomainServiceCollectionExtensions
{
    public static IServiceCollection AddHavenOwnedWindowsHomeDomain(this IServiceCollection services,
        HomeNativeWindowsComposition sameHome, IAppPaths samePaths,
        ITrustedHostPrincipalSource samePrincipal)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(sameHome);
        ArgumentNullException.ThrowIfNull(samePaths);
        ArgumentNullException.ThrowIfNull(samePrincipal);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The actual Windows Home domain requires Windows.");
        Type[] owners = [typeof(HomeNativeWindowsComposition), typeof(HomeLocalDomainComposition),
            typeof(FileHomeCoreStateStore), typeof(IHomeCoreStateStore), typeof(HomeLocalProfileIdentity),
            typeof(IAuthenticatedResourceActorSource), typeof(ITrustedHostPrincipalSource),
            typeof(HomePermissionTrustService), typeof(ResourceAuthorizationService),
            typeof(HomeResourceOperationBroker), typeof(HomeLocalStoreOwnership),
            typeof(HomeResourceStoreOwnershipAuthority), typeof(IResourceStoreOwnershipAuthority),
            typeof(IResourceStoreOwnershipReceiptAuthority), typeof(HomeNativeCoreApiSessions),
            typeof(IHomeCoreAuthorization), typeof(IHomeNativeInstalledPeerVerifier),
            typeof(HomeCoreRuntime), typeof(HomeCoreApi), typeof(IHomeCoreApi)];
        if (services.Any(row => owners.Contains(row.ServiceType)))
            throw new InvalidOperationException("A Home owner already exists; parallel or substituted tuples are refused.");
        if (!ReferenceEquals(sameHome.Services.GetService(typeof(ITrustedHostPrincipalSource)), samePrincipal) ||
            !HomeLocalReadComposition.IsBound(sameHome.StateStore, sameHome.Profiles, sameHome.Ownership) ||
            !sameHome.Resources.IsBoundToActorSource(sameHome.Profiles) ||
            !sameHome.Broker.IsBoundToOriginalComposition(sameHome.Resources, sameHome.Permissions))
            throw new UnauthorizedAccessException("The SAME Windows Home principal/store/profile/broker tuple is required.");
        var paths = services.Where(row => row.ServiceType == typeof(IAppPaths)).Take(2).ToArray();
        if (paths.Length != 1 || paths[0].Lifetime != ServiceLifetime.Singleton ||
            !(ReferenceEquals(paths[0].ImplementationInstance, samePaths) || paths[0].ImplementationType == typeof(AppPaths)))
            throw new InvalidOperationException("The maintained uninstantiated paths descriptor or SAME supplied paths instance is required.");
        // All refusal checks precede descriptor changes. Existing Home state is never adopted.
        services.Remove(paths[0]); services.AddSingleton(samePaths);
        services.AddSingleton(sameHome);
        services.AddSingleton(sameHome.StateStore); services.AddSingleton<IHomeCoreStateStore>(sameHome.StateStore);
        services.AddSingleton(sameHome.Profiles); services.AddSingleton<IAuthenticatedResourceActorSource>(sameHome.Profiles);
        services.AddSingleton(samePrincipal);
        services.AddSingleton(sameHome.Permissions); services.AddSingleton(sameHome.Resources);
        services.AddSingleton(sameHome.Broker); services.AddSingleton(sameHome.LocalStoreOwnership);
        services.AddSingleton(sameHome.Ownership);
        services.AddSingleton<IResourceStoreOwnershipAuthority>(sameHome.Ownership);
        services.AddSingleton<IResourceStoreOwnershipReceiptAuthority>(sameHome.Ownership);
        services.AddSingleton(sameHome.Sessions); services.AddSingleton<IHomeCoreAuthorization>(sameHome.Sessions);
        services.AddSingleton((IHomeNativeInstalledPeerVerifier)sameHome.Services.GetService(typeof(IHomeNativeInstalledPeerVerifier))!);
        services.AddSingleton(sameHome.Runtime); services.AddSingleton(sameHome.Api);
        services.AddSingleton<IHomeCoreApi>(sameHome.Api);
        return services;
    }

    /// <summary>Reference validation for subsequent trusted host registrations only.
    /// This never issues a permission, native session or installed-peer observation.</summary>
    public static HomeNativeWindowsOwnerComponents RequireOriginalWindowsHomeComponents(
        this IServiceProvider provider, HomeNativeWindowsComposition sameHome)
    {
        ArgumentNullException.ThrowIfNull(provider); ArgumentNullException.ThrowIfNull(sameHome);
        if (!ReferenceEquals(provider.GetRequiredService<HomeNativeWindowsComposition>(), sameHome) ||
            !ReferenceEquals(provider.GetRequiredService<IHomeCoreStateStore>(), sameHome.StateStore) ||
            !ReferenceEquals(provider.GetRequiredService<HomeLocalProfileIdentity>(), sameHome.Profiles) ||
            !ReferenceEquals(provider.GetRequiredService<IAuthenticatedResourceActorSource>(), sameHome.Profiles) ||
            !ReferenceEquals(provider.GetRequiredService<HomePermissionTrustService>(), sameHome.Permissions) ||
            !ReferenceEquals(provider.GetRequiredService<ResourceAuthorizationService>(), sameHome.Resources) ||
            !ReferenceEquals(provider.GetRequiredService<IResourceStoreOwnershipAuthority>(), sameHome.Ownership) ||
            !ReferenceEquals(provider.GetRequiredService<HomeResourceOperationBroker>(), sameHome.Broker))
            throw new UnauthorizedAccessException("The original Windows Home tuple was replaced.");
        return new(sameHome.StateStore, sameHome.Profiles, sameHome.Permissions,
            sameHome.Resources, sameHome.Ownership, sameHome.Broker);
    }
}
