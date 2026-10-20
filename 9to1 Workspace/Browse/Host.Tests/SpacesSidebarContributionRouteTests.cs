/*
 * FILE DOCUMENTATION
 * Where: Browse/Host.Tests/SpacesSidebarContributionRouteTests.cs, the real Haven host route test lane.
 * What: Proves an extension-contributed Space sidebar element resolves from the Space's actually
 *       attached extensions and opens its target through the host's existing typed navigation contract.
 * Why: Specification 4763-4866 requires the main Space sidebar to be customisable through stable
 *       section/item identities and for an element to open its declared target. The projection and the
 *       activation must be real, not a placeholder row.
 * Maintenance: Uses the real SpaceRegistry, a real ExtensionManager over a controlled extension
 *       repository, and the real Spaces session. No product surface is stubbed to obtain a pass.
 */

using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Home;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

public sealed class SpacesSidebarContributionRouteTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

    private static InstalledExtensionPackage DeclaredPackage(params ExtensionSidebarContribution[] contributions)
    {
        var manifest = new ExtensionPackageManifest("sidebar.demo", "/extensions/sidebar.demo", "Sidebar Demo",
            ExtensionPackageType.Plugin, "1.0.0", ">=1.0.0", "Declares Space sidebar elements", "9to1", "9to1",
            null, null, ExtensionPermission.None, [], [], [], null)
        { SidebarContributions = contributions };
        return new InstalledExtensionPackage(Guid.NewGuid(), Guid.NewGuid(), manifest, "/extensions/sidebar.demo",
            ExtensionPermission.None, ExtensionInstallState.Installed, true, false, "sha256:demo",
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    }

    /// <summary>An element declared by an extension attached to the selected Space opens its target
    /// through the host's existing typed navigation intent, on the same tab.</summary>
    [AvaloniaFact]
    public async Task An_attached_extensions_sidebar_element_opens_through_the_host_navigation_contract()
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.Page, "https://example.com/tool"));
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            extensionPackages: [package]);
        var space = await rig.Spaces.CreateAsync("Extension Space");
        await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);

        var tabId = Guid.NewGuid();
        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session("9to1://spaces", tabId: tabId, profileId: actor.ProfileId,
            queueNavigation: (intent, _) => { intents.Add(intent); return true; });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            await spaces.Controller.SelectSpaceAsync(space.Id);
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 1);

            await spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask().WaitAsync(Bound);

            var intent = Assert.Single(intents);
            Assert.Equal(tabId, intent.SourceTabId);
            Assert.Equal("https://example.com/tool", intent.Address.Value);
            Assert.False(intent.OpenInNewTab);
        }
        finally { try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { } }
    }

    /// <summary>Per-Space visibility is the attachment itself: a Space that has not attached the
    /// extension shows nothing even though the package is installed, and disabling the binding hides the
    /// element without dropping the attachment.</summary>
    [AvaloniaFact]
    public async Task Sidebar_elements_follow_the_selected_Space_attachment_and_override()
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.Page, "https://example.com/tool"));
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, extensionPackages: [package]);        var attached = await rig.Spaces.CreateAsync("Attached Space");
        var unattached = await rig.Spaces.CreateAsync("Unattached Space");
        await rig.Spaces.UpdateAsync(attached with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Disabled)]
        }, CancellationToken.None);

        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session("9to1://spaces", profileId: actor.ProfileId,
            queueNavigation: (intent, _) => { intents.Add(intent); return true; });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);

            await spaces.Controller.SelectSpaceAsync(unattached.Id);
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 0);

            await spaces.Controller.SelectSpaceAsync(attached.Id);
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 0);

            // The disabled binding must refuse rather than open, and must not reach the host at all.
            await spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask().WaitAsync(Bound);
            Assert.Empty(intents);
            Assert.Contains("no longer attached", spaces.Controller.State.StatusMessage, StringComparison.Ordinal);
        }
        finally { try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { } }
    }

    /// <summary>Unsupported target kinds are not offered as actionable and never reach the host
    /// navigation contract, even when their target string is a valid HTTPS address.</summary>
    [AvaloniaTheory]
    [InlineData(ExtensionSidebarTargetKind.CuiSurface)]
    [InlineData(ExtensionSidebarTargetKind.Automation)]
    [InlineData(ExtensionSidebarTargetKind.GenerativePage)]
    [InlineData((ExtensionSidebarTargetKind)999)]
    public async Task Unsupported_sidebar_target_kinds_are_not_actionable_and_do_not_navigate(ExtensionSidebarTargetKind kind)
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            kind, "https://example.com/tool"));
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            extensionPackages: [package]);
        var space = await rig.Spaces.CreateAsync("Extension Space");
        await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);

        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session("9to1://spaces", profileId: actor.ProfileId,
            queueNavigation: (intent, _) => { intents.Add(intent); return true; });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            await spaces.Controller.SelectSpaceAsync(space.Id);
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 1);

            Assert.True(spaces.TryGetValue("spaces.sidebarItems", out var value));
            var row = Assert.Single((IEnumerable<SpacesHomeSidebarRow>)value!);
            Assert.False(row.IsActionable);
            Assert.False(string.IsNullOrWhiteSpace(row.UnavailableReason));

            await spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask().WaitAsync(Bound);
            Assert.Empty(intents);
        }
        finally { try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { } }
    }

    /// <summary>A genuinely asynchronous extension read must not cause the host queue callback to be
    /// invoked off the UI thread. The read is not masked by Task.FromResult.</summary>
    [AvaloniaFact]
    public async Task Sidebar_open_with_genuinely_async_extension_read_calls_host_queue_on_ui_thread()
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.Page, "https://example.com/tool"));
        var packages = new[] { package };

        TaskCompletionSource? heldEntered = null;
        TaskCompletionSource? heldRelease = null;
        var heldCount = 0;
        var armHeld = false;
        Func<CancellationToken, Task<IReadOnlyList<InstalledExtensionPackage>>> readInstalled = ct =>
        {
            if (armHeld && Interlocked.Increment(ref heldCount) == 1)
            {
                heldEntered!.TrySetResult();
                return heldRelease!.Task.ContinueWith(_ => (IReadOnlyList<InstalledExtensionPackage>)packages, TaskScheduler.Default);
            }
            return Task.FromResult<IReadOnlyList<InstalledExtensionPackage>>(packages);
        };

        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            getInstalledAsync: readInstalled);
        var space = await rig.Spaces.CreateAsync("Extension Space");
        await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);

        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session($"9to1://spaces/selected/{space.Id:D}", profileId: actor.ProfileId,
            queueNavigation: (intent, _) =>
            {
                Dispatcher.UIThread.VerifyAccess();
                intents.Add(intent);
                return true;
            });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            armHeld = false;
            await spaces.RefreshSidebarItemsAsync();
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 1);

            heldEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            heldRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            armHeld = true;
            var openTask = spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask();
            await heldEntered.Task.WaitAsync(Bound);
            await Task.Run(() => heldRelease.SetResult());
            await openTask.WaitAsync(Bound);

            Assert.Single(intents);
            Assert.Equal("https://example.com/tool", intents[0].Address.Value);
        }
        finally
        {
            heldRelease?.TrySetResult();
            try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
        }
    }

    /// <summary>The host queue callback must receive the genuine incomplete originating settlement,
    /// not Task.CompletedTask, and that settlement must only complete after the originating action.</summary>
    [AvaloniaFact]
    public async Task Sidebar_open_passes_incomplete_genuine_originating_settlement_to_host_queue()
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.Page, "https://example.com/tool"));
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            extensionPackages: [package]);
        var space = await rig.Spaces.CreateAsync("Extension Space");
        await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);

        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        Task? capturedSettlement = null;
        var incompleteAtAdmission = false;
        var session = rig.Session("9to1://spaces", profileId: actor.ProfileId,
            queueNavigation: (intent, settlement) =>
            {
                intents.Add(intent);
                capturedSettlement = settlement;
                incompleteAtAdmission = !settlement.IsCompleted;
                return true;
            });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            await spaces.Controller.SelectSpaceAsync(space.Id);
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 1);

            await spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask().WaitAsync(Bound);
            Assert.Single(intents);
            Assert.NotNull(capturedSettlement);
            Assert.True(incompleteAtAdmission, "The host queue must be admitted before the originating settlement completes.");
            await capturedSettlement!.WaitAsync(Bound);
            Assert.True(capturedSettlement.IsCompletedSuccessfully);
        }
        finally { try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { } }
    }

    /// <summary>Rendered sidebar button activation also carries the genuine incomplete originating
    /// settlement to the host queue, not a manufactured completed task.</summary>
    [AvaloniaFact]
    public async Task Rendered_sidebar_button_activation_passes_incomplete_settlement_to_host_queue()
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.Page, "https://example.com/tool"));
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            extensionPackages: [package]);
        var space = await rig.Spaces.CreateAsync("Extension Space");
        await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);

        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        Task? capturedSettlement = null;
        var incompleteAtAdmission = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = rig.Session($"9to1://spaces/selected/{space.Id:D}", profileId: actor.ProfileId,
            queueNavigation: (intent, settlement) =>
            {
                intents.Add(intent);
                capturedSettlement = settlement;
                incompleteAtAdmission = !settlement.IsCompleted;
                entered.TrySetResult();
                return true;
            });
        Window? window = null;
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            await spaces.RefreshSidebarItemsAsync();
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 1);

            window = new Window { Width = 1000, Height = 700, Content = root };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var button = root.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(item => item.Name == "open-space-sidebar-item");
            Assert.NotNull(button);
            var point = button!.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
            Assert.NotNull(point);
            window.MouseMove(point.Value);
            window.MouseDown(point.Value, MouseButton.Left);
            window.MouseUp(point.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            await entered.Task.WaitAsync(Bound);
            Assert.Single(intents);
            Assert.True(incompleteAtAdmission);
            Assert.NotNull(capturedSettlement);
            await capturedSettlement!.WaitAsync(Bound);
            Assert.True(capturedSettlement.IsCompletedSuccessfully);
        }
        finally
        {
            if (window is not null) window.Close();
            try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
        }
    }

    /// <summary>A delayed extension read must not let a stale selection navigate after the
    /// Space changed; the refusal must not poison close.</summary>
    [AvaloniaFact]
    public async Task Delayed_extension_read_does_not_navigate_after_space_switch()
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.Page, "https://example.com/tool"));
        var packages = new[] { package };

        TaskCompletionSource? heldEntered = null;
        TaskCompletionSource? heldRelease = null;
        var heldCount = 0;
        var armHeld = false;
        Func<CancellationToken, Task<IReadOnlyList<InstalledExtensionPackage>>> readInstalled = ct =>
        {
            if (armHeld && Interlocked.Increment(ref heldCount) == 1)
            {
                heldEntered!.TrySetResult();
                return heldRelease!.Task.ContinueWith(_ => (IReadOnlyList<InstalledExtensionPackage>)packages, TaskScheduler.Default);
            }
            return Task.FromResult<IReadOnlyList<InstalledExtensionPackage>>(packages);
        };

        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            getInstalledAsync: readInstalled);
        var space = await rig.Spaces.CreateAsync("Extension Space");
        var other = await rig.Spaces.CreateAsync("Other Space");
        await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);

        rig.Row = rig.Row! with { SpaceId = space.Id };
        var saved = rig.Row!;

        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session($"9to1://spaces/selected/{space.Id:D}/chat/{saved.Id:D}", profileId: actor.ProfileId,
            queueNavigation: (intent, _) => { intents.Add(intent); return true; });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            var chat = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.Equal(saved.Id, chat.ConversationId);
            Assert.Equal(space.Id, chat.CurrentConversation.SpaceId);
            chat.SetDraft("retained draft");
            Assert.Equal("retained draft", ReadDraft(chat));
            var before = spaces.SpaceWorkspaceContent;

            armHeld = false;
            await spaces.RefreshSidebarItemsAsync();
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 1);

            heldEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            heldRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            armHeld = true;
            var openTask = spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask();
            await heldEntered.Task.WaitAsync(Bound);
            await spaces.Controller.SelectSpaceAsync(other.Id);
            heldRelease.SetResult();
            await openTask.WaitAsync(Bound);

            Assert.Empty(intents);
            Assert.Same(before, spaces.SpaceWorkspaceContent);
            Assert.Equal("retained draft", ReadDraft(chat));

            await session.CloseAndDrainAsync().WaitAsync(Bound);
            Assert.True(session.CanRetireOriginalView);
        }
        finally
        {
            heldRelease?.TrySetResult();
            try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
        }
    }

    /// <summary>A delayed extension read must not let a stale attachment navigate after the
    /// contribution is disabled in the same Space; the refusal must not poison close.</summary>
    [AvaloniaFact]
    public async Task Delayed_extension_read_does_not_navigate_after_binding_disable()
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.Page, "https://example.com/tool"));
        var packages = new[] { package };

        TaskCompletionSource? heldEntered = null;
        TaskCompletionSource? heldRelease = null;
        var heldCount = 0;
        var armHeld = false;
        Func<CancellationToken, Task<IReadOnlyList<InstalledExtensionPackage>>> readInstalled = ct =>
        {
            if (armHeld && Interlocked.Increment(ref heldCount) == 1)
            {
                heldEntered!.TrySetResult();
                return heldRelease!.Task.ContinueWith(_ => (IReadOnlyList<InstalledExtensionPackage>)packages, TaskScheduler.Default);
            }
            return Task.FromResult<IReadOnlyList<InstalledExtensionPackage>>(packages);
        };

        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            getInstalledAsync: readInstalled);
        var space = await rig.Spaces.CreateAsync("Extension Space");
        await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);

        rig.Row = rig.Row! with { SpaceId = space.Id };
        var saved = rig.Row!;

        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session($"9to1://spaces/selected/{space.Id:D}/chat/{saved.Id:D}", profileId: actor.ProfileId,
            queueNavigation: (intent, _) => { intents.Add(intent); return true; });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            var chat = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.Equal(saved.Id, chat.ConversationId);
            Assert.Equal(space.Id, chat.CurrentConversation.SpaceId);
            chat.SetDraft("retained draft");
            Assert.Equal("retained draft", ReadDraft(chat));
            var before = spaces.SpaceWorkspaceContent;

            armHeld = false;
            await spaces.RefreshSidebarItemsAsync();
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 1);

            heldEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            heldRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            armHeld = true;
            var openTask = spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask();
            await heldEntered.Task.WaitAsync(Bound);
            await rig.Spaces.UpdateAsync(space with
            {
                CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                    SpaceCapabilityOverride.Disabled)]
            }, CancellationToken.None);
            heldRelease.SetResult();
            await openTask.WaitAsync(Bound);

            Assert.Empty(intents);
            Assert.Same(before, spaces.SpaceWorkspaceContent);
            Assert.Equal("retained draft", ReadDraft(chat));

            await session.CloseAndDrainAsync().WaitAsync(Bound);
            Assert.True(session.CanRetireOriginalView);
        }
        finally
        {
            heldRelease?.TrySetResult();
            try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
        }
    }

    /// <summary>The second package lookup is a separate asynchronous admission boundary. Changing
    /// selection or a canonical binding only after that lookup starts must still refuse activation.
    /// Initial restoration avoids the unjoined refresh caused by a post-publication Open status.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Second_activation_package_read_rejects_selection_or_binding_change(bool disableBinding)
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.Page, "https://example.com/tool"));
        IReadOnlyList<InstalledExtensionPackage> packages = new[] { package };
        var secondReadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReadResult = new TaskCompletionSource<IReadOnlyList<InstalledExtensionPackage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var armed = 0;
        var activationReads = 0;
        Task<IReadOnlyList<InstalledExtensionPackage>> ReadInstalled(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref armed) == 0) return Task.FromResult(packages);
            var ordinal = Interlocked.Increment(ref activationReads);
            if (ordinal != 2) return Task.FromResult(packages);
            secondReadEntered.TrySetResult();
            return secondReadResult.Task;
        }

        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            getInstalledAsync: ReadInstalled);
        var space = await rig.Spaces.CreateAsync("Second-read source Space");
        var other = await rig.Spaces.CreateAsync("Second-read destination Space");
        space = await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);
        rig.Row = rig.Row! with { SpaceId = space.Id };
        var saved = rig.Row!;
        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session($"9to1://spaces/selected/{space.Id:D}/chat/{saved.Id:D}", profileId: actor.ProfileId,
            queueNavigation: (intent, _) => { intents.Add(intent); return true; });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var spaces = Assert.IsType<SpacesHomePage>(Assert.IsType<ContentControl>(session.OriginalNativeView).Content);
            var chat = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.Equal(space.Id, spaces.Controller.State.Selection!.Id);
            Assert.Equal(saved.Id, chat.ConversationId);
            Assert.Equal(space.Id, chat.CurrentConversation.SpaceId);
            chat.SetDraft("draft retained across second package read");
            await spaces.RefreshSidebarItemsAsync().WaitAsync(Bound);
            Assert.True(spaces.TryGetValue("spaces.sidebarItems", out var projection));
            Assert.True(Assert.Single((IReadOnlyList<SpacesHomeSidebarRow>)projection!).IsActionable);
            Assert.Equal(0, Volatile.Read(ref activationReads));

            // No controller command/status is issued between arming and dispatch. Both package
            // reads therefore belong to this activation; the second one remains incomplete.
            Volatile.Write(ref armed, 1);
            var opening = spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask();
            await secondReadEntered.Task.WaitAsync(Bound);
            Assert.Equal(2, Volatile.Read(ref activationReads));
            Assert.False(opening.IsCompleted);
            Assert.Empty(intents);

            if (disableBinding)
            {
                var changed = await rig.Spaces.UpdateAsync(space with
                {
                    CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                        SpaceCapabilityOverride.Disabled)]
                }, expectedRevision: space.Revision, cancellationToken: CancellationToken.None);
                Assert.True(changed.Revision > space.Revision);
                Assert.Equal(space.Id, spaces.Controller.State.Selection!.Id);
            }
            else
            {
                await spaces.Controller.SelectSpaceAsync(other.Id).WaitAsync(Bound);
                Assert.Equal(other.Id, spaces.Controller.State.Selection!.Id);
            }

            Volatile.Write(ref armed, 0);
            secondReadResult.SetResult(packages);
            await opening.WaitAsync(Bound);
            Assert.Empty(intents);
            Assert.Same(chat, spaces.SpaceWorkspaceContent);
            Assert.Equal("draft retained across second package read", ReadDraft(chat));
            await session.CloseAndDrainAsync().WaitAsync(Bound);
            Assert.True(session.CanRetireOriginalView);
        }
        finally
        {
            Volatile.Write(ref armed, 0);
            secondReadResult.TrySetResult(packages);
            try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
        }
    }

    /// <summary>A stale sidebar-item refresh must not overwrite a newer selection's rows.</summary>
    [AvaloniaFact]
    public async Task Stale_refresh_sidebar_items_does_not_overwrite_newer_selection_rows()
    {
        var registry = new SpaceRegistry(new Settings());
        var spaceA = await registry.CreateAsync("A");
        var spaceB = await registry.CreateAsync("B");
        var gateA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<SpacesHomeSelection, CancellationToken, Task<IReadOnlyList<SpacesHomeSidebarRow>>> provider = async (selection, ct) =>
        {
            if (selection.Id == spaceA.Id) await gateA.Task;
            return new[] { new SpacesHomeSidebarRow("item-" + selection.Id, "Label", "icon", "Page",
                "https://example.com/" + selection.Id, true) };
        };
        var page = new SpacesHomePage(registry, new NoOpActionHost(), SpacesHomePage.ImmediateReadinessInstance,
            sidebarItemsProvider: provider);
        var window = new Window { Width = 800, Height = 600, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);
            await page.Controller.SelectSpaceAsync(spaceA.Id);
            Dispatcher.UIThread.RunJobs();

            var refreshA = page.RefreshSidebarItemsAsync();
            await page.Controller.SelectSpaceAsync(spaceB.Id);
            var refreshB = page.RefreshSidebarItemsAsync();
            await refreshB.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(page.TryGetValue("spaces.sidebarItems", out var afterB));
            var bRows = (IReadOnlyList<SpacesHomeSidebarRow>)afterB!;
            Assert.Single(bRows);
            Assert.Equal("item-" + spaceB.Id, bRows[0].SidebarItemId);

            gateA.SetResult();
            await refreshA.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(page.TryGetValue("spaces.sidebarItems", out var afterA));
            var finalRows = (IReadOnlyList<SpacesHomeSidebarRow>)afterA!;
            Assert.Single(finalRows);
            Assert.Equal("item-" + spaceB.Id, finalRows[0].SidebarItemId);
        }
        finally
        {
            gateA.TrySetResult();
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
            window.Close();
        }
    }

    private static string ReadDraft(NewChatPage page)
    {
        var scene = typeof(NewChatPage).GetField("_scene", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
        // The scene's live canonical input is the single authority for the composer draft; the
        // retired `Instruction` reflection member no longer exists on ChatHavenScene.
        return ((ChatHavenScene)scene).Input.Text ?? string.Empty;
    }

    /// <summary>An AppSurface target declared with an HTTPS address is a kind/namespace mismatch:
    /// it is not actionable and never reaches host navigation.</summary>
    [AvaloniaFact]
    public async Task AppSurface_sidebar_target_with_https_address_is_unavailable_and_does_not_navigate()
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.AppSurface, "https://example.com/tool"));
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            extensionPackages: [package]);
        var space = await rig.Spaces.CreateAsync("Extension Space");
        await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);

        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session("9to1://spaces", profileId: actor.ProfileId,
            queueNavigation: (intent, _) => { intents.Add(intent); return true; });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            await spaces.Controller.SelectSpaceAsync(space.Id);
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 1);

            Assert.True(spaces.TryGetValue("spaces.sidebarItems", out var value));
            var row = Assert.Single((IEnumerable<SpacesHomeSidebarRow>)value!);
            Assert.False(row.IsActionable);
            Assert.Contains("app address", row.UnavailableReason, StringComparison.OrdinalIgnoreCase);

            await spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask().WaitAsync(Bound);
            Assert.Empty(intents);
        }
        finally { try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { } }
    }

    /// <summary>A Page target declared with an app namespace address is a kind/namespace mismatch:
    /// it is not actionable and never reaches host navigation.</summary>
    [AvaloniaFact]
    public async Task Page_sidebar_target_with_app_address_is_unavailable_and_does_not_navigate()
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.Page, "9to1://planner"));
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            extensionPackages: [package]);
        var space = await rig.Spaces.CreateAsync("Extension Space");
        await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);

        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session("9to1://spaces", profileId: actor.ProfileId,
            queueNavigation: (intent, _) => { intents.Add(intent); return true; });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            await spaces.Controller.SelectSpaceAsync(space.Id);
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 1);

            Assert.True(spaces.TryGetValue("spaces.sidebarItems", out var value));
            var row = Assert.Single((IEnumerable<SpacesHomeSidebarRow>)value!);
            Assert.False(row.IsActionable);

            await spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask().WaitAsync(Bound);
            Assert.Empty(intents);
        }
        finally { try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { } }
    }

    /// <summary>Two refreshes for the same Space that complete out of order must keep the newer read.</summary>
    [AvaloniaFact]
    public async Task Same_space_out_of_order_refresh_keeps_newer_read()
    {
        var registry = new SpaceRegistry(new Settings());
        var space = await registry.CreateAsync("Same");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = 0;
        Func<SpacesHomeSelection, CancellationToken, Task<IReadOnlyList<SpacesHomeSidebarRow>>> provider = async (selection, ct) =>
        {
            var sequence = Interlocked.Increment(ref seen);
            if (sequence == 1) await gate.Task;
            var tag = sequence == 1 ? "stale" : "fresh";
            return new[] { new SpacesHomeSidebarRow(tag, "Label", "icon", "Page", "https://example.com/" + tag, true) };
        };
        var page = new SpacesHomePage(registry, new NoOpActionHost(), SpacesHomePage.ImmediateReadinessInstance,
            sidebarItemsProvider: provider);
        var window = new Window { Width = 800, Height = 600, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(CancellationToken.None);
            await page.Controller.SelectSpaceAsync(space.Id);
            Dispatcher.UIThread.RunJobs();

            var refreshOld = page.RefreshSidebarItemsAsync();
            var refreshNew = page.RefreshSidebarItemsAsync();
            await refreshNew.WaitAsync(TimeSpan.FromSeconds(5));
            gate.SetResult();
            await refreshOld.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(page.TryGetValue("spaces.sidebarItems", out var value));
            var rows = (IReadOnlyList<SpacesHomeSidebarRow>)value!;
            Assert.Single(rows);
            Assert.Equal("fresh", rows[0].SidebarItemId);
        }
        finally
        {
            gate.TrySetResult();
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
            window.Close();
        }
    }

    /// <summary>AppSurface targets using non-NativeApp namespaces or malformed addresses are unavailable.</summary>
    [AvaloniaTheory]
    [InlineData("cui://write")]
    [InlineData("browse://home")]
    [InlineData("not-an-address")]
    public async Task AppSurface_sidebar_target_with_non_native_namespace_or_malformed_address_is_unavailable(string targetId)
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.AppSurface, targetId));
        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            extensionPackages: [package]);
        var space = await rig.Spaces.CreateAsync("Extension Space");
        await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);

        await rig.Home!.StartOriginalAsync().WaitAsync(Bound);
        var actor = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session("9to1://spaces", profileId: actor.ProfileId,
            queueNavigation: (intent, _) => { intents.Add(intent); return true; });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var root = Assert.IsType<ContentControl>(session.OriginalNativeView);
            var spaces = Assert.IsType<SpacesHomePage>(root.Content);
            await spaces.Controller.SelectSpaceAsync(space.Id);
            await WaitForProjectionAsync(spaces, "spaces.sidebarItems", items => Count(items) == 1);

            Assert.True(spaces.TryGetValue("spaces.sidebarItems", out var value));
            var row = Assert.Single((IEnumerable<SpacesHomeSidebarRow>)value!);
            Assert.False(row.IsActionable);

            await spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask().WaitAsync(Bound);
            Assert.Empty(intents);
        }
        finally { try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { } }
    }

    /// <summary>Home service loss after the second package read starts is an expected pre-effect
    /// denial. It must leave a healthy chat and close owner, including after Home recovers.</summary>
    [AvaloniaFact]
    public async Task Home_permission_service_loss_during_second_package_read_refuses_without_poisoning_close()
    {
        var package = DeclaredPackage(new ExtensionSidebarContribution("tool-panel", "Tool panel", "tool",
            ExtensionSidebarTargetKind.Page, "https://example.com/tool"));
        IReadOnlyList<InstalledExtensionPackage> packages = new[] { package };
        var secondReadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReadResult = new TaskCompletionSource<IReadOnlyList<InstalledExtensionPackage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var armed = 0;
        var activationReads = 0;
        Task<IReadOnlyList<InstalledExtensionPackage>> ReadInstalled(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Volatile.Read(ref armed) == 0) return Task.FromResult(packages);
            if (Interlocked.Increment(ref activationReads) != 2) return Task.FromResult(packages);
            secondReadEntered.TrySetResult();
            return secondReadResult.Task;
        }

        using var rig = new SpacesConversationRouteTests.Rig(withHome: true, allowConversationWrites: true,
            getInstalledAsync: ReadInstalled);
        var space = await rig.Spaces.CreateAsync("Home service loss Space");
        space = await rig.Spaces.UpdateAsync(space with
        {
            CapabilityBindings = [new SpaceCapabilityBinding("sidebar.demo", SpaceCapabilityKind.Plugin,
                SpaceCapabilityOverride.Inherit)]
        }, CancellationToken.None);
        rig.Row = rig.Row! with { SpaceId = space.Id };
        var saved = rig.Row!;
        var home = rig.Home!;
        await home.StartOriginalAsync().WaitAsync(Bound);
        var actor = await home.Profiles.GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(actor);
        var intents = new List<BrowseNavigationIntent>();
        var session = rig.Session($"9to1://spaces/selected/{space.Id:D}/chat/{saved.Id:D}", profileId: actor.ProfileId,
            queueNavigation: (intent, _) => { intents.Add(intent); return true; });
        try
        {
            await session.InitializeAsync().WaitAsync(Bound);
            var spaces = Assert.IsType<SpacesHomePage>(Assert.IsType<ContentControl>(session.OriginalNativeView).Content);
            var chat = Assert.IsType<NewChatPage>(spaces.SpaceWorkspaceContent);
            Assert.Equal(saved.Id, chat.ConversationId);
            Assert.Equal(space.Id, chat.CurrentConversation.SpaceId);
            chat.SetDraft("draft retained while Home permission service recovers");
            await spaces.RefreshSidebarItemsAsync().WaitAsync(Bound);
            Assert.True(spaces.TryGetValue("spaces.sidebarItems", out var projection));
            Assert.True(Assert.Single((IReadOnlyList<SpacesHomeSidebarRow>)projection!).IsActionable);

            // Initialization restored the chat before publication. No controller status/command
            // can launch a competing refresh between arming and the sidebar dispatch.
            Volatile.Write(ref armed, 1);
            var opening = spaces.DispatchAsync("spaces.open-sidebar-item", "tool-panel").AsTask();
            await secondReadEntered.Task.WaitAsync(Bound);
            Assert.Equal(2, Volatile.Read(ref activationReads));
            Assert.False(opening.IsCompleted);
            Assert.Empty(intents);
            home.Runtime.ReportServiceState("permissions.trust",
                HavenOS.Home.Core.HomeServiceLifecycleState.Unavailable, false, "Independent sidebar denial probe");
            Assert.False(home.Runtime.Current.Services.Single(item => item.ServiceId == "permissions.trust").IsAvailable);

            Volatile.Write(ref armed, 0);
            secondReadResult.SetResult(packages);
            await opening.WaitAsync(Bound);
            Assert.Empty(intents);
            Assert.Same(chat, spaces.SpaceWorkspaceContent);
            Assert.Equal("draft retained while Home permission service recovers", ReadDraft(chat));
            Assert.False(string.IsNullOrWhiteSpace(spaces.Controller.State.StatusMessage));

            home.Runtime.ReportServiceState("permissions.trust",
                HavenOS.Home.Core.HomeServiceLifecycleState.Ready, true);
            var preparation = await session.PrepareCloseAsync().WaitAsync(Bound);
            Assert.NotNull(preparation);
            await preparation!.AbortAsync().WaitAsync(Bound);
            await session.CloseAndDrainAsync().WaitAsync(Bound);
            Assert.True(session.CanRetireOriginalView);
        }
        finally
        {
            Volatile.Write(ref armed, 0);
            secondReadResult.TrySetResult(packages);
            home.Runtime.ReportServiceState("permissions.trust",
                HavenOS.Home.Core.HomeServiceLifecycleState.Ready, true);
            try { await session.CloseAndDrainAsync().WaitAsync(Bound); } catch { }
        }
    }

    private static int Count(object? value) => value is System.Collections.IEnumerable items
        ? items.Cast<object>().Count() : 0;

    private static async Task WaitForProjectionAsync(SpacesHomePage page, string path, Predicate<object?> ready)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (page.TryGetValue(path, out var value) && ready(value)) return;
            await Task.Delay(50);
        }
        Assert.Fail($"The authored {path} projection never became ready.");
    }

    private sealed class NoOpActionHost : ISpacesActionHost
    {
        public bool SupportsAction(SpacesActionKind action) => true;
        public Task ExecuteAsync(SpacesAction action, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Settings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class => Task.FromResult(_values.GetValueOrDefault(key) as T);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken cancellationToken) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Export");
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw new InvalidOperationException("Import");
    }
}
