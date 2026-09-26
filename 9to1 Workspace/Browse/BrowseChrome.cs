using System.Text.Json;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace HavenOS.Apps.Browse;

public enum BrowseEngineState { Ready, Unsupported, Crashed }
public enum BrowseTransportSecurity { NoPage, SecureTransport, InsecureTransport }

public sealed record BrowseEngineTabRequest(
    Guid TabId,
    BrowserTabPrivacy Privacy,
    string ProfileDirectory,
    Uri InitialAddress,
    BrowseEngineKind Engine = BrowseEngineKind.Gecko);

public sealed record BrowsePopupRequest(Uri Address);
public sealed record BrowseEngineCrash(string Reason);

/// <summary>
/// Platform composition seam for a native browser engine. Browse owns the chrome and state;
/// a platform adapter owns only the native web renderer for one isolated tab/profile.
/// </summary>
public interface IBrowseEngineTab : IEmbeddedBrowserHost, IAsyncDisposable
{
    event EventHandler<BrowsePopupRequest>? PopupRequested;
    event EventHandler<BrowseEngineCrash>? Crashed;
}

public interface IBrowseEngineHostFactory
{
    bool IsSupported { get; }
    string UnsupportedReason { get; }
    Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken cancellationToken);
}

public sealed record BrowseTabSnapshot(
    Guid Id,
    string Title,
    Uri Address,
    BrowserTabPrivacy Privacy,
    BrowseEngineState EngineState,
    bool CanGoBack,
    bool CanGoForward,
    bool IsLoading,
    string Status)
{
    public BrowseEngineKind Engine { get; init; } = BrowseEngineKind.Gecko;
}

public sealed record BrowseSecuritySnapshot(
    BrowseTransportSecurity Transport,
    string Label,
    string Explanation);

public sealed record BrowseDownloadSnapshot(
    bool IsSupported,
    string Status,
    IReadOnlyList<BrowserDownloadRecord> Items);

public sealed record BrowseChromeSnapshot(
    IReadOnlyList<BrowseTabSnapshot> Tabs,
    Guid SelectedTabId,
    IReadOnlyList<BrowserBookmark> Bookmarks,
    IReadOnlyList<BrowserHistoryEntry> History,
    IReadOnlyList<BrowserSitePermission> Permissions,
    BrowseDownloadSnapshot Downloads,
    BrowseSecuritySnapshot Security,
    string FindQuery,
    int ZoomPercent,
    string PopupStatus,
    string Status)
{
    public BrowseTabSnapshot SelectedTab => Tabs.Single(tab => tab.Id == SelectedTabId);
}

/// <summary>
/// Browse-owned application state. It composes the existing browser session, persistence,
/// permission, popup, private-profile, download, and recovery contracts without implementing
/// a web engine or platform WebView.
/// </summary>
public sealed class BrowseChrome : IAsyncDisposable
{
    private static readonly Uri BlankAddress = new("about:blank", UriKind.Absolute);
    private readonly BrowserDataService _data;
    private readonly BrowserSitePermissionStore _permissions;
    private readonly BrowserPrivateProfileManager _privateProfiles;
    private readonly BrowseEnginePreferencesStore _enginePreferences;
    private BrowseEngineSelectionPolicy _enginePolicy;
    private readonly IBrowseEngineHostFactory? _engineFactory;
    private readonly IBrowserAutomationService? _automation;
    private readonly BrowserRecoveryLimiter _recovery = new(3, TimeSpan.FromMinutes(1));
    private readonly List<TabRuntime> _tabs = [];
    private IReadOnlyList<BrowserDownloadRecord> _downloads = [];
    private Guid _selectedTabId;
    private string _findQuery = string.Empty;
    private int _zoomPercent = 100;
    private string _popupStatus = "No popup request.";
    private string _status = "Browse is starting.";
    private bool _disposed;

    private BrowseChrome(
        IAppPaths paths,
        IBrowseEngineHostFactory? engineFactory,
        IBrowserAutomationService? automation,
        BrowseEnginePreferences preferences)
    {
        _data = new BrowserDataService(paths);
        _permissions = new BrowserSitePermissionStore(paths);
        _privateProfiles = new BrowserPrivateProfileManager(paths.BrowserProfileDirectory);
        _enginePreferences = new BrowseEnginePreferencesStore(paths);
        _enginePolicy = BrowseEngineSelectionPolicy.Restore(preferences);
        _engineFactory = engineFactory;
        _automation = automation;
        StandardProfileDirectory = paths.BrowserProfileDirectory;
    }

