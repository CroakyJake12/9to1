using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Haven.Application;
using Haven.Browser;

namespace HavenOS.Apps.Browse;

/// <summary>Canonical CUI bindings consume the same Browse-owned tab/session
/// services. The supplied availability observer does not grant permissions.</summary>
public sealed partial class BrowseNativeWorkspace : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged, IAsyncDisposable
{
    private static readonly AsyncLocal<BrowseNativeWorkspace?> LogicalDriver = new();
    private readonly BrowseChrome _chrome;
    private readonly Func<string, bool> _originalActionAvailability;
    private readonly object _sourceGate = new();
    private readonly List<Task> _originalCommands = [], _originalOperations = [], _originalNotifications = [], _originalUiOperations = [];
    private BrowseChromeSnapshot _snapshot;
    private TabRow[] _tabRows = [];
    private SavedEntryRow[] _bookmarkRows = [], _historyRows = [], _downloadRows = [];
    private long _savedEpoch;
    private long _rowEpoch;
    private string _address, _find = string.Empty, _status;
    private bool _addressDraft, _busy, _ready, _disposed;
    private volatile bool _retiring;
    private Task? _originalCommand, _originalClose, _originalChromeClose;
    public event PropertyChangedEventHandler? PropertyChanged;
    public BrowseChromeSnapshot State => _snapshot;
    public bool IsOriginalPublicationCurrent => !_retiring && !_disposed;
    public bool IsReady => _ready && !_retiring && !_disposed;
    public BrowseChrome OriginalChrome => _chrome;
    public Task? OriginalCommand => _originalCommand;
    public Task? OriginalClose => _originalClose;
    public Task? OriginalChromeClose => _originalChromeClose;
    public IReadOnlyList<Task> OriginalCommands { get { lock (_sourceGate) return _originalCommands.ToArray(); } }
    public IReadOnlyList<Task> OriginalOperations { get { lock (_sourceGate) return _originalOperations.ToArray(); } }
    public IReadOnlyList<Task> OriginalNotifications { get { lock (_sourceGate) return _originalNotifications.Concat(_originalUiOperations).ToArray(); } }

