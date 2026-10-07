using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
namespace Haven.Desktop.Services;

public static class WindowsNativeColdProjectServiceCollectionExtensions
{
    /// <summary>After original Infra/cold/Windows Home/Files/Dev registrations, before Build.
    /// The domain must already contain the original current-project resolver/policy. They
    /// borrow the SAME source through this configured journal. Configuration is unverified;
    /// the real protected claim, current Files/native sources and manual READ remain required.</summary>
    public static IServiceCollection AddHavenOwnedNativeColdProjectResources(this IServiceCollection services,
        HomeNativeWindowsComposition sameHome)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(sameHome);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The genuine current project/native source requires Windows.");
        foreach (var type in new[] { typeof(HomeColdProjectReadReconciliation), typeof(ITaskRunColdProjectResourceSource),
            typeof(FilesDeveloperOriginalCurrentProjectSelection), typeof(IDeveloperOriginalCurrentProjectSelectionSource),
            typeof(IDeveloperOriginalCurrentProjectNativeSource), typeof(IDeveloperOriginalCurrentProjectReadAdmissionSource), typeof(IDeveloperOriginalProjectCommandReadSource), typeof(IDeveloperProjectOriginalWorkspaceMetadataStore) })
            if (services.Any(item => item.ServiceType == type))
                throw new InvalidOperationException("Original project resource configuration is single-owner and cannot replace existing sources.");
        var configuration = Single(services, typeof(NativePersonalTaskColdRecoveryConfiguration)).ImplementationInstance as NativePersonalTaskColdRecoveryConfiguration
            ?? throw new InvalidOperationException("Use the explicit original cold configuration before project resources.");
        if (configuration.OriginalStatus.Kind != NativePersonalTaskColdRecoveryConfigurationKind.RequestedUnverified)
            throw new NativePersonalTaskColdRecoverySetupRequiredException(configuration.OriginalStatus);
        void Same(Type type, object actual)
        {
            var row = Single(services, type);
            if (row.Lifetime != ServiceLifetime.Singleton || !ReferenceEquals(row.ImplementationInstance, actual))
                throw new InvalidOperationException("The SAME original Home instance alias is required: " + type.Name);
        }
        Same(typeof(HomeNativeWindowsComposition), sameHome); Same(typeof(FileHomeCoreStateStore), sameHome.StateStore);
        Same(typeof(HomeLocalProfileIdentity), sameHome.Profiles); Same(typeof(ResourceAuthorizationService), sameHome.Resources);
        Same(typeof(HomeResourceOperationBroker), sameHome.Broker);
        var actualFiles = sameHome.Services.GetService(typeof(NativeFilesWorkspaceAuthority)) as NativeFilesWorkspaceAuthority
            ?? throw new InvalidOperationException("The SAME precreated scoped Files authority must belong to this Home domain.");
        Same(typeof(NativeFilesWorkspaceAuthority), actualFiles);
        foreach (var type in new[] { typeof(FileDeveloperWorkspaceStore), typeof(IDeveloperWorkspaceStore), typeof(IContainerRepository),
            typeof(IWorkspaceToolService), typeof(SqliteDatabase), typeof(IAppPaths), typeof(HostLocalTaskActorSource), typeof(IDatabaseMaintenance) })
            if (Single(services, type).Lifetime != ServiceLifetime.Singleton)
                throw new InvalidOperationException("The actual configured project/repository/kernel prefix must be singleton: " + type.Name);
        var originalJournal = Single(services, typeof(SqliteTaskRunColdRecoveryJournal));
        var originalFactory = originalJournal.ImplementationFactory
            ?? throw new InvalidOperationException("Configure the original cold journal factory before project resources.");
        if (originalJournal.Lifetime != ServiceLifetime.Singleton) throw new InvalidOperationException("The configured journal must be singleton.");
        // This wraps the actual original factory and fixes its project source before exposing
        // that SAME journal to Authority/Coordinator/startup. Lazy Chat/current native sources
        // are not resolved here, and no journal/store operation or claim is run here.
        var index = services.IndexOf(originalJournal);
        services[index] = ServiceDescriptor.Singleton<SqliteTaskRunColdRecoveryJournal>(provider =>
        {
            var journal = originalFactory(provider) as SqliteTaskRunColdRecoveryJournal
                ?? throw new InvalidOperationException("The actual original factory returned no concrete journal.");
            var actors = provider.GetRequiredService<HostLocalTaskActorSource>();
            if (!journal.HasOriginalComposition(provider.GetRequiredService<SqliteDatabase>(), provider.GetRequiredService<IAppPaths>(), actors))
                throw new InvalidOperationException("The original journal requires the SAME configured database/paths/Task actor source.");
            var source = new HomeColdProjectReadReconciliation(sameHome.StateStore, sameHome.Profiles, sameHome.Resources,
                sameHome.Broker, sameHome.Permissions, journal, actors,
                () => provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectSelection>(),
                () => provider.GetRequiredService<IDeveloperOriginalCurrentProjectNativeSource>());
            journal.ConfigureOriginalProjectResourceSource(source);
            return journal;
        });
        services.AddSingleton<HomeColdProjectReadReconciliation>(provider =>
            provider.GetRequiredService<SqliteTaskRunColdRecoveryJournal>().RequireOriginalProjectResourceSource() as HomeColdProjectReadReconciliation
                ?? throw new InvalidOperationException("The SAME private original Home resource source is unavailable."));
        services.AddSingleton<ITaskRunColdProjectResourceSource>(provider => provider.GetRequiredService<HomeColdProjectReadReconciliation>());
        services.AddSingleton<IDeveloperOriginalCurrentProjectReadAdmissionSource>(provider => provider.GetRequiredService<HomeColdProjectReadReconciliation>());
        services.AddSingleton<IDeveloperOriginalProjectCommandReadSource>(provider => provider.GetRequiredService<HomeColdProjectReadReconciliation>());
        services.AddSingleton<IDeveloperProjectOriginalWorkspaceMetadataStore>(provider => provider.GetRequiredService<FileDeveloperWorkspaceStore>());
        services.AddSingleton<FilesDeveloperOriginalCurrentProjectSelection>(provider => new(actualFiles,
            provider.GetRequiredService<FileDeveloperWorkspaceStore>(), provider.GetRequiredService<IContainerRepository>()));
        services.AddSingleton<IDeveloperOriginalCurrentProjectSelectionSource>(provider => provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectSelection>());
        services.AddSingleton<IDeveloperOriginalCurrentProjectNativeSource>(provider =>
            (provider.GetRequiredService<IWorkspaceToolService>() as WorkspaceToolService
                ?? throw new InvalidOperationException("The SAME actual workspace kernel source is required.")).CreateOriginalCurrentProjectNativeSource(
            () => provider.GetRequiredService<FilesDeveloperOriginalCurrentProjectSelection>(),
            provider.GetRequiredService<HomeColdProjectReadReconciliation>(), provider.GetRequiredService<FileDeveloperWorkspaceStore>()));
        return services;
    }
    private static ServiceDescriptor Single(IServiceCollection services, Type type)
    {
        var rows = services.Where(item => item.ServiceType == type).Take(2).ToArray();
        return rows.Length == 1 ? rows[0] : throw new InvalidOperationException("One original configured descriptor is required: " + type.Name);
    }
}
