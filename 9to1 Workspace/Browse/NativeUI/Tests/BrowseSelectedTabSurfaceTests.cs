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
using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    /// <summary>The owner-latest selected-tab correction: the active tab's highlight must be ONE
    /// rounded surface that includes both the title and the close control, with quiet transparent
    /// inner buttons. This test asserts real rendered control/surface bounds and brushes, not
    /// labels, and uses real pointer actions.</summary>
    [AvaloniaFact]
    public async Task Selected_tab_surface_joins_title_and_close_as_one_rounded_pill()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths(),
            internalContentFactory: new TabSurfaceInternalFixtureFactory(), homeProfileId: "tab-surface-fixture");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Layout(window);

            // Single Home tab: not closable, so no close control; full outer roundness retained.
            Assert.True(chrome.State.SelectedTab.IsHome);
            var home = Tabs(window).Single();
            Assert.False(Close(home).IsEffectivelyVisible);
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
