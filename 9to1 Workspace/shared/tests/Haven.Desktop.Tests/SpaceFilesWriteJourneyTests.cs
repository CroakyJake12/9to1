using Avalonia.Automation;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Haven.Core;
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

public sealed class SpaceFilesWriteJourneyTests
{
    [Fact]
    public async Task Actual_Space_Write_package_mount_preserves_rich_paragraph_identity_after_Move()
    {
        if (!OperatingSystem.IsLinux()) return;
        var token = TestContext.Current.CancellationToken;
        await using var nativeUi = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        await nativeUi.Dispatch(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-space-write-" + Guid.NewGuid().ToString("N"));
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
                var block = NotesBlock.CreateParagraph();
                block.Runs = [new NotesTextRun { Text = "Canonical rich paragraph", Bold = true, Italic = true, FontFamily = "fonts:Inter#Inter" }];
                var document = new NotesDocument { Title = "Canonical Write" };
                document.Sections[0].Pages[0].Blocks = [block];
                var packages = new WriteNativeDocumentPackageStore();
                var packagePath = Path.Combine(chosen, "Write", "document.9to1w");
                Assert.True((await packages.SaveAsync(document, packagePath, token)).IsSuccess);
                var bytes = await File.ReadAllBytesAsync(packagePath, token);
                var fileId = HostedItemId.New();
                var reference = new FilesArtifactReference("write", document.Id.ToString("N"), fileId,
                    workspace.Configuration.AppFolders["write"], nameof(FilesArtifactType.WriteDocument), "Document.9to1w");
                Assert.True((await workspace.Provider.RegisterArtifactAsync(reference, actor.ActorId, token)).IsSuccess);
                var committed = await workspace.Provider.CommitDurableRevisionAsync(new(fileId, "write", "owning-revision", actor.ActorId,
                    DateTimeOffset.UtcNow, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), "document.9to1w", null), token);
                Assert.True(committed.IsSuccess);
                var now = DateTimeOffset.UtcNow;
                var moved = await workspace.Provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), actor.ActorId, fileId,
                    reference.ParentFolderId, workspace.Configuration.AppFolders["boards"], "Move", committed.Value!.Id,
                    null, FilesOperationState.Pending, now, now, null, null), null, token);
                Assert.True(moved.IsSuccess);
                var source = new SpaceContextReference(Guid.NewGuid(), SpaceContextReferenceKind.WriteArtifact, "write",
                    document.Id.ToString("N"), "owning-revision", SpaceContextPermission.Read, SpaceContextIndexState.NotRequired, false, now, fileId.Value);
                var space = await spaces.CreateAsync("Canonical Write", cancellationToken: token);
                space = await spaces.UpdateAsync(space with { ContextReferences = [source] }, space.Revision, token);
                var action = new SpaceFilesArtifactAction(space.Id, space.Revision, source.ContextId, fileId.Value,
                    source.CanonicalEntityId, moved.Value!.ResultRevisionId!.Value, false);
                var router = new SpaceFilesArtifactActionRouter(spaces, filesAuthority, profiles, resources);
                await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
                using var surface = new SpaceWriteCuiSurface(action, router, filesAuthority, packages, runtime, profiles, resources);
                await surface.InitializeAsync(token);
                var window = new Window { Content = surface, Width = 900, Height = 640 }; window.Show();
                try
                {
                    var paragraph = Assert.Single(surface.GetVisualDescendants().OfType<TextBlock>(), text => AutomationProperties.GetAutomationId(text) == "notes-" + block.Id.ToString("N"));
                    var run = Assert.IsType<Run>(Assert.Single(paragraph.Inlines!));
                    Assert.Equal("Canonical rich paragraph", run.Text);
                    Assert.Equal(FontWeight.Bold, run.FontWeight); Assert.Equal(FontStyle.Italic, run.FontStyle);
                    Assert.Empty(surface.GetVisualDescendants().OfType<TextBox>());
                    Assert.Equal(action.ExpectedFilesRevision, (await workspace.Provider.GetAsync(fileId, token)).Value!.CurrentRevisionId);
                    space = await spaces.UpdateAsync(space with { ContextReferences = [source with { Permission = SpaceContextPermission.Denied }] }, space.Revision, token);
                    await surface.ActivateAsync(token); Assert.Null(surface.Content);
                    Assert.Equal(bytes, await File.ReadAllBytesAsync(packagePath, token));
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
