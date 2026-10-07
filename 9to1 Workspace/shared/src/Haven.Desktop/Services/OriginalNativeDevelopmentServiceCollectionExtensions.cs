using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Call after the original Home/infrastructure and Files host registrations,
/// before building their provider. This changes composition only; it issues no Task,
/// resource, setup, execution-trust or readiness authority.</summary>
public static class OriginalNativeDevelopmentServiceCollectionExtensions
{
    public static IServiceCollection AddHavenOriginalNativeDevelopment(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(Registration))) return services;

        // The maintained Files composition already registered its one browser. Do not
        // construct a second Files/Home owner or override an unknown host composition.
        var browsers = services.Select((item, index) => (item, index))
            .Where(pair => pair.item.ServiceType == typeof(FilesNativeBrowserService)).ToArray();
        if (browsers.Length != 1 || browsers[0].item.Lifetime != ServiceLifetime.Singleton
            || browsers[0].item.ImplementationType != typeof(FilesNativeBrowserService))
            throw new InvalidOperationException("Configure the original Files host once before its Dev composition.");
        Type[] newOwners = [typeof(FileDeveloperWorkspaceStore), typeof(IDeveloperWorkspaceStore),
            typeof(DeveloperCanonicalWorkspaceBinding), typeof(DeveloperTaskWorkspaceService),
            typeof(DeveloperProjectWorkbenchPageFactory)];
        if (services.Any(item => newOwners.Contains(item.ServiceType)))
            throw new InvalidOperationException("An existing Dev composition must be retained explicitly; parallel owners are refused.");

        services.AddSingleton<FileDeveloperWorkspaceStore>(provider => new(
            Path.Combine(provider.GetRequiredService<IAppPaths>().DataDirectory, "Dev")));
        services.AddSingleton<IDeveloperWorkspaceStore>(provider => provider.GetRequiredService<FileDeveloperWorkspaceStore>());
        services.AddSingleton<DeveloperCanonicalWorkspaceBinding>(provider => new(
            provider.GetRequiredService<IConversationRepository>(), provider.GetRequiredService<IContainerRepository>()));
        services.AddSingleton<DeveloperTaskWorkspaceService>(provider =>
        {
            var checkpoints = provider.GetRequiredService<CheckpointService>();
            if (!ReferenceEquals(checkpoints, provider.GetRequiredService<ICheckpointExecutionObservationSource>()))
                throw new InvalidOperationException("Dev must retain the same shared checkpoint producer observed by the canonical Task owner.");
            return new(provider.GetRequiredService<IDeveloperWorkspaceStore>(), provider.GetRequiredService<DeveloperCanonicalWorkspaceBinding>(),
                provider.GetRequiredService<TaskExecutionCoordinator>(), provider.GetRequiredService<ITaskRunToolActionOwner>(),
                provider.GetRequiredService<WorkspaceToolRuntime>(), provider.GetService<IDeveloperWorkspaceTrustService>(),
                checkpoints, provider.GetRequiredService<IConversationRepository>());
        });
        services[browsers[0].index] = ServiceDescriptor.Singleton<FilesNativeBrowserService>(provider => new(
            provider.GetRequiredService<NativeFilesWorkspaceAuthority>(), provider.GetRequiredService<IAuthenticatedResourceActorSource>(),
            provider.GetRequiredService<ResourceAuthorizationService>(), provider.GetRequiredService<ICompatibilityPackageContentSource>(),
            provider.GetRequiredService<FilesOriginalChildFolderReadSource>(), provider.GetRequiredService<FileDeveloperWorkspaceStore>(),
            provider.GetService<HomeResourceOperationBroker>()));
        services.AddSingleton<DeveloperProjectWorkbenchPageFactory>(provider => new(
            provider.GetRequiredService<DeveloperTaskWorkspaceService>(), provider.GetRequiredService<TaskExecutionCoordinator>(),
            provider.GetRequiredService<FilesNativeBrowserService>()));
        services.AddSingleton(new Registration());
        return services;
    }

    private sealed class Registration { }
}

// The host retains these exact configured instances and requests all owners before
// joining any. Closing a view does not retire either borrowed global business owner.
internal sealed class DeveloperTaskOriginalRetirementAdapter(DeveloperTaskWorkspaceService original)
    : IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    public void DemandExternalOriginalRetirementJoin() => original.DemandExternalOriginalRetirementJoin();
    public void RequestRetirement() => original.RequestRetirement();
    public Task CloseAndDrainAsync() => original.CloseAndDrainAsync();
}

internal sealed class FilesDeveloperOriginalRetirementAdapter(FilesNativeBrowserService original)
    : IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    public void DemandExternalOriginalRetirementJoin() => original.DemandExternalOriginalDeveloperReadJoin();
    public void RequestRetirement() => original.RequestOriginalDeveloperReadRetirement();
    public Task CloseAndDrainAsync() => original.CloseOriginalDeveloperReadsAsync();
}
