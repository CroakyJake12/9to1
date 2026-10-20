using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application.Haven;
using Haven.Browser;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    private sealed class SharedControlsFakeGateway : IHavenSharedControlGateway
    {
        public int ModelOpens;
        public int NotificationOpens;
        public int ProfileOpens;
        public Task<HavenSharedOpenResult> OpenModelPickerAsync(CancellationToken token = default) { ModelOpens++; return Task.FromResult(HavenSharedOpenResult.Succeeded()); }
        public Task<HavenSharedOpenResult> OpenNotificationsAsync(CancellationToken token = default) { NotificationOpens++; return Task.FromResult(HavenSharedOpenResult.Succeeded()); }
        public Task<HavenSharedOpenResult> OpenProfileAsync(CancellationToken token = default) { ProfileOpens++; return Task.FromResult(HavenSharedOpenResult.Succeeded()); }
    }

    private sealed class SharedControlsRefusingGateway : IHavenSharedControlGateway
    {
        public int Calls;
        public Task<HavenSharedOpenResult> OpenModelPickerAsync(CancellationToken token = default) { Calls++; return Task.FromResult(HavenSharedOpenResult.Refused("Refused", "refused")); }
        public Task<HavenSharedOpenResult> OpenNotificationsAsync(CancellationToken token = default) { Calls++; return Task.FromResult(HavenSharedOpenResult.Refused("Refused", "refused")); }
        public Task<HavenSharedOpenResult> OpenProfileAsync(CancellationToken token = default) { Calls++; return Task.FromResult(HavenSharedOpenResult.Refused("Refused", "refused")); }
    }

    private sealed class SharedControlsFakeContext(
        IHavenSharedControlGateway? gateway, bool embedded = true, bool model = true, bool notifications = true, bool profile = true)
        : IHavenHostContext
    {
        public bool IsEmbedded => embedded;
        public bool ModelSupported => model;
        public bool NotificationsSupported => notifications;
        public bool ProfileSupported => profile;
        public IHavenSharedControlGateway? SharedControls => gateway;
    }

    private sealed class UnsettledGateway(TaskCompletionSource<HavenSharedOpenResult> pending) : IHavenSharedControlGateway
    {
        public int Calls;
        public Task<HavenSharedOpenResult> OpenModelPickerAsync(CancellationToken token = default) { Calls++; return pending.Task; }
        public Task<HavenSharedOpenResult> OpenNotificationsAsync(CancellationToken token = default) { Calls++; return pending.Task; }
        public Task<HavenSharedOpenResult> OpenProfileAsync(CancellationToken token = default) { Calls++; return pending.Task; }
    }

    private sealed class FailingGateway : IHavenSharedControlGateway
    {
        public int Calls;
        public Task<HavenSharedOpenResult> OpenModelPickerAsync(CancellationToken token = default) { Calls++; return Task.FromException<HavenSharedOpenResult>(new InvalidOperationException("original open failed")); }
        public Task<HavenSharedOpenResult> OpenNotificationsAsync(CancellationToken token = default) { Calls++; return Task.FromException<HavenSharedOpenResult>(new InvalidOperationException("original open failed")); }
        public Task<HavenSharedOpenResult> OpenProfileAsync(CancellationToken token = default) { Calls++; return Task.FromException<HavenSharedOpenResult>(new InvalidOperationException("original open failed")); }
    }

    private sealed class CancellingGateway : IHavenSharedControlGateway
    {
        public int Calls;
        public Task<HavenSharedOpenResult> OpenModelPickerAsync(CancellationToken token = default) { Calls++; return Task.FromCanceled<HavenSharedOpenResult>(new CancellationToken(true)); }
        public Task<HavenSharedOpenResult> OpenNotificationsAsync(CancellationToken token = default) { Calls++; return Task.FromCanceled<HavenSharedOpenResult>(new CancellationToken(true)); }
        public Task<HavenSharedOpenResult> OpenProfileAsync(CancellationToken token = default) { Calls++; return Task.FromCanceled<HavenSharedOpenResult>(new CancellationToken(true)); }
    }

    /// <summary>The persistent Haven shared rail must degrade truthfully: with no host context
    /// the model/notifications/profile controls are unavailable and their actions are refused.</summary>
    [AvaloniaFact]
    public async Task No_host_context_disables_shared_rail_and_refuses_actions()
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        workspace.ObserveReadiness(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "shared-rail-fixture", "Ready"));
        try
        {
            Assert.False(workspace.TryGetValue("Haven.Embedded", out var embedded) && (bool)embedded!);
            Assert.False(workspace.TryGetValue("Haven.ModelAvailable", out var model) && (bool)model!);
            Assert.False(workspace.TryGetValue("Haven.NotificationsAvailable", out var notifications) && (bool)notifications!);
            Assert.False(workspace.TryGetValue("Haven.ProfileAvailable", out var profile) && (bool)profile!);
            Assert.False(workspace.IsActionAvailable("9to1.Haven.OpenModel") == true);
            Assert.False(workspace.IsActionAvailable("9to1.Haven.OpenNotifications") == true);
            Assert.False(workspace.IsActionAvailable("9to1.Haven.OpenProfile") == true);
            await workspace.DispatchAsync("9to1.Haven.OpenModel", null);
            await workspace.DispatchAsync("9to1.Haven.OpenNotifications", null);
            await workspace.DispatchAsync("9to1.Haven.OpenProfile", null);
        }
        finally { await workspace.DisposeAsync().AsTask(); }
    }

    /// <summary>A host that owns the shared cluster must route each rail action to the typed gateway.</summary>
    [AvaloniaFact]
    public async Task Shared_context_routes_each_rail_action_to_the_typed_gateway()
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var gateway = new SharedControlsFakeGateway();
        var context = new SharedControlsFakeContext(gateway);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: context);
        workspace.ObserveReadiness(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "shared-rail-fixture", "Ready"));
        try
        {
            Assert.True(workspace.TryGetValue("Haven.Embedded", out var embedded) && (bool)embedded!);
            Assert.True(workspace.TryGetValue("Haven.ModelAvailable", out var model) && (bool)model!);
            Assert.True(workspace.TryGetValue("Haven.NotificationsAvailable", out var notifications) && (bool)notifications!);
            Assert.True(workspace.TryGetValue("Haven.ProfileAvailable", out var profile) && (bool)profile!);
            Assert.True(workspace.IsActionAvailable("9to1.Haven.OpenModel") == true);
            Assert.True(workspace.IsActionAvailable("9to1.Haven.OpenNotifications") == true);
            Assert.True(workspace.IsActionAvailable("9to1.Haven.OpenProfile") == true);

            await workspace.DispatchAsync("9to1.Haven.OpenModel", null);
            await workspace.DispatchAsync("9to1.Haven.OpenNotifications", null);
            await workspace.DispatchAsync("9to1.Haven.OpenProfile", null);
            Assert.Equal(1, gateway.ModelOpens);
            Assert.Equal(1, gateway.NotificationOpens);
            Assert.Equal(1, gateway.ProfileOpens);
        }
        finally { await workspace.DisposeAsync().AsTask(); }
    }

    /// <summary>Partial host support must refuse only the unsupported shared controls.</summary>
    [AvaloniaFact]
    public async Task Partial_support_refuses_only_the_unsupported_rail_actions()
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var gateway = new SharedControlsFakeGateway();
        var context = new SharedControlsFakeContext(gateway, model: false, notifications: true, profile: false);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: context);
        workspace.ObserveReadiness(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "shared-rail-fixture", "Ready"));
        try
        {
            Assert.False(workspace.TryGetValue("Haven.ModelAvailable", out var model) && (bool)model!);
            Assert.True(workspace.TryGetValue("Haven.NotificationsAvailable", out var notifications) && (bool)notifications!);
            Assert.False(workspace.TryGetValue("Haven.ProfileAvailable", out var profile) && (bool)profile!);
            Assert.False(workspace.IsActionAvailable("9to1.Haven.OpenModel") == true);
            Assert.True(workspace.IsActionAvailable("9to1.Haven.OpenNotifications") == true);
            Assert.False(workspace.IsActionAvailable("9to1.Haven.OpenProfile") == true);

            await workspace.DispatchAsync("9to1.Haven.OpenNotifications", null);
            Assert.Equal(0, gateway.ModelOpens);
            Assert.Equal(1, gateway.NotificationOpens);
            Assert.Equal(0, gateway.ProfileOpens);
        }
        finally { await workspace.DisposeAsync().AsTask(); }
    }

    /// <summary>When the host's typed gateway declines to open a surface, the rail action must
    /// still complete without fabricating success and without routing to a different control.</summary>
    [AvaloniaFact]
    public async Task Refusing_gateway_never_fabricates_an_open()
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var gateway = new SharedControlsRefusingGateway();
        var context = new SharedControlsFakeContext(gateway);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: context);
        workspace.ObserveReadiness(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "shared-rail-fixture", "Ready"));
        try
        {
            Assert.True(workspace.IsActionAvailable("9to1.Haven.OpenNotifications") == true);
            await workspace.DispatchAsync("9to1.Haven.OpenNotifications", null);
            Assert.Equal(1, gateway.Calls);
        }
        finally { await workspace.DisposeAsync().AsTask(); }
    }

    /// <summary>After the workspace retires, shared-rail actions must refuse regardless of the
    /// underlying host context, so a closed workspace cannot keep driving the borrowed surfaces.</summary>
    [AvaloniaFact]
    public async Task Retired_workspace_refuses_shared_rail_actions()
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var gateway = new SharedControlsFakeGateway();
        var context = new SharedControlsFakeContext(gateway);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: context);
        workspace.ObserveReadiness(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "shared-rail-fixture", "Ready"));
        await workspace.DisposeAsync().AsTask();
        Assert.False(workspace.IsActionAvailable("9to1.Haven.OpenModel") == true);
        Assert.False(workspace.IsActionAvailable("9to1.Haven.OpenNotifications") == true);
        Assert.False(workspace.IsActionAvailable("9to1.Haven.OpenProfile") == true);
        Assert.Equal(0, gateway.ModelOpens);
        Assert.Equal(0, gateway.NotificationOpens);
        Assert.Equal(0, gateway.ProfileOpens);
    }

    /// <summary>The shared gateway must return the ACTUAL unsettled open until the held surface
    /// init settles, the the workspace's close must join that genuine settlement rather than
    /// treating the rail action as an instantaneous bool.</summary>
    [AvaloniaFact]
    public async Task Shared_gateway_returns_unsettled_original_until_init_settles_and_close_joins_it()
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var pending = new TaskCompletionSource<HavenSharedOpenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new UnsettledGateway(pending);
        var context = new SharedControlsFakeContext(gateway);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: context);
        workspace.ObserveReadiness(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "shared-rail-fixture", "Ready"));
        try
        {
            var dispatch = workspace.DispatchAsync("9to1.Haven.OpenNotifications", null).AsTask();
            await Task.Yield();
            Assert.Equal(1, gateway.Calls);
            Assert.False(dispatch.IsCompleted); // still unsettled; no fabricated success
            pending.SetResult(HavenSharedOpenResult.Succeeded());
            await dispatch;
            Assert.True(dispatch.IsCompletedSuccessfully);
        }
        finally { await workspace.DisposeAsync().AsTask(); }
    }

    /// <summary>A real gateway fault must be retained as a failed original open and never
    /// fabricated as success: the original action task faults (so close joins it), a
    /// structured status is published, and the next rail action still routes.</summary>
    [AvaloniaFact]
    public async Task Shared_gateway_fault_is_retained_and_never_fabricated_as_success()
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var gateway = new FailingGateway();
        var context = new SharedControlsFakeContext(gateway);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: context);
        workspace.ObserveReadiness(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "shared-rail-fixture", "Ready"));
        try
        {
            var dispatch = workspace.DispatchAsync("9to1.Haven.OpenNotifications", null).AsTask();
            var fault = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatch);
            Assert.Equal("original open failed", fault.Message);
            Assert.Equal(1, gateway.Calls);
            var second = workspace.DispatchAsync("9to1.Haven.OpenProfile", null).AsTask();
            await Assert.ThrowsAsync<InvalidOperationException>(() => second);
            Assert.Equal(2, gateway.Calls);
        }
        finally
        {
            // Close truthfully surfaces the retained original open faults (never hides them).
            try { await workspace.DisposeAsync().AsTask(); }
            catch (AggregateException closeFailures)
            {
                Assert.Contains(closeFailures.InnerExceptions, failure => failure.Message == "original open failed");
            }
        }
    }

    /// <summary>An ignored cancellation must stay safe: it is never reclassified as success
    /// nor as a fault, and the next rail action still routes.</summary>
    [AvaloniaFact]
    public async Task Shared_gateway_cancellation_is_safe_and_never_fabricates_success()
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var gateway = new CancellingGateway();
        var context = new SharedControlsFakeContext(gateway);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: context);
        workspace.ObserveReadiness(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "shared-rail-fixture", "Ready"));
        try
        {
            var dispatch = workspace.DispatchAsync("9to1.Haven.OpenNotifications", null).AsTask();
            await dispatch; // safe non-fault non-success settlement
            Assert.Equal(1, gateway.Calls);
            var second = workspace.DispatchAsync("9to1.Haven.OpenProfile", null).AsTask();
            await second;
            Assert.Equal(2, gateway.Calls);
        }
        finally { await workspace.DisposeAsync().AsTask(); }
    }

    /// <summary>The authored topbar controls must route each genuine open through the SAME typed
    /// gateway when mounted with an embedded host context. This proves the production layout (not
    /// the command dispatch surface alone) settles the actual open, and the mounted controls are
    /// enabled, visible and wired in the navigation bar.</summary>
    [AvaloniaFact]
    public async Task Authored_topbar_controls_route_each_open_to_the_typed_gateway()
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var gateway = new SharedControlsFakeGateway();
        var context = new SharedControlsFakeContext(gateway);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: context);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var model = Find(window, "HavenModelButton");
            var notifications = Find(window, "HavenNotificationsButton");
            var profile = Find(window, "HavenProfileButton");
            Assert.True(model.IsEffectivelyVisible); Assert.True(model.IsEnabled);
            Assert.True(notifications.IsEffectivelyVisible); Assert.True(notifications.IsEnabled);
            Assert.True(profile.IsEffectivelyVisible); Assert.True(profile.IsEnabled);
            await Click(window, workspace, model);
            Assert.Equal(1, gateway.ModelOpens);
            await Click(window, workspace, notifications);
            Assert.Equal(1, gateway.NotificationOpens);
            await Click(window, workspace, profile);
            Assert.Equal(1, gateway.ProfileOpens);
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }
}
