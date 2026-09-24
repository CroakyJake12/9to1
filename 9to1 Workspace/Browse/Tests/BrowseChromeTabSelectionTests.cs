using Haven.Application;
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
