using Haven.Browser;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeOwnershipTests
{
    [Fact]
    public async Task Address_navigation_resolves_site_engine_without_reloading_old_site_or_changing_tab_id()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        await new BrowseEnginePreferencesStore(paths).SaveAsync(new(BrowseEngineKind.Gecko,
            new Dictionary<string, BrowseEngineKind> { ["chromium.example"] = BrowseEngineKind.Chromium }));
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var originalId = chrome.State.SelectedTabId; var originalHost = Assert.Single(factory.Hosts);

        await chrome.NavigateAsync("chromium.example/page");

        Assert.Equal(originalId, chrome.State.SelectedTabId);
        Assert.Equal(BrowseEngineKind.Chromium, chrome.State.SelectedTab.Engine);
        Assert.Equal(new Uri("https://chromium.example/page"), factory.Requests[1].InitialAddress);
        Assert.Equal(1, originalHost.CloseCalls);
        Assert.Empty(chrome.State.History);
        Assert.Empty((await new BrowseEnginePreferencesStore(paths).LoadAsync()).TabOverrides!);

        await chrome.NavigateAsync("https://firefox.example/return");
        Assert.Equal(originalId, chrome.State.SelectedTabId);
        Assert.Equal(BrowseEngineKind.Gecko, chrome.State.SelectedTab.Engine);
        Assert.Equal(new Uri("https://firefox.example/return"), factory.Requests[2].InitialAddress);
        Assert.NotEqual(factory.Requests[0].ProfileDirectory, factory.Requests[1].ProfileDirectory);
        Assert.Equal(factory.Requests[0].ProfileDirectory, factory.Requests[2].ProfileDirectory);
    }

    [Fact]
    public async Task Explicit_tab_override_remains_authoritative_over_destination_site_engine()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        await new BrowseEnginePreferencesStore(paths).SaveAsync(new(BrowseEngineKind.Gecko,
            new Dictionary<string, BrowseEngineKind> { ["gecko.example"] = BrowseEngineKind.Gecko }));
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        var originalHost = factory.Hosts[1];
        await chrome.NavigateAsync("https://gecko.example/page");
        Assert.Same(originalHost, chrome.ObserveOriginalEngine(chrome.State.SelectedTabId));
        Assert.Equal(2, factory.Requests.Count);
        Assert.Equal(new Uri("https://gecko.example/page"), chrome.State.SelectedTab.Address.WebUri);
    }

    [Fact]
    public async Task Unavailable_site_engine_keeps_destination_then_available_site_can_recover_same_tab()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        await new BrowseEnginePreferencesStore(paths).SaveAsync(new(BrowseEngineKind.Chromium,
            new Dictionary<string, BrowseEngineKind> { ["firefox.example"] = BrowseEngineKind.Gecko }));
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var originalId = chrome.State.SelectedTabId;
        await chrome.NavigateAsync("https://firefox.example/page");
        Assert.Equal(BrowseEngineKind.Gecko, chrome.State.SelectedTab.Engine);
        Assert.Equal(BrowseEngineState.Unsupported, chrome.State.SelectedTab.EngineState);
        Assert.Equal(new Uri("https://firefox.example/page"), chrome.State.SelectedTab.Address.WebUri);
        Assert.Empty(chrome.State.History);
        await chrome.NavigateAsync("https://available.example/page");
        Assert.Equal(originalId, chrome.State.SelectedTabId);
        Assert.Equal(BrowseEngineState.Ready, chrome.State.SelectedTab.EngineState);
        Assert.Equal(BrowseEngineKind.Chromium, chrome.State.SelectedTab.Engine);
        Assert.Equal(new Uri("https://available.example/page"), factory.Requests.Last().InitialAddress);
    }

    [Fact]
    public async Task Rejected_navigation_does_not_retire_renderer_or_rewrite_engine_preference()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        await new BrowseEnginePreferencesStore(paths).SaveAsync(new(BrowseEngineKind.Gecko,
            new Dictionary<string, BrowseEngineKind> { ["chromium.example"] = BrowseEngineKind.Chromium }));
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var original = chrome.State.SelectedTab; var host = Assert.Single(factory.Hosts);
        await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.NavigateAsync("https://user:secret@chromium.example/"));
        Assert.Equal(original, chrome.State.SelectedTab);
        Assert.Equal(0, host.CloseCalls);
        Assert.Single(factory.Requests);
    }

    [Fact]
    public async Task Failed_engine_start_restores_same_tab_and_previous_renderer_backend()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var original = chrome.State.SelectedTab; var host = Assert.Single(factory.Hosts);
        factory.FailEngine = BrowseEngineKind.Chromium;
        await Assert.ThrowsAsync<IOException>(() => chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium));
        Assert.Equal(original.Id, chrome.State.SelectedTabId);
        Assert.Equal(original.Address, chrome.State.SelectedTab.Address);
        Assert.Equal(BrowseEngineKind.Gecko, chrome.State.SelectedTab.Engine);
        Assert.Equal(BrowseEngineState.Ready, chrome.State.SelectedTab.EngineState);
        Assert.Equal(1, host.CloseCalls);
        Assert.Same(factory.Hosts.Last(), chrome.ObserveOriginalEngine(original.Id));
        Assert.NotSame(host, chrome.ObserveOriginalEngine(original.Id));
    }

    [Fact]
    public async Task Failed_preference_save_retires_new_renderer_before_recreating_previous_backend()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        var chrome = await BrowseChrome.CreateAsync(paths, factory); RetainedFailedOwners.Add(chrome);
        Directory.CreateDirectory(Path.Combine(paths.DataDirectory, "browse-engine-preferences.json"));
        await Assert.ThrowsAnyAsync<IOException>(() => chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium));
        Assert.Equal(3, factory.Hosts.Count);
        Assert.Equal(1, factory.Hosts[0].CloseCalls);
        Assert.Equal(1, factory.Hosts[1].CloseCalls);
        Assert.Equal(BrowseEngineKind.Gecko, chrome.State.SelectedTab.Engine);
        Assert.Same(factory.Hosts[2], chrome.ObserveOriginalEngine(chrome.State.SelectedTabId));
        // The original persistence failure remains part of retirement evidence.
        await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Equal(1, factory.Hosts[2].CloseCalls);
    }

    [Fact]
    public async Task Switching_tabs_preserves_each_tabs_find_query_and_observed_page_zoom()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        await new BrowseEnginePreferencesStore(paths).SaveAsync(new(BrowseEngineKind.Chromium, new Dictionary<string, BrowseEngineKind>()));
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var first = chrome.State.SelectedTabId;
        await chrome.FindAsync("first query", false); await chrome.SetZoomAsync(150);
        var second = (await chrome.NewTabAsync(false)).SelectedTabId;
        Assert.Equal(100, chrome.State.ZoomPercent); Assert.Empty(chrome.State.FindQuery);
        await chrome.FindAsync("second query", false); await chrome.SetZoomAsync(80);
        await chrome.SelectTabAsync(first);
        Assert.Equal(150, chrome.State.ZoomPercent); Assert.Equal("first query", chrome.State.FindQuery);
        await chrome.SelectTabAsync(second);
        Assert.Equal(80, chrome.State.ZoomPercent); Assert.Equal("second query", chrome.State.FindQuery);
        await chrome.NavigateAsync("https://new.example/page");
        Assert.Equal(80, chrome.State.ZoomPercent); // Requested navigation is not a new document yet.
        factory.Hosts[1].CommitFixturePage(new Uri("https://new.example/page"), "New document");
        await Task.WhenAll(chrome.OriginalNativeSources.ToArray());
        Assert.Equal(100, chrome.State.ZoomPercent);
        await chrome.SelectTabAsync(first);
        Assert.Equal(150, chrome.State.ZoomPercent);
    }

    [Fact]
    public async Task Reset_tab_override_returns_same_tab_to_persisted_site_policy()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var originalId = chrome.State.SelectedTabId;
        await chrome.SetSiteEngineAsync(BrowseEngineKind.Chromium);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Gecko);
        Assert.Equal(BrowseEngineKind.Gecko, chrome.State.SelectedTab.Engine);
        await chrome.ResetSelectedTabEngineAsync();
        Assert.Equal(originalId, chrome.State.SelectedTabId);
        Assert.Equal(BrowseEngineKind.Chromium, chrome.State.SelectedTab.Engine);
        var persisted = await new BrowseEnginePreferencesStore(paths).LoadAsync();
        Assert.Empty(persisted.TabOverrides!); Assert.Single(persisted.SiteOverrides);
    }

    [Fact]
    public async Task Cancelled_tab_close_keeps_live_renderer_and_canonical_session()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        var original = chrome.State.SelectedTab; var host = Assert.Single(factory.Hosts);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chrome.CloseTabAsync(original.Id, cancellation.Token));
        Assert.Equal(original, chrome.State.SelectedTab);
        Assert.Same(host, chrome.ObserveOriginalEngine(original.Id)); Assert.Equal(0, host.CloseCalls);
    }

    [Fact]
    public async Task Cancelled_new_tab_cannot_create_unavailable_or_private_ghost_tabs()
    {
        var paths = new PreservedPaths();
        await using var chrome = await BrowseChrome.CreateAsync(paths);
        var original = chrome.State.SelectedTab;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chrome.NewTabAsync(false, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chrome.NewTabAsync(true, cancellation.Token));
        Assert.Equal(original, Assert.Single(chrome.State.Tabs));
    }

    [Fact]
    public async Task Failed_page_script_does_not_publish_zoom_or_find_success()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        await chrome.SetZoomAsync(120); await chrome.FindAsync("existing", false);
        factory.Hosts[0].ScriptFailure = new IOException("Fixture page script failed.");
        await Assert.ThrowsAsync<IOException>(() => chrome.SetZoomAsync(180));
        await Assert.ThrowsAsync<IOException>(() => chrome.FindAsync("failed replacement", false));
        Assert.Equal(120, chrome.State.ZoomPercent); Assert.Equal("existing", chrome.State.FindQuery);
    }
}
