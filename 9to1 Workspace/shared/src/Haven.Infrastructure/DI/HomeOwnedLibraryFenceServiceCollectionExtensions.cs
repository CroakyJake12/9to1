using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Haven.Infrastructure;

/// <summary>Registers the final library fence against the existing canonical local Home composition.
/// No second Home store, actor source, ownership issuer or operation broker is created here.</summary>
public static class HomeOwnedLibraryFenceServiceCollectionExtensions
{
    public static IServiceCollection AddHomeOwnedLibraryCommitFences(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<HomeOwnedLibraryCommitFenceSource>(provider =>
        {
            var actualHome = provider.GetRequiredService<IHomeCoreStateStore>() as FileHomeCoreStateStore
                ?? throw new InvalidOperationException("Final library commits require the actual local FileHome store.");
            var actualOwnership = provider.GetRequiredService<IResourceStoreOwnershipAuthority>() as HomeResourceStoreOwnershipAuthority
                ?? throw new InvalidOperationException("Final library commits require the canonical Home ownership issuer.");
            return new(actualHome, provider.GetRequiredService<HomeLocalProfileIdentity>(), actualOwnership,
                provider.GetRequiredService<HomeResourceOperationBroker>());
        });
        return services;
    }
}
