using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Haven.Browser;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    [AvaloniaFact]
    public async Task History_search_removes_only_the_issued_visit_and_preserves_duplicate_url_sibling_and_draft()
    {
        using var trace = new FixtureTraceScope(nameof(History_search_removes_only_the_issued_visit_and_preserves_duplicate_url_sibling_and_draft));
        var paths = new Paths();
        using (var data = new BrowserDataService(paths))
        {
            await data.AddBookmarkAsync("Saved", "https://bookmarked.example/", "", default);
            await data.RecordVisitAsync("Research", "https://research.example/same", false, default);
            await data.RecordVisitAsync("Research", "https://research.example/same", false, default);
            await data.RecordVisitAsync("Other task", "https://other.example/", false, default);
            await data.RecordVisitAsync("Private research", "https://private.example/", true, default);
        }
        var chrome = await BrowseChrome.CreateAsync(paths);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            await Click(window, workspace, Find(window, "BrowseOpenHistoryButton"));
            Assert.Equal(3, chrome.State.History.Count);
            var tabId = chrome.State.SelectedTabId; var page = chrome.State.SelectedTab.Address;
            Assert.True(workspace.TrySetValue("Address", "https://unsent.example/draft"));

            var search = window.GetLogicalDescendants().OfType<TextBox>().Single(control => control.Name == "BrowseHistorySearchBox");
            search.Text = "ReSeArCh"; Dispatcher.UIThread.RunJobs(); Settle(window);
            Assert.True(workspace.TryGetValue("HistoryQuery", out var query)); Assert.Equal("ReSeArCh", query);
            var rows = HistoryManagementRows(workspace); Assert.Equal(2, rows.Length);
            var removed = rows[0]; var removedId = Guid.Parse(removed.Key);
            Assert.All(rows, row => Assert.Contains("https://research.example/same", row.Detail));
            var remove = window.GetLogicalDescendants().OfType<Button>().First(button => button.Name == "BrowseRemoveHistoryEntryButton");
            await Click(window, workspace, remove);
            Assert.Equal(2, chrome.State.History.Count);
            Assert.DoesNotContain(chrome.State.History, visit => visit.Id == removedId);
            Assert.Single(chrome.State.History.Where(visit => visit.Address == "https://research.example/same"));
            Assert.Single(chrome.State.Bookmarks);
            Assert.Equal(tabId, chrome.State.SelectedTabId); Assert.Equal(page, chrome.State.SelectedTab.Address);
            Assert.True(workspace.TryGetValue("Address", out var draft)); Assert.Equal("https://unsent.example/draft", draft);
            Assert.True(IsTrue(workspace, "HasAddressDraft"));

            var current = Assert.Single(HistoryManagementRows(workspace));
            search.Text = "other.example"; Dispatcher.UIThread.RunJobs(); Settle(window);
            Assert.Equal("Other task", Assert.Single(HistoryManagementRows(workspace)).Label);
            var lastCommand = workspace.OriginalCommand;
            await workspace.DispatchAsync("9to1.Browse.RemoveHistoryEntry", current.Target); // Present in storage, absent from this issued filter.
            await workspace.DispatchAsync("9to1.Browse.RemoveHistoryEntry", removed.Target);
            Assert.True(workspace.TryGetValue("Bookmarks", out var bookmarks));
            var bookmark = Assert.Single(Assert.IsType<BrowseNativeWorkspace.SavedEntryRow[]>(bookmarks));
            await workspace.DispatchAsync("9to1.Browse.RemoveHistoryEntry", bookmark.Target);
            Assert.Same(lastCommand, workspace.OriginalCommand);
            Assert.Equal(2, chrome.State.History.Count); Assert.Single(chrome.State.Bookmarks);
            await Click(window, workspace, Find(window, "BrowseResetHistoryQueryButton"));
            Assert.Equal(2, HistoryManagementRows(workspace).Length);
            using (var persisted = new BrowserDataService(paths))
            {
                Assert.Equal(2, persisted.History.Count);
                Assert.DoesNotContain(persisted.History, visit => visit.Id == removedId);
                Assert.Single(persisted.History.Where(visit => visit.Address == "https://research.example/same"));
                Assert.Single(persisted.Bookmarks);
            }
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task History_removal_obeys_action_availability_and_clear_all_includes_hidden_results()
    {
        using var trace = new FixtureTraceScope(nameof(History_removal_obeys_action_availability_and_clear_all_includes_hidden_results));
        var paths = new Paths();
        using (var data = new BrowserDataService(paths))
        {
            await data.RecordVisitAsync("One", "https://one.example/", false, default);
            await data.RecordVisitAsync("Two", "https://two.example/", false, default);
            await data.AddBookmarkAsync("Keep", "https://keep.example/", "", default);
        }
        var chrome = await BrowseChrome.CreateAsync(paths);
        var workspace = new BrowseNativeWorkspace(chrome, command => command != "9to1.Browse.RemoveHistoryEntry");
        var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
        RegisterProductionImageResolver(window);
        try
        {
            window.Show(); await Observe(window.InitializeAsync(), window);
            await Click(window, workspace, Find(window, "BrowseOpenHistoryButton"));
            Assert.True(workspace.TrySetValue("HistoryQuery", "one")); Settle(window);
            var row = Assert.Single(HistoryManagementRows(workspace));
            Assert.False(window.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "BrowseRemoveHistoryEntryButton").IsEnabled);
            var command = workspace.OriginalCommand;
            await workspace.DispatchAsync("9to1.Browse.RemoveHistoryEntry", row.Target);
            Assert.Same(command, workspace.OriginalCommand); Assert.Equal(2, chrome.State.History.Count);
            Assert.False(workspace.TrySetValue("HistoryQuery", true));
            Assert.False(workspace.TrySetValue("HistoryQuery", new string('x', 2049)));
            Assert.True(workspace.TryGetValue("HistoryQuery", out var query)); Assert.Equal("one", query);
            var selected = chrome.State.SelectedTabId;
            await Click(window, workspace, Find(window, "BrowseClearHistoryButton"));
            Assert.Empty(chrome.State.History); Assert.Empty(HistoryManagementRows(workspace));
            Assert.Equal(selected, chrome.State.SelectedTabId); Assert.Single(chrome.State.Bookmarks);
            Assert.True(IsTrue(workspace, "EmptyHistory")); Assert.False(IsTrue(workspace, "EmptyHistoryResults"));
            using (var persisted = new BrowserDataService(paths)) { Assert.Empty(persisted.History); Assert.Single(persisted.Bookmarks); }
            await Retire(window);
        }
        catch (Exception failure) { Trace("test:failed:" + failure.GetType().Name + ":" + failure.Message); RetainedFailedOwners.Add((window, failure)); throw; }
    }

    private static BrowseNativeWorkspace.SavedEntryRow[] HistoryManagementRows(BrowseNativeWorkspace workspace)
    {
        Assert.True(workspace.TryGetValue("History", out var history));
        return Assert.IsType<BrowseNativeWorkspace.SavedEntryRow[]>(history);
    }
}