    public BrowseNativeWorkspace(BrowseChrome originalChrome, Func<string, bool> originalActionAvailability, IBrowserOriginalDownloadFilesService? originalDownloadFilesService = null)
    {
        _originalDownloadFilesService = originalDownloadFilesService;
        _chrome = originalChrome ?? throw new ArgumentNullException(nameof(originalChrome));
        _originalActionAvailability = originalActionAvailability ?? throw new ArgumentNullException(nameof(originalActionAvailability));
        _snapshot = _chrome.State; _address = _snapshot.SelectedTab.Address.ToString(); _status = _snapshot.Status;
        IssueCurrentRows(); IssueSavedEntryRows(); _chrome.StateChanged += OnChromeStateChanged;
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
        if (TryGetDownloadFilesBinding(path, out value)) return true;
        var selected = _snapshot.SelectedTab;
        value = path switch
        {
            "Tabs" => _tabRows,
            "Address" => _address, "FindQuery" => _find, "Status" => _status,
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
            "Bookmarks" => _bookmarkRows,
            "History" => _historyRows,
            "Downloads" => _downloadRows,
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
        if (_busy || _retiring || _disposed || !_ready || value is not string text || text.Length > 8192) return false;
        if (path == "Address") { _address = text; _addressDraft = !string.Equals(text, _snapshot.SelectedTab.Address.ToString(), StringComparison.Ordinal); }
        else if (path == "FindQuery") _find = text;
        else return false;
        Changed(); return true;
    }
    public bool? IsActionAvailable(string command)
    {
        if (!_ready || _busy || _retiring || _disposed || !_originalActionAvailability(command) || _retiring || _disposed) return false;
        var tab = _snapshot.SelectedTab; var interactive = tab.EngineState == BrowseEngineState.Ready && _chrome.ObserveOriginalEngine(tab.Id) is not null;
        return command switch
        {
            "9to1.Browse.Back" => interactive && tab.CanGoBack,
            "9to1.Browse.Forward" => interactive && tab.CanGoForward,
            "9to1.Browse.Navigate" or "9to1.Browse.Reload" or "9to1.Browse.FindNext" or "9to1.Browse.FindPrevious" or
                "9to1.Browse.ZoomIn" or "9to1.Browse.ZoomOut" or "9to1.Browse.ZoomReset" or "9to1.Browse.DevTools" or
                "9to1.Browse.OpenBookmark" or "9to1.Browse.OpenHistory" => interactive,
            "9to1.Browse.Stop" => interactive && tab.IsLoading,
            "9to1.Browse.Bookmark" or "9to1.Browse.AllowPopups" or "9to1.Browse.DenyPopups" or
                "9to1.Browse.FirefoxSite" or "9to1.Browse.ChromiumSite" or "9to1.Browse.ResetSiteEngine" => tab.Address.Scheme is "http" or "https",
            "9to1.Browse.Recover" => tab.EngineState == BrowseEngineState.Crashed,
            "9to1.Browse.ShowDownloadInFiles" => _originalDownloadFilesService is not null,
            "9to1.Browse.RegisterDownloadDestination" => _originalDownloadFilesService is not null && _downloadDestinations.Length > 0,
            "9to1.Browse.RetryDownloadRegistration" => CanRetryOriginalDownloadRegistration,
            "9to1.Browse.Downloads" => _snapshot.Downloads.IsSupported,
            "9to1.Browse.NewTab" or "9to1.Browse.NewPrivateTab" or "9to1.Browse.CloseTab" or "9to1.Browse.SelectTab" or
                "9to1.Browse.ClearHistory" or "9to1.Browse.FirefoxTab" or "9to1.Browse.ChromiumTab" or
                "9to1.Browse.DefaultFirefox" or "9to1.Browse.DefaultChromium" => true,
            "9to1.Browse.DiscardAddress" => _addressDraft,
            _ => false
        };
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
    {
        Dispatcher.UIThread.VerifyAccess(); token.ThrowIfCancellationRequested();
        if (IsActionAvailable(command) != true) return ValueTask.CompletedTask;
        if (command == "9to1.Browse.SelectTab" && !IsCurrentTabTarget(parameter))
        { _status = "The tab row changed. Select its current row again."; Changed(); return ValueTask.CompletedTask; }
        if ((command == "9to1.Browse.OpenBookmark" && !IsCurrentSavedTarget(parameter, SavedEntryKind.Bookmark)) ||
            (command == "9to1.Browse.OpenHistory" && !IsCurrentSavedTarget(parameter, SavedEntryKind.History)))
        { _status = "The saved entry changed. Open its current row again."; Changed(); return ValueTask.CompletedTask; }
        if (!ValidateOriginalDownloadAdmission(command, parameter))
        { _status = "The download or Files folder changed. Select its current row again."; Changed(); return ValueTask.CompletedTask; }
        lock (_sourceGate) CheckAdmission(_originalCommands);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _busy = true; var original = RunAsync(start.Task, command, parameter, token);
        _originalCommand = original;
        lock (_sourceGate) Retain(_originalCommands, original);
        Exception? notificationFailure = null;
        try { Changed(); } catch (Exception failure) { notificationFailure = failure; }
        finally { if (notificationFailure is null) start.SetResult(); else start.SetException(notificationFailure); }
        return new(original);
    }
    private async Task RunAsync(Task start, string command, object? parameter, CancellationToken token)
    {
        var prior = LogicalDriver.Value; LogicalDriver.Value = this;
        var failures = new List<Exception>();
        try
        {
            await start; token.ThrowIfCancellationRequested();
            var tabId = _snapshot.SelectedTabId;
            switch (command)
            {
                case "9to1.Browse.Navigate": await Operation(() => _chrome.NavigateAsync(_address, token)); _addressDraft = false; break;
                case "9to1.Browse.Back": await Operation(() => _chrome.BackAsync(token)); break;
                case "9to1.Browse.Forward": await Operation(() => _chrome.ForwardAsync(token)); break;
                case "9to1.Browse.Reload": await Operation(() => _chrome.ReloadAsync(token)); break;
                case "9to1.Browse.Stop": await Operation(() => _chrome.StopAsync(token)); break;
                case "9to1.Browse.NewTab": await Operation(() => _chrome.NewTabAsync(false, token)); _addressDraft = false; break;
                case "9to1.Browse.NewPrivateTab": await Operation(() => _chrome.NewTabAsync(true, token)); _addressDraft = false; break;
                case "9to1.Browse.CloseTab": await Operation(() => _chrome.CloseTabAsync(tabId, token)); _addressDraft = false; break;
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
                case "9to1.Browse.ResetSiteEngine": await Operation(() => _chrome.ResetSiteEngineAsync(token)); break;
                case "9to1.Browse.Bookmark": await Operation(() => _chrome.ToggleBookmarkAsync(token)); break;
                case "9to1.Browse.ClearHistory": await Operation(() => _chrome.ClearHistoryAsync(token)); break;
                case "9to1.Browse.ShowDownloadInFiles": await ShowOriginalDownloadInFilesAsync(parameter!, token); break;
                case "9to1.Browse.RegisterDownloadDestination": await RegisterOriginalDownloadDestinationAsync(parameter!, token); break;
                case "9to1.Browse.RetryDownloadRegistration": await RetryOriginalDownloadRegistrationAsync(token); break;
                case "9to1.Browse.Downloads": await Operation(() => _chrome.RefreshDownloadsAsync(token)); break;
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
                case "9to1.Browse.DefaultFirefox": await Operation(() => _chrome.SetDefaultEngineAsync(BrowseEngineKind.Gecko, token)); break;
                case "9to1.Browse.DefaultChromium": await Operation(() => _chrome.SetDefaultEngineAsync(BrowseEngineKind.Chromium, token)); break;
                case "9to1.Browse.DevTools":
                    var engine = _chrome.ObserveOriginalEngine(tabId) ?? throw new PlatformNotSupportedException("The original tab engine is unavailable.");
                    var originalTools = engine.OpenDeveloperToolsAsync(token) ?? throw new InvalidOperationException("The tab engine did not produce its original developer-tools task.");
                    lock (_sourceGate) Retain(_originalOperations, originalTools);
                    await originalTools; break;
                case "9to1.Browse.DiscardAddress": _addressDraft = false; break;
                default: throw new ArgumentOutOfRangeException(nameof(command));
            }
            ApplySnapshot(_chrome.State); _status = _snapshot.Status;
        }
        catch (Exception failure) { failures.Add(failure); _status = "Browse retained the original failed operation: " + failure.Message; }
        finally
        {
            _busy = false;
            try { Changed(); } catch (Exception failure) { failures.Add(failure); }
            LogicalDriver.Value = prior;
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Browse retained operation and observer failures.", failures);
    }
    private async Task Operation(Func<Task<BrowseChromeSnapshot>> start)
    {
        lock (_sourceGate) CheckAdmission(_originalOperations);
        var original = start() ?? throw new InvalidOperationException("Browse did not produce its original operation task.");
        lock (_sourceGate) Retain(_originalOperations, original);
        await original;
    }
    private void OnChromeStateChanged(object? sender, BrowseChromeSnapshot snapshot)
    {
        if (_retiring || _disposed) return;
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
            if (!_retiring && !_disposed) ApplySnapshot(_chrome.State);
        }).GetTask();
        lock (_sourceGate) Retain(_originalUiOperations, originalUi);
        await originalUi.ConfigureAwait(false);
    }
    private void ApplySnapshot(BrowseChromeSnapshot snapshot)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_retiring || _disposed) return;
        _snapshot = snapshot; IssueCurrentRows(); IssueSavedEntryRows();
        if (!_addressDraft) _address = snapshot.SelectedTab.Address.ToString();
        _status = snapshot.Status; Changed();
    }
    private void Changed() => PropertyChanged?.Invoke(this, new(string.Empty));
    private static void CheckAdmission(List<Task> sources)
    {
        sources.RemoveAll(task => task.IsCompletedSuccessfully);
        if (sources.Count >= 128) throw new InvalidOperationException("Browse retains unresolved source failures. Retire this same workspace before continuing.");
    }
    private static void Retain(List<Task> sources, Task original)
    {
        // Admission is checked before producing a new source. Always retain a
        // source that already exists, including a source produced reentrantly.
        sources.RemoveAll(task => task.IsCompletedSuccessfully);
        sources.Add(original);
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
    public void WithdrawForClose()
    {
        Dispatcher.UIThread.VerifyAccess();
        _retiring = true;
    }
    public ValueTask DisposeAsync()
    {
        DemandOriginalFilesExternalJoin();
        Dispatcher.UIThread.VerifyAccess();
        if (ReferenceEquals(LogicalDriver.Value, this)) throw new InvalidOperationException("A Browse source callback cannot join its own retirement.");
        if (_originalClose is not null) return new(_originalClose);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _retiring = true; _originalClose = CloseAsync(start.Task); start.SetResult();
        return new(_originalClose);
    }
    private async Task CloseAsync(Task start)
    {
        await start; var failures = new List<Exception>();
        async Task Join(Task original) { try { await original; } catch (Exception failure) { failures.Add(failure); } }
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] admitted;
            lock (_sourceGate) admitted = _originalCommands.Concat(_originalOperations).Concat(_originalNotifications)
                .Concat(_originalUiOperations).Where(source => !joined.Contains(source)).Distinct().ToArray();
            if (admitted.Length == 0) break;
            foreach (var source in admitted) { joined.Add(source); await Join(source); }
        }
        try { _originalChromeClose = _chrome.DisposeAsync().AsTask(); } catch (Exception failure) { failures.Add(failure); }
        if (_originalChromeClose is not null) await Join(_originalChromeClose);
        if (failures.Count != 0) throw new AggregateException("Browse retained its same source or engine retirement failure.", failures);
        _chrome.StateChanged -= OnChromeStateChanged; _disposed = true;
    }
    private sealed record TabSelection(BrowseNativeWorkspace Owner, Guid TabId, long Epoch);
    private void IssueCurrentRows()
    {
        var actualRows = _snapshot.Tabs.Select(tab => (tab.Id, Key: tab.Id.ToString("D"),
            Label: (tab.Id == _snapshot.SelectedTabId ? "● " : string.Empty) + tab.Title +
            (tab.Privacy == BrowserTabPrivacy.Private ? " · Private" : string.Empty) +
            (tab.Engine == BrowseEngineKind.Chromium ? " · Chromium" : " · Firefox"))).ToArray();
        if (_tabRows.Length == actualRows.Length && _tabRows.Zip(actualRows).All(pair =>
            pair.First.Key == pair.Second.Key && pair.First.Label == pair.Second.Label)) return;
        var epoch = checked(++_rowEpoch);
        _tabRows = actualRows.Select(row => new TabRow(new TabSelection(this, row.Id, epoch), row.Key, row.Label)).ToArray();
    }
    private bool IsCurrentTabTarget(object? parameter) => parameter is TabSelection target &&
        ReferenceEquals(target.Owner, this) && target.Epoch == _rowEpoch &&
        _tabRows.Any(row => ReferenceEquals(row.Target, target)) && _chrome.State.Tabs.Any(tab => tab.Id == target.TabId);
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        if (TryGetDownloadDestinationItem(item, path, out value)) return true;
        value = null;
        if (item is TabRow row && _tabRows.Any(current => ReferenceEquals(current, row)))
            value = path switch { "Key" => row.Key, "Label" => row.Label, "Target" => row.Target, _ => null };
        else if (item is SavedEntryRow saved && _bookmarkRows.Concat(_historyRows).Concat(_downloadRows).Any(current => ReferenceEquals(current, saved)))
            value = path switch { "Key" => saved.Key, "Label" => saved.Label, "Detail" => saved.Detail, "Target" => saved.Target, _ => null };
        return value is not null;
    }
    private enum SavedEntryKind { Bookmark, History, Download }
    private sealed record SavedEntrySelection(BrowseNativeWorkspace Owner, SavedEntryKind Kind, Guid Id, long Epoch);
    private void IssueSavedEntryRows()
    {
        var bookmarks = _snapshot.Bookmarks.Select(entry => (entry.Id, Label: entry.Title, Detail: entry.Address)).ToArray();
        var history = _snapshot.History.Select(entry => (entry.Id, Label: entry.Title, Detail: entry.Address)).ToArray();
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
    public bool TrySetItemValue(object item, string path, object? value) => false;
    public sealed record TabRow(object Target, string Key, string Label);
}
