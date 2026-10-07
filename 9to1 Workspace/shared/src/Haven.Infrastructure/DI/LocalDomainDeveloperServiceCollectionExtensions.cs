using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure;

/// <summary>Explicit in-process Linux Home DOMAIN counterparts. Callers must place the
/// exact source/destination resolvers and fixed policies in the domain before construction.
/// Registration fixes source custody only; actual Files/private captures, manual reviews,
/// native currentness and held entries remain mandatory. Windows overloads are unchanged.</summary>
public static class LocalDomainDeveloperServiceCollectionExtensions
{
    public static IServiceCollection AddHavenOwnedDeveloperSourceReads(this IServiceCollection services,
        HomeLocalDomainComposition sameHome,
        Func<IServiceProvider, IDeveloperProjectOriginalPhysicalReadSelectionSource> actualSelections,
        HomeDeveloperProjectReadResourceResolver originalResolver,
        HomeDeveloperProjectReadActionPolicySource originalPolicy)
    {
        ArgumentNullException.ThrowIfNull(actualSelections); ArgumentNullException.ThrowIfNull(originalResolver);
        ArgumentNullException.ThrowIfNull(originalPolicy); RequireDomain(services, sameHome);
        RequireAbsent(services, typeof(HomeDeveloperProjectReadAdmissionSource), typeof(IDeveloperProjectOriginalReadAdmissionSource),
            typeof(IDeveloperProjectOriginalReadRetirementSource), typeof(IDeveloperProjectOriginalReadAdmissionJoinGuard),
            typeof(HomeDeveloperProjectReadResourceResolver), typeof(HomeDeveloperProjectReadActionPolicySource));
        services.AddSingleton(originalResolver); services.AddSingleton(originalPolicy);
        services.AddSingleton<HomeDeveloperProjectReadAdmissionSource>(provider =>
            new(sameHome.StateStore, sameHome.Profiles, sameHome.Broker, sameHome.Permissions,
                () => actualSelections(provider) ?? throw new InvalidOperationException("The actual Files source selection owner is unavailable.")));
        services.AddSingleton<IDeveloperProjectOriginalReadAdmissionSource>(provider => provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>());
        services.AddSingleton<IDeveloperProjectOriginalReadRetirementSource>(provider => provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>());
        services.AddSingleton<IDeveloperProjectOriginalReadAdmissionJoinGuard>(provider => provider.GetRequiredService<HomeDeveloperProjectReadAdmissionSource>());
        return services;
    }

    public static IServiceCollection AddHavenOwnedDeveloperSetups(this IServiceCollection services,
        HomeLocalDomainComposition sameHome,
        Func<IServiceProvider, IDeveloperProjectOriginalSetupScopeSource> actualScopes,
        Func<IServiceProvider, IDeveloperProjectOriginalCaptureAuthority> actualCaptures,
        Func<IServiceProvider, IDeveloperProjectOriginalSetupStepOutcomeSource> actualOutcomes,
        HomeDeveloperProjectSetupActionPolicySource originalPolicy)
    {
        ArgumentNullException.ThrowIfNull(actualScopes); ArgumentNullException.ThrowIfNull(actualCaptures);
        ArgumentNullException.ThrowIfNull(actualOutcomes); ArgumentNullException.ThrowIfNull(originalPolicy);
        RequireDomain(services, sameHome);
        RequireAbsent(services, typeof(HomeDeveloperProjectSetupPermissionSource), typeof(IDeveloperProjectOriginalSetupPermissionSource),
            typeof(HomeDeveloperProjectSetupJournal), typeof(IDeveloperProjectOriginalSetupCompletionSource),
            typeof(HomeDeveloperProjectSetupActionPolicySource));
        services.AddSingleton(originalPolicy);
        services.AddSingleton<HomeDeveloperProjectSetupJournal>(provider =>
            new(sameHome.StateStore, sameHome.Profiles,
                actualCaptures(provider) ?? throw new InvalidOperationException("The SAME actual Files capture authority is unavailable."),
                () => actualOutcomes(provider) ?? throw new InvalidOperationException("The SAME actual Files step outcome source is unavailable.")));
        services.AddSingleton<IDeveloperProjectOriginalSetupCompletionSource>(provider => provider.GetRequiredService<HomeDeveloperProjectSetupJournal>());
        services.AddSingleton<HomeDeveloperProjectSetupPermissionSource>(provider =>
            new(sameHome.StateStore, sameHome.Profiles, sameHome.Broker, sameHome.Permissions,
                () => actualScopes(provider) ?? throw new InvalidOperationException("The SAME Files destination source is unavailable."),
                () => actualCaptures(provider) ?? throw new InvalidOperationException("The SAME Files capture authority is unavailable."),
                () => actualOutcomes(provider) ?? throw new InvalidOperationException("The SAME Files outcome source is unavailable."),
                () => provider.GetRequiredService<HomeDeveloperProjectSetupJournal>()));
        services.AddSingleton<IDeveloperProjectOriginalSetupPermissionSource>(provider => provider.GetRequiredService<HomeDeveloperProjectSetupPermissionSource>());
        return services;
    }

