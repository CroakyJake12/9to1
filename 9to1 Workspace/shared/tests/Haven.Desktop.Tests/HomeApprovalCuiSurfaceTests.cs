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
    public async Task Exact_native_prompt_is_audited_only_after_visible_mount_and_hidden_button_cannot_approve()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-home-visible-prompt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var actor = (await profiles.GetCurrentAsync(token))!;
            var permissions = new HomePermissionTrustService(store, (app, action) => app == "write" && action == "write.file.save"
                ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, true, false, true) : null);
            var target = new HomeObjectReference("files.item", "file-42");
            var request = await permissions.AuthorizeAsync(new(null,
                new(actor.ActorId, "Local writer", actor.ProfileId, actor.AuthenticationRevision, true), "actual-visible-test",
                new("write", "write.file.save", [target]), new(["files.item"], 1, [target], false, "Save file-42")), token);
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(store), new HomePermissionsCoreService(permissions, profiles)]);
            using var surface = new HomeApprovalCuiSurface(runtime, profiles, permissions);
            await surface.InitializeAsync(token);
            Assert.True(await surface.FocusRequestAsync(request.RequestId, token));
            Assert.False(await surface.AcknowledgeDisplayedRequestAsync(token));
            Assert.DoesNotContain((await permissions.GetSnapshotAsync(cancellationToken: token)).RecentAuditEvents,
                item => item.Kind == HomePermissionAuditKind.ApprovalPromptShown);
            var window = new Window { Content = surface, Width = 900, Height = 720 };
            window.Show();
            try
            {
                Assert.True(await surface.AcknowledgeDisplayedRequestAsync(token));
                var text = surface.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.Equal("Target app: write", Assert.Single(text, item => item.Name == "approval-target").Text);
                Assert.Equal("Action: write.file.save", Assert.Single(text, item => item.Name == "approval-action").Text);
                Assert.Equal("Scope: files.item:file-42", Assert.Single(text, item => item.Name == "approval-scope").Text);
                Assert.Contains(actor.ActorId, Assert.Single(text, item => item.Name == "approval-identity").Text
                    ?? throw new InvalidDataException("The actual caller identity label is missing."));
                var acknowledged = await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token);
                Assert.True(await surface.AcknowledgeDisplayedRequestAsync(token));
                Assert.Equal(acknowledged, await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token));
                var snapshot = await permissions.GetSnapshotAsync(cancellationToken: token);
                var audit = Assert.Single(snapshot.RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.ApprovalPromptShown);
                Assert.Equal("HOME_PROMPT_DISPLAY_ACKNOWLEDGED", audit.ResultCode);
                Assert.Equal("write", audit.TargetAppId); Assert.Equal("write.file.save", audit.ActionName);
                Assert.Equal(target, Assert.Single(audit.AffectedObjects));
                Assert.Equal(HomePermissionRequestState.PendingApproval, (await permissions.GetAuthorizationAsync(request.RequestId, token)).State);
                var accept = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Accept once"));
                window.Hide();
                Assert.False(await surface.AcknowledgeDisplayedRequestAsync(token));
                accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var status = Assert.Single(surface.GetVisualDescendants().OfType<TextBlock>(), item => item.Name == "approval-status");
                for (var attempt = 0; attempt < 100 && status.Text?.Contains("not visibly displayed", StringComparison.Ordinal) != true; attempt++)
                    await Task.Delay(10, token);
                Assert.Contains("not visibly displayed", status.Text
                    ?? throw new InvalidDataException("The actual visibility refusal status is missing."));
                Assert.Equal(HomePermissionRequestState.PendingApproval, (await permissions.GetAuthorizationAsync(request.RequestId, token)).State);
                Assert.DoesNotContain((await permissions.GetSnapshotAsync(cancellationToken: token)).RecentAuditEvents,
                    item => item.Kind is HomePermissionAuditKind.DecisionMade or HomePermissionAuditKind.ExecutionStarted);
                Assert.False((await permissions.BeginExecutionAsync(request.RequestId, token)).IsAllowed);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }

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
            Assert.IsAssignableFrom<HavenOS.Home.NativeUI.HomeApprovalCuiSurface>(surface);
            Assert.True(await surface.FocusRequestAsync(request.RequestId, token));
            Assert.Equal(HomePermissionRequestState.PendingApproval, (await permissions.GetAuthorizationAsync(request.RequestId, token)).State);
            Assert.False(await surface.FocusRequestAsync("missing-request", token));
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
