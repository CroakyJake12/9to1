using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Tests;

public sealed class UnifiedHomeContentTests
{
    private static BrowseAppContentRequest Request(string address = "browse://home") =>
        new(Guid.NewGuid(), AppContentAddress.Parse(address), "original-home-profile", BrowserTabPrivacy.Standard);

    private static UnifiedHomeContentFactory Factory(Registry source, CancellationToken lifetime,
        Func<BrowseNavigationIntent, Task, bool>? navigation = null,
        Func<BrowseAppContentRequest, CancellationToken, Task>? admission = null, bool aiAvailable = true,
        Func<NewDashboardPage>? dashboard = null) => new(source,
            dashboard ?? (() => throw new InvalidOperationException("Apps must not construct an unused dashboard.")),
            () => throw new InvalidOperationException("Apps must not construct an unused model catalogue."),
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
        // Regression coverage through the REAL central Browse dispatch (NavigateAsync →
        // BrowseChrome.IsOwnedCanvas → internal content factory), never a Parse-string fixture or a
        // direct factory call. It depends on the root-owned IsOwnedCanvas allowance for
        // browse://home/models ONLY (returned as the 07 root-preserving hunk).
        using var lifetime = new CancellationTokenSource();
        var paths = new LayoutPaths();
        try
        {
            var chrome = await BrowseChrome.CreateAsync(paths,
                internalContentFactory: Factory(new(), lifetime.Token), homeProfileId: "central-route-profile")
                .WaitAsync(TimeSpan.FromSeconds(20));

            // The canonical local-model recovery route is admitted only under the home authority.
            var admitted = await chrome.NavigateAsync("browse://home/models").WaitAsync(TimeSpan.FromSeconds(20));
            var tab = admitted.SelectedTab;
            Assert.Equal(AppContentNamespace.Browse, tab.Address.Namespace);
            Assert.Equal("home", tab.Address.Authority);
            Assert.Equal(new[] { "models" }, tab.Address.ResourceSegments);
            Assert.Equal(BrowseEngineState.NativeApp, tab.EngineState);
            var session = chrome.ObserveOriginalAppContent(admitted.SelectedTabId);
            Assert.NotNull(session);
            Assert.Equal("browse://home/models", session!.Address.Value);

            // The SAME models segment is NOT admissible under the New Tab authority: the central
            // dispatch refuses it (route left unchanged, no new tab/content owner created).
            var tabCount = admitted.Tabs.Count;
            var refusedNewTab = await chrome.NavigateAsync("browse://newtab/models").WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("browse://home/models", refusedNewTab.SelectedTab.Address.Value);
            Assert.Equal(tabCount, refusedNewTab.Tabs.Count); // no tab was created for the refused route
            Assert.Contains("not registered", refusedNewTab.Status, StringComparison.Ordinal);

            // Arbitrary segments under home stay refused as well.
            var refusedNested = await chrome.NavigateAsync("browse://home/settings").WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal("browse://home/models", refusedNested.SelectedTab.Address.Value);
            Assert.Equal(tabCount, refusedNested.Tabs.Count);
            Assert.Contains("not registered", refusedNested.Status, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(paths.DataDirectory, true);
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
        var button = page.GetVisualDescendants().OfType<Button>().Single(item => item.Content?.ToString() == "Apps");
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
            foreach (var label in new[] { "Apps", "Models / Discover", "New Tab" })
            {
                var button = page.GetVisualDescendants().OfType<Button>().Single(item => item.Content?.ToString() == label);
                Assert.True(button.Bounds.Width >= 44 && button.Bounds.Height >= 24);
                var origin = button.TranslatePoint(default, window);
                Assert.NotNull(origin);
                Assert.InRange(origin.Value.X, -1, width - 43);
                Assert.InRange(origin.Value.Y, -1, height - 23);
                Assert.True(origin.Value.X + button.Bounds.Width <= width + 1);
            }
            var folder = Environment.GetEnvironmentVariable("HAVEN_UNIFIED_HOME_CAPTURES");
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                frame.Save(Path.Combine(folder, $"home-apps-{width}-{appearance}.png"));
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
            window.Show(); await window.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(20)); Layout();
            var page = Assert.Single(window.GetVisualDescendants().OfType<UnifiedHomePage>());
            var sidebar = Assert.IsType<CuiContainerControl>(page.GetVisualDescendants().OfType<Control>()
                .Single(item => item.Name == "home-sidebar"));
            var main = page.GetVisualDescendants().OfType<Control>().Single(item => item.Name == "home-main");
            var sideOrigin = sidebar.TranslatePoint(default, window)!.Value;
            var mainOrigin = main.TranslatePoint(default, window)!.Value;
            var scroll = Assert.IsType<ScrollViewer>(sidebar.Child);
            var buttons = sidebar.AuthoredChildren.OfType<Button>().ToArray();
            Assert.Equal(10, buttons.Length);
            foreach (var button in buttons)
            {
                var caption = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>());
                Assert.True(caption.TextLayout.Width <= caption.Bounds.Width + .5,
                    $"{button.Content} caption is clipped at {width}×{height}.");
            }
            if (width >= 720)
            {
                Assert.Equal(global::Avalonia.Layout.Orientation.Vertical, Assert.IsType<WrapPanel>(sidebar.Layout).Orientation);
                Assert.True(sideOrigin.X + sidebar.Bounds.Width <= mainOrigin.X + .5);
                double previousBottom = -1;
                foreach (var button in buttons)
                {
                    var origin = button.TranslatePoint(default, sidebar.Layout)!.Value;
                    Assert.True(origin.Y >= previousBottom, "The sidebar wrapped into a second column.");
                    Assert.InRange(origin.X, 0, 1);
                    previousBottom = origin.Y + button.Bounds.Height;
                }
            }
            else
            {
                Assert.Equal(global::Avalonia.Layout.Orientation.Horizontal, Assert.IsType<WrapPanel>(sidebar.Layout).Orientation);
                Assert.InRange(sidebar.Bounds.Height, 40, 160);
                Assert.True(sideOrigin.Y + sidebar.Bounds.Height <= mainOrigin.Y + .5);
            }
            Assert.True(main.Bounds.Height >= height - 300, $"Apps has only {main.Bounds.Height} vertical space.");
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + .5, "Sidebar content spills horizontally.");
            if (height <= 520 || width < 720)
            {
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                scroll.Offset = new Vector(0, scroll.Extent.Height); Layout();
                Assert.True(scroll.Offset.Y > 0);
                var last = buttons.Single(item => item.Content?.ToString() == "Notifications");
                var lastOrigin = last.TranslatePoint(default, window)!.Value;
                Assert.True(lastOrigin.Y >= sideOrigin.Y);
                Assert.True(lastOrigin.Y + last.Bounds.Height <= sideOrigin.Y + sidebar.Bounds.Height + .5);
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
            using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
            var folder = Environment.GetEnvironmentVariable("HAVEN_UNIFIED_HOME_CAPTURES");
            if (!string.IsNullOrEmpty(folder))
            { Directory.CreateDirectory(folder); frame.Save(Path.Combine(folder, $"browse-home-{width}x{height}.png")); }
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
