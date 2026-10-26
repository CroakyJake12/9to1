using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    [AvaloniaFact]
    public async Task Collapsed_group_keeps_its_selected_tab_and_actions_current_until_expanded()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var groupId = Guid.NewGuid();
        var stamp = DateTimeOffset.UtcNow;
        var projection = new BrowseTabSessionProjection([
            new(first, "First", "https://first.example/", BrowserTabPrivacy.Standard, groupId.ToString("D"), stamp) { ProfileId = "profile" },
            new(second, "Second", "https://second.example/", BrowserTabPrivacy.Standard, groupId.ToString("D"), stamp) { ProfileId = "profile" }
        ], second) { Groups = [new(groupId, "Research", true, [first, second])] };
        var persistence = new MemoryTabSessionPersistence(projection);
        var chrome = await BrowseChrome.CreateAsync(new Paths(), homeProfileId: "profile", tabSessionPersistence: persistence);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Layout();
            Assert.Equal(new[] { second }, Rows(workspace).Select(row => Guid.Parse(row.Key)));
            Assert.Equal("Research", Assert.Single(chrome.State.Groups).Name);
            await Click(window, workspace, Find(window, "BrowseMoreButton")); Layout();
            await Click(window, workspace, Find(window, "BrowseToggleTabGroupCollapsedButton")); Layout();
            Assert.Equal(new[] { first, second }, Rows(workspace).Select(row => Guid.Parse(row.Key)));
            var firstRow = Assert.Single(Rows(workspace), row => row.Key == first.ToString("D"));
            await Click(window, workspace, TabButton(window, workspace, firstRow.Key)); Layout();
            await Click(window, workspace, Find(window, "BrowseToggleTabGroupCollapsedButton")); Layout();
            Assert.Equal(new[] { first }, Rows(workspace).Select(row => Guid.Parse(row.Key)));
            Assert.Equal(first, chrome.State.SelectedTabId);
            Assert.True(workspace.IsActionAvailable("9to1.Browse.CloseTab") == true);
            await Click(window, workspace, Find(window, "BrowseCloseTabButton")); Layout();
            Assert.Equal(new[] { second }, chrome.State.Tabs.Select(tab => tab.Id));
            Assert.Equal(second, chrome.State.SelectedTabId);
            Assert.Equal(groupId, Assert.Single(chrome.State.Groups).Id);
            await Retire(window);

            void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaTheory]
    [InlineData(.07, true)]
    [InlineData(.93, false)]
    public async Task Dragging_to_either_outer_edge_swaps_the_exact_rows_with_a_narrow_marker(double relativeX, bool before)
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Layout();
            var home = chrome.State.SelectedTabId;
            var second = (await chrome.NewTabAsync(false)).SelectedTabId; Layout();
            var third = (await chrome.NewTabAsync(false)).SelectedTabId; Layout();
            var source = TabButton(window, workspace, second.ToString("D"));
            var target = TabButton(window, workspace, third.ToString("D"));
            var targetRowVisual = target.GetVisualAncestors().OfType<CuiContainerControl>().First();
            Assert.True(workspace.TryGetCurrentTabRow(second.ToString("D"), out var sourceRow));
            Assert.True(workspace.TryGetCurrentTabRow(third.ToString("D"), out var targetRow));
            Control InTargetRow(string name) => targetRowVisual.GetVisualDescendants().OfType<Control>().Single(control => control.Name == name);
            workspace.SetTabDropIndicator(targetRow, BrowseNativeWorkspace.TabDropZone.SplitBefore); Layout();
            Assert.True(InTargetRow("BrowseTabDropSplitBeforeIndicator").IsVisible);
            Assert.InRange(Math.Abs(InTargetRow("BrowseTabDropSplitBeforeHalf").Bounds.Width * 2 - targetRowVisual.Bounds.Width), 0, 1);
            Assert.False(InTargetRow("BrowseTabDropSplitAfterIndicator").IsVisible);
            workspace.SetTabDropIndicator(targetRow, BrowseNativeWorkspace.TabDropZone.Group); Layout();
            var groupGlow = Assert.IsAssignableFrom<CuiContainerControl>(InTargetRow("BrowseTabDropGroupIndicator"));
            Assert.True(groupGlow.IsVisible);
            Assert.InRange(Math.Abs(groupGlow.Bounds.Width - targetRowVisual.Bounds.Width), 0, 1);
            Assert.IsAssignableFrom<IGradientBrush>(groupGlow.Background);
            workspace.SetTabDropIndicator(null, BrowseNativeWorkspace.TabDropZone.None); Layout();
            var sourcePoint = source.TranslatePoint(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2), window)!.Value;
            var targetOrigin = targetRowVisual.TranslatePoint(default, window)!.Value;
            var dropPoint = targetOrigin + new Vector(targetRowVisual.Bounds.Width * relativeX, targetRowVisual.Bounds.Height / 2);
            window.MouseMove(sourcePoint); window.MouseDown(sourcePoint, MouseButton.Left);
            window.MouseMove(dropPoint); Layout();
            var preview = Assert.Single(window.GetVisualDescendants().OfType<Popup>().Where(popup => popup.IsOpen));
            Assert.Equal(PlacementMode.Bottom, preview.Placement);
            Assert.False(preview.IsHitTestVisible);
            Assert.IsType<Border>(preview.Child);
            var anchor = window.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "BrowseTabDragAnchor");
            var anchorPoint = anchor.TranslatePoint(default, window)!.Value;
            Assert.InRange(Math.Abs(anchorPoint.X - dropPoint.X), 0, .5);
            Assert.InRange(Math.Abs(anchorPoint.Y - dropPoint.Y), 0, .5);
            Assert.True(before ? targetRow!.DropSwapBefore : targetRow!.DropSwapAfter);
            Assert.False(targetRow!.DropGroup);
            var marker = InTargetRow(before ? "BrowseTabDropSwapBeforeIndicator" : "BrowseTabDropSwapAfterIndicator");
            Assert.True(marker.IsVisible);
            Assert.InRange(marker.Bounds.Width, 3, 5);
            var markerOrigin = marker.TranslatePoint(default, targetRowVisual)!.Value;
            Assert.InRange(Math.Abs(markerOrigin.X - (before ? 0 : targetRowVisual.Bounds.Width - marker.Bounds.Width)), 0, 1);
            Assert.Same(sourceRow, Assert.Single(Rows(workspace), row => row.Key == second.ToString("D")));
            Assert.Equal(new[] { home, second, third }, chrome.State.Tabs.Select(tab => tab.Id));
            window.MouseUp(dropPoint, MouseButton.Left);
            if (workspace.OriginalCommand is { } original) await original;
            Layout();
            Assert.Equal(new[] { home, third, second }, chrome.State.Tabs.Select(tab => tab.Id));
            Assert.Equal(third, chrome.State.SelectedTabId);
            Assert.Equal(2, factory.Hosts.Count);
            Assert.False(targetRow.DropSwapBefore || targetRow.DropSwapAfter, "Retiring the drag must clear its edge marker.");
            await Retire(window);

            void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaTheory]
    [InlineData(.25, true)]
    [InlineData(.75, false)]
    public async Task Dragging_to_an_inner_edge_splits_the_exact_pair_on_the_receiving_half(double relativeX, bool sourceReceivesFirst)
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Layout();
            var sourceId = (await chrome.NewTabAsync(false)).SelectedTabId; Layout();
            var targetId = (await chrome.NewTabAsync(false)).SelectedTabId; Layout();
            var source = TabButton(window, workspace, sourceId.ToString("D"));
            var target = TabButton(window, workspace, targetId.ToString("D"));
            var targetVisual = target.GetVisualAncestors().OfType<CuiContainerControl>().First();
            Assert.True(workspace.TryGetCurrentTabRow(targetId.ToString("D"), out var targetRow));
            var sourcePoint = source.TranslatePoint(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2), window)!.Value;
            var targetOrigin = targetVisual.TranslatePoint(default, window)!.Value;
            var dropPoint = targetOrigin + new Vector(targetVisual.Bounds.Width * relativeX, targetVisual.Bounds.Height / 2);
            var firstPreviewPoint = targetOrigin + new Vector(targetVisual.Bounds.Width * (sourceReceivesFirst ? .22 : .78), targetVisual.Bounds.Height / 2);

            window.MouseMove(sourcePoint); window.MouseDown(sourcePoint, MouseButton.Left);
            window.MouseMove(firstPreviewPoint); Layout();
            var preview = Assert.Single(window.GetVisualDescendants().OfType<Popup>().Where(popup => popup.IsOpen));
            var anchor = window.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "BrowseTabDragAnchor");
            var anchorPoint = anchor.TranslatePoint(default, window)!.Value;
            Assert.InRange(Math.Abs(anchorPoint.X - firstPreviewPoint.X), 0, .5);
            Assert.InRange(Math.Abs(anchorPoint.Y - firstPreviewPoint.Y), 0, .5);
            window.MouseMove(dropPoint); Layout();
            anchorPoint = anchor.TranslatePoint(default, window)!.Value;
            Assert.InRange(Math.Abs(anchorPoint.X - dropPoint.X), 0, .5);
            Assert.InRange(Math.Abs(anchorPoint.Y - dropPoint.Y), 0, .5);
            Assert.True(sourceReceivesFirst ? targetRow!.DropSplitBefore : targetRow!.DropSplitAfter);
            var targetControls = targetVisual.GetVisualDescendants().OfType<Control>();
            var half = targetControls.Single(control => control.Name == (sourceReceivesFirst ? "BrowseTabDropSplitBeforeHalf" : "BrowseTabDropSplitAfterHalf"));
            Assert.InRange(Math.Abs(half.Bounds.Width * 2 - targetVisual.Bounds.Width), 0, 1);
            Assert.NotNull(preview.Child);
            window.MouseUp(dropPoint, MouseButton.Left);
            if (workspace.OriginalCommand is { } original) await Observe(original, window);
            Layout();
            Assert.Equal(sourceReceivesFirst ? new[] { sourceId, targetId } : new[] { targetId, sourceId }, chrome.State.PaneTabIds);
            Assert.Equal(Haven.Core.SplitOrientation.Vertical, chrome.State.SplitOrientation);
            Assert.Equal(targetId, chrome.State.SelectedTabId);
            Assert.Equal(2, factory.Hosts.Count);
            await chrome.CloseSplitViewAsync();
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }

        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }

    [AvaloniaFact]
    public async Task Dragging_to_the_center_groups_the_exact_rows_and_lights_the_whole_row()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        var state = new BrowseTabSessionProjection([
            new(firstId, "First", "https://first.example/", BrowserTabPrivacy.Standard, string.Empty, DateTimeOffset.UtcNow) { ProfileId = "profile-a" },
            new(secondId, "Second", "https://second.example/", BrowserTabPrivacy.Standard, string.Empty, DateTimeOffset.UtcNow) { ProfileId = "profile-a" }
        ], secondId);
        var session = new NativeDragSession(state);
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory, homeProfileId: "profile-a", tabSessionPersistence: session);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Layout();
            var source = TabButton(window, workspace, firstId.ToString("D"));
            var target = TabButton(window, workspace, secondId.ToString("D"));
            var targetVisual = target.GetVisualAncestors().OfType<CuiContainerControl>().First();
            Assert.True(workspace.TryGetCurrentTabRow(secondId.ToString("D"), out var targetRow));
            var sourcePoint = source.TranslatePoint(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2), window)!.Value;
            var targetOrigin = targetVisual.TranslatePoint(default, window)!.Value;
            var dropPoint = targetOrigin + new Vector(targetVisual.Bounds.Width * .5, targetVisual.Bounds.Height / 2);

            window.MouseMove(sourcePoint); window.MouseDown(sourcePoint, MouseButton.Left);
            window.MouseMove(dropPoint); Layout();
            Assert.True(targetRow!.DropGroup);
            var groupGlow = Assert.IsAssignableFrom<CuiContainerControl>(targetVisual.GetVisualDescendants()
                .OfType<Control>().Single(control => control.Name == "BrowseTabDropGroupIndicator"));
            Assert.True(groupGlow.IsVisible);
            Assert.InRange(Math.Abs(groupGlow.Bounds.Width - targetVisual.Bounds.Width), 0, 1);
            Assert.IsAssignableFrom<IGradientBrush>(groupGlow.Background);
            Assert.True(window.GetVisualDescendants().OfType<Popup>().Any(popup => popup.IsOpen));
            window.MouseUp(dropPoint, MouseButton.Left);
            if (workspace.OriginalCommand is { } original) await Observe(original, window);
            Layout();
            var group = Assert.Single(chrome.State.Groups);
            Assert.Equal(new[] { firstId, secondId }, group.OrderedTabIds);
            Assert.Equal(secondId, chrome.State.SelectedTabId);
            Assert.Equal(new[] { firstId, secondId }, session.Saves[^1].Groups!.Single().OrderedTabIds);
            Assert.Equal(2, factory.Hosts.Count);
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }

        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }

    [AvaloniaFact]
    public async Task Escape_cancels_an_active_split_drag_and_retires_its_native_preview()
    {
        var factory = new FixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), factory);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Layout();
            var sourceId = (await chrome.NewTabAsync(false)).SelectedTabId; Layout();
            var targetId = (await chrome.NewTabAsync(false)).SelectedTabId; Layout();
            var source = TabButton(window, workspace, sourceId.ToString("D"));
            var target = TabButton(window, workspace, targetId.ToString("D"));
            var targetVisual = target.GetVisualAncestors().OfType<CuiContainerControl>().First();
            Assert.True(workspace.TryGetCurrentTabRow(targetId.ToString("D"), out var targetRow));
            var sourcePoint = source.TranslatePoint(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2), window)!.Value;
            var dropPoint = targetVisual.TranslatePoint(default, window)!.Value + new Vector(targetVisual.Bounds.Width * .25, targetVisual.Bounds.Height / 2);
            var before = chrome.State.Tabs.Select(tab => tab.Id).ToArray();

            source.Focus(); window.MouseMove(sourcePoint); window.MouseDown(sourcePoint, MouseButton.Left);
            window.MouseMove(dropPoint); Layout();
            Assert.True(targetRow!.DropSplitBefore);
            Assert.True(window.GetVisualDescendants().OfType<Popup>().Any(popup => popup.IsOpen));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Layout();
            Assert.False(targetRow!.DropSplitBefore);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Popup>(), popup => popup.IsOpen);
            Assert.Equal(before, chrome.State.Tabs.Select(tab => tab.Id));
            Assert.Null(workspace.OriginalCommand);
            window.MouseUp(dropPoint, MouseButton.Left); Layout();
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }

        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }

    [AvaloniaTheory]
    [InlineData("close-source")]
    [InlineData("close-target")]
    [InlineData("navigate-source")]
    [InlineData("navigate-target")]
    [InlineData("dispose")]
    public async Task Changing_either_drag_owner_retires_the_preview_and_indicator(string change)
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths(), new FixtureFactory());
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Layout();
            var sourceId = (await chrome.NewTabAsync(false)).SelectedTabId; Layout();
            var targetId = (await chrome.NewTabAsync(false)).SelectedTabId; Layout();
            // Select the navigating owner before starting so navigation itself is
            // what invalidates the gesture, rather than a selected-tab change.
            if (change == "navigate-source") { await chrome.SelectTabAsync(sourceId); Layout(); }
            var source = TabButton(window, workspace, sourceId.ToString("D"));
            var target = TabButton(window, workspace, targetId.ToString("D"));
            var targetVisual = target.GetVisualAncestors().OfType<CuiContainerControl>().First();
            Assert.True(workspace.TryGetCurrentTabRow(targetId.ToString("D"), out var targetRow));
            var sourcePoint = source.TranslatePoint(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2), window)!.Value;
            var dropPoint = targetVisual.TranslatePoint(default, window)!.Value + new Vector(targetVisual.Bounds.Width * .25, targetVisual.Bounds.Height / 2);
            window.MouseMove(sourcePoint); window.MouseDown(sourcePoint, MouseButton.Left);
            window.MouseMove(dropPoint); Layout();
            Assert.True(targetRow!.DropSplitBefore);
            Assert.Contains(window.GetVisualDescendants().OfType<Popup>(), popup => popup.IsOpen);

            if (change == "dispose") await Observe(workspace.DisposeAsync().AsTask(), window);
            else if (change.StartsWith("close-", StringComparison.Ordinal))
                await chrome.CloseTabAsync(change == "close-source" ? sourceId : targetId);
            else await chrome.NavigateAsync("https://changed.example/next");
            Layout();
            Assert.False(targetRow.DropSplitBefore);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Popup>(), popup => popup.IsOpen);
            window.MouseUp(dropPoint, MouseButton.Left); Layout();
            Assert.Empty(workspace.State.PaneTabIds);
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }

        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }

    private sealed class NativeDragSession(BrowseTabSessionProjection projection) : IBrowseTabSessionPersistence
    {
        public List<BrowseTabSessionProjection> Saves { get; } = [];
        public Task<BrowseTabSessionProjection?> LoadAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<BrowseTabSessionProjection?>(projection); }
        public Task SaveAsync(BrowseTabSessionProjection updated, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Saves.Add(updated); return Task.CompletedTask; }
    }

    [AvaloniaFact]
    public async Task Split_view_buttons_render_both_original_engine_views_and_focus_without_recreation()
    {
        var factory = new FixtureFactory(); var paths = new Paths();
        await new BrowseEnginePreferencesStore(paths).SaveAsync(new(BrowseEngineKind.Chromium,
            new Dictionary<string, BrowseEngineKind>()));
        var chrome = await BrowseChrome.CreateAsync(paths, factory, homeProfileId: "current-profile");
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Layout();
            await Click(window, workspace, Find(window, "BrowseNewTabButton")); Layout();
            var tabs = chrome.State.Tabs.ToArray(); Assert.Equal(2, tabs.Length);
            await Click(window, workspace, Find(window, "BrowseMoreButton")); Layout();
            var split = Find(window, "BrowseToggleSplitViewButton");
            Assert.True(split.IsEnabled);
            await Click(window, workspace, split); Layout();
            Assert.Equal(new[] { tabs[1].Id, tabs[0].Id }, chrome.State.PaneTabIds);
            var views = factory.Hosts.Select(host => host.OriginalNativeView).ToArray();
            Assert.Equal(2, views.Length);
            Assert.All(views, view => Assert.True(view.IsEffectivelyVisible));
            var parents = views.Select(view => Assert.IsType<Grid>(view.Parent)).Distinct().ToArray();
            Assert.Single(parents);
            Assert.Equal(2, Grid.GetColumn(views[0])); Assert.Equal(0, Grid.GetColumn(views[1]));
            await Click(window, workspace, Find(window, "BrowseToggleSplitOrientationButton")); Layout();
            Assert.Equal(Haven.Core.SplitOrientation.Horizontal, chrome.State.SplitOrientation);
            Assert.Equal(0, Grid.GetColumn(views[0])); Assert.Equal(0, Grid.GetColumn(views[1]));
            Assert.Equal(2, Grid.GetRow(views[0])); Assert.Equal(0, Grid.GetRow(views[1]));
            await Click(window, workspace, Find(window, "BrowseWidenPrimaryPaneButton")); Layout();
            Assert.Equal(0.55, chrome.State.PrimaryPaneRatio, precision: 2);
            var before = factory.Hosts.Count;
            await Click(window, workspace, Find(window, "BrowseFocusSplitSecondaryButton")); Layout();
            Assert.Equal(tabs[0].Id, chrome.State.SelectedTabId);
            Assert.Equal(before, factory.Hosts.Count);
            await Retire(window);

            void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

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
            // Materialize the opened tools before Click decides whether a detached control needs
            // the panel opened; otherwise its attachment precheck can toggle the real panel shut.
            Layout();
            Assert.Equal(true, Value("ToolsOpen"));
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
