using System.Text.Json;

namespace NineToOne.Web;

internal sealed class BrowserHomeSearchState { internal string Query = ""; }

internal sealed partial class BrowserHomeContext
{
    private IHomeLocalPreferences? _localPreferences;
    private HomeLocalDashboard _localDashboard = HomeLocalDashboard.Default;
    private bool _customising, _serviceDetails, _localSaving, _localStorageHealthy = true;
    private int _localSelection;
    private string _localNotice = "Appearance is saved on this browser.";
    private string _localDiagnostic = "";
    private BrowserHomeSearchState _searchState = new();
    private string _appSearch { get => _searchState.Query; set => _searchState.Query = value; }
    private bool _moreNavigation;
    private bool _compact, _drawerOpen;
    internal bool DrawerOpen => _compact && _drawerOpen;
    internal void SetCompact(bool compact) { _compact = compact; if (!compact) _drawerOpen = false; }
    private Func<CancellationToken, Task<IReadOnlyList<Haven.Core.NotesDocumentSummary>>>? _readLocalDocuments;
    private readonly CancellationTokenSource _briefingLifetime = new();
    private Haven.Core.NotesDocumentSummary[] _recentDocuments = [];
    private int _localDocumentCount;
    private string _localDocumentsStatus = "Open an app to find its local documents.";
    internal event Action? DashboardPresentationChanged;
    internal HomeLocalDashboard LocalDashboard => _localDashboard;
    internal bool IsCustomising => _customising;
    internal string CurrentRoute => _navigation.Current.ToString();

