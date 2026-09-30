using System.Text.Json;
using System.Security.Cryptography;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core.Media;
using Haven.Desktop.Services;
using HavenOS.Files;
using HavenOS.Apps.Sites.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed class NativeFilesWorkspaceServiceTests
{
    [Fact]
    public async Task Setup_denies_final_configuration_publication_after_trusted_principal_changes()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-files-setup-admission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        DelayedConfigurationStore? delayed = null;
        Task<NativeFilesWorkspace>? setup = null;
        try
        {
            var backing = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var home = delayed = new DelayedConfigurationStore(backing);
            var principal = new RevocablePrincipal();
            var profiles = new HomeLocalProfileIdentity(home, principal);
            var files = new NativeFilesWorkspaceService(home, profiles);
            var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([files]),
                new HomePermissionTrustService(home, (_, _) => null));
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            setup = files.ConfigureNewAsync(chosen, ownership, token);
            await home.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            var before = await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token);
            principal.Revoked = true;
            home.Release.TrySetResult();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => setup);
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token));
            Assert.DoesNotContain((await backing.ReadAsync(token)).State!.Records, r => r.RecordType == "files.native-workspace");
            Assert.True(File.Exists(Path.Combine(chosen, ".9to1-files", "drive.json"))); // Preserve partial setup for recovery.
        }
        finally
        {
            delayed?.Release.TrySetResult();
            if (setup is not null) { try { await setup; } catch { /* Preserve the primary test failure. */ } }
            Directory.Delete(root, true);
        }
    }

    private sealed class RevocablePrincipal : ITrustedHostPrincipalSource
    {
        public bool Revoked;
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => Revoked
            ? ValueTask.FromResult<string?>(null) : new OperatingSystemPrincipalSource().GetPrincipalAsync(token);
    }

    private sealed class DelayedConfigurationStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<HomeStateReadResult> ReadAsync(CancellationToken token = default) => inner.ReadAsync(token);
        private async Task DelayAsync(HomeCoreStateRecord record, CancellationToken token)
        {
            if (record.RecordType != "files.native-workspace") return;
            Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        }
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long revision, CancellationToken token = default)
        { await DelayAsync(record, token); return await inner.WriteAsync(record, revision, token); }
        public async Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long revision,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken token = default)
        { await DelayAsync(record, token); return await inner.WriteGuardedAsync(record, revision, actor, guard, token); }
    }

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
            Assert.Equal(8, configured.Configuration.AppFolders.Count);
            Assert.Equal(Path.Combine(chosen, "Games"), await authority.ResolveAppDirectoryAsync("games", token));
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
            actor = reopened.Actor; // Restart establishes a fresh authentication session for the same OS profile.
            Assert.Equal(configured.Configuration.StoreId, reopened.Configuration.StoreId);
            Assert.Equal(sitesId, reopened.Configuration.AppFolders["sites"]);
            Assert.False((await reopened.Provider.GetStoreEvidenceAsync(token)).NewlyCreated);
            Assert.Equal(Path.Combine(chosen, "Sites"), await reopenedAuthority.ResolveAppDirectoryAsync("sites", token));
            var sitesAuthority = new SitesNativeWorkspaceAuthority(reopenedAuthority);
            var sitesBinding = await sitesAuthority.GetCurrentAsync(token);
            Assert.NotNull(sitesBinding);
            Assert.Equal(actor.ProfileId, sitesBinding.ProfileId);
            Assert.Equal(actor.ActorId, sitesBinding.ActorId);
            Assert.Equal(sitesId.Value, sitesBinding.FilesFolderId);
            Assert.Equal(Path.Combine(chosen, "Sites"), sitesBinding.RootDirectory);
            Assert.Equal((await reopened.Provider.GetAsync(sitesId, token)).Value!.CurrentRevisionId!.Value.ToString(), sitesBinding.FolderRevision);
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
                }, async (current, appId, ct) =>
                {
                    var currentWorkspace = await reopenedAuthority.GetCurrentAsync(ct);
                    return currentWorkspace?.Actor == current && currentWorkspace.Configuration.AppFolders.TryGetValue(appId, out var folder)
                        ? folder : null;
                }), new SiteNativeProjectAccessResolver(sitesAuthority)]);
            var sitesScope = new ResourceScope("files.item", sitesId.ToString(), sitesBinding.FolderRevision, ResourceAccess.Write);
            Assert.Equal(actor, await resourceAuthorization.AuthorizeAsync("sites.project.create", [sitesScope], token));
            Assert.Equal(actor, await resourceAuthorization.AuthorizeAsync("sites.project.save", [sitesScope], token));
            Assert.Null(await resourceAuthorization.AuthorizeAsync("sites.project.create", [sitesScope with { Access = ResourceAccess.Read }], token));
            var otherFolder = (await reopened.Provider.GetAsync(reopened.Configuration.AppFolders["write"], token)).Value!;
            Assert.Null(await resourceAuthorization.AuthorizeAsync("sites.project.create", [sitesScope with
                { Id = otherFolder.Id.ToString(), Revision = otherFolder.CurrentRevisionId!.Value.ToString() }], token));
            var unconfiguredSites = new FilesArtifactResourceResolver(_ => reopened.Provider);
            Assert.False((await unconfiguredSites.EvaluateAsync(actor, "sites.project.save", sitesScope, token)).Allowed);
            Assert.Null(await resourceAuthorization.AuthorizeAsync("sites.project.save", [sitesScope with { Revision = Guid.NewGuid().ToString("N") }], token));
            var sitePermissions = new HomePermissionTrustService(home, new SiteNativeActionPolicies().TryGet);
            var siteBroker = new HomeResourceOperationBroker(resourceAuthorization, sitePermissions);
            var siteOwner = new SiteNativeWriteCoordinator(sitesAuthority, siteBroker);
            var createSite = SiteNativeWriteIntent.Create(sitesBinding, "Canonical native site", "9to1-native", "canonical-site");
            var pendingSite = await siteBroker.AuthorizeAsync("sites", createSite.ActionId, createSite.Scopes, createSite.Arguments,
                "Create the reviewed site in the configured Files folder", null, actor.AuthenticationRevision, token);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pendingSite.State);
            Assert.Null(await siteBroker.BeginExecutionCapabilityAsync(pendingSite.RequestId, createSite.Arguments, token));
            Assert.True((await sitePermissions.DecideAsync(pendingSite.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            var createCapability = Assert.IsType<HomeResourceExecutionCapability>(await siteBroker.BeginExecutionCapabilityAsync(pendingSite.RequestId, createSite.Arguments, token));
            var createdSite = await siteOwner.ExecuteAsync(createSite, createCapability, token);
            Assert.Null(createdSite.Error);
            Assert.Equal(sitesId.Value, createdSite.Value!.Source.FilesDirectoryId);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => siteOwner.ExecuteAsync(createSite, createCapability, token));
            var renameSite = SiteNativeWriteIntent.Rename(sitesBinding, createdSite.Value.SiteId, createdSite.Value.Revision, "Renamed canonical site");
            var renamePending = await siteBroker.AuthorizeAsync("sites", renameSite.ActionId, renameSite.Scopes, renameSite.Arguments,
                "Rename the reviewed canonical site", null, actor.AuthenticationRevision, token);
            Assert.True((await sitePermissions.DecideAsync(renamePending.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            var renameCapability = Assert.IsType<HomeResourceExecutionCapability>(await siteBroker.BeginExecutionCapabilityAsync(renamePending.RequestId, renameSite.Arguments, token));
            var renamedSite = await siteOwner.ExecuteAsync(renameSite, renameCapability, token);
            Assert.Null(renamedSite.Error);
            Assert.Equal(createdSite.Value.SiteId, renamedSite.Value!.SiteId);
            Assert.Equal(createdSite.Value.Revision + 1, renamedSite.Value.Revision);
            Assert.Null(await resourceAuthorization.AuthorizeAsync("sites.project.save", renameSite.Scopes, token));
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
            Assert.Null(await sitesAuthority.GetCurrentAsync(token));
            Assert.Null(await resourceAuthorization.AuthorizeAsync("sites.project.save", [sitesScope], token));
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
