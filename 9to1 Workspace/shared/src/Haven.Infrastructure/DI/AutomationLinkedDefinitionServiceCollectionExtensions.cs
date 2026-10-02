using Haven.Application.Automations;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>Opt-in same-graph linked definition review only; no graph publication, run or scheduler activation.</summary>
public static class AutomationLinkedDefinitionServiceCollectionExtensions
{
    public static IServiceCollection AddHavenAutomationLinkedDefinitionOwnership(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        RequireCanonicalConcrete<AutomationDefinitionReviewCaller>(services);
        RequireCanonicalConcrete<AutomationLocalStoreAuthority>(services);
        RequireExistingSingleton<AutomationRepository>(services);
        RequireExistingSingleton<WorkspaceStateRepository>(services);
        RequireExistingSingleton<IAutomationDefinitionReviewCaller>(services);
        RequireExistingSingleton<IAutomationOwnerRepository>(services);
        RequireExistingSingleton<IReusableTaskOwnerRepository>(services);
        if (services.Any(d => d.ServiceType == typeof(AutomationLinkedDefinitionReviewCaller)))
            throw new InvalidOperationException("A competing linked automation owner is already registered.");
        services.AddSingleton<AutomationLinkedDefinitionReviewCaller>();
        return services;
    }

    private static void RequireCanonicalConcrete<T>(IServiceCollection services)
    {
        var rows = services.Where(d => d.ServiceType == typeof(T)).ToArray();
        if (rows.Length != 1 || rows[0].Lifetime != ServiceLifetime.Singleton || rows[0].ImplementationType != typeof(T))
            throw new InvalidOperationException("The canonical automation definition owner module must be registered first.");
    }

    private static void RequireExistingSingleton<T>(IServiceCollection services)
    {
        var rows = services.Where(d => d.ServiceType == typeof(T)).ToArray();
        if (rows.Length != 1 || rows[0].Lifetime != ServiceLifetime.Singleton)
            throw new InvalidOperationException("The existing canonical automation owner singleton is unavailable or competing.");
    }
}
