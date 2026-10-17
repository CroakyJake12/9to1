using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeOwnershipTests
{
    [Theory]
    [InlineData(BrowserNativePage.History)]
    [InlineData(BrowserNativePage.Bookmarks)]
    [InlineData(BrowserNativePage.Downloads)]
    [InlineData(BrowserNativePage.Settings)]
    public async Task Builtin_native_tab_restores_same_identity_without_renderer_web_history_or_permission(BrowserNativePage page)
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var webTab = chrome.State.SelectedTabId;
        var original = Assert.Single(factory.Hosts);
        var opened = await chrome.OpenNativePageAsync(page);
        var id = opened.SelectedTabId;
        Assert.NotEqual(webTab, id);
        Assert.Equal(AppContentAddress.Parse(BrowserNativePages.Address(page).AbsoluteUri), opened.SelectedTab.Address);
        Assert.Equal(page, opened.SelectedTab.NativePage);
        Assert.Equal(BrowseEngineState.NativePage, opened.SelectedTab.EngineState);
        Assert.Null(chrome.ObserveOriginalEngine(id)); Assert.Single(factory.Hosts);
        Assert.Same(original, chrome.ObserveOriginalEngine(webTab)); Assert.Equal(0, original.CloseCalls);
        Assert.Empty(opened.History); Assert.Empty(opened.Permissions);
        Assert.Equal(BrowseTransportSecurity.NoPage, opened.Security.Transport);
        Assert.False(opened.SelectedTab.CanGoBack); Assert.False(opened.SelectedTab.CanGoForward);
        Assert.Equal(id, (await chrome.OpenNativePageAsync(page)).SelectedTabId);
        Assert.Equal(2, chrome.State.Tabs.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.SetPermissionAsync(BrowserSitePermissionKind.Camera, BrowserSitePermissionDecision.Allow));
        await chrome.DisposeAsync();
        await using var restored = await BrowseChrome.CreateAsync(paths);
        var tab = Assert.Single(restored.State.Tabs, item => item.Id == id);
        Assert.Equal(page, tab.NativePage); Assert.Equal(BrowseEngineState.NativePage, tab.EngineState);
        Assert.Empty(restored.State.History);
    }

    [Fact]
    public async Task Address_navigation_replaces_exact_renderer_and_can_return_same_tab_to_web()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var id = chrome.State.SelectedTabId; var original = Assert.Single(factory.Hosts);
        original.CommitFixturePage(new Uri("https://prior.example/"), "Prior committed page");
        // Intentionally do not wait for the asynchronous native history writer:
        // later canonical tab edits must win over that accepted earlier snapshot.
        await chrome.NavigateAsync("BROWSE://HISTORY");
        Assert.Equal(id, chrome.State.SelectedTabId); Assert.Equal(1, original.CloseCalls);
        Assert.Equal(BrowserNativePage.History, chrome.State.SelectedTab.NativePage);
        Assert.Single(chrome.State.History); Assert.Single(factory.Hosts);
        using (var stored = new BrowserDataService(paths))
            Assert.Equal("browse://history", Assert.Single(stored.Tabs).Address);
        await chrome.NavigateAsync("https://next.example/document");
        Assert.Equal(id, chrome.State.SelectedTabId); Assert.Equal(BrowserNativePage.None, chrome.State.SelectedTab.NativePage);
        Assert.Equal(BrowseEngineState.Ready, chrome.State.SelectedTab.EngineState);
        Assert.Equal(new Uri("https://next.example/document"), factory.Requests[^1].InitialAddress);
        Assert.Equal(2, factory.Hosts.Count); Assert.Single(chrome.State.History);
    }

    [Fact]
    public async Task Private_native_pages_remain_private_and_are_never_restored_or_reopened()
    {
        var paths = new PreservedPaths(); var chrome = await BrowseChrome.CreateAsync(paths);
        await chrome.NewTabAsync(true); await chrome.OpenNativePageAsync(BrowserNativePage.History);
        var id = chrome.State.SelectedTabId;
        Assert.Equal(BrowserTabPrivacy.Private, chrome.State.SelectedTab.Privacy);
        await chrome.CloseTabAsync(id); Assert.False(chrome.CanReopenClosedTab);
        await chrome.OpenNativePageAsync(BrowserNativePage.Settings);
        await chrome.DisposeAsync();
        await using var restored = await BrowseChrome.CreateAsync(paths);
        Assert.DoesNotContain(restored.State.Tabs, tab => tab.Privacy == BrowserTabPrivacy.Private || tab.NativePage != BrowserNativePage.None);
    }

    [Fact]
    public async Task Failed_web_engine_creation_preserves_original_native_page_for_retry()
    {
        var factory = new ChromiumFixtureFactory(includeGecko: true);
        var chrome = await BrowseChrome.CreateAsync(new PreservedPaths(), factory);
        RetainedFailedOwners.Add(chrome);
        await chrome.NavigateAsync("browse://bookmarks");
        var original = chrome.State.SelectedTab;
        factory.FailEngine = BrowseEngineKind.Gecko;
        await Assert.ThrowsAsync<IOException>(() => chrome.NavigateAsync("https://destination.example/"));
        Assert.Equal(original, chrome.State.SelectedTab); Assert.Null(chrome.ObserveOriginalEngine(original.Id));
        factory.FailEngine = null;
        await chrome.NavigateAsync("https://destination.example/");
        Assert.Equal(original.Id, chrome.State.SelectedTabId); Assert.Equal(BrowseEngineState.Ready, chrome.State.SelectedTab.EngineState);
        var retained = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(retained.Flatten().InnerExceptions, failure => failure is IOException);
    }

    [Theory]
    [InlineData("browse://history.evil/")]
    [InlineData("browse://history@evil/")]
    [InlineData("browse://evil@history/")]
    [InlineData("browse://history:80/")]
    [InlineData("browse://history/../settings")]
    [InlineData("browse://history/?origin=https://evil.example")]
    [InlineData("browse://history/#privilege")]
    [InlineData("browse://%68istory/")]
    [InlineData("browse://history\\settings")]
    [InlineData("cui://history/")]
    public async Task Unregistered_and_spoofed_native_routes_are_never_rendered_searched_or_persisted(string address)
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var original = chrome.State.SelectedTab;
        await chrome.NavigateAsync(address);
        Assert.Equal(original, chrome.State.SelectedTab); Assert.Single(factory.Requests); Assert.Empty(chrome.State.History);
        Assert.Contains("not registered", chrome.State.Status);
        using var data = new BrowserDataService(new PreservedPaths());
        await data.SaveTabsAsync([new(Guid.NewGuid(), "Untrusted", address, BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow)], CancellationToken.None);
        Assert.Empty(data.Tabs);
    }

    [Fact]
    public async Task Shared_tab_store_keeps_only_registered_native_and_web_tabs_with_private_state_excluded()
    {
        var paths = new PreservedPaths();
        using (var data = new BrowserDataService(paths))
        {
            await data.RecordVisitAsync("Public", "https://public.example/", false, CancellationToken.None);
            await data.RecordVisitAsync("Private", "https://private.example/", true, CancellationToken.None);
            await data.SaveTabsAsync([
                new(Guid.NewGuid(), "Public", "https://public.example/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "History", "browse://history/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "Private settings", "browse://settings/", BrowserTabPrivacy.Private, "", DateTimeOffset.UtcNow),
                new(Guid.NewGuid(), "Unregistered", "browse://plugins/", BrowserTabPrivacy.Standard, "", DateTimeOffset.UtcNow)
            ], CancellationToken.None);
        }
        using var restored = new BrowserDataService(paths);
        Assert.Equal(2, restored.Tabs.Count); Assert.Single(restored.History);
        Assert.Equal("Public", restored.History[0].Title);
        Assert.Contains(restored.Tabs, tab => tab.Address == "browse://history/");
        // Persisted input is also validated on load, not just on save.
        var file = Path.Combine(paths.DataDirectory, "browser-data.json");
        var content = await File.ReadAllTextAsync(file);
        await File.WriteAllTextAsync(file, content.Replace("browse://history/", "browse://history@evil/", StringComparison.Ordinal));
        using var tampered = new BrowserDataService(paths);
        Assert.Single(tampered.Tabs); Assert.Equal("https://public.example/", tampered.Tabs[0].Address);
    }
}
