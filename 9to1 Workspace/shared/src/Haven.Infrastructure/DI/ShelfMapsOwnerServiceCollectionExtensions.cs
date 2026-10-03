using Haven.Application;
using Haven.Application.Shelf;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Haven.Infrastructure;

/// <summary>Explicit same-settings owner composition only; no store binding or native mounting.</summary>
public static class ShelfMapsOwnerServiceCollectionExtensions
{
    public static IServiceCollection AddHavenShelfMapsLibraryOwnership(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] owned = [typeof(ShelfLibraryService), typeof(MapsJourneyService),
            typeof(HomeShelfLibraryOwner), typeof(HomeMapsLibraryOwner)];
        if (services.Any(descriptor => owned.Contains(descriptor.ServiceType)))
            throw new InvalidOperationException("A competing Shelf/Maps owning service graph is already registered.");
        services.AddSingleton<ShelfLibraryService>(provider => new(Settings(provider)));
        services.AddSingleton<MapsJourneyService>(provider => new(Settings(provider)));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeLocalStoreEvidenceProvider, ShelfOwnedLibraryEvidence>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeLocalStoreEvidenceProvider, MapsOwnedLibraryEvidence>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeActionPolicySource, ShelfOwnedLibraryActionPolicies>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeActionPolicySource, MapsOwnedLibraryActionPolicies>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICanonicalResourceAccessResolver, ShelfOwnedLibraryAccessResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICanonicalResourceAccessResolver, MapsOwnedLibraryAccessResolver>());
        services.AddSingleton<HomeShelfLibraryOwner>(provider =>
        {
            RequireSoleEvidence(provider, "shelf", typeof(ShelfOwnedLibraryEvidence));
            return ActivatorUtilities.CreateInstance<HomeShelfLibraryOwner>(provider);
        });
        services.AddSingleton<HomeMapsLibraryOwner>(provider =>
        {
            RequireSoleEvidence(provider, "maps", typeof(MapsOwnedLibraryEvidence));
            return ActivatorUtilities.CreateInstance<HomeMapsLibraryOwner>(provider);
        });
        return services;
    }
    private static IVersionedSettingsStore Settings(IServiceProvider provider)
    {
        var settings = provider.GetRequiredService<IVersionedSettingsStore>();
        if (settings is not IResourceStoreIdentitySource)
            throw new InvalidOperationException("The canonical settings singleton must own its actual store identity.");
        return settings; // Never use a global SQLite identity alias.
    }
    private static void RequireSoleEvidence(IServiceProvider provider, string kind, Type expectedType)
    {
        var entries = provider.GetServices<IHomeLocalStoreEvidenceProvider>()
            .Where(value => string.Equals(value.ResourceKind, kind, StringComparison.Ordinal)).ToArray();
        if (entries.Length != 1 || entries[0].GetType() != expectedType)
            throw new InvalidOperationException("Exactly one canonical evidence provider is required for " + kind + ".");
    }
}
