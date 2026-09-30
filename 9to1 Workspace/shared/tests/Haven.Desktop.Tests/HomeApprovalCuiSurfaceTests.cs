using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Controls;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed class HomeApprovalCuiSurfaceTests
{
    [AvaloniaFact]
    public async Task Actual_native_Cui_button_decides_real_OS_profile_request_without_granting_extra_trust()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-home-approvals-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var actor = await profiles.GetCurrentAsync(token);
            Assert.NotNull(actor);
            var permissions = new HomePermissionTrustService(store, (app, action) => app == "write" && action == "write.file.save"
                ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, true, false, true) : null);
            var target = new HomeObjectReference("files.item", Guid.NewGuid().ToString("N"));
            var request = await permissions.AuthorizeAsync(new(null,
                new(actor.ActorId, "Local writer", actor.ProfileId, actor.AuthenticationRevision, true), "actual-native-test",
                new("write", "write.file.save", [target]), new(["files.item"], 1, [target], false, "Save the reviewed document")), token);
            Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(store), new HomePermissionsCoreService(permissions, profiles)]);
            using var surface = new HomeApprovalCuiSurface(runtime, profiles, permissions);
            await surface.InitializeAsync(token);
            var host = Assert.IsType<CuiSceneHost>(surface.Content);
            Assert.Equal(CuiSceneAvailabilityState.Ready, host.Availability!.State);
            Assert.DoesNotContain(host.Diagnostics, diagnostic => diagnostic.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            var window = new Window { Content = surface, Width = 900, Height = 700 };
            window.Show();
            try
            {
                var buttons = surface.GetVisualDescendants().OfType<Button>().ToArray();
                var accept = Assert.Single(buttons, button => Equals(button.Content, "Accept once"));
                accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                HomePermissionAuthorization actual = request;
                for (var attempt = 0; attempt < 100 && !actual.IsAllowed; attempt++)
                {
                    await Task.Delay(10, token);
                    actual = await permissions.GetAuthorizationAsync(request.RequestId, token);
                }
                Assert.True(actual.IsAllowed, actual.Message);
                var snapshot = await permissions.GetSnapshotAsync(cancellationToken: token);
                Assert.Empty(snapshot.PendingRequests);
                Assert.Empty(snapshot.Grants);
                Assert.True((await permissions.BeginExecutionAsync(request.RequestId, token)).IsAllowed);
                var redispatch = await permissions.BeginExecutionAsync(request.RequestId, token);
                Assert.Equal("HOME_PERMISSION_NOT_AUTHORIZED", redispatch.Code);
                Assert.False(redispatch.IsAllowed);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task Missing_actual_permissions_service_mounts_repair_state_without_decision_controls()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-home-approvals-unavailable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var permissions = new HomePermissionTrustService(store, (_, _) => null);
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(store)]);
            using var surface = new HomeApprovalCuiSurface(runtime, profiles, permissions);
            await surface.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.Equal(CuiSceneAvailabilityState.Unavailable, Assert.IsType<CuiSceneHost>(surface.Content).Availability!.State);
            Assert.Empty(surface.GetVisualDescendants().OfType<Button>());
        }
        finally { Directory.Delete(root, true); }
    }
}
