using Haven.Application;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
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
        if (!services.Any(x => x.ServiceType == typeof(TaskExecutionCoordinator)) ||
            !services.Any(x => x.ServiceType == typeof(McpConnectionClient)))
            throw new InvalidOperationException("Call after the reviewed AddHavenInfrastructure composition; use its SAME canonical Task and MCP owners.");
        if (services.Any(x => x.ServiceType == typeof(HomeCloudflareServiceOwner)))
            throw new InvalidOperationException("The original Home Cloudflare producer is already configured.");
        // Home composition must use this SAME lazy resolver and compiled action policy when
        // constructing its real ResourceAuthorizationService / HomePermissionTrustService.
        // A missing resolver/policy is a denied review, never a synthesized replacement graph.
        services.AddSingleton<HomeCloudflareResourceResolver>(provider => new(() => provider.GetRequiredService<HomeCloudflareServiceOwner>()));
        services.AddSingleton<HomeCloudflareActionPolicySource>();
        services.AddSingleton<CloudflareOriginalActionAdmissionSource>(provider => new(() => provider.GetRequiredService<TaskExecutionCoordinator>()));
        services.AddSingleton<ICloudflareMcpInvocationClient>(provider => provider.GetRequiredService<McpConnectionClient>());
        services.AddSingleton<ICloudflareWorkerBindingClient>(provider => provider.GetRequiredService<McpConnectionClient>());
        services.AddSingleton<HomeCloudflareServiceOwner>(provider =>
        {
            var home = actualHomeComponents(provider) ?? throw new InvalidOperationException("Actual installed Home composition is unavailable; configure it explicitly.");
            if (!home.Resources.IsBoundToActorSource(home.Profiles) || !home.Broker.IsBoundToOriginalComposition(home.Resources, home.Permissions))
                throw new UnauthorizedAccessException("SAME actual Home profile/resource/broker components required.");
            return new(home.StateStore, home.Profiles, home.Broker, home.Permissions,
                provider.GetRequiredService<IExternalConnectionRepository>(),
                provider.GetRequiredService<CloudflareOriginalActionAdmissionSource>(), provider.GetRequiredService<ICloudflareMcpInvocationClient>());
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
        services.AddSingleton<ITaskRunToolActionOwner>(provider => provider.GetRequiredService<CanonicalWorkspaceCloudflareToolActionOwner>());
        services.AddSingleton<ITaskRunActionReceiptAuthority>(provider => provider.GetRequiredService<CanonicalWorkspaceCloudflareReceiptAuthority>());
        return services;
    }
}
