using System.Runtime.InteropServices;
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
using HavenOS.Images;

namespace Haven.Desktop.Tests;

public sealed class SpaceFilesPictureJourneyTests
{
    [Theory]
    [InlineData("media")]
    [InlineData("picture")]
    public async Task Actual_Space_Picture_mount_steps_sandboxed_animation_after_Move_and_revocation_clears_display(string sourceApp)
    {
        if (!OperatingSystem.IsLinux()) return;
        var token = TestContext.Current.CancellationToken;
        await using var nativeUi = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await nativeUi.Dispatch(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-space-picture-" + Guid.NewGuid().ToString("N"));
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
                var bytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
                var sourcePath = Path.Combine((await filesAuthority.ResolveAppDirectoryAsync(sourceApp, token))!, "source.gif");
                await File.WriteAllBytesAsync(sourcePath, bytes, token);
                var rawId = HostedItemId.New(); var rawRevision = new FilesRevisionId(Guid.NewGuid());
                var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                Assert.True((await workspace.Provider.CommitUploadedContentAsync(new(rawId, workspace.Configuration.AppFolders[sourceApp],
                    "source.gif", "image/gif", rawRevision, null, actor.ActorId, DateTimeOffset.UtcNow, bytes.Length, hash, "source.gif"), token)).IsSuccess);
                await workspace.Materializations.RegisterValidatedAsync(sourcePath,
                    new(rawId, rawRevision, hash, bytes.Length, DateTimeOffset.UtcNow), SyncAvailability.AvailableOffline, token);
                var rawMoveAt = DateTimeOffset.UtcNow;
                Assert.True((await workspace.Provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), actor.ActorId, rawId,
                    workspace.Configuration.AppFolders[sourceApp], workspace.Configuration.AppFolders["boards"], "Move", rawRevision,
                    null, FilesOperationState.Pending, rawMoveAt, rawMoveAt, null, null), null, token)).IsSuccess);
                var sourceAsset = new PictureSourceAssetReference(rawId.Value, rawRevision.Value, hash[7..], bytes.Length, Guid.NewGuid());
                var document = PictureDocument.Create(2, 1, rawId.ToString(), rawRevision.ToString());
                var bridge = new PictureFilesArtifactBridge(profiles, current => current == actor ? workspace.Provider : null,
                    workspace.Directories, resources, () => true);
                var created = await bridge.CreateAsync(document, sourceAsset, token);
                var fileId = new HostedItemId(created.Artifact.BackingFileId);
                var now = DateTimeOffset.UtcNow;
                var moved = await workspace.Provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), actor.ActorId, fileId,
                    workspace.Configuration.AppFolders["picture"], workspace.Configuration.AppFolders["boards"], "Move", created.CasRevisionId,
                    null, FilesOperationState.Pending, now, now, null, null), null, token);
                Assert.True(moved.IsSuccess);
                var pictureFolder = workspace.Configuration.AppFolders["picture"];
                var folderMetadata = (await workspace.Provider.GetAsync(pictureFolder, token)).Value!;
                var exportSource = new ResourceScope("files.item", fileId.ToString(), moved.Value!.ResultRevisionId!.Value.ToString(), ResourceAccess.Read);
                var exportDestination = new ResourceScope("files.item", pictureFolder.ToString(), folderMetadata.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Write);
                Assert.Equal(actor, await resources.AuthorizeAsync("picture.file.export", [exportSource, exportDestination], token));
                Assert.Null(await resources.AuthorizeAsync("picture.file.export", [exportSource with { Access = ResourceAccess.Write }, exportDestination], token));
                Assert.Null(await resources.AuthorizeAsync("picture.file.export", [exportSource, exportDestination with { Id = workspace.Configuration.AppFolders["boards"].ToString() }], token));
                Assert.Null(await resources.AuthorizeAsync("picture.file.export", [exportSource with { Revision = created.CasRevisionId.ToString() }, exportDestination], token));
                Assert.Equal(actor, await resources.AuthorizeAsync("picture.file.import", [exportDestination], token));
                Assert.Null(await resources.AuthorizeAsync("picture.file.import", [exportDestination with { Access = ResourceAccess.Read }], token));
                Assert.Null(await resources.AuthorizeAsync("picture.file.import", [exportDestination with { Id = workspace.Configuration.AppFolders["boards"].ToString() }], token));
                Assert.Null(await resources.AuthorizeAsync("picture.file.import", [exportDestination with { Revision = Guid.NewGuid().ToString("N") }], token));
                var source = new SpaceContextReference(Guid.NewGuid(), SpaceContextReferenceKind.PictureArtifact, "picture",
                    document.DocumentId.ToString("N"), created.Revision.OwningAppRevisionId, SpaceContextPermission.Read,
                    SpaceContextIndexState.NotRequired, false, now, fileId.Value);
                var space = await spaces.CreateAsync("Native Picture animation", cancellationToken: token);
                space = await spaces.UpdateAsync(space with { ContextReferences = [source] }, space.Revision, token);
                var action = new SpaceFilesArtifactAction(space.Id, space.Revision, source.ContextId, fileId.Value,
                    source.CanonicalEntityId, moved.Value!.ResultRevisionId!.Value, false);
                var router = new SpaceFilesArtifactActionRouter(spaces, filesAuthority, profiles, resources);
                var reader = new NativeFilesArtifactContentReader(filesAuthority, profiles, resources);
                var media = new NativeFilesMediaAssetSourceResolver(filesAuthority, profiles, resources);
                await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
                var motion = new LocalMotionPreferencesService(Path.Combine(root, "ui-preferences.json"));
                motion.SetReduceAnimations(true);
                using var surface = new SpacePictureCuiSurface(action, router, reader, media, runtime, profiles, resources, motion);
                await surface.InitializeAsync(token);
                var window = new Window { Content = surface, Width = 900, Height = 640 };
                window.Show();
                try
                {
                    var image = Assert.Single(surface.GetVisualDescendants().OfType<Image>());
                    Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(Assert.IsAssignableFrom<Bitmap>(image.Source)));
                    var buttons = surface.GetVisualDescendants().OfType<Button>().ToArray();
                    Assert.False(Assert.Single(buttons, button => Equals(button.Content, "Save")).IsEnabled);
                    Assert.False(Assert.Single(buttons, button => Equals(button.Content, "Export")).IsEnabled);
                    var play = Assert.Single(buttons, button => Equals(button.Content, "Play"));
                    var pause = Assert.Single(buttons, button => Equals(button.Content, "Pause"));
                    Assert.False(play.IsEnabled); // Actual shared reduced-motion preference, not a host-local flag.
                    var next = Assert.Single(buttons, button => Equals(button.Content, "Next frame"));
                    Assert.True(next.IsEnabled);
                    next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    for (var attempt = 0; attempt < 250 && image.Source is Bitmap frame && Pixel(frame)[0] != 255; attempt++)
                        await Task.Delay(20, token);
                    Assert.Equal(new byte[] { 255, 0, 0, 255 }, Pixel(Assert.IsAssignableFrom<Bitmap>(image.Source)));
                    motion.SetReduceAnimations(false);
                    await surface.ActivateAsync(token);
                    Assert.True(play.IsEnabled);
                    play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    for (var attempt = 0; attempt < 250 && !pause.IsEnabled; attempt++) await Task.Delay(20, token);
                    Assert.True(pause.IsEnabled);
                    for (var attempt = 0; attempt < 250 && image.Source is Bitmap playing && Pixel(playing)[2] != 255; attempt++) await Task.Delay(20, token);
                    Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(Assert.IsAssignableFrom<Bitmap>(image.Source)));
                    motion.SetReduceAnimations(true);
                    for (var attempt = 0; attempt < 250 && pause.IsEnabled; attempt++) await Task.Delay(20, token);
                    Assert.False(pause.IsEnabled); Assert.False(play.IsEnabled);
                    Assert.Equal(action.ExpectedFilesRevision, (await workspace.Provider.GetAsync(fileId, token)).Value!.CurrentRevisionId);
                    Assert.Equal(created.Revision.Id, (await bridge.OpenAsync(fileId, token)).Revision.Id);
                    if (sourceApp == "media")
                        space = await spaces.UpdateAsync(space with { ContextReferences = [source with { Permission = SpaceContextPermission.Denied }] }, space.Revision, token);
                    else
                    {
                        var currentRaw = (await workspace.Provider.GetAsync(rawId, token)).Value!;
                        Assert.True((await workspace.Provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), actor.ActorId, rawId,
                            currentRaw.ParentId, null, "Delete", currentRaw.CurrentRevisionId, null,
                            FilesOperationState.Pending, now, now, null, null), null, token)).IsSuccess);
                    }
                    await surface.ActivateAsync(token);
                    Assert.Null(image.Source);
                    Assert.False(next.IsEnabled);
                    Assert.Equal(bytes, await File.ReadAllBytesAsync(sourcePath, token));
                    Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(chosen, "Boards")));
                }
                finally { window.Close(); }
            }
            finally { Directory.Delete(root, true); }
            return 0;
        }, token);
    }

    private static byte[] Pixel(Bitmap bitmap)
    {
        using var converted = new WriteableBitmap(bitmap.PixelSize, new(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = converted.Lock(); bitmap.CopyPixels(buffer);
        var pixel = new byte[4]; Marshal.Copy(buffer.Address, pixel, 0, 4); return pixel;
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
