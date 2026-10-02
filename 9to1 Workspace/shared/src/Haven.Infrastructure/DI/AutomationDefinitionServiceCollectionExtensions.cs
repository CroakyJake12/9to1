using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Haven.Infrastructure;

/// <summary>Explicit definition-owner graph composition only. No scheduler, graph publication or run activation.</summary>
public static class AutomationDefinitionServiceCollectionExtensions
{
    public static IServiceCollection AddHavenAutomationDefinitionOwnership(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var automationRegistration = CanonicalRegistration<IAutomationRepository, AutomationRepository>(services);
        var workspaceRegistration = CanonicalRegistration<IWorkspaceStateRepository, WorkspaceStateRepository>(services);
        if (services.Any(d => d.ServiceType == typeof(IAutomationOwnerRepository) ||
            d.ServiceType == typeof(IReusableTaskOwnerRepository) || d.ServiceType == typeof(AutomationLocalStoreAuthority) ||
            d.ServiceType == typeof(AutomationHomeDefinitionOperation) ||
            d.ServiceType == typeof(AutomationDefinitionReviewCaller) || d.ServiceType == typeof(IAutomationDefinitionReviewCaller)))
            throw new InvalidOperationException("A competing automation owner graph is already registered.");
        services.Remove(automationRegistration);
        services.Remove(workspaceRegistration);
        services.AddSingleton<AutomationRepository>(sp => new(sp.GetRequiredService<SqliteDatabase>(),
            ownerAuthority: null, ownerAuthorityAccessor: () => sp.GetRequiredService<AutomationLocalStoreAuthority>()));
        services.AddSingleton<IAutomationRepository>(sp => sp.GetRequiredService<AutomationRepository>());
        services.AddSingleton<IAutomationOwnerRepository>(sp => sp.GetRequiredService<AutomationRepository>());
        services.AddSingleton<WorkspaceStateRepository>(sp => new(sp.GetRequiredService<SqliteDatabase>(),
            ownerAuthority: null, ownerAuthorityAccessor: () => sp.GetRequiredService<AutomationLocalStoreAuthority>()));
        services.AddSingleton<IWorkspaceStateRepository>(sp => sp.GetRequiredService<WorkspaceStateRepository>());
        services.AddSingleton<IReusableTaskOwnerRepository>(sp => sp.GetRequiredService<WorkspaceStateRepository>());
        services.AddSingleton<AutomationLocalStoreAuthority>();
        services.AddSingleton<AutomationHomeDefinitionOperation>();
        services.AddSingleton<AutomationDefinitionReviewCaller>();
        services.AddSingleton<IAutomationDefinitionReviewCaller>(sp => sp.GetRequiredService<AutomationDefinitionReviewCaller>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeLocalStoreEvidenceProvider, AutomationLocalStoreEvidenceProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICanonicalResourceAccessResolver, AutomationOwnerDefinitionAccessResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICanonicalResourceAccessResolver, AutomationOwnerReusableTaskAccessResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeActionPolicySource, AutomationDefinitionActionPolicies>());
        return services;
    }
    private static ServiceDescriptor CanonicalRegistration<TInterface, TImplementation>(IServiceCollection services)
    {
        var existing = services.Where(d => d.ServiceType == typeof(TInterface)).ToArray();
        if (existing.Length != 1 || existing[0].Lifetime != ServiceLifetime.Singleton ||
            existing[0].ImplementationType != typeof(TImplementation) || services.Any(d => d.ServiceType == typeof(TImplementation)))
            throw new InvalidOperationException("The existing canonical repository registration is unavailable or competing.");
        return existing[0];
    }
}
