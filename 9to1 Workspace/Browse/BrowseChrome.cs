using System.Text.Json;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace HavenOS.Apps.Browse;

public enum BrowseEngineState { Ready, Unsupported, Crashed, NativePage, NativeApp }
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
    AppContentAddress Address,
    BrowserTabPrivacy Privacy,
    BrowseEngineState EngineState,
    bool CanGoBack,
    bool CanGoForward,
    bool IsLoading,
    string Status)
{
    public BrowseEngineKind Engine { get; init; } = BrowseEngineKind.Gecko;
    public BrowserNativePage NativePage { get; init; }
    public string ProfileId { get; init; } = string.Empty;
    public string IconKey { get; init; } = "web";
    public bool IsHome { get; init; }
    public bool IsPinned { get; init; }
    public bool IsCloseable { get; init; } = true;
}

public sealed record BrowseSecuritySnapshot(
    BrowseTransportSecurity Transport,
    string Label,
    string Explanation);

public sealed record BrowseDownloadSnapshot(
    bool IsSupported,
    string Status,
    IReadOnlyList<BrowserDownloadRecord> Items)
{
    public IReadOnlyList<BrowsePendingDownloadObservation> Pending { get; init; } = [];
    public string ReviewStatus { get; init; } = string.Empty;
}

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
public sealed partial class BrowseChrome : IAsyncDisposable
{
    private readonly BrowserDataService _data;
    private readonly IBrowseTabSessionPersistence? _tabSessionPersistence;
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
    private long _selectionGeneration;
    private string _popupStatus = "No popup request.";
    private string _status = "Browse is starting.";
    private bool _disposed;

    private BrowseChrome(
        IAppPaths paths,
        IBrowseEngineHostFactory? engineFactory,
        IBrowserAutomationService? automation,
        BrowseEnginePreferences preferences,
        IBrowseAppContentFactory? appContentFactory,
        IBrowseInternalContentFactory? internalContentFactory,
        string homeProfileId,
        IBrowseTabSessionPersistence? tabSessionPersistence)
    {
        _data = new BrowserDataService(paths);
        _tabSessionPersistence = tabSessionPersistence;
        _permissions = new BrowserSitePermissionStore(paths);
        _privateProfiles = new BrowserPrivateProfileManager(paths.BrowserProfileDirectory);
        _enginePreferences = new BrowseEnginePreferencesStore(paths);
        _enginePolicy = BrowseEngineSelectionPolicy.Restore(preferences);
        _engineFactory = engineFactory;
        _automation = automation;
        _appContentFactory = appContentFactory;
        _internalContentFactory = internalContentFactory;
        _homeProfileId = homeProfileId;
        StandardProfileDirectory = paths.BrowserProfileDirectory;
    }

    public string StandardProfileDirectory { get; }
    public event EventHandler<BrowseChromeSnapshot>? StateChanged;
    /// <summary>Raised only after ordinary unsaved-work preflight accepted retirement.
    /// Observers cannot prevent the original resources from being drained.</summary>
    public event EventHandler? RetirementStarted;
    private static readonly AsyncLocal<RetirementNotification?> LogicalRetirementNotification = new();
    private sealed class RetirementNotification(BrowseChrome owner, RetirementNotification? parent)
    {
        public BrowseChrome Owner { get; } = owner;
        public RetirementNotification? Parent { get; } = parent;
        public volatile bool Active = true;
    }

    public static async Task<BrowseChrome> CreateAsync(
        IAppPaths paths,
        IBrowseEngineHostFactory? engineFactory = null,
        IBrowserAutomationService? automation = null,
        CancellationToken cancellationToken = default,
        IBrowseAppContentFactory? appContentFactory = null,
        IBrowseInternalContentFactory? internalContentFactory = null,
        string homeProfileId = "",
        IBrowseTabSessionPersistence? tabSessionPersistence = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var preferenceStore = new BrowseEnginePreferencesStore(paths);
        var preferences = await preferenceStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        var chrome = new BrowseChrome(paths, engineFactory, automation, preferences,
            appContentFactory, internalContentFactory, homeProfileId, tabSessionPersistence);
        try
        {
            BrowseTabSessionProjection? projected = null;
            if (tabSessionPersistence is not null)
            {
                var originalLoad = tabSessionPersistence.LoadAsync(cancellationToken)
                    ?? throw new InvalidOperationException("The workspace owner supplied no original session load task.");
                chrome.RetainAppSource(originalLoad); projected = await originalLoad.ConfigureAwait(false);
            }
            // The same preference applies to the genuine owner-backed projection and
            // the local store. The owner's original load above is still retained/awaited.
            var stored = chrome._data.Settings.RestoreTabs
                ? (tabSessionPersistence is not null ? projected?.Tabs ?? [] : chrome._data.Tabs)
                : [];
            foreach (var tab in stored)
            {
                // Older versions may have left private tabs in the session file; never restore them.
                if (tab.Privacy == BrowserTabPrivacy.Private) continue;
                if (!TryStoredAddress(tab.Address, out var address)) continue;
                if (chrome._tabs.Any(existing => existing.Id == tab.Id || IsHomeAddress(address!) && existing.IsHome)) continue;
                await chrome.AddRuntimeAsync(tab.Id, tab.Title, address!, tab.Privacy, tab.Group, cancellationToken,
                    string.IsNullOrEmpty(tab.ProfileId) ? homeProfileId : tab.ProfileId, restoring: true, isPinned: tab.IsPinned == true).ConfigureAwait(false);
            }

            if (internalContentFactory is not null && !chrome._tabs.Any(tab => tab.IsHome))
                await chrome.AddRuntimeAsync(Guid.NewGuid(), "Home", HomeAddress, BrowserTabPrivacy.Standard,
                    string.Empty, cancellationToken, homeProfileId, restoring: true).ConfigureAwait(false);

            if (chrome._tabs.Count == 0)
            {
                var home = AppContentAddress.Parse(chrome._data.Settings.HomePage);
                await chrome.AddRuntimeAsync(Guid.NewGuid(), "New tab", home, BrowserTabPrivacy.Standard, string.Empty, cancellationToken).ConfigureAwait(false);
            }

            chrome.NormalizeTabPlacement();
            chrome._selectedTabId = projected?.SelectedTabId is { } restoredSelection && chrome._tabs.Any(tab => tab.Id == restoredSelection)
                ? restoredSelection : chrome._tabs.FirstOrDefault(tab => tab.IsHome)?.Id ?? chrome._tabs[0].Id;
            chrome._status = chrome._tabs[0].EngineState == BrowseEngineState.Ready
                ? "Browse is ready."
                : chrome._tabs[0].Status;
            await chrome._privateProfiles.CleanupOrphansAsync(
                chrome._tabs.Where(tab => tab.Privacy == BrowserTabPrivacy.Private).Select(tab => tab.Id).ToHashSet(),
                cancellationToken).ConfigureAwait(false);
            if (internalContentFactory is not null || tabSessionPersistence is not null) await chrome.SaveTabsAsync(cancellationToken).ConfigureAwait(false);
            return chrome;
        }
        catch (Exception startupFailure)
        {
            try { await chrome.RetireFailedStartupAsync().ConfigureAwait(false); }
            catch (Exception cleanupFailure) { throw new AggregateException("Browse startup and original retirement failed.", startupFailure, cleanupFailure); }
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
        ThrowIfCannotAccept();
        cancellationToken.ThrowIfCancellationRequested();
        var previous = _enginePolicy.DefaultEngine;
        _enginePolicy.SetDefault(engine);
        try { await SaveEnginePreferencesAsync(cancellationToken).ConfigureAwait(false); }
        catch
        {
            _enginePolicy.SetDefault(previous);
            throw;
        }
        _status = $"Default browser engine set to {engine}. New tabs use this choice unless a site has its own preference.";
        return Publish();
    }

    public Task<BrowseChromeSnapshot> SetSiteEngineAsync(BrowseEngineKind engine, CancellationToken cancellationToken = default) =>
        RetainContentTransition(() => SetSiteEngineCoreAsync(engine, cancellationToken), cancellationToken);

    private async Task<BrowseChromeSnapshot> SetSiteEngineCoreAsync(BrowseEngineKind engine, CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept();
        cancellationToken.ThrowIfCancellationRequested();
        var tab = SelectedRuntime();
        if (!tab.Address.IsWeb)
        {
            _status = "A site-specific engine preference is available after opening an HTTP or HTTPS page.";
            return Publish();
        }
        var address = tab.Address.WebUri!;
        var host = address.IdnHost.TrimEnd('.');
        var hadOverride = _enginePolicy.SiteOverrides.TryGetValue(host, out var previous);
        _enginePolicy.SetSiteOverride(address, engine);
        try { await SaveEnginePreferencesAsync(cancellationToken).ConfigureAwait(false); }
        catch
        {
            if (hadOverride) _enginePolicy.SetSiteOverride(address, previous);
            else _enginePolicy.SetSiteOverride(address, null);
            throw;
        }
        await SetTabEngineAsync(tab, _enginePolicy.Resolve(address, tab.Id), cancellationToken, persistTabOverride: false).ConfigureAwait(false);
        _status = _enginePolicy.TabOverrides.ContainsKey(tab.Id)
            ? $"Site preference set to {engine}. This tab keeps its explicit engine choice."
            : $"Site preference set to {engine}. " + tab.Status;
        return Publish();
    }

    public Task<BrowseChromeSnapshot> SetSelectedTabEngineAsync(BrowseEngineKind engine, CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept(); cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(engine)) throw new ArgumentOutOfRangeException(nameof(engine));
        var tab = SelectedRuntime();
        if (!tab.Address.IsWeb) throw new InvalidOperationException("Native Browse pages do not use a web engine.");
        return RetainContentTransition(() => SetTabEngineAsync(tab, engine, cancellationToken), cancellationToken);
    }

