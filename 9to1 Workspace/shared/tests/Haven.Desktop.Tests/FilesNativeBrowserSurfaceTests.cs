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
