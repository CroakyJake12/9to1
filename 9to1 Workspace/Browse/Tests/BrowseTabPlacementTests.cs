using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeOwnershipTests
{
    [Fact]
    public async Task Canonical_tab_groups_keep_guid_membership_order_name_and_collapse_state_through_operations()
    {
        var session = new TabSessionFixture(new([], null) { Groups = [] });
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), homeProfileId: "profile",
            tabSessionPersistence: session);
        var first = (await chrome.NewTabAsync(false)).SelectedTabId;
        var second = (await chrome.NewTabAsync(false)).SelectedTabId;
        var third = (await chrome.NewTabAsync(false)).SelectedTabId;
        await chrome.CreateTabGroupAsync("Research");
        var group = Assert.Single(chrome.State.Groups);
        var stableId = group.Id;
        Assert.Equal(new[] { third }, group.OrderedTabIds);

        await chrome.SelectTabAsync(second);
        Assert.True(chrome.CanMoveSelectedTabToGroup(stableId));
        await chrome.MoveSelectedTabToGroupAsync(stableId);
        await chrome.RenameTabGroupAsync(stableId, "Project notes");
        await chrome.SetTabGroupCollapsedAsync(stableId, true);
        await chrome.SelectTabAsync(first);
        await chrome.MoveSelectedTabToGroupAsync(stableId);
        Assert.Equal(stableId, Assert.Single(chrome.State.Groups).Id);
        Assert.Equal("Project notes", Assert.Single(chrome.State.Groups).Name);
        Assert.True(Assert.Single(chrome.State.Groups).IsCollapsed);
        Assert.Equal(new[] { first, second, third }, Assert.Single(chrome.State.Groups).OrderedTabIds);
        Assert.All(chrome.State.Tabs.Where(tab => tab.GroupId == stableId), tab =>
        { Assert.Equal("Project notes", tab.GroupName); Assert.True(tab.IsGroupCollapsed); });
        Assert.Equal(new[] { first, second, third }, session.Saves[^1].Groups!.Single().OrderedTabIds);

        await chrome.SelectTabAsync(first);
        await chrome.UngroupSelectedTabAsync();
        await chrome.SelectTabAsync(second);
        await chrome.UngroupSelectedTabAsync();
        Assert.Equal(new[] { third }, Assert.Single(chrome.State.Groups).OrderedTabIds);
        await chrome.DissolveTabGroupAsync(stableId);
        Assert.Empty(chrome.State.Groups);
        Assert.All(chrome.State.Tabs, tab => Assert.Null(tab.GroupId));
        Assert.Empty(session.Saves[^1].Groups!);
    }

    [Fact]
    public async Task Stable_id_reorder_moves_before_or_after_a_target_without_crossing_private_owner()
    {
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths());
        var home = chrome.State.SelectedTabId;
        var first = (await chrome.NewTabAsync(false)).SelectedTabId;
        var second = (await chrome.NewTabAsync(false)).SelectedTabId;
        var third = (await chrome.NewTabAsync(false)).SelectedTabId;
        var privateTab = (await chrome.NewTabAsync(true)).SelectedTabId;

        await chrome.MoveTabRelativeAsync(first, third, beforeTarget: false);
        Assert.Equal(new[] { home, second, third, first, privateTab }, chrome.State.Tabs.Select(tab => tab.Id));
        await chrome.MoveTabRelativeAsync(first, second, beforeTarget: true);
        Assert.Equal(new[] { home, first, second, third, privateTab }, chrome.State.Tabs.Select(tab => tab.Id));
        Assert.False(chrome.CanMoveTab(privateTab, -1));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chrome.MoveTabRelativeAsync(privateTab, third, beforeTarget: true));
        Assert.Equal(new[] { home, first, second, third, privateTab }, chrome.State.Tabs.Select(tab => tab.Id));
    }

    [Fact]
    public async Task Pin_and_move_preserve_live_views_selection_then_survive_reopen_and_restart()
    {
        var paths = new PreservedPaths();
        Guid[] savedOrder;
        Guid first, third;
        await using (var chrome = await BrowseChrome.CreateAsync(paths, new ChromiumFixtureFactory(includeGecko: true)))
        {
            first = chrome.State.SelectedTabId;
            var second = (await chrome.NewTabAsync(false)).SelectedTabId;
            third = (await chrome.NewTabAsync(false)).SelectedTabId;
            var originalViews = chrome.State.Tabs.ToDictionary(tab => tab.Id, tab => chrome.ObserveOriginalEngine(tab.Id));
            await chrome.SetTabPinnedAsync(first, true);
            await chrome.SetTabPinnedAsync(third, true);
            Assert.Equal(third, chrome.State.SelectedTabId);
            Assert.Equal(new[] { first, third, second }, chrome.State.Tabs.Select(tab => tab.Id));
            Assert.True(chrome.CanMoveTab(third, -1));
            await chrome.MoveTabAsync(third, -1);
            Assert.Equal(new[] { third, first, second }, chrome.State.Tabs.Select(tab => tab.Id));
            Assert.False(chrome.CanMoveTab(third, -1));
            Assert.False(chrome.CanMoveTab(second, -1)); // Unpinned tabs do not cross the pin boundary.
            Assert.Equal(third, chrome.State.SelectedTabId);
            foreach (var tab in chrome.State.Tabs) Assert.Same(originalViews[tab.Id], chrome.ObserveOriginalEngine(tab.Id));

            await chrome.CloseTabAsync(first); // An explicit close still works for a pinned tab.
            await chrome.ReopenClosedTabAsync();
            Assert.Equal(first, chrome.State.SelectedTabId);
            Assert.True(chrome.State.SelectedTab.IsPinned);
            savedOrder = chrome.State.Tabs.Select(tab => tab.Id).ToArray();
        }
        await using var restored = await BrowseChrome.CreateAsync(paths);
        Assert.Equal(savedOrder, restored.State.Tabs.Select(tab => tab.Id));
        Assert.True(restored.State.Tabs.Single(tab => tab.Id == first).IsPinned);
        Assert.True(restored.State.Tabs.Single(tab => tab.Id == third).IsPinned);
    }

    [Fact]
    public async Task Owner_projection_roundtrips_pin_order_and_selection_without_private_tab_metadata()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var session = new TabSessionFixture(new([
            new(first, "First", "https://first.example/", BrowserTabPrivacy.Standard, "original-group", DateTimeOffset.UtcNow),
            new(second, "Second", "https://second.example/", BrowserTabPrivacy.Standard, "original-group", DateTimeOffset.UtcNow) { IsPinned = true }
        ], first));
        BrowseTabSessionProjection checkpoint;
        await using (var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), tabSessionPersistence: session))
        {
            Assert.Equal(new[] { second, first }, chrome.State.Tabs.Select(tab => tab.Id));
            Assert.Equal(first, chrome.State.SelectedTabId);
            await chrome.SetTabPinnedAsync(first, true);
            await chrome.MoveTabAsync(first, -1);
            var privateId = (await chrome.NewTabAsync(true)).SelectedTabId;
            await chrome.SetTabPinnedAsync(privateId, true);
            Assert.True(chrome.State.SelectedTab.IsPinned);
            Assert.DoesNotContain(session.Saves[^1].Tabs, tab => tab.Id == privateId || tab.Privacy == BrowserTabPrivacy.Private);
            Assert.Null(session.Saves[^1].SelectedTabId);
            await chrome.SelectTabAsync(second);
            checkpoint = session.Saves[^1];
            Assert.Equal(new[] { first, second }, checkpoint.Tabs.Select(tab => tab.Id));
            Assert.All(checkpoint.Tabs, tab => { Assert.True(tab.IsPinned == true); Assert.Equal("original-group", tab.Group); });
            Assert.Equal(second, checkpoint.SelectedTabId);
        }
        await using var restored = await BrowseChrome.CreateAsync(new PreservedPaths(), tabSessionPersistence: new TabSessionFixture(checkpoint));
        Assert.Equal(new[] { first, second }, restored.State.Tabs.Select(tab => tab.Id));
        Assert.Equal(second, restored.State.SelectedTabId);
        Assert.All(restored.State.Tabs, tab => Assert.True(tab.IsPinned));
    }

    [Fact]
    public async Task Native_app_placement_keeps_the_original_owner_and_permanent_home()
    {
        var apps = new AppFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(),
            appContentFactory: apps, internalContentFactory: apps, homeProfileId: "profile");
        var home = chrome.State.SelectedTabId;
        await chrome.NewTabAsync(false);
        await chrome.NavigateAsync("9to1://write/original-document");
        var document = chrome.State.SelectedTabId;
        var originalOwner = chrome.ObserveOriginalAppContent(document);
        var originalAddress = chrome.State.SelectedTab.Address;
        var other = (await chrome.NewTabAsync(false)).SelectedTabId;
        var admitted = apps.Requests.Count;
        await chrome.SetTabPinnedAsync(document, true);
        await chrome.SetTabPinnedAsync(other, true);
        await chrome.MoveTabAsync(document, 1);
        Assert.Equal(new[] { home, other, document }, chrome.State.Tabs.Select(tab => tab.Id));
        Assert.Equal(other, chrome.State.SelectedTabId);
        Assert.Same(originalOwner, chrome.ObserveOriginalAppContent(document));
        Assert.Equal(originalAddress, chrome.State.Tabs.Single(tab => tab.Id == document).Address);
        Assert.Equal(admitted, apps.Requests.Count);
        Assert.False(chrome.CanMoveTab(home, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.SetTabPinnedAsync(home, true));
        Assert.Equal(home, chrome.State.Tabs[0].Id);
    }

    [Fact]
    public async Task Cancelled_placement_leaves_pin_order_and_selection_unchanged()
    {
        await using var chrome = await BrowseChrome.CreateAsync(new PreservedPaths());
        var first = chrome.State.SelectedTabId;
        await chrome.NewTabAsync(false);
        var before = chrome.State;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chrome.SetTabPinnedAsync(first, true, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chrome.MoveTabAsync(first, 1, cancellation.Token));
        Assert.Equal(before.SelectedTabId, chrome.State.SelectedTabId);
        Assert.Equal(before.Tabs, chrome.State.Tabs);
        Assert.False(chrome.HasUnsavedTabPlacement);
    }

    [Fact]
    public async Task Failed_owner_write_preserves_live_pin_and_original_fault_for_explicit_save_retry()
    {
        var id = Guid.NewGuid();
        var session = new TabSessionFixture(new([
            new(id, "Original", "https://original.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow)
        ], id));
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), tabSessionPersistence: session);
        RetainedFailedOwners.Add(chrome);
        var failure = new IOException("Original owner pin save failed");
        var actual = Task.FromException(failure); session.NextSave = actual;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => chrome.SetTabPinnedAsync(id, true)));
        Assert.True(chrome.State.SelectedTab.IsPinned);
        Assert.True(chrome.HasUnsavedTabPlacement);
        Assert.Contains(actual, chrome.OriginalNativeSources);
        await chrome.SaveTabPlacementAsync();
        Assert.False(chrome.HasUnsavedTabPlacement);
        Assert.True(Assert.Single(session.Saves[^1].Tabs).IsPinned == true);
        var close = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure, close.Flatten().InnerExceptions); // A later successful write does not erase the failed original.
    }
}
