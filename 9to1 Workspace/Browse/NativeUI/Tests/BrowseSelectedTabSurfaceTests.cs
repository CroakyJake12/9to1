using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    [AvaloniaTheory]
    [InlineData(360, 38, 42)]
    [InlineData(1180, 48, 52)]
    public async Task Embedded_tab_strip_keeps_new_tab_adjacent_until_overflow_and_uses_scoped_neutral_selection(
        int width, int tabStripHeight, int toolbarHeight)
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths(),
            internalContentFactory: new TabSurfaceInternalFixtureFactory(), homeProfileId: "chrome-layout-fixture");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: new SharedControlsFakeContext(new SharedControlsFakeGateway()));
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        window.Width = width;
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Layout(window);
            var strip = Authored(window, "BrowseTabStrip");
            var toolbar = Authored(window, "BrowseNavigationBar");
            Assert.InRange(strip.Bounds.Height, tabStripHeight - .5, tabStripHeight + .5);
            Assert.InRange(toolbar.Bounds.Height, toolbarHeight - .5, toolbarHeight + .5);
            var add = Find(window, "BrowseNewTabButton");
            var tab = Tabs(window).Single();
            var tabEnd = tab.TranslatePoint(new Point(tab.Bounds.Width, 0), window)!.Value.X;
            var addOrigin = add.TranslatePoint(default, window)!.Value.X;
            Assert.InRange(addOrigin - tabEnd, -.5, 12);
            Capture("sparse");

            var scopedChrome = Color.Parse("#FF29272D");
            window.SceneHost.Resources["CuiPanelBrush"] = new SolidColorBrush(scopedChrome);
            window.SceneHost.Resources["CuiTextBrush"] = new SolidColorBrush(Color.Parse("#FFEAE8F0"));
            window.SceneHost.Resources["CuiAccentBrush"] = Brushes.DodgerBlue;
            Layout(window);
            AssertSelection(tab, scopedChrome, 1.35, 4.5);

            scopedChrome = Color.Parse("#FFF3F2EF");
            window.SceneHost.Resources["CuiPanelBrush"] = new SolidColorBrush(scopedChrome);
            window.SceneHost.Resources["CuiTextBrush"] = new SolidColorBrush(Colors.Black);
            window.SceneHost.Resources["CuiHighContrast"] = true;
            Layout(window);
            AssertSelection(tab, scopedChrome, 3, 7);

            for (var index = 0; index < 9; index++)
            {
                await Pointer(window, workspace, add);
                Layout(window);
                Assert.Equal(index + 2, chrome.State.Tabs.Count);
            }
            var viewport = Authored(window, "BrowseTabViewport");
            var scroll = viewport.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
            var viewportEnd = viewport.TranslatePoint(new Point(viewport.Bounds.Width, 0), window)!.Value.X;
            addOrigin = add.TranslatePoint(default, window)!.Value.X;
            Assert.InRange(addOrigin - viewportEnd, -.5, 2);
            Assert.InRange(addOrigin + add.Bounds.Width, 0, width + .5);
            var selected = chrome.State.SelectedTabId;
            Assert.True(add.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.None, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.None, null);
            Layout(window);
            Assert.Contains(viewport.GetVisualDescendants().OfType<Button>(), button => button.IsFocused &&
                button.Name is "CuiOverflowLeft" or "CuiOverflowRight");
            Assert.Equal(selected, chrome.State.SelectedTabId);
            Capture("overflow-high-contrast");
        }
        finally { await Retire(window); }

        static void AssertSelection(CuiContainerControl tab, Color chrome, double surfaceRatio, double inkRatio)
        {
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(tab.Background).Color;
            var ink = Assert.IsAssignableFrom<ISolidColorBrush>(Title(tab).Foreground).Color;
            Assert.Equal(fill.R, fill.G);
            Assert.Equal(fill.G, fill.B);
            Assert.True(CuiContrast.Ratio(fill, chrome) >= surfaceRatio - .03);
            Assert.True(CuiContrast.Ratio(ink, fill) >= inkRatio - .03);
        }

        void Capture(string state)
        {
            if (Environment.GetEnvironmentVariable("BROWSE_EVIDENCE_DIRECTORY") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, $"embedded-shell-{state}-{width}x{window.Bounds.Height:0}.png"));
        }
    }

    /// <summary>The owner-latest selected-tab correction: the active tab's highlight must be ONE
    /// rounded surface that includes both the title and the close control, with quiet transparent
    /// inner buttons. This test asserts real rendered control/surface bounds and brushes, not
    /// labels, and uses real pointer actions.</summary>
    [AvaloniaFact]
    public async Task Selected_tab_surface_joins_title_and_close_as_one_rounded_pill()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths(),
            internalContentFactory: new TabSurfaceInternalFixtureFactory(), homeProfileId: "tab-surface-fixture");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, null, null, tabIconSource: address =>
            address.Authority == "home" ? "avares://HavenOS.Apps.Browse.NativeUI/Assets/nav-forward.svg" : null);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Layout(window);

            // Single Home tab: not closable, so no close control; full outer roundness retained.
            Assert.True(chrome.State.SelectedTab.IsHome);
            var home = Tabs(window).Single();
            Assert.False(Close(home).IsEffectivelyVisible);
            var icon = Assert.Single(home.GetVisualDescendants().OfType<Image>());
            Assert.NotNull(icon.Source);
            Assert.Equal(16, icon.Bounds.Width);
            Assert.Equal(FontWeight.Bold, Title(home).FontWeight);
            Assert.True(Title(home).Padding.Left >= icon.Bounds.Width + 10);
            Assert.Equal(new CornerRadius(8), home.CornerRadius);

            // The address text's actual presenter is vertically centred with the trailing arrow.
            var address = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "BrowseAddressBox");
            var navigate = Find(window, "BrowseNavigateButton");
            var addressPresenter = address.GetVisualDescendants().OfType<TextPresenter>().Single();
            var presenterCentre = addressPresenter.TranslatePoint(new Point(0, addressPresenter.Bounds.Height / 2), window)!.Value;
            var navigateCentre = navigate.TranslatePoint(new Point(0, navigate.Bounds.Height / 2), window)!.Value;
            Assert.InRange(Math.Abs(presenterCentre.Y - navigateCentre.Y), 0, 2.5);

            // Open a second tab, which becomes selected.
            await Click(window, workspace, Find(window, "BrowseNewTabButton")); Layout(window);
            var tabs = Tabs(window);
            Assert.Equal(2, tabs.Length);
            var selected = tabs.Single(tab => !IsTransparent(tab.Background));
            var unselected = tabs.Single(tab => IsTransparent(tab.Background));

            // One surface contains both title and close.
            Assert.True(Contains(selected, Title(selected)));
            Assert.True(Contains(selected, Close(selected)));
            Assert.True(Close(selected).IsEffectivelyVisible);

            // Inner buttons do not paint their own islands.
            Assert.True(IsTransparent(Title(selected).Background));
            Assert.True(IsTransparent(Close(selected).Background));

            // Selecting the other tab (real pointer) moves the joined surface.
            await Pointer(window, workspace, Title(unselected)); Layout(window);
            Assert.False(IsTransparent(unselected.Background));
            Assert.True(IsTransparent(selected.Background));
        }
        finally { await Retire(window); }
    }

    /// <summary>The joined selected surface must survive a narrow window and a close action must keep
    /// the selection on its own tab (close never selects another tab).</summary>
    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(1180)]
    public async Task Tab_selected_surface_survives_narrow_width_and_close_keeps_selection(int width)
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths(),
            internalContentFactory: new TabSurfaceInternalFixtureFactory(), homeProfileId: "tab-surface-fixture");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        window.Width = width;
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Layout(window);
            await Click(window, workspace, Find(window, "BrowseNewTabButton")); Layout(window);
            await Click(window, workspace, Find(window, "BrowseNewTabButton")); Layout(window);

            var tabs = Tabs(window);
            Assert.Equal(3, tabs.Length);
            var selected = tabs.Single(tab => !IsTransparent(tab.Background));
            var selectedId = chrome.State.SelectedTabId;
            var closableUnselected = tabs.Single(tab => IsTransparent(tab.Background) && Close(tab).IsEffectivelyVisible);

            // A single surface still contains both controls at narrow width.
            Assert.True(Contains(selected, Title(selected)));
            Assert.True(Contains(selected, Close(selected)));

            // Closing an unselected, closable tab keeps the joined selection intact.
            await Pointer(window, workspace, Close(closableUnselected)); Layout(window);
            Assert.Equal(selectedId, chrome.State.SelectedTabId);
            tabs = Tabs(window);
            Assert.Equal(2, tabs.Length);
            Assert.False(IsTransparent(tabs.Single(tab => !IsTransparent(tab.Background)).Background));
        }
        finally { await Retire(window); }
    }

    // The fixture supplies only the content-owner contract; production BrowseChrome still creates
    // its canonical browse://home pinned tab and browse://newtab closable tabs. This does not boot
    // the production Home canvas or grant Home/profile authority.
    private sealed class TabSurfaceInternalFixtureFactory : IBrowseInternalContentFactory
    {
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request) => new TabSurfaceInternalFixtureSession(request.Address);
    }

    private sealed class TabSurfaceInternalFixtureSession(AppContentAddress address) : IBrowseAppContentSession
    {
        public AppContentAddress Address => address;
        public string Title => address.Authority == "home" ? "Home" : "New Tab";
        public string IconKey => "home";
        public event EventHandler? StateChanged { add { } remove { } }
        public bool IsPublicationCurrent => OriginalInitialization is { IsCompletedSuccessfully: true } && OriginalClose is null;
        public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
        public Task? OriginalInitialization { get; private set; }
        public Task? OriginalClose { get; private set; }
        public Task InitializeAsync(CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); return OriginalInitialization ??= Task.CompletedTask; }
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(true); }
        public Task CloseAndDrainAsync() => OriginalClose ??= Task.CompletedTask;
    }

    private static CuiContainerControl[] Tabs(BrowseNativeWindow window) =>
        window.GetVisualDescendants().OfType<CuiContainerControl>()
            .Where(container => container.AuthoredChildren.OfType<Button>().Any(button => button.Name == "BrowseTabButton"))
            .ToArray();

    private static Button Title(CuiContainerControl tab) =>
        tab.AuthoredChildren.OfType<Button>().Single(button => button.Name == "BrowseTabButton");

    private static Button Close(CuiContainerControl tab) =>
        tab.AuthoredChildren.OfType<Button>().Single(button => button.Name == "BrowseCloseTabRowButton");

    private static bool Contains(CuiContainerControl tab, Control child)
    {
        var origin = child.TranslatePoint(default, tab)!.Value;
        // Both rectangles use the tab's LOCAL coordinates; Bounds itself is parent-relative.
        return new Rect(default, tab.Bounds.Size).Contains(new Rect(origin, child.Bounds.Size));
    }

    private static bool IsTransparent(IBrush? brush) =>
        brush is ISolidColorBrush solid && solid.Color.A == 0;

    private static async Task Pointer(BrowseNativeWindow window, BrowseNativeWorkspace workspace, Button button)
    {
        button.BringIntoView(); Layout(window);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var before = workspace.OriginalCommand;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
        await Observe(Loader(window).WhenActionsIdleAsync(), window);
        Assert.NotNull(workspace.OriginalCommand); Assert.NotSame(before, workspace.OriginalCommand);
        await Observe(workspace.OriginalCommand!, window);
        await Task.WhenAll(workspace.OriginalNotifications.ToArray()); Layout(window);
    }

    private static void Layout(BrowseNativeWindow window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
}
