using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
namespace Haven.Infrastructure;

/// <summary>Explicit composition after genuine saved Files/Dev/root and installed Home
/// owners exist. This creates no store, actor, publisher, project, approval or native executor.
/// The exact binding resolver and compiled action policy must be in SAME Home composition.</summary>
public static class DeveloperWorkspaceExecutionServiceCollectionExtensions
{
    public static IServiceCollection AddHavenOwnedDeveloperExecutionConsent(this IServiceCollection services,
        Func<IServiceProvider, HomeNativeWindowsOwnerComponents> actualHomeComponents,
        Func<IServiceProvider, IDeveloperWorkspaceOriginalExecutionBindingSource> actualBindings)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(actualHomeComponents); ArgumentNullException.ThrowIfNull(actualBindings);
        if (services.Any(item => item.ServiceType == typeof(HomeDeveloperWorkspaceExecutionConsentSource)
            || item.ServiceType == typeof(IWorkspaceOriginalProcessStartConsentSource)
            || item.ServiceType == typeof(Func<IWorkspaceOriginalProcessStartConsentSource>)))
            throw new InvalidOperationException("The original Home execution consent source is already configured.");
        if (services.Count(item => item.ServiceType == typeof(WorkspaceTaskRunEffectAuthority)) != 1)
            throw new InvalidOperationException("One maintained actual Workspace effect authority must already be registered.");
        services.AddSingleton<HomeDeveloperWorkspaceExecutionActionPolicySource>();
        services.AddSingleton<HomeDeveloperWorkspaceExecutionConsentSource>(provider =>
        {
            var home = actualHomeComponents(provider) ?? throw new UnauthorizedAccessException("Actual installed Home owner composition is required.");
            if (!home.Resources.IsBoundToActorSource(home.Profiles) || !home.Broker.IsBoundToOriginalComposition(home.Resources, home.Permissions))
                throw new UnauthorizedAccessException("SAME original Home actor/resource/broker/policy tuple is required.");
            return new(home.StateStore, home.Profiles, home.Broker, home.Permissions, () => actualBindings(provider),
                () => provider.GetRequiredService<ITaskRunToolActionOwner>());
        });
        services.AddSingleton<IWorkspaceOriginalProcessStartConsentSource>(provider => provider.GetRequiredService<HomeDeveloperWorkspaceExecutionConsentSource>());
        // Lazy source resolves only for explicit consent binding. No effect→tool/coordinator constructor cycle.
        services.AddSingleton<Func<IWorkspaceOriginalProcessStartConsentSource>>(provider => () => provider.GetRequiredService<IWorkspaceOriginalProcessStartConsentSource>());
        return services;
    }
}
