using Haven.Application;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class BrowseChromeTabSelectionTests
{
    [Fact]
    public async Task ClosingBackgroundTabPreservesSelectedTab()
    {
        using var paths = new TestPaths();
        await using var chrome = await BrowseChrome.CreateAsync(paths);
        var original = chrome.State.SelectedTabId;
        var added = await chrome.NewTabAsync(isPrivate: false);
        var selected = added.SelectedTabId;

        var result = await chrome.CloseTabAsync(original);

        Assert.Equal(selected, result.SelectedTabId);
        Assert.DoesNotContain(result.Tabs, tab => tab.Id == original);
        Assert.Contains(result.Tabs, tab => tab.Id == selected);
    }

    [Fact]
    public async Task CancelledSelectionDoesNotChangeActiveTab()
    {
        using var paths = new TestPaths();
        await using var chrome = await BrowseChrome.CreateAsync(paths);
        var active = chrome.State.SelectedTabId;
        var other = (await chrome.NewTabAsync(isPrivate: false)).SelectedTabId;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chrome.SelectTabAsync(active, cancellation.Token));

        Assert.Equal(other, chrome.State.SelectedTabId);
    }

    [Fact]
    public async Task DefaultEnginePreferenceSurvivesBrowseRestartAndAppliesToNewTab()
    {
        using var paths = new TestPaths();
        await using (var first = await BrowseChrome.CreateAsync(paths))
            await first.SetDefaultEngineAsync(BrowseEngineKind.Chromium);

        await using var restored = await BrowseChrome.CreateAsync(paths);

        Assert.Equal(BrowseEngineKind.Chromium, restored.State.SelectedTab.Engine);
    }

    [Fact]
    public async Task PrivateTabsAreNotRestoredFromTheStandardSessionFile()
    {
        using var paths = new TestPaths();
        await using (var chrome = await BrowseChrome.CreateAsync(paths))
            await chrome.NewTabAsync(isPrivate: true);

        await using var restored = await BrowseChrome.CreateAsync(paths);

        Assert.All(restored.State.Tabs, tab => Assert.Equal(BrowserTabPrivacy.Standard, tab.Privacy));
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "havenos-browse-selection-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
