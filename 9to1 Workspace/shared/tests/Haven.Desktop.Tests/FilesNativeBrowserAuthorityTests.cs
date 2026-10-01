using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class FilesNativeBrowserAuthorityTests
{
    [Fact]
    public async Task Actual_Home_bound_browser_and_folder_read_lease_retain_original_actor_and_deny_stale_or_revoked_sources()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-files-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var registrations = new ServiceCollection();
            registrations.AddSingleton<IHomeCoreStateStore>(home);
            registrations.AddSingleton(profiles);
            registrations.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
            registrations.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
            registrations.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
            registrations.AddSingleton<HomeLocalStoreOwnership>();
            registrations.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
            registrations.AddSingleton<ResourceAuthorizationService>();
            registrations.AddFilesNativeHost();
            using var graph = registrations.BuildServiceProvider();
            var original = (await profiles.GetCurrentAsync(token))!;
            var browser = graph.GetRequiredService<FilesNativeBrowserService>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => browser.ListAsync(original, token: token));
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            await graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
            var workspace = (await graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(token))!;
            var page = await browser.ListAsync(original, token: token);
            Assert.Equal(workspace.Configuration.StoreId, page.StoreID);
            Assert.Equal(workspace.Configuration.AppFolders.Count, page.Items.Count);
            await browser.RevalidateAsync(page, original, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => browser.ListAsync(
                original with { ActorId = "different-original-session" }, token: token));
            var sites = (await workspace.Provider.GetAsync(workspace.Configuration.AppFolders["sites"], token)).Value!;
            var directory = (await workspace.Directories.ResolveProfileAsync(Guid.Parse(original.ProfileId), "sites", token)).Value!.DirectoryPath;
            var folders = graph.GetRequiredService<FilesNativeFolderReadSource>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => folders.ReadAsync("stacks", sites.Id.Value,
                sites.CurrentRevisionId!.Value.ToString(), original, token));
            // Trusted Files setup registers this preexisting materialisation, never a path from a browser/RPC caller.
            Assert.True((await workspace.Directories.RegisterProfileAsync(Guid.Parse(original.ProfileId), sites.Id,
                "stacks", directory, token)).IsSuccess);
            using var lease = await folders.ReadAsync("stacks", sites.Id.Value,
                sites.CurrentRevisionId!.Value.ToString(), original, token);
            Assert.Equal(directory, lease.DirectoryPath);
            Assert.Equal(original, lease.Source.ObservedActor);
            await lease.RevalidateAsync(token);
            var scope = new ResourceScope("files.item", sites.Id.ToString(), sites.CurrentRevisionId.Value.ToString(), ResourceAccess.Read);
            var resources = graph.GetRequiredService<ResourceAuthorizationService>();
            Assert.Equal(original, await resources.AuthorizeAsync("files.folder.native-root.read", [scope], token));
            Assert.Null(await resources.AuthorizeAsync("files.folder.native-root.read", [scope with { Access = ResourceAccess.Write }], token));
            Assert.Null(await resources.AuthorizeAsync("stacks.source.commit", [scope with { Access = ResourceAccess.Write }], token));
            var now = DateTimeOffset.UtcNow;
            var rename = new FilesOperation(new(Guid.NewGuid()), original.ActorId, sites.Id, sites.ParentId, sites.ParentId,
                "Rename", sites.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null);
            Assert.True((await workspace.Provider.MutateAsync(rename, "renamed-sites", token)).IsSuccess);
            await Assert.ThrowsAsync<InvalidOperationException>(() => browser.RevalidateAsync(page, original, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lease.RevalidateAsync(token));
            var refreshed = await browser.ListAsync(original, token: token);
            Assert.Contains(refreshed.Items, item => item.Id == sites.Id && item.Name == "renamed-sites");
            var record = Assert.Single((await home.ReadAsync(token)).State!.Records,
                item => item.RecordType == "home.local-store-ownership");
            var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await home.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked" }) }, record.Revision, token)).IsSuccess);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => browser.ListAsync(original, token: token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => browser.RevalidateAsync(refreshed, original, token));
            lease.Dispose(); lease.Dispose();
            Assert.Throws<ObjectDisposedException>(() => lease.DirectoryPath);
        }
        finally { Directory.Delete(root, true); }
    }
}