    private async Task<BrowseChromeSnapshot> SetTabEngineAsync(TabRuntime tab, BrowseEngineKind engine, CancellationToken cancellationToken, bool persistTabOverride = true, Uri? initialAddress = null)
    {
        using var transition = AdmitContentTransition();
        ThrowIfCannotAccept();
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(engine)) throw new ArgumentOutOfRangeException(nameof(engine));
        if (!tab.Address.IsWeb)
            throw new InvalidOperationException("Native Browse pages do not use a web engine.");
        var hadOverride = _enginePolicy.TabOverrides.TryGetValue(tab.Id, out var previousOverride);
        if (tab.Engine == engine)
        {
            if (persistTabOverride)
            {
                _enginePolicy.SetTabOverride(tab.Id, engine);
                try { await SaveEnginePreferencesAsync(cancellationToken).ConfigureAwait(false); }
                catch
                {
                    _enginePolicy.SetTabOverride(tab.Id, hadOverride ? previousOverride : null);
                    throw;
                }
            }
            return Publish();
        }
        var previous = tab.Engine;
        var previousAddress = tab.Address;
        await ReleaseHostAsync(tab).ConfigureAwait(false);
        tab.Engine = engine;
        if (initialAddress is not null) tab.Address = AppContentAddress.Parse(initialAddress.AbsoluteUri);
        try
        {
            if (IsEngineAvailable(tab.Engine)) await AttachHostAsync(tab, cancellationToken).ConfigureAwait(false);
            else
            {
                tab.EngineState = BrowseEngineState.Unsupported;
                tab.Status = EngineUnavailableReason(tab.Engine);
            }
            if (persistTabOverride)
            {
                _enginePolicy.SetTabOverride(tab.Id, engine);
                await SaveEnginePreferencesAsync(cancellationToken).ConfigureAwait(false);
            }
            _status = tab.EngineState == BrowseEngineState.Ready ? $"This tab now uses {engine}." : EngineUnavailableReason(engine);
        }
        catch (Exception switchFailure) when (switchFailure is not OutOfMemoryException)
        {
            if (persistTabOverride) _enginePolicy.SetTabOverride(tab.Id, hadOverride ? previousOverride : null);
            try
            {
                // A preference write can fail after the new renderer was created.
                // Retire that exact renderer before restoring the previous backend.
                await ReleaseHostAsync(tab).ConfigureAwait(false);
                tab.Engine = previous;
                tab.Address = previousAddress;
                if (IsEngineAvailable(previous)) await AttachHostAsync(tab, CancellationToken.None).ConfigureAwait(false);
                else
                {
                    tab.EngineState = BrowseEngineState.Unsupported;
                    tab.Status = EngineUnavailableReason(previous);
                }
            }
            catch (Exception recoveryFailure) when (recoveryFailure is not OutOfMemoryException)
            {
                tab.EngineState = BrowseEngineState.Crashed;
                tab.Status = "Engine switch failed and the previous renderer could not be restored.";
                throw new AggregateException(tab.Status, switchFailure, recoveryFailure);
            }
            throw;
        }
        return Publish();
    }

    private Task SaveEnginePreferencesAsync(CancellationToken cancellationToken)
    {
        var captured = _enginePolicy.Capture();
        var standardTabIds = _tabs.Where(tab => tab.Privacy == BrowserTabPrivacy.Standard).Select(tab => tab.Id).ToHashSet();
        var original = _enginePreferences.SaveAsync(captured with
        {
            TabOverrides = (captured.TabOverrides ?? new Dictionary<Guid, BrowseEngineKind>())
                .Where(entry => standardTabIds.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value)
        }, cancellationToken);
        lock (_nativeSourceGate) _originalNativeSources.Add(original);
        return original;
    }

    public Task<BrowseChromeSnapshot> NewTabAsync(bool isPrivate, CancellationToken cancellationToken = default) =>
        RetainContentTransition(() => NewTabCoreAsync(isPrivate, cancellationToken), cancellationToken, _internalContentFactory is not null);

    private async Task<BrowseChromeSnapshot> NewTabCoreAsync(bool isPrivate, CancellationToken cancellationToken)
    {
        ThrowIfCannotAccept();
        cancellationToken.ThrowIfCancellationRequested();
        var privacy = isPrivate ? BrowserTabPrivacy.Private : BrowserTabPrivacy.Standard;
        var home = _internalContentFactory is not null ? NewTabAddress : AppContentAddress.Parse(_data.Settings.HomePage);
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
        ThrowIfCannotAccept();
        cancellationToken.ThrowIfCancellationRequested();
        var tab = RequireTab(tabId);
        Guid previousSelection;
        string previousStatus;
        string selectedStatus;
        long generation;
        lock (_nativeSourceGate)
        {
            previousSelection = _selectedTabId;
            previousStatus = _status;
            _selectedTabId = tab.Id;
            selectedStatus = _status = tab.EngineState == BrowseEngineState.Crashed
                ? "This tab's web renderer crashed. Recovery is available."
                : tab.Status;
            generation = ++_selectionGeneration;
        }
        if (_tabSessionPersistence is not null)
        {
            try { await SaveTabsAsync(cancellationToken).ConfigureAwait(false); }
            catch
            {
                lock (_nativeSourceGate)
                {
                    // Another selection or tab removal owns the newer state. Roll
                    // back only while this request is still the latest selection,
                    // its target remains selected, and its prior target still exists.
                    if (_selectionGeneration == generation && _selectedTabId == tab.Id &&
                        _tabs.Any(candidate => candidate.Id == previousSelection))
                    {
                        _selectedTabId = previousSelection;
                        // Preserve a later status observation even when no newer
                        // selection superseded this request.
                        if (_status == selectedStatus) _status = previousStatus;
                        _selectionGeneration++;
                    }
                }
                throw;
            }
        }
        return Publish();
    }

    public Task<BrowseChromeSnapshot> CloseTabAsync(Guid tabId, CancellationToken cancellationToken = default) =>
        RetainContentTransition(() => CloseTabCoreAsync(tabId, cancellationToken), cancellationToken);

    private async Task<BrowseChromeSnapshot> CloseTabCoreAsync(Guid tabId, CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept();
        cancellationToken.ThrowIfCancellationRequested();
        var tab = RequireTab(tabId);
        if (tab.IsHome) { _status = "Home stays available in this window."; return Publish(); }
        var preparation = await AcquireContentCloseAsync(tab, cancellationToken).ConfigureAwait(false);
        if (preparation is null)
        { _status = "Finish or save the app's pending work before closing this tab."; return Publish(); }
        Exception? preparationFailure = null;
        try
        {
        cancellationToken.ThrowIfCancellationRequested();
        BeginContentClose(preparation);
        BrowseEngineKind? engineOverride = _enginePolicy.TabOverrides.TryGetValue(tab.Id, out var explicitEngine) ? explicitEngine : null;
        // From this accepted close boundary onward, a pending selection rollback
        // must not revive the tab even while renderer retirement is still awaiting.
        lock (_nativeSourceGate) _selectionGeneration++;
        await ReleaseContentAsync(tab).ConfigureAwait(false);
        int index;
        bool wasSelected;
        // Determine against the latest selection/order after renderer retirement.
        lock (_nativeSourceGate)
        {
            index = _tabs.IndexOf(tab);
            if (index < 0) throw new InvalidOperationException("The tab was removed before its close completed.");
            wasSelected = _selectedTabId == tab.Id;
            _tabs.RemoveAt(index);
        }
        _enginePolicy.SetTabOverride(tab.Id, null);
        if (_tabs.Count == 0)
        {
            try
            {
                var home = _internalContentFactory is not null ? HomeAddress : AppContentAddress.Parse(_data.Settings.HomePage);
                var replacementId = Guid.NewGuid();
                _selectedTabId = replacementId;
                await AddRuntimeAsync(replacementId, "New tab", home, BrowserTabPrivacy.Standard, string.Empty, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Keep a valid canonical owner if the replacement engine cannot
                // start. The retired original renderer is explicitly recoverable.
                _tabs.Add(tab); _selectedTabId = tab.Id;
                _enginePolicy.SetTabOverride(tab.Id, engineOverride);
                tab.EngineState = BrowseEngineState.Crashed;
                tab.Status = "The replacement tab could not start. Recover this tab or retry closing it.";
                _status = tab.Status;
                throw;
            }
        }
        else if (wasSelected)
        {
            _selectedTabId = _tabs[Math.Clamp(index, 0, _tabs.Count - 1)].Id;
        }
        // Selection must already identify a surviving tab before any later I/O
        // can fail; callers must still be able to inspect the real remaining state.
        tab.Session.Dispose();
        if (tab.Privacy == BrowserTabPrivacy.Private)
            await _privateProfiles.CleanupAsync(tab.Id, cancellationToken).ConfigureAwait(false);
        // The renderer is retired and the tab is now genuinely closed. Keep its
        // standard-tab recovery record before either durable write can fail, so a
        // persistence outage does not turn a recoverable close into data loss.
        RememberClosedTab(tab, index, engineOverride);
        await SaveEnginePreferencesAsync(cancellationToken).ConfigureAwait(false);
        _status = "Tab closed.";
        await SaveTabsAsync(cancellationToken).ConfigureAwait(false);
        return Publish();
        }
        catch (Exception error) { preparationFailure = error; throw; }
        finally { await FinishContentAttemptAsync(preparation, preparationFailure).ConfigureAwait(false); }
    }

    public Task<BrowseChromeSnapshot> NavigateAsync(string value, CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept(); cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (AppContentAddress.IsReserved(value))
        {
            if (!AppContentAddress.TryParse(value, out var reserved) || reserved!.Namespace == AppContentNamespace.Cui ||
                reserved.Namespace == AppContentNamespace.Browse && !IsOwnedCanvas(reserved) && !BrowserNativePages.TryResolve(reserved.Value, out _))
            { _status = BrowseInternalNavigation.UnavailableMessage; return Task.FromResult(Publish()); }
            return OpenContentAsync(reserved, openInNewTab: false, cancellationToken: cancellationToken);
        }
        var web = BrowserSessionService.NormaliseAddress(value, _data.Settings.SearchTemplate);
        var assessment = BrowserNativeRequestPolicy.AssessTopLevel(web);
        if (!assessment.IsAllowed) throw new InvalidOperationException(assessment.Reason);
        return OpenContentAsync(AppContentAddress.Parse(web.AbsoluteUri), openInNewTab: false, cancellationToken: cancellationToken);
    }

    /// <summary>Opens an exact typed route in a canonical tab. Queued app actions
    /// supply their original source identity so a later selection cannot redirect them.</summary>
    public Task<BrowseChromeSnapshot> OpenContentAsync(AppContentAddress address, bool openInNewTab = true,
        Guid? sourceTabId = null, CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept(); cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(address);
        address = AppContentRouteMigration.Canonicalize(address);
        if (address.Namespace == AppContentNamespace.Cui || address.Namespace == AppContentNamespace.Browse &&
            !IsOwnedCanvas(address) && !BrowserNativePages.TryResolve(address.Value, out _))
            throw new NotSupportedException("The content route is not registered in this host.");
        return RetainContentTransition(() => OpenContentCoreAsync(address, openInNewTab, sourceTabId, cancellationToken), cancellationToken, !address.IsWeb);
    }
    private async Task<BrowseChromeSnapshot> OpenContentCoreAsync(AppContentAddress address, bool openInNewTab,
        Guid? sourceTabId, CancellationToken cancellationToken)
    {
        ThrowIfCannotAccept(); cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(address);
        address = AppContentRouteMigration.Canonicalize(address);
        if (address.Namespace == AppContentNamespace.Cui || address.Namespace == AppContentNamespace.Browse &&
            !IsOwnedCanvas(address) && !BrowserNativePages.TryResolve(address.Value, out _))
            throw new NotSupportedException("The content route is not registered in this host.");
        var source = RequireTab(sourceTabId ?? _selectedTabId);
        var activate = source.Id == _selectedTabId;
        if (IsHomeAddress(address) && _tabs.FirstOrDefault(tab => tab.IsHome) is { } home)
        {
            if (activate) _selectedTabId = home.Id;
            if (_tabSessionPersistence is not null) await SaveTabsAsync(cancellationToken).ConfigureAwait(false);
            return Publish();
        }
        if (openInNewTab || source.IsHome)
        {
            var created = await AddRuntimeAsync(Guid.NewGuid(), address.Authority, address, source.Privacy,
                source.Group, cancellationToken, source.ProfileId).ConfigureAwait(false);
            if (activate) _selectedTabId = created.Id;
            await SaveTabsAsync(cancellationToken).ConfigureAwait(false);
            _status = created.Status; return Publish();
        }
        return await RunNavigationAsync(source, async (tab, token) =>
        {
            if (!tab.Address.IsWeb || !address.IsWeb)
            {
                await ReplaceAppContentAsync(tab, address, token).ConfigureAwait(false);
                return;
            }
            var web = address.WebUri!;
            var assessment = BrowserNativeRequestPolicy.AssessTopLevel(web);
            if (!assessment.IsAllowed) throw new InvalidOperationException(assessment.Reason);
            var engine = _enginePolicy.Resolve(web, tab.Id);
            if (!tab.Address.IsWeb)
            {
                await LeaveNativePageAsync(tab, web, engine, token).ConfigureAwait(false);
                await SaveTabsAsync(token).ConfigureAwait(false); return;
            }
            if (engine != tab.Engine)
            {
                await SetTabEngineAsync(tab, engine, token, persistTabOverride: false, initialAddress: web).ConfigureAwait(false);
                await SaveTabsAsync(token).ConfigureAwait(false); return;
            }
            if (tab.Host is null)
            {
                tab.Address = address; tab.Title = address.Authority;
                tab.Status = EngineUnavailableReason(engine);
                await SaveTabsAsync(token).ConfigureAwait(false); return;
            }
            tab.Status = await tab.Session.NavigateAsync(address.Value, token).ConfigureAwait(false);
            ApplyHostState(tab, tab.Session.State);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<BrowseChromeSnapshot> BackAsync(CancellationToken cancellationToken = default) =>
        RunWebNavigationAsync(async (tab, token) =>
        {
            tab.Status = await tab.Session.BackAsync(token).ConfigureAwait(false);
            ApplyHostState(tab, tab.Session.State);
        }, cancellationToken);

    public Task<BrowseChromeSnapshot> ForwardAsync(CancellationToken cancellationToken = default) =>
        RunWebNavigationAsync(async (tab, token) =>
        {
            tab.Status = await tab.Session.ForwardAsync(token).ConfigureAwait(false);
            ApplyHostState(tab, tab.Session.State);
        }, cancellationToken);

    public Task<BrowseChromeSnapshot> ReloadAsync(CancellationToken cancellationToken = default) =>
        RunWebNavigationAsync(async (tab, token) =>
        {
            if (!EngineAvailable || tab.Host is null)
                throw new PlatformNotSupportedException(EngineUnsupportedReason);
            await tab.Session.ReloadAsync(token).ConfigureAwait(false);
            ApplyHostState(tab, tab.Session.State);
            tab.Status = "Page reloaded.";
        }, cancellationToken);

    public async Task<BrowseChromeSnapshot> StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept();
        cancellationToken.ThrowIfCancellationRequested();
        var tab = SelectedRuntime();
        EnsureWebAddress(tab.Address);
        if (tab.Host is null) throw new PlatformNotSupportedException(EngineUnsupportedReason);
        await tab.Session.StopAsync(cancellationToken).ConfigureAwait(false);
        ApplyHostState(tab, tab.Session.State);
        _status = tab.Status = "Loading stopped.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> ToggleBookmarkAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept();
        var tab = SelectedRuntime();
        EnsureWebAddress(tab.Address);
        var added = await _data.ToggleBookmarkAsync(tab.Title, tab.Address.ToString(), "Bookmarks", cancellationToken).ConfigureAwait(false);
        _status = added ? "Bookmark saved locally." : "Bookmark removed locally.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept();
        await _data.ClearHistoryAsync(cancellationToken).ConfigureAwait(false);
        _status = "Browser history cleared.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> SetPermissionAsync(
        BrowserSitePermissionKind kind,
        BrowserSitePermissionDecision decision,
        CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept();
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(decision)) throw new ArgumentOutOfRangeException(nameof(decision));
        var tab = SelectedRuntime();
        EnsureWebAddress(tab.Address);
        if (tab.Privacy == BrowserTabPrivacy.Private)
        {
            var key = PrivatePermissionKey(tab.Address.WebUri!, kind);
            if (decision == BrowserSitePermissionDecision.Ask) tab.PrivatePermissions.Remove(key);
            else tab.PrivatePermissions[key] = decision;
            _status = $"Private {kind} permission set to {decision} for {tab.Address.Authority} in this tab only.";
        }
        else
        {
            await _permissions.SetDecisionAsync(tab.Address.WebUri!, kind, decision, cancellationToken).ConfigureAwait(false);
            _status = $"{kind} permission set to {decision} for {tab.Address.Authority}.";
        }
        return Publish();
    }

    public Task<BrowserPopupAssessment> HandlePopupAsync(Uri requestedAddress, CancellationToken cancellationToken = default) =>
        RetainContentTransition(() => HandlePopupCoreAsync(requestedAddress, cancellationToken), cancellationToken);

    private async Task<BrowserPopupAssessment> HandlePopupCoreAsync(Uri requestedAddress, CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(requestedAddress);
        var opener = SelectedRuntime();
        var decision = GetPermission(opener, BrowserSitePermissionKind.WindowManagement);
        var assessment = BrowserNativeRequestPolicy.AssessPopup(opener.Address.WebUri, requestedAddress, decision);
        _popupStatus = assessment.Reason;
        _status = assessment.Reason;
        if (assessment.IsAllowed)
        {
            var tab = await AddRuntimeAsync(Guid.NewGuid(), requestedAddress.Host, AppContentAddress.Parse(requestedAddress.AbsoluteUri), opener.Privacy, opener.Group, cancellationToken, opener.ProfileId).ConfigureAwait(false);
            _selectedTabId = tab.Id;
            await SaveTabsAsync(cancellationToken).ConfigureAwait(false);
        }
        Publish();
        return assessment;
    }

    public async Task<BrowseChromeSnapshot> FindAsync(string query, bool backwards, CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept();
        var tab = RequireInteractiveTab();
        var findQuery = query?.Trim() ?? string.Empty;
        var script = string.IsNullOrEmpty(findQuery)
            ? "getSelection()?.removeAllRanges(); 'find-cleared'"
            : $"window.find({JsonSerializer.Serialize(findQuery)}, false, {(backwards ? "true" : "false")}, true, false, false, false) ? 'found' : 'not-found'";
        var result = await tab.Session.ExecuteUiScriptAsync(script, cancellationToken).ConfigureAwait(false);
        tab.FindQuery = findQuery;
        _status = string.IsNullOrEmpty(findQuery) ? "Find cleared." : result == "found" ? $"Found “{findQuery}”." : $"No match for “{findQuery}”.";
        return Publish();
    }

    public async Task<BrowseChromeSnapshot> SetZoomAsync(int percent, CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept();
        var tab = RequireInteractiveTab();
        var zoomPercent = Math.Clamp(percent, 50, 200);
        var result = await tab.Session.ExecuteUiScriptAsync(
            $"document.documentElement.style.zoom={JsonSerializer.Serialize(zoomPercent + "%")}; 'zoomed'",
            cancellationToken).ConfigureAwait(false);
        if (result != "zoomed") throw new InvalidOperationException("The page did not confirm the requested zoom.");
        tab.ZoomPercent = zoomPercent;
        _status = $"Page content zoom: {zoomPercent}%. This DOM zoom resets on navigation.";
        return Publish();
    }

    public Task<BrowseChromeSnapshot> RefreshDownloadsAsync(CancellationToken cancellationToken = default) =>
        RetainContentTransition(() => RefreshDownloadsCoreAsync(cancellationToken), cancellationToken);

    public Task<BrowseChromeSnapshot> ReportCrashAsync(Guid tabId, string reason) =>
        RetainContentTransition(() => ReportCrashCoreAsync(tabId, reason), default);

    private async Task<BrowseChromeSnapshot> ReportCrashCoreAsync(Guid tabId, string reason)
    {
        ThrowIfCannotAccept();
        var tab = RequireTab(tabId);
        await ReleaseHostAsync(tab).ConfigureAwait(false);
        tab.EngineState = BrowseEngineState.Crashed;
        tab.Status = string.IsNullOrWhiteSpace(reason) ? "The native web renderer crashed." : reason.Trim();
        _status = tab.Status + " Recovery is available.";
        return Publish();
    }

    public Task<BrowseChromeSnapshot> RecoverSelectedTabAsync(CancellationToken cancellationToken = default) =>
        RetainContentTransition(() => RecoverSelectedTabCoreAsync(cancellationToken), cancellationToken);

    private async Task<BrowseChromeSnapshot> RecoverSelectedTabCoreAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfCannotAccept();
        var tab = SelectedRuntime();
        if (tab.EngineState != BrowseEngineState.Crashed)
        {
            _status = "The selected tab does not need recovery.";
            return Publish();
        }
        if (!IsEngineAvailable(tab.Engine) || _engineFactory is null)
            throw new PlatformNotSupportedException(EngineUnavailableReason(tab.Engine));
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

    private async Task<BrowseChromeSnapshot> RunWebNavigationAsync(
        Func<TabRuntime, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        ThrowIfCannotAccept(); cancellationToken.ThrowIfCancellationRequested();
        var tab = SelectedRuntime();
        // Refuse before retaining a navigation source or cancelling any original
        // app work. A web-only command must not poison native owner retirement.
        EnsureWebAddress(tab.Address);
        return await RunNavigationAsync(tab, operation, cancellationToken).ConfigureAwait(false);
    }

    private Task<BrowseChromeSnapshot> RunNavigationAsync(TabRuntime tab,
        Func<TabRuntime, CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        RetainContentTransition(() => RunNavigationCoreAsync(Task.CompletedTask, tab, operation, cancellationToken), cancellationToken, blocksRetirement: true);

    private async Task<BrowseChromeSnapshot> RunNavigationCoreAsync(Task start, TabRuntime tab,
        Func<TabRuntime, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await start.ConfigureAwait(false);
        ThrowIfCannotAccept();
        cancellationToken.ThrowIfCancellationRequested();
        if (tab.EngineState == BrowseEngineState.Crashed)
            throw new InvalidOperationException("Recover the crashed tab before navigating.");
        tab.NavigationCancellation?.Cancel();
        tab.NavigationCancellation?.Dispose();
        tab.NavigationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var generation = ++tab.NavigationGeneration;
        var navigationToken = tab.NavigationCancellation.Token;
        try
        {
            await operation(tab, navigationToken).ConfigureAwait(false);
            if (generation != tab.NavigationGeneration) return State;
            await ObserveCompletedNavigationAsync(tab, cancellationToken).ConfigureAwait(false);
            _status = tab.Status;
            return Publish();
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested &&
            navigationToken.IsCancellationRequested && error.CancellationToken == navigationToken &&
            (generation != tab.NavigationGeneration || _nativeRetiring))
        {
            return State;
        }
    }

    private async Task ObserveCompletedNavigationAsync(TabRuntime tab, CancellationToken cancellationToken)
    {
        if (tab.Host is null) return;
        var state = tab.Session.State;
        ApplyHostState(tab, state);
        if (tab.Host is IBrowseOriginalPageCommitSource) return;
        if (state.Address is null || state.IsLoading || state.Address.Scheme is not ("http" or "https")) return;
        tab.ZoomPercent = 100;
        await _data.RecordVisitAsync(tab.Title, tab.Address.ToString(), tab.Privacy == BrowserTabPrivacy.Private, cancellationToken).ConfigureAwait(false);
        await SaveTabsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<TabRuntime> AddRuntimeAsync(
        Guid id,
        string title,
        AppContentAddress address,
        BrowserTabPrivacy privacy,
        string group,
        CancellationToken cancellationToken,
        string? profileId = null,
        bool restoring = false,
        bool isPinned = false)
    {
        using var transition = AdmitContentTransition();
        address = AppContentRouteMigration.Canonicalize(address);
        var session = new BrowserSessionService(new TabPaths(StandardProfileDirectory));
        var tab = new TabRuntime(id, title, address, privacy, group, session,
            address.WebUri is { } web ? _enginePolicy.Resolve(web, id) : _enginePolicy.DefaultEngine, profileId ?? _homeProfileId)
            { IsPinned = isPinned };
        _tabs.Add(tab);
        if (!_tabs.Any(item => item.Id == _selectedTabId)) _selectedTabId = tab.Id;
        try
        {
            if (BrowserNativePages.TryResolve(address.Value, out var nativePage))
                ApplyNativePage(tab, nativePage);
            else if (!address.IsWeb)
            {
                try { await AttachAppContentAsync(tab, address, cancellationToken).ConfigureAwait(false); }
                catch when (restoring && !cancellationToken.IsCancellationRequested)
                { tab.EngineState = BrowseEngineState.Unsupported; tab.Status = "The saved native app could not start. Its route and original owner are retained."; }
            }
            else if (IsEngineAvailable(tab.Engine))
                await AttachHostAsync(tab, cancellationToken).ConfigureAwait(false);
            else
            {
                tab.EngineState = BrowseEngineState.Unsupported;
                tab.Status = EngineUnavailableReason(tab.Engine);
            }
            return tab;
        }
        catch
        {
            // A refused factory has produced no renderer to retire. Remove the
            // speculative tab while retaining any actually acquired host on failure.
            if (tab.Host is null && tab.AppOwners.Count == 0) { _tabs.Remove(tab); session.Dispose(); }
            else
            {
                tab.EngineState = tab.Address.IsWeb ? BrowseEngineState.Crashed : BrowseEngineState.Unsupported;
                tab.Status = tab.Address.IsWeb ? "The tab engine could not finish starting." : "The original native app could not finish starting. Its owner is retained.";
            }
            throw;
        }
    }

    private async Task AttachHostAsync(TabRuntime tab, CancellationToken cancellationToken)
    {
        using var transition = AdmitContentTransition();
        var host = await CreateEngineHostAsync(tab, tab.Address.WebUri ?? throw new InvalidOperationException("A web renderer requires a real web address."), tab.Engine, cancellationToken).ConfigureAwait(false);
        AttachAcquiredHost(tab, host);
    }

    private async Task<IBrowseEngineTab> CreateEngineHostAsync(TabRuntime tab, Uri address, BrowseEngineKind engine, CancellationToken cancellationToken)
    {
        if (_engineFactory is null || !IsEngineAvailable(engine)) throw new PlatformNotSupportedException(EngineUnavailableReason(engine));
        var profileRoot = tab.Privacy == BrowserTabPrivacy.Private
            ? await _privateProfiles.CreateAsync(tab.Id, cancellationToken).ConfigureAwait(false)
            : StandardProfileDirectory;
        var profile = Path.Combine(profileRoot, "engines", engine.ToString().ToLowerInvariant());
        Directory.CreateDirectory(profile);
        var actual = _engineFactory.CreateAsync(
            new BrowseEngineTabRequest(tab.Id, tab.Privacy, profile, address, engine), cancellationToken);
        RetainContentSource(actual);
        return await actual.ConfigureAwait(false);
    }

    private void AttachAcquiredHost(TabRuntime tab, IBrowseEngineTab host)
    {
        tab.Host = host;
        tab.ZoomPercent = 100;
        tab.HasOriginalCommittedPage = false;
        tab.OriginalHostClose = null;
        tab.Session.Attach(host);
        host.PopupRequested += tab.PopupHandler = (_, request) => CaptureOriginalNativeCallback(tab, host, () => HandlePopupFromAsync(tab, request.Address));
        host.Crashed += tab.CrashHandler = (_, crash) => CaptureOriginalNativeCallback(tab, host, async () => { await ReportCrashAsync(tab.Id, crash.Reason).ConfigureAwait(false); });
        host.StateChanged += tab.NativeStateHandler = (_, snapshot) => ObserveOriginalNativeState(tab, host, snapshot);
        if (host is IBrowseOriginalNavigationStateSource navigationState)
            navigationState.NavigationStateChanged += tab.NavigationStateHandler = (_, snapshot) => CaptureOriginalNavigationState(tab, host, snapshot);
        if (host is IBrowseOriginalPageCommitSource committed)
            committed.PageCommitted += tab.NativeCommitHandler = (_, snapshot) => CaptureOriginalPageCommit(tab, host, snapshot);
        ApplyHostState(tab, host.State);
        tab.EngineState = BrowseEngineState.Ready;
    }

    private async Task HandlePopupFromAsync(TabRuntime opener, Uri address)
    {
        if (_nativeRetiring || _disposed || !_tabs.Contains(opener)) return;
        _selectedTabId = opener.Id;
        await HandlePopupAsync(address).ConfigureAwait(false);
    }

    private Task ReleaseHostAsync(TabRuntime tab)
    {
        lock (_nativeSourceGate)
        {
            if (tab.OriginalHostClose is not null) return tab.OriginalHostClose;
            if (tab.Host is not { } originalHost) return Task.CompletedTask;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            tab.OriginalHostClose = ReleaseOriginalHostAsync(start.Task, tab, originalHost);
            _originalNativeSources.Add(tab.OriginalHostClose);
            start.SetResult(); return tab.OriginalHostClose;
        }
    }
    private async Task ReleaseOriginalHostAsync(Task start, TabRuntime tab, IBrowseEngineTab originalHost)
    {
        await start.ConfigureAwait(false);
        if (tab.PopupHandler is not null) originalHost.PopupRequested -= tab.PopupHandler;
        if (tab.CrashHandler is not null) originalHost.Crashed -= tab.CrashHandler;
        if (tab.NativeStateHandler is not null) originalHost.StateChanged -= tab.NativeStateHandler;
        if (originalHost is IBrowseOriginalNavigationStateSource navigationState && tab.NavigationStateHandler is not null)
            navigationState.NavigationStateChanged -= tab.NavigationStateHandler;
        if (originalHost is IBrowseOriginalPageCommitSource committed && tab.NativeCommitHandler is not null)
            committed.PageCommitted -= tab.NativeCommitHandler;
        tab.Session.Detach(originalHost);
        var originalClose = originalHost.DisposeAsync().AsTask();
        lock (_nativeSourceGate) _originalNativeSources.Add(originalClose);
        await originalClose.ConfigureAwait(false);
        if (!ReferenceEquals(tab.Host, originalHost)) throw new InvalidOperationException("The original tab renderer changed during retirement.");
        tab.Host = null;
    }

    private void ApplyHostState(TabRuntime tab, BrowserSnapshot state)
    {
        // Engine-private addresses (including about:blank) cannot reclassify the
        // canonical tab as a privileged native surface.
        if (state.Address is { Scheme: "http" or "https" } web && AppContentAddress.TryParse(web.AbsoluteUri, out var parsed)) tab.Address = parsed!;
        tab.Title = string.IsNullOrWhiteSpace(state.Title) ? tab.Address.Authority : state.Title.Trim();
        tab.CanGoBack = state.CanGoBack;
        tab.CanGoForward = state.CanGoForward;
        tab.IsLoading = state.IsLoading;
        tab.Status = state.Status;
    }

    private static (string Origin, BrowserSitePermissionKind Kind) PrivatePermissionKey(Uri address, BrowserSitePermissionKind kind) =>
        (BrowserSitePermissionStore.CanonicalOrigin(address).ToUpperInvariant(), kind);

    private BrowserSitePermissionDecision GetPermission(TabRuntime tab, BrowserSitePermissionKind kind) =>
        !tab.Address.IsWeb ? BrowserSitePermissionDecision.Ask :
        tab.Privacy == BrowserTabPrivacy.Private
            ? tab.PrivatePermissions.GetValueOrDefault(PrivatePermissionKey(tab.Address.WebUri!, kind), BrowserSitePermissionDecision.Ask)
            : _permissions.GetDecision(tab.Address.WebUri!, kind);

    private BrowserTabState[] CaptureStandardTabs()
    {
        // The shared store sorts by UpdatedAt. One session snapshot must use
        // one timestamp so its stable order survives that sort and restoration.
        var capturedAt = DateTimeOffset.UtcNow;
        return _tabs.Where(tab => tab.Privacy == BrowserTabPrivacy.Standard)
            .Select(tab => new BrowserTabState(tab.Id, tab.Title, tab.Address.Value, tab.Privacy, tab.Group, capturedAt)
                { ProfileId = tab.ProfileId, IsPinned = tab.IsPinned }).ToArray();
    }

    private async Task SaveTabsAsync(CancellationToken cancellationToken)
    {
        // An already accepted native commit must finish before a later tab edit
        // persists its current snapshot, otherwise that old commit can restore
        // a closed or replaced tab after the user action succeeds.
        if (_lastOriginalPageCommit is { } originalCommit) await originalCommit.ConfigureAwait(false);
        await SaveTabProjectionAsync(CaptureStandardTabs(), PersistedSelectedTabId(), cancellationToken).ConfigureAwait(false);
    }
    private Guid? PersistedSelectedTabId() => _tabs.Any(tab => tab.Id == _selectedTabId && tab.Privacy == BrowserTabPrivacy.Standard)
        ? _selectedTabId : null;
    private Task SaveTabProjectionAsync(IReadOnlyList<BrowserTabState> tabs, Guid? selectedTabId, CancellationToken token)
    {
        var original = _tabSessionPersistence is null ? _data.SaveTabsAsync(tabs, token)
            : _tabSessionPersistence.SaveAsync(new(tabs, selectedTabId), token);
        if (original is null) throw new InvalidOperationException("The workspace owner supplied no original session save task.");
        RetainAppSource(original); return original;
    }

    private BrowseChromeSnapshot CaptureState()
    {
        ThrowIfDisposed();
        var selected = SelectedRuntime();
        var tabs = _tabs.Select(tab => new BrowseTabSnapshot(
            tab.Id, tab.Title, tab.Address, tab.Privacy, tab.EngineState,
            tab.CanGoBack, tab.CanGoForward, tab.IsLoading, tab.Status)
            { Engine = tab.Engine, NativePage = tab.NativePage, ProfileId = tab.ProfileId, IconKey = tab.IconKey,
                IsHome = tab.IsHome, IsPinned = tab.IsPinned, IsCloseable = !tab.IsHome }).ToArray();
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
                _downloads)
            {
                Pending = ObserveCurrentPendingDownloads(),
                ReviewStatus = CurrentDownloadReviewStatus()
            },
            security,
            selected.FindQuery,
            selected.ZoomPercent,
            _popupStatus,
            _status);
    }

    private static BrowseSecuritySnapshot SecurityFor(TabRuntime tab) =>
        !tab.Address.IsWeb
        ? new(BrowseTransportSecurity.NoPage, tab.Address.Namespace == AppContentNamespace.NativeApp ? "Native 9to1 app" : "Native Browse page",
            "This native surface uses its owning app's permissions. It is not a web origin and grants no authority to a referring site.")
        : tab.EngineState != BrowseEngineState.Ready || tab.Host is null ||
        (tab.Host is IBrowseOriginalPageCommitSource && (!tab.HasOriginalCommittedPage || tab.IsLoading))
        ? new(BrowseTransportSecurity.NoPage, "No committed web page", "Transport security is available after the actual native page commits.")
        : tab.Address.WebUri?.Scheme switch
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
    private static void EnsureWebAddress(AppContentAddress address)
    {
        if (!address.IsWeb)
            throw new InvalidOperationException("This action requires an HTTP or HTTPS page.");
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private void ThrowIfCannotAccept() => ObjectDisposedException.ThrowIf(_nativeRetiring || _disposed, this);

    /// <summary>Rejects an original callback joining its own retirement without starting close.</summary>
    public void DemandExternalOriginalRetirementJoin()
    {
        lock (_nativeSourceGate)
        {
            if (_executingContentTransition.Value is { } content && _contentTransitions.Contains(content) ||
                _executingWindowPreparation.Value is { } preparation && ReferenceEquals(_windowPreparation, preparation) ||
                _executingWindowCommit.Value is { } commit && ReferenceEquals(_windowPreparation, commit))
                throw new InvalidOperationException("An admitted content/preparation callback must return before its external owner joins Browse retirement.");
        }
        for (var notification = LogicalRetirementNotification.Value; notification is not null; notification = notification.Parent)
            if (notification.Active && ReferenceEquals(notification.Owner, this))
                throw new InvalidOperationException("A retirement observer cannot join its own original close.");
        if (ReferenceEquals(LogicalNativeSource.Value, this)) throw new InvalidOperationException("The original native callback cannot join its own Browse retirement.");
        foreach (var tab in _tabs) if (tab.Host is IBrowseEngineRetirementGuard guard) guard.DemandExternalJoin();
    }

    public ValueTask DisposeAsync()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_nativeSourceGate)
        {
            if (_originalChromeClose is not null) return new(_originalChromeClose);
            if (_pendingExternalClose is not null) return new(_pendingExternalClose);
            if (HasAppContent && _tabs.Any(tab => tab.AppOwner?.Session is
                IBrowseAppContentClosePreparationParticipant { SupportsReversibleClosePreparation: true }))
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingExternalClose = PrepareStrongExternalCloseAsync(gate.Task);
                gate.SetResult(); return new(_pendingExternalClose);
            }
            if (HasAppContent)
            {
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingExternalClose = PrepareExternalCloseAsync(gate.Task);
                gate.SetResult(); return new(_pendingExternalClose);
            }
            return new(BeginAcceptedClose());
        }
    }
    private Task? _pendingExternalClose;
    private async Task PrepareExternalCloseAsync(Task start)
    {
        await start.ConfigureAwait(false);
        try
        {
            if (!await TryPrepareToCloseAsync().ConfigureAwait(false))
                throw new BrowseCloseVetoException();
            Task actual;
            lock (_nativeSourceGate)
            {
                _nativeRetiring = true;
                _originalChromeClose = _pendingExternalClose;
                actual = CloseOriginalChromeAsync(Task.CompletedTask);
            }
            await actual.ConfigureAwait(false);
        }
        finally { lock (_nativeSourceGate) if (_originalChromeClose is null) _pendingExternalClose = null; }
    }
    private Task RetireFailedStartupAsync()
    { lock (_nativeSourceGate) return _originalChromeClose ?? BeginAcceptedClose(); }
    private Task BeginAcceptedClose()
    {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _nativeRetiring = true;
            _originalChromeClose = CloseOriginalChromeAsync(start.Task);
            start.SetResult(); return _originalChromeClose;
    }
    private async Task CloseOriginalChromeAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        Task[] activeTransitions;
        lock (_nativeSourceGate) activeTransitions = _originalContentTransitions.Where(actual => !actual.IsCompleted).ToArray();
        var priorNotification = LogicalRetirementNotification.Value;
        var notification = new RetirementNotification(this, priorNotification);
        LogicalRetirementNotification.Value = notification;
        try
        {
            foreach (var observer in RetirementStarted?.GetInvocationList().Cast<EventHandler>() ?? [])
                try { observer(this, EventArgs.Empty); } catch (Exception failure) { failures.Add(failure); }
        }
        finally { notification.Active = false; LogicalRetirementNotification.Value = priorNotification; }
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        async Task DrainOriginalSourcesAsync()
        {
            while (true)
            {
                Task[] originals; lock (_nativeSourceGate) originals = _originalNativeSources.Where(source => !joined.Contains(source)).Distinct().ToArray();
                if (originals.Length == 0) return;
                foreach (var source in originals)
                { joined.Add(source); try { await source.ConfigureAwait(false); } catch (Exception failure) { if (!TryRetireAdmissionSource(source)) AddCloseCauses(failures, source, failure); } }
            }
        }
        var releases = new List<Task>();
        foreach (var tab in _tabs.ToArray())
        {
            try { tab.NavigationCancellation?.Cancel(); } catch (Exception failure) { failures.Add(failure); }
            try { releases.Add(ReleaseContentAsync(tab)); } catch (Exception failure) { failures.Add(failure); }
        }
        foreach (var release in releases)
            try { await release.ConfigureAwait(false); } catch (Exception failure) { AddCloseCauses(failures, release, failure); }
        foreach (var active in activeTransitions)
            try { await active.ConfigureAwait(false); }
            catch (Exception failure) { if (!TryRetireAdmissionSource(active)) AddCloseCauses(failures, active, failure); }
        await DrainOriginalSourcesAsync().ConfigureAwait(false);
        // An admitted source may acquire its final native owner while cancellation
        // settles. Close those exact late acquisitions before the terminal drain.
        foreach (var tab in _tabs.ToArray())
            try { await ReleaseContentAsync(tab).ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
        await DrainOriginalSourcesAsync().ConfigureAwait(false);
        // A no-effect admission may be retained only in the transition set. Withdraw it
        // from both sets here so the inspectable accounting matches the ledger; genuine
        // causes were already added to 'failures' above and stay blocking.
        lock (_nativeSourceGate)
        {
            PruneAdmissionRefusalsLocked(_originalContentTransitions);
            PruneAdmissionRefusalsLocked(_originalNativeSources);
        }
        // Keep the same tabs, stores and failed sources available to their owner
        // when any effect/retirement remains unsuccessful.
        if (failures.Count != 0) throw new AggregateException("Browse retained its original native source/resource failures.", failures);
        foreach (var tab in _tabs.ToArray())
        {
            tab.NavigationCancellation?.Dispose(); tab.Session.Dispose();
            if (tab.Privacy == BrowserTabPrivacy.Private)
            {
                var originalCleanup = _privateProfiles.CleanupAsync(tab.Id, CancellationToken.None);
                lock (_nativeSourceGate) _originalNativeSources.Add(originalCleanup);
                try { await originalCleanup.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); }
            }
        }
        if (failures.Count != 0) throw new AggregateException("Browse retained its original profile retirement failures.", failures);
        _permissions.Dispose(); _data.Dispose(); _tabs.Clear();
        _disposed = true; GC.SuppressFinalize(this);
    }

    private sealed class TabRuntime(
        Guid id,
        string title,
        AppContentAddress address,
        BrowserTabPrivacy privacy,
        string group,
        BrowserSessionService session,
        BrowseEngineKind engine,
        string profileId)
    {
        public Guid Id { get; } = id;
        public string Title { get; set; } = title;
        public AppContentAddress Address { get; set; } = address;
        public string ProfileId { get; } = profileId;
        public bool IsHome => IsHomeAddress(Address);
        public bool IsPinned { get; set; }
        public string IconKey { get; set; } = "web";
        public AppContentOwner? AppOwner { get; set; }
        public List<AppContentOwner> AppOwners { get; } = [];
        public List<IBrowseEngineTab> PendingHosts { get; } = [];
        public Dictionary<IBrowseEngineTab, Task> PendingHostCloses { get; } = new(ReferenceEqualityComparer.Instance);
        public BrowserTabPrivacy Privacy { get; } = privacy;
        public string Group { get; } = group;
        public BrowserSessionService Session { get; } = session;
        public BrowseEngineKind Engine { get; set; } = engine;
        public BrowserNativePage NativePage { get; set; }
        public IBrowseEngineTab? Host { get; set; }
        public Task? OriginalHostClose { get; set; }
        public EventHandler<BrowsePopupRequest>? PopupHandler { get; set; }
        public EventHandler<BrowseEngineCrash>? CrashHandler { get; set; }
        public EventHandler<BrowserSnapshot>? NativeStateHandler { get; set; }
        public EventHandler<BrowserSnapshot>? NativeCommitHandler { get; set; }
        public EventHandler<BrowserSnapshot>? NavigationStateHandler { get; set; }
        public BrowseEngineState EngineState { get; set; }
        public bool CanGoBack { get; set; }
        public bool CanGoForward { get; set; }
        public bool IsLoading { get; set; }
        public bool HasOriginalCommittedPage { get; set; }
        public string Status { get; set; } = "Idle";
        public string FindQuery { get; set; } = string.Empty;
        public int ZoomPercent { get; set; } = 100;
        public Dictionary<(string Origin, BrowserSitePermissionKind Kind), BrowserSitePermissionDecision> PrivatePermissions { get; } = [];
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
