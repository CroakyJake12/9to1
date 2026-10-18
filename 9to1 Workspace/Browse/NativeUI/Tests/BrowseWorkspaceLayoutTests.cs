using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Browser;
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
        using (var validation = new CuiControlLoader(BrowseNativeSurface.CreateScene(workspace, new FixtureReadiness()).ControlRegistry!))
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
}
