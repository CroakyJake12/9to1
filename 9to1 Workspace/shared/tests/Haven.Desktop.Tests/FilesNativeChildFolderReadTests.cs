using System.Text.Json.Nodes;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class FilesNativeChildFolderReadTests
{
    [Fact]
    public async Task Actual_Home_canonical_child_ancestry_and_trusted_mapping_keep_original_store_root_and_project_receipts()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-files-child-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var actors = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var services = new ServiceCollection();
            services.AddSingleton<IHomeCoreStateStore>(home); services.AddSingleton(actors);
            services.AddSingleton<IAuthenticatedResourceActorSource>(actors);
            services.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
            services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
            services.AddSingleton<HomeLocalStoreOwnership>();
            services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
            services.AddSingleton<ResourceAuthorizationService>(); services.AddFilesNativeHost();
            using var graph = services.BuildServiceProvider();
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            var configured = await graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
            var originalStore = configured.Configuration.StoreId;
            var workspace = (await graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(originalStore, token))!;
            var actor = workspace.Actor; var profile = Guid.Parse(actor.ProfileId);
            var rootFolder = (await workspace.Provider.GetAsync(workspace.Configuration.AppFolders["sites"], token)).Value!;
            var rootDirectory = (await workspace.Directories.ResolveProfileAsync(profile, "sites", token)).Value!.DirectoryPath;
            // Explicit trusted fixture registration of a canonical preexisting root, not a production Stacks setup/initializer claim.
            Assert.True((await workspace.Directories.RegisterProfileAsync(profile, rootFolder.Id, "stacks", rootDirectory, token)).IsSuccess);
            var child = HostedItemId.New(); var now = DateTimeOffset.UtcNow;
            Assert.True((await workspace.Provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), actor.ActorId, child,
                null, rootFolder.Id, "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null), "Project", token)).IsSuccess);
            rootFolder = (await workspace.Provider.GetAsync(rootFolder.Id, token)).Value!;
            var childMetadata = (await workspace.Provider.GetAsync(child, token)).Value!;
            var folderSource = graph.GetRequiredService<FilesNativeFolderReadSource>();
            Task<FilesNativeFolderReadLease> Read(Guid store) => folderSource.ReadChildAsync("stacks", store,
                rootFolder.Id.Value, rootFolder.CurrentRevisionId!.Value.ToString(), child.Value,
                childMetadata.CurrentRevisionId!.Value.ToString(), actor, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Read(originalStore)); // No trusted child mapping yet.
            var childDirectory = Path.Combine(rootDirectory, "preexisting-project"); Directory.CreateDirectory(childDirectory);
            var mappingKey = "stacks.project." + child.Value.ToString("N");
            Assert.True((await workspace.Directories.RegisterProfileAsync(profile, child, mappingKey, childDirectory, token)).IsSuccess);
            using var lease = await Read(originalStore);
            Assert.Equal(child.Value, lease.Source.FolderID); Assert.Equal(originalStore, lease.Source.StoreID);
            Assert.Equal(rootFolder.Id.Value, lease.Source.RootFolderID);
            Assert.Equal(rootFolder.CurrentRevisionId!.Value.ToString(), lease.Source.RootFolderRevision);
            Assert.Equal(actor, lease.Source.ObservedActor); Assert.Equal(childDirectory, lease.DirectoryPath);
            await lease.RevalidateAsync(token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Read(Guid.NewGuid()));
            var statePath = Path.Combine(chosen, ".9to1-files", "drive.json");
            var before = await File.ReadAllBytesAsync(statePath, token);
            var envelope = JsonNode.Parse(before)!.AsObject(); envelope["state"]!["storeId"] = Guid.NewGuid();
            envelope["state"]!["opaqueFutureMetadata"] = JsonNode.Parse("{\"retained\":true}");
            await File.WriteAllTextAsync(statePath, envelope.ToJsonString(), token);
            var replaced = await File.ReadAllBytesAsync(statePath, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Read(originalStore));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lease.RevalidateAsync(token));
            Assert.Equal(replaced, await File.ReadAllBytesAsync(statePath, token));
            await File.WriteAllBytesAsync(statePath, before, token);
            await lease.RevalidateAsync(token);
            var sibling = (await workspace.Provider.GetAsync(workspace.Configuration.AppFolders["write"], token)).Value!;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => folderSource.ReadChildAsync("stacks", originalStore,
                rootFolder.Id.Value, rootFolder.CurrentRevisionId!.Value.ToString(), sibling.Id.Value,
                sibling.CurrentRevisionId!.Value.ToString(), actor, token));
            // A new registration receipt is a changed authority observation even if the canonical IDs/CAS stay identical.
            Assert.True((await workspace.Directories.RegisterProfileAsync(profile, child, mappingKey, childDirectory, token)).IsSuccess);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lease.RevalidateAsync(token));
            using var refreshed = await Read(originalStore);
            Assert.True((await workspace.Directories.RegisterProfileAsync(profile, child, "duplicate-child-mapping", childDirectory, token)).IsSuccess);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => refreshed.RevalidateAsync(token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Read(originalStore));
        }
        finally { Directory.Delete(root, true); }
    }
}
