using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HavenOS.Apps.Canvas.NativeUI.Tests;

[Collection("Canvas native UI")]
public sealed class CanvasNativeHostTests
{
    [Fact]
    public async Task Actual_native_host_creates_then_draws_only_after_each_real_Home_approval()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch(async () =>
        {
            var ct = CancellationToken.None;
            var root = Path.Combine(Path.GetTempPath(), "canvas-native-host-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
                var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var files = new NativeFilesWorkspaceService(home, profiles);
                var permissions = new HomePermissionTrustService(home, new CanvasNativeActionPolicies().TryGet);
                var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([files]), permissions);
                var authority = new NativeFilesWorkspaceAuthority(files, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
                var chosen = Path.Combine(root, "selected-empty-workspace"); Directory.CreateDirectory(chosen);
                await files.ConfigureNewAsync(chosen, ownership, ct);
                var workspace = (await authority.GetCurrentAsync(ct))!;
                var resources = new ResourceAuthorizationService(profiles, [new FilesArtifactResourceResolver(async (actor, token) =>
                { var current = await authority.GetCurrentAsync(token); return current?.Actor == actor ? current.Provider : null; },
                async (actor, app, token) =>
                { var current = await authority.GetCurrentAsync(token); return current?.Actor == actor && current.Configuration.AppFolders.TryGetValue(app, out var folder) ? folder : null; })]);
                var broker = new HomeResourceOperationBroker(resources, permissions);
                await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
                var services = new ServiceCollection();
                services.AddSingleton(runtime); services.AddSingleton(profiles); services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
                services.AddSingleton(files); services.AddSingleton(authority); services.AddSingleton(resources); services.AddSingleton(broker);
                services.AddSingleton(permissions); services.AddSingleton(ownership);
                await using var provider = services.BuildServiceProvider();
                var folderId = workspace.Configuration.AppFolders["canvas"];
                var window = new CanvasHostWindow(provider); window.Show();
                try
                {
                    await window.Initialization;
                    Click(window, "Create canvas");
                    await Until(async () => (await permissions.GetSnapshotAsync(cancellationToken: ct)).PendingRequests.Count == 1);
                    Assert.Empty((await workspace.Provider.ListAsync(folderId, new("", Limit: 100), null, ct)).Items);
                    await AcceptAndFinish(window, permissions, ct);
                    await Until(() => Task.FromResult(window.GetVisualDescendants().OfType<CanvasNativeCuiSurface>().Any()));
                    var surface = Assert.Single(window.GetVisualDescendants().OfType<CanvasNativeCuiSurface>());
                    var item = Assert.Single((await workspace.Provider.ListAsync(folderId, new("", Limit: 100), null, ct)).Items);
                    var bridge = new CanvasFilesArtifactBridge(profiles, actor => actor == workspace.Actor ? workspace.Provider : null,
                        workspace.Directories, resources, () => false);
                    var initial = await bridge.OpenAsync(item.Id, ct);
                    Assert.Empty(initial.Artifact.Pages[0].Strokes);
                    Click(surface, "Red");
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    window.UpdateLayout();
                    var viewport = Assert.Single(surface.GetVisualDescendants().OfType<CanvasNativeViewport>());
                    var start = viewport.TranslatePoint(new Point(viewport.Bounds.Width / 2, viewport.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(start, MouseButton.Left);
                    window.MouseMove(start + new Vector(15, 15), RawInputModifiers.LeftMouseButton);
                    window.MouseUp(start + new Vector(15, 15), MouseButton.Left);
                    await Until(async () => (await permissions.GetSnapshotAsync(cancellationToken: ct)).PendingRequests.Count == 1);
                    Assert.Equal(initial.CasRevisionId, (await bridge.OpenAsync(item.Id, ct)).CasRevisionId);
                    await AcceptAndFinish(window, permissions, ct);
                    await Until(async () => (await bridge.OpenAsync(item.Id, ct)).Artifact.Pages[0].Strokes.Count == 1);
                    var committed = await bridge.OpenAsync(item.Id, ct);
                    var stroke = Assert.Single(committed.Artifact.Pages[0].Strokes);
                    Assert.Equal("#FFFF0000", stroke.ResolvedBrushProperties.Color);
                    Assert.All(stroke.Samples, sample => Assert.Equal(0.5, sample.Pressure));
                    Assert.Equal(initial.Artifact.ArtifactId, committed.Artifact.ArtifactId);
                    Assert.NotEqual(initial.CasRevisionId, committed.CasRevisionId);
                    await Until(() => Task.FromResult(window.GetVisualDescendants().OfType<CanvasNativeCuiSurface>().Any(value => !ReferenceEquals(value, surface))));
                    Assert.Contains((await permissions.GetSnapshotAsync(cancellationToken: ct)).RecentAuditEvents,
                        audit => audit.ResultCode == "CANVAS_COMMITTED" && audit.AffectedObjects.Any(value => value.ObjectId == item.Id.ToString()));
                }
                finally { window.Close(); }
            }
            finally { Directory.Delete(root, true); }
        }, CancellationToken.None);
    }
    private static Button Button(Control root, string text) => Assert.Single(root.GetVisualDescendants().OfType<Button>(), value => Equals(value.Content, text));
    private static void Click(Control root, string text)
    { root.UpdateLayout(); var button = Button(root, text); Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); }
    private static async Task Until(Func<Task<bool>> predicate)
    { for (var i = 0; i < 400; i++) { if (await predicate()) return; await Task.Delay(10); } Assert.True(await predicate()); }
    private static async Task AcceptAndFinish(Control root, HomePermissionTrustService permissions, CancellationToken ct)
    {
        await Until(() => Task.FromResult(root.GetVisualDescendants().OfType<Button>().Any(value => Equals(value.Content, "Accept once") && value.IsEnabled)));
        Click(root, "Accept once");
        await Until(async () => (await permissions.GetSnapshotAsync(cancellationToken: ct)).PendingRequests.Count == 0);
        await Until(() => Task.FromResult(Button(root, "Finish approved request").IsEnabled));
        Click(root, "Finish approved request");
    }
}
