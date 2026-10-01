using System.Text.Json;
using HavenOS.Files;
using Haven.Application;
using Haven.Core.Media;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class FilesNativeHostCompositionTests
{
    [Fact]
    public async Task Native_apps_share_one_actual_Home_authority_graph_and_configuration_only_setup_view()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-files-nativehost-" + Guid.NewGuid().ToString("N"));
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
            services.AddFilesNativeHost();
            services.AddFilesNativeHost();
            using var provider = services.BuildServiceProvider();
            Assert.Same(home, provider.GetRequiredService<IHomeCoreStateStore>());
            Assert.Same(profiles, provider.GetRequiredService<IAuthenticatedResourceActorSource>());
            var files = provider.GetRequiredService<NativeFilesWorkspaceService>();
            Assert.Same(files, Assert.Single(provider.GetServices<IHomeLocalStoreEvidenceProvider>()));
            Assert.Single(provider.GetServices<ICanonicalResourceAccessResolver>());
            Assert.Same(provider.GetRequiredService<NativeFilesMediaAssetSourceResolver>(), provider.GetRequiredService<IMediaAssetSourceResolver>());
            Assert.Same(provider.GetRequiredService<NativeFilesMediaAssetSourceResolver>(), provider.GetRequiredService<IMediaRetainedAssetSourceResolver>());
            var authority = provider.GetRequiredService<NativeFilesWorkspaceAuthority>();
            Assert.Null(await authority.GetCurrentAsync(token));
            Assert.Null(await files.GetConfigurationAsync(token));
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            var configured = await files.ConfigureNewAsync(chosen, provider.GetRequiredService<HomeLocalStoreOwnership>(), token);
            var active = await authority.GetCurrentAsync(token);
            Assert.NotNull(active);
            Assert.Same(active.Provider, (await authority.GetCurrentAsync(token))!.Provider);
            var view = await files.GetConfigurationAsync(token);
            Assert.NotNull(view);
            Assert.Equal(configured.Configuration.StoreId, view.StoreId);
            Assert.NotSame(configured.Configuration.AppFolders, view.AppFolders);
            Assert.IsAssignableFrom<System.Collections.Frozen.FrozenDictionary<string, HavenOS.Files.HostedItemId>>(view.AppFolders);
            var folder = (await active.Provider.GetAsync(view.AppFolders["picture"], token)).Value!;
            var scope = new ResourceScope("files.item", folder.Id.ToString(), folder.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Write);
            var resources = provider.GetRequiredService<ResourceAuthorizationService>();
            Assert.Equal(active.Actor, await resources.AuthorizeAsync("picture.file.import", [scope], token));
            Assert.Null(await resources.AuthorizeAsync("picture.file.import", [scope with { Access = ResourceAccess.Read }], token));
            var guard = await authority.CaptureCommitAuthorityAsync(active.Actor, active.Provider, () => true, token);
            var snapshot = (await home.ReadAsync(token)).State!;
            var bindingRecord = Assert.Single(snapshot.Records, record => record.RecordType == "home.local-store-ownership");
            var binding = bindingRecord.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await home.WriteAsync(bindingRecord with { Revision = bindingRecord.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked-profile" }) }, bindingRecord.Revision, token)).IsSuccess);
            var statePath = Path.Combine(view.RootDirectory, ".9to1-files", "drive.json");
            var before = await File.ReadAllBytesAsync(statePath, token);
            var upload = new FilesUploadedContent(HostedItemId.New(), folder.Id, "denied.bin", "application/octet-stream",
                new(Guid.NewGuid()), null, active.Actor.ActorId, DateTimeOffset.UtcNow, 1, new string('a', 64), "denied.bin");
            var denied = await active.Provider.CommitUploadedContentAsync(upload,
                [new(folder.Id, folder.CurrentRevisionId)], guard, token);
            Assert.Equal(FilesErrorCode.PermissionDenied, denied.Error!.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(statePath, token));
            Assert.Null(await authority.GetCurrentAsync(token));
            Assert.NotNull(await files.GetConfigurationAsync(token)); // Details are not a provider/content grant.

        }
        finally { Directory.Delete(root, true); }
    }
}
