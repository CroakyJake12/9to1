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
        services.TryAddSingleton<FilesNativeFolderReadSource>();
        services.TryAddSingleton<FilesNativeBrowserService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHomeActionPolicySource, FilesNativeBrowserActionPolicySource>());
        services.TryAddSingleton<FilesCompatibilityPackageContentSource>();
        services.TryAddSingleton<ICompatibilityPackageContentSource>(provider => provider.GetRequiredService<FilesCompatibilityPackageContentSource>());
        services.AddSingleton<IMediaAssetSourceResolver>(provider => provider.GetRequiredService<NativeFilesMediaAssetSourceResolver>());
        services.AddSingleton<IMediaRetainedAssetSourceResolver>(provider => provider.GetRequiredService<NativeFilesMediaAssetSourceResolver>());
        // One genuine owner instance serves both ordinary and privately captured original observations.
        services.TryAddSingleton<FilesArtifactResourceResolver>(provider =>
            new FilesArtifactResourceResolver(provider.GetRequiredService<NativeFilesWorkspaceAuthority>()));
        services.AddSingleton<ICanonicalResourceAccessResolver>(provider => provider.GetRequiredService<FilesArtifactResourceResolver>());
        services.TryAddSingleton<FilesOriginalChildFolderReadSource>();
        services.AddSingleton<IHomeLocalStoreEvidenceProvider>(provider => provider.GetRequiredService<NativeFilesWorkspaceService>());
        return services;
    }

    private sealed class Registration { }
}
