using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Haven.Infrastructure;

/// <summary>Explicit local graph publication composition only; no run, scheduler or SQL graph-binding activation.</summary>
public static class AutomationGraphOwnerServiceCollectionExtensions
{
    public static IServiceCollection AddHavenLocalAutomationGraphPublication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var graphRows = services.Where(d => d.ServiceType == typeof(IVersionedNodeGraphRepository)).ToArray();
        if (graphRows.Length != 1 || graphRows[0].Lifetime != ServiceLifetime.Singleton ||
            graphRows[0].ImplementationType != typeof(HomeVersionedNodeGraphRepository))
            throw new InvalidOperationException("The existing canonical Home graph repository is required.");
        RequireDefinitionOwner<AutomationDefinitionReviewCaller>(services);
        RequireDefinitionOwner<AutomationLocalStoreAuthority>(services);
        var reserved = new[] { typeof(HomeVersionedNodeGraphRepository), typeof(HomeGraphPublicationOwner),
            typeof(HomeGraphPublicationResourceRegistry), typeof(HomeGraphSqlAssociationLeaseSource),
            typeof(AutomationGraphOriginAuthority), typeof(AutomationGraphPublicationPreparer),
            typeof(IGraphPublicationOriginAuthority), typeof(NodeGraphAutomationAdapter) };
        if (services.Any(d => reserved.Contains(d.ServiceType)))
            throw new InvalidOperationException("A competing graph publication owner is already configured.");
        // Alias the actual existing interface singleton. Never instantiate a second Home graph store/repository.
        services.AddSingleton<HomeVersionedNodeGraphRepository>(sp =>
            sp.GetRequiredService<IVersionedNodeGraphRepository>() as HomeVersionedNodeGraphRepository
            ?? throw new NotSupportedException("The configured canonical graph repository is unavailable."));
        services.AddSingleton<AutomationGraphOriginAuthority>();
        services.AddSingleton<IGraphPublicationOriginAuthority>(sp => sp.GetRequiredService<AutomationGraphOriginAuthority>());
        // Use only the existing trusted schema and explicitly configured bindings. Missing catalogue
        // remains an unavailable projection; this module never invents node/capability bindings.
        services.AddSingleton<NodeGraphAutomationAdapter>(sp => new(
            sp.GetRequiredService<NodeGraphSchemaRegistry>(), sp.GetServices<AutomationNodeGraphBinding>()));
        services.AddSingleton<AutomationGraphPublicationPreparer>();
        services.AddSingleton<HomeGraphPublicationResourceRegistry>();
        services.AddSingleton<ICanonicalResourceAccessResolver>(sp => sp.GetRequiredService<HomeGraphPublicationResourceRegistry>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeActionPolicySource, AutomationGraphPublicationActionPolicies>());
        services.AddSingleton<HomeGraphPublicationOwner>(sp =>
        {
            var store = sp.GetRequiredService<IHomeCoreStateStore>() as FileHomeCoreStateStore
                ?? throw new NotSupportedException("Actual local Home operation leases are required.");
            var profiles = sp.GetRequiredService<HomeLocalProfileIdentity>();
            var ownership = sp.GetRequiredService<HomeResourceStoreOwnershipAuthority>();
            if (!ReferenceEquals(sp.GetRequiredService<IAuthenticatedResourceActorSource>(), profiles) ||
                !ReferenceEquals(sp.GetRequiredService<IResourceStoreOwnershipAuthority>(), ownership))
                throw new NotSupportedException("The original canonical local Home actor and ownership graph are required.");
            var registry = sp.GetRequiredService<HomeGraphPublicationResourceRegistry>();
            var resolvers = sp.GetServices<ICanonicalResourceAccessResolver>().Where(r => r.ResourceKind == registry.ResourceKind).ToArray();
            if (resolvers.Length != 1 || !ReferenceEquals(resolvers[0], registry))
                throw new InvalidOperationException("The sole actual private graph resource registry is required.");
            return new(store, profiles, ownership, sp.GetRequiredService<HomeVersionedNodeGraphRepository>(),
                sp.GetRequiredService<HomeResourceOperationBroker>(), sp.GetRequiredService<HomePermissionTrustService>(),
                sp.GetServices<IGraphPublicationOriginAuthority>(), registry);
        });
        services.AddSingleton<HomeGraphSqlAssociationLeaseSource>(sp => new(
            sp.GetRequiredService<IHomeCoreStateStore>() as FileHomeCoreStateStore
                ?? throw new NotSupportedException("Actual local Home operation leases are required."),
            sp.GetRequiredService<HomeLocalProfileIdentity>(), sp.GetRequiredService<HomeResourceStoreOwnershipAuthority>(),
            sp.GetRequiredService<HomeResourceOperationBroker>(), sp.GetRequiredService<HomeGraphPublicationOwner>()));
        return services;
    }
    private static void RequireDefinitionOwner<T>(IServiceCollection services)
    {
        var rows = services.Where(d => d.ServiceType == typeof(T)).ToArray();
        if (rows.Length != 1 || rows[0].Lifetime != ServiceLifetime.Singleton || rows[0].ImplementationType != typeof(T))
            throw new InvalidOperationException("The canonical automation definition ownership module must be configured first.");
    }
}
