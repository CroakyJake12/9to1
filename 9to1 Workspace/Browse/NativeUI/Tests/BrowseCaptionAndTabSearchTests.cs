using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    [AvaloniaFact]
    public async Task Tab_search_finds_collapsed_tabs_preserves_query_and_activates_only_current_identity()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths(), tabSessionPersistence: new MemoryTabSessionPersistence());
        // Built-in pages open their own canonical tabs; retain the actual returned IDs.
        // Keep the original blank tab as the sole ungrouped, selected tab.
        var selected = chrome.State.SelectedTabId;
        var history = (await chrome.OpenNativePageAsync(BrowserNativePage.History)).SelectedTabId;
        var bookmarks = (await chrome.OpenNativePageAsync(BrowserNativePage.Bookmarks)).SelectedTabId;
        await chrome.SelectTabAsync(selected);
        await chrome.GroupTabsAsync(history, bookmarks, "Reference");
        var group = Assert.Single(chrome.State.Groups).Id;
        await chrome.SetTabGroupCollapsedAsync(group, true);
        Assert.Equal(3, chrome.State.Tabs.Count);
        Assert.True(Assert.Single(chrome.State.Tabs, tab => tab.Id == history).IsGroupCollapsed);
        Assert.True(Assert.Single(chrome.State.Tabs, tab => tab.Id == bookmarks).IsGroupCollapsed);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Settle(window);
            Assert.Single(Rows(workspace));
            await SearchClick(window, Find(window, "BrowseTabSearchButton"));
            var query = Assert.IsType<TextBox>(Authored(window, "BrowseTabSearchQuery"));
            query.Text = "History"; Settle(window);
            var issued = Assert.Single(SearchRows(workspace));
            Assert.Equal(history.ToString("D"), issued.Key);
            Assert.Contains("Collapsed group", issued.SearchDetail);
            await Observe(chrome.SetTabGroupCollapsedAsync(group, false), window);
            await Observe(Task.WhenAll(workspace.OriginalNotifications), window); Settle(window);
            Assert.Equal("History", query.Text);
            Assert.True(workspace.TryGetValue("TabSearchQuery", out var retained)); Assert.Equal("History", retained);
            var before = workspace.OriginalCommand;
            await workspace.DispatchAsync("9to1.Browse.SelectTab", issued.Target);
            Assert.Same(before, workspace.OriginalCommand);
            Assert.Equal(selected, chrome.State.SelectedTabId);
            Assert.False(workspace.TryGetItemValue(issued, "Target", out _));
            await SearchClick(window, SearchResult(window, history, "BrowseTabSearchResult"));
            Assert.Equal(history, chrome.State.SelectedTabId);
            Assert.Equal(3, chrome.State.Tabs.Count);
            Assert.False(IsTrue(workspace, "TabSearchOpen"));
        }
        finally { await Retire(window); }
    }

    [AvaloniaFact]
    public async Task Tab_search_background_close_keeps_selection_address_draft_and_search_draft()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths(), tabSessionPersistence: new MemoryTabSessionPersistence());
        var current = chrome.State.SelectedTabId;
        var background = (await chrome.OpenNativePageAsync(BrowserNativePage.History)).SelectedTabId;
        await chrome.SelectTabAsync(current);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Settle(window);
            Assert.True(workspace.TrySetValue("Address", "Keep my newer address draft"));
            await SearchClick(window, Find(window, "BrowseTabSearchButton"));
            var query = Assert.IsType<TextBox>(Authored(window, "BrowseTabSearchQuery"));
            query.Text = "History"; Settle(window);
            var result = SearchResult(window, background, "BrowseTabSearchResult");
            Assert.NotNull(result.ContextMenu); // Secondary result actions use the same issued target.
            await SearchClick(window, SearchResult(window, background, "BrowseTabSearchCloseResult"));
            Assert.Equal(current, chrome.State.SelectedTabId);
            Assert.DoesNotContain(chrome.State.Tabs, tab => tab.Id == background);
            Assert.True(workspace.TryGetValue("Address", out var draft)); Assert.Equal("Keep my newer address draft", draft);
            Assert.Equal("History", query.Text);
            Assert.True(IsTrue(workspace, "TabSearchOpen"));
            Assert.True(IsTrue(workspace, "TabSearchEmpty"));
        }
        finally { await Retire(window); }
    }

    [AvaloniaFact]
    public async Task Tab_search_uses_saved_remapped_shortcut_and_escape_restores_focus_without_navigation()
    {
        var paths = new Paths();
        var saved = new KeybindingService(paths);
        Assert.Equal("Ctrl+Shift+A", saved.GetBinding("SearchTabs"));
        saved.SetBinding("SearchTabs", "Ctrl+Shift+Y"); await saved.SaveAsync(CancellationToken.None);
        var actual = new KeybindingService(paths); await actual.LoadAsync(CancellationToken.None);
        var chrome = await BrowseChrome.CreateAsync(paths);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, null, null, null,
            tabSearchShortcut: () => actual.GetBinding("SearchTabs"));
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Settle(window);
            var address = Assert.IsType<TextBox>(Authored(window, "BrowseAddressBox"));
            address.Focus(); address.Text = "Unsubmitted address"; Settle(window);
            SendSearchKey(window, Key.A, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.False(IsTrue(workspace, "TabSearchOpen"));
            SendSearchKey(window, Key.Y, RawInputModifiers.Control | RawInputModifiers.Shift);
            await Observe(Task.WhenAll(workspace.OriginalNotifications), window); Settle(window);
            Assert.True(IsTrue(workspace, "TabSearchOpen"));
            Assert.True(workspace.TryGetValue("TabSearchTooltip", out var tooltip));
            Assert.Contains("Ctrl+Shift+Y", Assert.IsType<string>(tooltip));
            var query = Assert.IsType<TextBox>(Authored(window, "BrowseTabSearchQuery"));
            Assert.True(query.IsFocused);
            query.Text = "No matching tab or destination"; Settle(window);
            var selected = chrome.State.SelectedTabId; var count = chrome.State.Tabs.Count;
            SendSearchKey(window, Key.Enter);
            await Observe(Loader(window).WhenActionsIdleAsync(), window); Settle(window);
            Assert.Equal(selected, chrome.State.SelectedTabId); Assert.Equal(count, chrome.State.Tabs.Count);
            SendSearchKey(window, Key.Escape);
            await Observe(Task.WhenAll(workspace.OriginalNotifications), window); Settle(window);
            Assert.False(IsTrue(workspace, "TabSearchOpen")); Assert.True(address.IsFocused);
            Assert.Equal("Unsubmitted address", address.Text);
            await SearchClick(window, Find(window, "BrowseTabSearchButton"));
            Assert.Equal("No matching tab or destination", query.Text);
            await SearchClick(window, Find(window, "BrowseTabSearchNewTab"));
            Assert.Equal(count + 1, chrome.State.Tabs.Count); Assert.False(IsTrue(workspace, "TabSearchOpen"));
        }
        finally { await Retire(window); }
    }

    [AvaloniaTheory]
    [InlineData(400)]
    [InlineData(720)]
    [InlineData(1440)]
    public async Task Caption_controls_remain_visible_in_two_rows_and_do_not_cancel_an_unsettled_command(int width)
    {
        var persistence = new CaptionHeldPersistence();
        var chrome = await BrowseChrome.CreateAsync(new Paths(), tabSessionPersistence: persistence);
        var controls = new CaptionFixtureControls();
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, null,
            new SharedControlsFakeContext(null), null, controls);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        window.Width = width; window.Height = 520;
        RegisterProductionImageResolver(window);
        Task? pending = null;
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Settle(window);
            Assert.True(controls.CaptionReady);
            var navigation = Authored(window, "BrowseNavigationBar");
            var navigationTop = navigation.TranslatePoint(default, window)!.Value.Y;
            foreach (var id in new[] { "BrowseTabSearchButton", "BrowseNewTabButton", "BrowseWindowMinimizeButton", "BrowseWindowMaximizeButton", "BrowseWindowCloseButton" })
            {
                var control = FindMounted(window, id);
                var origin = control.TranslatePoint(default, window)!.Value;
                Assert.InRange(origin.X, 0, width);
                Assert.InRange(origin.X + control.Bounds.Width, 1, width + 1);
                Assert.True(origin.Y + control.Bounds.Height <= navigationTop + 1, id);
            }
            CaptureCaptionFrame(window, $"caption-{width}x520");
            await SearchClick(window, Find(window, "BrowseTabSearchButton"));
            var overlay = Authored(window, "BrowseTabSearchOverlay");
            var overlayOrigin = overlay.TranslatePoint(default, window)!.Value;
            Assert.InRange(overlayOrigin.X, 0, width);
            Assert.InRange(overlayOrigin.X + overlay.Bounds.Width, 1, width + 1);
            Assert.InRange(overlayOrigin.Y + overlay.Bounds.Height, 1, window.Bounds.Height + 1);
            CaptureCaptionFrame(window, $"caption-search-{width}x520");
            await SearchClick(window, Find(window, "BrowseTabSearchClose"));
            persistence.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = workspace.DispatchAsync("9to1.Browse.NewTab", null).AsTask();
            await Observe(persistence.Entered.Task, window); Settle(window);
            Assert.False(pending.IsCompleted);
            Assert.False(Find(window, "BrowseNewTabButton").IsEffectivelyEnabled);
            var original = workspace.OriginalCommand;
            await SearchClick(window, Find(window, "BrowseWindowMinimizeButton"));
            await SearchClick(window, Find(window, "BrowseWindowMaximizeButton"));
            await SearchClick(window, Find(window, "BrowseWindowCloseButton"));
            Assert.Equal(1, controls.Minimizes); Assert.Equal(1, controls.Maximizes); Assert.Equal(1, controls.Closes);
            Assert.Same(original, workspace.OriginalCommand); Assert.False(pending.IsCompleted);
            Assert.False(persistence.ObservedToken.IsCancellationRequested);
            Assert.True(workspace.TryGetValue("WindowMaximizeLabel", out var label)); Assert.Equal("Restore window", label);
        }
        finally
        {
            persistence.Gate?.TrySetResult();
            if (pending is not null) await Observe(pending, window);
            await Retire(window);
            Assert.False(controls.CaptionReady);
        }
    }

    [AvaloniaFact]
    public async Task Caption_drag_and_double_click_only_apply_to_the_authored_empty_title_region()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        var controls = new CaptionFixtureControls();
        var workspace = new BrowseNativeWorkspace(chrome, _ => true, null, null, null, controls);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window); Settle(window);
            using var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true);
            void Press(Control target, int clicks, bool right = false)
            {
                var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
                var properties = new PointerPointProperties(right ? RawInputModifiers.RightMouseButton : RawInputModifiers.LeftMouseButton,
                    right ? PointerUpdateKind.RightButtonPressed : PointerUpdateKind.LeftButtonPressed);
                target.RaiseEvent(new PointerPressedEventArgs(target, pointer, window, center, 0, properties, KeyModifiers.None, clicks));
                pointer.Capture(null); Settle(window);
            }
            var title = Authored(window, "BrowseCaptionDragRegion");
            Press(title, 1); Assert.Equal(1, controls.Moves);
            Press(title, 2); Assert.Equal(1, controls.Maximizes);
            Press(TabButton(window, workspace, chrome.State.SelectedTabId.ToString("D")), 2);
            Press(Find(window, "BrowseTabSearchButton"), 2);
            Press(Find(window, "BrowseWindowCloseButton"), 2);
            Assert.Equal(1, controls.Moves); Assert.Equal(1, controls.Maximizes); Assert.Equal(0, controls.Closes);
            Press(title, 1, right: true);
            Assert.True(IsTrue(workspace, "WindowMenuOpen"));
            await SearchClick(window, Find(window, "BrowseWindowMenuMaximize"));
            Assert.Equal(2, controls.Maximizes); Assert.False(IsTrue(workspace, "WindowMenuOpen"));
        }
        finally { await Retire(window); }
    }

    private static BrowseNativeWorkspace.TabRow[] SearchRows(BrowseNativeWorkspace workspace)
    { Assert.True(workspace.TryGetValue("TabSearchRows", out var rows)); return Assert.IsType<BrowseNativeWorkspace.TabRow[]>(rows); }

    private static Button SearchResult(BrowseNativeWindow window, Guid id, string name)
    {
        var key = id.ToString("D");
        return window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name &&
            CuiRuntimeIdentity.GetStableId(button)?.Contains($"|key:{key.Length}:{key}", StringComparison.Ordinal) == true);
    }

    // Presentation toggles and native caption callbacks deliberately do not invent a Browse
    // operation. Genuine navigation still goes through the loader and its retained pipeline.
    private static async Task SearchClick(BrowseNativeWindow window, Button button)
    {
        Settle(window); AssertMounted(window, button); Assert.True(button.IsEffectivelyEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Observe(Loader(window).WhenActionsIdleAsync(), window); Settle(window);
    }

    private static void SendSearchKey(BrowseNativeWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null); Settle(window);
    }

    private static void CaptureCaptionFrame(BrowseNativeWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("BROWSE_EVIDENCE_DIRECTORY") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

    private sealed class CaptionFixtureControls : IBrowseNativeWindowControls
    {
        public bool IsAvailable => true;
        public bool CanMinimize => true;
        public bool CanMaximize => true;
        public bool IsMaximized { get; private set; }
        public bool CaptionReady { get; private set; }
        public int Minimizes, Maximizes, Closes, Moves;
        public event EventHandler? StateChanged;
        public void Minimize() => Minimizes++;
        public void ToggleMaximize() { Maximizes++; IsMaximized = !IsMaximized; StateChanged?.Invoke(this, EventArgs.Empty); }
        public void RequestClose() => Closes++;
        public void BeginMoveDrag(PointerPressedEventArgs args) => Moves++;
        public void SetCaptionReady(bool ready) => CaptionReady = ready;
    }

    private sealed class CaptionHeldPersistence : IBrowseTabSessionPersistence
    {
        public TaskCompletionSource? Gate;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ObservedToken;
        public Task<BrowseTabSessionProjection?> LoadAsync(CancellationToken token) => Task.FromResult<BrowseTabSessionProjection?>(null);
        public Task SaveAsync(BrowseTabSessionProjection projection, CancellationToken token)
        {
            if (Gate is null) return Task.CompletedTask;
            ObservedToken = token; Entered.TrySetResult(); return Gate.Task;
        }
    }
}
