using Haven.Application.Games;
using Haven.Core.Games;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;


namespace Haven.Desktop.Tests;

public sealed class SpaceFilesGamesJourneyTests
{
    [Fact]
    public async Task Actual_Space_Games_inspector_preserves_Moved_project_and_disables_unapproved_writes()
    {
        if (!OperatingSystem.IsLinux()) return;
        var token = TestContext.Current.CancellationToken;
        await using var nativeUi = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await nativeUi.Dispatch(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-space-games-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var actor = (await profiles.GetCurrentAsync(token))!;
                var settings = new VersionedAtomicSettingsStore(new Paths(Path.Combine(root, "settings")));
                var files = new NativeFilesWorkspaceService(home, profiles);
                var permissions = new HomePermissionTrustService(home, (_, _) => null);
                var ownership = new HomeLocalStoreOwnership(home, profiles,
                    new HomeLocalStoreEvidenceRegistry([files, new SpacesLocalStoreEvidenceProvider(settings, settings)]), permissions);
                var storeOwnership = new HomeResourceStoreOwnershipAuthority(ownership, profiles);
                var settingsId = await settings.GetStoreIdentityAsync(token);
                Assert.NotNull(await ownership.BindNewEmptyAsync("spaces", settingsId.StoreId.ToString("D"), token));
                var chosen = Path.Combine(root, "files"); Directory.CreateDirectory(chosen);
                var workspace = await files.ConfigureNewAsync(chosen, ownership, token);
                var filesAuthority = new NativeFilesWorkspaceAuthority(files, profiles, storeOwnership);
                var spaces = new SpaceRegistry(settings);
                var resources = new ResourceAuthorizationService(profiles,
                    [new FilesArtifactResourceResolver(async (current, ct) =>
                        (await filesAuthority.GetCurrentAsync(ct))?.Actor == current ? workspace.Provider : null,
                        async (current, app, ct) => (await filesAuthority.GetCurrentAsync(ct))?.Actor == current &&
                            workspace.Configuration.AppFolders.TryGetValue(app, out var folder) ? folder : null),
                    new SpaceSourceResourceResolver(profiles, settings, storeOwnership, spaces, () => true)]);
                var projectId = Guid.NewGuid(); var sceneId = Guid.NewGuid();
                var scene = new GamesSceneSnapshot(projectId, sceneId, 2,
                    [new(Guid.NewGuid(), Guid.NewGuid(), null, "Canonical node", new(Guid.NewGuid(), new(1, 2, 3)), null)], []);
                var project = new GamesProjectDocument(1, projectId, 3, GamesWorkspaceMode.Development, sceneId, [scene]);
                var bytes = GamesProjectCodec.Encode(project);
                var fileId = HostedItemId.New();
                var reference = new FilesArtifactReference("games", projectId.ToString("D"), fileId,
                    workspace.Configuration.AppFolders["games"], nameof(FilesArtifactType.GameProject), "Game.9to1g");
                Assert.True((await workspace.Provider.RegisterArtifactAsync(reference, actor.ActorId, token)).IsSuccess);
                await File.WriteAllBytesAsync(Path.Combine(chosen, "Games", "game.9to1g"), bytes, token);
                var committed = await workspace.Provider.CommitDurableRevisionAsync(new(fileId, "games", "3", actor.ActorId,
                    DateTimeOffset.UtcNow, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), "game.9to1g", null), token);
                Assert.True(committed.IsSuccess);
                var now = DateTimeOffset.UtcNow;
                var moved = await workspace.Provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), actor.ActorId, fileId,
                    reference.ParentFolderId, workspace.Configuration.AppFolders["boards"], "Move", committed.Value!.Id,
                    null, FilesOperationState.Pending, now, now, null, null), null, token);
                Assert.True(moved.IsSuccess);
                var source = new SpaceContextReference(Guid.NewGuid(), SpaceContextReferenceKind.GamesProject, "games",
                    projectId.ToString("D"), "3", SpaceContextPermission.Read, SpaceContextIndexState.NotRequired, false, now, fileId.Value);
                var space = await spaces.CreateAsync("Canonical Games", cancellationToken: token);
                space = await spaces.UpdateAsync(space with { ContextReferences = [source] }, space.Revision, token);
                var action = new SpaceFilesArtifactAction(space.Id, space.Revision, source.ContextId, fileId.Value,
                    source.CanonicalEntityId, moved.Value!.ResultRevisionId!.Value, false);
                var router = new SpaceFilesArtifactActionRouter(spaces, filesAuthority, profiles, resources);
                var reader = new NativeFilesArtifactContentReader(filesAuthority, profiles, resources);
                var editor = new GamesProjectEditorService(new GamesFilesArtifactBridge(filesAuthority, reader, resources));
                await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
                using var surface = new SpaceGamesCuiSurface(action, router, editor, null, runtime, profiles, resources);
                await surface.InitializeAsync(token);
                var window = new Window { Content = surface, Width = 900, Height = 640 }; window.Show();
                try
                {
                    Assert.Contains(surface.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == projectId.ToString("D"));
                    Assert.False(surface.IsActionAvailable("9to1.Games.SetPosition"));
                    Assert.False(surface.IsActionAvailable("9to1.Games.Observe"));
                    Assert.True(surface.IsActionAvailable("9to1.Games.NextNode"));
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await surface.DispatchAsync("9to1.Games.SetPosition", null, token));
                    Assert.Equal(action.ExpectedFilesRevision, (await workspace.Provider.GetAsync(fileId, token)).Value!.CurrentRevisionId);
                    space = await spaces.UpdateAsync(space with { ContextReferences = [source with { Permission = SpaceContextPermission.Denied }] }, space.Revision, token);
                    await surface.ActivateAsync(token);
                    Assert.Null(surface.Content);
                    Assert.False(surface.IsActionAvailable("9to1.Games.NextNode"));
                    Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(chosen, "Boards")));
                }
                finally { window.Close(); }
            }
            finally { Directory.Delete(root, true); }
            return 0;
        }, token);
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
