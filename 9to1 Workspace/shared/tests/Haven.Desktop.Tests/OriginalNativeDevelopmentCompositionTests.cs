using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class OriginalNativeDevelopmentCompositionTests
{
    [Fact]
    public void Original_composition_resolves_one_durable_store_and_preserves_the_configured_host_descriptors()
    {
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        services.AddFilesNativeHost();
        var originalAuthority = services.Single(item => item.ServiceType == typeof(NativeFilesWorkspaceAuthority));
        var originalFolders = services.Single(item => item.ServiceType == typeof(FilesOriginalChildFolderReadSource));
        services.AddHavenOriginalNativeDevelopment();
        var browser = services.Single(item => item.ServiceType == typeof(FilesNativeBrowserService));
        var factory = services.Single(item => item.ServiceType == typeof(DeveloperProjectWorkbenchPageFactory));
        services.AddHavenOriginalNativeDevelopment();
        Assert.Same(browser, services.Single(item => item.ServiceType == typeof(FilesNativeBrowserService)));
        Assert.Same(factory, services.Single(item => item.ServiceType == typeof(DeveloperProjectWorkbenchPageFactory)));
        Assert.Same(originalAuthority, services.Single(item => item.ServiceType == typeof(NativeFilesWorkspaceAuthority)));
        Assert.Same(originalFolders, services.Single(item => item.ServiceType == typeof(FilesOriginalChildFolderReadSource)));
        Assert.Equal(ServiceLifetime.Singleton, browser.Lifetime);
        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<FileDeveloperWorkspaceStore>(), provider.GetRequiredService<IDeveloperWorkspaceStore>());
        Assert.Null(provider.GetService<IDeveloperWorkspaceTrustService>());
        // Resolving a store/descriptor does not assert a configured Home actor, page,
        // source selection, command trust, actual Files read, native rendering or Ready.
    }
}
