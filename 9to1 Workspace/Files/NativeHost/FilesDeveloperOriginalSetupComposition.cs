using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Files.NativeHost;

/// <summary>The single destination resolver included in the actual Home domain before
/// Resources is constructed. Its lazy source must be the SAME configured Files setup
/// owner. This forwarder creates no root, actor, approval, selection, scope or receipt.</summary>
public sealed class FilesDeveloperOriginalSetupDestinationResolver(Func<FilesDeveloperOriginalSetupScopeSource> originalSource)
    : ICanonicalResourceAccessResolver
{
    public string ResourceKind => "dev.project.destination";
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope, CancellationToken token)
    {
        var actual = originalSource() ?? throw new InvalidOperationException("The SAME configured Files destination owner is unavailable.");
        return actual.EvaluateAsync(actor, actionId, scope, token);
    }
}

/// <summary>Explicit configured Files/kernel/Dev aliases for the maintained register-existing
/// producer. Add before Build, alongside the local Home composition overloads. Every lazy
/// dependency resolves the SAME singleton; no Windows owner tuple or fallback store is made.</summary>
public static class FilesDeveloperOriginalSetupComposition
{
    public static IServiceCollection AddFilesOriginalDeveloperSetups(this IServiceCollection services,
        FilesDeveloperOriginalSetupDestinationResolver originalDestinationResolver,
        Func<IServiceProvider, IDeveloperProjectOriginalPhysicalCaptureSource> originalPhysicalSource,
        Func<IServiceProvider, HomeDeveloperProjectSetupJournal> originalJournal,
        Func<IServiceProvider, IDeveloperProjectOriginalReadAdmissionSource> originalReads,
        Func<IServiceProvider, IDeveloperProjectOriginalSetupPermissionSource> originalPermissions,
        Func<IServiceProvider, FileDeveloperWorkspaceStore> originalWorkspaceStore)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(originalDestinationResolver);
        ArgumentNullException.ThrowIfNull(originalPhysicalSource); ArgumentNullException.ThrowIfNull(originalJournal);
        ArgumentNullException.ThrowIfNull(originalReads); ArgumentNullException.ThrowIfNull(originalPermissions);
        ArgumentNullException.ThrowIfNull(originalWorkspaceStore);
        Type[] owned = [typeof(FilesDeveloperOriginalSourceSelection), typeof(IDeveloperProjectOriginalPhysicalCaptureSource),
            typeof(IDeveloperProjectOriginalPhysicalReadSelectionSource), typeof(IDeveloperProjectOriginalCaptureAuthority),
            typeof(FilesDeveloperOriginalSetupScopeSource), typeof(IDeveloperProjectOriginalSetupScopeSource),
            typeof(FilesDeveloperOriginalFolderSetupProducer), typeof(IDeveloperProjectOriginalSetupStepOutcomeSource),
            typeof(IDeveloperProjectOriginalDirectoryObservationSource), typeof(IDeveloperProjectOriginalDirectoryRegistrationSource),
            typeof(IDeveloperProjectOriginalFileRegistrationSource), typeof(IDeveloperProjectOriginalWorkspaceMetadataSource),
            typeof(FilesDeveloperOriginalSetupDestinationResolver)];
        if (services.Any(row => owned.Contains(row.ServiceType)))
            throw new InvalidOperationException("An original Files setup/source owner or alias is already configured.");
        services.AddSingleton(originalDestinationResolver);
        if (!services.Any(row => row.ServiceType == typeof(ICanonicalResourceAccessResolver) &&
            ReferenceEquals(row.ImplementationInstance, originalDestinationResolver)))
            services.AddSingleton<ICanonicalResourceAccessResolver>(originalDestinationResolver);
        services.AddSingleton<IDeveloperProjectOriginalPhysicalCaptureSource>(provider =>
            originalPhysicalSource(provider) ?? throw new InvalidOperationException("The actual original kernel source is unavailable."));
        services.AddSingleton<IDeveloperProjectOriginalDirectoryObservationSource>(provider =>
            RequirePort<IDeveloperProjectOriginalDirectoryObservationSource>(provider));
        services.AddSingleton<IDeveloperProjectOriginalDirectoryRegistrationSource>(provider =>
            RequirePort<IDeveloperProjectOriginalDirectoryRegistrationSource>(provider));
        services.AddSingleton<IDeveloperProjectOriginalFileRegistrationSource>(provider =>
            RequirePort<IDeveloperProjectOriginalFileRegistrationSource>(provider));
        services.AddSingleton<IDeveloperProjectOriginalWorkspaceMetadataSource>(provider =>
            RequirePort<IDeveloperProjectOriginalWorkspaceMetadataSource>(provider));
        services.AddSingleton<FilesDeveloperOriginalSourceSelection>(provider => new(
            provider.GetRequiredService<NativeFilesWorkspaceAuthority>(),
            provider.GetRequiredService<IDeveloperProjectOriginalPhysicalCaptureSource>(), originalReads(provider)));
        services.AddSingleton<IDeveloperProjectOriginalPhysicalReadSelectionSource>(provider =>
            provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>());
        services.AddSingleton<IDeveloperProjectOriginalCaptureAuthority>(provider =>
            provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>());
        services.AddSingleton<FilesDeveloperOriginalSetupScopeSource>(provider => new(
            provider.GetRequiredService<NativeFilesWorkspaceAuthority>(),
            provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(), () => originalJournal(provider)));
        services.AddSingleton<IDeveloperProjectOriginalSetupScopeSource>(provider =>
            provider.GetRequiredService<FilesDeveloperOriginalSetupScopeSource>());
        services.AddSingleton<FilesDeveloperOriginalFolderSetupProducer>(provider => new(
            provider.GetRequiredService<FilesDeveloperOriginalSetupScopeSource>(), () => originalJournal(provider),
            () => originalPermissions(provider),
            () => provider.GetRequiredService<IDeveloperProjectOriginalDirectoryObservationSource>(),
            () => originalWorkspaceStore(provider)));
        services.AddSingleton<IDeveloperProjectOriginalSetupStepOutcomeSource>(provider =>
            provider.GetRequiredService<FilesDeveloperOriginalFolderSetupProducer>());
        return services;
    }
    private static T RequirePort<T>(IServiceProvider provider) where T : class =>
        provider.GetRequiredService<IDeveloperProjectOriginalPhysicalCaptureSource>() as T
        ?? throw new NotSupportedException($"The SAME configured kernel source lacks {typeof(T).Name}; original setup refuses.");
}
