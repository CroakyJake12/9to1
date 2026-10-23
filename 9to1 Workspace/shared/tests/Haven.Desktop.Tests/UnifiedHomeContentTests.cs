using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;
using HavenOS.Home.Discover;
using HomeDiscoverPage = Haven.Desktop.Views.Pages.Home.HomeDiscoverPage;

namespace Haven.Desktop.Tests;

public sealed class UnifiedHomeContentTests
{
    private static BrowseAppContentRequest Request(string address = "browse://home") =>
        new(Guid.NewGuid(), AppContentAddress.Parse(address), "original-home-profile", BrowserTabPrivacy.Standard);

    private static UnifiedHomeContentFactory Factory(Registry source, CancellationToken lifetime,
        Func<BrowseNavigationIntent, Task, bool>? navigation = null,
        Func<BrowseAppContentRequest, CancellationToken, Task>? admission = null, bool aiAvailable = true,
        Func<NewDashboardPage>? dashboard = null, Func<HomeDiscoverPage>? discover = null) => new(source,
            dashboard ?? (() => throw new InvalidOperationException("Apps must not construct an unused dashboard.")),
            discover ?? (() => throw new InvalidOperationException("Apps must not construct an unused model catalogue.")),
            navigation ?? ((_, _) => true),
            (_, _) => throw new InvalidOperationException("Search must not implicitly invoke AI."),
            admission ?? ((_, _) => Task.CompletedTask), () => true, lifetime, aiAvailable);

