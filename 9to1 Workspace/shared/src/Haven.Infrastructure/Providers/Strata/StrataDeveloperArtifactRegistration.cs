using HavenOS.Home.Core;
using Haven.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

public static class StrataDeveloperArtifactRegistration
{
    /// <summary>Opt-in only, after the SAME maintained local Home registration. This does
    /// not install, start or approve Strata. The default source remains setup-required until
    /// an explicit developer tuple and the original Task-scoped individual Home Accept exist.</summary>
    public static IServiceCollection AddHavenApprovedDeveloperStrataArtifacts(this IServiceCollection services,
        HomeLocalDomainComposition sameHome)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(sameHome);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The protected developer Strata source requires Linux.");
        if (services.Any(row => row.ServiceType == typeof(IStrataVerifiedInstallationSource) || row.ServiceType == typeof(HomeApprovedStrataDeveloperArtifactSource)))
            throw new InvalidOperationException("An original installation/read source is already configured; never replace its issuer.");
        var registrations = services.Where(row => row.ServiceType == typeof(HomeLocalDomainComposition)).ToArray();
        if (registrations.Length != 1 || !ReferenceEquals(registrations[0].ImplementationInstance, sameHome))
            throw new InvalidOperationException("The SAME supplied original Home domain must already be registered.");
        services.AddSingleton<HomeApprovedStrataDeveloperArtifactSource>(provider =>
        {
            if (!ReferenceEquals(provider.GetRequiredService<HomeLocalDomainComposition>(), sameHome))
                throw new UnauthorizedAccessException("The original Home composition changed.");
            return new(sameHome, provider.GetRequiredService<TaskExecutionCoordinator>(), provider.GetRequiredService<TaskRunPermissionAuthority>());
        });
        services.AddSingleton<IStrataVerifiedInstallationSource>(provider => provider.GetRequiredService<HomeApprovedStrataDeveloperArtifactSource>());
        return services;
    }
}
