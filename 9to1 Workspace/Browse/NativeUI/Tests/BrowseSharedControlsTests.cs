using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Haven.SharedHeader;
using HavenOS.Home;
using HavenOS.Home.Core;
using Haven.Application.Haven;
using Haven.Browser;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    // Presentation/command-boundary regression over the SAME shipped shell and model-picker
    // documents. Fixture route data is explicit; this is not native provider/auth/host acceptance.
    [AvaloniaTheory]
    [InlineData(360, 520)]
    [InlineData(720, 520)]
    [InlineData(1440, 920)]
    public async Task Shared_model_panel_bounds_scroll_and_keeps_its_close_action_reachable_without_mutating_the_route(int width, int height)
    {
        var surface = HomeModelPickerCuiSurface.LoadDefault();
        var route = new HomeModelRouteContract("fixture.route.with.a.long.stable.identifier.for.narrow.viewport.regression", 1,
            "global", "chat", null, null,
            Enumerable.Range(0, 6).Select(index => new HomeModelRouteCandidate("fixture-provider", "fixture-model-" + index,
                "fixture-revision", true, index)).ToArray(), JsonSerializer.SerializeToElement(new { }));
        surface.ApplyState(new(false, "Fixture", "Explicit model-picker layout fixture", new(1, "global", "chat", [route]),
            route.RouteId, route, true, null));
        surface.ApplyCatalogue(new HomeModelCataloguePage([new HomeModelPickerCatalogueEntry(
            "fixture-provider", "fixture-model-0", "fixture-revision", "Explicit catalogue layout fixture",
            "Fixture provider", true, new HashSet<string>(["text"]), 1024, "fixture", "local", "fixture")],
            0, 50, false, 1), "Explicit catalogue fixture, not provider readiness");
        surface.ApplyCompactModel(HavenModelProjectionSnapshot.CreateValidated(null, null, "global", null,
            route.RouteId, route.Version, 1, "fixture-provider", "fixture-model-0", true, true, ["quick", "balanced", "deep"], "deep"));
        var bindings = new SharedPanelDocumentFixture { ModelPicker = surface };
        using var shellLoader = new CuiControlLoader();
        shellLoader.SetBindingContext(bindings); shellLoader.SetActionDispatcher(bindings);
        var shell = shellLoader.TryLoad(HavenSharedHeaderDocuments.LoadShell());
        Assert.NotNull(shell.Root);
        Assert.DoesNotContain(shell.Diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
        shellLoader.WireBindings(shell.Root!);
        var originalState = surface.State;
        using var pickerLoader = new CuiControlLoader();
        pickerLoader.SetBindingContext(surface); pickerLoader.SetActionDispatcher(bindings);
        var picker = pickerLoader.TryLoad(surface.Document);
        Assert.NotNull(picker.Root);
        Assert.DoesNotContain(picker.Diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
        pickerLoader.WireBindings(picker.Root!);
        var body = NamedContainer(shell.Root!, "shared-panel-host");
        body.AddChild(picker.Root!); // The same authored mount contract used by the production host.
        var window = new Window { Content = shell.Root, Width = width, Height = height };
        try
        {
            window.Show(); Pump();
            var frame = NamedContainer(shell.Root!, "shared-panel-frame");
            var close = shell.Root!.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "shared-panel-close");
            var backdrop = NamedContainer(shell.Root!, "shared-shell-backdrop");
            var scroll = Assert.Single(body.GetVisualDescendants().OfType<ScrollViewer>());
            var viewport = Assert.Single(scroll.GetVisualDescendants().OfType<ScrollContentPresenter>());
            AssertInside(frame, window); AssertInside(close, window);
            Assert.InRange(frame.Bounds.Width, 240, 360);
            Assert.Equal(HorizontalAlignment.Right, frame.HorizontalAlignment);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(backdrop.Background).Color.A);
            var modelName = picker.Root!.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.Name == "compact-model-picker-selection");
            // The fixture state declares HasUnsavedChanges: true; since f07c06ca6 the compact
            // projection truthfully prefixes the pending draft marker before the model name.
            Assert.Equal("Pending: Explicit catalogue layout fixture", modelName.Text);
            Assert.True(modelName.IsEffectivelyVisible);
            var more = NamedButton("compact-model-picker-edit");
            Assert.DoesNotContain(picker.Root!.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "save-model-route" && button.IsEffectivelyVisible);
            // The compact panel is a bounded scroll body (420 max): since f07c06ca6 the pending
            // draft rows can place More settings below the fold, so its contract is scroll-to-
            // reachable inside the same bounded viewport — never clipped outside it.
            more.BringIntoView(); Pump();
            AssertInside(more, viewport);
            Capture("compact-model-picker");
            await Click(more);
            Assert.True(surface.IsSettingsOpen);
            Assert.Equal(HorizontalAlignment.Stretch, frame.HorizontalAlignment);
            Assert.Equal(0x66, Assert.IsAssignableFrom<ISolidColorBrush>(backdrop.Background).Color.A);
            var back = NamedButton("model-settings-back");
            AssertInside(back, viewport);
            Capture("expanded-model-settings");
            Assert.Same(originalState, surface.State);
            await Click(back);
            Assert.False(surface.IsSettingsOpen);
            Assert.Same(originalState, surface.State);
            await Click(more);
            Assert.True(close.IsCancel);
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            foreach (var name in new[] { "model-settings-back", "model-picker-refresh", "model-catalogue-refresh",
                "category-video", "save-model-route", "discard-model-route", "preview-video-route" })
            {
                var button = NamedButton(name);
                button.BringIntoView(); Pump();
                AssertInside(button, viewport);
                AssertInside(close, window); // The dismiss action does not scroll away with content.
            }
            var point = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;
            var hit = window.InputHitTest(point) as Visual;
            Assert.Contains(close, hit!.GetSelfAndVisualAncestors());
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            await shellLoader.WhenActionsIdleAsync(); Pump();
            Assert.False(bindings.HasOpenPanel); Assert.Equal(1, bindings.CloseCommands);
            Assert.Same(originalState, surface.State);
            bindings.HasOpenPanel = true; Pump();
            var save = NamedButton("save-model-route");
            save.BringIntoView(); Pump(); save.Focus();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            await shellLoader.WhenActionsIdleAsync(); Pump();
            Assert.False(bindings.HasOpenPanel); Assert.Equal(2, bindings.CloseCommands);
            Assert.Same(originalState, surface.State);
            Assert.True(surface.State.HasUnsavedChanges);

            // Root-popover dismissal through native pointer input using the SAME shared
            // backdrop behaviour the production host wires: with the compact view open, a blank
            // press outside the panel retires it through the ClosePanel owner; a press inside
            // the panel never dismisses; the full decision view is not a root popover, so a
            // blank press cannot retire it.
            HavenSharedPanelBackdropDismissal.Wire(backdrop,
                () => bindings.HasOpenPanel && bindings.IsRootPopoverOpen, () => frame,
                () => { if (bindings.HasOpenPanel) bindings.CloseSharedPanelViaBackdrop(); });
            await bindings.DispatchAsync("ShowCompactPicker", null);
            bindings.HasOpenPanel = true; Pump();
            var blank = new Point(12, window.Bounds.Height / 2);
            var closesBefore = bindings.CloseCommands;
            window.MouseDown(blank, MouseButton.Left); window.MouseUp(blank, MouseButton.Left);
            await shellLoader.WhenActionsIdleAsync(); Pump();
            var blankChain = string.Join(" > ", (window.InputHitTest(blank) as Visual)?
                .GetSelfAndVisualAncestors().Select(v => v.GetType().Name +
                    (string.IsNullOrEmpty(v.Name) ? "" : ":" + v.Name)) ?? new[] { "null" });
            Assert.True(!bindings.HasOpenPanel,
                $"outside press did not dismiss; chain={blankChain}; backdropVisible={backdrop.IsEffectivelyVisible}; backdropBounds={backdrop.Bounds}; frameBounds={frame.Bounds}");
            Assert.Equal(closesBefore + 1, bindings.CloseCommands);
            Assert.Same(originalState, surface.State);

            bindings.HasOpenPanel = true; Pump();
            var insideFrame = frame.TranslatePoint(new Point(frame.Bounds.Width / 2, 4), window)!.Value;
            window.MouseDown(insideFrame, MouseButton.Left); window.MouseUp(insideFrame, MouseButton.Left);
            await shellLoader.WhenActionsIdleAsync(); Pump();
            Assert.True(bindings.HasOpenPanel);
            Assert.Equal(closesBefore + 1, bindings.CloseCommands);

            await bindings.DispatchAsync("ShowModelSettings", null); Pump();
            window.MouseDown(blank, MouseButton.Left); window.MouseUp(blank, MouseButton.Left);
            await shellLoader.WhenActionsIdleAsync(); Pump();
            Assert.True(bindings.HasOpenPanel);
            Assert.Equal(closesBefore + 1, bindings.CloseCommands);
        }
        finally { window.Close(); }

        void Pump() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        Button NamedButton(string name) => picker.Root!.GetVisualDescendants().OfType<Button>().Single(control => control.Name == name);
        void Capture(string view)
        {
            using var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            var evidence = Environment.GetEnvironmentVariable("BROWSE_EVIDENCE_DIRECTORY");
            if (string.IsNullOrWhiteSpace(evidence)) return;
            Directory.CreateDirectory(evidence);
            rendered.Save(Path.Combine(evidence, $"{view}-{width}x{height}.png"));
        }
        async Task Click(Button button)
        {
            button.BringIntoView(); Pump();
            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left); window.MouseUp(center, MouseButton.Left);
            await pickerLoader.WhenActionsIdleAsync(); Pump();
        }
        static CuiContainerControl NamedContainer(Control root, string name) => root.GetVisualDescendants().OfType<CuiContainerControl>()
            .Single(control => control.Name == name);
        static void AssertInside(Control child, Control parent)
        {
            var origin = child.TranslatePoint(default, parent)!.Value;
            Assert.True(origin.X >= -.5 && origin.X + child.Bounds.Width <= parent.Bounds.Width + .5,
                $"{child.Name} spans {origin.X}..{origin.X + child.Bounds.Width} outside width {parent.Bounds.Width}.");
            Assert.True(origin.Y >= -.5 && origin.Y + child.Bounds.Height <= parent.Bounds.Height + .5,
                $"{child.Name} spans {origin.Y}..{origin.Y + child.Bounds.Height} outside height {parent.Bounds.Height}.");
        }
    }

    private sealed class SharedPanelDocumentFixture : ICuiBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
    {
        private bool _hasOpenPanel = true;
        public HomeModelPickerCuiSurface? ModelPicker { get; init; }
        private bool IsCompactModelPicker => ModelPicker is { IsSettingsOpen: false };
        public bool HasOpenPanel
        {
            get => _hasOpenPanel;
            set { _hasOpenPanel = value; PropertyChanged?.Invoke(this, new(nameof(HasOpenPanel))); }
        }
        public bool IsRootPopoverOpen => IsCompactModelPicker;
        public void CloseSharedPanelViaBackdrop() =>
            DispatchAsync("9to1.Haven.ClosePanel", null).AsTask().GetAwaiter().GetResult();
        public int CloseCommands { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                nameof(HasOpenPanel) => HasOpenPanel,
                "PanelBackdrop" => IsCompactModelPicker ? "#00000000" : "#667F7F7F",
                "PanelHorizontalAlignment" => IsCompactModelPicker ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                "PanelVerticalAlignment" => IsCompactModelPicker ? VerticalAlignment.Top : VerticalAlignment.Stretch,
                "PanelMargin" => IsCompactModelPicker ? new Thickness(16, 84, 16, 16) : new Thickness(16),
                "PanelMinWidth" => IsCompactModelPicker ? 240d : 0d,
                "PanelMaxWidth" => IsCompactModelPicker ? 360d : 880d,
                "PanelMaxHeight" => IsCompactModelPicker ? 420d : 720d,
                _ => null
            };
            return value is not null;
        }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (command is "ShowModelSettings" or "ShowCompactPicker")
            {
                Assert.NotNull(ModelPicker);
                ModelPicker.SetSettingsOpen(command == "ShowModelSettings");
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return ValueTask.CompletedTask;
            }
            Assert.Equal("9to1.Haven.ClosePanel", command);
            CloseCommands++; HasOpenPanel = false;
            return ValueTask.CompletedTask;
        }
    }

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
        public HavenTopBarModelInfo? ModelInfo { get; set; }
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
        RegisterProductionImageResolver(window);
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
            await ClickSharedOpen(window, workspace, model);
            Assert.Equal(1, gateway.ModelOpens);
            await ClickSharedOpen(window, workspace, notifications);
            Assert.Equal(1, gateway.NotificationOpens);
            // The real HavenProfileButton opens the profile menu; the mounted BrowseProfileAccountButton
            // routes the genuine profile open through the typed gateway once the menu is open.
            await Click(window, workspace, profile);
            Assert.True(IsTrue(workspace, "ProfileMenuOpen"));
            var profileAccount = FindMounted(window, "BrowseProfileAccountButton");
            await ClickSharedOpen(window, workspace, profileAccount);
            Assert.Equal(1, gateway.ProfileOpens);
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Embedded_model_pill_displays_the_live_name_and_retains_one_keyboard_target_at_narrow_width()
    {
        using var trace = new FixtureTraceScope(nameof(Embedded_model_pill_displays_the_live_name_and_retains_one_keyboard_target_at_narrow_width));
        var chrome = await BrowseChrome.CreateAsync(new Paths(), internalContentFactory: new TabSurfaceInternalFixtureFactory(),
            homeProfileId: "model-pill-fixture");
        var gateway = new SharedControlsFakeGateway();
        var context = new SharedControlsFakeContext(gateway);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: context);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Settle(window);
            var model = FindMounted(window, "HavenModelButton");
            var label = FindVisual<TextBlock>("HavenModelLabel");
            var disclosure = FindVisual<Image>("HavenModelDisclosure");
            var mark = FindVisual<Image>("HavenHostMark");
            Assert.True(label.IsEffectivelyVisible);
            Assert.Equal("Choose model", label.Text);
            Assert.Equal("Choose AI model", AutomationProperties.GetName(model));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), control =>
                control.Name == "HavenModelEffort" && control.IsEffectivelyVisible);
            Assert.True(disclosure.IsEffectivelyVisible);
            Assert.NotNull(disclosure.Source);
            Assert.True(mark.IsEffectivelyVisible);
            Assert.IsType<Bitmap>(mark.Source);
            var firstTab = TabButton(window, workspace, chrome.State.SelectedTabId.ToString("D"));
            Assert.True(mark.TranslatePoint(new Point(mark.Bounds.Width, 0), window)!.Value.X <=
                firstTab.TranslatePoint(default, window)!.Value.X + .5);

            const string modelName = "An unusually long installed model display name that reaches the compact toolbar limit";
            context.ModelInfo = new(modelName, true, 3, "70%", true, false, null, null, false, null, EffortLevelOrdinal: 1);
            workspace.PublishModelProjectionChanged(); Settle(window);
            Assert.Equal(modelName, label.Text);
            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            Assert.InRange(label.Bounds.Width, 1, 144.5);
            var effort = FindVisual<TextBlock>("HavenModelEffort");
            Assert.True(effort.IsEffectivelyVisible);
            Assert.Equal("70%", effort.Text);
            Assert.Equal(11d, effort.FontSize);
            Assert.Equal((FontWeight)600, effort.FontWeight);
            var fullName = $"Choose {modelName}, reasoning effort 70%";
            Assert.Equal(fullName, AutomationProperties.GetName(model));
            Trace("wide:focus");
            Assert.True(model.Focus(NavigationMethod.Tab));

            Trace("narrow:resize");
            window.Width = 360; Settle(window);
            Assert.Same(model, FindMounted(window, "HavenModelButton"));
            Assert.True(model.IsFocused);
            Assert.InRange(model.Bounds.Width, 32, 34);
            Assert.Equal(fullName, AutomationProperties.GetName(model));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), control => control.IsEffectivelyVisible &&
                control.Name is "HavenModelLabel" or "HavenModelEffort" or "HavenModelDisclosure" or "HavenHostMark");
            Assert.True(FindVisual<TextBox>("BrowseAddressBox").Bounds.Width >= 63.5);
            Trace("narrow:activate");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            await Observe(Loader(window).WhenActionsIdleAsync(), window);
            Assert.Equal(1, gateway.ModelOpens);

            Trace("wide:restore");
            window.Width = 1180; Settle(window);
            Assert.Same(model, FindMounted(window, "HavenModelButton"));
            Assert.True(FindVisual<TextBlock>("HavenModelLabel").IsEffectivelyVisible);
            Assert.True(FindVisual<Image>("HavenModelDisclosure").IsEffectivelyVisible);
            Assert.Equal(fullName, AutomationProperties.GetName(model));
            Assert.True(FindVisual<Image>("HavenHostMark").IsEffectivelyVisible);
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }

        T FindVisual<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    }

    /// <summary>Observe the actual authored shared-open pipeline and the SAME original operation
    /// retained by Browse. Shared surfaces intentionally use the operation ledger, while ordinary
    /// browser commands use OriginalCommand; requiring the latter would reject a genuine open.</summary>
    private static async Task ClickSharedOpen(BrowseNativeWindow window, BrowseNativeWorkspace workspace, Button button)
    {
        window.UpdateLayout();
        button.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(window, TopLevel.GetTopLevel(button));
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(button.IsEnabled);
        var loader = Loader(window);
        var diagnostics = Assert.IsType<CuiControlDiagnostics>(loader.Inspect(button));
        Assert.True(diagnostics.DispatcherConnected);
        Assert.True(diagnostics.ActionsWired);
        var ordinaryBefore = workspace.OriginalCommand;
        var operationsBefore = workspace.OriginalOperations.ToArray();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var pipeline = loader.WhenActionsIdleAsync();
        await Observe(pipeline, window);
        var admitted = workspace.OriginalOperations
            .Where(actual => !operationsBefore.Any(previous => ReferenceEquals(previous, actual))).ToArray();
        var original = Assert.Single(admitted);
        await Observe(original, window);
        Assert.Contains(original, workspace.OriginalOperations);
        Assert.Same(ordinaryBefore, workspace.OriginalCommand);
        await Task.WhenAll(workspace.OriginalNotifications.ToArray());
    }

    /// <summary>The profile menu keeps exactly ONE actionable Switch &amp; Manage Users row (no
    /// duplicate heading) and the real popup opens/closes with neutral truthful current-profile
    /// copy — never the false unconditional 'Signed-in principal for this session.' claim.</summary>
    [AvaloniaFact]
    public async Task Profile_menu_has_one_mounted_switch_manage_users_row_and_popup_open_close_with_neutral_copy()
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var gateway = new SharedControlsFakeGateway();
        var context = new SharedControlsFakeContext(gateway);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: context);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            await Click(window, workspace, Find(window, "HavenProfileButton"));
            Assert.True(IsTrue(workspace, "ProfileMenuOpen"));
            var menu = Authored(window, "BrowseProfileMenu");
            Assert.Single(menu.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Switch & Manage Users");
            var switchRows = menu.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "BrowseSwitchUsersButton").ToArray();
            Assert.Single(switchRows);
            Assert.True(switchRows[0].IsEffectivelyVisible);
            await Click(window, workspace, switchRows[0]);
            Assert.True(IsTrue(workspace, "ManageUsersOpen"));
            var overlay = Authored(window, "BrowseManageUsersOverlay");
            Assert.Contains(overlay.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Switch & Manage Users");
            Assert.Contains(overlay.GetVisualDescendants().OfType<Button>(), b => b.Name == "BrowseManageUsersAccountButton");
            Assert.Contains(overlay.GetVisualDescendants().OfType<Button>(), b => b.Name == "BrowseCloseManageUsersButton");
            var hints = overlay.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Text == "Click to open account and user settings." || t.Text == "The native account owner is unavailable in this session.").ToArray();
            Assert.NotEmpty(hints);
            Assert.DoesNotContain(overlay.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Signed-in principal for this session.");
            var account = Assert.Single(overlay.GetVisualDescendants().OfType<Button>(), b => b.Name == "BrowseManageUsersAccountButton");
            await ClickSharedOpen(window, workspace, account);
            Assert.Equal(1, gateway.ProfileOpens);
            Assert.False(IsTrue(workspace, "ManageUsersOpen"));
            Assert.False(IsTrue(workspace, "ProfileMenuOpen"));
            await Click(window, workspace, Find(window, "HavenProfileButton"));
            await Click(window, workspace, Find(window, "BrowseSwitchUsersButton"));
            var closeUsers = Assert.Single(overlay.GetVisualDescendants().OfType<Button>(), b => b.Name == "BrowseCloseManageUsersButton");
            await ClickCore(window, workspace, closeUsers);
            Assert.False(IsTrue(workspace, "ManageUsersOpen"));
            var closeMenu = FindMounted(window, "BrowseCloseProfileMenuButton");
            await ClickCore(window, workspace, closeMenu);
            Assert.False(IsTrue(workspace, "ProfileMenuOpen"));
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    /// <summary>The canonical site-popups switch derives its visible state from the real typed
    /// WindowManagement permission (never an optimistic bool). Both directions route through the
    /// mounted ToggleSwitch pointer→TwoWay→Allow/Deny path, and a native page refuses it.</summary>
    [AvaloniaFact]
    public async Task BrowseAllowPopupsSwitch_toggles_both_directions_through_typed_permission_state_including_disabled_path()
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            Assert.True(chrome.State.SelectedTab.Address.IsWeb);
            await Click(window, workspace, Find(window, "HavenProfileButton"));
            await Click(window, workspace, Find(window, "BrowseWebToolsHeader"));
            var toggle = Assert.IsType<ToggleSwitch>(Authored(window, "BrowseAllowPopupsSwitch"));
            Assert.Same(window, TopLevel.GetTopLevel(toggle));
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.True(toggle.IsEnabled);
            Assert.Equal(false, toggle.IsChecked);
            Assert.True(workspace.IsActionAvailable("9to1.Browse.AllowPopups") == true);
            await ClickPopupsSwitch(window, workspace, toggle);
            Assert.Equal(true, toggle.IsChecked);
            Assert.True(IsTrue(workspace, "AllowPopups"));
            var origin = BrowserSitePermissionStore.CanonicalOrigin(chrome.State.SelectedTab.Address.WebUri!);
            var allowed = Assert.Single(chrome.State.Permissions, permission =>
                permission.Kind == BrowserSitePermissionKind.WindowManagement && permission.Origin == origin);
            Assert.Equal(BrowserSitePermissionDecision.Allow, allowed.Decision);
            Assert.True(workspace.IsActionAvailable("9to1.Browse.DenyPopups") == true);
            await ClickPopupsSwitch(window, workspace, toggle);
            Assert.Equal(false, toggle.IsChecked);
            Assert.False(IsTrue(workspace, "AllowPopups"));
            var denied = Assert.Single(chrome.State.Permissions, permission =>
                permission.Kind == BrowserSitePermissionKind.WindowManagement && permission.Origin == origin);
            Assert.Equal(BrowserSitePermissionDecision.Deny, denied.Decision);
            await Click(window, workspace, Find(window, "BrowseOpenBookmarksButton"));
            Assert.Equal(BrowserNativePage.Bookmarks, chrome.State.SelectedTab.NativePage);
            Assert.False(workspace.IsActionAvailable("9to1.Browse.AllowPopups") == true);
            if (!IsTrue(workspace, "ProfileMenuOpen"))
                await Click(window, workspace, Find(window, "HavenProfileButton"));
            if (!IsTrue(workspace, "ToolsOpen"))
                await Click(window, workspace, Find(window, "BrowseWebToolsHeader"));
            Settle(window);
            Assert.False(toggle.IsEnabled);
            Assert.Equal(false, toggle.IsChecked);
            var before = workspace.OriginalCommand;
            var permissions = chrome.State.Permissions.ToArray();
            PointerClickPopupsSwitch(window, toggle);
            await Observe(Loader(window).WhenActionsIdleAsync(), window);
            Settle(window);
            Assert.Same(before, workspace.OriginalCommand);
            Assert.Equal(permissions, chrome.State.Permissions);
            Assert.Equal(false, toggle.IsChecked);
            Assert.False(workspace.TrySetValue("AllowPopups", true));
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    /// <summary>Owner-requested boolean menu control uses the SAME real tab-placement
    /// operation and persistence. Native pointer input must not apply a second toggle when
    /// the bound value is already current; retry/unsaved state remains owned by Browse.</summary>
    [AvaloniaFact]
    public async Task Profile_pin_switch_updates_real_tab_state_in_both_directions_and_restores_the_saved_pin()
    {
        var paths = new Paths(); var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            Assert.False(chrome.State.SelectedTab.IsHome);
            await Observe(workspace.DispatchAsync("9to1.Browse.EnableTabRestore", null).AsTask(), window);
            await Click(window, workspace, Find(window, "HavenProfileButton"));
            await Click(window, workspace, Find(window, "BrowseWebToolsHeader"));
            var toggle = Assert.IsType<ToggleSwitch>(Authored(window, "BrowsePinTabSwitch"));
            Assert.True(toggle.IsEnabled); Assert.Equal(false, toggle.IsChecked);
            var selected = chrome.State.SelectedTab.Id;
            await ClickPopupsSwitch(window, workspace, toggle); // Same actual pointer/TwoWay/retained-command helper.
            Assert.True(chrome.State.SelectedTab.IsPinned); Assert.Equal(true, toggle.IsChecked);
            var original = workspace.OriginalCommand;
            Assert.True(workspace.TrySetValue("TabPinned", true)); // Same desired state is idempotent.
            Assert.Same(original, workspace.OriginalCommand);
            await ClickPopupsSwitch(window, workspace, toggle);
            Assert.False(chrome.State.SelectedTab.IsPinned); Assert.Equal(false, toggle.IsChecked);
            await ClickPopupsSwitch(window, workspace, toggle);
            Assert.True(chrome.State.SelectedTab.IsPinned);
            Assert.False(chrome.HasUnsavedTabPlacement);
            await Retire(window);
            var restored = await BrowseChrome.CreateAsync(paths, new FixtureFactory());
            try { Assert.Contains(restored.State.Tabs, tab => tab.Id == selected && tab.IsPinned); }
            finally { await restored.DisposeAsync(); }
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Profile_pin_switch_refuses_the_actual_host_policy_without_mutating_tab_or_creating_a_command()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths(), new FixtureFactory());
        var workspace = new BrowseNativeWorkspace(chrome, command => command != "9to1.Browse.ToggleTabPinned");
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            await Click(window, workspace, Find(window, "HavenProfileButton"));
            await Click(window, workspace, Find(window, "BrowseWebToolsHeader"));
            var toggle = Assert.IsType<ToggleSwitch>(Authored(window, "BrowsePinTabSwitch"));
            Assert.False(toggle.IsEnabled);
            var before = workspace.OriginalCommand; var pinned = chrome.State.SelectedTab.IsPinned;
            PointerClickPopupsSwitch(window, toggle);
            await Observe(Loader(window).WhenActionsIdleAsync(), window); Settle(window);
            Assert.False(workspace.TrySetValue("TabPinned", !pinned));
            Assert.Same(before, workspace.OriginalCommand);
            Assert.Equal(pinned, chrome.State.SelectedTab.IsPinned);
            Assert.Equal(pinned, toggle.IsChecked);
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    private static void PointerClickPopupsSwitch(BrowseNativeWindow window, ToggleSwitch toggle)
    {
        Settle(window); toggle.BringIntoView(); Settle(window);
        Assert.Same(window, TopLevel.GetTopLevel(toggle));
        Assert.True(toggle.IsEffectivelyVisible);
        Assert.True(toggle.Bounds.Width > 0 && toggle.Bounds.Height > 0);
        var point = toggle.TranslatePoint(new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window);
        Assert.NotNull(point);
        window.MouseMove(point.Value);
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
    }

    private static async Task ClickPopupsSwitch(BrowseNativeWindow window, BrowseNativeWorkspace workspace, ToggleSwitch toggle)
    {
        Assert.True(toggle.IsEnabled);
        var before = workspace.OriginalCommand;
        PointerClickPopupsSwitch(window, toggle); // Native OnClick toggles Checked before the TwoWay setter dispatches.
        await Observe(Loader(window).WhenActionsIdleAsync(), window);
        Assert.NotNull(workspace.OriginalCommand);
        Assert.NotSame(before, workspace.OriginalCommand);
        var original = workspace.OriginalCommand!;
        await Observe(original, window);
        Assert.Same(original, workspace.OriginalCommand);
        await Observe(Task.WhenAll(workspace.OriginalNotifications.ToArray()), window);
        Settle(window);
    }
}
