using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Browser;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    [AvaloniaFact]
    public async Task Inline_close_uses_its_own_live_tab_and_preserves_the_selected_address_draft()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            var first = chrome.State.SelectedTabId;
            var stale = Rows(workspace).Single();
            await Click(window, workspace, Find(window, "BrowseNewTabButton"));
            var selected = chrome.State.SelectedTabId;
            var evidenceBefore = Environment.GetEnvironmentVariable("BROWSE_EVIDENCE_DIRECTORY");
            if (!string.IsNullOrEmpty(evidenceBefore))
            {
                Directory.CreateDirectory(evidenceBefore); window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(evidenceBefore, "two-tab-chrome.png"));
            }
            Assert.True(workspace.TrySetValue("Address", "https://unsent.example/draft"));
            var previous = workspace.OriginalCommand;
            await workspace.DispatchAsync("9to1.Browse.CloseTabRow", stale.Target);
            Assert.Same(previous, workspace.OriginalCommand);
            Assert.Equal(2, chrome.State.Tabs.Count);

            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var close = window.GetLogicalDescendants().OfType<Button>()
                .Where(item => item.Name == "BrowseCloseTabRowButton").First();
            Assert.True(close.IsEffectivelyVisible);
            close.BringIntoView(); window.UpdateLayout();
            var point = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            await Observe(Loader(window).WhenActionsIdleAsync(), window);
            Assert.NotSame(previous, workspace.OriginalCommand);
            await Observe(workspace.OriginalCommand!, window);
            await Task.WhenAll(workspace.OriginalNotifications.ToArray());
            Assert.DoesNotContain(chrome.State.Tabs, tab => tab.Id == first);
            Assert.Equal(selected, chrome.State.SelectedTabId);
            Assert.True(TabButton(window, workspace, selected.ToString("D")).IsFocused);
            Assert.True(workspace.TryGetValue("Address", out var draft));
            Assert.Equal("https://unsent.example/draft", draft);
            Assert.True(chrome.CanReopenClosedTab);

            var evidence = Environment.GetEnvironmentVariable("BROWSE_EVIDENCE_DIRECTORY");
            if (!string.IsNullOrEmpty(evidence))
            {
                Directory.CreateDirectory(evidence);
                window.UpdateLayout();
                using var image = window.CaptureRenderedFrame();
                image?.Save(Path.Combine(evidence, "compact-chrome.png"));
            }
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(1180)]
    public async Task Tab_close_and_reopen_follow_the_selected_title_and_keep_surviving_address_focus(int width)
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths(), new FixtureFactory());
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        window.Width = width;
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            var leftId = chrome.State.SelectedTabId;
            var add = Find(window, "BrowseNewTabButton");
            await Click(window, workspace, add);
            var middle = chrome.State.SelectedTab;
            await Click(window, workspace, add);
            var rightId = chrome.State.SelectedTabId;
            var rightTitle = TabButton(window, workspace, rightId.ToString("D"));
            var middleTitle = TabButton(window, workspace, middle.Id.ToString("D"));
            var leftTitle = TabButton(window, workspace, leftId.ToString("D"));
            Assert.True(rightTitle.IsTabStop);
            Assert.False(leftTitle.IsTabStop); Assert.False(middleTitle.IsTabStop);
            Assert.True(rightTitle.Focus(NavigationMethod.Tab));
            Press(Key.Left);
            Assert.True(middleTitle.IsFocused); Assert.True(middleTitle.IsTabStop);
            Assert.False(rightTitle.IsTabStop); Assert.Equal(rightId, chrome.State.SelectedTabId);
            Press(Key.Home); Assert.True(leftTitle.IsFocused);
            Press(Key.End); Assert.True(rightTitle.IsFocused);
            Press(Key.Left); Assert.True(middleTitle.IsFocused);
            var middleClose = TabClose(middleTitle);
            var x = middleTitle.TranslatePoint(new Point(middleTitle.Bounds.Width / 2, 0), window)!.Value.X;
            Assert.InRange(x, 0, width);
            Press(Key.Tab); Assert.True(middleClose.IsFocused);
            Press(Key.Tab, RawInputModifiers.Shift); Assert.True(middleTitle.IsFocused);
            await Shortcut(Key.Enter, RawInputModifiers.None);
            Assert.Equal(middle.Id, chrome.State.SelectedTabId);
            Assert.True(middleTitle.IsFocused);

            await Shortcut(Key.W, RawInputModifiers.Control);
            Assert.Equal(rightId, chrome.State.SelectedTabId);
            Assert.True(TabButton(window, workspace, rightId.ToString("D")).IsFocused);
            await Shortcut(Key.T, RawInputModifiers.Control | RawInputModifiers.Shift);
            var restored = chrome.State.SelectedTab;
            Assert.Equal(middle.Id, restored.Id);
            Assert.Equal(middle.Address, restored.Address);
            Assert.Equal(middle.Title, restored.Title);
            Assert.Equal(new[] { leftId, middle.Id, rightId }, chrome.State.Tabs.Select(tab => tab.Id));
            Assert.True(TabButton(window, workspace, middle.Id.ToString("D")).IsFocused);

            // At narrow widths the inline close can sit under an overflow arrow. Scroll to
            // the end of the strip to expose this middle row's real pointer target.
            var scroll = Authored(window, "BrowseTabViewport").GetVisualDescendants().OfType<ScrollViewer>().Single();
            scroll.Offset = new Vector(Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width), 0);
            Press(Key.Escape); // The preceding keyboard reopen may still be showing the title tooltip.
            var activeClose = TabClose(TabButton(window, workspace, middle.Id.ToString("D")));
            var closePoint = activeClose.TranslatePoint(new Rect(activeClose.Bounds.Size).Center, window)!.Value;
            var hit = window.InputHitTest(closePoint) as Visual;
            Assert.True(hit?.GetSelfAndVisualAncestors().Contains(activeClose) == true,
                $"Pointer hit {hit?.GetType().Name} at {closePoint}; scroll {scroll.Offset}, extent {scroll.Extent}, viewport {scroll.Viewport}.");
            var beforePointer = workspace.OriginalCommand;
            window.MouseMove(closePoint); window.MouseDown(closePoint, MouseButton.Left); window.MouseUp(closePoint, MouseButton.Left);
            await SettleCommand(beforePointer);
            Assert.Equal(rightId, chrome.State.SelectedTabId);
            Assert.True(TabButton(window, workspace, rightId.ToString("D")).IsFocused);
            await Shortcut(Key.T, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Equal(middle.Id, chrome.State.SelectedTabId);
            Assert.True(TabButton(window, workspace, middle.Id.ToString("D")).IsFocused);

            if (Environment.GetEnvironmentVariable("BROWSE_EVIDENCE_DIRECTORY") is { Length: > 0 } evidence)
            {
                Directory.CreateDirectory(evidence);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(evidence, $"reopened-tab-focus-{width}x{window.Bounds.Height:0}.png"));
            }

            var backgroundClose = TabClose(TabButton(window, workspace, leftId.ToString("D")));
            var address = Assert.IsType<TextBox>(Authored(window, "BrowseAddressBox"));
            Assert.True(address.Focus(NavigationMethod.Tab));
            address.SelectAll(); window.KeyTextInput("https://unsent.example/draft");
            // The automation action is issued through the rendered background Close control
            // while the editor owns keyboard focus.
            await Click(window, workspace, backgroundClose);
            Assert.True(address.IsFocused);
            Assert.Equal("https://unsent.example/draft", address.Text);
            Assert.Equal(middle.Id, chrome.State.SelectedTabId);
            Assert.DoesNotContain(chrome.State.Tabs, tab => tab.Id == leftId);
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }

        async Task Shortcut(Key key, RawInputModifiers modifiers)
        {
            var before = workspace.OriginalCommand;
            Press(key, modifiers);
            await SettleCommand(before);
        }

        async Task SettleCommand(Task? before)
        {
            await Observe(Loader(window).WhenActionsIdleAsync(), window);
            Assert.NotNull(workspace.OriginalCommand); Assert.NotSame(before, workspace.OriginalCommand);
            await Observe(workspace.OriginalCommand!, window);
            await Observe(Task.WhenAll(workspace.OriginalNotifications.ToArray()), window);
            Settle(window);
        }

        void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            window.KeyPress(key, modifiers, PhysicalKey.None, null);
            window.KeyRelease(key, modifiers, PhysicalKey.None, null);
            Settle(window);
        }

        static Button TabClose(Button title) => title.GetVisualAncestors().OfType<CuiContainerControl>()
            .First(container => container.Name == "BrowseTabRow").AuthoredChildren.OfType<Button>()
            .Single(button => button.Name == "BrowseCloseTabRowButton");
    }

    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(1180)]
    public async Task New_tab_remains_a_visible_pointer_target_when_tabs_overflow(int width)
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        window.Width = width;
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            for (var i = 0; i < 12; i++)
            {
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var button = Find(window, "BrowseNewTabButton");
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                Assert.InRange(point.X, 0, width); Assert.InRange(point.Y, 0, 48);
                window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                await Observe(Loader(window).WhenActionsIdleAsync(), window);
                await Observe(workspace.OriginalCommand!, window);
                Assert.Equal(i + 2, chrome.State.Tabs.Count);
            }
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }

}
