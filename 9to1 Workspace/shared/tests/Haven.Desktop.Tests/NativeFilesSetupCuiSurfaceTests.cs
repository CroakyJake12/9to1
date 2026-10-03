using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using System.Text.Json;

namespace Haven.Desktop.Tests;

public sealed class NativeFilesSetupCuiSurfaceTests
{
    [AvaloniaFact]
    public async Task Restored_existing_Files_requires_actual_Home_approval_before_native_Cui_finish_can_bind_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-files-import-cui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var chosen = Path.Combine(root, "original-workspace");
            Directory.CreateDirectory(chosen);
            var homePath = Path.Combine(root, "home.json");
            var home = new FileHomeCoreStateStore(homePath);
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var files = new NativeFilesWorkspaceService(home, profiles);
            var permissions = new HomePermissionTrustService(home, (app, action) =>
                app == "9to1.home.local-profile" && action == "home.profile.importStore"
                    ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null);
            var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([files]), permissions);
            var original = await files.ConfigureNewAsync(chosen, ownership, token);
            var userPath = Path.Combine(chosen, "Sites", "existing-user.txt");
            await File.WriteAllTextAsync(userPath, "Preserve this existing user content", token);
            // Simulate restoring Home's configuration without its ownership binding. This is not a new Files store.
            var snapshot = (await home.ReadAsync(token)).State!;
            await File.WriteAllTextAsync(homePath, JsonSerializer.Serialize(snapshot with { Revision = snapshot.Revision + 1,
                Records = snapshot.Records.Where(record => record.RecordType != "home.local-store-ownership").ToArray() },
                new JsonSerializerOptions()), token);
            var authority = new NativeFilesWorkspaceAuthority(files, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
            Assert.Null(await authority.GetCurrentAsync(token));
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
            using var surface = new NativeFilesSetupCuiSurface(runtime, profiles, files, authority, ownership,
                _ => throw new InvalidOperationException("Existing configured data must not invoke the new-workspace picker."), _ => Task.CompletedTask);
            await surface.InitializeAsync(token);
            var window = new Window { Content = surface, Width = 800, Height = 620 };
            window.Show();
            try
            {
                var requestButton = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Request ownership import"));
                requestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                HomePermissionManagementSnapshot? approval = null;
                for (var attempt = 0; attempt < 100 && approval?.PendingRequests.Count != 1; attempt++)
                {
                    await Task.Delay(10, token);
                    approval = await permissions.GetSnapshotAsync(cancellationToken: token);
                }
                var request = Assert.Single(approval!.PendingRequests);
                var finish = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Finish approved ownership import"));
                finish.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(30, token);
                Assert.Null(await authority.GetCurrentAsync(token));
                Assert.True((await permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
                finish.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                NativeFilesWorkspace? imported = null;
                for (var attempt = 0; attempt < 100 && imported is null; attempt++)
                {
                    await Task.Delay(10, token);
                    imported = await authority.GetCurrentAsync(token);
                }
                Assert.NotNull(imported);
                Assert.Equal(original.Configuration.StoreId, imported.Configuration.StoreId);
                Assert.Equal(original.Configuration.AppFolders["sites"], imported.Configuration.AppFolders["sites"]);
                Assert.Equal("Preserve this existing user content", await File.ReadAllTextAsync(userPath, token));
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task Disposing_during_native_picker_cancels_dispatch_without_creating_storage()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-files-close-cui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var files = new NativeFilesWorkspaceService(home, profiles);
            var permissions = new HomePermissionTrustService(home, (_, _) => null);
            var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([files]), permissions);
            var authority = new NativeFilesWorkspaceAuthority(files, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var surface = new NativeFilesSetupCuiSurface(runtime, profiles, files, authority, ownership,
                async cancellationToken =>
                {
                    entered.SetResult();
                    try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                    finally { cancelled.SetResult(); }
                    return null;
                }, _ => Task.CompletedTask);
            await surface.InitializeAsync(token);
            var window = new Window { Content = surface, Width = 800, Height = 620 };
            window.Show();
            try
            {
                var choose = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Set up Files in an empty folder"));
                choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await entered.Task.WaitAsync(token);
                // A second click waits for the same owner gate while the picker is active.
                choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                surface.Dispose();
                await cancelled.Task.WaitAsync(token);
                // Drain the async click continuation, including the cancelled owner dispatch.
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Null(await files.GetConfiguredAsync(token));
                Assert.Null(await authority.GetCurrentAsync(token));
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task Actual_Cui_setup_click_uses_native_choice_and_real_Home_new_empty_binding_without_fake_account()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-files-setup-cui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var chosen = Path.Combine(root, "explicit-native-choice");
            Directory.CreateDirectory(chosen);
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var files = new NativeFilesWorkspaceService(home, profiles);
            var permissions = new HomePermissionTrustService(home, (_, _) => null);
            var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([files]), permissions);
            var authority = new NativeFilesWorkspaceAuthority(files, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(home), new HomePermissionsCoreService(permissions, profiles)]);
            var choices = 0;
            using var surface = new NativeFilesSetupCuiSurface(runtime, profiles, files, authority, ownership,
                cancellationToken =>
                {
                    Dispatcher.UIThread.VerifyAccess();
                    cancellationToken.ThrowIfCancellationRequested();
                    choices++;
                    return Task.FromResult<string?>(chosen);
                }, _ => throw new InvalidOperationException("New empty setup must not request an existing-store approval."));
            await surface.InitializeAsync(token);
            Assert.Equal(CuiSceneAvailabilityState.Ready, Assert.IsType<CuiSceneHost>(surface.Content).Availability!.State);
            var window = new Window { Content = surface, Width = 800, Height = 620 };
            window.Show();
            try
            {
                var choose = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Set up Files in an empty folder"));
                choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                NativeFilesWorkspace? workspace = null;
                for (var attempt = 0; attempt < 100 && workspace is null; attempt++)
                {
                    await Task.Delay(10, token);
                    workspace = await authority.GetCurrentAsync(token);
                }
                Assert.NotNull(workspace);
                Assert.Null(workspace.Actor.AccountId);
                Assert.Null(workspace.Actor.OrganisationId);
                Assert.Equal(chosen, workspace.Configuration.RootDirectory);
                Assert.Equal(Path.Combine(chosen, "Sites"), await authority.ResolveAppDirectoryAsync("sites", token));
                Assert.Equal(1, choices);
                Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
                choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(30, token);
                Assert.Equal(1, choices);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }
}
