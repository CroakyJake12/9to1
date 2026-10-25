using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Browser;

namespace HavenOS.Apps.Browse;

/// <summary>Canonical CUI bindings consume the same Browse-owned tab/session
/// services. The supplied availability observer does not grant permissions.</summary>
public sealed partial class BrowseNativeWorkspace : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly AsyncLocal<CommandInvocation?> LogicalDriver = new();
    private sealed class CommandInvocation(BrowseNativeWorkspace owner, CommandInvocation? parent)
    {
        public BrowseNativeWorkspace Owner { get; } = owner;
        public CommandInvocation? Parent { get; } = parent;
        public volatile bool Active = true;

        /// <summary>The exact outer action task this invocation is. The creator binds it as
        /// soon as the driver task exists, before the driver can reach any operation. Every
        /// operation the driver directly awaits records a causal edge from this task, which is
        /// how the chrome proves an admission's own propagation chain instead of treating any
        /// task that happened to be pending at the witness as part of it.</summary>
        public Task? Self;
    }
    private bool IsInsideOriginalCommand()
    {
        for (var invocation = LogicalDriver.Value; invocation is not null; invocation = invocation.Parent)
            if (invocation.Active && ReferenceEquals(invocation.Owner, this)) return true;
        return false;
    }
    private readonly BrowseChrome _chrome;
    private readonly Func<string, bool> _originalActionAvailability;
    private readonly IHavenHostContext? _hostContext;
    private readonly object _sourceGate = new();
    private readonly List<Task> _originalCommands = [], _originalOperations = [], _originalNotifications = [], _originalUiOperations = [];
    private BrowseChromeSnapshot _snapshot;
    private TabRow[] _tabRows = [];
    private SavedEntryRow[] _bookmarkRows = [], _historyRows = [], _downloadRows = [];
    private long _savedEpoch;
    private long _rowEpoch;
    private string _address, _find = string.Empty, _historyQuery = string.Empty, _status;
    private bool _addressDraft, _busy, _ready, _disposed, _toolsOpen, _profileMenuOpen, _manageUsersOpen;
    private volatile bool _retiring;
    private Task? _originalCommand, _originalClose, _originalChromeClose, _pendingCloseAttempt;
    private readonly List<Task> _closeAttempts = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    public BrowseChromeSnapshot State => _snapshot;
    public bool IsOriginalPublicationCurrent => !_retiring && !_disposed && _chrome.OriginalClose is null;
    public bool IsReady => _ready && IsOriginalPublicationCurrent && _pendingCloseAttempt is null;
    public BrowseChrome OriginalChrome => _chrome;
    public Task? OriginalCommand => _originalCommand;
    public Task? OriginalClose => _originalClose ?? (_chrome.OriginalClose is not null ? _pendingCloseAttempt : null);
    public IReadOnlyList<Task> OriginalCloseAttempts => _closeAttempts.ToArray();
    public Task? OriginalChromeClose => _originalChromeClose;
    public IReadOnlyList<Task> OriginalCommands { get { lock (_sourceGate) return _originalCommands.ToArray(); } }
    public IReadOnlyList<Task> OriginalOperations { get { lock (_sourceGate) return _originalOperations.ToArray(); } }
    public IReadOnlyList<Task> OriginalNotifications { get { lock (_sourceGate) return _originalNotifications.Concat(_originalUiOperations).ToArray(); } }

    public BrowseNativeWorkspace(BrowseChrome originalChrome, Func<string, bool> originalActionAvailability, IBrowserOriginalDownloadFilesService? originalDownloadFilesService = null, IHavenHostContext? sharedContext = null)
    {
        _originalDownloadFilesService = originalDownloadFilesService;
        _hostContext = sharedContext;
        _chrome = originalChrome ?? throw new ArgumentNullException(nameof(originalChrome));
        _originalActionAvailability = originalActionAvailability ?? throw new ArgumentNullException(nameof(originalActionAvailability));
        _snapshot = _chrome.State; _address = _snapshot.SelectedTab.Address.ToString(); _status = _snapshot.Status;
        IssueCurrentRows(); IssueSavedEntryRows(); IssuePendingDownloadRows(); _chrome.StateChanged += OnChromeStateChanged;
        _chrome.RetirementStarted += OnChromeRetirementStarted;
    }
    public static CuiDocument LoadDocument()
    {
        using var stream = typeof(BrowseNativeWorkspace).Assembly.GetManifestResourceStream("HavenOS.Apps.Browse.NativeUI.UI.BrowseWorkspace.cui")
            ?? throw new InvalidOperationException("The authored Browse CUI document is missing.");
        using var reader = new StreamReader(stream);
        return new CuiRichParser().Parse(reader.ReadToEnd());
    }
    public void ObserveReadiness(CuiSceneAvailability actualAvailability)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_retiring || _disposed) return;
        _ready = actualAvailability.State == CuiSceneAvailabilityState.Ready;
        if (!_ready) _status = actualAvailability.Message;
        Changed();
    }
    public bool TryGetValue(string path, out object? value)
    {
        if (path.StartsWith("Can.", StringComparison.Ordinal))
        { value = IsActionAvailable("9to1.Browse." + path[4..]) == true; return true; }
        if (TryGetDownloadFilesBinding(path, out value)) return true;
        if (TryGetPendingDownloadBinding(path, out value)) return true;
        var profileData = _hostContext as IHavenProfileDataContext;
        var selected = _snapshot.SelectedTab;
        // Capture the host's current typed model snapshot once per query; the bindings read it
        // live (never caching invented parallel state), so the host's own projection refresh is
        // what repaints this seam.
        var model = _hostContext?.ModelInfo;
        value = path switch
        {
            "Tabs" => _tabRows,
            "SelectedTabTarget" => _tabRows.FirstOrDefault(row => row.Selected)?.Target,
            "PinTabLabel" => selected.IsPinned ? "Unpin selected tab" : "Pin selected tab",
            "TabPinned" => selected.IsPinned,
            "HasUnsavedTabPlacement" => _chrome.HasUnsavedTabPlacement,
            "HasUnsavedSplitViewLayout" => _chrome.HasUnsavedSplitViewLayout,
            "HasSplitView" => _chrome.HasSplitView,
            "SplitOrientationLabel" => _snapshot.SplitOrientation == SplitOrientation.Vertical ? "Side by side" : "Stacked",
            "SplitRatioLabel" => "Primary pane " + (_snapshot.PrimaryPaneRatio * 100).ToString("N0", CultureInfo.CurrentCulture) + "%",
            "ToggleSplitLabel" => _chrome.HasSplitView ? "Close split view" : "Split selected tab with another tab",
            "SplitPrimaryLabel" => (_snapshot.SplitOrientation == SplitOrientation.Vertical ? "Focus left pane" : "Focus top pane") +
                (_snapshot.PaneTabIds.Count == 2 ? ": " + (_snapshot.Tabs.FirstOrDefault(tab => tab.Id == _snapshot.PaneTabIds[0])?.Title ?? "Tab") : string.Empty),
            "SplitSecondaryLabel" => (_snapshot.SplitOrientation == SplitOrientation.Vertical ? "Focus right pane" : "Focus bottom pane") +
                (_snapshot.PaneTabIds.Count == 2 ? ": " + (_snapshot.Tabs.FirstOrDefault(tab => tab.Id == _snapshot.PaneTabIds[1])?.Title ?? "Tab") : string.Empty),
            "ToolsOpen" => _toolsOpen,
            "IsLoading" => selected.IsLoading,
            "IsNotLoading" => !selected.IsLoading,
            "IsWebPage" => selected.Address.IsWeb,
            "IsNativePage" => selected.NativePage != BrowserNativePage.None,
            "IsHistory" => selected.NativePage == BrowserNativePage.History,
            "IsBookmarks" => selected.NativePage == BrowserNativePage.Bookmarks,
            "IsDownloads" => selected.NativePage == BrowserNativePage.Downloads,
            "IsSettings" => selected.NativePage == BrowserNativePage.Settings,
            "RecordHistory" => _chrome.HistoryRecordingEnabled,
            "RestoreTabs" => _chrome.TabRestoreEnabled,
            "HasUnassignedLegacyProfileData" => _chrome.HasUnassignedLegacyProfileData,
            "LegacyProfileDataStatus" => _chrome.HasUnassignedLegacyProfileData
                ? "Older browser storage has no verified profile owner. It is preserved and isolated until reviewed; no account data was imported."
                : string.Empty,
            "NativeTitle" => selected.Title,
            "EmptyBookmarks" => _bookmarkRows.Length == 0,
            "EmptyHistory" => _snapshot.History.Count == 0,
            "EmptyHistoryResults" => _snapshot.History.Count > 0 && _historyRows.Length == 0,
            "EmptyDownloads" => _downloadRows.Length == 0,
            "Address" => _address, "AddressDisplay" => ResolveAddressDisplay(selected), "FindQuery" => _find, "Status" => _status,
            "Security" => _snapshot.Security.Label + " · " + _snapshot.Security.Explanation,
            "Profile" => selected.Privacy == BrowserTabPrivacy.Private ? "Private tab · isolated temporary profile" : "Standard profile",
            "Engine" => selected.Engine == BrowseEngineKind.Gecko ? "Firefox" : "Chromium",
            "Zoom" => _snapshot.ZoomPercent.ToString(CultureInfo.CurrentCulture) + "%",
            "PopupStatus" => _snapshot.PopupStatus,
            "EngineStatus" => selected.EngineState switch
            {
                BrowseEngineState.Crashed => "The tab's renderer crashed. Recover the same tab to continue.",
                BrowseEngineState.Unsupported => selected.Status,
                _ => selected.IsLoading ? "Loading…" : selected.Status
            },
            "ShowChromiumFallback" => selected.Address.IsWeb && selected.Engine == BrowseEngineKind.Gecko &&
                selected.EngineState == BrowseEngineState.Unsupported &&
                _chrome.QueryEngineCapability(BrowseEngineKind.Chromium),
            "ChromiumFallbackStatus" => selected.Status,
            "Bookmarks" => _bookmarkRows,
            "IsBookmarked" => SelectedPageIsBookmarked(_snapshot),
            "BookmarkGlyph" => SelectedPageIsBookmarked(_snapshot) ? "★" : "☆",
            "BookmarkLabel" => SelectedPageIsBookmarked(_snapshot) ? "Remove bookmark for this page" : "Bookmark page",
            "History" => _historyRows,
            "HistoryQuery" => _historyQuery,
            "HasHistoryQuery" => _historyQuery.Length > 0,
            "HistoryResultSummary" => $"Showing {_historyRows.Length.ToString("N0", CultureInfo.CurrentCulture)} of {_snapshot.History.Count.ToString("N0", CultureInfo.CurrentCulture)} visits",
            "Downloads" => _downloadRows,
            "Haven.Embedded" => _hostContext?.IsEmbedded == true,
            "Haven.ModelAvailable" => _hostContext?.ModelSupported == true,
            "Haven.NotificationsAvailable" => _hostContext?.NotificationsSupported == true,
            "Haven.ProfileAvailable" => _hostContext?.ProfileSupported == true,
            "Haven.ModelName" => model?.Name ?? string.Empty,
            "Haven.EffortLabel" => ModelDisplayPercent(model),
            "Haven.EffortPercent" => ModelDisplayPercent(model),
            "Haven.EffortKnown" => !string.IsNullOrEmpty(ModelDisplayPercent(model)),
            "Haven.EffortConfigurable" => model?.EffortConfigurable == true,
            "Haven.UsageVisible" => model?.UsageVisible == true,
            "Haven.ContextVisible" => model?.ContextVisible == true,
            "Haven.MeterMarker" => model?.MeterMarker == true,
            "Haven.ModelAccessibleName" => ModelAccessibleName(model),
            "Haven.ModelTooltip" => ModelTooltip(model),
            "ProfileMenuOpen" => _profileMenuOpen,
            "ProfilePrincipalLabel" => profileData?.Profile is { } principal && !string.IsNullOrWhiteSpace(principal.DisplayName)
                ? principal.DisplayName
                : (_hostContext?.ProfileSupported == true ? "Account/User" : "Sign-in unavailable"),
            "ProfileAccountHint" => _hostContext?.ProfileSupported == true
                ? "Click to open account and user settings."
                : "The native account owner is unavailable in this session.",
            "ManageUsersOpen" => _manageUsersOpen,
            "ManageUsersOtherProfilesStatus" => "Other profiles are not manageable: the user-management service is unavailable for this session.",
            "ShowManageUsersOtherProfilesStatus" => true,
            "ShowStorage" => profileData?.Storage is { } storage && storage.IsRenderable,
            "StorageLabel" => profileData?.Storage is { } storage && storage.IsRenderable
                ? $"{storage.Label}: {FormatBytes(storage.UsedBytes)} of {FormatBytes(storage.QuotaBytes)}"
                : string.Empty,
            "StorageUnitsLabel" => profileData?.Storage is { } storage && storage.IsRenderable
                ? $"{storage.Label}: {FormatBytes(storage.UsedBytes)} of {FormatBytes(storage.QuotaBytes)}"
                : string.Empty,
            "StorageUsedBytes" => profileData?.Storage is { } storage && storage.IsRenderable ? (double)storage.UsedBytes : 0d,
            "StorageQuotaBytes" => profileData?.Storage is { } storage && storage.IsRenderable ? (double)storage.QuotaBytes : 100d,
            "ShowUsage" => profileData?.UsageWindows.FirstOrDefault(window => window.IsRenderable) is not null,
            "UsageLabel" => ProfileUsageLabel(profileData),
            "UsageWindowRows" => profileData?.UsageWindows.Where(window => window.IsRenderable)
                .Select(window => new UsageWindowRow(window.Name,
                    $"{window.Name}: {window.UsedUnits.ToString("N0", CultureInfo.CurrentCulture)} of {window.LimitUnits.ToString("N0", CultureInfo.CurrentCulture)} {window.Unit}",
                    window.UsedUnits, window.LimitUnits)).ToArray() ?? [],
            "AutomationNote" => "Web automations are unavailable in this session.",
            "ShowAutomationNote" => profileData?.Automations is null,
            "AllowPopups" => SelectedWebPopupsAllowed(_snapshot),
            "WebToolsChevron" => _toolsOpen ? "▾" : "▸",
            "DownloadOpenStatus" => _downloadFilesStatus,
            "DownloadStatus" => _snapshot.Downloads.Status,
            "CanBack" => IsActionAvailable("9to1.Browse.Back") == true,
            "CanForward" => IsActionAvailable("9to1.Browse.Forward") == true,
            "CanEdit" => _ready && !_busy && !_retiring && !_disposed,
            "HasAddressDraft" => _addressDraft,
            _ => null
        };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (_busy || _retiring || _disposed || !_ready) return false;
        switch (path)
        {
            case "Address":
                if (value is not string addressText || addressText.Length > 8192) return false;
                _address = addressText;
                _addressDraft = !string.Equals(addressText, _snapshot.SelectedTab.Address.ToString(), StringComparison.Ordinal);
                break;
            case "FindQuery":
                if (value is not string findText || findText.Length > 8192) return false;
                _find = findText;
                break;
            case "HistoryQuery":
                if (value is not string historyText || historyText.Length > 2048) return false;
                _historyQuery = historyText;
                IssueSavedEntryRows();
                break;
            case "RecordHistory":
            case "RestoreTabs":
                if (value is not bool enabled) return false;
                var settingCommand = path == "RecordHistory"
                    ? (enabled ? "9to1.Browse.EnableHistory" : "9to1.Browse.DisableHistory")
                    : (enabled ? "9to1.Browse.EnableTabRestore" : "9to1.Browse.DisableTabRestore");
                if (IsActionAvailable(settingCommand) != true) return false;
                _ = DispatchAsync(settingCommand, null);
                return true;
            case "TabPinned":
                if (value is not bool pinned) return false;
                // Capture the real issued row BEFORE any borrowed action-policy callback.
                // Revalidate the original tab, row epoch and complete placement stamp after it;
                // DispatchAsync checks the SAME target again before the real persistence action.
                var pinTarget = _tabRows.FirstOrDefault(row => row.Selected)?.Target;
                if (IsActionAvailable("9to1.Browse.ToggleTabPinned") != true) return false;
                if (!IsCurrentSelectedTabTarget(pinTarget)) { Changed(); return false; }
                if (((TabSelection)pinTarget!).IsPinned == pinned) return true;
                _ = DispatchAsync("9to1.Browse.ToggleTabPinned", pinTarget);
                return true;
            case "AllowPopups":
                // The site-popups control is a genuine boolean bound to the real effective
                // WindowManagement permission. Toggling dispatches the real Allow/Deny command
                // (a retained operation); the snapshot drives the visible state, so a failed or
                // refused permission change reverts the switch to the actual effective decision.
                if (value is not bool allowed) return false;
                var command = allowed ? "9to1.Browse.AllowPopups" : "9to1.Browse.DenyPopups";
                if (IsActionAvailable(command) != true) return false;
                _ = DispatchAsync(command, null);
                return true;
            default:
                return false;
        }
        Changed(); return true;
    }
    public bool? IsActionAvailable(string command)
    {
        if (!_ready || _busy || _retiring || _disposed || !_originalActionAvailability(command) || _retiring || _disposed) return false;
        var shared = HavenSharedActionAvailable(command);
        if (shared.HasValue) return shared;
        var tab = _snapshot.SelectedTab; var interactive = tab.EngineState == BrowseEngineState.Ready && _chrome.ObserveOriginalEngine(tab.Id) is not null;
        return command switch
        {
            "9to1.Browse.Back" => interactive && tab.CanGoBack,
            "9to1.Browse.Forward" => interactive && tab.CanGoForward,
            "9to1.Browse.Navigate" or "9to1.Browse.OpenBookmark" or "9to1.Browse.OpenHistory" => tab.EngineState != BrowseEngineState.Crashed,
            "9to1.Browse.Reload" or "9to1.Browse.OpenFind" or "9to1.Browse.FindNext" or "9to1.Browse.FindPrevious" or
                "9to1.Browse.ZoomIn" or "9to1.Browse.ZoomOut" or "9to1.Browse.ZoomReset" or "9to1.Browse.DevTools" => interactive,
            "9to1.Browse.Stop" => interactive && tab.IsLoading,
            "9to1.Browse.Bookmark" or "9to1.Browse.AllowPopups" or "9to1.Browse.DenyPopups" or
                "9to1.Browse.FirefoxSite" or "9to1.Browse.ChromiumSite" or "9to1.Browse.ResetSiteEngine" => tab.Address.IsWeb,
            "9to1.Browse.Recover" => tab.EngineState == BrowseEngineState.Crashed,
            "9to1.Browse.FirefoxTab" or "9to1.Browse.ChromiumTab" or "9to1.Browse.ResetTabEngine" => tab.Address.IsWeb,
            "9to1.Browse.ReopenClosedTab" => _chrome.CanReopenClosedTab,
            "9to1.Browse.RemoveBookmark" => _snapshot.Bookmarks.Count > 0,
            "9to1.Browse.RemoveHistoryEntry" => _snapshot.History.Count > 0,
            "9to1.Browse.ResetHistoryQuery" => _historyQuery.Length > 0,
            "9to1.Browse.ToggleTabPinned" => !tab.IsHome,
            "9to1.Browse.MoveTabLeft" => _chrome.CanMoveTab(tab.Id, -1),
            "9to1.Browse.MoveTabRight" => _chrome.CanMoveTab(tab.Id, 1),
            "9to1.Browse.SaveTabPlacement" => _chrome.HasUnsavedTabPlacement,
            "9to1.Browse.ReorderTab" => _ready && !_busy && !_retiring && !_disposed,
            "9to1.Browse.ToggleSplitView" => _chrome.HasSplitView || _chrome.CanSplitSelectedTab,
            "9to1.Browse.ToggleSplitOrientation" => _chrome.HasSplitView,
            "9to1.Browse.WidenPrimaryPane" => _chrome.CanWidenPrimaryPane,
            "9to1.Browse.NarrowPrimaryPane" => _chrome.CanNarrowPrimaryPane,
            "9to1.Browse.SaveSplitViewLayout" => _chrome.HasUnsavedSplitViewLayout,
            "9to1.Browse.FocusSplitPrimary" or "9to1.Browse.FocusSplitSecondary" or
                "9to1.Browse.SwapSplitPanes" or "9to1.Browse.CloseSplitView" => _chrome.HasSplitView,
            "9to1.Browse.CloseTab" => tab.IsCloseable,
            "9to1.Browse.CloseTabRow" => _snapshot.Tabs.Any(item => item.IsCloseable),
            "9to1.Browse.ShowDownloadInFiles" => _originalDownloadFilesService is not null,
            "9to1.Browse.RegisterDownloadDestination" => _originalDownloadFilesService is not null && _downloadDestinations.Length > 0,
            "9to1.Browse.RetryDownloadRegistration" => CanRetryOriginalDownloadRegistration,
            "9to1.Browse.Downloads" => _snapshot.Downloads.IsSupported,
            "9to1.Browse.ApprovePendingDownload" => _pendingDownloadRows.Any(row => IsCurrentPendingDownloadTarget(row.Target, approving: true)),
            "9to1.Browse.RejectPendingDownload" => _pendingDownloadRows.Any(row => IsCurrentPendingDownloadTarget(row.Target, approving: false)),
            "9to1.Browse.NewTab" or "9to1.Browse.NewPrivateTab" or "9to1.Browse.SelectTab" or
                "9to1.Browse.ClearHistory" or "9to1.Browse.OpenHistoryPage" or "9to1.Browse.OpenBookmarksPage" or
                "9to1.Browse.OpenDownloadsPage" or "9to1.Browse.OpenSettingsPage" or
                "9to1.Browse.DefaultFirefox" or "9to1.Browse.DefaultChromium" or
                "9to1.Browse.EnableHistory" or "9to1.Browse.DisableHistory" or
                "9to1.Browse.EnableTabRestore" or "9to1.Browse.DisableTabRestore" => true,
            "9to1.Browse.DiscardAddress" => _addressDraft,
            "9to1.Browse.ToggleTools" => true,
            "9to1.Browse.CloseTools" => _toolsOpen,
            "9to1.Browse.ToggleProfileMenu" => true,
            "9to1.Browse.CloseProfileMenu" => _profileMenuOpen,
            "9to1.Browse.OpenManageUsers" => true,
            "9to1.Browse.CloseManageUsers" => _manageUsersOpen,
            "9to1.Browse.OpenWebAutomations" => (_hostContext as IHavenProfileDataContext)?.Automations is not null,
            "9to1.Browse.SignOut" => false,
            _ => false
        };
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
    {
        Dispatcher.UIThread.VerifyAccess(); token.ThrowIfCancellationRequested();
        if (IsActionAvailable(command) != true) return ValueTask.CompletedTask;
        if (IsHavenSharedCommand(command))
        {
            // Shared rail opens are genuine retained operations: reserve admission before
            // invoking the gateway and return the real settlement so the workspace's close
            // joins actual show/init/account faults and refusals. The open runs inside the
            // same CommandInvocation/logical-driver guard as ordinary commands, so a
            // reentrant provider callback that asks to close cannot join its own in-flight
            // action.
            lock (_sourceGate) CheckAdmission(_originalOperations);
            var sharedStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sharedPrior = LogicalDriver.Value;
            var sharedInvocation = new CommandInvocation(this, sharedPrior);
            var sharedOriginal = RunSharedOpenAsync(sharedStart.Task, command, token, sharedInvocation);
            sharedInvocation.Self = sharedOriginal;
            lock (_sourceGate) Retain(_originalOperations, sharedOriginal);
            sharedStart.SetResult();
            return new(sharedOriginal);
        }
        if ((command is "9to1.Browse.SelectTab" or "9to1.Browse.CloseTabRow") && !IsCurrentTabTarget(parameter))
        { _status = "The tab row changed. Select its current row again."; Changed(); return ValueTask.CompletedTask; }
        if (IsTabPlacementCommand(command) && !IsCurrentSelectedTabTarget(parameter))
        { _status = "The selected tab changed. Use its current tab controls."; Changed(); return ValueTask.CompletedTask; }
        if (command == "9to1.Browse.ReorderTab" &&
            (parameter is not TabReorderSelection reorder || !ReferenceEquals(reorder.Owner, this) ||
             !IsCurrentTabTarget(reorder.Source) || !IsCurrentTabTarget(reorder.Target)))
        { _status = "The tab rows changed. Drag their current rows again."; Changed(); return ValueTask.CompletedTask; }
        if (command == "9to1.Browse.CloseTabRow" &&
            !_chrome.State.Tabs.Any(tab => tab.Id == ((TabSelection)parameter!).TabId && tab.IsCloseable))
            return ValueTask.CompletedTask;
        if ((command == "9to1.Browse.OpenBookmark" && !IsCurrentSavedTarget(parameter, SavedEntryKind.Bookmark)) ||
            (command == "9to1.Browse.OpenHistory" && !IsCurrentSavedTarget(parameter, SavedEntryKind.History)))
        { _status = "The saved entry changed. Open its current row again."; Changed(); return ValueTask.CompletedTask; }
        if (command == "9to1.Browse.RemoveBookmark" && !IsCurrentSavedTarget(parameter, SavedEntryKind.Bookmark))
        { _status = "The bookmark changed. Use its current row again."; Changed(); return ValueTask.CompletedTask; }
        if (command == "9to1.Browse.RemoveHistoryEntry" && !IsCurrentSavedTarget(parameter, SavedEntryKind.History))
        { _status = "The history visit changed. Use its current row again."; Changed(); return ValueTask.CompletedTask; }
        if ((command is "9to1.Browse.ApprovePendingDownload" or "9to1.Browse.RejectPendingDownload") &&
            !IsCurrentPendingDownloadTarget(parameter, command == "9to1.Browse.ApprovePendingDownload"))
        { _status = "This pending download changed or expired. Refresh Downloads and review its current row."; Changed(); return ValueTask.CompletedTask; }
        if (!ValidateOriginalDownloadAdmission(command, parameter))
        { _status = "The download or Files folder changed. Select its current row again."; Changed(); return ValueTask.CompletedTask; }
        lock (_sourceGate) CheckAdmission(_originalCommands);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _busy = true;
        var prior = LogicalDriver.Value;
        var invocation = new CommandInvocation(this, prior);
        var original = RunAsync(start.Task, command, parameter, token, invocation);
        invocation.Self = original;
        _originalCommand = original;
        lock (_sourceGate) Retain(_originalCommands, original);
        Exception? notificationFailure = null;
        try { Changed(); } catch (Exception failure) { notificationFailure = failure; }
        finally { if (notificationFailure is null) start.SetResult(); else start.SetException(notificationFailure); }
        return new(original);
    }
    private async Task RunAsync(Task start, string command, object? parameter, CancellationToken token,
        CommandInvocation invocation)
    {
        var prior = LogicalDriver.Value; LogicalDriver.Value = invocation;
        var failures = new List<Exception>();
        try
        {
            await start; token.ThrowIfCancellationRequested();
            var tabId = _snapshot.SelectedTabId;
            if (command is "9to1.Browse.OpenHistoryPage" or "9to1.Browse.OpenBookmarksPage" or
                "9to1.Browse.OpenDownloadsPage" or "9to1.Browse.OpenSettingsPage" or
                "9to1.Browse.SelectTab" or "9to1.Browse.NewTab" or "9to1.Browse.NewPrivateTab" or
                "9to1.Browse.ReopenClosedTab") _toolsOpen = false;
            switch (command)
            {
                case "9to1.Browse.OpenHistoryPage": await Operation(() => _chrome.OpenNativePageAsync(BrowserNativePage.History, token)); _addressDraft = false; break;
                case "9to1.Browse.OpenBookmarksPage": await Operation(() => _chrome.OpenNativePageAsync(BrowserNativePage.Bookmarks, token)); _addressDraft = false; break;
                case "9to1.Browse.OpenDownloadsPage":
                    await Operation(() => _chrome.OpenNativePageAsync(BrowserNativePage.Downloads, token)); _addressDraft = false;
                    if (_snapshot.Downloads.IsSupported) await Operation(() => _chrome.RefreshDownloadsAsync(token)); break;
                case "9to1.Browse.OpenSettingsPage": await Operation(() => _chrome.OpenNativePageAsync(BrowserNativePage.Settings, token)); _addressDraft = false; break;
                case "9to1.Browse.Navigate": await Operation(() => _chrome.NavigateAsync(_address, token)); _addressDraft = false; break;
                case "9to1.Browse.Back": await Operation(() => _chrome.BackAsync(token)); break;
                case "9to1.Browse.Forward": await Operation(() => _chrome.ForwardAsync(token)); break;
                case "9to1.Browse.Reload": await Operation(() => _chrome.ReloadAsync(token)); break;
                case "9to1.Browse.Stop": await Operation(() => _chrome.StopAsync(token)); break;
                case "9to1.Browse.NewTab": await Operation(() => _chrome.NewTabAsync(false, token)); _addressDraft = false; break;
                case "9to1.Browse.NewPrivateTab": await Operation(() => _chrome.NewTabAsync(true, token)); _addressDraft = false; break;
                case "9to1.Browse.CloseTab": await Operation(() => _chrome.CloseTabAsync(tabId, token)); _addressDraft = false; break;
                case "9to1.Browse.CloseTabRow":
                    if (!IsCurrentTabTarget(parameter)) { _status = "The tab row changed before closing; use its current close control."; break; }
                    var closingTarget = (TabSelection)parameter!;
                    if (!_chrome.State.Tabs.Any(tab => tab.Id == closingTarget.TabId && tab.IsCloseable)) break;
                    await Operation(() => _chrome.CloseTabAsync(closingTarget.TabId, token));
                    if (closingTarget.TabId == tabId) _addressDraft = false;
                    break;
                case "9to1.Browse.ReopenClosedTab": await Operation(() => _chrome.ReopenClosedTabAsync(token)); _addressDraft = false; break;
                case "9to1.Browse.ToggleSplitView": await Operation(() => _chrome.ToggleSplitViewAsync(token)); break;
                case "9to1.Browse.ToggleSplitOrientation": await Operation(() => _chrome.ToggleSplitOrientationAsync(token)); break;
                case "9to1.Browse.WidenPrimaryPane": await Operation(() => _chrome.AdjustPrimaryPaneAsync(1, token)); break;
                case "9to1.Browse.NarrowPrimaryPane": await Operation(() => _chrome.AdjustPrimaryPaneAsync(-1, token)); break;
                case "9to1.Browse.SaveSplitViewLayout": await Operation(() => _chrome.SaveSplitViewLayoutAsync(token)); break;
                case "9to1.Browse.FocusSplitPrimary":
                    if (_snapshot.PaneTabIds.Count == 2) await Operation(() => _chrome.FocusSplitPaneAsync(_snapshot.PaneTabIds[0], token));
                    break;
                case "9to1.Browse.FocusSplitSecondary":
                    if (_snapshot.PaneTabIds.Count == 2) await Operation(() => _chrome.FocusSplitPaneAsync(_snapshot.PaneTabIds[1], token));
                    break;
                case "9to1.Browse.SwapSplitPanes": await Operation(() => _chrome.SwapSplitPanesAsync(token)); break;
                case "9to1.Browse.CloseSplitView": await Operation(() => _chrome.CloseSplitViewAsync(token)); break;
                case "9to1.Browse.ToggleTabPinned":
                case "9to1.Browse.MoveTabLeft":
                case "9to1.Browse.MoveTabRight":
                    if (!IsCurrentSelectedTabTarget(parameter)) { _status = "The selected tab changed before arranging it; use its current controls."; break; }
                    var placementTarget = (TabSelection)parameter!;
                    if (command == "9to1.Browse.ToggleTabPinned")
                        await Operation(() => _chrome.SetTabPinnedAsync(placementTarget.TabId, !placementTarget.IsPinned, token));
                    else
                        await Operation(() => _chrome.MoveTabAsync(placementTarget.TabId, command == "9to1.Browse.MoveTabLeft" ? -1 : 1, token));
                    break;
                case "9to1.Browse.SaveTabPlacement": await Operation(() => _chrome.SaveTabPlacementAsync(token)); break;
                case "9to1.Browse.ReorderTab":
                    var reorderTarget = (TabReorderSelection)parameter!;
                    await Operation(() => _chrome.MoveTabRelativeAsync(reorderTarget.Source.TabId, reorderTarget.Target.TabId,
                        reorderTarget.BeforeTarget, token));
                    break;
                case "9to1.Browse.SelectTab":
                    if (!IsCurrentTabTarget(parameter)) { _status = "The tab row changed before selection; select its current row again."; break; }
                    var selectedTarget = (TabSelection)parameter!;
                    await Operation(() => _chrome.SelectTabAsync(selectedTarget.TabId, token)); _addressDraft = false; break;
                case "9to1.Browse.OpenBookmark":
                case "9to1.Browse.OpenHistory":
                    var entryKind = command == "9to1.Browse.OpenBookmark" ? SavedEntryKind.Bookmark : SavedEntryKind.History;
                    if (!IsCurrentSavedTarget(parameter, entryKind)) { _status = "The saved entry changed before navigation; open its current row again."; break; }
                    var entry = (SavedEntrySelection)parameter!;
                    await Operation(() => entryKind == SavedEntryKind.Bookmark ? _chrome.OpenBookmarkAsync(entry.Id, token) : _chrome.OpenHistoryEntryAsync(entry.Id, token));
                    _addressDraft = false; break;
                case "9to1.Browse.ResetTabEngine": await Operation(() => _chrome.ResetSelectedTabEngineAsync(token)); break;
                case "9to1.Browse.RemoveBookmark":
                    if (!IsCurrentSavedTarget(parameter, SavedEntryKind.Bookmark)) { _status = "The bookmark changed before removal; use its current row again."; break; }
                    var removedBookmark = (SavedEntrySelection)parameter!;
                    await Operation(() => _chrome.RemoveBookmarkAsync(removedBookmark.Id, token));
                    break;
                case "9to1.Browse.RemoveHistoryEntry":
                    if (!IsCurrentSavedTarget(parameter, SavedEntryKind.History)) { _status = "The history visit changed before removal; use its current row again."; break; }
                    var removedVisit = (SavedEntrySelection)parameter!;
                    await Operation(() => _chrome.RemoveHistoryEntryAsync(removedVisit.Id, token));
                    break;
                case "9to1.Browse.ResetHistoryQuery": _historyQuery = string.Empty; break;
                case "9to1.Browse.ResetSiteEngine": await Operation(() => _chrome.ResetSiteEngineAsync(token)); break;
                case "9to1.Browse.Bookmark": await Operation(() => _chrome.ToggleBookmarkAsync(token)); break;
                case "9to1.Browse.ClearHistory": await Operation(() => _chrome.ClearHistoryAsync(token)); break;
                case "9to1.Browse.ShowDownloadInFiles": await ShowOriginalDownloadInFilesAsync(parameter!, token); break;
                case "9to1.Browse.RegisterDownloadDestination": await RegisterOriginalDownloadDestinationAsync(parameter!, token); break;
                case "9to1.Browse.RetryDownloadRegistration": await RetryOriginalDownloadRegistrationAsync(token); break;
                case "9to1.Browse.Downloads": await Operation(() => _chrome.RefreshDownloadsAsync(token)); break;
                case "9to1.Browse.ApprovePendingDownload":
                case "9to1.Browse.RejectPendingDownload":
                    await DecideOriginalPendingDownloadAsync(parameter!, command == "9to1.Browse.ApprovePendingDownload", token); break;
                case "9to1.Browse.OpenFind": _profileMenuOpen = true; _manageUsersOpen = false; _toolsOpen = true; break;
                case "9to1.Browse.FindNext": await Operation(() => _chrome.FindAsync(_find, false, token)); break;
                case "9to1.Browse.FindPrevious": await Operation(() => _chrome.FindAsync(_find, true, token)); break;
                case "9to1.Browse.ZoomIn": await Operation(() => _chrome.SetZoomAsync(_snapshot.ZoomPercent + 10, token)); break;
                case "9to1.Browse.ZoomOut": await Operation(() => _chrome.SetZoomAsync(_snapshot.ZoomPercent - 10, token)); break;
                case "9to1.Browse.ZoomReset": await Operation(() => _chrome.SetZoomAsync(100, token)); break;
                case "9to1.Browse.AllowPopups": await Operation(() => _chrome.SetPermissionAsync(BrowserSitePermissionKind.WindowManagement, BrowserSitePermissionDecision.Allow, token)); break;
                case "9to1.Browse.DenyPopups": await Operation(() => _chrome.SetPermissionAsync(BrowserSitePermissionKind.WindowManagement, BrowserSitePermissionDecision.Deny, token)); break;
                case "9to1.Browse.Recover": await Operation(() => _chrome.RecoverSelectedTabAsync(token)); break;
                case "9to1.Browse.FirefoxTab": await Operation(() => _chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Gecko, token)); break;
                case "9to1.Browse.ChromiumTab": await Operation(() => _chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium, token)); break;
                case "9to1.Browse.FirefoxSite": await Operation(() => _chrome.SetSiteEngineAsync(BrowseEngineKind.Gecko, token)); break;
                case "9to1.Browse.ChromiumSite": await Operation(() => _chrome.SetSiteEngineAsync(BrowseEngineKind.Chromium, token)); break;
                case "9to1.Browse.EnableHistory": await Operation(() => _chrome.SetHistoryRecordingAsync(true, token)); break;
                case "9to1.Browse.DisableHistory": await Operation(() => _chrome.SetHistoryRecordingAsync(false, token)); break;
                case "9to1.Browse.EnableTabRestore": await Operation(() => _chrome.SetTabRestoreAsync(true, token)); break;
                case "9to1.Browse.DisableTabRestore": await Operation(() => _chrome.SetTabRestoreAsync(false, token)); break;
                case "9to1.Browse.DefaultFirefox": await Operation(() => _chrome.SetDefaultEngineAsync(BrowseEngineKind.Gecko, token)); break;
                case "9to1.Browse.DefaultChromium": await Operation(() => _chrome.SetDefaultEngineAsync(BrowseEngineKind.Chromium, token)); break;
                case "9to1.Browse.DevTools":
                    var engine = _chrome.ObserveOriginalEngine(tabId) ?? throw new PlatformNotSupportedException("The original tab engine is unavailable.");
                    var originalTools = engine.OpenDeveloperToolsAsync(token) ?? throw new InvalidOperationException("The tab engine did not produce its original developer-tools task.");
                    lock (_sourceGate) Retain(_originalOperations, originalTools);
                    await originalTools; break;
                case "9to1.Browse.DiscardAddress": _addressDraft = false; break;
                case "9to1.Browse.ToggleTools": _toolsOpen = !_toolsOpen; break;
                case "9to1.Browse.CloseTools": _toolsOpen = false; break;
                case "9to1.Browse.ToggleProfileMenu": _profileMenuOpen = !_profileMenuOpen; if (!_profileMenuOpen) _manageUsersOpen = false; break;
                case "9to1.Browse.CloseProfileMenu": _profileMenuOpen = false; _manageUsersOpen = false; break;
                case "9to1.Browse.OpenManageUsers": _manageUsersOpen = true; break;
                case "9to1.Browse.CloseManageUsers": _manageUsersOpen = false; break;
                case "9to1.Browse.SignOut":
                    _status = "Sign out is managed from Account & user settings.";
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(command));
            }
            if (IsOriginalPublicationCurrent) { ApplySnapshot(_chrome.State); _status = _snapshot.Status; }
        }
        catch (Exception failure)
        {
            failures.Add(failure);
            try { if (IsOriginalPublicationCurrent) ApplySnapshot(_chrome.State); } catch (Exception observationFailure) { failures.Add(observationFailure); }
            _status = "Browse retained the original failed operation: " + failure.Message;
        }
        finally
        {
            _busy = false;
            try { Changed(); } catch (Exception failure) { failures.Add(failure); }
            invocation.Active = false; LogicalDriver.Value = prior;
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Browse retained operation and observer failures.", failures);
    }
    private async Task Operation(Func<Task<BrowseChromeSnapshot>> start)
    {
        lock (_sourceGate) CheckAdmission(_originalOperations);
        var original = start() ?? throw new InvalidOperationException("Browse did not produce its original operation task.");
        lock (_sourceGate) Retain(_originalOperations, original);
        // This is the exact await the current driver performs, so the driver's action task is
        // a genuine propagation source of this operation. Only such a declared edge can join
        // an admission's propagation chain; a source that merely overlapped in time cannot.
        if (LogicalDriver.Value is { } invocation && invocation.Self is { } action)
            _chrome.LinkAdmissionPropagation(action, original);
        await original;
    }
    private void OnChromeRetirementStarted(object? sender, EventArgs args)
    {
        // Core has accepted the ordinary preflight. Keep publication current
        // until this exact boundary; a rejected attempt never reaches it.
        _retiring = true;
        _originalClose ??= _pendingCloseAttempt;
        _originalChromeClose = _chrome.OriginalClose;
        var original = Dispatcher.UIThread.InvokeAsync(Changed).GetTask();
        lock (_sourceGate) Retain(_originalUiOperations, original);
    }
    private void OnChromeStateChanged(object? sender, BrowseChromeSnapshot snapshot)
    {
        if (!IsOriginalPublicationCurrent) return;
        lock (_sourceGate) CheckAdmission(_originalNotifications);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = NotifyAsync(start.Task, snapshot);
        lock (_sourceGate) Retain(_originalNotifications, original);
        start.SetResult();
    }
    private async Task NotifyAsync(Task start, BrowseChromeSnapshot snapshot)
    {
        await start.ConfigureAwait(false);
        var originalUi = Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (IsOriginalPublicationCurrent) ApplySnapshot(_chrome.State);
        }).GetTask();
        lock (_sourceGate) Retain(_originalUiOperations, originalUi);
        await originalUi.ConfigureAwait(false);
    }
    private void ApplySnapshot(BrowseChromeSnapshot snapshot)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_retiring || _disposed) return;
        if (_snapshot.SelectedTabId != snapshot.SelectedTabId || _snapshot.SelectedTab.NativePage != snapshot.SelectedTab.NativePage) _find = snapshot.FindQuery;
        _snapshot = snapshot; IssueCurrentRows(); IssueSavedEntryRows(); IssuePendingDownloadRows();
        if (!_addressDraft) _address = snapshot.SelectedTab.Address.ToString();
        _status = snapshot.Status; Changed();
    }
    /// <summary>Resolves the unfocused, readable location for the selected tab. Web addresses keep
    /// their exact canonical text. First-party app/Space/conversation locations show the genuinely
    /// admitted content-session title when one is published; otherwise the canonical address is
    /// retained rather than an invented friendly name or a GUID route leak. The raw writable
    /// Address port is never replaced by this presentation value.</summary>
    private string ResolveAddressDisplay(BrowseTabSnapshot tab)
    {
        // Web locations keep their exact canonical address text.
        if (tab.Address.IsWeb) return tab.Address.ToString();
        // First-party surface: show the genuinely admitted content-session title when one is
        // published (including a real Spaces session titled "Spaces"). Only when no genuine title
        // exists surface the top-level surface address (scheme://authority) — never the raw route,
        // which can carry opaque GUID segments.
        var title = tab.Title;
        if (!string.IsNullOrWhiteSpace(title)) return title;
        return SurfaceAddress(tab.Address);
    }

    private static string SurfaceAddress(AppContentAddress address) =>
        (address.Namespace switch
        {
            AppContentNamespace.Browse => "browse",
            AppContentNamespace.Cui => "cui",
            AppContentNamespace.NativeApp => "9to1",
            _ => string.Empty
        }) + "://" + address.Authority;

    /// <summary>Retains the real deferred focus operation with the scene's other UI sources.</summary>
    internal Task FocusFindControlAsync(Action focus)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_sourceGate) CheckAdmission(_originalUiOperations);
        var original = Dispatcher.UIThread.InvokeAsync(() => { if (IsReady) focus(); }).GetTask();
        lock (_sourceGate) Retain(_originalUiOperations, original);
        return original;
    }

    private void Changed() => PropertyChanged?.Invoke(this, new(string.Empty));
    private void CheckAdmission(List<Task> sources)
    {
        PruneAdmissionRefusals(sources);
        if (sources.Count >= 128) throw new InvalidOperationException("Browse retains unresolved source failures. Retire this same workspace before continuing.");
    }
    private void Retain(List<Task> sources, Task original)
    {
        // Admission is checked before producing a new source. Always retain a
        // source that already exists, including a source produced reentrantly.
        PruneAdmissionRefusals(sources);
        sources.Add(original);
    }
    /// <summary>A native admission this chrome's own composition owner proved it had not
    /// registered created no session, owner or view here, so it must not permanently
    /// retain a workspace source or consume unresolved-source capacity. The exact refusal
    /// stays failed and inspectable in the chrome's admission ledger; the chrome withdraws
    /// only that retirement obligation, so a mixed or unwitnessed graph is retained. This
    /// only ever applies to a source whose own await lineage reaches the refused
    /// admission, never to a source that merely happened to be pending at the witness.</summary>
    private void PruneAdmissionRefusals(List<Task> sources)
    {
        for (var index = sources.Count - 1; index >= 0; index--)
            if (sources[index].IsCompletedSuccessfully || _chrome.TryRetireAdmissionSource(sources[index]))
            {
                var retired = sources[index];
                sources.RemoveAt(index);
                // This action source is no longer retained, so its declared await
                // lineage is withdrawn with it.
                _chrome.UnlinkAdmissionPropagation(retired);
            }
    }
    public bool CanAdmitClose()
    {
        Dispatcher.UIThread.VerifyAccess(); DemandOriginalFilesExternalJoin();
        if (_busy) { _status = "Wait for the current original browser action before closing."; Changed(); return false; }
        foreach (var tab in _snapshot.Tabs)
            if (_chrome.ObserveOriginalEngine(tab.Id) is IBrowseNativeEngineTab native && !native.CanRetireOriginalView)
            { _status = "Complete or cancel the current page download through Browser approval before closing its engine."; Changed(); return false; }
        return true;
    }
    /// <summary>Checks original callback ownership without changing publication, status or close admission.</summary>
    public void DemandExternalOriginalRetirementJoin()
    {
        Dispatcher.UIThread.VerifyAccess();
        DemandOriginalFilesExternalJoin();
        _chrome.DemandExternalOriginalRetirementJoin();
    }

    public ValueTask DisposeAsync()
    {
        DemandExternalOriginalRetirementJoin();
        if (OriginalClose is { } accepted) return new(accepted);
        if (_pendingCloseAttempt is not null) return new(_pendingCloseAttempt);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCloseAttempt = CloseAsync(start.Task); _closeAttempts.Add(_pendingCloseAttempt);
        start.SetResult(); return new(_pendingCloseAttempt);
    }
    private async Task CloseAsync(Task start)
    {
        await start; var failures = new List<Exception>();
        async Task Join(Task original)
        {
            try { await original; }
            catch (Exception failure)
            {
                // The chrome classifies the source's COMPLETE original graph, never only
                // the first fault this await happened to expose.
                if (_chrome.TryRetireAdmissionSource(original)) return;
                // Retain every sibling cause, not just the exposed one.
                foreach (var cause in original.Exception is { } group
                    ? (IEnumerable<Exception>)group.InnerExceptions : new[] { failure })
                    if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause);
            }
        }
        try
        {
            // Core first resolves unsaved work while this host is still current,
            // then cancels/closes actual owners before joining their initialization.
            var attempt = _chrome.DisposeAsync().AsTask();
            try { await attempt; }
            catch (Exception failure)
            {
                if (_chrome.OriginalClose is null) throw; // Veto: keep commands and publication alive.
                failures.Add(failure);
            }
        }
        finally
        {
            if (_chrome.OriginalClose is null) _pendingCloseAttempt = null;
            else
            {
                _originalClose ??= _pendingCloseAttempt; _retiring = true;
                _originalChromeClose = _chrome.OriginalClose;
            }
        }
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] admitted;
            lock (_sourceGate) admitted = _originalCommands.Concat(_originalOperations).Concat(_originalNotifications)
                .Concat(_originalUiOperations).Where(source => !joined.Contains(source)).Distinct().ToArray();
            if (admitted.Length == 0) break;
            foreach (var source in admitted) { joined.Add(source); await Join(source); }
        }
        if (failures.Count != 0) throw new AggregateException("Browse retained its same source or engine retirement failure.", failures);
        _chrome.StateChanged -= OnChromeStateChanged;
        _chrome.RetirementStarted -= OnChromeRetirementStarted; _disposed = true;
    }
    private sealed record TabSelection(BrowseNativeWorkspace Owner, Guid TabId, long Epoch, bool IsPinned, string PlacementStamp);
    private sealed record TabReorderSelection(BrowseNativeWorkspace Owner, TabSelection Source, TabSelection Target, bool BeforeTarget);
    private void IssueCurrentRows()
    {
        var actualRows = _snapshot.Tabs.Select(tab => (tab.Id, Key: tab.Id.ToString("D"),
            Label: (tab.IsPinned ? "Pinned · " : string.Empty) + tab.Title + (tab.Privacy == BrowserTabPrivacy.Private ? " · Private" : string.Empty),
            Description: tab.Title + " · " + (tab.Address.IsWeb
                ? (tab.Engine == BrowseEngineKind.Chromium ? "Chromium" : "Firefox")
                : tab.IsHome ? "Home" : tab.Address.Authority),
            Selected: tab.Id == _snapshot.SelectedTabId, CanClose: tab.IsCloseable && !tab.IsPinned, Pinned: tab.IsPinned)).ToArray();
        if (_tabRows.Length == actualRows.Length && _tabRows.Zip(actualRows).All(pair =>
            pair.First.Key == pair.Second.Key && pair.First.Label == pair.Second.Label &&
            pair.First.Description == pair.Second.Description && pair.First.Selected == pair.Second.Selected &&
            pair.First.CanClose == pair.Second.CanClose)) return;
        var epoch = checked(++_rowEpoch);
        var placementStamp = TabPlacementStamp(_snapshot);
        _tabRows = actualRows.Select(row => new TabRow(new TabSelection(this, row.Id, epoch, row.Pinned, placementStamp), row.Key, row.Label, row.Selected)
            { Description = row.Description, CanClose = row.CanClose }).ToArray();
    }
    private bool IsCurrentTabTarget(object? parameter) => parameter is TabSelection target &&
        ReferenceEquals(target.Owner, this) && target.Epoch == _rowEpoch &&
        _tabRows.Any(row => ReferenceEquals(row.Target, target)) && _chrome.State.Tabs.Any(tab => tab.Id == target.TabId);
    public bool TryGetCurrentTabRow(int index, out TabRow? row)
    {
        Dispatcher.UIThread.VerifyAccess();
        row = (uint)index < (uint)_tabRows.Length ? _tabRows[index] : null;
        return row is not null && IsCurrentTabTarget(row.Target);
    }
    public bool TryGetCurrentTabRow(string stableKey, out TabRow? row)
    {
        Dispatcher.UIThread.VerifyAccess();
        row = _tabRows.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Key, stableKey));
        return row is not null && IsCurrentTabTarget(row.Target);
    }
    public bool IsCurrentTabRow(TabRow row)
    {
        Dispatcher.UIThread.VerifyAccess();
        return _tabRows.Any(item => ReferenceEquals(item, row)) && IsCurrentTabTarget(row.Target);
    }
    public ValueTask ReorderTabRowsAsync(TabRow source, TabRow target, bool beforeTarget, CancellationToken token = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (ReferenceEquals(source, target) || !_tabRows.Any(row => ReferenceEquals(row, source)) ||
            !_tabRows.Any(row => ReferenceEquals(row, target))) return ValueTask.CompletedTask;
        var sourceId = ((TabSelection)source.Target).TabId;
        var targetId = ((TabSelection)target.Target).TabId;
        if (!_chrome.CanMoveTabRelative(sourceId, targetId))
        { _status = "Tabs can be reordered only within the same privacy, profile, and pinned section."; Changed(); return ValueTask.CompletedTask; }
        return DispatchAsync("9to1.Browse.ReorderTab",
            new TabReorderSelection(this, (TabSelection)source.Target, (TabSelection)target.Target, beforeTarget), token);
    }
    private static bool IsTabPlacementCommand(string command) => command is
        "9to1.Browse.ToggleTabPinned" or "9to1.Browse.MoveTabLeft" or "9to1.Browse.MoveTabRight";
    private static string TabPlacementStamp(BrowseChromeSnapshot snapshot) => string.Join(";", snapshot.Tabs.Select(tab =>
        tab.Id.ToString("N") + (tab.IsPinned ? ":p" : ":u") + (tab.IsHome ? ":h" : string.Empty)));
    private bool IsCurrentSelectedTabTarget(object? parameter)
    {
        if (!IsCurrentTabTarget(parameter) || parameter is not TabSelection target) return false;
        var actual = _chrome.State;
        return actual.SelectedTabId == target.TabId && string.Equals(target.PlacementStamp, TabPlacementStamp(actual), StringComparison.Ordinal);
    }
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        if (TryGetDownloadDestinationItem(item, path, out value)) return true;
        if (TryGetPendingDownloadItem(item, path, out value)) return true;
        value = null;
        if (item is TabRow row && _tabRows.Any(current => ReferenceEquals(current, row)))
            value = path switch { "Key" => row.Key, "Label" => row.Label, "Target" => row.Target, "Selected" => row.Selected,
                "Description" => row.Description, "CanClose" => row.CanClose, "CloseLabel" => "Close " + row.Label, _ => null };
        else if (item is SavedEntryRow saved && _bookmarkRows.Concat(_historyRows).Concat(_downloadRows).Any(current => ReferenceEquals(current, saved)))
            value = path switch { "Key" => saved.Key, "Label" => saved.Label, "Detail" => saved.Detail, "Target" => saved.Target,
                "RemoveLabel" => saved.Target is SavedEntrySelection { Kind: SavedEntryKind.History }
                    ? "Remove history visit " + saved.Label + " · " + saved.Detail
                    : "Remove bookmark " + saved.Label, _ => null };
        return value is not null;
    }
    private enum SavedEntryKind { Bookmark, History, Download }
    private sealed record SavedEntrySelection(BrowseNativeWorkspace Owner, SavedEntryKind Kind, Guid Id, long Epoch);
    private void IssueSavedEntryRows()
    {
        var bookmarks = _snapshot.Bookmarks.Select(entry => (entry.Id, Label: entry.Title, Detail: entry.Address)).ToArray();
        var query = _historyQuery.Trim();
        var history = _snapshot.History.Where(entry => query.Length == 0 ||
                entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Address.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(entry => (entry.Id, Label: entry.Title,
                Detail: entry.VisitedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + " · " + entry.Address)).ToArray();
        var downloads = _snapshot.Downloads.Items.Select(entry => (entry.Id, Label: entry.FileName,
            Detail: entry.SizeBytes.ToString("N0", CultureInfo.CurrentCulture) + " bytes · " + entry.Address)).ToArray();
        static bool Same(SavedEntryRow[] rows, (Guid Id, string Label, string Detail)[] actual) =>
            rows.Length == actual.Length && rows.Zip(actual).All(pair => pair.First.Key == pair.Second.Id.ToString("D") &&
                pair.First.Label == pair.Second.Label && pair.First.Detail == pair.Second.Detail);
        if (Same(_bookmarkRows, bookmarks) && Same(_historyRows, history) && Same(_downloadRows, downloads)) return;
        var epoch = checked(++_savedEpoch);
        SavedEntryRow[] Issue((Guid Id, string Label, string Detail)[] actual, SavedEntryKind kind) => actual
            .Select(entry => new SavedEntryRow(new SavedEntrySelection(this, kind, entry.Id, epoch), entry.Id.ToString("D"), entry.Label, entry.Detail)).ToArray();
        _bookmarkRows = Issue(bookmarks, SavedEntryKind.Bookmark); _historyRows = Issue(history, SavedEntryKind.History);
        _downloadRows = Issue(downloads, SavedEntryKind.Download);
    }
    private bool IsCurrentSavedTarget(object? parameter, SavedEntryKind expectedKind)
    {
        if (parameter is not SavedEntrySelection target || !ReferenceEquals(target.Owner, this) || target.Kind != expectedKind || target.Epoch != _savedEpoch) return false;
        var rows = expectedKind == SavedEntryKind.Bookmark ? _bookmarkRows : expectedKind == SavedEntryKind.History ? _historyRows : _downloadRows;
        if (!rows.Any(row => ReferenceEquals(row.Target, target))) return false;
        var actual = _chrome.State;
        return expectedKind switch
        {
            SavedEntryKind.Bookmark => actual.Bookmarks.Count(entry => entry.Id == target.Id) == 1,
            SavedEntryKind.History => actual.History.Count(entry => entry.Id == target.Id) == 1,
            SavedEntryKind.Download => actual.Downloads.Items.Count(entry => entry.Id == target.Id) == 1,
            _ => false
        };
    }
    public sealed record SavedEntryRow(object Target, string Key, string Label, string Detail);

    public sealed record UsageWindowRow(string Key, string Label, double UsedUnits, double LimitUnits);
    public bool TrySetItemValue(object item, string path, object? value) => false;
    public sealed record TabRow(object Target, string Key, string Label, bool Selected)
    {
        public string Description { get; init; } = Label;
        public bool CanClose { get; init; }
    }

    /// <summary>Formats a genuine byte count for the real storage quota label.</summary>
    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => bytes.ToString(CultureInfo.CurrentCulture) + " B",
        < 1024L * 1024 => (bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.CurrentCulture) + " MB",
        _ => (bytes / (1024.0 * 1024 * 1024)).ToString("0.##", CultureInfo.CurrentCulture) + " GB"
    };

    /// <summary>Builds the genuine usage-window label only from renderable (real) windows;
    /// unknown/absent windows are never invented.</summary>
    private static string ProfileUsageLabel(IHavenProfileDataContext? profileData)
    {
        if (profileData is null) return string.Empty;
        var windows = profileData.UsageWindows.Where(window => window.IsRenderable).ToArray();
        if (windows.Length == 0) return string.Empty;
        return string.Join(" · ", windows.Select(window =>
            $"{window.Name}: {window.UsedUnits.ToString("N0", CultureInfo.CurrentCulture)} of {window.LimitUnits.ToString("N0", CultureInfo.CurrentCulture)} {window.Unit}"));
    }

    /// <summary>Resolves the real effective WindowManagement site-permission decision for the
    /// selected tab's canonical origin. Non-web surfaces have no site permission and report
    /// false; absent permissions mean the default Ask decision (not allowed).</summary>
    private static bool SelectedWebPopupsAllowed(BrowseChromeSnapshot snapshot)
    {
        var address = snapshot.SelectedTab.Address;
        if (!address.IsWeb || address.WebUri is not { } uri) return false;
        var canonical = BrowserSitePermissionStore.CanonicalOrigin(uri);
        return snapshot.Permissions.Any(permission =>
            permission.Kind == BrowserSitePermissionKind.WindowManagement &&
            string.Equals(permission.Origin, canonical, StringComparison.OrdinalIgnoreCase) &&
            permission.Decision == BrowserSitePermissionDecision.Allow);
    }

    /// <summary>Uses the same canonical URL comparison as the owning bookmark store.</summary>
    private static bool SelectedPageIsBookmarked(BrowseChromeSnapshot snapshot) =>
        snapshot.SelectedTab.Address.WebUri is { } address && snapshot.Bookmarks.Any(bookmark =>
            string.Equals(bookmark.Address, address.ToString(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The only percentage shown on the top-bar model control is the real selected
    /// reasoning-effort tier (<see cref="HavenTopBarModelInfo.EffortPercent"/>), never usage,
    /// quota, context fill or hardware load. It is shown only when the model actually advertises
    /// a configurable effort scale AND the canonical tier percentage is non-empty. A stale known
    /// scale with <see cref="HavenTopBarModelInfo.EffortConfigurable"/> false, or a missing tier,
    /// stays empty (truthfully hidden) — never an invented 100%.</summary>
    private static string ModelDisplayPercent(HavenTopBarModelInfo? model) =>
        model is { EffortConfigurable: true } && !string.IsNullOrEmpty(model.EffortPercent)
            ? model.EffortPercent
            : string.Empty;

    /// <summary>Accessible name for the model control exposes the actual canonical model name
    /// (and the genuine reasoning tier when one is known), never a pretend configured model.</summary>
    private static string ModelAccessibleName(HavenTopBarModelInfo? model)
    {
        if (model is null || string.IsNullOrWhiteSpace(model.Name)) return "Choose AI model";
        var percent = ModelDisplayPercent(model);
        return string.IsNullOrEmpty(percent) ? $"Choose {model.Name}" : $"Choose {model.Name}, reasoning effort {percent}";
    }

    /// <summary>Tooltip for the model control mirrors the accessible name so the actual model
    /// identity and any real reasoning tier are exposed on hover/focus without inventing state.</summary>
    private static string ModelTooltip(HavenTopBarModelInfo? model)
    {
        if (model is null || string.IsNullOrWhiteSpace(model.Name)) return "Choose the active AI model";
        var percent = ModelDisplayPercent(model);
        return string.IsNullOrEmpty(percent)
            ? $"Choose the active AI model: {model.Name}"
            : $"Choose the active AI model: {model.Name}, reasoning effort {percent}";
    }
}