    private static readonly (string Name, string Command, string Description)[] QuickApps =
    [
        ("Write", "OpenWrite", "Documents and notes"), ("Present", "OpenPresent", "Slides and stories"),
        ("Picture", "OpenPicture", "Images and visual ideas"), ("Wave", "OpenWave", "Audio and video"),
        ("Boards", "OpenBoards", "Projects and shared thinking"), ("Browse", "OpenBrowse", "Explore the web"),
        ("Data", "OpenData", "Spreadsheets and analysis")
    ];
    private void InitialiseDashboard(IHomeLocalPreferences? store)
    {
        _localPreferences = store;
        if (store is null) { _localNotice = "Appearance changes are available for this visit; browser storage is not connected."; return; }
        try { _localDashboard = HomeLocalDashboard.Read(store.Read()); }
        catch (Exception cause)
        {
            _localStorageHealthy = false;
            _localNotice = "Saved appearance could not be opened. The saved copy is preserved; changes are available for this visit.";
            _localDiagnostic = cause.Message;
        }
    }
    private bool TryGetDashboardValue(string path, out object? value)
    {
        var available = QuickApps.Count(app => IsActionAvailable(app.Command) == true);
        value = path switch
        {
            "DayLabel" => DateTime.Now.ToString("dddd, d MMMM"),
            "BriefSummary" => _recentDocuments.Length > 0 ? $"{_localDocumentCount} local documents · {available} apps ready. " +
                (_recentDocuments.Any(document => document.HasRecovery) ? "A document has a recovery copy to review." : "Your latest work is ready to continue.") :
                available > 0 ? $"{available} apps are ready here. Start with a document or project; connected activity will join your briefing when available." :
                "Your workspace is open. Check the app and service cards below to see what is available.",
            "BriefSource" => _recentDocuments.Length > 0 ? "Based on local Write metadata · recovery copies shown first" : "Based on available apps on this device",
            "PriorityTitle" => _recentDocuments.FirstOrDefault()?.HasRecovery is true ? "A recovery copy needs your attention" : _recentDocuments.Length > 0 ? "Pick up your latest work" : IsActionAvailable("OpenWrite") == true ? "Make room for your next idea" : "Choose your next step",
            "PrioritySummary" => _recentDocuments.FirstOrDefault() is { } recent ? recent.Title + " · " + recent.WordCount + " words · updated " + recent.UpdatedAt.ToLocalTime().ToString("d MMM, HH:mm") : "Open Write for a document or note, or use Boards to organise your next steps. Your local apps do not need Home's cloud services.",
            "HasRecentDocuments" => _recentDocuments.Length > 0,
            "HasRecent1" => _recentDocuments.Length > 1,
            "HasRecent2" => _recentDocuments.Length > 2,
            "Recent0" => _recentDocuments.ElementAtOrDefault(0)?.Title ?? "",
            "Recent1" => _recentDocuments.ElementAtOrDefault(1)?.Title ?? "",
            "Recent2" => _recentDocuments.ElementAtOrDefault(2)?.Title ?? "",
            "RecentSummary" => _localDocumentsStatus,
            "ShowMoreNavigation" => _moreNavigation,
            "MoreNavigationLabel" => _moreNavigation ? "Fewer" : "More destinations…",
            "StudioAvailable" => IsActionAvailable("OpenStudio") == true,
            "AccountLabel" => AccountValue("HasAccount") is true ? AccountValue("DisplayName") ?? "Your account" : "Local workspace",
            "AccountMessage" => AccountValue("HasAccount") is true ? "Account connected" : AccountValue("Busy") is true ? "Checking your account…" :
                AccountValue("CanSignIn") is true ? "Sign in to connect your account and saved dashboard." : "Sign-in is not configured for this browser. Local apps remain available.",
            "CanSignInHere" => AccountValue("HasAccount") is not true && AccountValue("CanSignIn") is true,
            "CanSignOutHere" => AccountValue("HasAccount") is true,
            "AccountRecovery" => AccountValue("CanSignIn") is true ? "Use Sign in or retry the account connection." : "Open Account settings for connection details, or retry after the host is configured.",
            "ShowServiceDetails" => _serviceDetails,
            "ServiceDetailsLabel" => _serviceDetails ? "Hide technical details" : "Technical details",
            "ServiceDiagnostic" => (AccountValue("Status") ?? "No account provider is connected.") + "\n" + _localDiagnostic,
            "LocalAppsSummary" => $"{available} apps ready in this browser",
            "LibrarySummary" => "Connected files are not available here yet. Open an app to find its local documents.",
            "EventsSummary" => "Connected activity is not available here yet. This is your activity timeline, not a calendar.",
            "ActivitySummary" => _recentDocuments.Length > 0 ? string.Join("\n", _recentDocuments.OrderByDescending(row => row.UpdatedAt).Select(row => row.UpdatedAt.ToLocalTime().ToString("d MMM, HH:mm") + " · Updated “" + row.Title + "” in Write")) : "No connected activity to show. Local documents remain available in their owning apps.",
            "PlannerSummary" => "For upcoming appointments and schedules, use Planner. Planner is not connected in this browser.",
            "AiSummary" => (_layout?.AllowAiGeneratedTiles ?? _localDashboard.AiTiles) ?
                "AI tiles are allowed. Connect a model service to generate a briefing; the current briefing uses available app information." :
                "AI tiles are off. Your briefing uses available app information.",
            "IsCustomising" => _customising,
            "CustomiseLabel" => _customising ? "Done customising" : "Customise dashboard",
            "LocalPreferenceSummary" => _localNotice,
            "LocalTileItems" => _localDashboard.Tiles.Select(tile => TileLabel(tile.Id) + (tile.Pinned ? " · pinned" : "") +
                (tile.Locked ? " · locked" : "") + (tile.Hidden ? " · hidden" : "")).ToArray(),
            "LocalTileIndex" => _localSelection,
            "SelectedLocalTileName" => "Selected tile: " + TileLabel(SelectedLocalTile.Id),
            "LocalTilePinned" => SelectedLocalTile.Pinned,
            "LocalTileLocked" => SelectedLocalTile.Locked,
            "LocalTileHidden" => SelectedLocalTile.Hidden,
            "PinTileLabel" => SelectedLocalTile.Pinned ? "Unpin tile" : "Pin tile",
            "LockTileLabel" => SelectedLocalTile.Locked ? "Unlock tile" : "Lock tile",
            "HideTileLabel" => SelectedLocalTile.Hidden ? "Show tile" : "Hide tile",
            "TileEditReason" => SelectedLocalTile.Locked ? "This tile is locked. Unlock it to move, pin or hide it." :
                SelectedLocalTile.Pinned ? "Pinned tiles stay at the top. Unpin to move this tile." : "Move, pin or hide the selected tile. Changes save as you make them.",
            "AiTilesSwitch" => _layout?.AllowAiGeneratedTiles ?? _localDashboard.AiTiles,
            "AiReorderSwitch" => _layout?.AllowAiReorder ?? _localDashboard.AiReorder,
            "AppSearch" => _appSearch,
            "AppSearchSummary" => string.IsNullOrWhiteSpace(_appSearch) ? "" :
                $"{QuickApps.Count(app => MatchesSearch(app.Name))} matching apps in Quick access",
            _ => null
        };
        if (value is not null) return true;
        foreach (var app in QuickApps)
        {
            if (path == "Show" + app.Name) { value = MatchesSearch(app.Name); return true; }
            if (path == app.Name + "Availability") { value = IsActionAvailable(app.Command) == true ? app.Name == "Write" && _localDocumentCount > 0 ? $"{_localDocumentCount} local documents · Ready here" : "Ready here · " + app.Description : "Not connected in this browser"; return true; }
        }
        return false;
    }
    private bool MatchesSearch(string name) => name.Contains(_appSearch.Trim(), StringComparison.OrdinalIgnoreCase);
    private HomeLocalTile SelectedLocalTile => _localDashboard.Tiles[_localSelection];
    private static string TileLabel(string id) => id switch { "continue" => "Start here", "apps" => "Quick access", "activity" => "Events activity", "services" => "Connections", _ => id };
    private bool? DashboardActionAvailable(string command) => command.StartsWith("ChooseTile_", StringComparison.Ordinal) && HomeLocalDashboard.TileIds.Contains(command[11..]) ? _customising : command switch
    {
        "ToggleCustomise" or "ToggleServiceDetails" or "SearchApps" or "ToggleMoreNavigation" => true,
        "ToggleDrawer" => _compact,
        "OpenRecent0" => _recentDocuments.Length > 0 && IsActionAvailable("OpenWrite") == true,
        "OpenRecent1" => _recentDocuments.Length > 1 && IsActionAvailable("OpenWrite") == true,
        "OpenRecent2" => _recentDocuments.Length > 2 && IsActionAvailable("OpenWrite") == true,
        "RefreshLocalBriefing" => _readLocalDocuments is not null,
        "ToggleAiTiles" or "ToggleAiReorder" => !_layoutBusy && !_localSaving,
        "ToggleLocalLock" or "ResetLocalLayout" => _customising && !_localSaving,
        "ToggleLocalPin" or "ToggleLocalHidden" => _customising && !_localSaving && !SelectedLocalTile.Locked,
        "MoveLocalUp" => _customising && !_localSaving && !SelectedLocalTile.Locked && !SelectedLocalTile.Pinned && _localSelection > 0 && !_localDashboard.Tiles[_localSelection - 1].Locked && !_localDashboard.Tiles[_localSelection - 1].Pinned,
        "MoveLocalDown" => _customising && !_localSaving && !SelectedLocalTile.Locked && !SelectedLocalTile.Pinned && _localSelection < _localDashboard.Tiles.Length - 1 && !_localDashboard.Tiles[_localSelection + 1].Locked && !_localDashboard.Tiles[_localSelection + 1].Pinned,
        _ => null
    };
    private ValueTask? DispatchDashboard(string command, CancellationToken caller)
    {
        if (DashboardActionAvailable(command) is not { } available) return null;
        if (!available) return ValueTask.CompletedTask;
        return new ValueTask(DispatchDashboardAsync(command, caller));
    }
    private async Task DispatchDashboardAsync(string command, CancellationToken caller)
    {
        if (command == "ToggleCustomise") _customising = !_customising;
        else if (command == "ToggleDrawer") _drawerOpen = !_drawerOpen;
        else if (command.StartsWith("ChooseTile_", StringComparison.Ordinal)) _localSelection = Array.FindIndex(_localDashboard.Tiles, tile => tile.Id == command[11..]);
        else if (command == "ToggleServiceDetails") _serviceDetails = !_serviceDetails;
        else if (command == "ToggleMoreNavigation") _moreNavigation = !_moreNavigation;
        else if (command == "RefreshLocalBriefing") { await RefreshLocalBriefingAsync(); return; }
        else if (command.StartsWith("OpenRecent", StringComparison.Ordinal))
        {
            var recent = _recentDocuments[command[^1] - '0'];
            _navigate(new("app.write", "NotesDocument", recent.Id.ToString("D"), "open"));
        }
        else if (command == "SearchApps")
        {
            if (_navigation.Current != HavenOS.Home.HomeRoute.Dashboard)
                _navigate(new(HavenOS.Home.HomeRouteIds.For(HavenOS.Home.HomeRoute.Dashboard)));
        }
        else if (command is "ToggleAiTiles" or "ToggleAiReorder")
        {
            var tiles = command == "ToggleAiTiles";
            if (LayoutAvailable && _layoutOwner is not null)
            {
                var enabled = tiles ? _layout!.AllowAiGeneratedTiles : _layout!.AllowAiReorder;
                await _layoutOwner.RunViewAsync(() => ChangeLayoutAsync(
                    tiles ? (enabled ? "DisableAIGeneratedTiles" : "EnableAIGeneratedTiles") : (enabled ? "DisableAIReorder" : "EnableAIReorder"), caller), caller);
                return;
            }
            await SaveLocalAsync(tiles ? _localDashboard with { AiTiles = !_localDashboard.AiTiles } : _localDashboard with { AiReorder = !_localDashboard.AiReorder }, caller);
        }
        else
        {
            var rows = _localDashboard.Tiles.ToArray();
            if (command == "ResetLocalLayout") { _localSelection = 0; await SaveLocalAsync(_localDashboard with { Tiles = HomeLocalDashboard.Default.Tiles }, caller); }
            else if (command is "MoveLocalUp" or "MoveLocalDown")
            {
                var target = _localSelection + (command == "MoveLocalUp" ? -1 : 1);
                (rows[target], rows[_localSelection]) = (rows[_localSelection], rows[target]); _localSelection = target;
                await SaveLocalAsync(_localDashboard with { Tiles = rows }, caller);
            }
            else
            {
                rows[_localSelection] = command switch
                {
                    "ToggleLocalLock" => SelectedLocalTile with { Locked = !SelectedLocalTile.Locked },
                    "ToggleLocalPin" => SelectedLocalTile with { Pinned = !SelectedLocalTile.Pinned },
                    "ToggleLocalHidden" => SelectedLocalTile with { Hidden = !SelectedLocalTile.Hidden },
                    _ => SelectedLocalTile
                };
                await SaveLocalAsync(_localDashboard with { Tiles = rows }, caller);
            }
        }
        if (!_disposed) { Notify(); DashboardPresentationChanged?.Invoke(); }
    }
    private async Task SaveLocalAsync(HomeLocalDashboard state, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        _localSaving = true;
        Notify();
        try
        {
            if (_localPreferences is not null && _localStorageHealthy)
            {
                try { await _localPreferences.WriteAsync(state.Serialize(), caller); _localNotice = "Saved on this browser."; }
                catch (OperationCanceledException) { throw; }
                catch (Exception cause) { _localNotice = "Changes apply for this visit but could not be saved. Check browser storage permissions."; _localDiagnostic = cause.Message; }
            }
            if (!_disposed) _localDashboard = state;
        }
        finally { _localSaving = false; Notify(); }
    }
    private bool SetDashboardValue(string path, object? value)
    {
        if (_disposed) return false;
        if (path == "LocalTileIndex" && value is int index && index >= 0 && index < _localDashboard.Tiles.Length) _localSelection = index;
        else if (path == "AppSearch" && value is string search) _appSearch = search.Length > 80 ? search[..80] : search;
        else return false;
        Notify(); DashboardPresentationChanged?.Invoke(); return true;
    }
    private async Task RefreshLocalBriefingAsync()
    {
        if (_readLocalDocuments is null || IsActionAvailable("OpenWrite") != true || _disposed) return;
        try
        {
            var rows = await _readLocalDocuments(_briefingLifetime.Token);
            if (_disposed) return;
            _localDocumentCount = rows.Count;
            _recentDocuments = rows.OrderByDescending(row => row.HasRecovery).ThenByDescending(row => row.UpdatedAt).Take(3).ToArray();
            _localDocumentsStatus = rows.Count == 0 ? "No saved Write documents yet. Start your first one." : $"{rows.Count} saved Write documents on this browser.";
        }
        catch (OperationCanceledException) when (_disposed) { return; }
        catch (Exception cause)
        {
            if (_disposed) return;
            _recentDocuments = []; _localDocumentCount = 0;
            _localDocumentsStatus = "Recent documents could not be read. Open Write to check its local storage.";
            _localDiagnostic = cause.Message;
        }
        Notify(); DashboardPresentationChanged?.Invoke();
    }
}
