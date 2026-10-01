using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core.Media;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;
public sealed class NativeFilesMediaOriginalStoreTests
{
    [Fact]
    public async Task Retained_media_read_denies_replaced_store_and_foreign_actor_without_rewriting_original_source()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-native-media-store-" + Guid.NewGuid().ToString("N"));
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
            using var graph = services.BuildServiceProvider();
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            await graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
            var authority = graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
            var workspace = Assert.IsType<NativeFilesWorkspace>(await authority.GetCurrentAsync(token));
            var storeId = workspace.Configuration.StoreId;
            var folder = (await workspace.Provider.GetAsync(workspace.Configuration.AppFolders["media"], token)).Value!;
            var directory = (await workspace.Directories.ResolveProfileAsync(Guid.Parse(workspace.Actor.ProfileId), "media", token)).Value!.DirectoryPath;
            byte[] bytes = [82, 73, 70, 70, 1, 2, 3, 4]; // Source integrity only; no codec/device presentation claim.
            var sourcePath = Path.Combine(directory, "original.wav"); await File.WriteAllBytesAsync(sourcePath, bytes, token);
            var id = HostedItemId.New(); var revision = new FilesRevisionId(Guid.NewGuid());
            var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var upload = new FilesUploadedContent(id, folder.Id, "original.wav", "audio/wav", revision, null,
                workspace.Actor.ActorId, DateTimeOffset.UtcNow, bytes.Length, hash, "original.wav");
            var guard = await authority.CaptureCommitAuthorityAsync(workspace.Actor, workspace.Provider, () => true, token);
            Assert.True((await workspace.Provider.CommitUploadedContentAsync(upload, [new(folder.Id, folder.CurrentRevisionId)], guard, token)).IsSuccess);
            var resolver = graph.GetRequiredService<NativeFilesMediaAssetSourceResolver>(); var asset = MediaAssetId.New();
            var correct = await resolver.ResolveRetainedAsync(storeId, workspace.Actor, id.ToString(), asset, revision.ToString(), token);
            Assert.True(correct.IsSuccess, correct.Error?.Message);
            await using (var lease = correct.Value!) Assert.Equal(bytes, await File.ReadAllBytesAsync(lease.Source.SourceUri.LocalPath, token));
            Assert.False((await resolver.ResolveRetainedAsync(Guid.NewGuid(), workspace.Actor, id.ToString(), asset, revision.ToString(), token)).IsSuccess);
            Assert.False((await resolver.ResolveRetainedAsync(storeId, workspace.Actor with { ActorId = "foreign" }, id.ToString(), asset, revision.ToString(), token)).IsSuccess);
            var statePath = Path.Combine(chosen, ".9to1-files", "drive.json");
            var originalState = await File.ReadAllBytesAsync(statePath, token);
            var envelope = JsonNode.Parse(originalState)!.AsObject();
            envelope["state"]!["storeId"] = Guid.NewGuid().ToString("D");
            envelope["opaqueMediaSelectionFixture"] = "retained-original-store-only";
            var replacedState = JsonSerializer.SerializeToUtf8Bytes(envelope);
            await File.WriteAllBytesAsync(statePath, replacedState, token);
            var leaseDirectory = Path.Combine(directory, ".9to1-media-leases");
            var before = Directory.Exists(leaseDirectory) ? Directory.EnumerateDirectories(leaseDirectory).ToArray() : [];
            Assert.False((await resolver.ResolveRetainedAsync(storeId, workspace.Actor, id.ToString(), asset, revision.ToString(), token)).IsSuccess);
            Assert.True(Enumerable.SequenceEqual(replacedState, await File.ReadAllBytesAsync(statePath, token)));
            Assert.Equal(before, Directory.Exists(leaseDirectory) ? Directory.EnumerateDirectories(leaseDirectory).ToArray() : []);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(sourcePath, token));
            await File.WriteAllBytesAsync(statePath, originalState, token);
            var restored = await resolver.ResolveRetainedAsync(storeId, workspace.Actor, id.ToString(), asset, revision.ToString(), token);
            Assert.True(restored.IsSuccess, restored.Error?.Message); await restored.Value!.DisposeAsync();
        }
        finally { Directory.Delete(root, true); }
    }
}
