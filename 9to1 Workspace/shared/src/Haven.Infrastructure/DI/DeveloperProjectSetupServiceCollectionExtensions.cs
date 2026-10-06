using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
namespace Haven.Infrastructure;

/// <summary>Explicit composition after the genuine installed Home/Files/source owners are
/// configured. The destination resolver and this compiled policy must also be included in
/// the SAME Home broker construction; no store, actor, destination or approval is created.</summary>
public static class DeveloperProjectSetupServiceCollectionExtensions
{
    public static IServiceCollection AddHavenOwnedDeveloperSetups(this IServiceCollection services,
        Func<IServiceProvider, HomeNativeWindowsOwnerComponents> actualHomeComponents,
        Func<IServiceProvider, IDeveloperProjectOriginalSetupScopeSource> actualScopes,
        Func<IServiceProvider, IDeveloperProjectOriginalCaptureAuthority> actualCaptures,
        Func<IServiceProvider, IDeveloperProjectOriginalSetupStepOutcomeSource> actualOutcomes)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(actualHomeComponents);
        ArgumentNullException.ThrowIfNull(actualScopes); ArgumentNullException.ThrowIfNull(actualCaptures); ArgumentNullException.ThrowIfNull(actualOutcomes);
        if (services.Any(item => item.ServiceType == typeof(HomeDeveloperProjectSetupPermissionSource)))
            throw new InvalidOperationException("The genuine Home setup permission source is already configured.");
        services.AddSingleton<HomeDeveloperProjectSetupActionPolicySource>();
        services.AddSingleton<HomeDeveloperProjectSetupPermissionSource>(provider =>
        {
            var home = actualHomeComponents(provider) ?? throw new UnauthorizedAccessException("Genuine installed Home composition is required.");
            if (!home.Resources.IsBoundToActorSource(home.Profiles) || !home.Broker.IsBoundToOriginalComposition(home.Resources, home.Permissions))
                throw new UnauthorizedAccessException("The SAME current Home actor/resource/broker/policy tuple is required.");
            return new(home.StateStore, home.Profiles, home.Broker, home.Permissions,
                () => actualScopes(provider), () => actualCaptures(provider), () => actualOutcomes(provider));
        });
        services.AddSingleton<IDeveloperProjectOriginalSetupPermissionSource>(provider => provider.GetRequiredService<HomeDeveloperProjectSetupPermissionSource>());
        return services;
    }
}
