using Haven.Core;
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
    [AvaloniaTheory]
    [InlineData(BrowserNativePage.History, "BrowseOpenHistoryButton", "BrowseHistoryPage")]
    [InlineData(BrowserNativePage.Bookmarks, "BrowseOpenBookmarksButton", "BrowseBookmarksPage")]
    [InlineData(BrowserNativePage.Downloads, "BrowseOpenDownloadsButton", "BrowseDownloadsPage")]
    [InlineData(BrowserNativePage.Settings, "BrowseOpenSettingsButton", "BrowseSettingsPage")]
    public async Task Pointer_opens_native_page_without_creating_renderer_and_keeps_background_view_mounted(
        BrowserNativePage page, string commandId, string panelId)
    {
        var paths = new Paths(); var factory = new FixtureFactory(); var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            await Click(window, workspace, Find(window, "BrowseChromiumTabButton"));
            var originalEngine = Assert.Single(factory.Hosts);
            var originalParent = originalEngine.OriginalNativeView.Parent; Assert.NotNull(originalParent);
            var webId = chrome.State.SelectedTabId;
            var button = Find(window, commandId);
            window.UpdateLayout(); button.BringIntoView(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
            Assert.NotNull(point); Assert.True(button.IsEffectivelyVisible);
            var previous = workspace.OriginalCommand;
            window.MouseDown(point.Value, MouseButton.Left); window.MouseUp(point.Value, MouseButton.Left);
            Assert.NotNull(workspace.OriginalCommand); Assert.NotSame(previous, workspace.OriginalCommand);
            await Observe(workspace.OriginalCommand!, window);
            await Task.WhenAll(workspace.OriginalNotifications.ToArray());
            Assert.Equal(page, chrome.State.SelectedTab.NativePage); Assert.NotEqual(webId, chrome.State.SelectedTabId);
            Assert.Equal(AppContentAddress.Parse(BrowserNativePages.Address(page).AbsoluteUri), chrome.State.SelectedTab.Address);
            Assert.Null(chrome.ObserveOriginalEngine(chrome.State.SelectedTabId));
            var panel = window.GetLogicalDescendants().OfType<Control>().Single(control => control.Name == panelId);
            Assert.True(panel.IsEffectivelyVisible);
            Assert.Same(originalParent, originalEngine.OriginalNativeView.Parent);
            Assert.False(originalEngine.OriginalNativeView.IsEffectivelyVisible);
            Assert.Single(factory.Hosts);
            Assert.False(Find(window, "BrowseChromiumTabButton").IsEnabled);
            Assert.False(Find(window, "BrowseBookmarkButton").IsEnabled);
            Assert.Empty(chrome.State.History);
            if (page == BrowserNativePage.Settings)
            {
                await Click(window, workspace, Find(window, "BrowseDefaultChromiumButton"));
                Assert.Equal(BrowseEngineKind.Chromium, (await new BrowseEnginePreferencesStore(paths).LoadAsync(CancellationToken.None)).DefaultEngine);
                Assert.Equal(page, chrome.State.SelectedTab.NativePage);
            }
            var internalId = chrome.State.SelectedTabId;
            await Click(window, workspace, button);
            Assert.Equal(internalId, chrome.State.SelectedTabId); Assert.Equal(2, chrome.State.Tabs.Count);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var evidence = Environment.GetEnvironmentVariable("BROWSE_EVIDENCE_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                Directory.CreateDirectory(evidence);
                using var capture = window.CaptureRenderedFrame(); Assert.NotNull(capture);
                capture.Save(Path.Combine(evidence, "browse-native-" + page.ToString().ToLowerInvariant() + ".png"));
            }
            var originalRow = Rows(workspace).Single(row => row.Key == webId.ToString("D"));
            var originalButton = window.GetLogicalDescendants().OfType<Button>().Single(control => Equals(control.Content, originalRow.Label));
            await Click(window, workspace, originalButton);
            Assert.Equal(webId, chrome.State.SelectedTabId);
            Assert.Same(originalEngine, chrome.ObserveOriginalEngine(webId));
            Assert.Same(originalParent, originalEngine.OriginalNativeView.Parent);
            Assert.True(originalEngine.OriginalNativeView.IsEffectivelyVisible);
            await Retire(window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((window, failure)); throw; }
    }
}
