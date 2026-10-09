using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

// Explicit deterministic engine fixture: these tests certify Browse custody,
// persistence and routing, not the platform renderer or installed authority.
public sealed class BrowseNativeOwnershipTests
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
        Assert.Equal(new Uri(history.Address), chrome.State.SelectedTab.Address);
        var close = chrome.DisposeAsync().AsTask(); await close;
        Assert.Same(close, chrome.OriginalClose);
        await using var restored = await BrowseChrome.CreateAsync(paths);
        Assert.Equal(tabId, restored.State.SelectedTabId);
        Assert.Equal(new Uri(history.Address), restored.State.SelectedTab.Address);
        Assert.Equal(history, Assert.Single(restored.State.History));
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

    private sealed class ChromiumFixtureFactory : IBrowseEngineHostFactory, IBrowseEngineAvailability
    {
        public bool IsSupported => true;
        public string UnsupportedReason => "Fixture Chromium only";
        public List<BrowseEngineTabRequest> Requests { get; } = [];
        public List<FixtureTab> Hosts { get; } = [];
        public bool IsEngineSupported(BrowseEngineKind engine) => engine == BrowseEngineKind.Chromium;
        public string UnsupportedReasonFor(BrowseEngineKind engine) => "Fixture has no " + engine;
        public Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requests.Add(request); var host = new FixtureTab(request); Hosts.Add(host);
            return Task.FromResult<IBrowseEngineTab>(host);
        }
    }
    private sealed class FixtureTab(BrowseEngineTabRequest request) : IBrowseEngineTab, IBrowseOriginalPageCommitSource
    {
        public BrowserSnapshot State { get; private set; } = new(request.InitialAddress, "Fixture page", false, false, false, "Fixture engine attached");
        public event EventHandler<BrowserSnapshot>? StateChanged;
        public event EventHandler<BrowserSnapshot>? PageCommitted;
        public event EventHandler<BrowsePopupRequest>? PopupRequested { add { } remove { } }
        public event EventHandler<BrowseEngineCrash>? Crashed { add { } remove { } }
        public int CloseCalls { get; private set; }
        public TaskCompletionSource OriginalCloseSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void CommitFixturePage(Uri address, string title)
        {
            State = State with { Address = address, Title = title, IsLoading = false };
            StateChanged?.Invoke(this, State); PageCommitted?.Invoke(this, State);
        }
        public Task NavigateAsync(Uri address, CancellationToken token)
        { token.ThrowIfCancellationRequested(); State = State with { Address = address, IsLoading = true }; StateChanged?.Invoke(this, State); return Task.CompletedTask; }
        public Task GoBackAsync(CancellationToken token) => Task.CompletedTask;
        public Task GoForwardAsync(CancellationToken token) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        public Task<string?> ExecuteScriptAsync(string script, CancellationToken token) => Task.FromResult<string?>(null);
        public Task OpenDeveloperToolsAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync()
        { CloseCalls++; OriginalCloseSource.TrySetResult(); return new(OriginalCloseSource.Task); }
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
