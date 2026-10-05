using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Haven.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Haven.Infrastructure;

/// <summary>Opt-in Core read composition for the designated Home owner. This registration
/// neither starts Home nor acquires a lease or creates an installed verifier/actor/socket.</summary>
public static class HomeOriginalCoreServiceCollectionExtensions
{
    public static IServiceCollection AddHavenOriginalHomeCoreServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeActionPolicySource, HomeCoreServiceReadActionPolicies>());
        services.TryAddSingleton<HomeNativeCoreApiSessions>(provider => new(
            provider.GetRequiredService<HomePermissionTrustService>(),
            provider.GetRequiredService<IAuthenticatedResourceActorSource>(),
            provider.GetRequiredService<IHomeNativeInstalledPeerVerifier>(),
            () => new HomeCoreApi(provider.GetRequiredService<HomeCoreRuntime>(),
                provider.GetRequiredService<HomeNativeCoreApiSessions>())));
        services.TryAddSingleton<IHomeCoreAuthorization>(provider => provider.GetRequiredService<HomeNativeCoreApiSessions>());
        return services;
    }
}
