using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class FilesNativeBrowserSurfaceTests
{
    [AvaloniaFact]
    public async Task Native_CUI_lists_canonical_folders_routes_original_package_selection_and_clears_revoked_Home_sources()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-files-native-view-" + Guid.NewGuid().ToString("N"));
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
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            await graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
            var authority = graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
            var workspace = (await authority.GetCurrentAsync(token))!;
            var actor = workspace.Actor;
            var folder = (await workspace.Provider.GetAsync(workspace.Configuration.AppFolders["picture"], token)).Value!;
            var directory = (await workspace.Directories.ResolveProfileAsync(Guid.Parse(actor.ProfileId), "picture", token)).Value!.DirectoryPath;
            byte[] bytes = [77, 90, 1, 2, 3, 4]; // Lease/selection fixture, not an installation or publisher-trust proof.
            await File.WriteAllBytesAsync(Path.Combine(directory, "package.exe"), bytes, token);
            var file = HostedItemId.New(); var contentRevision = new FilesRevisionId(Guid.NewGuid());
            var upload = new FilesUploadedContent(file, folder.Id, "package.exe", "application/octet-stream", contentRevision,
                null, actor.ActorId, DateTimeOffset.UtcNow, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), "package.exe");
            var guard = await authority.CaptureCommitAuthorityAsync(actor, workspace.Provider, () => true, token);
            Assert.True((await workspace.Provider.CommitUploadedContentAsync(upload, [new(folder.Id, folder.CurrentRevisionId)], guard, token)).IsSuccess);
            var alternate = HostedItemId.New();
            await File.WriteAllBytesAsync(Path.Combine(directory, "alternate.exe"), bytes, token);
            var currentFolder = (await workspace.Provider.GetAsync(folder.Id, token)).Value!;
            Assert.True((await workspace.Provider.CommitUploadedContentAsync(upload with { FileId = alternate,
                Name = "alternate.exe", RevisionId = new FilesRevisionId(Guid.NewGuid()), ProviderContentReference = "alternate.exe" },
                [new(folder.Id, currentFolder.CurrentRevisionId)], guard, token)).IsSuccess);
            var browser = graph.GetRequiredService<FilesNativeBrowserService>();
            var opened = new CapturedOpen();
            var packages = new FilesCompatibilityPackageOpenCoordinator(graph.GetRequiredService<ICompatibilityPackageContentSource>(), profiles, opened);
            using var hostLifetime = new CancellationTokenSource();
            var readiness = new SuspendedOwnerReadiness(browser, actor);
            using var view = new FilesNativeBrowserSurface(browser, packages, actor, readiness, hostLifetime.Token);
            var window = new Window { Content = view, Width = 720, Height = 480 }; window.Show();
            try
            {
                await view.InitializeAsync(token);
                window.UpdateLayout();
                var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>());
                Assert.Equal(workspace.Configuration.AppFolders.Count, list.Items.Count);
                list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == folder.Id);
                Assert.True(view.IsActionAvailable("9to1.Files.Open"));
                await view.DispatchAsync("9to1.Files.Open", null, token);
                var displayed = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == file);
                Assert.Equal(file, displayed.Id);
                list.SelectedItem = displayed;
                Assert.True(view.TryGetValue("SelectedDetails", out var details));
                var detailsText = Assert.IsType<string>(details);
                Assert.Contains(displayed.Name, detailsText);
                Assert.Contains($"Size: {bytes.Length:N0} bytes", detailsText);
                Assert.Contains($"Availability: {displayed.Availability}", detailsText);
                Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), item =>
                    item.Name == "files-selected-details" && item.Text == detailsText);
                var sorter = Assert.Single(view.GetVisualDescendants().OfType<ComboBox>());
                Assert.True(sorter.IsEnabled);
                var homeBeforeSort = await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token);
                var drivePath = Path.Combine(chosen, ".9to1-files", "drive.json");
                var driveBeforeSort = await File.ReadAllBytesAsync(drivePath, token);
                sorter.SelectedIndex = 1;
                Assert.Equal(new[] { "package.exe", "alternate.exe" }, list.Items.Cast<HostedItemMetadata>().Select(item => item.Name));
                Assert.Same(displayed, list.SelectedItem);
                Assert.True(view.TryGetValue("SelectedDetails", out var sortedDetails));
                Assert.Equal(detailsText, sortedDetails);
                sorter.SelectedIndex = 0;
                Assert.Equal(new[] { "alternate.exe", "package.exe" }, list.Items.Cast<HostedItemMetadata>().Select(item => item.Name));
                Assert.Same(displayed, list.SelectedItem);
                Assert.Equal(homeBeforeSort, await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token));
                Assert.Equal(driveBeforeSort, await File.ReadAllBytesAsync(drivePath, token));
                Assert.Empty(opened.Selections);
                // A detached lookalike is not the row retained by the original page.
                list.SelectedItem = displayed with { Name = displayed.Name + " caller replacement" };
                Assert.True(view.TryGetValue("SelectedDetails", out var detachedDetails));
                Assert.Equal("Select an item to view its displayed details.", detachedDetails);
                list.SelectedItem = displayed;
                await view.DispatchAsync("9to1.Files.Open", null, token);
                Assert.True(opened.Selections.Count == 1,
                    view.TryGetValue("Status", out var openStatus) ? openStatus?.ToString() : "Files status unavailable");
                var selection = Assert.Single(opened.Selections);
                Assert.Equal(file.Value, selection.FileId); Assert.Equal(actor, selection.ObservedActor);
                Assert.Equal(displayed.CurrentRevisionId!.Value.ToString(), selection.MetadataRevision);
                await Assert.ThrowsAsync<ArgumentException>(async () => await view.DispatchAsync("9to1.Files.Open", "caller-path.exe", token));
                Assert.Single(opened.Selections);
                await view.DispatchAsync("9to1.Files.Back", null, token);
                Assert.Equal(workspace.Configuration.AppFolders.Count, list.Items.Count);
                await view.DispatchAsync("9to1.Files.Forward", null, token);
                Assert.Equal(2, list.Items.Count);
                list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == file);
                readiness.SuspendNext();
                var retainedOpen = view.DispatchAsync("9to1.Files.Open", null, token).AsTask();
                await readiness.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == alternate);
                readiness.Release(); await retainedOpen;
                Assert.Single(opened.Selections); // No later mutable selection is substituted for the original click.
                Assert.Empty(list.Items);
                Assert.True(view.TryGetValue("SelectedDetails", out var retiredDetails));
                Assert.Equal("Select an item to view its displayed details.", retiredDetails);
                await view.DispatchAsync("9to1.Files.Refresh", null, token);
                list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == file);
                readiness.SuspendNext();
                var secondOpen = view.DispatchAsync("9to1.Files.Open", null, token).AsTask();
                await readiness.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                var focusRefresh = view.RefreshAsync(token);
                Assert.False(focusRefresh.IsCompleted); Assert.Equal(2, list.Items.Count);
                readiness.Release(); await secondOpen; await focusRefresh;
                Assert.Equal(2, opened.Selections.Count);
                Assert.All(opened.Selections, item => Assert.Equal(file.Value, item.FileId));
                list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == file);
                readiness.FailNextRead();
                await view.DispatchAsync("9to1.Files.Open", null, token);
                Assert.Equal(2, opened.Selections.Count); Assert.Empty(list.Items);
                Assert.True(view.TryGetValue("Status", out var failedStatus));
                Assert.Contains("fixture storage read failed", Assert.IsType<string>(failedStatus), StringComparison.Ordinal);
                await view.DispatchAsync("9to1.Files.Refresh", null, token);
                Assert.Equal(2, list.Items.Count);
                using var impostor = new FilesNativeBrowserSurface(browser, packages, actor with { ActorId = "new-session" },
                    new OwnerReadiness(browser, actor), hostLifetime.Token);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => impostor.InitializeAsync(token));
                Assert.Null(impostor.Content);
                var record = Assert.Single((await home.ReadAsync(token)).State!.Records, item => item.RecordType == "home.local-store-ownership");
                var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
                Assert.True((await home.WriteAsync(record with { Revision = record.Revision + 1,
                    Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked" }) }, record.Revision, token)).IsSuccess);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => view.RefreshAsync(token));
                Assert.Empty(list.Items);
                Assert.False(view.IsActionAvailable("9to1.Files.Open"));
                hostLifetime.Cancel(); view.Dispose(); view.Dispose(); Assert.Null(view.Content);
                Assert.False(sorter.IsEnabled);
                sorter.SelectedIndex = 1;
                Assert.Empty(list.Items);
                Assert.True(view.TryGetValue("SelectedDetails", out var disposedDetails));
                Assert.Equal("Select an item to view its displayed details.", disposedDetails);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }
    [AvaloniaFact]
    public async Task Native_Up_uses_canonical_parent_after_nested_navigation_and_denies_stale_original_folder_without_writes()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-files-up-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var services = new ServiceCollection();
            services.AddSingleton<IHomeCoreStateStore>(home); services.AddSingleton(profiles);
            services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
            services.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
            services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
            services.AddSingleton<HomeLocalStoreOwnership>();
            services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
            services.AddSingleton<ResourceAuthorizationService>(); services.AddFilesNativeHost();
            using var graph = services.BuildServiceProvider();
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            var workspace = await graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
            var picture = workspace.Configuration.AppFolders["picture"];
            async Task<HostedItemId> CreateFolder(HostedItemId parent, string name)
            {
                var id = HostedItemId.New(); var now = DateTimeOffset.UtcNow;
                Assert.True((await workspace.Provider.MutateAsync(new(new(Guid.NewGuid()), workspace.Actor.ActorId, id,
                    null, parent, "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null), name, token)).IsSuccess);
                return id;
            }
            var nested = await CreateFolder(picture, "Nested canonical folder");
            var deep = await CreateFolder(nested, "Deep canonical folder");
            var browser = graph.GetRequiredService<FilesNativeBrowserService>();
            var direct = await browser.ListAsync(workspace.Actor, deep.Value, token: token, expectedStoreId: workspace.Configuration.StoreId);
            var canonicalParent = await browser.GetParentAsync(direct, workspace.Actor, token);
            Assert.Equal(nested.Value, canonicalParent.ID); Assert.Equal("Nested canonical folder", canonicalParent.Title);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => browser.GetParentAsync(direct,
                workspace.Actor with { ActorId = "foreign-session" }, token));
            var opened = new CapturedOpen();
            var packages = new FilesCompatibilityPackageOpenCoordinator(graph.GetRequiredService<ICompatibilityPackageContentSource>(), profiles, opened);
            using var view = new FilesNativeBrowserSurface(browser, packages, workspace.Actor, new OwnerReadiness(browser, workspace.Actor), CancellationToken.None);
            var window = new Window { Content = view, Width = 720, Height = 480 }; window.Show();
            try
            {
                await view.InitializeAsync(token); window.UpdateLayout();
                var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>());
                var upButton = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => button.Name == "files-up");
                Assert.False(upButton.IsEnabled); Assert.False(view.IsActionAvailable("9to1.Files.Up"));
                var driveFile = Path.Combine(chosen, ".9to1-files", "drive.json");
                var homeBefore = await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token);
                var driveBefore = await File.ReadAllBytesAsync(driveFile, token);
                foreach (var id in new[] { picture, nested, deep })
                {
                    list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == id);
                    await view.DispatchAsync("9to1.Files.Open", null, token);
                }
                Assert.Empty(list.Items); Assert.True(upButton.IsEnabled);
                Assert.True(view.TryGetValue("FolderTitle", out var title)); Assert.Equal("Deep canonical folder", title);
                var search = Assert.Single(view.GetVisualDescendants().OfType<TextBox>());
                search.Text = "no matching item"; await view.DispatchAsync("9to1.Files.Search", null, token);
                await view.DispatchAsync("9to1.Files.Up", null, token);
                Assert.Equal(deep, Assert.Single(list.Items.Cast<HostedItemMetadata>()).Id); Assert.Equal("", search.Text);
                Assert.True(view.TryGetValue("FolderTitle", out title)); Assert.Equal("Nested canonical folder", title);
                await view.DispatchAsync("9to1.Files.Back", null, token); Assert.Empty(list.Items);
                await view.DispatchAsync("9to1.Files.Forward", null, token);
                Assert.Equal(deep, Assert.Single(list.Items.Cast<HostedItemMetadata>()).Id);
                await view.DispatchAsync("9to1.Files.Up", null, token);
                Assert.Equal(nested, Assert.Single(list.Items.Cast<HostedItemMetadata>()).Id);
                await view.DispatchAsync("9to1.Files.Up", null, token);
                Assert.False(view.IsActionAvailable("9to1.Files.Up"));
                Assert.Equal(workspace.Configuration.AppFolders.Count, list.Items.Count);
                Assert.Equal(homeBefore, await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token));
                Assert.Equal(driveBefore, await File.ReadAllBytesAsync(driveFile, token)); Assert.Empty(opened.Selections);
                list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == picture);
                await view.DispatchAsync("9to1.Files.Open", null, token);
                await CreateFolder(picture, "Independent later folder");
                var changedDrive = await File.ReadAllBytesAsync(driveFile, token);
                await view.DispatchAsync("9to1.Files.Up", null, token);
                Assert.Empty(list.Items); Assert.False(view.IsActionAvailable("9to1.Files.Up"));
                Assert.Equal(changedDrive, await File.ReadAllBytesAsync(driveFile, token));
                Assert.Equal(homeBefore, await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token));
                view.Dispose(); Assert.False(view.IsActionAvailable("9to1.Files.Up")); Assert.Empty(opened.Selections);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class CapturedOpen : ICompatibilityPackageOpenHandler
    {
        public List<CompatibilityPackageSource> Selections { get; } = [];
        public Task OpenAsync(CompatibilityPackageSource selection, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Selections.Add(selection); return Task.CompletedTask; }
    }
    private sealed class SuspendedOwnerReadiness(FilesNativeBrowserService browser, AuthenticatedResourceActor original) : ICuiSceneReadiness
    {
        private readonly OwnerReadiness _owner = new(browser, original);
        private bool _pause;
        private bool _failRead;
        public void FailNextRead() => _failRead = true;
        private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void SuspendNext()
        { _pause = true; Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); _release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public void Release() => _release.TrySetResult();
        public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            var actual = await _owner.CheckAsync(token);
            if (_failRead) { _failRead = false; throw new IOException("fixture storage read failed"); }
            if (_pause) { _pause = false; Entered.TrySetResult(); await _release.Task.WaitAsync(token); }
            return actual;
        }
    }
    // Real Files/Home owner reads gate this native scene fixture; this is not an OS compositor handshake claim.
    private sealed class OwnerReadiness(FilesNativeBrowserService browser, AuthenticatedResourceActor original) : ICuiSceneReadiness
    {
        public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            try { await browser.ListAsync(original, token: token); return new(CuiSceneAvailabilityState.Ready, "OwnerRead", "Ready"); }
            catch (UnauthorizedAccessException) { return new(CuiSceneAvailabilityState.Unavailable, "OwnerDenied", "Files ownership unavailable"); }
        }
    }
}
