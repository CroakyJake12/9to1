using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeOwnershipTests
{
    [Fact]
    public async Task Canonical_session_projection_replaces_tab_storage_and_preserves_original_browser_data()
    {
        var paths = new PreservedPaths(); var legacyId = Guid.NewGuid(); var webId = Guid.NewGuid(); var appId = Guid.NewGuid();
        using (var data = new BrowserDataService(paths))
        {
            await data.SaveTabsAsync([new(legacyId, "Legacy", "https://legacy.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow)], default);
            await data.AddBookmarkAsync("Original bookmark", "https://bookmark.example/", "", default);
            await data.RecordVisitAsync("Original history", "https://history.example/", false, default);
        }
        var session = new TabSessionFixture(new([
            new(webId, "Web", "https://projected.example/", BrowserTabPrivacy.Standard, "original-group", DateTimeOffset.UtcNow) { ProfileId = "profile" },
            new(appId, "Assistant", "9to1://assistants/library/id", BrowserTabPrivacy.Standard, "original-group", DateTimeOffset.UtcNow) { ProfileId = "profile" },
            new(Guid.NewGuid(), "Private", "https://private.example/", BrowserTabPrivacy.Private, "", DateTimeOffset.UtcNow)
        ], appId));
        var apps = new AppFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, appContentFactory: apps, homeProfileId: "profile", tabSessionPersistence: session);
        Assert.Equal(appId, chrome.State.SelectedTabId); Assert.Equal(2, chrome.State.Tabs.Count);
        Assert.Equal("9to1://spaces/assistants/library/id", Assert.Single(apps.Requests).Address.Value);
        Assert.Single(chrome.State.Bookmarks); Assert.Single(chrome.State.History);
        Assert.Equal(appId, session.Saves[^1].SelectedTabId);
        await chrome.SelectTabAsync(webId);
        Assert.Equal(webId, session.Saves[^1].SelectedTabId);
        Assert.All(session.Saves[^1].Tabs, tab => Assert.Equal("original-group", tab.Group));
        await chrome.NewTabAsync(true);
        Assert.Null(session.Saves[^1].SelectedTabId);
        Assert.DoesNotContain(session.Saves[^1].Tabs, tab => tab.Privacy == BrowserTabPrivacy.Private);
        using var originalStore = new BrowserDataService(paths);
        Assert.Equal(legacyId, Assert.Single(originalStore.Tabs).Id);
        Assert.Single(originalStore.Bookmarks); Assert.Single(originalStore.History);
    }

    [Fact]
    public async Task Canonical_session_selection_save_retains_the_actual_pending_owner_task()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var session = new TabSessionFixture(new([
            new(first, "First", "https://first.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow),
            new(second, "Second", "https://second.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow)
        ], first));
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), tabSessionPersistence: session);
        var actualSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); session.NextSave = actualSave.Task;
        var selection = chrome.SelectTabAsync(second);
        Assert.False(selection.IsCompleted); Assert.Contains(actualSave.Task, chrome.OriginalNativeSources);
        Assert.Equal(second, session.Saves[^1].SelectedTabId);
        actualSave.SetResult(); await selection;
        Assert.Equal(second, chrome.State.SelectedTabId);
    }

    [Fact]
    public async Task Canonical_session_save_failure_remains_in_original_retirement_evidence()
    {
        var session = new TabSessionFixture(null);
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), tabSessionPersistence: session);
        RetainedFailedOwners.Add(chrome);
        var failure = new IOException("Canonical session write failed"); var actual = Task.FromException(failure); session.NextSave = actual;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => chrome.SelectTabAsync(chrome.State.SelectedTabId)));
        Assert.Contains(actual, chrome.OriginalNativeSources);
        var close = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure, close.Flatten().InnerExceptions);
    }

    private sealed class TabSessionFixture(BrowseTabSessionProjection? projection) : IBrowseTabSessionPersistence
    {
        public List<BrowseTabSessionProjection> Saves { get; } = [];
        public Task? NextSave;
        public Task<BrowseTabSessionProjection?> LoadAsync(CancellationToken token) => Task.FromResult(projection);
        public Task SaveAsync(BrowseTabSessionProjection value, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Saves.Add(value); var original = NextSave ?? Task.CompletedTask; NextSave = null; return original; }
    }
}
