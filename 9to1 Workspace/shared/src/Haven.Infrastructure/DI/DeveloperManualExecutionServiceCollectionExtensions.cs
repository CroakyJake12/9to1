using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>The SAME lazy resolver and fixed policy must be installed BEFORE constructing Home.
/// Registers private manual execution custody only; creates no project, actor, trust or executor.</summary>
public static class DeveloperManualExecutionServiceCollectionExtensions
{
    public static IServiceCollection AddHavenOwnedDeveloperManualExecution(this IServiceCollection services,
        HomeNativeWindowsComposition sameHome, Func<IServiceProvider, IDeveloperProjectOriginalManualExecutionSource> actualFiles,
        HomeDeveloperManualProjectExecutionResourceResolver originalResolver, HomeDeveloperManualProjectExecutionActionPolicySource originalPolicy)
    {
        ArgumentNullException.ThrowIfNull(sameHome);
        Same(services, typeof(HomeNativeWindowsComposition), sameHome);
        return Register(services, sameHome.StateStore, sameHome.Profiles, sameHome.Broker, sameHome.Permissions,
            sameHome.Resources, actualFiles, originalResolver, originalPolicy);
    }
    public static IServiceCollection AddHavenOwnedDeveloperManualExecution(this IServiceCollection services,
        HomeLocalDomainComposition sameHome, Func<IServiceProvider, IDeveloperProjectOriginalManualExecutionSource> actualFiles,
        HomeDeveloperManualProjectExecutionResourceResolver originalResolver, HomeDeveloperManualProjectExecutionActionPolicySource originalPolicy)
    {
        ArgumentNullException.ThrowIfNull(sameHome);
        if (!OperatingSystem.IsLinux() || !sameHome.IsBoundToOriginalComposition(sameHome.StateStore, sameHome.Profiles,
            sameHome.Permissions, sameHome.Resources, sameHome.Ownership, sameHome.Broker))
            throw new InvalidOperationException("The original Linux Home domain tuple is required.");
        Same(services, typeof(HomeLocalDomainComposition), sameHome);
        return Register(services, sameHome.StateStore, sameHome.Profiles, sameHome.Broker, sameHome.Permissions,
            sameHome.Resources, actualFiles, originalResolver, originalPolicy);
    }
    private static IServiceCollection Register(IServiceCollection services, FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceOperationBroker broker, HomePermissionTrustService permissions, ResourceAuthorizationService resources,
        Func<IServiceProvider, IDeveloperProjectOriginalManualExecutionSource> actualFiles,
        HomeDeveloperManualProjectExecutionResourceResolver resolver, HomeDeveloperManualProjectExecutionActionPolicySource policy)
    {
        ArgumentNullException.ThrowIfNull(actualFiles); ArgumentNullException.ThrowIfNull(resolver); ArgumentNullException.ThrowIfNull(policy);
        if (!profiles.IsBoundToStore(store) || !resources.IsBoundToActorSource(profiles) || !broker.IsBoundToOriginalComposition(resources, permissions))
            throw new InvalidOperationException("SAME original Home manual execution composition is required.");
        Same(services, typeof(FileHomeCoreStateStore), store); Same(services, typeof(HomeLocalProfileIdentity), profiles);
        Same(services, typeof(HomeResourceOperationBroker), broker); Same(services, typeof(HomePermissionTrustService), permissions);
        Same(services, typeof(ResourceAuthorizationService), resources);
        if (services.Count(x => x.ServiceType == typeof(WorkspaceTaskRunEffectAuthority)) != 1 ||
            services.Any(x => x.ServiceType == typeof(HomeDeveloperManualProjectExecutionAdmissionSource) ||
                x.ServiceType == typeof(IDeveloperProjectOriginalManualExecutionAdmissionSource) ||
                x.ServiceType == typeof(IWorkspaceManualProcessFinalFenceValidator) || x.ServiceType == typeof(Func<IWorkspaceManualProcessFinalFenceValidator>) ||
                x.ServiceType == typeof(HomeDeveloperManualProjectExecutionResourceResolver) || x.ServiceType == typeof(HomeDeveloperManualProjectExecutionActionPolicySource)))
            throw new InvalidOperationException("One maintained effect authority and no previous manual process issuer registration are required.");
        services.AddSingleton(resolver); services.AddSingleton(policy);
        services.AddSingleton<HomeDeveloperManualProjectExecutionAdmissionSource>(provider => new(store, profiles, broker, permissions,
            () => actualFiles(provider) ?? throw new InvalidOperationException("The genuine Files manual process source is unavailable."),
            () => provider.GetRequiredService<IWorkspaceToolService>() as IWorkspaceOriginalInvocationSource
                ?? throw new NotSupportedException("The SAME maintained original workspace process owner is required.")));
        services.AddSingleton<IDeveloperProjectOriginalManualExecutionAdmissionSource>(provider => provider.GetRequiredService<HomeDeveloperManualProjectExecutionAdmissionSource>());
        services.AddSingleton<IWorkspaceManualProcessFinalFenceValidator>(provider => provider.GetRequiredService<HomeDeveloperManualProjectExecutionAdmissionSource>());
        services.AddSingleton<Func<IWorkspaceManualProcessFinalFenceValidator>>(provider => () => provider.GetRequiredService<HomeDeveloperManualProjectExecutionAdmissionSource>());
        return services;
    }
    private static void Same(IServiceCollection services, Type type, object same)
    {
        var rows = services.Where(x => x.ServiceType == type).Take(2).ToArray();
        if (rows.Length != 1 || rows[0].Lifetime != ServiceLifetime.Singleton || !ReferenceEquals(rows[0].ImplementationInstance, same))
            throw new InvalidOperationException("SAME original singleton alias required: " + type.Name);
    }
}
