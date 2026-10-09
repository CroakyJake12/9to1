using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Haven.Infrastructure;

/// <summary>Explicit trusted Home host composition only. AddHavenInfrastructure keeps its
/// ordinary Workspace owner unless this method is called with actual installed Home components.
/// This installs no profile, publisher, account, OAuth credential or permission grant.</summary>
public static class CloudflareTaskToolServiceCollectionExtensions
{
    public static IServiceCollection AddHavenOwnedCloudflareTaskTools(this IServiceCollection services,
        Func<IServiceProvider, HomeNativeWindowsOwnerComponents> actualHomeComponents)
    {
        ArgumentNullException.ThrowIfNull(actualHomeComponents);
        return AddOriginalCore(services, actualHomeComponents, null, null, null);
    }
    internal static IServiceCollection AddHavenOwnedCloudflareTaskTools(this IServiceCollection services,
        CloudflareNativeHostRegistration actualRegistration, HomeCloudflareResourceResolver sameResolver, HomeCloudflareActionPolicySource samePolicy)
        => AddOriginalCore(services, actualRegistration.RequireOriginalHomeComponents, sameResolver, samePolicy, actualRegistration);
    private static IServiceCollection AddOriginalCore(IServiceCollection services,
        Func<IServiceProvider, HomeNativeWindowsOwnerComponents> actualHomeComponents,
        HomeCloudflareResourceResolver? sameResolver, HomeCloudflareActionPolicySource? samePolicy,
        CloudflareNativeHostRegistration? registration)
    {
        if (!services.Any(x => x.ServiceType == typeof(TaskExecutionCoordinator)) ||
            !services.Any(x => x.ServiceType == typeof(McpConnectionClient)))
            throw new InvalidOperationException("Call after the reviewed AddHavenInfrastructure composition; use its SAME canonical Task and MCP owners.");
        if (services.Any(x => x.ServiceType == typeof(HomeCloudflareServiceOwner)))
            throw new InvalidOperationException("The original Home Cloudflare producer is already configured.");
        ServiceDescriptor RequireOriginalSingleton(Type type)
        {
            var rows = services.Where(row => row.ServiceType == type).Take(2).ToArray();
            if (rows.Length != 1 || rows[0].Lifetime != ServiceLifetime.Singleton)
                throw new InvalidOperationException("One SAME maintained original singleton is required before Cloudflare composition: " + type.Name);
            return rows[0];
        }
        RequireOriginalSingleton(typeof(WorkspaceTaskRunToolActionOwner));
        RequireOriginalSingleton(typeof(WorkspaceTaskRunReceiptAuthority));
        var originalToolAlias = RequireOriginalSingleton(typeof(ITaskRunToolActionOwner));
        var originalReceiptAlias = RequireOriginalSingleton(typeof(ITaskRunActionReceiptAuthority));
        var originalToolFactory = originalToolAlias.ImplementationFactory
            ?? throw new InvalidOperationException("The maintained original Workspace tool alias factory is required.");
        var originalReceiptFactory = originalReceiptAlias.ImplementationFactory
            ?? throw new InvalidOperationException("The maintained original Workspace receipt alias factory is required.");
        // Home composition must use this SAME lazy resolver and compiled action policy when
        // constructing its real ResourceAuthorizationService / HomePermissionTrustService.
        // A missing resolver/policy is a denied review, never a synthesized replacement graph.
        if (sameResolver is null) services.AddSingleton<HomeCloudflareResourceResolver>(provider => new(() => provider.GetRequiredService<HomeCloudflareServiceOwner>()));
        else services.AddSingleton(sameResolver);
        if (samePolicy is null) services.AddSingleton<HomeCloudflareActionPolicySource>();
        else services.AddSingleton(samePolicy);
        services.AddSingleton<CloudflareOriginalActionAdmissionSource>(provider => new(() => provider.GetRequiredService<TaskExecutionCoordinator>()));
        services.AddSingleton<ICloudflareMcpInvocationClient>(provider => provider.GetRequiredService<McpConnectionClient>());
        services.AddSingleton<ICloudflareWorkerBindingClient>(provider => provider.GetRequiredService<McpConnectionClient>());
        services.AddSingleton<HomeCloudflareServiceOwner>(provider =>
        {
            var home = actualHomeComponents(provider) ?? throw new InvalidOperationException("Actual installed Home composition is unavailable; configure it explicitly.");
            if (!home.Resources.IsBoundToActorSource(home.Profiles) || !home.Broker.IsBoundToOriginalComposition(home.Resources, home.Permissions))
                throw new UnauthorizedAccessException("SAME actual Home profile/resource/broker components required.");
            var acquired = new HomeCloudflareServiceOwner(home.StateStore, home.Profiles, home.Broker, home.Permissions,
                provider.GetRequiredService<IExternalConnectionRepository>(),
                provider.GetRequiredService<CloudflareOriginalActionAdmissionSource>(), provider.GetRequiredService<ICloudflareMcpInvocationClient>());
            registration?.RetainConstructedOriginalOwner(acquired); return acquired;
        });
        services.AddSingleton<ICloudflareStagingDelegationSource>(provider => provider.GetRequiredService<HomeCloudflareServiceOwner>());
        services.AddSingleton<ICloudflareProductionSetupSource>(provider => provider.GetRequiredService<HomeCloudflareServiceOwner>());
        services.AddSingleton<ICloudflareKnownCreateReconciliationSource>(provider => provider.GetRequiredService<HomeCloudflareServiceOwner>());
        services.AddSingleton<ICloudflareKnownCreateRecoveryRetirementSource>(provider => provider.GetRequiredService<HomeCloudflareServiceOwner>());
        services.AddSingleton<ICloudflareSavedServiceSource>(provider => provider.GetRequiredService<HomeCloudflareServiceOwner>());
        services.AddSingleton<ICloudflareOriginalNamespaceOwner>(provider => provider.GetRequiredService<HomeCloudflareServiceOwner>());
        services.AddSingleton<ICloudflareOriginalPermissionSource>(provider => provider.GetRequiredService<HomeCloudflareServiceOwner>());
        services.AddSingleton<CloudflareTypedToolRuntime>(provider => new(
            () => provider.GetRequiredService<TaskExecutionCoordinator>(), provider.GetRequiredService<ICloudflareSavedServiceSource>(),
            provider.GetRequiredService<ICloudflareOriginalPermissionSource>(), provider.GetRequiredService<CloudflareOriginalActionAdmissionSource>(),
            provider.GetRequiredService<ICloudflareMcpInvocationClient>()));
        services.AddSingleton<CloudflareTaskRunReceiptAuthority>();
        services.AddSingleton<CloudflareTaskRunToolActionOwner>(provider => new(
            () => provider.GetRequiredService<TaskExecutionCoordinator>(), () => provider.GetRequiredService<TaskRunPermissionAuthority>(),
            provider.GetRequiredService<ITaskRunOriginalFrameOwner>(), provider.GetRequiredService<CloudflareTypedToolRuntime>(),
            provider.GetRequiredService<ICloudflareSavedServiceSource>(), provider.GetRequiredService<ICloudflareOriginalNamespaceOwner>(),
            provider.GetRequiredService<CloudflareOriginalActionAdmissionSource>(), provider.GetRequiredService<CloudflareTaskRunReceiptAuthority>()));
        services.AddSingleton<CanonicalWorkspaceCloudflareToolActionOwner>();
        services.AddSingleton<CanonicalWorkspaceCloudflareReceiptAuthority>();
        services.Replace(ServiceDescriptor.Singleton<ITaskRunToolActionOwner>(provider =>
        {
            if (!ReferenceEquals(originalToolFactory(provider), provider.GetRequiredService<WorkspaceTaskRunToolActionOwner>()))
                throw new UnauthorizedAccessException("The original Workspace tool alias must resolve to the SAME configured concrete owner.");
            return provider.GetRequiredService<CanonicalWorkspaceCloudflareToolActionOwner>();
        }));
        services.Replace(ServiceDescriptor.Singleton<ITaskRunActionReceiptAuthority>(provider =>
        {
            if (!ReferenceEquals(originalReceiptFactory(provider), provider.GetRequiredService<WorkspaceTaskRunReceiptAuthority>()))
                throw new UnauthorizedAccessException("The original Workspace receipt alias must resolve to the SAME configured concrete authority.");
            return provider.GetRequiredService<CanonicalWorkspaceCloudflareReceiptAuthority>();
        }));
        return services;
    }
}
