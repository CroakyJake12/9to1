using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using Haven.Core.Media;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureNativeHostTests
{
    [AvaloniaFact]
    public async Task Standalone_Cui_uses_actual_Home_Files_Glycin_and_approved_edit_then_clears_revoked_source()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "picture-native-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var home = new FailFirstPictureCompletionStore(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var files = new NativeFilesWorkspaceService(home, profiles);
            var permissions = new HomePermissionTrustService(home, new PictureNativeActionPolicies().TryGet);
            var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([files]), permissions);
            var authority = new NativeFilesWorkspaceAuthority(files, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
            var chosen = Path.Combine(root, "selected-empty-workspace"); Directory.CreateDirectory(chosen);
            await files.ConfigureNewAsync(chosen, ownership, ct);
            var workspace = (await authority.GetCurrentAsync(ct))!;
            Assert.NotNull(workspace);
            var resources = new ResourceAuthorizationService(profiles,
                [new FilesArtifactResourceResolver(async (actor, token) =>
                {
                    var current = await authority.GetCurrentAsync(token);
                    return current?.Actor == actor ? current.Provider : null;
                }, async (actor, app, token) =>
                {
                    var current = await authority.GetCurrentAsync(token);
                    return current?.Actor == actor && current.Configuration.AppFolders.TryGetValue(app, out var folder) ? folder : null;
                })]);
            var broker = new HomeResourceOperationBroker(resources, permissions);
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
            var media = new NativeFilesMediaAssetSourceResolver(authority, profiles, resources);
            var services = new ServiceCollection();
            var motionPath = Path.Combine(root, "ui-preferences.json");
            var motion = new Haven.Infrastructure.LocalMotionPreferencesService(motionPath);
            services.AddSingleton<Haven.Application.IMotionPreferenceSource>(motion);
            services.AddSingleton(runtime); services.AddSingleton(profiles); services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
            services.AddSingleton(files); services.AddSingleton(authority); services.AddSingleton(resources); services.AddSingleton(broker);
            services.AddSingleton(permissions); services.AddSingleton(ownership); services.AddSingleton(media);
            var bytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
            services.AddSingleton<IStorageProvider>(new PicturePickerFixture(bytes).Provider);
            await using var provider = services.BuildServiceProvider();
            var pictureFolder = workspace.Configuration.AppFolders["picture"];
            var directory = (await authority.ResolveAppDirectoryAsync("picture", ct))!;
            await File.WriteAllBytesAsync(Path.Combine(directory, "native-source.gif"), bytes, ct);
            var rawId = HostedItemId.New(); var rawRevision = new FilesRevisionId(Guid.NewGuid());
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var folderRevision = (await workspace.Provider.GetAsync(pictureFolder, ct)).Value!.CurrentRevisionId;
            var commitGuard = await authority.CaptureCommitAuthorityAsync(workspace.Actor, workspace.Provider, () => true, ct);
            Assert.True((await workspace.Provider.CommitUploadedContentAsync(new(rawId, pictureFolder, "native-source.gif", "image/gif", rawRevision,
                null, workspace.Actor.ActorId, DateTimeOffset.UtcNow, bytes.Length, hash, "native-source.gif"),
                [new(pictureFolder, folderRevision)], commitGuard, ct)).IsSuccess);
            var bridge = new PictureFilesArtifactBridge(profiles, actor => actor == workspace.Actor ? workspace.Provider : null,
                workspace.Directories, resources, () => true,
                (actor, expectedProvider, token) => authority.CaptureCommitAuthorityAsync(actor, expectedProvider, () => true, token));
            var original = await bridge.CreateAsync(PictureDocument.Create(2, 1, rawId.ToString(), rawRevision.ToString()),
                new(rawId.Value, rawRevision.Value, hash, bytes.Length, Guid.NewGuid()), ct);
            var window = new MainWindow(provider);
            window.Show();
            try
            {
                await window.Initialization;
                var open = Button(window, "Open selected"); Assert.True(open.IsEnabled);
                open.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(() => Descendants(window).OfType<PictureNativeCuiSurface>().Any(), ct);
                var surface = Assert.Single(Descendants(window).OfType<PictureNativeCuiSurface>());
                var image = Assert.Single(Descendants(surface).OfType<Image>());
                var exportButton = Button(surface, "Export");
                var exportPosition = exportButton.TranslatePoint(default, surface);
                Assert.NotNull(exportPosition);
                Assert.InRange(exportPosition.Value.X + exportButton.Bounds.Width, 0, surface.Bounds.Width + 1);
                AssertRed(Assert.IsAssignableFrom<Bitmap>(image.Source));
                // Playback uses the same current source, real donor timing and per-frame authority checks.
                Button(surface, "Play").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(() => Pixel(Assert.IsAssignableFrom<Bitmap>(image.Source)).SequenceEqual(new byte[] { 255, 0, 0, 255 }), ct);
                Assert.False(Button(surface, "Next frame").IsEnabled);
                Button(surface, "Pause").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await surface.ValidateAccessAsync(ct); // Wait for a frame already submitted when Pause was clicked.
                var paused = Pixel(Assert.IsAssignableFrom<Bitmap>(image.Source));
                await Task.Delay(250, ct);
                Assert.Equal(paused, Pixel(Assert.IsAssignableFrom<Bitmap>(image.Source)));
                Assert.True(Button(surface, "Play").IsEnabled);
                Assert.False(Button(surface, "Pause").IsEnabled);
                Assert.Equal(original.CasRevisionId, (await bridge.OpenAsync(new(original.Artifact.BackingFileId), ct)).CasRevisionId);
                motion.SetReduceAnimations(true);
                await UntilAsync(() => !Button(surface, "Play").IsEnabled, ct);
                Assert.True(Button(surface, "Next frame").IsEnabled);
                motion.SetReduceAnimations(false);
                await UntilAsync(() => Button(surface, "Play").IsEnabled, ct);
                Button(surface, "Play").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(() => Button(surface, "Pause").IsEnabled, ct);
                // Simulate the actual shared preference being changed by another process, without a notification.
                await File.WriteAllTextAsync(motionPath, "{\"reduceAnimations\":true}", ct);
                await UntilAsync(() => !Button(surface, "Pause").IsEnabled && !Button(surface, "Play").IsEnabled, ct);
                await surface.ValidateAccessAsync(ct);
                var motionPaused = Pixel(Assert.IsAssignableFrom<Bitmap>(image.Source));
                await Task.Delay(250, ct);
                Assert.Equal(motionPaused, Pixel(Assert.IsAssignableFrom<Bitmap>(image.Source)));
                Assert.True(Button(surface, "Next frame").IsEnabled);
                var rotate = Button(surface, "Rotate clockwise"); Assert.True(rotate.IsEnabled);
                rotate.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(async () => (await permissions.GetSnapshotAsync(cancellationToken: ct)).PendingRequests.Count == 1, ct);
                Assert.Equal(original.CasRevisionId, (await bridge.OpenAsync(new(original.Artifact.BackingFileId), ct)).CasRevisionId);
                await UntilAsync(() => HasEnabledButton(window, "Finish approved request"), ct);
                await UntilAsync(() => HasEnabledButton(window, "Accept once"), ct);
                Button(window, "Accept once").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(async () => (await permissions.GetSnapshotAsync(cancellationToken: ct)).PendingRequests.Count == 0, ct);
                await UntilAsync(() => HasEnabledButton(window, "Finish approved request"), ct);
                Button(window, "Finish approved request").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(() => Descendants(window).OfType<PictureNativeCuiSurface>().Any(item => !ReferenceEquals(item, surface)), ct);
                var edited = await bridge.OpenAsync(new(original.Artifact.BackingFileId), ct);
                Assert.Equal(1, edited.Artifact.Document.Revision);
                Assert.Equal(original.Artifact.SourceAsset, edited.Artifact.SourceAsset);
                Assert.Equal(1, edited.Artifact.Document.CanvasWidth); Assert.Equal(2, edited.Artifact.Document.CanvasHeight);
                surface = Assert.Single(Descendants(window).OfType<PictureNativeCuiSurface>());
                image = Assert.Single(Descendants(surface).OfType<Image>());
                Assert.NotNull(image.Source);
                Assert.Equal(1, home.CompletionFailures);
                Assert.True(HasEnabledButton(window, "Finish approved request"));
                var committedBeforeAuditRetry = edited.CasRevisionId;
                Button(window, "Finish approved request").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(() => !HasEnabledButton(window, "Finish approved request") && HasEnabledButton(window, "Import image"), ct);
                Assert.Equal(committedBeforeAuditRetry, (await bridge.OpenAsync(new(edited.Artifact.BackingFileId), ct)).CasRevisionId);
                Assert.Contains((await permissions.GetSnapshotAsync(cancellationToken: ct)).RecentAuditEvents,
                    item => item.Kind == HomePermissionAuditKind.ExecutionCompleted && item.RequestState == HomePermissionRequestState.Succeeded &&
                        item.AffectedObjects.Any(affected => affected.ObjectType == "files.item" && affected.ObjectId == edited.Artifact.BackingFileId.ToString("N")));
                // A real Home decline must release only the local pending operation and preserve the committed document.
                Button(surface, "Flip horizontally").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(async () => (await permissions.GetSnapshotAsync(cancellationToken: ct)).PendingRequests.Count == 1, ct);
                await UntilAsync(() => HasEnabledButton(window, "Finish approved request") && HasEnabledButton(window, "Decline"), ct);
                Button(window, "Decline").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(async () => (await permissions.GetSnapshotAsync(cancellationToken: ct)).PendingRequests.Count == 0, ct);
                Button(window, "Finish approved request").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(() => !HasEnabledButton(window, "Finish approved request") && HasEnabledButton(window, "Import image"), ct);
                Assert.Equal(edited.CasRevisionId, (await bridge.OpenAsync(new(edited.Artifact.BackingFileId), ct)).CasRevisionId);
                motion.SetReduceAnimations(false);
                await UntilAsync(() => Button(surface, "Play").IsEnabled, ct);
                Button(surface, "Play").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(() => Button(surface, "Pause").IsEnabled, ct);
                var now = DateTimeOffset.UtcNow;
                Assert.True((await workspace.Provider.MutateAsync(new(new(Guid.NewGuid()), workspace.Actor.ActorId, rawId,
                    pictureFolder, null, "Delete", rawRevision, null, FilesOperationState.Pending, now, now, null, null), null, ct)).IsSuccess);
                // Automatic playback itself must discover raw-source revocation and remove its pixels.
                await UntilAsync(() => image.Source is null, ct);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => surface.ValidateAccessAsync(ct));
                Assert.Null(image.Source);
                Assert.False(Button(surface, "Next frame").IsEnabled);

                // Drive the actual owning picker/import form and native Home approval UI.
                Button(window, "Import image").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(() => Descendants(window).OfType<Button>().Any(button => Equals(button.Content, "Choose image")), ct);
                Button(window, "Choose image").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(() => Button(window, "Review in Home").IsEnabled, ct);
                Button(window, "Review in Home").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await ApprovePendingAsync(window, permissions, ct);
                Button(window, "Finish approved request").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(() => Descendants(window).OfType<PictureNativeCuiSurface>().Any(), ct);
                var items = await workspace.Provider.ListAsync(pictureFolder, null, null, ct);
                var importedMetadata = Assert.Single(items.Items, item => item.Kind == HostedItemKind.Artifact && item.Id.Value != original.Artifact.BackingFileId);
                var imported = await bridge.OpenAsync(importedMetadata.Id, ct);
                Assert.Equal(0, imported.Artifact.Document.Revision);
                Assert.NotEqual(rawId.Value, imported.Artifact.SourceAsset!.FileId);
                surface = Assert.Single(Descendants(window).OfType<PictureNativeCuiSurface>());
                AssertRed(Assert.IsAssignableFrom<Bitmap>(Assert.Single(Descendants(surface).OfType<Image>()).Source));

                Button(surface, "Export").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(() => Descendants(window).OfType<CheckBox>().Any(box => box.Name == "picture-export-flatten"), ct);
                Assert.Single(Descendants(window).OfType<TextBox>(), control => control.Name == "picture-export-name").Text = "approved-ui.png";
                // TextChanged is posted by Avalonia after rendering; allow the actual two-way binding to receive it
                // before the next input triggers a host binding refresh.
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.Single(Descendants(window).OfType<CheckBox>(), control => control.Name == "picture-export-flatten").IsChecked = true;
                Button(window, "Prepare export").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await UntilAsync(() => Button(window, "Review in Home").IsEnabled, ct);
                Assert.Contains("approved-ui.png", Assert.Single(Descendants(window).OfType<TextBlock>(), control => control.Name == "picture-export-preview").Text);
                Button(window, "Review in Home").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await ApprovePendingAsync(window, permissions, ct);
                Button(window, "Finish approved request").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                var exportNames = "";
                await UntilAsync(async () =>
                {
                    var listing = await workspace.Provider.ListAsync(pictureFolder, null, null, ct);
                    exportNames = string.Join(", ", listing.Items.Select(item => item.Name));
                    return listing.Items.Any(item => item.Name == "approved-ui.png");
                }, ct, () => " Files: " + exportNames + " UI: " + string.Join(" | ", Descendants(window).OfType<TextBlock>().Select(control => control.Text)));
                var exported = Assert.Single((await workspace.Provider.ListAsync(pictureFolder, null, null, ct)).Items, item => item.Name == "approved-ui.png");
                Assert.Equal("image/png", exported.ContentType);
                Assert.Equal(imported.CasRevisionId, (await bridge.OpenAsync(importedMetadata.Id, ct)).CasRevisionId);
                var leaseResult = await media.ResolveRetainedAsync(exported.Id.ToString(), new MediaAssetId(Guid.NewGuid()), exported.CurrentRevisionId!.Value.ToString(), ct);
                Assert.True(leaseResult.IsSuccess);
                await using var lease = leaseResult.Value!;
                var pngBytes = await File.ReadAllBytesAsync(lease.Source.SourceUri.LocalPath, ct);
                var png = new PictureGlycinSharedRasterDecoder().DecodeFirstFrame(pngBytes, ct);
                Assert.Equal(2, png.Width); Assert.Equal(1, png.Height);

            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // A failure of the actual Home durable completion write must never cause the owning Files mutation to run twice.
    private sealed class FailFirstPictureCompletionStore(FileHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public int CompletionFailures { get; private set; }
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRevision, CancellationToken ct = default)
        {
            if (CompletionFailures == 0 && record.RecordType == "home.permissions-trust" &&
                record.Payload.GetRawText().Contains("PICTURE_COMMITTED", StringComparison.Ordinal))
            {
                CompletionFailures++;
                throw new IOException("Injected first actual Picture completion persistence failure.");
            }
            return inner.WriteAsync(record, expectedRevision, ct);
        }
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expectedRevision,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken ct = default) =>
            inner.WriteGuardedAsync(record, expectedRevision, actor, guard, ct);
    }

    private static async Task ApprovePendingAsync(MainWindow window, HomePermissionTrustService permissions, CancellationToken ct)
    {
        await UntilAsync(async () => (await permissions.GetSnapshotAsync(cancellationToken: ct)).PendingRequests.Count == 1, ct);
        await UntilAsync(() => HasEnabledButton(window, "Finish approved request"), ct);
        await UntilAsync(() => HasEnabledButton(window, "Accept once"), ct);
        Button(window, "Accept once").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await UntilAsync(async () => (await permissions.GetSnapshotAsync(cancellationToken: ct)).PendingRequests.Count == 0, ct);
        await UntilAsync(() => Button(window, "Finish approved request").IsEnabled, ct);
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Control root)
    {
        root.UpdateLayout();
        return root.GetVisualDescendants();
    }
    private static bool HasEnabledButton(Control root, string label) => Descendants(root).OfType<Button>().Any(button => Equals(button.Content, label) && button.IsEnabled);
    private static Button Button(Control root, string label) => Assert.Single(Descendants(root).OfType<Button>(), button => Equals(button.Content, label));
    private static async Task UntilAsync(Func<bool> predicate, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 300; attempt++) { if (predicate()) return; await Task.Delay(10, ct); }
        Assert.True(predicate(), "The actual native CUI operation did not complete.");
    }
    private static async Task UntilAsync(Func<Task<bool>> predicate, CancellationToken ct, Func<string>? diagnostics = null)
    {
        for (var attempt = 0; attempt < 300; attempt++) { if (await predicate()) return; await Task.Delay(10, ct); }
        Assert.True(await predicate(), "The actual native CUI operation did not complete." + diagnostics?.Invoke());
    }
    private static void AssertRed(Bitmap bitmap) => Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(bitmap));
    private static byte[] Pixel(Bitmap bitmap)
    {
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 1, 1), buffer, 4, 4);
            var pixel = new byte[4]; Marshal.Copy(buffer, pixel, 0, 4);
            return pixel;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