    [AvaloniaFact]
    public async Task Unavailable_AI_retains_input_does_not_invoke_draft_and_keeps_Search_and_close_usable()
    {
        using var lifetime = new CancellationTokenSource();
        using var bus = new HavenEventBus();
        BrowseNavigationIntent? navigation = null;
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token,
            (intent, _) => { navigation = intent; return true; }, aiAvailable: false,
            dashboard: () => new NewDashboardPage(bus, null!, null!, null!, null!, new EmptySettings(),
                new EmptyDashboard(), new EmptyDashboardLayout(), [])).CreateSession(Request("browse://newtab")));
        await session.InitializeAsync();
        var page = Assert.IsType<UnifiedHomePage>(session.OriginalNativeView);
        var window = Show(page);
        try
        {
            var mode = page.GetVisualDescendants().OfType<ComboBox>().Single(item => item.Name == "home-search-mode");
            Assert.False(Assert.IsType<ComboBoxItem>(mode.Items.Cast<object>().ElementAt(1)).IsEnabled);
            var search = page.GetVisualDescendants().OfType<TextBox>().Single(item => item.Name == "home-search");
            Assert.True(search.IsEffectivelyVisible);
            search.Text = "charging diagnosis";
            mode.SelectedIndex = 1; // Programmatic/late action must also refuse harmlessly.
            await page.DispatchAsync("home.submit", null);
            Assert.Equal("charging diagnosis", search.Text);
            Assert.Null(navigation);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), item => item.Text?.Contains("AI is unavailable") == true);
            mode.SelectedIndex = 0;
            await page.DispatchAsync("home.submit", null);
            Assert.NotNull(navigation);
            Assert.True(navigation.Address.IsWeb);
            Assert.True(await session.TryPrepareToCloseAsync());
        }
        finally { await session.CloseAndDrainAsync(); window.Close(); }
        Assert.True(session.CanRetireOriginalView);
    }

    [AvaloniaTheory]
    [InlineData("9to1://files")]
    [InlineData("cui://home")]
    [InlineData("browse://history")]
    [InlineData("browse://home/settings")]
    [InlineData("browse://newtab/models")]
    public void Home_factory_does_not_admit_other_namespaces_or_nested_privileged_routes(string route)
    {
        using var lifetime = new CancellationTokenSource();
        Assert.Throws<NotSupportedException>(() => Factory(new(), lifetime.Token).CreateSession(Request(route)));
    }

    [AvaloniaFact]
    public void Home_models_route_is_admitted_only_under_the_home_authority()
    {
        using var lifetime = new CancellationTokenSource();
        // The canonical local-model recovery route (browse://home/models) is admitted, and it
        // resolves to the Home surface.
        var admitted = Factory(new(), lifetime.Token).CreateSession(Request("browse://home/models"));
        Assert.Equal(AppContentNamespace.Browse, admitted.Address.Namespace);
        Assert.Equal("home", admitted.Address.Authority);
        Assert.Equal(new[] { "models" }, admitted.Address.ResourceSegments);
        // The same models segment is NOT admissible under the New Tab authority.
        Assert.Throws<NotSupportedException>(() => Factory(new(), lifetime.Token).CreateSession(Request("browse://newtab/models")));
    }

    [AvaloniaFact]
    public async Task Central_Browse_dispatch_admits_only_browse_home_models_and_refuses_newtab_models_and_arbitrary_segments()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var paths = new LayoutPaths();
        var catalogue = new OwnedRecoveryCatalogueFixture();
        BrowseChrome? chrome = null;
        HomeDiscoverPage? discover = null;
        Window? window = null;
        IBrowseNativeAppContentSession? session = null;
        try
        {
            // The ACTUAL owning Discover page/catalogue is acquired by the normal Home factory.
            // This fixture reports an explicitly empty local catalogue, not a fabricated model.
            chrome = await BrowseChrome.CreateAsync(paths,
                internalContentFactory: Factory(new(), lifetime.Token,
                    discover: () => discover = new HomeDiscoverPage(new HomeDiscoverCatalog([catalogue]))),
                homeProfileId: "central-route-profile", cancellationToken: lifetime.Token)
                .WaitAsync(TimeSpan.FromSeconds(20), lifetime.Token);

            var admitted = await chrome.NavigateAsync("browse://home/models", lifetime.Token)
                .WaitAsync(TimeSpan.FromSeconds(20), lifetime.Token);
            var tab = admitted.SelectedTab;
            Assert.Equal(AppContentNamespace.Browse, tab.Address.Namespace);
            Assert.Equal("home", tab.Address.Authority);
            Assert.Equal(new[] { "models" }, tab.Address.ResourceSegments);
            Assert.Equal(BrowseEngineState.NativeApp, tab.EngineState);
            session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(
                chrome.ObserveOriginalAppContent(admitted.SelectedTabId));
            Assert.NotNull(session);
            Assert.Equal("browse://home/models", session!.Address.Value);
            var page = Assert.IsType<UnifiedHomePage>(session.OriginalNativeView);
            window = Show(page);
            Assert.NotNull(discover);
            Assert.Contains(page.GetVisualDescendants().OfType<HomeDiscoverPage>(), item => ReferenceEquals(item, discover));
            Assert.True(catalogue.Reads > 0, "The genuine Models page must activate its owned catalogue source.");
            Assert.Equal(0, catalogue.Actions);

            var tabCount = admitted.Tabs.Count;
            var refusedNewTab = await chrome.NavigateAsync("browse://newtab/models", lifetime.Token)
                .WaitAsync(TimeSpan.FromSeconds(20), lifetime.Token);
            Assert.Equal("browse://home/models", refusedNewTab.SelectedTab.Address.Value);
            Assert.Equal(tabCount, refusedNewTab.Tabs.Count);
            Assert.Contains("not registered", refusedNewTab.Status, StringComparison.Ordinal);
            var refusedNested = await chrome.NavigateAsync("browse://home/settings", lifetime.Token)
                .WaitAsync(TimeSpan.FromSeconds(20), lifetime.Token);
            Assert.Equal("browse://home/models", refusedNested.SelectedTab.Address.Value);
            Assert.Equal(tabCount, refusedNested.Tabs.Count);
            Assert.Contains("not registered", refusedNested.Status, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (chrome is not null)
                {
                    var preparation = await chrome.PrepareCloseAsync(TestContext.Current.CancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                    Assert.NotNull(preparation);
                    await preparation!.BeginClose().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                    if (session is not null) Assert.True(session.CanRetireOriginalView);
                }
            }
            finally { window?.Close(); Directory.Delete(paths.DataDirectory, true); }
        }
    }

    private sealed class OwnedRecoveryCatalogueFixture : IHomeDiscoverCatalogueSource
    {
        public string ProviderId => "fixture-owned-local-catalogue";
        internal int Reads, Actions;
        public Task<HomeDiscoverProviderSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            return Task.FromResult(new HomeDiscoverProviderSnapshot(ProviderId, "Fixture-owned empty local catalogue",
                HomeDiscoverConnectionState.Connected, HomeDiscoverSourceState.Available, "fixture-revision-1", []));
        }
        public Task<HomeDiscoverActionResult> ExecuteAsync(HomeDiscoverActionRequest request, CancellationToken cancellationToken)
        {
            Actions++;
            throw new NotSupportedException("This catalogue fixture does not authorize model actions.");
        }
    }

    [AvaloniaFact]
    public async Task Home_uses_current_catalogue_and_retains_exact_initialization_and_retirement()
    {
        using var lifetime = new CancellationTokenSource();
        var source = new Registry(); var admissions = 0;
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(source, lifetime.Token,
            admission: (_, _) => { admissions++; return Task.CompletedTask; }).CreateSession(Request()));
        Assert.Equal(0, source.Reads);
        Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
        var original = session.InitializeAsync();
        Assert.Same(original, session.OriginalInitialization);
        Assert.Same(original, session.InitializeAsync());
        await original;
        var page = Assert.IsType<UnifiedHomePage>(session.OriginalNativeView);
        var window = Show(page);
        // The launcher is authored canonical CUI (Repeat Panel="Wrap"), never a donor ListBox, so the
        // same catalogue rule is enforced against the tiles the page actually projects.
        var tiles = page.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Name == "home-app-tile")
            .Select(button => Assert.Single(button.GetVisualDescendants().OfType<TextBlock>()).Text)
            .ToArray();
        Assert.Contains("Files", tiles);
        Assert.Contains("Spaces", tiles);
        Assert.DoesNotContain("Assistants", tiles);
        Assert.DoesNotContain("Dashboard", tiles);
        Assert.Equal(1, source.Reads);
        Assert.Equal(2, admissions);
        Assert.True(await session.TryPrepareToCloseAsync());
        var close = session.CloseAndDrainAsync();
        Assert.Same(close, session.CloseAndDrainAsync());
        Assert.Same(close, session.OriginalClose);
        await close;
        Assert.True(session.CanRetireOriginalView);
        Assert.False(session.IsPublicationCurrent);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Authored_Apps_sidebar_button_dispatches_through_its_actual_CUI_loader()
    {
        using var lifetime = new CancellationTokenSource();
        var source = new Registry();
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(source, lifetime.Token).CreateSession(Request()));
        await session.InitializeAsync();
        var page = Assert.IsType<UnifiedHomePage>(session.OriginalNativeView);
        var window = Show(page);
        var button = HomeRailButton(page, "home-apps", "Apps");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await page.WhenActionsIdleAsync();
        Assert.Equal(2, source.Reads);
        await session.CloseAndDrainAsync();
        window.Close();
    }

    [AvaloniaFact]
    public async Task Close_cancels_original_admission_before_joining_it_and_retains_its_cancellation()
    {
        using var lifetime = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Registry();
        var session = Factory(source, lifetime.Token, admission: async (_, token) =>
        {
            entered.SetResult(); await Task.Delay(Timeout.Infinite, token);
        }).CreateSession(Request());
        var original = session.InitializeAsync();
        await entered.Task;
        var close = session.CloseAndDrainAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(close.IsCompleted);
        Assert.False(close.IsCompletedSuccessfully);
        Assert.True(original.IsCompleted);
        Assert.Equal(0, source.Reads);
        Assert.False(session.IsPublicationCurrent);
        Assert.Same(close, session.CloseAndDrainAsync());
    }

    [AvaloniaFact]
    public async Task Restored_execution_context_cannot_join_the_initialization_callback_owning_it()
    {
        using var lifetime = new CancellationTokenSource();
        var capturedContext = ExecutionContext.Capture()!;
        var session = Factory(new(), lifetime.Token).CreateSession(Request());
        var refused = false;
        session.StateChanged += (_, _) => ExecutionContext.Run(capturedContext, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = session.CloseAndDrainAsync(); }); refused = true;
        }, null);
        await session.InitializeAsync();
        Assert.True(refused);
        await session.CloseAndDrainAsync();
    }

    [AvaloniaTheory]
    [InlineData(390, 844, CuiAppearance.SuperBright)]
    [InlineData(390, 844, CuiAppearance.Bright)]
    [InlineData(390, 844, CuiAppearance.Dark)]
    [InlineData(390, 844, CuiAppearance.SuperDark)]
    [InlineData(1280, 900, CuiAppearance.SuperBright)]
    [InlineData(1280, 900, CuiAppearance.Bright)]
    [InlineData(1280, 900, CuiAppearance.Dark)]
    [InlineData(1280, 900, CuiAppearance.SuperDark)]
    public async Task Shared_Home_renders_canonical_apps_and_keeps_controls_inside_the_window(int width, int height, CuiAppearance appearance)
    {
        CuiNativeHost.InitialisePrimitiveTheme(global::Avalonia.Application.Current!, "9to1", appearance);
        using var lifetime = new CancellationTokenSource();
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token).CreateSession(Request()));
        await session.InitializeAsync();
        var page = session.OriginalNativeView;
        var window = new Window { Width = width, Height = height, Content = page };
        try
        {
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(width, frame.PixelSize.Width); Assert.Equal(height, frame.PixelSize.Height);
            // Preserve the actual frame even when a later semantic/bounds assertion fails.
            CaptureOptionalFrame(window, frame, $"home-apps-{width}-{appearance}", "UnifiedHome Apps layout fixture");
            foreach (var (id, label) in new[] { ("home-apps", "Apps"), ("home-models", "Models / Discover"), ("home-newtab", "New Tab") })
            {
                // Owner-refined icon-only rail: Content is an Image, not its accessible label.
                var button = HomeRailButton(page, id, label);
                Assert.True(button.Bounds.Width >= 44 && button.Bounds.Height >= 24);
                var origin = button.TranslatePoint(default, window);
                Assert.NotNull(origin);
                Assert.InRange(origin.Value.X, -1, width - 43);
                Assert.InRange(origin.Value.Y, -1, height - 23);
                Assert.True(origin.Value.X + button.Bounds.Width <= width + 1);
            }

        }
        finally { await session.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1440, 920)]
    [InlineData(1280, 520)]
    [InlineData(390, 844)]
    public async Task Browse_hosted_Home_sidebar_scrolls_without_spilling_into_main_content(int width, int height)
    {
        using var lifetime = new CancellationTokenSource();
        var paths = new LayoutPaths();
        var chrome = await BrowseChrome.CreateAsync(paths,
            internalContentFactory: Factory(new(), lifetime.Token), homeProfileId: "layout-fixture-profile");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new LayoutReadiness());
        window.Width = width; window.Height = height;
        try
        {
            // The real host registers this SAME resolver on the exact scene before Show.
            // Keep renderer/readiness unchanged; failures still reach the original close/drain.
            RegisterProductionImageResolver(window);
            window.Show(); await window.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(20)); Layout();
            using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
            CaptureOptionalFrame(window, frame, $"browse-home-{width}x{height}", "Browse/Home layout fixture; explicit fixture readiness");
            var page = Assert.Single(window.GetVisualDescendants().OfType<UnifiedHomePage>());
            var sidebar = Assert.IsType<CuiContainerControl>(page.GetVisualDescendants().OfType<Control>()
                .Single(item => item.Name == "home-sidebar"));
            var main = page.GetVisualDescendants().OfType<Control>().Single(item => item.Name == "home-main");
            var sideOrigin = sidebar.TranslatePoint(default, window)!.Value;
            var mainOrigin = main.TranslatePoint(default, window)!.Value;
            // Canonical Home is seven scrolling icon destinations plus a pinned Settings footer.
            // Profile/notifications/model remain on Haven's persistent topbar, not this rail.
            var nav = Assert.IsType<CuiContainerControl>(page.GetVisualDescendants().OfType<Control>()
                .Single(item => item.Name == "home-rail-nav"));
            var footer = Assert.IsType<CuiContainerControl>(page.GetVisualDescendants().OfType<Control>()
                .Single(item => item.Name == "home-rail-footer"));
            var scroll = Assert.IsType<ScrollViewer>(nav.Child);
            var buttons = nav.AuthoredChildren.OfType<Button>().ToArray();
            Assert.Equal(7, buttons.Length);
            Assert.Equal(new[] { "home-apps", "home-dashboard", "home-models", "home-newtab", "home-library", "home-events", "home-mesh" },
                buttons.Select(button => button.Name));
            var settings = Assert.Single(footer.AuthoredChildren.OfType<Button>());
            Assert.Same(settings, HomeRailButton(page, "home-settings", "Settings"));
            Assert.Equal(global::Avalonia.Layout.Orientation.Vertical, Assert.IsType<StackPanel>(nav.Layout).Orientation);
            Assert.True(sideOrigin.X + sidebar.Bounds.Width <= mainOrigin.X + .5);
            double previousBottom = -1;
            foreach (var button in buttons)
            {
                var origin = button.TranslatePoint(default, nav.Layout)!.Value;
                Assert.True(origin.Y >= previousBottom, "The icon rail wrapped into a second column.");
                Assert.True(origin.X >= 0 && origin.X + button.Bounds.Width <= nav.Bounds.Width + .5);
                previousBottom = origin.Y + button.Bounds.Height;
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                var icon = Assert.Single(button.GetVisualDescendants().OfType<Image>());
                Assert.NotNull(icon.Source);
                Assert.True(icon.Bounds.Width > 0 && icon.Bounds.Height > 0);
            }
            Assert.True(main.Bounds.Height >= height - 300, $"Apps has only {main.Bounds.Height} vertical space.");
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + .5, "Sidebar content spills horizontally.");
            var settingsOrigin = settings.TranslatePoint(default, window)!.Value;
            Assert.True(settingsOrigin.Y >= sideOrigin.Y);
            Assert.True(settingsOrigin.Y + settings.Bounds.Height <= sideOrigin.Y + sidebar.Bounds.Height + .5);
            if (scroll.Extent.Height > scroll.Viewport.Height + .5)
            {
                scroll.Offset = new Vector(0, scroll.Extent.Height); Layout();
                Assert.True(scroll.Offset.Y > 0);
                var last = HomeRailButton(page, "home-mesh", "Mesh");
                var lastOrigin = last.TranslatePoint(default, scroll)!.Value;
                Assert.True(lastOrigin.Y >= -.5);
                Assert.True(lastOrigin.Y + last.Bounds.Height <= scroll.Viewport.Height + .5);
                Assert.Equal(settingsOrigin, settings.TranslatePoint(default, window)!.Value);
            }
            var appRegion = Assert.Single(page.GetVisualDescendants().OfType<Control>(),
                item => item.Name == "home-apps-results");
            Assert.True(appRegion.Bounds.Width >= 150 && appRegion.Bounds.Height >= 180);
            var selectedId = chrome.State.SelectedTabId;
            scroll.Offset = default; Layout();
            var apps = buttons.Single(item => item.Name == "home-apps");
            apps.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(selectedId, chrome.State.SelectedTabId);
            Assert.Same(page, Assert.Single(window.GetVisualDescendants().OfType<UnifiedHomePage>()));
            // Preserve the existing 44px requirement; authored40px targets may still FAIL.
            // A captured failing frame is evidence, never a waived product-quality assertion.
            foreach (var button in buttons.Append(settings))
                Assert.True(button.Bounds.Width >= 44 && button.Bounds.Height >= 24);

        }
        finally
        {
            window.Close();
            if (window.OriginalClosePreparation is { } preparation)
                Assert.True(await preparation.WaitAsync(TimeSpan.FromSeconds(20)));
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.OriginalClose);
            await window.OriginalClose!.WaitAsync(TimeSpan.FromSeconds(20));
            Directory.Delete(paths.DataDirectory, true);
        }
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaFact]
    public async Task Shared_Home_rail_scrolls_in_a_short_component_viewport_and_keeps_Settings_pinned()
    {
        // Stress the SAME canonical Home component below Browse's 520px shipping window minimum.
        // This is component overflow coverage, not acceptance of a below-minimum browser window.
        using var lifetime = new CancellationTokenSource();
        IBrowseNativeAppContentSession? session = null;
        Window? window = null;
        var failures = new List<Exception>();
        Task? initialization = null, close = null;
        try
        {
            session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token).CreateSession(Request()));
            initialization = session.InitializeAsync();
            await initialization;
            var page = session.OriginalNativeView;
            // Retain the acquired window before setting properties or attaching the owned view.
            window = new Window(); window.Width = 390; window.Height = 320; window.Content = page;
            window.Show(); Layout();
            var nav = Assert.IsType<CuiContainerControl>(page.GetVisualDescendants().OfType<Control>()
                .Single(item => item.Name == "home-rail-nav"));
            var scroll = Assert.IsType<ScrollViewer>(nav.Child);
            var settings = HomeRailButton(page, "home-settings", "Settings");
            var pinnedOrigin = settings.TranslatePoint(default, window)!.Value;
            Assert.True(scroll.Viewport.Height > 0);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "Short real Home viewport must exercise rail overflow.");
            scroll.Offset = new Vector(0, scroll.Extent.Height); Layout();
            Assert.True(scroll.Offset.Y > 0);
            var last = HomeRailButton(page, "home-mesh", "Mesh");
            var lastOrigin = last.TranslatePoint(default, scroll)!.Value;
            Assert.True(lastOrigin.Y >= -.5);
            Assert.True(lastOrigin.Y + last.Bounds.Height <= scroll.Viewport.Height + .5);
            Assert.Equal(pinnedOrigin, settings.TranslatePoint(default, window)!.Value);
        }
        catch (Exception cause) { Retain(cause); }
        finally
        {
            var drained = false;
            if (session is not null)
            {
                try
                {
                    close = session.CloseAndDrainAsync();
                    await close;
                    Assert.True(session.CanRetireOriginalView);
                    drained = true;
                }
                catch (Exception cause) { Retain(cause); }
            }
            // A failed original drain retains its mounted view; never detach or close around it.
            if (window is not null && drained)
            {
                try { window.Content = null; } catch (Exception cause) { Retain(cause); }
                try { window.Close(); } catch (Exception cause) { Retain(cause); }
            }
            // Await may expose only one fault. Preserve raw direct siblings on the exact tasks.
            RetainTask(initialization); RetainTask(close);
            if (session is not null) { RetainTask(session.OriginalInitialization); RetainTask(session.OriginalClose); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Short Home layout assertion and independent original cleanup failed.", failures);
        void Retain(Exception cause)
        {
            if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause);
        }
        void RetainTask(Task? source)
        {
            if (source?.Exception is { } original)
                foreach (var cause in original.InnerExceptions) Retain(cause);
        }
        void Layout() { window!.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }

    private static Button HomeRailButton(Control page, string id, string label)
    {
        var button = Assert.Single(page.GetVisualDescendants().OfType<Button>(), item => item.Name == id);
        Assert.Equal(label, AutomationProperties.GetName(button));
        Assert.Equal(label, ToolTip.GetTip(button));
        Assert.True(button.IsEffectivelyVisible);
        return button;
    }

    private static void RegisterProductionImageResolver(BrowseNativeWindow window)
    {
        // Same retained scene seam used by existing BrowseWorkspaceLayoutTests.
        var scene = typeof(BrowseNativeWindow).GetField("_originalScene", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window) as CuiNativeScene;
        Assert.NotNull(scene);
        scene!.ControlRegistry!.RegisterImageSourceResolver(HavenPackagedSvgResolver.Resolve);
    }

    private static readonly object CaptureGate = new();
    private static int CaptureCount;
    private static long CaptureBytes;
    private static void CaptureOptionalFrame(Window window, Bitmap frame, string name, string fixture)
    {
        var directory = Environment.GetEnvironmentVariable("HAVEN_UNIFIED_HOME_CAPTURES");
        if (string.IsNullOrWhiteSpace(directory)) return;
        lock (CaptureGate)
        {
            try
            {
                const int maximumBytes = 1024 * 1024;
                var source = Environment.GetEnvironmentVariable("HAVEN_CAPTURE_SOURCE_REVISION");
                var build = Environment.GetEnvironmentVariable("HAVEN_CAPTURE_BUILD_REVISION");
                if (CaptureCount >= 6 || CaptureBytes >= maximumBytes || !IsRevision(source) || !IsRevision(build))
                    throw new InvalidOperationException("NO_IMAGE: capture allowance or exact runner source/build identity unavailable.");
                // Fixed capacity refuses oversized encoding BEFORE any file write.
                using var encoded = new MemoryStream(new byte[maximumBytes], writable: true);
                encoded.SetLength(0); frame.Save(encoded);
                var png = encoded.ToArray();
                if (CaptureBytes + png.Length > maximumBytes) throw new IOException("NO_IMAGE: PNG run allowance exceeded.");
                var stem = name + "-" + Guid.NewGuid().ToString("N");
                var pngPath = Path.Combine(directory, stem + ".png");
                var metadata = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    captured_utc = DateTimeOffset.UtcNow, source_revision = source, build_revision = build,
                    fixture, renderer = "headless-Skia", installed_native = false,
                    appearance = CuiThemeScopeApplier.DetectAppearance().ToString(),
                    viewport = new { window.ClientSize.Width, window.ClientSize.Height },
                    pixels = new { frame.PixelSize.Width, frame.PixelSize.Height },
                    outcome = "PRE_ASSERTION_FRAME; owning TRX and original cleanup determine result",
                    png = new { path = pngPath, bytes = png.Length, sha256 = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant() },
                    assemblies = new[] { typeof(UnifiedHomeContentTests).Assembly, typeof(UnifiedHomePage).Assembly,
                        typeof(CuiSceneHost).Assembly, typeof(BrowseNativeWorkspace).Assembly }.Distinct().Select(assembly => new
                        {
                            name = assembly.GetName().Name, path = assembly.Location,
                            sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant()
                        }).ToArray()
                });
                foreach (var location in new[] { "/workspace", Path.GetTempPath(), directory }.Where(Directory.Exists))
                {
                    // Longest actual mount prefix also checks the separate cloud /tmp volume.
                    var full = Path.GetFullPath(location).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    var drive = DriveInfo.GetDrives().Where(item => full.StartsWith(item.Name, StringComparison.Ordinal))
                        .OrderByDescending(item => item.Name.Length).First();
                    if (drive.AvailableFreeSpace < 268435456L + png.Length + metadata.Length + 8192)
                        throw new IOException("NO_IMAGE: disk floor would be crossed.");
                }
                Directory.CreateDirectory(directory);
                CaptureCount++; CaptureBytes += png.Length;
                using (var output = new FileStream(pngPath, FileMode.CreateNew, FileAccess.Write)) output.Write(png);
                using (var output = new FileStream(Path.Combine(directory, stem + ".json"), FileMode.CreateNew, FileAccess.Write))
                    output.Write(metadata);
                Console.WriteLine("PRE_ASSERTION_CAPTURE: " + pngPath);
            }
            catch (Exception failure)
            {
                // Optional capture cannot suppress/replace genuine assertions or original drain faults.
                Console.WriteLine("OPTIONAL_CAPTURE_FAILED_OR_REFUSED: " + failure);
            }
        }
        static bool IsRevision(string? value) => value is { Length: 40 } && value.All(Uri.IsHexDigit);
    }

    // The New Tab fixture renders the real dashboard with empty, local repositories.
    private sealed class EmptyDashboard : IDashboardRepository
    {
        public Task<DashboardSnapshot> GetSnapshotAsync(DateTimeOffset now, CancellationToken token) =>
            Task.FromResult(new DashboardSnapshot(now, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, [], []));
    }
    private sealed class EmptyDashboardLayout : IDashboardLayoutRepository
    {
        public Task<IReadOnlyList<DashboardTileLayout>> GetAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<DashboardTileLayout>>([]);
        public Task SaveAsync(IReadOnlyList<DashboardTileLayout> layout, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class EmptySettings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => Task.FromResult(_values.GetValueOrDefault(key) as T);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class LayoutReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(
            new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "home-layout-fixture", "Explicit headless layout fixture"));
    }
    private sealed class LayoutPaths : IAppPaths
    {
        public LayoutPaths() { Directory.CreateDirectory(DataDirectory); }
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "home-layout-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "fixture.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
    }

    private static Window Show(Control page)
    {
        var window = new Window { Width = 1280, Height = 900, Content = page };
        window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); return window;
    }

    private sealed class Registry : IModeRegistry
    {
        internal int Reads;
        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Reads++; return Task.FromResult(BuiltInModeSeed.Modes); }
        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken token) => Task.FromResult(BuiltInModeSeed.Modes.SingleOrDefault(mode => mode.Id == id));
        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken token) => Task.FromResult(BuiltInModeSeed.Modes.SingleOrDefault(mode => mode.Key == key));
        public Task UpsertModeAsync(ModeDefinition mode, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task AddVersionAsync(ModeVersion version, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken token) => throw new NotSupportedException();
    }
}
