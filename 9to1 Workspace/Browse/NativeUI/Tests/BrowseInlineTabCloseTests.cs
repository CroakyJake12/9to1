using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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
    public async Task New_tab_remains_a_visible_pointer_target_when_tabs_overflow(int width)
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
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
