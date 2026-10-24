using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

// Explicit deterministic engine fixture: these tests certify Browse custody,
// persistence and routing, not the platform renderer or installed authority.
public sealed partial class BrowseNativeOwnershipTests
{
    private static readonly List<BrowseChrome> RetainedFailedOwners = [];
    [Fact]
    public async Task Chromium_supplier_does_not_certify_default_Firefox_and_switch_keeps_original_tab()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var original = chrome.State.SelectedTab;
        Assert.Equal(BrowseEngineKind.Gecko, original.Engine);
        Assert.Equal(BrowseEngineState.Unsupported, original.EngineState);
        Assert.Empty(factory.Requests);
        var switched = await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        Assert.Equal(original.Id, switched.SelectedTabId);
        Assert.Equal(BrowseEngineState.Ready, switched.SelectedTab.EngineState);
        var request = Assert.Single(factory.Requests);
        Assert.Equal(original.Id, request.TabId);
        Assert.Equal(Path.Combine(paths.BrowserProfileDirectory, "engines", "chromium"), request.ProfileDirectory);
        Assert.Same(factory.Hosts[0], chrome.ObserveOriginalEngine(original.Id));
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Gecko);
        Assert.Equal(original.Id, chrome.State.SelectedTabId);
        Assert.Equal(BrowseEngineState.Unsupported, chrome.State.SelectedTab.EngineState);
        Assert.Equal(1, factory.Hosts[0].CloseCalls);
    }

    [Fact]
    public async Task Only_actual_committed_source_records_history_and_restores_same_tab()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var tabId = chrome.State.SelectedTabId;
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        await chrome.NavigateAsync("https://requested.example/path");
        Assert.Empty(chrome.State.History);
        factory.Hosts[0].CommitFixturePage(new Uri("https://committed.example/final"), "Committed title");
        var actualSources = chrome.OriginalNativeSources.ToArray();
        Assert.NotEmpty(actualSources); await Task.WhenAll(actualSources);
        var history = Assert.Single(chrome.State.History);
        Assert.Equal("https://committed.example/final", history.Address);
        Assert.Equal("Committed title", history.Title);
        Assert.Equal(new Uri(history.Address), chrome.State.SelectedTab.Address.WebUri);
        var close = chrome.DisposeAsync().AsTask(); await close;
        Assert.Same(close, chrome.OriginalClose);
        await using var restored = await BrowseChrome.CreateAsync(paths);
        Assert.Equal(tabId, restored.State.SelectedTabId);
        Assert.Equal(new Uri(history.Address), restored.State.SelectedTab.Address.WebUri);
        Assert.Equal(history, Assert.Single(restored.State.History));
    }

    [Fact]
    public async Task Same_document_navigation_persists_the_current_tab_address_without_fabricating_a_page_commit()
    {
        var paths = new PreservedPaths();
        var factory = new ChromiumFixtureFactory(includeGecko: true);
        var chrome = await BrowseChrome.CreateAsync(paths, factory);
        RetainedFailedOwners.Add(chrome);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        var tabId = chrome.State.SelectedTabId;
        var host = Assert.Single(factory.Hosts);
        host.CommitFixturePage(new Uri("https://same-document.example/start"), "Initial document");
        await Task.WhenAll(chrome.OriginalNativeSources.ToArray());
        var pageHistory = Assert.Single(chrome.State.History);

        host.NavigateWithinDocumentFixture(new Uri("https://same-document.example/start#section"));
        await Task.WhenAll(chrome.OriginalNativeSources.ToArray());
        Assert.Equal(new Uri("https://same-document.example/start#section"), chrome.State.SelectedTab.Address.WebUri);
        Assert.Equal(pageHistory, Assert.Single(chrome.State.History));

        // Restore while the source is still open: the same-document address must
        // already be in the canonical session, not only saved by a later close.
        await using var restored = await BrowseChrome.CreateAsync(paths, new ChromiumFixtureFactory(includeGecko: true));
        Assert.Equal(tabId, restored.State.SelectedTabId);
        Assert.Equal(new Uri("https://same-document.example/start#section"), restored.State.SelectedTab.Address.WebUri);
        Assert.Equal(pageHistory, Assert.Single(restored.State.History));
        await chrome.DisposeAsync();
        RetainedFailedOwners.Remove(chrome);
    }

    [Fact]
    public async Task Private_commit_uses_private_profile_and_never_persists_visit_or_private_tab()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(paths, factory);
        await chrome.SetDefaultEngineAsync(BrowseEngineKind.Chromium);
        var tab = (await chrome.NewTabAsync(true)).SelectedTab;
        var request = Assert.Single(factory.Requests);
        Assert.Equal(BrowserTabPrivacy.Private, request.Privacy);
        Assert.NotEqual(Path.Combine(paths.BrowserProfileDirectory, "engines", "chromium"), request.ProfileDirectory);
        factory.Hosts[0].CommitFixturePage(new Uri("https://private.example/secret"), "Private title");
        await Task.WhenAll(chrome.OriginalNativeSources.ToArray());
        Assert.Empty(chrome.State.History);
        await chrome.DisposeAsync();
        await using var restored = await BrowseChrome.CreateAsync(paths);
        Assert.Empty(restored.State.History);
        Assert.DoesNotContain(restored.State.Tabs, item => item.Id == tab.Id || item.Privacy == BrowserTabPrivacy.Private);
    }

    [Fact]
    public async Task Permission_observation_requires_same_current_engine_and_origin()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        var host = factory.Hosts[0];
        host.CommitFixturePage(new Uri("https://allowed.example/path"), "Allowed");
        await Task.WhenAll(chrome.OriginalNativeSources.ToArray());
        await chrome.SetPermissionAsync(BrowserSitePermissionKind.Camera, BrowserSitePermissionDecision.Allow);
        var id = chrome.State.SelectedTabId;
        Assert.Equal(BrowserSitePermissionDecision.Allow, chrome.ObserveOriginPermission(id, host, new Uri("https://allowed.example/other"), BrowserSitePermissionKind.Camera));
        Assert.Equal(BrowserSitePermissionDecision.Deny, chrome.ObserveOriginPermission(id, host, new Uri("https://foreign.example/"), BrowserSitePermissionKind.Camera));
        Assert.Equal(BrowserSitePermissionDecision.Deny, chrome.ObserveOriginPermission(id, new FixtureTab(factory.Requests[0]), new Uri("https://allowed.example/"), BrowserSitePermissionKind.Camera));
    }

    [Fact]
    public async Task Original_history_write_failure_is_retained_through_same_failed_close()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(paths, factory); RetainedFailedOwners.Add(chrome);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        // A fixture-owned directory occupies the actual shared store target.
        // No source or stored data is removed to manufacture the failure.
        Directory.CreateDirectory(Path.Combine(paths.DataDirectory, "browser-data.json"));
        factory.Hosts[0].CommitFixturePage(new Uri("https://failure.example/"), "Failure");
        var originalSources = chrome.OriginalNativeSources; Assert.NotEmpty(originalSources);
        var original = originalSources[0];
        await Assert.ThrowsAnyAsync<Exception>(() => original);
        Assert.True(original.IsFaulted);
        var close = chrome.DisposeAsync().AsTask();
        await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Same(close, chrome.DisposeAsync().AsTask());
        Assert.Same(close, chrome.OriginalClose);
        Assert.Contains(original, chrome.OriginalNativeSources);
        Assert.Single(chrome.State.Tabs);
    }

    [Fact]
    public async Task Failed_original_engine_close_keeps_same_renderer_tab_and_close_task()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        var chrome = await BrowseChrome.CreateAsync(paths, factory); RetainedFailedOwners.Add(chrome);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        var host = factory.Hosts[0]; var failure = new IOException("Original fixture renderer close failed.");
        host.OriginalCloseSource.SetException(failure);
        var originalClose = chrome.DisposeAsync().AsTask();
        await Assert.ThrowsAsync<AggregateException>(() => originalClose);
        Assert.Same(originalClose, chrome.DisposeAsync().AsTask());
        Assert.Same(host, chrome.ObserveOriginalEngine(chrome.State.SelectedTabId));
        Assert.Contains(host.OriginalCloseSource.Task, chrome.OriginalNativeSources);
        Assert.Equal(1, host.CloseCalls);
        Assert.Same(failure, host.OriginalCloseSource.Task.Exception!.InnerException);
    }

    [Fact]
    public async Task Site_preference_reset_removes_only_site_policy_and_returns_same_tab_to_default()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        var originalId = chrome.State.SelectedTabId;
        await chrome.SetSiteEngineAsync(BrowseEngineKind.Chromium);
        Assert.Equal(BrowseEngineKind.Chromium, chrome.State.SelectedTab.Engine);
        var configured = await new BrowseEnginePreferencesStore(paths).LoadAsync(CancellationToken.None);
        Assert.Single(configured.SiteOverrides); Assert.DoesNotContain(originalId, configured.TabOverrides!.Keys);
        await chrome.ResetSiteEngineAsync();
        Assert.Equal(originalId, chrome.State.SelectedTabId);
        Assert.Equal(BrowseEngineKind.Gecko, chrome.State.SelectedTab.Engine);
        Assert.Equal(BrowseEngineState.Unsupported, chrome.State.SelectedTab.EngineState);
        var reset = await new BrowseEnginePreferencesStore(paths).LoadAsync(CancellationToken.None);
        Assert.Empty(reset.SiteOverrides); Assert.DoesNotContain(originalId, reset.TabOverrides!.Keys);
    }

    [Fact]
    public async Task Failed_site_reset_save_restores_site_rule_and_keeps_tab_override_and_renderer()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        var chrome = await BrowseChrome.CreateAsync(paths, factory); RetainedFailedOwners.Add(chrome);
        var originalId = chrome.State.SelectedTabId;
        await chrome.SetSiteEngineAsync(BrowseEngineKind.Chromium);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Gecko);
        var original = chrome.State.SelectedTab;
        var originalRenderer = Assert.IsType<FixtureTab>(chrome.ObserveOriginalEngine(originalId));
        var preferencesPath = Path.Combine(paths.DataDirectory, "browse-engine-preferences.json");
        var preferencesBytes = await File.ReadAllBytesAsync(preferencesPath);

        // Replace only this test-owned preferences file with an obstruction at its
        // exact target so the real atomic writer fails after the in-memory removal.
        File.Delete(preferencesPath);
        Directory.CreateDirectory(preferencesPath);
        Exception? failure = null;
        try
        {
            failure = await Assert.ThrowsAnyAsync<Exception>(() => chrome.ResetSiteEngineAsync());
        }
        finally
        {
            Directory.Delete(preferencesPath);
            await File.WriteAllBytesAsync(preferencesPath, preferencesBytes);
        }

        Assert.NotNull(failure);
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.GetType().FullName ?? "null");
        Assert.Equal(original, chrome.State.SelectedTab);
        Assert.Same(originalRenderer, chrome.ObserveOriginalEngine(originalId));
        Assert.Equal(0, originalRenderer.CloseCalls);

        // The site override was restored in memory despite this tab's explicit
        // Gecko choice; a sibling without a tab override must still resolve Chromium.
        var sibling = await chrome.NewTabAsync(false);
        Assert.NotEqual(originalId, sibling.SelectedTabId);
        Assert.Equal(BrowseEngineKind.Chromium, sibling.SelectedTab.Engine);
        await chrome.SelectTabAsync(originalId);
        Assert.Same(originalRenderer, chrome.ObserveOriginalEngine(originalId));

        var persisted = await new BrowseEnginePreferencesStore(paths).LoadAsync();
        Assert.Equal(BrowseEngineKind.Chromium, persisted.SiteOverrides[original.Address.WebUri!.IdnHost]);
        Assert.Equal(BrowseEngineKind.Gecko, persisted.TabOverrides![originalId]);
        Assert.Equal(preferencesBytes, await File.ReadAllBytesAsync(preferencesPath));
        var shutdown = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure!, shutdown.Flatten().InnerExceptions);
        RetainedFailedOwners.Remove(chrome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_site_preference_save_restores_the_exact_previous_rule_and_renderer(bool hadPreviousSiteRule)
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        var chrome = await BrowseChrome.CreateAsync(paths, factory); RetainedFailedOwners.Add(chrome);
        var originalId = chrome.State.SelectedTabId;
        var address = chrome.State.SelectedTab.Address.WebUri!;
        if (hadPreviousSiteRule) await chrome.SetSiteEngineAsync(BrowseEngineKind.Chromium);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Gecko);
        var original = chrome.State.SelectedTab;
        var originalRenderer = Assert.IsType<FixtureTab>(chrome.ObserveOriginalEngine(originalId));
        var preferencesPath = Path.Combine(paths.DataDirectory, "browse-engine-preferences.json");
        var preferencesBytes = await File.ReadAllBytesAsync(preferencesPath);

        // Obstruct only this test-owned atomic-write destination so persistence
        // fails after the proposed in-memory change but before renderer switching.
        File.Delete(preferencesPath);
        Directory.CreateDirectory(preferencesPath);
        Exception? saveFailure = null;
        try
        {
            saveFailure = await Assert.ThrowsAnyAsync<Exception>(() => chrome.SetSiteEngineAsync(
                hadPreviousSiteRule ? BrowseEngineKind.Gecko : BrowseEngineKind.Chromium));
        }
        finally
        {
            Directory.Delete(preferencesPath);
            await File.WriteAllBytesAsync(preferencesPath, preferencesBytes);
        }

        Assert.Equal(original, chrome.State.SelectedTab);
        Assert.Same(originalRenderer, chrome.ObserveOriginalEngine(originalId));
        Assert.Equal(0, originalRenderer.CloseCalls);
        var sibling = await chrome.NewTabAsync(false);
        Assert.Equal(hadPreviousSiteRule ? BrowseEngineKind.Chromium : BrowseEngineKind.Gecko, sibling.SelectedTab.Engine);
        await chrome.SelectTabAsync(originalId);
        Assert.Same(originalRenderer, chrome.ObserveOriginalEngine(originalId));

        var persisted = await new BrowseEnginePreferencesStore(paths).LoadAsync();
        if (hadPreviousSiteRule) Assert.Equal(BrowseEngineKind.Chromium, persisted.SiteOverrides[address.IdnHost]);
        else Assert.Empty(persisted.SiteOverrides);
        Assert.Equal(BrowseEngineKind.Gecko, persisted.TabOverrides![originalId]);
        Assert.Equal(preferencesBytes, await File.ReadAllBytesAsync(preferencesPath));
        Assert.NotNull(saveFailure);
        Assert.True(saveFailure is IOException or UnauthorizedAccessException, saveFailure?.GetType().FullName ?? "null");
        var shutdown = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(saveFailure!, shutdown.Flatten().InnerExceptions);
        RetainedFailedOwners.Remove(chrome);
    }

    [Fact]
    public async Task Failed_default_engine_save_restores_the_previous_choice_for_new_tabs()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        var chrome = await BrowseChrome.CreateAsync(paths, factory);
        RetainedFailedOwners.Add(chrome);
        var original = chrome.State.SelectedTab;
        var renderer = Assert.Single(factory.Hosts);
        var preferencesPath = Path.Combine(paths.DataDirectory, "browse-engine-preferences.json");
        Directory.CreateDirectory(preferencesPath);

        Exception? failure = null;
        try
        {
            failure = await Assert.ThrowsAnyAsync<Exception>(
                () => chrome.SetDefaultEngineAsync(BrowseEngineKind.Chromium));
        }
        finally { Directory.Delete(preferencesPath); }

        Assert.NotNull(failure);
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.GetType().FullName ?? "null");
        Assert.False(File.Exists(preferencesPath));
        Assert.Equal(original, chrome.State.SelectedTab);
        Assert.Same(renderer, chrome.ObserveOriginalEngine(original.Id));
        Assert.Equal(0, renderer.CloseCalls);
        var sibling = await chrome.NewTabAsync(false);
        Assert.Equal(BrowseEngineKind.Gecko, sibling.SelectedTab.Engine);
        Assert.NotEqual(original.Id, sibling.SelectedTabId);

        var shutdown = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure!, shutdown.Flatten().InnerExceptions);
        RetainedFailedOwners.Remove(chrome);
    }

    [Fact]
    public async Task Failed_same_engine_tab_override_save_restores_site_precedence()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        var chrome = await BrowseChrome.CreateAsync(paths, factory); RetainedFailedOwners.Add(chrome);
        var tabId = chrome.State.SelectedTabId;
        await chrome.SetSiteEngineAsync(BrowseEngineKind.Chromium);
        var before = chrome.State.SelectedTab;
        var renderer = Assert.IsType<FixtureTab>(chrome.ObserveOriginalEngine(tabId));
        var preferencesPath = Path.Combine(paths.DataDirectory, "browse-engine-preferences.json");
        var preferencesBytes = await File.ReadAllBytesAsync(preferencesPath);
        File.Delete(preferencesPath);
        Directory.CreateDirectory(preferencesPath);

        Exception? failure = null;
        try
        {
            failure = await Assert.ThrowsAnyAsync<Exception>(
                () => chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium));
        }
        finally
        {
            Directory.Delete(preferencesPath);
            await File.WriteAllBytesAsync(preferencesPath, preferencesBytes);
        }

        Assert.NotNull(failure);
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.GetType().FullName ?? "null");
        Assert.Equal(before, chrome.State.SelectedTab);
        Assert.Same(renderer, chrome.ObserveOriginalEngine(tabId));
        Assert.Equal(0, renderer.CloseCalls);
        Assert.Equal(preferencesBytes, await File.ReadAllBytesAsync(preferencesPath));

        // The failed same-engine click must not leave a hidden tab override that
        // masks a subsequently changed site rule.
        await chrome.SetSiteEngineAsync(BrowseEngineKind.Gecko);
        Assert.Equal(tabId, chrome.State.SelectedTabId);
        Assert.Equal(BrowseEngineKind.Gecko, chrome.State.SelectedTab.Engine);
        var persisted = await new BrowseEnginePreferencesStore(paths).LoadAsync();
        Assert.Equal(BrowseEngineKind.Gecko, persisted.SiteOverrides[before.Address.WebUri!.IdnHost]);
        Assert.DoesNotContain(tabId, persisted.TabOverrides!.Keys);

        var shutdown = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure!, shutdown.Flatten().InnerExceptions);
        RetainedFailedOwners.Remove(chrome);
    }

    [Fact]
    public async Task Site_reset_preserves_existing_explicit_tab_override_and_native_renderer()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        var originalId = chrome.State.SelectedTabId; var originalRenderer = Assert.Single(factory.Hosts);
        await chrome.SetSiteEngineAsync(BrowseEngineKind.Gecko);
        await chrome.ResetSiteEngineAsync();
        Assert.Equal(BrowseEngineKind.Chromium, chrome.State.SelectedTab.Engine);
        Assert.Same(originalRenderer, chrome.ObserveOriginalEngine(originalId));
        var reset = await new BrowseEnginePreferencesStore(paths).LoadAsync(CancellationToken.None);
        Assert.Empty(reset.SiteOverrides); Assert.Equal(BrowseEngineKind.Chromium, reset.TabOverrides![originalId]);
    }

    [Fact]
    public async Task Failed_selected_tab_reset_save_restores_tab_override_and_site_precedence()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory(includeGecko: true);
        var chrome = await BrowseChrome.CreateAsync(paths, factory); RetainedFailedOwners.Add(chrome);
        var tabId = chrome.State.SelectedTabId;
        await chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium);
        var original = chrome.State.SelectedTab;
        var renderer = Assert.IsType<FixtureTab>(chrome.ObserveOriginalEngine(tabId));
        var preferencesPath = Path.Combine(paths.DataDirectory, "browse-engine-preferences.json");
        var preferencesBytes = await File.ReadAllBytesAsync(preferencesPath);
        File.Delete(preferencesPath);
        Directory.CreateDirectory(preferencesPath);

        Exception? failure = null;
        try
        {
            failure = await Assert.ThrowsAnyAsync<Exception>(() => chrome.ResetSelectedTabEngineAsync());
        }
        finally
        {
            Directory.Delete(preferencesPath);
            await File.WriteAllBytesAsync(preferencesPath, preferencesBytes);
        }

        Assert.NotNull(failure);
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.GetType().FullName ?? "null");
        Assert.Equal(original, chrome.State.SelectedTab);
        Assert.Same(renderer, chrome.ObserveOriginalEngine(tabId));
        Assert.Equal(0, renderer.CloseCalls);
        Assert.Equal(preferencesBytes, await File.ReadAllBytesAsync(preferencesPath));

        await chrome.SetSiteEngineAsync(BrowseEngineKind.Gecko);
        Assert.Equal(BrowseEngineKind.Chromium, chrome.State.SelectedTab.Engine);
        Assert.Same(renderer, chrome.ObserveOriginalEngine(tabId));
        var persisted = await new BrowseEnginePreferencesStore(paths).LoadAsync();
        Assert.Equal(BrowseEngineKind.Gecko, persisted.SiteOverrides[original.Address.WebUri!.IdnHost]);
        Assert.Equal(BrowseEngineKind.Chromium, persisted.TabOverrides![tabId]);

        var shutdown = await Assert.ThrowsAsync<AggregateException>(() => chrome.DisposeAsync().AsTask());
        Assert.Contains(failure!, shutdown.Flatten().InnerExceptions);
        RetainedFailedOwners.Remove(chrome);
    }

    [Theory]
    [InlineData("https://other.example/")]
    [InlineData("http://allowed.example/")]
    [InlineData("https://allowed.example:8443/")]
    public async Task Private_permission_grants_are_scoped_to_the_original_origin(string otherOrigin)
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        await chrome.SetDefaultEngineAsync(BrowseEngineKind.Chromium);
        var tab = (await chrome.NewTabAsync(true)).SelectedTab;
        var host = Assert.Single(factory.Hosts);
        host.CommitFixturePage(new Uri("https://allowed.example/first"), "Private");
        await Task.WhenAll(chrome.OriginalNativeSources.ToArray());
        await chrome.SetPermissionAsync(BrowserSitePermissionKind.Camera, BrowserSitePermissionDecision.Allow);
        Assert.Equal(BrowserSitePermissionDecision.Allow,
            chrome.ObserveOriginPermission(tab.Id, host, new Uri("https://allowed.example/second"), BrowserSitePermissionKind.Camera));

        host.CommitFixturePage(new Uri(otherOrigin), "Other private origin");
        await Task.WhenAll(chrome.OriginalNativeSources.ToArray());
        Assert.Equal(BrowserSitePermissionDecision.Ask,
            chrome.ObserveOriginPermission(tab.Id, host, new Uri(otherOrigin), BrowserSitePermissionKind.Camera));
        await chrome.SetPermissionAsync(BrowserSitePermissionKind.Camera, BrowserSitePermissionDecision.Deny);

        host.CommitFixturePage(new Uri("https://allowed.example/return"), "Original private origin");
        await Task.WhenAll(chrome.OriginalNativeSources.ToArray());
        Assert.Equal(BrowserSitePermissionDecision.Allow,
            chrome.ObserveOriginPermission(tab.Id, host, new Uri("https://allowed.example/"), BrowserSitePermissionKind.Camera));
        Assert.Empty(chrome.State.Permissions);
        Assert.False(File.Exists(Path.Combine(paths.DataDirectory, "browser-site-permissions.json")));
    }

    [Fact]
    public async Task Cancelled_private_permission_does_not_grant_access()
    {
        var paths = new PreservedPaths(); var factory = new ChromiumFixtureFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory);
        await chrome.SetDefaultEngineAsync(BrowseEngineKind.Chromium);
        var tab = (await chrome.NewTabAsync(true)).SelectedTab;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chrome.SetPermissionAsync(
            BrowserSitePermissionKind.Camera, BrowserSitePermissionDecision.Allow, cancelled.Token));
        Assert.Equal(BrowserSitePermissionDecision.Ask, chrome.ObserveOriginPermission(tab.Id,
            factory.Hosts[0], tab.Address.WebUri!, BrowserSitePermissionKind.Camera));
    }

    private sealed class ChromiumFixtureFactory(bool includeGecko = false) : IBrowseEngineHostFactory, IBrowseEngineAvailability
    {
        public bool IsSupported => true;
        public string UnsupportedReason => "Fixture Chromium only";
        public List<BrowseEngineTabRequest> Requests { get; } = [];
        public List<FixtureTab> Hosts { get; } = [];
        public BrowseEngineKind? FailEngine { get; set; }
        public bool IsEngineSupported(BrowseEngineKind engine) => includeGecko || engine == BrowseEngineKind.Chromium;
        public string UnsupportedReasonFor(BrowseEngineKind engine) => "Fixture has no " + engine;
        public Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests.Add(request);
            if (request.Engine == FailEngine) throw new IOException("Fixture engine startup failed.");
            var host = new FixtureTab(request); Hosts.Add(host);
            return Task.FromResult<IBrowseEngineTab>(host);
        }
    }
    private sealed class FixtureTab(BrowseEngineTabRequest request) : IBrowseEngineTab, IBrowseOriginalPageCommitSource, IBrowseOriginalNavigationStateSource
    {
        public BrowserSnapshot State { get; private set; } = new(request.InitialAddress, "Fixture page", false, false, false, "Fixture engine attached");
        public event EventHandler<BrowserSnapshot>? StateChanged;
        public event EventHandler<BrowserSnapshot>? PageCommitted;
        public event EventHandler<BrowserSnapshot>? NavigationStateChanged;
        public event EventHandler<BrowsePopupRequest>? PopupRequested { add { } remove { } }
        public event EventHandler<BrowseEngineCrash>? Crashed { add { } remove { } }
        public int CloseCalls { get; private set; }
        public List<string> WebNavigationCalls { get; } = [];
        public TaskCompletionSource OriginalCloseSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? CloseGate { get; set; }
        public void CommitFixturePage(Uri address, string title, bool canGoBack = false)
        {
            State = State with { Address = address, Title = title, IsLoading = false, CanGoBack = canGoBack };
            StateChanged?.Invoke(this, State); PageCommitted?.Invoke(this, State);
        }
        public void NavigateWithinDocumentFixture(Uri address)
        {
            State = State with { Address = address, IsLoading = false, Status = "Fixture same-document navigation" };
            StateChanged?.Invoke(this, State); NavigationStateChanged?.Invoke(this, State);
        }
        public Task NavigateAsync(Uri address, CancellationToken token)
        { token.ThrowIfCancellationRequested(); State = State with { Address = address, IsLoading = true }; StateChanged?.Invoke(this, State); return Task.CompletedTask; }
        public Task GoBackAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); WebNavigationCalls.Add("back");
            State = State with { CanGoBack = false, CanGoForward = true };
            StateChanged?.Invoke(this, State); return Task.CompletedTask;
        }
        public Task GoForwardAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); WebNavigationCalls.Add("forward");
            State = State with { CanGoBack = true, CanGoForward = false };
            StateChanged?.Invoke(this, State); return Task.CompletedTask;
        }
        public Task ReloadAsync(CancellationToken token) { WebNavigationCalls.Add("reload"); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken token) { WebNavigationCalls.Add("stop"); return Task.CompletedTask; }
        public Exception? ScriptFailure { get; set; }
        public List<string> Scripts { get; } = [];
        public Task<string?> ExecuteScriptAsync(string script, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Scripts.Add(script);
            return ScriptFailure is { } failure ? Task.FromException<string?>(failure)
                : Task.FromResult<string?>(script.Contains("'zoomed'", StringComparison.Ordinal) ? "zoomed" : "found");
        }
        public Task OpenDeveloperToolsAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync()
        { CloseCalls++; OriginalCloseSource.TrySetResult(); return new(CloseGate ?? OriginalCloseSource.Task); }
    }
    private sealed class PreservedPaths : IAppPaths
    {
        public PreservedPaths()
        { DataDirectory = Path.Combine(Path.GetTempPath(), "browse-native-source-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(DataDirectory); }
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "fixture.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
    }
}