    public static IServiceCollection AddHavenOwnedDeveloperExecutionConsent(this IServiceCollection services,
        HomeLocalDomainComposition sameHome,
        Func<IServiceProvider, IDeveloperWorkspaceOriginalExecutionBindingSource> actualBindings,
        HomeDeveloperWorkspaceExecutionActionPolicySource originalPolicy)
    {
        ArgumentNullException.ThrowIfNull(actualBindings); ArgumentNullException.ThrowIfNull(originalPolicy); RequireDomain(services, sameHome);
        RequireAbsent(services, typeof(HomeDeveloperWorkspaceExecutionConsentSource), typeof(IWorkspaceOriginalProcessStartConsentSource),
            typeof(Func<IWorkspaceOriginalProcessStartConsentSource>), typeof(HomeDeveloperWorkspaceExecutionActionPolicySource));
        if (services.Count(item => item.ServiceType == typeof(WorkspaceTaskRunEffectAuthority)) != 1)
            throw new InvalidOperationException("One actual Workspace effect authority must already be configured.");
        services.AddSingleton(originalPolicy);
        services.AddSingleton<HomeDeveloperWorkspaceExecutionConsentSource>(provider =>
            new(sameHome.StateStore, sameHome.Profiles, sameHome.Broker, sameHome.Permissions,
                () => actualBindings(provider) ?? throw new InvalidOperationException("The actual private Files execution binding source is unavailable."),
                () => provider.GetRequiredService<ITaskRunToolActionOwner>()));
        services.AddSingleton<IWorkspaceOriginalProcessStartConsentSource>(provider => provider.GetRequiredService<HomeDeveloperWorkspaceExecutionConsentSource>());
        services.AddSingleton<Func<IWorkspaceOriginalProcessStartConsentSource>>(provider => () => provider.GetRequiredService<HomeDeveloperWorkspaceExecutionConsentSource>());
        return services;
    }

    private static void RequireAbsent(IServiceCollection services, params Type[] types)
    {
        if (services.Any(row => types.Contains(row.ServiceType)))
            throw new InvalidOperationException("The original local Home developer owners cannot replace existing issuer aliases.");
    }
    private static void RequireDomain(IServiceCollection services, HomeLocalDomainComposition sameHome)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(sameHome);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This composition uses the actual in-process Linux Home domain.");
        if (!sameHome.IsBoundToOriginalComposition(sameHome.StateStore, sameHome.Profiles, sameHome.Permissions,
            sameHome.Resources, sameHome.Ownership, sameHome.Broker))
            throw new InvalidOperationException("The SAME original Home domain tuple is required.");
        void Same(Type type, object actual)
        {
            var rows = services.Where(row => row.ServiceType == type).Take(2).ToArray();
            if (rows.Length != 1 || rows[0].Lifetime != ServiceLifetime.Singleton || !ReferenceEquals(rows[0].ImplementationInstance, actual))
                throw new InvalidOperationException("The exact original domain singleton alias is required: " + type.Name);
        }
        Same(typeof(HomeLocalDomainComposition), sameHome); Same(typeof(FileHomeCoreStateStore), sameHome.StateStore);
        Same(typeof(HomeLocalProfileIdentity), sameHome.Profiles); Same(typeof(HomePermissionTrustService), sameHome.Permissions);
        Same(typeof(ResourceAuthorizationService), sameHome.Resources); Same(typeof(HomeResourceOperationBroker), sameHome.Broker);
    }
}
