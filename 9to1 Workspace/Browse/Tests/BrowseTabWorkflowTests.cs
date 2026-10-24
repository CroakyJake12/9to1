using System.Text.Json.Nodes;
using Haven.Browser;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeOwnershipTests
{
    [Fact]
    public async Task Search_terms_use_the_saved_search_template_but_explicit_web_addresses_remain_direct()
    {
        var paths = new PreservedPaths();
        using (var data = new BrowserDataService(paths))
        {
            await data.SaveSettingsAsync(data.Settings with
            {
                SearchTemplate = "https://search.example/results?query={query}"
            }, default);
        }

        var factory = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);

        await chrome.NavigateAsync("research and design + notes");
        Assert.Equal(new Uri("https://search.example/results?query=research%20and%20design%20%2B%20notes"),
            chrome.State.SelectedTab.Address.WebUri);

        await chrome.NavigateAsync("https://direct.example/path?q=keep");
        Assert.Equal(new Uri("https://direct.example/path?q=keep"), chrome.State.SelectedTab.Address.WebUri);
        await chrome.NavigateAsync("direct.example/article");
        Assert.Equal(new Uri("https://direct.example/article"), chrome.State.SelectedTab.Address.WebUri);
        Assert.Same(Assert.Single(factory.Hosts), chrome.ObserveOriginalEngine(chrome.State.SelectedTabId));
    }

    [Fact]
    public async Task Invalid_saved_search_template_refuses_without_falling_back_or_mutating_the_tab()
    {
        var paths = new PreservedPaths();
        using (var data = new BrowserDataService(paths))
        {
            await data.SaveSettingsAsync(data.Settings with { SearchTemplate = "javascript:{query}" }, default);
        }

        var factory = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var before = chrome.State;
        var renderer = Assert.Single(factory.Hosts);
        var rendererBefore = renderer.State;
        var publications = 0;
        chrome.StateChanged += (_, _) => publications++;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.NavigateAsync("ordinary search"));

        Assert.Equal("The configured browser search template must produce an absolute HTTP or HTTPS address.", error.Message);
        Assert.Equal(before.Tabs, chrome.State.Tabs);
        Assert.Equal(before.SelectedTabId, chrome.State.SelectedTabId);
        Assert.Same(renderer, chrome.ObserveOriginalEngine(before.SelectedTabId));
        Assert.Equal(rendererBefore, renderer.State);
        Assert.Empty(renderer.WebNavigationCalls);
        Assert.Empty(chrome.State.History);
        Assert.Equal(0, publications);
    }

    [Theory]
    [InlineData("9to1://write/private-document")]
    [InlineData("cui://extension/page")]
    [InlineData("browse://settings")]
    [InlineData("about:blank")]
    [InlineData("file:///private/document")]
    [InlineData("ftp://files.example/document")]
    [InlineData("mailto:person@example.test")]
    [InlineData("blob:https://example.test/id")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,untrusted")]
    [InlineData("relative/path")]
    [InlineData("not a valid address")]
    public async Task Opening_a_saved_history_row_refuses_nonweb_or_malformed_addresses_without_mutation(string address)
    {
        var paths = new PreservedPaths();
        var visitId = await SeedHistoryWithUntrustedAddressAsync(paths, address);

        var factory = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var before = chrome.State;
        var profilesBefore = before.Tabs.Select(tab => tab.ProfileId).ToArray();
        var renderer = Assert.Single(factory.Hosts);
        var rendererStateBefore = renderer.State;
        var requestCountBefore = factory.Requests.Count;
        var closeCallsBefore = renderer.CloseCalls;
        var sourcesBefore = chrome.OriginalNativeSources.ToArray();
        var publications = 0;
        chrome.StateChanged += (_, _) => publications++;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.OpenHistoryEntryAsync(visitId));

        Assert.Equal("A saved history visit must use HTTP or HTTPS.", error.Message);
        Assert.Equal(before.SelectedTabId, chrome.State.SelectedTabId);
        Assert.Equal(before.Tabs, chrome.State.Tabs);
        Assert.Equal(before.History, chrome.State.History);
        Assert.Equal(profilesBefore, chrome.State.Tabs.Select(tab => tab.ProfileId));
        Assert.Same(renderer, chrome.ObserveOriginalEngine(before.SelectedTabId));
        Assert.Equal(rendererStateBefore, renderer.State);
        Assert.Equal(requestCountBefore, factory.Requests.Count);
        Assert.Equal(closeCallsBefore, renderer.CloseCalls);
        Assert.Empty(renderer.WebNavigationCalls);
        Assert.Equal(sourcesBefore, chrome.OriginalNativeSources.ToArray());
        Assert.Equal(0, publications);
    }

    private static async Task<Guid> SeedHistoryWithUntrustedAddressAsync(PreservedPaths paths, string address)
    {
        // The public history writer accepts only absolute addresses. Emulate a
        // legacy/corrupt stored row to prove the open action still fails closed.
        Guid visitId;
        using (var data = new BrowserDataService(paths))
        {
            await data.RecordVisitAsync("History fixture", "https://history-fixture.example/", false, default);
            visitId = Assert.Single(data.History).Id;
        }

        var storePath = Path.Combine(paths.DataDirectory, "browser-data.json");
        var dataRoot = JsonNode.Parse(await File.ReadAllTextAsync(storePath))!;
        var row = dataRoot["history"]!.AsArray().Single()!;
        row["address"] = address;
        await File.WriteAllTextAsync(storePath, dataRoot.ToJsonString());
        return visitId;
    }

    [Fact]
    public async Task Opening_saved_history_does_not_bypass_embedded_credential_navigation_policy()
    {
        var paths = new PreservedPaths();
        Guid visitId;
        using (var data = new BrowserDataService(paths))
        {
            await data.RecordVisitAsync("Credential URL", "https://user:secret@example.test/", false, default);
            visitId = Assert.Single(data.History).Id;
        }

        var factory = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var before = chrome.State;
        var renderer = Assert.Single(factory.Hosts);
        var rendererStateBefore = renderer.State;
        var historyBefore = before.History.ToArray();
        var sourcesBefore = chrome.OriginalNativeSources.ToArray();
        var requestCountBefore = factory.Requests.Count;
        var publications = 0;
        chrome.StateChanged += (_, _) => publications++;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => chrome.OpenHistoryEntryAsync(visitId));

        Assert.Equal("Addresses containing embedded credentials are not allowed.", error.Message);
        Assert.Equal(before.SelectedTab, chrome.State.SelectedTab);
        Assert.Equal(before.Tabs, chrome.State.Tabs);
        Assert.Equal(historyBefore, chrome.State.History);
        Assert.Same(renderer, chrome.ObserveOriginalEngine(before.SelectedTabId));
        Assert.Equal(rendererStateBefore, renderer.State);
        Assert.Equal(requestCountBefore, factory.Requests.Count);
        Assert.Equal(sourcesBefore, chrome.OriginalNativeSources.ToArray());
        Assert.Empty(renderer.WebNavigationCalls);
        Assert.Equal(0, publications);
    }

    [Fact]
    public async Task Removed_history_identity_cannot_be_replayed_as_a_later_navigation()
    {
        var paths = new PreservedPaths();
        Guid visitId;
        using (var data = new BrowserDataService(paths))
        {
            await data.RecordVisitAsync("Removed visit", "https://removed-history.example/", false, default);
            visitId = Assert.Single(data.History).Id;
        }

        var factory = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var renderer = Assert.Single(factory.Hosts);
        await chrome.RemoveHistoryEntryAsync(visitId);
        var before = chrome.State.SelectedTab;

        await Assert.ThrowsAsync<ArgumentException>(() => chrome.OpenHistoryEntryAsync(visitId));

        Assert.Equal(before, chrome.State.SelectedTab);
        Assert.Empty(chrome.State.History);
        Assert.Same(renderer, chrome.ObserveOriginalEngine(before.Id));
        Assert.Empty(renderer.WebNavigationCalls);
    }

    // This deterministic fixture proves central policy/engine dispatch only; it is
    // not evidence of a real platform renderer or native acceptance.
    [Theory]
    [InlineData("http://saved-history.example/article")]
    [InlineData("https://saved-history.example/article")]
    public async Task Restored_web_history_entry_navigates_the_existing_tab_and_keeps_back_forward_on_its_engine(string address)
    {
        var paths = new PreservedPaths();
        Guid visitId;
        using (var data = new BrowserDataService(paths))
        {
            await data.RecordVisitAsync("Saved destination", address, false, default);
            visitId = Assert.Single(data.History).Id;
        }

        var factory = new ChromiumFixtureFactory(includeGecko: true);
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var originalTabId = chrome.State.SelectedTabId;
        var originalTabCount = chrome.State.Tabs.Count;
        var originalProfileId = chrome.State.SelectedTab.ProfileId;
        var renderer = Assert.Single(factory.Hosts);
        Assert.Equal(visitId, Assert.Single(chrome.State.History).Id);

        await chrome.OpenHistoryEntryAsync(visitId);

        Assert.Equal(originalTabId, chrome.State.SelectedTabId);
        Assert.Equal(originalTabCount, chrome.State.Tabs.Count);
        Assert.Equal(originalProfileId, chrome.State.SelectedTab.ProfileId);
        Assert.Equal(address, chrome.State.SelectedTab.Address.Value);
        Assert.Same(renderer, chrome.ObserveOriginalEngine(originalTabId));
        renderer.CommitFixturePage(new Uri(address), "Restored article", canGoBack: true);
        await Task.WhenAll(chrome.OriginalNativeSources.ToArray());
        Assert.Equal(2, chrome.State.History.Count);
        Assert.Contains(chrome.State.History, visit => visit.Id == visitId);
        Assert.True(chrome.State.SelectedTab.CanGoBack);
        await chrome.BackAsync();
        Assert.False(chrome.State.SelectedTab.CanGoBack);
        Assert.True(chrome.State.SelectedTab.CanGoForward);
        await chrome.ForwardAsync();
        Assert.True(chrome.State.SelectedTab.CanGoBack);
        Assert.False(chrome.State.SelectedTab.CanGoForward);
        Assert.Same(renderer, chrome.ObserveOriginalEngine(originalTabId));
        Assert.Equal(new[] { "back", "forward" }, renderer.WebNavigationCalls);
        Assert.Equal(originalTabId, chrome.State.SelectedTabId);
        Assert.Equal(2, chrome.State.History.Count);
        Assert.Contains(chrome.State.History, visit => visit.Id == visitId);
    }

    [Fact]
    public async Task Cancelled_history_navigation_leaves_the_selected_document_and_visit_available()
    {
        var paths = new PreservedPaths();
        Guid visitId;
        using (var data = new BrowserDataService(paths))
        {
            await data.RecordVisitAsync("Saved destination", "https://saved-history.example/article", false, default);
            visitId = Assert.Single(data.History).Id;
        }

        var factory = new ChromiumFixtureFactory(includeGecko: true);
        await using (var chrome = await BrowseChrome.CreateAsync(paths, factory))
        {
            var before = chrome.State.SelectedTab;
            var renderer = Assert.Single(factory.Hosts);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => chrome.OpenHistoryEntryAsync(visitId, cancellation.Token));

            Assert.Equal(before, chrome.State.SelectedTab);
            Assert.Same(renderer, chrome.ObserveOriginalEngine(before.Id));
            Assert.Empty(renderer.WebNavigationCalls);
            Assert.Equal(visitId, Assert.Single(chrome.State.History).Id);
        }

        await using var restored = await BrowseChrome.CreateAsync(paths, new ChromiumFixtureFactory(includeGecko: true));
        Assert.Equal(visitId, Assert.Single(restored.State.History).Id);
    }

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
