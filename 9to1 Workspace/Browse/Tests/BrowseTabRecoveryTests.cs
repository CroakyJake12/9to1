using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeOwnershipTests
{
    [Fact]
    public async Task Reopen_retains_original_standard_identity_address_position_engine_and_profile()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        var originalId = chrome.State.SelectedTabId; var oldHost = Assert.Single(factory.Hosts);
        oldHost.CommitFixturePage(new Uri("https://restore.example/resource"), "Restore me");
        await Task.WhenAll(chrome.OriginalNativeSources.ToArray());
        await chrome.NewTabAsync(false);
        await chrome.CloseTabAsync(originalId);
        Assert.True(chrome.CanReopenClosedTab);
        await chrome.ReopenClosedTabAsync();
        Assert.Equal(originalId, chrome.State.SelectedTabId);
        Assert.Equal(originalId, chrome.State.Tabs[0].Id);
        Assert.Equal(new Uri("https://restore.example/resource"), chrome.State.SelectedTab.Address.WebUri);
        Assert.Equal(BrowseEngineKind.Chromium, chrome.State.SelectedTab.Engine);
        Assert.Equal(factory.Requests[0].ProfileDirectory, factory.Requests[1].ProfileDirectory);
        Assert.Equal(1, oldHost.CloseCalls);
        Assert.NotSame(oldHost, chrome.ObserveOriginalEngine(originalId));
        Assert.Single(chrome.State.History); // Reopening is not itself a committed visit.
        Assert.False(chrome.CanReopenClosedTab);
        Assert.Equal(BrowseEngineKind.Chromium, (await new BrowseEnginePreferencesStore(paths).LoadAsync()).TabOverrides![originalId]);
    }

    [Fact]
    public async Task Closing_private_tab_never_offers_private_state_for_reopen()
    {
        var paths = new PreservedPaths();
        await using var chrome = await BrowseChrome.CreateAsync(paths);
        var privateId = (await chrome.NewTabAsync(true)).SelectedTabId;
        await chrome.CloseTabAsync(privateId);
        Assert.False(chrome.CanReopenClosedTab);
        await chrome.ReopenClosedTabAsync();
        Assert.DoesNotContain(chrome.State.Tabs, tab => tab.Id == privateId || tab.Privacy == BrowserTabPrivacy.Private);
    }

    [Fact]
    public async Task Cancelled_reopen_preserves_closed_tab_for_explicit_retry()
    {
        var paths = new PreservedPaths();
        await using var chrome = await BrowseChrome.CreateAsync(paths);
        var closedId = chrome.State.SelectedTabId;
        await chrome.CloseTabAsync(closedId);
        var current = chrome.State.SelectedTab;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chrome.ReopenClosedTabAsync(cancellation.Token));
        Assert.Equal(current, Assert.Single(chrome.State.Tabs)); Assert.True(chrome.CanReopenClosedTab);
        await chrome.ReopenClosedTabAsync(); Assert.Equal(closedId, chrome.State.SelectedTabId);
    }

    [Fact]
    public async Task Restart_restores_standard_tab_order_from_the_same_session_snapshot()
    {
        var paths = new PreservedPaths(); Guid[] originalOrder;
        await using (var chrome = await BrowseChrome.CreateAsync(paths))
        {
            await chrome.NewTabAsync(false); await chrome.NewTabAsync(false);
            originalOrder = chrome.State.Tabs.Select(tab => tab.Id).ToArray();
        }
        await using var restored = await BrowseChrome.CreateAsync(paths);
        Assert.Equal(originalOrder, restored.State.Tabs.Select(tab => tab.Id).ToArray());
    }

    [Fact]
    public async Task Failed_close_preference_write_keeps_a_valid_selected_surviving_tab()
    {
        var paths = new PreservedPaths(); var chrome = await BrowseChrome.CreateAsync(paths);
        RetainedFailedOwners.Add(chrome);
        var survivor = chrome.State.SelectedTabId;
        var closing = (await chrome.NewTabAsync(false)).SelectedTabId;
        Directory.CreateDirectory(Path.Combine(paths.DataDirectory, "browse-engine-preferences.json"));
        await Assert.ThrowsAnyAsync<IOException>(() => chrome.CloseTabAsync(closing));
        Assert.Equal(survivor, chrome.State.SelectedTabId);
        Assert.Equal(survivor, Assert.Single(chrome.State.Tabs).Id);
        Assert.True(chrome.CanReopenClosedTab);
        Directory.Delete(Path.Combine(paths.DataDirectory, "browse-engine-preferences.json"));
        await chrome.ReopenClosedTabAsync();
        Assert.Equal(closing, chrome.State.SelectedTabId);
        Assert.Contains(chrome.State.Tabs, tab => tab.Id == closing);
        await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        RetainedFailedOwners.Remove(chrome);
    }

    [Fact]
    public async Task Failed_last_tab_replacement_retains_original_identity_for_recovery()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var original = chrome.State.SelectedTabId;
        factory.FailEngine = BrowseEngineKind.Gecko;
        await Assert.ThrowsAsync<IOException>(() => chrome.CloseTabAsync(original));
        Assert.Equal(original, Assert.Single(chrome.State.Tabs).Id);
        Assert.Equal(original, chrome.State.SelectedTabId);
        Assert.Equal(BrowseEngineState.Crashed, chrome.State.SelectedTab.EngineState);
        factory.FailEngine = null;
        await chrome.RecoverSelectedTabAsync();
        Assert.Equal(original, chrome.State.SelectedTabId);
        Assert.Equal(BrowseEngineState.Ready, chrome.State.SelectedTab.EngineState);
    }

    [Fact]
    public async Task Failed_reopen_start_leaves_no_ghost_and_retry_reuses_original_closed_identity()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        var closedId = chrome.State.SelectedTabId;
        await chrome.CloseTabAsync(closedId);
        var current = chrome.State.SelectedTab;
        factory.FailEngine = BrowseEngineKind.Chromium;
        await Assert.ThrowsAsync<IOException>(() => chrome.ReopenClosedTabAsync());
        Assert.Equal(current, Assert.Single(chrome.State.Tabs)); Assert.True(chrome.CanReopenClosedTab);
        factory.FailEngine = null;
        await chrome.ReopenClosedTabAsync();
        Assert.Equal(closedId, chrome.State.SelectedTabId);
        Assert.Single(chrome.State.Tabs, tab => tab.Id == closedId); Assert.False(chrome.CanReopenClosedTab);
    }
}
