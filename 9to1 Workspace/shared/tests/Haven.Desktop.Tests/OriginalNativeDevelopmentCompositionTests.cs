using System.Reflection;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
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

    [Fact]
    public async Task Original_composition_keeps_same_Dev_and_Home_owners_and_refuses_foreign_browser_descriptors()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-original-files-dev-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var withBroker in new[] { true, false })
            {
                var services = new ServiceCollection();
                var home = new FileHomeCoreStateStore(Path.Combine(root, withBroker ? "with-home.json" : "without-home.json"));
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                services.AddSingleton<IAppPaths>(new OwnedPaths(Path.Combine(root, withBroker ? "with" : "without")));
                services.AddSingleton<IHomeCoreStateStore>(home); services.AddSingleton(profiles);
                services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
                services.AddSingleton(new HomePermissionTrustService(home, new FilesNativeBrowserActionPolicySource().TryGet));
                services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
                services.AddSingleton<HomeLocalStoreOwnership>();
                services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
                services.AddSingleton<ResourceAuthorizationService>();
                if (withBroker) services.AddSingleton<HomeResourceOperationBroker>();
                services.AddFilesNativeHost();
                Assert.Equal(typeof(FilesNativeBrowserService), services.Single(row => row.ServiceType == typeof(FilesNativeBrowserService)).ImplementationType);
                services.AddHavenOriginalNativeDevelopment();
                var configured = services.Single(row => row.ServiceType == typeof(FilesNativeBrowserService));
                services.AddHavenOriginalNativeDevelopment();
                Assert.Same(configured, services.Single(row => row.ServiceType == typeof(FilesNativeBrowserService)));
                await using var provider = services.BuildServiceProvider();
                var actual = provider.GetRequiredService<FilesNativeBrowserService>();
                Assert.Same(actual, provider.GetRequiredService<FilesNativeBrowserService>());
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var dev = provider.GetRequiredService<FileDeveloperWorkspaceStore>();
                Assert.Same(dev, provider.GetRequiredService<IDeveloperWorkspaceStore>());
                Assert.Same(dev, typeof(FilesNativeBrowserService).GetField("_originalDeveloperProjects", flags)!.GetValue(actual));
                Assert.Same(provider.GetService<HomeResourceOperationBroker>(),
                    typeof(FilesNativeBrowserService).GetField("_mutationBroker", flags)!.GetValue(actual));
                Assert.Equal(withBroker, actual.HasNativeMutationOwner);
                await actual.CloseOriginalDeveloperReadsAsync();
                // Same-owner construction is not a configured Files actor, Home approval,
                // native route, installed bootstrap or local model eligibility claim.
            }
            var foreign = new ServiceCollection();
            foreign.AddSingleton<FilesNativeBrowserService>(_ => throw new InvalidOperationException("Foreign factory must never run."));
            foreign.AddFilesNativeHost();
            Assert.Throws<InvalidOperationException>(() => foreign.AddHavenOriginalNativeDevelopment());
            var duplicate = new ServiceCollection();
            duplicate.AddFilesNativeHost(); duplicate.AddSingleton<FilesNativeBrowserService>();
            Assert.Throws<InvalidOperationException>(() => duplicate.AddHavenOriginalNativeDevelopment());
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class OwnedPaths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(root, "Attachments");
        public string LogsDirectory => Path.Combine(root, "Logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
