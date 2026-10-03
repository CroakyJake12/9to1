using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class FilesOriginalOwnerCompositionTests
{
    [Fact]
    public async Task Actual_Files_registration_retains_one_original_issuer_and_ordinary_read_behavior_without_root_enrollment()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-files-original-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var services = new ServiceCollection();
            services.AddSingleton<IHomeCoreStateStore>(home);
            services.AddSingleton(profiles);
            services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
            services.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
            services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
            services.AddSingleton<HomeLocalStoreOwnership>();
            services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
            services.AddSingleton<ResourceAuthorizationService>();
            services.AddFilesNativeHost(); services.AddFilesNativeHost();
            using var graph = services.BuildServiceProvider();
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            var workspace = await graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
            var authority = graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
            workspace = await authority.GetCurrentAsync(workspace.Configuration.StoreId, token)
                ?? throw new InvalidOperationException("Actual configured Files required.");
            var issuer = graph.GetRequiredService<FilesArtifactResourceResolver>();
            Assert.Same(issuer, Assert.Single(graph.GetServices<ICanonicalResourceAccessResolver>()));
            Assert.Same(issuer, graph.GetRequiredService<FilesArtifactResourceResolver>());
            Assert.NotNull(graph.GetRequiredService<FilesOriginalChildFolderReadSource>());
            Assert.NotNull(graph.GetRequiredService<FilesNativeBrowserService>());
            var resources = graph.GetRequiredService<ResourceAuthorizationService>();
            Assert.True(resources.IsBoundToActorSource(profiles));
            var file = HostedItemId.New();
            Assert.True((await workspace.Provider.RegisterArtifactAsync(new("canvas", Guid.NewGuid().ToString("N"), file,
                workspace.Configuration.AppFolders["canvas"], nameof(FilesArtifactType.Canvas), "Original.9to1c"),
                workspace.Actor.ActorId, token)).IsSuccess);
            var metadata = (await workspace.Provider.GetAsync(file, token)).Value!;
            var scope = new ResourceScope("files.item", file.ToString(), metadata.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Read);
            var beforeHome = await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token);
            var drivePath = Path.Combine(workspace.Configuration.RootDirectory, ".9to1-files", "drive.json");
            var beforeDrive = await File.ReadAllBytesAsync(drivePath, token);
            Assert.Equal(workspace.Actor, await resources.AuthorizeForActorAsync(workspace.Actor, "canvas.file.open", [scope], token));
            var original = await issuer.CaptureOriginalCanvasReadAsync(workspace, file, () => true, token);
            Assert.Equal(scope, original.OriginalScope);
            Assert.Equal(workspace.Actor, await resources.AuthorizeOriginalReadForActorAsync(workspace.Actor, original,
                "canvas.file.open", [scope], token));
            Assert.False(workspace.Configuration.AppFolders.ContainsKey("stacks"));
            Assert.Equal(beforeHome, await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token));
            Assert.Equal(beforeDrive, await File.ReadAllBytesAsync(drivePath, token));
        }
        finally { Directory.Delete(root, true); }
    }
}
