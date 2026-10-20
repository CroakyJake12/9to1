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

        public bool OpenModelPicker() { ModelOpens++; return true; }
        public bool OpenNotifications() { NotificationOpens++; return true; }
        public bool OpenProfile() { ProfileOpens++; return true; }
    }

    private sealed class SharedControlsRefusingGateway : IHavenSharedControlGateway
    {
        public int Calls;
        public bool OpenModelPicker() { Calls++; return false; }
        public bool OpenNotifications() { Calls++; return false; }
        public bool OpenProfile() { Calls++; return false; }
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
}