    public string StandardProfileDirectory { get; }
    public event EventHandler<BrowseChromeSnapshot>? StateChanged;

    public static async Task<BrowseChrome> CreateAsync(
        IAppPaths paths,
        IBrowseEngineHostFactory? engineFactory = null,
        IBrowserAutomationService? automation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var preferenceStore = new BrowseEnginePreferencesStore(paths);
        var preferences = await preferenceStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var chrome = new BrowseChrome(paths, engineFactory, automation, preferences);
        try
        {
            var stored = chrome._data.Settings.RestoreTabs ? chrome._data.Tabs : [];
            foreach (var tab in stored)
            {
                // Older versions may have left private tabs in the session file; never restore them.
                if (tab.Privacy == BrowserTabPrivacy.Private) continue;
                if (!Uri.TryCreate(tab.Address, UriKind.Absolute, out var address)) continue;
                await chrome.AddRuntimeAsync(tab.Id, tab.Title, address, tab.Privacy, tab.Group, cancellationToken).ConfigureAwait(false);
            }

            if (chrome._tabs.Count == 0)
            {
                var home = new Uri(chrome._data.Settings.HomePage, UriKind.Absolute);
                await chrome.AddRuntimeAsync(Guid.NewGuid(), "New tab", home, BrowserTabPrivacy.Standard, string.Empty, cancellationToken).ConfigureAwait(false);
            }

            chrome._selectedTabId = chrome._tabs[0].Id;
            chrome._status = chrome.EngineAvailable
                ? "Browse is ready."
                : chrome.EngineUnsupportedReason;
            await chrome._privateProfiles.CleanupOrphansAsync(
                chrome._tabs.Where(tab => tab.Privacy == BrowserTabPrivacy.Private).Select(tab => tab.Id).ToHashSet(),
                cancellationToken).ConfigureAwait(false);
            return chrome;
        }
        catch
        {
            await chrome.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public bool EngineAvailable => _engineFactory?.IsSupported == true;
    public string EngineUnsupportedReason => _engineFactory is null
        ? "Interactive browsing is unavailable because no native browser engine is registered."
        : _engineFactory.UnsupportedReason;
    public BrowseChromeSnapshot State => CaptureState();

    public async Task<BrowseChromeSnapshot> SetDefaultEngineAsync(BrowseEngineKind engine, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _enginePolicy.SetDefault(engine);
        await SaveEnginePreferencesAsync(cancellationToken).ConfigureAwait(false);
        _status = $"Default browser engine set to {engine}. New tabs use this choice unless a site has its own preference.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> SetSiteEngineAsync(BrowseEngineKind engine, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var tab = SelectedRuntime();
        if (tab.Address.Scheme is not ("http" or "https"))
        {
            _status = "A site-specific engine preference is available after opening an HTTP or HTTPS page.";
            return Publish();
        }
        _enginePolicy.SetSiteOverride(tab.Address, engine);
        await SaveEnginePreferencesAsync(cancellationToken).ConfigureAwait(false);
        return await SetTabEngineAsync(tab, engine, cancellationToken).ConfigureAwait(false);
    }

    public Task<BrowseChromeSnapshot> SetSelectedTabEngineAsync(BrowseEngineKind engine, CancellationToken cancellationToken = default) =>
        SetTabEngineAsync(SelectedRuntime(), engine, cancellationToken);

    private async Task<BrowseChromeSnapshot> SetTabEngineAsync(TabRuntime tab, BrowseEngineKind engine, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(engine)) throw new ArgumentOutOfRangeException(nameof(engine));
        var hadOverride = _enginePolicy.TabOverrides.TryGetValue(tab.Id, out var previousOverride);
        if (tab.Engine == engine)
        {
            _enginePolicy.SetTabOverride(tab.Id, engine);
            await SaveEnginePreferencesAsync(cancellationToken).ConfigureAwait(false);
            return Publish();
        }
        var previous = tab.Engine;
        await ReleaseHostAsync(tab).ConfigureAwait(false);
        tab.Engine = engine;
        try
        {
            if (EngineAvailable) await AttachHostAsync(tab, cancellationToken).ConfigureAwait(false);
            else
            {
                tab.EngineState = BrowseEngineState.Unsupported;
                tab.Status = EngineUnsupportedReason;
            }
            _enginePolicy.SetTabOverride(tab.Id, engine);
            await SaveEnginePreferencesAsync(cancellationToken).ConfigureAwait(false);
            _status = $"This tab now uses {engine}.";
        }
        catch
        {
            tab.Engine = previous;
            _enginePolicy.SetTabOverride(tab.Id, hadOverride ? previousOverride : null);
            try { await AttachHostAsync(tab, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception recoveryFailure) when (recoveryFailure is not OutOfMemoryException)
            {
                tab.EngineState = BrowseEngineState.Crashed;
                tab.Status = "Engine switch failed and the previous renderer could not be restored.";
            }
            throw;
        }
        return Publish();
    }

    private Task SaveEnginePreferencesAsync(CancellationToken cancellationToken)
    {
        var captured = _enginePolicy.Capture();
        var standardTabIds = _tabs.Where(tab => tab.Privacy == BrowserTabPrivacy.Standard).Select(tab => tab.Id).ToHashSet();
        return _enginePreferences.SaveAsync(captured with
        {
            TabOverrides = (captured.TabOverrides ?? new Dictionary<Guid, BrowseEngineKind>())
                .Where(entry => standardTabIds.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value)
        }, cancellationToken);
    }

    public async Task<BrowseChromeSnapshot> NewTabAsync(bool isPrivate, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var privacy = isPrivate ? BrowserTabPrivacy.Private : BrowserTabPrivacy.Standard;
        var home = new Uri(_data.Settings.HomePage, UriKind.Absolute);
        var tab = await AddRuntimeAsync(Guid.NewGuid(), isPrivate ? "Private tab" : "New tab", home, privacy, string.Empty, cancellationToken).ConfigureAwait(false);
        _selectedTabId = tab.Id;
        _status = isPrivate
            ? "Private tab opened. History, permissions, downloads, and tab state are not persisted by Browse."
            : "New tab opened.";
        await SaveTabsAsync(cancellationToken).ConfigureAwait(false);
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> SelectTabAsync(Guid tabId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var tab = RequireTab(tabId);
        _selectedTabId = tab.Id;
        _findQuery = string.Empty;
        _zoomPercent = 100;
        if (tab.EngineState == BrowseEngineState.Crashed)
            _status = "This tab's web renderer crashed. Recovery is available.";
        else
            _status = tab.Status;
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> CloseTabAsync(Guid tabId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var tab = RequireTab(tabId);
        var index = _tabs.IndexOf(tab);
        var wasSelected = tab.Id == _selectedTabId;
        _tabs.Remove(tab);
        _enginePolicy.SetTabOverride(tab.Id, null);
        await ReleaseHostAsync(tab).ConfigureAwait(false);
        tab.Session.Dispose();
        if (tab.Privacy == BrowserTabPrivacy.Private)
            await _privateProfiles.CleanupAsync(tab.Id, cancellationToken).ConfigureAwait(false);
        await SaveEnginePreferencesAsync(cancellationToken).ConfigureAwait(false);

        if (_tabs.Count == 0)
        {
            var home = new Uri(_data.Settings.HomePage, UriKind.Absolute);
            var recreated = await AddRuntimeAsync(Guid.NewGuid(), "New tab", home, BrowserTabPrivacy.Standard, string.Empty, cancellationToken).ConfigureAwait(false);
            _selectedTabId = recreated.Id;
        }
        else if (wasSelected)
        {
            _selectedTabId = _tabs[Math.Clamp(index, 0, _tabs.Count - 1)].Id;
        }
        _status = "Tab closed.";
        await SaveTabsAsync(cancellationToken).ConfigureAwait(false);
        return Publish();
    }

    public Task<BrowseChromeSnapshot> NavigateAsync(string value, CancellationToken cancellationToken = default) =>
        RunNavigationAsync(async (tab, token) =>
        {
            if (!EngineAvailable || tab.Host is null)
                throw new PlatformNotSupportedException(EngineUnsupportedReason);
            var status = await tab.Session.NavigateAsync(value, token).ConfigureAwait(false);
            ApplyHostState(tab, tab.Session.State);
            tab.Status = status;
        }, cancellationToken);

    public Task<BrowseChromeSnapshot> BackAsync(CancellationToken cancellationToken = default) =>
        RunNavigationAsync(async (tab, token) =>
        {
            tab.Status = await tab.Session.BackAsync(token).ConfigureAwait(false);
            ApplyHostState(tab, tab.Session.State);
        }, cancellationToken);

    public Task<BrowseChromeSnapshot> ForwardAsync(CancellationToken cancellationToken = default) =>
        RunNavigationAsync(async (tab, token) =>
        {
            tab.Status = await tab.Session.ForwardAsync(token).ConfigureAwait(false);
            ApplyHostState(tab, tab.Session.State);
        }, cancellationToken);

    public Task<BrowseChromeSnapshot> ReloadAsync(CancellationToken cancellationToken = default) =>
        RunNavigationAsync(async (tab, token) =>
        {
            if (!EngineAvailable || tab.Host is null)
                throw new PlatformNotSupportedException(EngineUnsupportedReason);
            await tab.Session.ReloadAsync(token).ConfigureAwait(false);
            ApplyHostState(tab, tab.Session.State);
            tab.Status = "Page reloaded.";
        }, cancellationToken);

    public async Task<BrowseChromeSnapshot> StopAsync(CancellationToken cancellationToken = default)
    {
        var tab = SelectedRuntime();
        if (tab.Host is null) throw new PlatformNotSupportedException(EngineUnsupportedReason);
        await tab.Session.StopAsync(cancellationToken).ConfigureAwait(false);
        ApplyHostState(tab, tab.Session.State);
        _status = tab.Status = "Loading stopped.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> ToggleBookmarkAsync(CancellationToken cancellationToken = default)
    {
        var tab = SelectedRuntime();
        EnsureWebAddress(tab.Address);
        var added = await _data.ToggleBookmarkAsync(tab.Title, tab.Address.ToString(), "Bookmarks", cancellationToken).ConfigureAwait(false);
        _status = added ? "Bookmark saved locally." : "Bookmark removed locally.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        await _data.ClearHistoryAsync(cancellationToken).ConfigureAwait(false);
        _status = "Browser history cleared.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> SetPermissionAsync(
        BrowserSitePermissionKind kind,
        BrowserSitePermissionDecision decision,
        CancellationToken cancellationToken = default)
    {
        var tab = SelectedRuntime();
        EnsureWebAddress(tab.Address);
        if (tab.Privacy == BrowserTabPrivacy.Private)
        {
            if (decision == BrowserSitePermissionDecision.Ask) tab.PrivatePermissions.Remove(kind);
            else tab.PrivatePermissions[kind] = decision;
            _status = $"Private {kind} permission set to {decision} for this tab only.";
        }
        else
        {
            await _permissions.SetDecisionAsync(tab.Address, kind, decision, cancellationToken).ConfigureAwait(false);
            _status = $"{kind} permission set to {decision} for {tab.Address.Host}.";
        }
        return Publish();
    }

    public async Task<BrowserPopupAssessment> HandlePopupAsync(Uri requestedAddress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedAddress);
        var opener = SelectedRuntime();
        var decision = GetPermission(opener, BrowserSitePermissionKind.WindowManagement);
        var assessment = BrowserNativeRequestPolicy.AssessPopup(opener.Address, requestedAddress, decision);
        _popupStatus = assessment.Reason;
        _status = assessment.Reason;
        if (assessment.IsAllowed)
        {
            var tab = await AddRuntimeAsync(Guid.NewGuid(), requestedAddress.Host, requestedAddress, opener.Privacy, opener.Group, cancellationToken).ConfigureAwait(false);
            _selectedTabId = tab.Id;
            await SaveTabsAsync(cancellationToken).ConfigureAwait(false);
        }
        Publish();
        return assessment;
    }

    public async Task<BrowseChromeSnapshot> FindAsync(string query, bool backwards, CancellationToken cancellationToken = default)
    {
        var tab = RequireInteractiveTab();
        _findQuery = query?.Trim() ?? string.Empty;
        var script = string.IsNullOrEmpty(_findQuery)
            ? "getSelection()?.removeAllRanges(); 'find-cleared'"
            : $"window.find({JsonSerializer.Serialize(_findQuery)}, false, {(backwards ? "true" : "false")}, true, false, false, false) ? 'found' : 'not-found'";
        var result = await tab.Session.ExecuteUiScriptAsync(script, cancellationToken).ConfigureAwait(false);
        _status = string.IsNullOrEmpty(_findQuery) ? "Find cleared." : result == "found" ? $"Found “{_findQuery}”." : $"No match for “{_findQuery}”.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> SetZoomAsync(int percent, CancellationToken cancellationToken = default)
    {
        var tab = RequireInteractiveTab();
        _zoomPercent = Math.Clamp(percent, 50, 200);
        await tab.Session.ExecuteUiScriptAsync(
            $"document.documentElement.style.zoom={JsonSerializer.Serialize(_zoomPercent + "%")}; 'zoomed'",
            cancellationToken).ConfigureAwait(false);
        _status = $"Page content zoom: {_zoomPercent}%. This DOM zoom resets on navigation.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> RefreshDownloadsAsync(CancellationToken cancellationToken = default)
    {
        if (_automation is null)
        {
            _status = "Downloads are unavailable because no approved download ledger is registered.";
            return Publish();
        }
        _downloads = await _automation.GetDownloadsAsync(500, cancellationToken).ConfigureAwait(false);
        _status = $"Loaded {_downloads.Count} completed download{(_downloads.Count == 1 ? string.Empty : "s")}.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> ReportCrashAsync(Guid tabId, string reason)
    {
        var tab = RequireTab(tabId);
        await ReleaseHostAsync(tab).ConfigureAwait(false);
        tab.EngineState = BrowseEngineState.Crashed;
        tab.Status = string.IsNullOrWhiteSpace(reason) ? "The native web renderer crashed." : reason.Trim();
        _status = tab.Status + " Recovery is available.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> RecoverSelectedTabAsync(CancellationToken cancellationToken = default)
    {
        var tab = SelectedRuntime();
        if (tab.EngineState != BrowseEngineState.Crashed)
        {
            _status = "The selected tab does not need recovery.";
            return Publish();
        }
        if (!EngineAvailable || _engineFactory is null)
            throw new PlatformNotSupportedException(EngineUnsupportedReason);
        if (!_recovery.TryAcquire(DateTimeOffset.UtcNow))
        {
            _status = "Automatic recovery paused after three crashes in one minute.";
            return Publish();
        }

        await AttachHostAsync(tab, cancellationToken).ConfigureAwait(false);
        tab.EngineState = BrowseEngineState.Ready;
        tab.Status = "Web renderer recovered. The current address is ready to reload.";
        _status = tab.Status;
        return Publish();
    }

    private async Task<BrowseChromeSnapshot> RunNavigationAsync(
        Func<TabRuntime, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var tab = SelectedRuntime();
        if (tab.EngineState == BrowseEngineState.Crashed)
            throw new InvalidOperationException("Recover the crashed tab before navigating.");
        tab.NavigationCancellation?.Cancel();
        tab.NavigationCancellation?.Dispose();
        tab.NavigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var generation = ++tab.NavigationGeneration;
        try
        {
            await operation(tab, tab.NavigationCancellation.Token).ConfigureAwait(false);
            if (generation != tab.NavigationGeneration) return State;
            await ObserveCompletedNavigationAsync(tab, cancellationToken).ConfigureAwait(false);
            _status = tab.Status;
            return Publish();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return State;
        }
    }

    private async Task ObserveCompletedNavigationAsync(TabRuntime tab, CancellationToken cancellationToken)
    {
        var state = tab.Session.State;
        ApplyHostState(tab, state);
        if (state.Address is null || state.IsLoading || state.Address.Scheme is not ("http" or "https")) return;
        await _data.RecordVisitAsync(tab.Title, tab.Address.ToString(), tab.Privacy == BrowserTabPrivacy.Private, cancellationToken).ConfigureAwait(false);
        await SaveTabsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<TabRuntime> AddRuntimeAsync(
        Guid id,
        string title,
        Uri address,
        BrowserTabPrivacy privacy,
        string group,
        CancellationToken cancellationToken)
    {
        var session = new BrowserSessionService(new TabPaths(StandardProfileDirectory));
        var tab = new TabRuntime(id, title, address, privacy, group, session, _enginePolicy.Resolve(address, id));
        _tabs.Add(tab);
        if (EngineAvailable)
            await AttachHostAsync(tab, cancellationToken).ConfigureAwait(false);
        else
        {
            tab.EngineState = BrowseEngineState.Unsupported;
            tab.Status = EngineUnsupportedReason;
        }
        return tab;
    }

    private async Task AttachHostAsync(TabRuntime tab, CancellationToken cancellationToken)
    {
        if (_engineFactory is null) throw new PlatformNotSupportedException(EngineUnsupportedReason);
        var profileRoot = tab.Privacy == BrowserTabPrivacy.Private
            ? await _privateProfiles.CreateAsync(tab.Id, cancellationToken).ConfigureAwait(false)
            : StandardProfileDirectory;
        var profile = Path.Combine(profileRoot, "engines", tab.Engine.ToString().ToLowerInvariant());
        Directory.CreateDirectory(profile);
        var host = await _engineFactory.CreateAsync(
            new BrowseEngineTabRequest(tab.Id, tab.Privacy, profile, tab.Address, tab.Engine), cancellationToken).ConfigureAwait(false);
        tab.Host = host;
        tab.Session.Attach(host);
        host.PopupRequested += tab.PopupHandler = (_, request) => _ = HandlePopupFromAsync(tab, request.Address);
        host.Crashed += tab.CrashHandler = (_, crash) => _ = ReportCrashAsync(tab.Id, crash.Reason);
        ApplyHostState(tab, host.State);
        tab.EngineState = BrowseEngineState.Ready;
    }

    private async Task HandlePopupFromAsync(TabRuntime opener, Uri address)
    {
        if (_disposed || !_tabs.Contains(opener)) return;
        _selectedTabId = opener.Id;
        try { await HandlePopupAsync(address).ConfigureAwait(false); }
        catch (Exception exception) { _status = "Popup handling failed: " + exception.Message; Publish(); }
    }

    private async Task ReleaseHostAsync(TabRuntime tab)
    {
        if (tab.Host is null) return;
        if (tab.PopupHandler is not null) tab.Host.PopupRequested -= tab.PopupHandler;
        if (tab.CrashHandler is not null) tab.Host.Crashed -= tab.CrashHandler;
        tab.Session.Detach(tab.Host);
        await tab.Host.DisposeAsync().ConfigureAwait(false);
        tab.Host = null;
    }

    private void ApplyHostState(TabRuntime tab, BrowserSnapshot state)
    {
        if (state.Address is not null) tab.Address = state.Address;
        tab.Title = string.IsNullOrWhiteSpace(state.Title) ? tab.Address.Host : state.Title.Trim();
        tab.CanGoBack = state.CanGoBack;
        tab.CanGoForward = state.CanGoForward;
        tab.IsLoading = state.IsLoading;
        tab.Status = state.Status;
    }

    private BrowserSitePermissionDecision GetPermission(TabRuntime tab, BrowserSitePermissionKind kind) =>
        tab.Privacy == BrowserTabPrivacy.Private
            ? tab.PrivatePermissions.GetValueOrDefault(kind, BrowserSitePermissionDecision.Ask)
            : tab.Address.Scheme is "http" or "https"
                ? _permissions.GetDecision(tab.Address, kind)
                : BrowserSitePermissionDecision.Ask;

    private Task SaveTabsAsync(CancellationToken cancellationToken) => _data.SaveTabsAsync(
        _tabs.Where(tab => tab.Privacy == BrowserTabPrivacy.Standard)
            .Select(tab => new BrowserTabState(tab.Id, tab.Title, tab.Address.ToString(), tab.Privacy, tab.Group, DateTimeOffset.UtcNow)),
        cancellationToken);

    private BrowseChromeSnapshot CaptureState()
    {
        ThrowIfDisposed();
        var selected = SelectedRuntime();
        var tabs = _tabs.Select(tab => new BrowseTabSnapshot(
            tab.Id, tab.Title, tab.Address, tab.Privacy, tab.EngineState,
            tab.CanGoBack, tab.CanGoForward, tab.IsLoading, tab.Status) { Engine = tab.Engine }).ToArray();
        var security = SecurityFor(selected);
        return new BrowseChromeSnapshot(
            tabs,
            selected.Id,
            _data.Bookmarks,
            _data.History,
            _permissions.Permissions,
            new BrowseDownloadSnapshot(
                _automation is not null,
                _automation is null ? "No approved download ledger is registered." : "Only completed, policy-approved downloads are listed.",
                _downloads),
            security,
            _findQuery,
            _zoomPercent,
            _popupStatus,
            _status);
    }

    private static BrowseSecuritySnapshot SecurityFor(TabRuntime tab) => tab.Address.Scheme switch
    {
        "https" => new(BrowseTransportSecurity.SecureTransport, "HTTPS", "Transport is encrypted. Browse does not claim that encryption verifies the site's trustworthiness."),
        "http" => new(BrowseTransportSecurity.InsecureTransport, "Not secure", "This page uses unencrypted HTTP transport."),
        _ => new(BrowseTransportSecurity.NoPage, "No web page", "No HTTP or HTTPS page is active.")
    };

    private BrowseChromeSnapshot Publish()
    {
        var snapshot = CaptureState();
        StateChanged?.Invoke(this, snapshot);
        return snapshot;
    }

    private TabRuntime SelectedRuntime() => RequireTab(_selectedTabId);
    private TabRuntime RequireInteractiveTab()
    {
        var tab = SelectedRuntime();
        if (tab.Host is null || tab.EngineState != BrowseEngineState.Ready)
            throw new PlatformNotSupportedException(tab.EngineState == BrowseEngineState.Crashed
                ? "The tab's native web renderer crashed. Recover it before using page tools."
                : EngineUnsupportedReason);
        return tab;
    }
    private TabRuntime RequireTab(Guid id) => _tabs.FirstOrDefault(tab => tab.Id == id)
        ?? throw new ArgumentException("The browser tab does not exist.", nameof(id));
    private static void EnsureWebAddress(Uri address)
    {
        if (address.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("This action requires an HTTP or HTTPS page.");
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        foreach (var tab in _tabs.ToArray())
        {
            tab.NavigationCancellation?.Cancel();
            tab.NavigationCancellation?.Dispose();
            await ReleaseHostAsync(tab).ConfigureAwait(false);
            tab.Session.Dispose();
            if (tab.Privacy == BrowserTabPrivacy.Private)
            {
                try { await _privateProfiles.CleanupAsync(tab.Id, CancellationToken.None).ConfigureAwait(false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        _tabs.Clear();
        _permissions.Dispose();
        _data.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class TabRuntime(
        Guid id,
        string title,
        Uri address,
        BrowserTabPrivacy privacy,
        string group,
        BrowserSessionService session,
        BrowseEngineKind engine)
    {
        public Guid Id { get; } = id;
        public string Title { get; set; } = title;
        public Uri Address { get; set; } = address;
        public BrowserTabPrivacy Privacy { get; } = privacy;
        public string Group { get; } = group;
        public BrowserSessionService Session { get; } = session;
        public BrowseEngineKind Engine { get; set; } = engine;
        public IBrowseEngineTab? Host { get; set; }
        public EventHandler<BrowsePopupRequest>? PopupHandler { get; set; }
        public EventHandler<BrowseEngineCrash>? CrashHandler { get; set; }
        public BrowseEngineState EngineState { get; set; }
        public bool CanGoBack { get; set; }
        public bool CanGoForward { get; set; }
        public bool IsLoading { get; set; }
        public string Status { get; set; } = "Idle";
        public Dictionary<BrowserSitePermissionKind, BrowserSitePermissionDecision> PrivatePermissions { get; } = [];
        public CancellationTokenSource? NavigationCancellation { get; set; }
        public long NavigationGeneration { get; set; }
    }

    private sealed class TabPaths(string profileDirectory) : IAppPaths
    {
        private readonly string _root = Path.GetDirectoryName(profileDirectory) ?? profileDirectory;
        public string DataDirectory => _root;
        public string DatabasePath => Path.Combine(_root, "haven.db");
        public string BrowserProfileDirectory => profileDirectory;
        public string AttachmentsDirectory => Path.Combine(_root, "attachments");
        public string LogsDirectory => Path.Combine(_root, "logs");
        public string LegacyStatePath => Path.Combine(_root, "state.json");
    }
}
