using Haven.Application;
using Haven.Application.Compatibility;
using Haven.Core.Media;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HavenOS.Files.NativeHost;

public static class FilesNativeHostComposition
{
    /// <summary>Add after the canonical Home/infrastructure services, before building the provider.
    /// Reuses that exact profile, ownership registry and resource broker; never creates another Home store.</summary>
    public static IServiceCollection AddFilesNativeHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(Registration))) return services;
        services.AddSingleton(new Registration());
        services.TryAddSingleton<NativeFilesWorkspaceService>();
        services.TryAddSingleton<NativeFilesWorkspaceAuthority>();
        services.TryAddSingleton<NativeFilesMediaAssetSourceResolver>();
        services.TryAddSingleton<NativeFilesArtifactContentReader>();
        services.TryAddSingleton<FilesCompatibilityPackageContentSource>();
        services.TryAddSingleton<ICompatibilityPackageContentSource>(provider => provider.GetRequiredService<FilesCompatibilityPackageContentSource>());
        services.AddSingleton<IMediaAssetSourceResolver>(provider => provider.GetRequiredService<NativeFilesMediaAssetSourceResolver>());
        services.AddSingleton<IMediaRetainedAssetSourceResolver>(provider => provider.GetRequiredService<NativeFilesMediaAssetSourceResolver>());
        services.AddSingleton<ICanonicalResourceAccessResolver>(provider => new FilesArtifactResourceResolver(async (actor, token) =>
        {
            var workspace = await provider.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(token).ConfigureAwait(false);
            return workspace?.Actor == actor ? workspace.Provider : null;
        }, async (actor, appId, token) =>
        {
            var workspace = await provider.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(token).ConfigureAwait(false);
            return workspace?.Actor == actor && workspace.Configuration.AppFolders.TryGetValue(appId, out var folder) ? folder : null;
        }));
        services.AddSingleton<IHomeLocalStoreEvidenceProvider>(provider => provider.GetRequiredService<NativeFilesWorkspaceService>());
        return services;
    }

    private sealed class Registration { }
}
