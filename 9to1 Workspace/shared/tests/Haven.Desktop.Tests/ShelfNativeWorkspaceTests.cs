using Microsoft.Extensions.DependencyInjection;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Application.Shelf;
using Haven.Core.Shelf;
using Haven.Infrastructure;
using Haven.Desktop.Views.Pages.Shelf;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
namespace Haven.Desktop.Tests;
public sealed class ShelfNativeWorkspaceTests
{
    [AvaloniaFact]
    public async Task Canonical_launcher_keyboard_opens_actual_Shelf_host_without_approving_or_writing()
    {
        var testCancellation = TestContext.Current.CancellationToken;
        using var f = new Fixture(); await f.InitializeAsync();
        using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton(f.Owner).AddSingleton<IAuthenticatedResourceActorSource>(f.Profiles).BuildServiceProvider();
        var beforeHome = await File.ReadAllBytesAsync(f.HomeFile);
        var beforeSettings = await File.ReadAllBytesAsync(f.SettingsFile);
        var launcher = new Haven.Desktop.Views.Shell.TopRail.AppLauncherControl();
        Task<ShelfNativeWorkspaceHost>? opening = null;
        var current = true;
        launcher.Configure(BuiltInModeSeed.Modes.ToArray(), new HashSet<Guid>(), false, (app, _) =>
        {
            var route = Haven.Desktop.Views.Shell.HavenAppRoutePolicy.Resolve(app);
            Assert.Equal(Haven.Desktop.Views.Shell.HavenAppRouteKind.Shelf, route.Kind);
            Assert.Equal(Haven.Core.HavenSurface.Shelf, route.Surface);
            opening = Haven.Desktop.Views.Shell.MainView.CreateShelfWorkspaceAsync(services, f.Actor,
                _ => throw new InvalidOperationException("Opening must not request Home review."),
                action => { action(); return Task.CompletedTask; }, () => current, default);
        }, () => { });
        var window = new Window { Content = launcher }; window.Show(); window.UpdateLayout();
        ShelfNativeWorkspaceHost? host = null;
        try
        {
            var button = Assert.Single(launcher.HavenScene.AppButtons, item => item.Content == "Shelf");
            Assert.True(launcher.SceneHost.FocusElement(button));
            window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, "Enter");
            window.KeyRelease(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, "Enter");
            Assert.NotNull(opening);
            host = await opening.WaitAsync(TimeSpan.FromSeconds(10), testCancellation);
            window.Content = host; window.UpdateLayout();
            Assert.Empty(host.Reviews);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBox>(), item => item.Name == "shelf-name");
            Assert.Equal(0, f.Held.GuardedWriteCalls);
            Assert.Equal(beforeHome, await File.ReadAllBytesAsync(f.HomeFile));
            Assert.Equal(beforeSettings, await File.ReadAllBytesAsync(f.SettingsFile));
            current = false;
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                Haven.Desktop.Views.Shell.MainView.CreateShelfWorkspaceAsync(services, f.Actor,
                    _ => Task.CompletedTask, action => { action(); return Task.CompletedTask; }, () => current, default));
            Assert.Equal(beforeHome, await File.ReadAllBytesAsync(f.HomeFile));
            Assert.Equal(beforeSettings, await File.ReadAllBytesAsync(f.SettingsFile));
        }
        finally
        {
            current = false;
            try
            {
                if (host is null && opening is not null)
                {
                    try { host = await opening; }
                    catch { /* Observe the original opening failure without replacing the primary assertion. */ }
                }
            }
            finally { host?.Dispose(); window.Close(); }
        }
    }


    [AvaloniaFact]
    public async Task Real_rendered_target_actions_require_Home_then_reload_canonical_identity_without_Finish_replay()
    {
        using var f = new Fixture(); await f.InitializeAsync();
        string? opened = null;
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor,
            id => { opened = id; return Task.CompletedTask; }, action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        try
        {
            await Fill(host); var before = await File.ReadAllBytesAsync(f.SettingsFile);
            await Click(host, "Review web target"); var review = Assert.Single(host.Reviews);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            await Click(host, "Review in Home"); Assert.Equal(review.RequestID, opened);
            await Click(host, "Finish audit"); Assert.Equal("NoAttemptedOutcome", Assert.Single(host.Reviews).LastObservation!.Code);
            await Click(host, "Apply approved request or recover outcome");
            Assert.Equal("ApprovalRequired", Assert.Single(host.Reviews).LastObservation!.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.True((await f.Permissions.DecideAsync(review.RequestID, HomeApprovalChoice.Accept)).Succeeded);
            await Click(host, "Apply approved request or recover outcome");
            Assert.True(Assert.Single(host.Reviews).LastObservation!.Committed);
            var committed = await File.ReadAllBytesAsync(f.SettingsFile);
            await Click(host, "Reload library");
            var item = Assert.Single((await f.Library.ReadAsync()).Library.Items);
            Assert.Equal("https://example.test/reference", item.Target.Uri);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), x => x.Text?.Contains(item.Id.ToString("D")) == true);
            await Click(host, "Finish audit"); Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
            host.Dispose();
            Assert.True((await host.FinishRetainedAsync(review.RequestID)).Committed);
            await host.OpenRetainedHomeReviewAsync(review.RequestID); Assert.Equal(review.RequestID, opened);
            Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task Real_rendered_pending_request_survives_tab_close_for_exact_Home_decline_without_owner_write()
    {
        using var f = new Fixture(); await f.InitializeAsync();
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor, _ => Task.CompletedTask,
            action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        try
        {
            await Fill(host); await Click(host, "Review web target");
            var id = Assert.Single(host.Reviews).RequestID;
            var before = await File.ReadAllBytesAsync(f.SettingsFile);
            var retained = Assert.Single(host.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Apply approved request or recover outcome"));
            host.Dispose(); retained.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await host.WhenActionsIdleAsync();
            Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Decline)).Succeeded);
            Assert.Equal(id, Assert.Single(host.Reviews).RequestID);
            Assert.Equal("NoAttemptedOutcome", (await host.FinishRetainedAsync(id)).Code);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => host.FinishRetainedAsync(Guid.NewGuid().ToString("N")));
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile)); Assert.Empty((await f.Library.ReadAsync()).Library.Items);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_tab_close_during_original_identity_or_final_publication_denies_without_physical_write(bool finalPublication)
    {
        var testCancellation = TestContext.Current.CancellationToken;
        using var f = new Fixture(); await f.InitializeAsync();
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor, _ => Task.CompletedTask,
            action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        try
        {
            await Fill(host);
            if (finalPublication)
            {
                await Click(host, "Review web target");
                var id = Assert.Single(host.Reviews).RequestID;
                Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Accept)).Succeeded);
                f.Held.HoldPublication = true;
            }
            else f.Held.HoldIdentity = true;
            var before = await File.ReadAllBytesAsync(f.SettingsFile);
            var home = await File.ReadAllBytesAsync(f.HomeFile);
            var button = Assert.Single(host.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content,
                finalPublication ? "Apply approved request or recover outcome" : "Review web target"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var accepted = host.WhenActionsIdleAsync();
            try { await f.Held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), testCancellation); host.Dispose(); }
            catch
            {
                f.Held.Release.TrySetResult();
                try { await accepted; } catch { } // Preserve primary failure after observing the action.
                throw;
            }
            finally { f.Held.Release.TrySetResult(); }
            await accepted;
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.Empty((await f.Library.ReadAsync()).Library.Items);
            if (!finalPublication)
            {
                Assert.Empty(host.Reviews);
                Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile));
                Assert.Equal(0, f.Held.GuardedWriteCalls);
            }
            else
            {
                Assert.Equal(1, f.Held.GuardedWriteCalls);
                Assert.False(Assert.Single(host.Reviews).LastObservation?.Committed == true);
            }
        }
        finally { f.Held.Release.TrySetResult(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task Old_rendered_display_refuses_independently_Home_approved_replacement_root_without_new_pending_or_foreign_write()
    {
        using var f = new Fixture(); await f.InitializeAsync();
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor, _ => Task.CompletedTask,
            action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        try
        {
            await Fill(host);
            var envelope = JsonNode.Parse(await File.ReadAllTextAsync(f.SettingsFile))!.AsObject();
            var foreignID = Guid.NewGuid();
            envelope[nameof(SettingsExportManifest.StoreIdentity)]![nameof(SettingsStoreIdentity.StoreId)] = foreignID;
            var foreign = JsonSerializer.SerializeToUtf8Bytes(envelope);
            await File.WriteAllBytesAsync(f.SettingsFile, foreign);
            // Actual repository refuses the first observed substitution; do not pretend automatic adoption.
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Held.GetStoreIdentityAsync(default).AsTask());
            Assert.Equal(foreign, await File.ReadAllBytesAsync(f.SettingsFile));
            var import = await f.Ownership.RequestImportAsync(f.Actor, "shelf", foreignID.ToString("D"), "actual-native-foreign-root");
            Assert.Equal(HomePermissionRequestState.PendingApproval, import.State);
            Assert.True((await f.Permissions.DecideAsync(import.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            await f.Ownership.CompleteImportAsync(import.RequestId);
            Assert.NotNull(await f.Ownership.GetVerifiedAsync("shelf", foreignID.ToString("D")));
            // Genuine newly loaded private context is now admitted; stale displayed origin must independently deny.
            var current = await f.Owner.LoadForDisplayAsync(f.Actor); current.Selection.Dispose();
            var home = await File.ReadAllBytesAsync(f.HomeFile);
            await Click(host, "Review web target"); await Click(host, "Reload library");
            Assert.Empty(host.Reviews);
            Assert.Equal(foreign, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile));
            Assert.Equal(0, f.Held.GuardedWriteCalls);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task Actual_delayed_UI_publication_after_close_preserves_pending_owner_handle_without_late_presentation()
    {
        var testCancellation = TestContext.Current.CancellationToken;
        using var f = new Fixture(); await f.InitializeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor, _ => Task.CompletedTask,
            async action => { entered.TrySetResult(); await release.Task; action(); });
        var window = new Window { Content = host }; window.Show();
        try
        {
            await Fill(host); var before = await File.ReadAllBytesAsync(f.SettingsFile);
            var button = Assert.Single(host.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Review web target"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var accepted = host.WhenActionsIdleAsync();
            string id;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), testCancellation); id = Assert.Single(host.Reviews).RequestID;
                Assert.Equal("Ready", host.PresentationStatus); Assert.Equal("", host.PresentedRequestID);
                host.Dispose();
            }
            catch
            {
                release.TrySetResult();
                try { await accepted; } catch { } // Preserve primary failure before fixture deletion.
                throw;
            }
            finally { release.TrySetResult(); }
            await accepted;
            Assert.Equal("Ready", host.PresentationStatus); Assert.Equal("", host.PresentedRequestID);
            Assert.Equal(id, Assert.Single(host.Reviews).RequestID);
            Assert.True((await f.Permissions.DecideAsync(id, HomeApprovalChoice.Decline)).Succeeded);
            Assert.Equal("NoAttemptedOutcome", (await host.FinishRetainedAsync(id)).Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
        }
        finally { release.TrySetResult(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task Genuine_rendered_manual_collection_and_canonical_membership_require_individual_Home_and_survive_reopen_without_replay()
    {
        using var f = new Fixture(); await f.InitializeAsync(); string? opened = null;
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor,
            id => { opened = id; return Task.CompletedTask; }, action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        try
        {
            await Fill(host); await Click(host, "Review web target");
            var itemRequest = Assert.Single(host.Reviews).RequestID;
            Assert.True((await f.Permissions.DecideAsync(itemRequest, HomeApprovalChoice.Accept)).Succeeded);
            await Click(host, "Apply approved request or recover outcome"); Assert.True(host.Reviews.Last().LastObservation!.Committed);
            await Click(host, "Reload library");
            var item = Assert.Single((await f.Library.ReadAsync()).Library.Items);
            var name = Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "shelf-collection-name");
            name.Text = "Original course resources";
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            var before = await File.ReadAllBytesAsync(f.SettingsFile);
            await Click(host, "Review manual collection"); var collectionRequest = host.Reviews.Last().RequestID;
            Assert.Equal(2, host.Reviews.Count); Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            await Click(host, "Review in Home"); Assert.Equal(collectionRequest, opened);
            name.Text = "Later unrelated draft";
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            await Click(host, "Apply approved request or recover outcome");
            Assert.Equal("ApprovalRequired", host.Reviews.Last().LastObservation!.Code); Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.True((await f.Permissions.DecideAsync(collectionRequest, HomeApprovalChoice.Accept)).Succeeded);
            await Click(host, "Apply approved request or recover outcome"); Assert.True(host.Reviews.Last().LastObservation!.Committed);
            Assert.True(host.Reviews.Last().LastObservation!.AuditRecorded);
            Assert.Equal("Saved to Shelf.", host.PresentationStatus);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Saved to Shelf.");
            await Click(host, "Reload library");
            var collection = Assert.Single((await f.Library.ReadAsync()).Library.Collections);
            Assert.Equal("Original course resources", collection.Name);
            var collectionChoice = Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => x.Name == "shelf-membership-collection");
            var itemChoice = Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => x.Name == "shelf-membership-item");
            collectionChoice.SelectedIndex = 0; itemChoice.SelectedIndex = 0;
            before = await File.ReadAllBytesAsync(f.SettingsFile);
            await Click(host, "Review collection membership"); var membershipRequest = host.Reviews.Last().RequestID;
            Assert.Equal(3, host.Reviews.Count); Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            collectionChoice.SelectedIndex = -1; itemChoice.SelectedIndex = -1;
            Assert.True((await f.Permissions.DecideAsync(membershipRequest, HomeApprovalChoice.Accept)).Succeeded);
            await Click(host, "Apply approved request or recover outcome"); Assert.True(host.Reviews.Last().LastObservation!.Committed);
            Assert.True(host.Reviews.Last().LastObservation!.AuditRecorded); await Click(host, "Reload library");
            var reopened = await new ShelfLibraryService(new VersionedAtomicSettingsStore(f.Paths)).ReadAsync();
            var member = Assert.Single(reopened.Library.Memberships); Assert.Equal(collection.Id, member.CollectionId); Assert.Equal(item.Id, member.LaunchItemId);
            Assert.Equal("Original course resources", Assert.Single(reopened.Library.Collections).Name);
            var committed = await File.ReadAllBytesAsync(f.SettingsFile); await Click(host, "Finish audit");
            Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
            name.Text = "Second development collection";
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            await Click(host, "Review manual collection"); var secondCollectionRequest = host.Reviews.Last().RequestID;
            Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.True((await f.Permissions.DecideAsync(secondCollectionRequest, HomeApprovalChoice.Accept)).Succeeded);
            await Click(host, "Apply approved request or recover outcome");
            Assert.True(host.Reviews.Last().LastObservation!.Committed); Assert.True(host.Reviews.Last().LastObservation!.AuditRecorded);
            await Click(host, "Reload library");
            collectionChoice.SelectedIndex = collectionChoice.Items.Cast<string>().ToList().IndexOf("Second development collection");
            Assert.True(collectionChoice.SelectedIndex >= 0); itemChoice.SelectedIndex = 0;
            var beforeSecondMembership = await File.ReadAllBytesAsync(f.SettingsFile);
            await Click(host, "Review collection membership"); var secondMembershipRequest = host.Reviews.Last().RequestID;
            Assert.Equal(beforeSecondMembership, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.True((await f.Permissions.DecideAsync(secondMembershipRequest, HomeApprovalChoice.Accept)).Succeeded);
            await Click(host, "Apply approved request or recover outcome");
            Assert.True(host.Reviews.Last().LastObservation!.Committed); Assert.True(host.Reviews.Last().LastObservation!.AuditRecorded);
            reopened = await new ShelfLibraryService(new VersionedAtomicSettingsStore(f.Paths)).ReadAsync();
            Assert.Equal(item.Id, Assert.Single(reopened.Library.Items).Id); Assert.Equal(2, reopened.Library.Collections.Count);
            Assert.Equal(2, reopened.Library.Memberships.Count);
            Assert.All(reopened.Library.Memberships, membership => Assert.Equal(item.Id, membership.LaunchItemId));
            Assert.Equal(2, reopened.Library.Memberships.Select(membership => membership.CollectionId).Distinct().Count());
            Assert.Contains(reopened.Library.Memberships, membership => membership.CollectionId == collection.Id);
            Assert.Contains(reopened.Library.Collections, group => group.Name == "Second development collection");
            committed = await File.ReadAllBytesAsync(f.SettingsFile);
            host.Dispose(); Assert.True((await host.FinishRetainedAsync(membershipRequest)).Committed);
            Assert.True((await host.FinishRetainedAsync(secondMembershipRequest)).Committed);
            Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Genuine_rendered_pending_collection_survives_close_and_exact_decline_without_collection_publication()
    {
        using var f = new Fixture(); await f.InitializeAsync();
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor, _ => Task.CompletedTask,
            action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        try
        {
            var name = Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "shelf-collection-name");
            name.Text = "Pending course resources";
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            var before = await File.ReadAllBytesAsync(f.SettingsFile);
            await Click(host, "Review manual collection"); var request = Assert.Single(host.Reviews).RequestID;
            host.Dispose(); Assert.True((await f.Permissions.DecideAsync(request, HomeApprovalChoice.Decline)).Succeeded);
            Assert.Equal("NoAttemptedOutcome", (await host.FinishRetainedAsync(request)).Code);
            Assert.Equal(request, Assert.Single(host.Reviews).RequestID);
            Assert.Empty((await f.Library.ReadAsync()).Library.Collections);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Genuine_native_collection_proposal_is_captured_before_actual_original_identity_await(bool membership)
    {
        var testCancellation = TestContext.Current.CancellationToken;
        using var f = new Fixture(); await f.InitializeAsync();
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor, _ => Task.CompletedTask,
            action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        Task? accepted = null;
        try
        {
            var name = Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "shelf-collection-name");
            name.Text = "Original captured collection";
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            if (membership)
            {
                await Fill(host); await Click(host, "Review web target");
                Assert.True((await f.Permissions.DecideAsync(host.Reviews.Last().RequestID, HomeApprovalChoice.Accept)).Succeeded);
                await Click(host, "Apply approved request or recover outcome"); Assert.True(host.Reviews.Last().LastObservation!.Committed);
                await Click(host, "Reload library"); await Click(host, "Review manual collection");
                Assert.True((await f.Permissions.DecideAsync(host.Reviews.Last().RequestID, HomeApprovalChoice.Accept)).Succeeded);
                await Click(host, "Apply approved request or recover outcome"); Assert.True(host.Reviews.Last().LastObservation!.Committed);
                await Click(host, "Reload library");
                Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => x.Name == "shelf-membership-collection").SelectedIndex = 0;
                Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => x.Name == "shelf-membership-item").SelectedIndex = 0;
            }
            var original = await f.Library.ReadAsync(); var before = await File.ReadAllBytesAsync(f.SettingsFile);
            var reviews = host.Reviews.Count; f.Held.HoldIdentity = true;
            var button = Assert.Single(host.GetVisualDescendants().OfType<Button>(), x => Equals(x.Content,
                membership ? "Review collection membership" : "Review manual collection"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); accepted = host.WhenActionsIdleAsync();
            await f.Held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), testCancellation); Assert.False(accepted.IsCompleted);
            name.Text = "Later unrelated collection draft";
            if (membership)
            {
                Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => x.Name == "shelf-membership-collection").SelectedIndex = -1;
                Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => x.Name == "shelf-membership-item").SelectedIndex = -1;
            }
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            f.Held.Release.TrySetResult(); await accepted.WaitAsync(TimeSpan.FromSeconds(10), testCancellation);
            Assert.Equal(reviews + 1, host.Reviews.Count); var request = host.Reviews.Last().RequestID;
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.True((await f.Permissions.DecideAsync(request, HomeApprovalChoice.Accept)).Succeeded);
            await Click(host, "Apply approved request or recover outcome"); Assert.True(host.Reviews.Last().LastObservation!.Committed);
            var reopened = await new ShelfLibraryService(new VersionedAtomicSettingsStore(f.Paths)).ReadAsync();
            Assert.Equal("Original captured collection", Assert.Single(reopened.Library.Collections).Name);
            if (membership)
            {
                var member = Assert.Single(reopened.Library.Memberships);
                Assert.Equal(Assert.Single(original.Library.Collections).Id, member.CollectionId);
                Assert.Equal(Assert.Single(original.Library.Items).Id, member.LaunchItemId);
            }
            else Assert.Empty(reopened.Library.Memberships);
        }
        catch
        {
            f.Held.Release.TrySetResult();
            if (accepted is not null) { try { await accepted; } catch { } }
            throw;
        }
        finally { f.Held.Release.TrySetResult(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Actual_native_unknown_save_guidance_retains_original_receipt_recovery_without_replaying_publication()
    {
        using var f = new Fixture(); await f.InitializeAsync();
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor, _ => Task.CompletedTask,
            action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        try
        {
            await Fill(host); await Click(host, "Review web target");
            var original = Assert.Single(host.Reviews).RequestID;
            Assert.True((await f.Permissions.DecideAsync(original, HomeApprovalChoice.Accept)).Succeeded);
            f.Held.LoseWriteReturnOnce = true; f.Held.HideAfterLostReturn = true;
            await Click(host, "Apply approved request or recover outcome");
            var unknown = Assert.Single(host.Reviews).LastObservation!;
            Assert.True(unknown.CompletionUnknown); Assert.False(unknown.Committed); Assert.False(unknown.AuditRecorded);
            const string guidance = "The save outcome is unresolved. Recover this original request before making another save.";
            Assert.Equal(guidance, host.PresentationStatus);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == guidance);
            var committed = await File.ReadAllBytesAsync(f.SettingsFile);
            var actual = await new ShelfLibraryService(new VersionedAtomicSettingsStore(f.Paths)).ReadAsync();
            Assert.Equal("https://example.test/reference", Assert.Single(actual.Library.Items).Target.Uri);
            Assert.Equal(1, f.Held.GuardedWriteCalls);
            f.Held.HideLibraryReads = false;
            await Click(host, "Finish audit");
            var known = Assert.Single(host.Reviews).LastObservation!;
            Assert.True(known.Committed); Assert.True(known.AuditRecorded); Assert.Equal(original, Assert.Single(host.Reviews).RequestID);
            Assert.Equal("Saved to Shelf.", host.PresentationStatus);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Saved to Shelf.");
            Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile)); Assert.Equal(1, f.Held.GuardedWriteCalls);
            await Click(host, "Finish audit");
            Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile)); Assert.Equal(1, f.Held.GuardedWriteCalls);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_native_pin_tag_order_review_keeps_original_edit_across_real_identity_wait_and_recovers_ack(bool loseReturn)
    {
        var testCancellation = TestContext.Current.CancellationToken;
        using var f = new Fixture(); await f.InitializeAsync();
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor,
            _ => Task.CompletedTask, action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show(); Task? reviewing = null;
        try
        {
            await Fill(host); await Click(host, "Review web target");
            var seed = Assert.Single(host.Reviews).RequestID;
            Assert.True((await f.Permissions.DecideAsync(seed, HomeApprovalChoice.Accept)).Succeeded);
            await Click(host, "Apply approved request or recover outcome"); await Click(host, "Reload library");
            var original = Assert.Single((await f.Library.ReadAsync()).Library.Items);
            var selector = Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => x.Name == "shelf-membership-item");
            selector.SelectedIndex = 0;
            var name = Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "shelf-edit-name");
            var tags = Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "shelf-edit-tags");
            var order = Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "shelf-edit-order");
            var favourite = Assert.Single(host.GetVisualDescendants().OfType<CheckBox>(), x => x.Name == "shelf-edit-favourite");
            name.Text = "Pinned reference"; tags.Text = "work, reference"; order.Text = "7"; favourite.IsChecked = true;
            var behaviour = Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => x.Name == "shelf-edit-behaviour");
            behaviour.SelectedIndex = (int)ShelfLaunchBehaviour.Reveal;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            var before = await File.ReadAllBytesAsync(f.SettingsFile); var writes = f.Held.GuardedWriteCalls;
            f.Held.HoldIdentity = true;
            var button = Assert.Single(host.GetVisualDescendants().OfType<Button>(), x => Equals(x.Content, "Review pin, tags and order"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); reviewing = host.WhenActionsIdleAsync();
            await f.Held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), testCancellation); Assert.False(reviewing.IsCompleted);
            name.Text = "Later draft"; tags.Text = "replacement"; order.Text = "99"; favourite.IsChecked = false; selector.SelectedIndex = -1;
            behaviour.SelectedIndex = (int)ShelfLaunchBehaviour.Launch;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            f.Held.Release.TrySetResult(); await reviewing;
            var request = host.Reviews.Last().RequestID; Assert.NotEqual(seed, request); Assert.Equal(2, host.Reviews.Count);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile)); Assert.Equal(writes, f.Held.GuardedWriteCalls);
            await Click(host, "Apply approved request or recover outcome"); Assert.Equal("ApprovalRequired", host.Reviews.Last().LastObservation!.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.True((await f.Permissions.DecideAsync(request, HomeApprovalChoice.Accept)).Succeeded);
            f.Held.LoseWriteReturnOnce = loseReturn;
            await Click(host, "Apply approved request or recover outcome");
            Assert.True(host.Reviews.Last().LastObservation!.Committed); Assert.True(host.Reviews.Last().LastObservation!.AuditRecorded);
            var actual = Assert.Single((await new ShelfLibraryService(new VersionedAtomicSettingsStore(f.Paths)).ReadAsync()).Library.Items);
            Assert.Equal(original.Id, actual.Id); Assert.Equal(original.Target, actual.Target);
            Assert.Equal("Pinned reference", actual.Name); Assert.True(actual.IsFavourite); Assert.Equal(7, actual.Order);
            Assert.Equal(new[] { "work", "reference" }, actual.Tags); Assert.Equal(ShelfLaunchBehaviour.Reveal, actual.Behaviour);
            var committed = await File.ReadAllBytesAsync(f.SettingsFile);
            await Click(host, "Finish audit"); Assert.True(host.Reviews.Last().LastObservation!.Committed);
            Assert.Equal(writes + 1, f.Held.GuardedWriteCalls); Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
        }
        catch
        {
            f.Held.Release.TrySetResult(); if (reviewing is not null) { try { await reviewing; } catch { } }
            throw;
        }
        finally { f.Held.Release.TrySetResult(); window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_native_smart_criteria_are_original_before_await_and_filter_only_canonical_local_matches(bool loseReturn)
    {
        var testCancellation = TestContext.Current.CancellationToken;
        using var f = new Fixture(); await f.InitializeAsync();
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor,
            _ => Task.CompletedTask, action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show(); Task? reviewing = null;
        async Task ApproveLatest()
        {
            Assert.True((await f.Permissions.DecideAsync(host.Reviews.Last().RequestID, HomeApprovalChoice.Accept)).Succeeded);
            await Click(host, "Apply approved request or recover outcome"); await Click(host, "Reload library");
        }
        try
        {
            await Fill(host); await Click(host, "Review web target"); await ApproveLatest();
            var original = Assert.Single((await f.Library.ReadAsync()).Library.Items);
            Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => x.Name == "shelf-membership-item").SelectedIndex = 0;
            Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "shelf-edit-tags").Text = "work, offline";
            Assert.Single(host.GetVisualDescendants().OfType<CheckBox>(), x => x.Name == "shelf-edit-favourite").IsChecked = true;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            await Click(host, "Review pin, tags and order"); await ApproveLatest();
            Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "shelf-name").Text = "Other site";
            Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "shelf-address").Text = "https://example.test/other";
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            await Click(host, "Review web target"); await ApproveLatest();
            var name = Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "shelf-collection-name");
            var tags = Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "shelf-smart-tags");
            var kind = Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => x.Name == "shelf-smart-kind");
            var favourite = Assert.Single(host.GetVisualDescendants().OfType<CheckBox>(), x => x.Name == "shelf-smart-favourites");
            var offline = Assert.Single(host.GetVisualDescendants().OfType<CheckBox>(), x => x.Name == "shelf-smart-offline");
            name.Text = "Original smart collection"; tags.Text = "work";
            kind.SelectedIndex = Array.IndexOf(Enum.GetValues<ShelfTargetKind>(), ShelfTargetKind.WebAddress) + 1;
            favourite.IsChecked = true; offline.IsChecked = true;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            var before = await File.ReadAllBytesAsync(f.SettingsFile); var writes = f.Held.GuardedWriteCalls;
            f.Held.HoldIdentity = true;
            var button = Assert.Single(host.GetVisualDescendants().OfType<Button>(), x => Equals(x.Content, "Review smart collection"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); reviewing = host.WhenActionsIdleAsync();
            await f.Held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), testCancellation); Assert.False(reviewing.IsCompleted);
            name.Text = "Later collection"; tags.Text = "replacement"; kind.SelectedIndex = 0;
            favourite.IsChecked = false; offline.IsChecked = false;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            f.Held.Release.TrySetResult(); await reviewing;
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile)); Assert.Equal(writes, f.Held.GuardedWriteCalls);
            await Click(host, "Apply approved request or recover outcome"); Assert.Equal("ApprovalRequired", host.Reviews.Last().LastObservation!.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.True((await f.Permissions.DecideAsync(host.Reviews.Last().RequestID, HomeApprovalChoice.Accept)).Succeeded);
            f.Held.LoseWriteReturnOnce = loseReturn; await Click(host, "Apply approved request or recover outcome");
            Assert.True(host.Reviews.Last().LastObservation!.Committed); Assert.True(host.Reviews.Last().LastObservation!.AuditRecorded);
            await Click(host, "Reload library");
            var reopened = await new ShelfLibraryService(new VersionedAtomicSettingsStore(f.Paths)).ReadAsync();
            var collection = Assert.Single(reopened.Library.Collections);
            Assert.Equal("Original smart collection", collection.Name); Assert.Equal(ShelfCollectionKind.Smart, collection.Kind);
            Assert.Equal(new[] { "work" }, collection.Criteria!.RequiredTags);
            Assert.Equal(new[] { ShelfTargetKind.WebAddress }, collection.Criteria.TargetKinds);
            Assert.True(collection.Criteria.FavouritesOnly); Assert.True(collection.Criteria.OfflineCapableOnly);
            Assert.Equal(original.Id, Assert.Single(ShelfLibraryPolicy.ResolveCollection(reopened.Library, collection.Id)).Id);
            var filter = Assert.Single(host.GetVisualDescendants().OfType<ComboBox>(), x => x.Name == "shelf-smart-collection");
            var committed = await File.ReadAllBytesAsync(f.SettingsFile); var home = await File.ReadAllBytesAsync(f.HomeFile);
            filter.SelectedIndex = 1;
            var text = Assert.Single(host.GetVisualDescendants().OfType<TextBlock>(), x => x.Name == "shelf-library");
            Assert.Equal($"{original.Name} ({original.Id:D})", text.Text);
            filter.SelectedIndex = 0; Assert.Contains("Other site", text.Text);
            Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile)); Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
            await Click(host, "Finish audit"); Assert.True(host.Reviews.Last().LastObservation!.Committed);
            Assert.Equal(writes + 1, f.Held.GuardedWriteCalls); Assert.Equal(committed, await File.ReadAllBytesAsync(f.SettingsFile));
        }
        catch { f.Held.Release.TrySetResult(); if (reviewing is not null) { try { await reviewing; } catch { } } throw; }
        finally { f.Held.Release.TrySetResult(); window.Close(); }
    }
    private static async Task Fill(ShelfNativeWorkspaceHost host)
    {
        var inputs = host.GetVisualDescendants().OfType<TextBox>()
            .Where(input => input.Name is "shelf-search" or "shelf-name" or "shelf-address" or "shelf-request" or "shelf-collection-name" or "shelf-smart-tags" or "shelf-edit-name" or "shelf-edit-tags" or "shelf-edit-order").ToArray(); Assert.Equal(9, inputs.Length);
        Assert.Single(inputs, x => x.Name == "shelf-name").Text = "Reference site";
        Assert.Single(inputs, x => x.Name == "shelf-address").Text = "https://example.test/reference";
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
    }
    private static async Task Click(ShelfNativeWorkspaceHost host, string content)
    {
        var button = Assert.Single(host.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, content));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await host.WhenActionsIdleAsync();
    }
    [AvaloniaFact]
    public async Task Real_native_search_filters_genuine_approved_targets_without_Home_or_settings_write_and_retires_after_close()
    {
        using var f = new Fixture(); await f.InitializeAsync();
        var items = new[] {
            new ShelfLaunchItem(Guid.NewGuid(), "Alpha Docs", new(ShelfTargetKind.WebAddress, "https://example.test/alpha", Uri: "https://example.test/alpha"), Tags: ["refuel"], Order: 2),
            new ShelfLaunchItem(Guid.NewGuid(), "Beta Game", new(ShelfTargetKind.WebAddress, "https://example.test/beta", Uri: "https://example.test/beta"), Tags: ["play"], Order: 1),
            new ShelfLaunchItem(Guid.NewGuid(), "Gamma Docs", new(ShelfTargetKind.WebAddress, "https://example.test/gamma", Uri: "https://example.test/gamma"), Tags: ["refuel"], Order: 3) };
        foreach (var item in items)
        {
            var original = await f.Owner.LoadForDisplayAsync(f.Actor);
            using (original.Selection)
            {
                var review = await f.Owner.ReviewAsync(original.Selection, item);
                Assert.True((await f.Permissions.DecideAsync(review.RequestID, HomeApprovalChoice.Accept)).Succeeded);
                Assert.True((await f.Owner.CommitAsync(review)).Committed);
            }
        }
        using var host = await ShelfNativeWorkspaceHost.OpenAsync(f.Owner, f.Actor, _ => Task.CompletedTask,
            action => { action(); return Task.CompletedTask; });
        var window = new Window { Content = host }; window.Show();
        try
        {
            var search = Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), control => control.Name == "shelf-search");
            var library = Assert.Single(host.GetVisualDescendants().OfType<TextBlock>(), control => control.Name == "shelf-library");
            var summary = Assert.Single(host.GetVisualDescendants().OfType<TextBlock>(), control => control.Name == "shelf-search-summary");
            var settingsBefore = await File.ReadAllBytesAsync(f.SettingsFile); var homeBefore = await File.ReadAllBytesAsync(f.HomeFile);
            var writes = f.Held.GuardedWriteCalls;
            foreach (var (query, expected) in new[] {
                ("REFUEL docs", new[] { items[0], items[2] }),
                ("example.test/beta", new[] { items[1] }),
                ("not-present", Array.Empty<ShelfLaunchItem>()),
                ("", new[] { items[1], items[0], items[2] }) })
            {
                search.Text = query;
                // Observe maintained native binding delivery; no fabricated query service/results.
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.Equal(string.Join(Environment.NewLine, expected.Select(item => $"{item.Name} ({item.Id:D})")), library.Text);
                Assert.Equal($"{expected.Length} matching Shelf items", summary.Text);
                Assert.Empty(host.Reviews); Assert.Equal(writes, f.Held.GuardedWriteCalls);
                Assert.Equal(settingsBefore, await File.ReadAllBytesAsync(f.SettingsFile));
                Assert.Equal(homeBefore, await File.ReadAllBytesAsync(f.HomeFile));
            }
            var retiredText = library.Text;
            host.Dispose(); search.Text = "Beta";
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            Assert.Equal(retiredText, library.Text); Assert.Equal(writes, f.Held.GuardedWriteCalls);
            Assert.Equal(settingsBefore, await File.ReadAllBytesAsync(f.SettingsFile));
            Assert.Equal(homeBefore, await File.ReadAllBytesAsync(f.HomeFile));
            var reopened = await f.Library.ReadAsync(); Assert.Equal(3, reopened.Library.Items.Count);
            Assert.All(items, item => Assert.Contains(reopened.Library.Items, current => current.Id == item.Id && current.Target == item.Target));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Actual_owned_page_refuses_overlong_or_retired_query_without_losing_original_valid_search_or_writing()
    {
        using var f = new Fixture(); await f.InitializeAsync();
        using var workspace = await ShelfLibraryWorkspace.OpenAsync(f.Owner, f.Actor);
        using var page = new ShelfNativeWorkspacePage(workspace, _ => Task.CompletedTask,
            action => { action(); return Task.CompletedTask; });
        var settingsBefore = await File.ReadAllBytesAsync(f.SettingsFile); var homeBefore = await File.ReadAllBytesAsync(f.HomeFile);
        Assert.True(page.TrySetValue("Search", "original valid query"));
        Assert.True(page.TryGetValue("Library", out var original));
        Assert.False(page.TrySetValue("Search", new string('x', 4097)));
        Assert.True(page.TryGetValue("Search", out var query)); Assert.Equal("original valid query", query);
        Assert.True(page.TryGetValue("Library", out var after)); Assert.Equal(original, after);
        Assert.True(page.TryGetValue("SearchSummary", out var diagnostic)); Assert.Contains("Shorten", Assert.IsType<string>(diagnostic));
        Assert.True(page.TrySetValue("Search", ""));
        Assert.True(page.TryGetValue("SearchSummary", out var cleared)); Assert.Equal("0 matching Shelf items", cleared);
        page.Dispose(); Assert.False(page.TrySetValue("Search", "retired"));
        Assert.Empty(page.Reviews); Assert.Equal(0, f.Held.GuardedWriteCalls);
        Assert.Equal(settingsBefore, await File.ReadAllBytesAsync(f.SettingsFile));
        Assert.Equal(homeBefore, await File.ReadAllBytesAsync(f.HomeFile));
    }

    private sealed class Fixture : IDisposable
    {
        public Paths Paths { get; } = new();
        public string SettingsFile => Path.Combine(Paths.DataDirectory, "settings.json");
        public string HomeFile => Path.Combine(Paths.DataDirectory, "home.json");
        public HomePermissionTrustService Permissions { get; private set; } = null!;
        public HomeShelfLibraryOwner Owner { get; private set; } = null!;
        public AuthenticatedResourceActor Actor { get; private set; } = null!;
        public ShelfLibraryService Library { get; }
        private readonly VersionedAtomicSettingsStore _settings;
        public HeldSettings Held { get; }
        public HomeLocalStoreOwnership Ownership { get; private set; } = null!;
        private readonly FileHomeCoreStateStore _home;
        private readonly HomeLocalProfileIdentity _profiles;
        public HomeLocalProfileIdentity Profiles => _profiles;
        public Fixture()
        {
            _settings = new(Paths); Held = new(_settings); Library = new(Held);
            _home = new(Path.Combine(Paths.DataDirectory, "home.json"));
            _profiles = new(_home, new OperatingSystemPrincipalSource());

        }
        public async Task InitializeAsync()
        {
            Actor = await _profiles.GetCurrentAsync(default) ?? throw new InvalidOperationException("Actual local Home actor required.");
            // Actual production policy owner supplies the maintained individual store-import policy.
            // The same authentic OS-profile caller/Home store and owning Shelf policy are composed;
            // no graph or invocation operation is dispatched by this native ownership fixture.
            var graphDatabase = new SqliteDatabase(Paths);
            var policyOwner = new HomeAppAiServices(new ModelProviderRegistry([]), _home,
                new HomePermissionCallerIdentity(Actor.ActorId, "9to1 native Home host", "os-bound-local-profile", Actor.AuthenticationRevision, true),
                new ExecutionEventRepository(graphDatabase),
                new HomeInvocationCatalogue(new ModeRegistry(graphDatabase), new ExtensionRepository(graphDatabase)),
                [new ShelfOwnedLibraryActionPolicies()]);
            Permissions = policyOwner.Permissions;
            var identity = await _settings.GetStoreIdentityAsync(default);
            var ownership = new HomeLocalStoreOwnership(_home, _profiles,
                new HomeLocalStoreEvidenceRegistry([new ShelfOwnedLibraryEvidence(Held)]), Permissions);
            Ownership = ownership;
            var authority = new HomeResourceStoreOwnershipAuthority(ownership, _profiles);
            var resolver = new ShelfOwnedLibraryAccessResolver(Library, _profiles, authority);
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(_profiles, [resolver]), Permissions);
            Owner = new(Library, _profiles, authority, broker, Permissions,
                new HomeOwnedLibraryCommitFenceSource(_home, _profiles, authority, broker));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Owner.LoadForDisplayAsync(Actor));
            await ownership.BindNewEmptyAsync("shelf", identity.StoreId.ToString("D"));
        }
        public void Dispose() { try { Directory.Delete(Paths.RootDirectory, true); } catch (IOException) { } }
    }
    private sealed class HeldSettings(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore,
        IResourceStoreIdentitySource, IVersionedSettingsGuardedCompareExchange
    {
        public bool LoseWriteReturnOnce { get; set; }
        public bool HideAfterLostReturn { get; set; }
        public bool HideLibraryReads { get; set; }
        public int GuardedWriteCalls { get; private set; }
        public bool HoldIdentity { get; set; }
        public bool HoldPublication { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token)
        {
            var identity = await actual.GetStoreIdentityAsync(token);
            if (HoldIdentity) { HoldIdentity = false; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return identity;
        }
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class =>
            HideLibraryReads && (key == "shelf.library.v1" || key == "maps.journeys.v1")
                ? Task.FromException<T?>(new IOException("Actual library observation temporarily unavailable after lost return."))
                : actual.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => actual.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => actual.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => actual.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => actual.ImportAsync(value, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken token)
            => actual.CompareExchangeAsync(key, expected, replacement, token);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, CancellationToken token)
            => actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
        public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
        {
            GuardedWriteCalls++;
            var result = await actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, new HeldAdmission(this, admission), token);
            if (result.Exchanged && LoseWriteReturnOnce)
            {
                LoseWriteReturnOnce = false; HideLibraryReads = HideAfterLostReturn;
                throw new IOException("Decorator loses return AFTER actual inner atomic physical publication.");
            }
            return result;
        }
        private sealed class HeldAdmission(HeldSettings owner, ISettingsCommitAdmission actual) : ISettingsCommitAdmission
        {
            public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
            {
                if (owner.HoldPublication && context.Phase == SettingsCommitPhase.Publication)
                { owner.HoldPublication = false; owner.Entered.TrySetResult(); await owner.Release.Task.WaitAsync(token); }
                return await actual.CheckAsync(context, token);
            }
        }
    }
    private sealed class Paths : IAppPaths
    {
        public string RootDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-shelf-workspace-" + Guid.NewGuid().ToString("N"));
        public string DataDirectory => RootDirectory;
        public string DatabasePath => Path.Combine(RootDirectory, "actual.sqlite");
        public string BrowserProfileDirectory => Path.Combine(RootDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(RootDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(RootDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(RootDirectory, "logs");
        public string SettingsPath => Path.Combine(RootDirectory, "settings.json");
        public void EnsureCreated() => Directory.CreateDirectory(RootDirectory);
    }
}
