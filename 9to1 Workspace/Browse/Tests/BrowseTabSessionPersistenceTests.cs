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
        Assert.Equal(chrome.State.SelectedTab.Status, chrome.State.Status);
    }

    [Fact]
    public async Task Canonical_session_save_failure_remains_in_original_retirement_evidence()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var session = new TabSessionFixture(new([
            new(first, "First", "https://first.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow),
            new(second, "Second", "https://second.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow)
        ], first));
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), tabSessionPersistence: session);
        RetainedFailedOwners.Add(chrome);
        var failure = new IOException("Canonical session write failed"); var actual = Task.FromException(failure); session.NextSave = actual;
        var before = chrome.State;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => chrome.SelectTabAsync(second)));
        Assert.Equal(second, session.Saves[^1].SelectedTabId);
        Assert.Equal(first, chrome.State.SelectedTabId);
        Assert.Equal(before.SelectedTabId, chrome.State.SelectedTabId);
        Assert.Equal(before.Status, chrome.State.Status);
        Assert.Contains(actual, chrome.OriginalNativeSources);
        var close = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure, close.Flatten().InnerExceptions);
    }

    [Fact]
    public async Task Failed_close_session_save_keeps_the_retired_standard_tab_available_for_reopen()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var session = new TabSessionFixture(new([
            new(first, "First", "https://first.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow),
            new(second, "Second", "https://second.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow)
        ], second));
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), tabSessionPersistence: session);
        RetainedFailedOwners.Add(chrome);
        var failure = new IOException("Canonical session write failed while closing");
        var actual = Task.FromException(failure);
        session.NextSave = actual;

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => chrome.CloseTabAsync(second)));

        Assert.Single(chrome.State.Tabs, tab => tab.Id == first);
        Assert.Equal(first, chrome.State.SelectedTabId);
        Assert.True(chrome.CanReopenClosedTab);
        Assert.Equal(first, session.Saves[^1].SelectedTabId);
        Assert.Equal(first, Assert.Single(session.Saves[^1].Tabs).Id);
        Assert.Contains(actual, chrome.OriginalNativeSources);

        await chrome.ReopenClosedTabAsync();

        Assert.Contains(chrome.State.Tabs, tab => tab.Id == second && tab.Address.WebUri == new Uri("https://second.example/"));
        Assert.Equal(second, chrome.State.SelectedTabId);
        Assert.False(chrome.CanReopenClosedTab);
        var close = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure, close.Flatten().InnerExceptions);
        RetainedFailedOwners.Remove(chrome);
    }

    [Fact]
    public async Task Earlier_selection_save_failure_does_not_undo_a_newer_selection_or_status()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var session = new TabSessionFixture(new([
            new(first, "First", "https://first.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow),
            new(second, "Second", "https://second.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow)
        ], first));
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), tabSessionPersistence: session);
        RetainedFailedOwners.Add(chrome);
        var failure = new IOException("Earlier canonical selection write failed");
        var heldSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.NextSave = heldSave.Task;

        var earlierSelection = chrome.SelectTabAsync(second);
        Assert.False(earlierSelection.IsCompleted);
        await chrome.SelectTabAsync(first);
        var latest = chrome.State;
        heldSave.SetException(failure);

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => earlierSelection));
        Assert.Equal(first, chrome.State.SelectedTabId);
        Assert.Equal(latest.SelectedTabId, chrome.State.SelectedTabId);
        Assert.Equal(latest.Status, chrome.State.Status);
        Assert.Contains(heldSave.Task, chrome.OriginalNativeSources);
        var close = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure, close.Flatten().InnerExceptions);
        RetainedFailedOwners.Remove(chrome);
    }

    [Fact]
    public async Task Selection_save_failure_after_closing_previous_tab_keeps_latest_live_identity_and_status()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var session = new TabSessionFixture(new([
            new(first, "First", "https://first.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow),
            new(second, "Second", "https://second.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow)
        ], first));
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), tabSessionPersistence: session);
        RetainedFailedOwners.Add(chrome);
        var failure = new IOException("Held selection write failed after previous tab close");
        var heldSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.NextSave = heldSave.Task;

        var pendingSelection = chrome.SelectTabAsync(second);
        Assert.False(pendingSelection.IsCompleted);
        await chrome.CloseTabAsync(first);
        var latest = chrome.State;
        Assert.Equal(second, latest.SelectedTabId);
        Assert.DoesNotContain(latest.Tabs, tab => tab.Id == first);
        heldSave.SetException(failure);

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => pendingSelection));
        Assert.Equal(second, chrome.State.SelectedTabId);
        Assert.DoesNotContain(chrome.State.Tabs, tab => tab.Id == first);
        Assert.Equal(latest.Status, chrome.State.Status);
        Assert.Contains(heldSave.Task, chrome.OriginalNativeSources);
        Assert.True(chrome.CanReopenClosedTab);
        var close = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure, close.Flatten().InnerExceptions);
        RetainedFailedOwners.Remove(chrome);
    }

    [Fact]
    public async Task Selection_failure_during_renderer_retirement_cannot_restore_the_closing_previous_tab()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var session = new TabSessionFixture(new([
            new(first, "First", "https://first.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow),
            new(second, "Second", "https://second.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow)
        ], first));
        var factory = new ChromiumFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), factory, tabSessionPersistence: session);
        RetainedFailedOwners.Add(chrome);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        var renderer = Assert.Single(factory.Hosts);
        var closeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        renderer.CloseGate = closeGate.Task;
        var heldSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException("Selection save failed during accepted renderer close");
        session.NextSave = heldSave.Task;

        var pendingSelection = chrome.SelectTabAsync(second);
        var pendingClose = chrome.CloseTabAsync(first);
        try
        {
            await renderer.OriginalCloseSource.Task;
            heldSave.SetException(failure);
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => pendingSelection));
        }
        finally { closeGate.TrySetResult(); }
        await pendingClose;

        Assert.Equal(second, chrome.State.SelectedTabId);
        Assert.DoesNotContain(chrome.State.Tabs, tab => tab.Id == first);
        Assert.Equal("Tab closed.", chrome.State.Status);
        Assert.Contains(heldSave.Task, chrome.OriginalNativeSources);
        var close = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure, close.Flatten().InnerExceptions);
        RetainedFailedOwners.Remove(chrome);
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
