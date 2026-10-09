using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
namespace Haven.Infrastructure;

/// <summary>Explicit actual Home/Files host composition. This registers no profile, root,
/// source selection or approval. The configured source factory must return the actual singleton.</summary>
public static class DeveloperProjectReadServiceCollectionExtensions
{
    public static IServiceCollection AddHavenOwnedDeveloperSourceReads(this IServiceCollection services,
        Func<IServiceProvider, HomeNativeWindowsOwnerComponents> actualHomeComponents,
        Func<IServiceProvider, IDeveloperProjectOriginalPhysicalReadSelectionSource> actualSelections)
    {
        ArgumentNullException.ThrowIfNull(actualHomeComponents); ArgumentNullException.ThrowIfNull(actualSelections);
        if (services.Any(x => x.ServiceType == typeof(HomeDeveloperProjectReadAdmissionSource)))
            throw new InvalidOperationException("The original Home project READ owner is already configured.");
        services.AddSingleton<HomeDeveloperProjectReadActionPolicySource>();
        services.AddSingleton<HomeDeveloperProjectReadResourceResolver>(provider => new(() => provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>()));
        services.AddSingleton<HomeDeveloperProjectReadAdmissionSource>(provider =>
        {
            var home = actualHomeComponents(provider) ?? throw new InvalidOperationException("Actual installed Home composition is required.");
            if (!home.Resources.IsBoundToActorSource(home.Profiles) || !home.Broker.IsBoundToOriginalComposition(home.Resources, home.Permissions))
                throw new UnauthorizedAccessException("SAME actual Home actor/resource/broker composition required.");
            return new(home.StateStore, home.Profiles, home.Broker, home.Permissions, () => actualSelections(provider));
        });
        services.AddSingleton<IDeveloperProjectOriginalReadAdmissionSource>(provider => provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>());
        services.AddSingleton<IDeveloperProjectOriginalReadRetirementSource>(provider => provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>());
        services.AddSingleton<IDeveloperProjectOriginalReadAdmissionJoinGuard>(provider => provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>());
        return services;
    }
}
