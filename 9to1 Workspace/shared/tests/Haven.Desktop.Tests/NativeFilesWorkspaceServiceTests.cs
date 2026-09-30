using System.Text.Json;
using System.Security.Cryptography;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core.Media;
using Haven.Desktop.Services;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed class NativeFilesWorkspaceServiceTests
{
    [Fact]
    public async Task Explicit_empty_native_Files_setup_persists_real_profile_folders_and_revoked_ownership_denies_Sites()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-native-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var actor = await profiles.GetCurrentAsync(token);
            Assert.NotNull(actor);
            Assert.Null(actor.AccountId);
            Assert.Null(actor.OrganisationId);
            var files = new NativeFilesWorkspaceService(home, profiles);
            var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([files]), new HomePermissionTrustService(home, (_, _) => null));
            var authority = new NativeFilesWorkspaceAuthority(files, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
            Assert.Null(await authority.GetCurrentAsync(token));
            var existing = Path.Combine(root, "existing");
            Directory.CreateDirectory(existing);
            await File.WriteAllTextAsync(Path.Combine(existing, "user.txt"), "Existing user content", token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => files.ConfigureNewAsync(existing, ownership, token));
            Assert.False(Directory.Exists(Path.Combine(existing, ".9to1-files")));
            var chosen = Path.Combine(root, "chosen-empty-folder");
            Directory.CreateDirectory(chosen);
            var configured = await files.ConfigureNewAsync(chosen, ownership, token);
            Assert.Equal(actor.ProfileId, configured.Configuration.ProfileId);
            Assert.Equal(7, configured.Configuration.AppFolders.Count);
            Assert.Equal(Path.Combine(chosen, "Sites"), await authority.ResolveAppDirectoryAsync("sites", token));
            Assert.Null(await authority.ResolveAppDirectoryAsync("unregistered-app", token));
            var sitesId = configured.Configuration.AppFolders["sites"];
            Assert.Equal(sitesId, (await configured.Provider.GetAsync(sitesId, token)).Value!.Id);
            var reopenedProfiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var reopenedFiles = new NativeFilesWorkspaceService(home, reopenedProfiles);
            var reopenedOwnership = new HomeLocalStoreOwnership(home, reopenedProfiles, new HomeLocalStoreEvidenceRegistry([reopenedFiles]), new HomePermissionTrustService(home, (_, _) => null));
            var reopenedAuthority = new NativeFilesWorkspaceAuthority(reopenedFiles, reopenedProfiles,
                new HomeResourceStoreOwnershipAuthority(reopenedOwnership, reopenedProfiles));
            var reopened = await reopenedAuthority.GetCurrentAsync(token);
            Assert.NotNull(reopened);
            Assert.Equal(configured.Configuration.StoreId, reopened.Configuration.StoreId);
            Assert.Equal(sitesId, reopened.Configuration.AppFolders["sites"]);
            Assert.False((await reopened.Provider.GetStoreEvidenceAsync(token)).NewlyCreated);
            Assert.Equal(Path.Combine(chosen, "Sites"), await reopenedAuthority.ResolveAppDirectoryAsync("sites", token));
            var sourcePath = Path.Combine(chosen, "Media", "source.wav");
            byte[] sourceBytes = [82, 73, 70, 70, 1, 2, 3, 4];
            await File.WriteAllBytesAsync(sourcePath, sourceBytes, token);
            var sourceId = HostedItemId.New();
            var sourceRevision = new FilesRevisionId(Guid.NewGuid());
            var sourceHash = "sha256:" + Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
            Assert.True((await reopened.Provider.CommitUploadedContentAsync(new(sourceId,
                reopened.Configuration.AppFolders["media"], "source.wav", "audio/wav", sourceRevision,
                null, actor.ActorId, DateTimeOffset.UtcNow, sourceBytes.Length, sourceHash, "source.wav"), token)).IsSuccess);
            await reopened.Materializations.RegisterValidatedAsync(sourcePath,
                new(sourceId, sourceRevision, sourceHash, sourceBytes.Length, DateTimeOffset.UtcNow), SyncAvailability.AvailableOffline, token);
            var resourceAuthorization = new ResourceAuthorizationService(reopenedProfiles,
                [new FilesArtifactResourceResolver(async (current, ct) =>
                {
                    var currentWorkspace = await reopenedAuthority.GetCurrentAsync(ct);
                    return currentWorkspace?.Actor == current ? currentWorkspace.Provider : null;
                })]);
            var media = new NativeFilesMediaAssetSourceResolver(reopenedAuthority, reopenedProfiles, resourceAuthorization);
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home),
                new HomePermissionsCoreService(new HomePermissionTrustService(home, (_, _) => null), reopenedProfiles)]);
            var viewReadiness = new HomeResourceCuiReadiness(runtime, reopenedProfiles, resourceAuthorization,
                "media.asset.read", _ => ValueTask.FromResult<IReadOnlyList<ResourceScope>>(
                    [new("files.item", sourceId.ToString(), sourceRevision.ToString(), ResourceAccess.Read)]));
            Assert.Equal(CuiSceneAvailabilityState.Ready, (await viewReadiness.CheckAsync(token)).State);
            var assetId = MediaAssetId.New();
            var leased = await media.ResolveAsync(sourceId.ToString(), assetId, sourceRevision.ToString(), token);
            Assert.True(leased.IsSuccess, leased.Error?.Message);
            await using (var lease = leased.Value!)
                Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(lease.Source.SourceUri.LocalPath, token));
            var state = (await home.ReadAsync(token)).State!;
            var bindingRecord = Assert.Single(state.Records, record => record.RecordType == "home.local-store-ownership");
            var binding = bindingRecord.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await home.WriteAsync(bindingRecord with { Revision = bindingRecord.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "foreign-profile" }) }, bindingRecord.Revision, token)).IsSuccess);
            Assert.Null(await reopenedAuthority.GetCurrentAsync(token));
            Assert.Null(await reopenedAuthority.ResolveAppDirectoryAsync("sites", token));
            Assert.Equal(CuiSceneAvailabilityState.Unavailable, (await viewReadiness.CheckAsync(token)).State);
            Assert.Equal(MediaEngineErrorCode.PermissionDenied,
                (await media.ResolveAsync(sourceId.ToString(), assetId, sourceRevision.ToString(), token)).Error!.Code);
            Assert.True(Directory.Exists(Path.Combine(chosen, "Sites")));
            Assert.Equal("Existing user content", await File.ReadAllTextAsync(Path.Combine(existing, "user.txt"), token));
            await File.WriteAllTextAsync(Path.Combine(root, "home.json"), "corrupt recovery fixture", token);
            Assert.Equal(MediaEngineErrorCode.SourceUnavailable,
                (await media.ResolveAsync(sourceId.ToString(), assetId, sourceRevision.ToString(), token)).Error!.Code);
            Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(sourcePath, token));
        }
        finally { Directory.Delete(root, true); }
    }
}
