using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Canvas;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed class SpaceFilesCanvasJourneyTests
{
    [AvaloniaFact]
    public async Task Actual_profile_Space_reference_opens_same_Files_Canvas_after_restart_and_read_only_denies_save()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "astra-space-files-canvas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var actor = (await profiles.GetCurrentAsync(token))!;
            var settings = new VersionedAtomicSettingsStore(new Paths(Path.Combine(root, "settings")));
            var files = new NativeFilesWorkspaceService(home, profiles);
            var permissions = new HomePermissionTrustService(home, (app, action) =>
                app == "canvas" && action == "canvas.file.save"
                    ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null);
            var ownership = new HomeLocalStoreOwnership(home, profiles,
                new HomeLocalStoreEvidenceRegistry([files, new SpacesLocalStoreEvidenceProvider(settings, settings)]), permissions);
            var storeOwnership = new HomeResourceStoreOwnershipAuthority(ownership, profiles);
            var settingsId = await settings.GetStoreIdentityAsync(token);
            Assert.NotNull(await ownership.BindNewEmptyAsync("spaces", settingsId.StoreId.ToString("D"), token));
            var chosen = Path.Combine(root, "files");
            Directory.CreateDirectory(chosen);
            var workspace = await files.ConfigureNewAsync(chosen, ownership, token);
            var filesAuthority = new NativeFilesWorkspaceAuthority(files, profiles, storeOwnership);
            var spaces = new SpaceRegistry(settings);
            var resources = new ResourceAuthorizationService(profiles,
                [new FilesArtifactResourceResolver(async (current, ct) =>
                    (await filesAuthority.GetCurrentAsync(ct))?.Actor == current ? workspace.Provider : null),
                    new SpaceSourceResourceResolver(profiles, settings, storeOwnership, spaces, () => true)]);
            var bridge = new CanvasFilesArtifactBridge(profiles, current => current == actor ? workspace.Provider : null,
                workspace.Directories, resources, () => true);
            using var document = CanvasRnoteDocument.Create("Space canonical Canvas");
            document.DrawStroke([new(10, 20, 0.2), new(80, 100, 0.8)],
                new CanvasMutationRequest(document.Snapshot.RevisionId, Guid.NewGuid(), new(actor.ActorId, "Verified local user")));
            var created = await bridge.CreateAsync(document.Snapshot, token);
            var space = await spaces.CreateAsync("Canvas reference journey", cancellationToken: token);
            var source = new SpaceContextReference(Guid.NewGuid(), SpaceContextReferenceKind.CanvasArtifact,
                "canvas", document.Snapshot.ArtifactId.ToString("N"), document.Snapshot.RevisionId.ToString("N"),
                SpaceContextPermission.ReadWrite, SpaceContextIndexState.NotRequired, false, DateTimeOffset.UtcNow, created.FileId.Value);
            space = await spaces.UpdateAsync(space with { ContextReferences = [source] }, space.Revision, token);
            var reopenedSettings = new VersionedAtomicSettingsStore(new Paths(Path.Combine(root, "settings")));
            var reopenedSpaces = new SpaceRegistry(reopenedSettings);
            Assert.Equal(settingsId.StoreId, (await reopenedSettings.GetStoreIdentityAsync(token)).StoreId);
            resources = new ResourceAuthorizationService(profiles,
                [new FilesArtifactResourceResolver(async (current, ct) =>
                    (await filesAuthority.GetCurrentAsync(ct))?.Actor == current ? workspace.Provider : null),
                    new SpaceSourceResourceResolver(profiles, reopenedSettings, storeOwnership, reopenedSpaces, () => true)]);
            var router = new SpaceFilesArtifactActionRouter(reopenedSpaces, filesAuthority, profiles, resources);
            var captured = new SpaceFilesArtifactAction(space.Id, space.Revision, source.ContextId,
                created.FileId.Value, document.Snapshot.ArtifactId.ToString("N"), created.Revision.Id, false);
            var target = await router.ResolveAsync(captured, token);
            Assert.Equal(created.FileId, target.Artifact.FileId);
            Assert.Equal(source.CanonicalEntityId, target.Artifact.ArtifactId);
            var opened = await bridge.OpenAsync(target.Artifact.FileId, token);
            using var native = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
            Assert.Equal(document.Snapshot.ArtifactId, native.Snapshot.ArtifactId);
            Assert.Equal(document.Snapshot.RevisionId, native.Snapshot.RevisionId);
            var writingTarget = await router.ResolveAsync(captured with { Writing = true }, token);
            var broker = new HomeResourceOperationBroker(resources, permissions);
            var mutationId = Guid.NewGuid();
            var arguments = JsonSerializer.SerializeToElement(new { FileID = created.FileId.Value,
                ArtifactID = native.Snapshot.ArtifactId, BaseRevision = native.Snapshot.RevisionId, OperationID = mutationId });
            var approval = await broker.AuthorizeAsync("canvas", writingTarget.ActionId, writingTarget.Scopes,
                arguments, "Add a stroke to the selected Canvas artifact", null, actor.AuthenticationRevision, token);
            Assert.False(await broker.BeginExecutionAsync(approval.RequestId, arguments, token));
            Assert.True((await permissions.DecideAsync(approval.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
            Assert.True(await broker.BeginExecutionAsync(approval.RequestId, arguments, token));
            Assert.False(await broker.BeginExecutionAsync(approval.RequestId, arguments, token));
            Assert.NotNull(await router.ResolveAsync(captured with { Writing = true }, token));
            native.DrawStroke([new(25, 50, 0.5), new(90, 125, 0.8)],
                new CanvasMutationRequest(native.Snapshot.RevisionId, mutationId, new(actor.ActorId, "Verified local user")));
            var savedRevision = await bridge.SaveAsync(created.FileId, native.Snapshot, created.Revision.Id, token);
            source = source with { RevisionToken = native.Snapshot.RevisionId.ToString("N") };
            space = await reopenedSpaces.UpdateAsync(space with { ContextReferences = [source] }, space.Revision, token);
            captured = captured with { ExpectedSpaceRevision = space.Revision, ExpectedFilesRevision = savedRevision.Id };
            target = await router.ResolveAsync(captured, token);
            var renameTime = DateTimeOffset.UtcNow;
            var renamed = await workspace.Provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), actor.ActorId,
                created.FileId, null, null, "Rename", savedRevision.Id, null, FilesOperationState.Pending,
                renameTime, renameTime, null, null), "Renamed canonical Canvas", token);
            Assert.True(renamed.IsSuccess);
            captured = captured with { ExpectedFilesRevision = renamed.Value!.ResultRevisionId!.Value };
            target = await router.ResolveAsync(captured, token);
            var logicalDestination = workspace.Configuration.AppFolders["boards"];
            var moved = await workspace.Provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), actor.ActorId,
                created.FileId, workspace.Configuration.AppFolders["canvas"], logicalDestination, "Move",
                captured.ExpectedFilesRevision, null, FilesOperationState.Pending, renameTime, renameTime, null, null), null, token);
            Assert.True(moved.IsSuccess);
            captured = captured with { ExpectedFilesRevision = moved.Value!.ResultRevisionId!.Value };
            target = await router.ResolveAsync(captured, token);
            Assert.Equal(logicalDestination, target.Artifact.ParentFolderId);
            Assert.Equal("canvas", target.Artifact.OwnerAppId);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(chosen, "Boards")));
            var artifactReader = new NativeFilesArtifactContentReader(filesAuthority, profiles, resources);
            var canonicalRead = await artifactReader.ReadAsync("canvas", created.FileId, savedRevision.Id, token);
            Assert.Equal(native.Snapshot.ArtifactId, CanvasArtifactCodec.Deserialize(canonicalRead.Bytes).ArtifactId);
            Assert.Equal(savedRevision.Id, canonicalRead.Revision.Id);
            Assert.Equal(captured.ExpectedFilesRevision, canonicalRead.Metadata.CurrentRevisionId);
            Assert.NotEqual(canonicalRead.Revision.Id, canonicalRead.Metadata.CurrentRevisionId);
            await Assert.ThrowsAsync<InvalidDataException>(() => artifactReader.ReadAsync("games", created.FileId, savedRevision.Id, token));
            await Assert.ThrowsAsync<InvalidDataException>(() => artifactReader.ReadAsync("canvas", created.FileId, created.Revision.Id, token));
            var saved = await bridge.OpenAsync(created.FileId, token);
            Assert.Equal(native.Snapshot.ArtifactId, saved.Artifact.ArtifactId);
            Assert.Equal(native.Snapshot.RevisionId, saved.Artifact.RevisionId);
            Assert.Equal(savedRevision.Id, saved.Revision.Id);
            Assert.Equal(captured.ExpectedFilesRevision, saved.CasRevisionId);
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
            using var surface = new SpaceCanvasCuiSurface(captured, router, artifactReader, runtime, profiles, resources);
            await surface.InitializeAsync(token);
            var owner = Assert.IsType<CanvasNativeCuiSurface>(surface.Content);
            var sceneHost = Assert.IsType<CuiSceneHost>(owner.Content);
            Assert.Equal(CuiSceneAvailabilityState.Ready, sceneHost.Availability!.State);
            var window = new Window { Content = surface, Width = 1000, Height = 760 };
            window.Show();
            try
            {
                var viewport = Assert.Single(surface.GetVisualDescendants().OfType<CanvasNativeViewport>());
                Assert.True(await surface.RefreshAsync(token));
                Assert.NotNull(Assert.Single(viewport.GetVisualDescendants().OfType<Image>()).Source);
                Assert.False(Assert.Single(sceneHost.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Flush")).IsEnabled);
                space = await reopenedSpaces.UpdateAsync(space with { ContextReferences = [source with { Permission = SpaceContextPermission.Read }] }, space.Revision, token);
                var readOnlyAction = captured with { ExpectedSpaceRevision = space.Revision };
                Assert.NotNull(await router.ResolveAsync(readOnlyAction, token));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => router.ResolveAsync(readOnlyAction with { Writing = true }, token));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => router.ResolveAsync(readOnlyAction with { ArtifactId = Guid.NewGuid().ToString("N") }, token));
                Assert.False(await surface.RefreshAsync(token)); // Its previously captured Space revision is stale.
                Assert.Null(Assert.Single(viewport.GetVisualDescendants().OfType<Image>()).Source);
                Assert.Equal(captured.ExpectedFilesRevision, (await workspace.Provider.GetAsync(created.FileId, token)).Value!.CurrentRevisionId);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "store.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
