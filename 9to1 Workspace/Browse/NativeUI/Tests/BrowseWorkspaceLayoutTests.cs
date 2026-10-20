using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Browser;
using Haven.Desktop.HavenUI.Backend;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    [AvaloniaTheory]
    [InlineData(1440, 920)]
    [InlineData(1280, 520)]
    [InlineData(390, 844)]
    [InlineData(360, 520)]
    public async Task Compact_chrome_leaves_content_room_and_tools_scroll_without_retiring_the_web_view(int width, int height)
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        window.Width = width; window.Height = height;
        RegisterProductionImageResolver(window);
        var validationRegistry = BrowseNativeSurface.CreateScene(workspace, new FixtureReadiness()).ControlRegistry!;
        RegisterProductionImageResolver(validationRegistry);
        using (var validation = new CuiControlLoader(validationRegistry))
        {
            validation.SetBindingContext(workspace); validation.SetActionDispatcher(workspace);
            var loaded = validation.TryLoad(BrowseNativeWorkspace.LoadDocument());
            Assert.DoesNotContain(loaded.Diagnostics, item => item.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
        }
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Layout();
            var content = Named("BrowseContentViewport");
            var contentOrigin = content.TranslatePoint(default, window)!.Value;
            Assert.InRange(contentOrigin.Y, 60, 110);
            Assert.True(content.Bounds.Height >= height - 150, $"Content is only {content.Bounds.Height} at {width}×{height}.");
            var nav = Named("BrowseNavigationBar");
            Assert.InRange(nav.Bounds.Height, 32, 48);
            double? rowY = null;
            double previousRight = 0;
            foreach (var id in new[] { "BrowseBackButton", "BrowseForwardButton", "BrowseReloadButton",
                "BrowseAddressBox", "BrowseNavigateButton", "BrowseBookmarkButton", "BrowseMoreButton" })
            {
                var control = Named(id); var origin = control.TranslatePoint(default, window)!.Value;
                rowY ??= origin.Y; Assert.InRange(Math.Abs(origin.Y - rowY.Value), 0, 1);
                Assert.True(origin.X >= previousRight - .5, $"{id} overlaps its neighbour.");
                Assert.True(origin.X + control.Bounds.Width <= width + .5, $"{id} falls outside width {width}.");
                previousRight = origin.X + control.Bounds.Width;
            }
            await Pointer(Find(window, "BrowseMoreButton"));
            await Click(window, workspace, Find(window, "BrowseChromiumTabButton")); Layout();
            Assert.True(workspace.TrySetValue("Address", "https://layout.example/"));
            await Click(window, workspace, Find(window, "BrowseNavigateButton")); Layout();
            var engine = Assert.Single(factory.Hosts);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), item => item.Name == "BrowseReloadButton" && item.IsEffectivelyVisible);
            var stop = Find(window, "BrowseStopButton");
            Assert.True(stop.IsEffectivelyVisible);
            var stopOrigin = stop.TranslatePoint(default, window)!.Value;
            Assert.InRange(stopOrigin.X, 0, width - stop.Bounds.Width);
            await Pointer(stop); Layout();
            Assert.Equal(1, engine.StopCalls);
            Assert.True(Named("BrowseReloadButton").IsEffectivelyVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), item => item.Name == "BrowseStopButton" && item.IsEffectivelyVisible);
            var parent = engine.OriginalNativeView.Parent;
            Assert.NotNull(parent); var tabId = chrome.State.SelectedTabId;
            var panel = Named("BrowseToolsPanel");
            var scroll = Assert.IsType<ScrollViewer>(Assert.IsType<CuiContainerControl>(panel).Child);
            var panelOrigin = panel.TranslatePoint(default, window)!.Value;
            Assert.True(panelOrigin.Y + panel.Bounds.Height <= contentOrigin.Y + content.Bounds.Height + .5,
                $"Tools bottom {panelOrigin.Y + panel.Bounds.Height} exceeds content bottom {contentOrigin.Y + content.Bounds.Height}.");
            var evidenceFolder = Environment.GetEnvironmentVariable("BROWSE_EVIDENCE_DIRECTORY");
            if (!string.IsNullOrEmpty(evidenceFolder))
            {
                Directory.CreateDirectory(evidenceFolder);
                using var evidenceFrame = window.CaptureRenderedFrame();
                evidenceFrame?.Save(Path.Combine(evidenceFolder, $"tools-{width}x{height}.png"));
            }
            var engineControl = Named("BrowseEngineView");
            var engineOrigin = engineControl.TranslatePoint(default, window)!.Value;
            Assert.True(engineOrigin.X + engineControl.Bounds.Width <= panelOrigin.X + .5,
                "The tools drawer overlaps the original native engine surface.");
            if (height <= 520) Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            if (scroll.Extent.Height > scroll.Viewport.Height)
            {
                scroll.Offset = new Vector(0, scroll.Extent.Height); Layout();
                Assert.True(scroll.Offset.Y > 0);
            }
            var reset = Find(window, "BrowseResetSiteEngineButton");
            reset.BringIntoView(); Layout();
            var point = reset.TranslatePoint(new Point(reset.Bounds.Width / 2, reset.Bounds.Height / 2), window)!.Value;
            Assert.InRange(point.Y, panelOrigin.Y, panelOrigin.Y + panel.Bounds.Height);
            var selected = chrome.State.SelectedTabId;
            await Pointer(reset); Assert.Equal(selected, chrome.State.SelectedTabId);
            // More remains in the fixed navigation row while the drawer is scrolled.
            // Closing from there must restore the full viewport without retiring its view.
            await Pointer(Find(window, "BrowseMoreButton")); Layout();
            Assert.False((bool)Value("ToolsOpen")!);
            Assert.InRange(Math.Abs(engineControl.Bounds.Width - content.Bounds.Width), 0, .5);
            Assert.Equal(tabId, chrome.State.SelectedTabId);
            Assert.Same(engine, chrome.ObserveOriginalEngine(tabId));
            Assert.Same(parent, engine.OriginalNativeView.Parent);
            Assert.True(engine.OriginalNativeView.IsEffectivelyVisible);
            await Pointer(Find(window, "BrowseMoreButton"));
            await Click(window, workspace, Find(window, "BrowseOpenHistoryButton")); Layout();
            Assert.Equal(BrowserNativePage.History, chrome.State.SelectedTab.NativePage);
            Assert.False((bool)Value("ToolsOpen")!);
            await Pointer(Find(window, "BrowseMoreButton")); Layout();
            Assert.True(Named("BrowseToolsPanel").IsEffectivelyVisible);
            await Click(window, workspace, Find(window, "BrowseCloseToolsButton"));
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }

        object? Value(string key) { Assert.True(workspace.TryGetValue(key, out var value)); return value; }
        Control Named(string name) => window.GetVisualDescendants().OfType<Control>().Single(item => item.Name == name);
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        async Task Pointer(Button button)
        {
            button.BringIntoView(); Layout();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            var before = workspace.OriginalCommand;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            await Observe(Loader(window).WhenActionsIdleAsync(), window);
            Assert.NotNull(workspace.OriginalCommand); Assert.NotSame(before, workspace.OriginalCommand);
            await Observe(workspace.OriginalCommand!, window);
            await Task.WhenAll(workspace.OriginalNotifications.ToArray()); Layout();
        }
    }

    /// <summary>The owner-latest topbar relocation must mount the three genuine shared controls
    /// (model, notifications, profile) inside the persistent navigation bar immediately right of
    /// the address entry when an embedded Haven host context is present. The removed vertical rail
    /// and its vacant layout column must be gone, and the browser engine surface must keep its
    /// proper host under the single remapped content column.</summary>
    [AvaloniaTheory]
    [InlineData(1440)]
    [InlineData(1280)]
    [InlineData(390)]
    [InlineData(360)]
    public async Task Embedded_context_mounts_shared_cluster_in_navigation_bar_right_of_address(int width)
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var gateway = new SharedControlsFakeGateway();
        var context = new SharedControlsFakeContext(gateway);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, sharedContext: context);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        window.Width = width;
        RegisterProductionImageResolver(window);
        var validationRegistry = BrowseNativeSurface.CreateScene(workspace, new FixtureReadiness()).ControlRegistry!;
        RegisterProductionImageResolver(validationRegistry);
        try
        {
            using (var validation = new CuiControlLoader(validationRegistry))
            {
                validation.SetBindingContext(workspace); validation.SetActionDispatcher(workspace);
                var loaded = validation.TryLoad(BrowseNativeWorkspace.LoadDocument());
                Assert.DoesNotContain(loaded.Diagnostics, item => item.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            }
            window.Show(); await Observe(window.InitializeAsync(), window); Layout();

            Assert.True((bool)Value("Haven.Embedded")!);
            Assert.True((bool)Value("Haven.ModelAvailable")!);
            Assert.True((bool)Value("Haven.NotificationsAvailable")!);
            Assert.True((bool)Value("Haven.ProfileAvailable")!);

            var nav = Named("BrowseNavigationBar");
            var address = Named("BrowseAddressBox");
            var addressOrigin = address.TranslatePoint(default, window)!.Value;
            Assert.True(address.IsEffectivelyVisible);
            Assert.True(address.Bounds.Width >= 64 - .5,
                $"Address entry is only {address.Bounds.Width}px at embedded width {width}.");
            Assert.True(address.Bounds.Height > 0);
            Assert.InRange(addressOrigin.X, -.5, window.ClientSize.Width - address.Bounds.Width + .5);
            Assert.InRange(addressOrigin.Y, -.5, window.ClientSize.Height - address.Bounds.Height + .5);
            var addressRight = address.TranslatePoint(new Point(address.Bounds.Width, 0), window)!.Value.X;
            double previousRight = 0;
            foreach (var id in new[] { "HavenModelButton", "HavenNotificationsButton", "HavenProfileButton" })
            {
                var control = Named(id);
                Assert.True(control.IsEffectivelyVisible, $"{id} must be visible in embedded context.");
                Assert.True(control.IsEnabled, $"{id} must be enabled in embedded context.");
                // The control must be a descendant of the persistent navigation bar.
                Assert.Contains(nav, Ancestors(control));
                var origin = control.TranslatePoint(default, window)!.Value;
                Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0,
                    $"{id} must have usable bounds at embedded width {width}.");
                Assert.InRange(origin.X, -.5, window.ClientSize.Width - control.Bounds.Width + .5);
                Assert.InRange(origin.Y, -.5, window.ClientSize.Height - control.Bounds.Height + .5);
                Assert.InRange(Math.Abs(origin.Y - addressOrigin.Y), 0, 1);
                Assert.InRange(Math.Abs(origin.Y + control.Bounds.Height - addressOrigin.Y - address.Bounds.Height), 0, 1);
                Assert.True(origin.X >= addressRight - .5, $"{id} is not right of the address field.");
                // Ordering: model, notifications, profile left-to-right, no overlap.
                Assert.True(origin.X >= previousRight - .5, $"{id} overlaps or precedes its neighbour.");
                previousRight = origin.X + control.Bounds.Width;
            }

            // No remaining vertical rail or vacant BrowseLayout column: the navigation bar and the
            // content viewport must share the same left edge under the single remapped column.
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), item => item.Name == "HavenSharedRail");
            var content = Named("BrowseContentViewport");
            var navOrigin = nav.TranslatePoint(default, window)!.Value;
            var contentOrigin = content.TranslatePoint(default, window)!.Value;
            Assert.InRange(Math.Abs(contentOrigin.X - navOrigin.X), 0, .5);

            // Establish a native tab so the host is exercised, then assert the browser content
            // retains that proper host under the remapped content column.
            await Click(window, workspace, Find(window, "BrowseMoreButton"));
            await Click(window, workspace, Find(window, "BrowseChromiumTabButton")); Layout();
            Assert.True(workspace.TrySetValue("Address", "https://layout.example/"));
            await Click(window, workspace, Find(window, "BrowseNavigateButton")); Layout();
            var engine = Assert.Single(factory.Hosts);
            Assert.Same(engine, chrome.ObserveOriginalEngine(chrome.State.SelectedTabId));
            Assert.NotNull(engine.OriginalNativeView.Parent);
            Assert.True(engine.OriginalNativeView.IsEffectivelyVisible);
            Assert.Contains(content, Ancestors(engine.OriginalNativeView));
            await Retire(window);

            IEnumerable<Control> Ancestors(Control control)
            {
                for (var parent = control.Parent; parent is not null; parent = parent.Parent)
                    if (parent is Control controlParent) yield return controlParent;
            }
            object? Value(string key) { Assert.True(workspace.TryGetValue(key, out var value)); return value; }
            Control Named(string name) => window.GetVisualDescendants().OfType<Control>().Single(item => item.Name == name);
            void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    /// <summary>Mount the SAME production packaged-SVG resolver on a mounted Browse window's
    /// retained native scene before Show/InitializeAsync, so authored avares://Haven SVG icons
    /// resolve exactly as in the real host.</summary>
    private static void RegisterProductionImageResolver(BrowseNativeWindow window)
    {
        var scene = typeof(BrowseNativeWindow)
            .GetField("_originalScene", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window) as CuiNativeScene;
        Assert.NotNull(scene);
        scene!.ControlRegistry!.RegisterImageSourceResolver(HavenPackagedSvgResolver.Resolve);
    }

    /// <summary>Mount the SAME production packaged-SVG resolver on a validation scene registry
    /// before TryLoad.</summary>
    private static void RegisterProductionImageResolver(CuiControlRegistry registry)
        => registry.RegisterImageSourceResolver(HavenPackagedSvgResolver.Resolve);
}
