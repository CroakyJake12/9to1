using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>Maintained native composition over the SAME settings/evidence/Home
/// tuple. Registration neither imports a store nor grants any object operation.</summary>
public static class OriginalWindowsSpaceSettingsServiceCollectionExtensions
{
    public static IServiceCollection AddOriginalWindowsSpaceSettings(this IServiceCollection services,
        HomeNativeWindowsComposition originalHome, VersionedAtomicSettingsStore originalSettings,
        SpaceLocalStoreEvidenceProvider originalEvidence, SpaceLocalStoreAuthority originalAuthority)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(originalHome);
        ArgumentNullException.ThrowIfNull(originalSettings);
        ArgumentNullException.ThrowIfNull(originalEvidence);
        ArgumentNullException.ThrowIfNull(originalAuthority);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The original native Home composition requires Windows.");
        if (!ReferenceEquals(originalHome.Services.GetService(typeof(SpaceLocalStoreEvidenceProvider)), originalEvidence) ||
            !ReferenceEquals(originalHome.Services.GetService(typeof(SpaceLocalStoreAuthority)), originalAuthority) ||
            !originalEvidence.IsBoundToOriginalSettings(originalSettings) ||
            !originalAuthority.IsBoundToOriginalComposition(originalSettings, originalHome.Profiles, originalHome.Ownership))
            throw new UnauthorizedAccessException("Use the same original Home/Space store/evidence/authority instances.");
        var settings = services.Where(row => row.ServiceType == typeof(IVersionedSettingsStore)).ToArray();
        if (settings.Length != 1 || settings[0].Lifetime != ServiceLifetime.Singleton ||
            settings[0].ImplementationType != typeof(VersionedAtomicSettingsStore) ||
            services.Any(row => row.ServiceType == typeof(VersionedAtomicSettingsStore) ||
                row.ServiceType == typeof(SpaceLocalStoreEvidenceProvider) || row.ServiceType == typeof(SpaceLocalStoreAuthority) ||
                row.ServiceType == typeof(SpaceNavigationPreferences)))
            throw new InvalidOperationException("Only the maintained uninstantiated settings descriptor may be replaced with its original native instance.");
        // All identity/refusal checks precede descriptor changes. The existing
        // SpaceRegistry factory then resolves this same instance, as do all other
        // current settings consumers; no parallel registry/store is introduced.
        services.Remove(settings[0]);
        services.AddSingleton(originalSettings);
        services.AddSingleton<IVersionedSettingsStore>(originalSettings);
        services.AddSingleton(originalEvidence);
        services.AddSingleton(originalAuthority);
        services.AddSingleton<SpaceNavigationPreferences>(provider => new(
            provider.GetRequiredService<VersionedAtomicSettingsStore>(), provider.GetRequiredService<SpaceLocalStoreAuthority>()));
        return services;
    }
}
