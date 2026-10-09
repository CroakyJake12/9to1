using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Home;
using HavenOS.Home.Core;
using NineToOne.Web.Accounts;

namespace NineToOne.Web;

/// <summary>Projects the existing verified account owner; other Home providers stay unavailable.</summary>
internal sealed class BrowserHomeContext : ICuiWritableBindingContext, ICuiLifetimeAwareActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private static readonly IReadOnlyDictionary<string, HomeRoute> ShellActions = new Dictionary<string, HomeRoute>(StringComparer.Ordinal)
    {
        ["NavigateDashboard"] = HomeRoute.Dashboard,
        ["NavigateLibrary"] = HomeRoute.Library,
        ["NavigateEvents"] = HomeRoute.Events,
    };
    private static readonly IReadOnlyDictionary<string, string> AccountActions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AccountSignIn"] = "RequestSignIn",
        ["AccountRequestSignOut"] = "RequestSignOut",
        ["AccountConfirmSignOut"] = "ConfirmSessionMutation",
        ["AccountCancelSignOut"] = "CancelSessionMutation",
    };
    private static readonly IReadOnlyDictionary<string, string> FeatureActions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["NavigateApps"] = HomeFeatureRouteIds.Apps,
        ["NavigateDiscover"] = HomeFeatureRouteIds.Discover,
        ["NavigateMesh"] = HomeFeatureRouteIds.Mesh,
        ["NavigatePermissions"] = HomeFeatureRouteIds.Permissions,
        ["NavigateNotifications"] = HomeFeatureRouteIds.Notifications,
        ["NavigateSpaces"] = HomeFeatureRouteIds.Spaces,
        ["NavigateAutomations"] = HomeFeatureRouteIds.Automations,
        ["OpenStudio"] = "app.studio",
        ["OpenWrite"] = "app.write",
        ["OpenPresent"] = "app.present",
        ["OpenPicture"] = "app.picture",
        ["OpenWave"] = "app.wave",
        ["OpenBrowse"] = "app.browse",
        ["OpenData"] = "app.data",
        ["OpenBoards"] = "app.boards",
    };
    private readonly Func<string, bool>? _isRegisteredRoute;
    private readonly HomeCuiSurface _surface;
    private readonly HomeNavigationState _navigation = new();
    private readonly Action<HomeFeatureNavigationRequest> _navigate;
    private readonly AccountSettingsFeature? _accountOwner;
    private readonly AccountBrowserBindings? _account;
    private readonly BrowserHomeDashboardLayoutStore? _layoutOwner;
    private BrowserHomeDashboardLayoutStore.Binding? _layoutBinding;
    private HomeDashboardLayout? _layout;
    private int _selectedTile = -1;
    private bool _layoutBusy;
    private string _layoutStatus = "Sign in to save and reopen your dashboard on this browser.";
    private Task? _activation;
    private bool _disposed;

    public BrowserHomeContext(CuiDocument document, Action<HomeFeatureNavigationRequest> navigate,
        AccountSettingsFeature? accountOwner = null, Func<string, bool>? isRegisteredRoute = null,
        BrowserHomeDashboardLayoutStore? layoutOwner = null)
    {
        _surface = new(document);
        _navigate = navigate;
        _accountOwner = accountOwner;
        _isRegisteredRoute = isRegisteredRoute;
        _layoutOwner = layoutOwner;
        // The SAME registered feature enrols this binding before any original service callback.
        _account = accountOwner?.CreateHomeBinding();
        _surface.PropertyChanged += OnChanged;
        if (_account is not null) _account.PropertyChanged += OnChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task ActivateAsync()
    {
        if (_disposed || _account is null || _accountOwner is null) return Task.CompletedTask;
        return _activation ??= _layoutOwner is null ? _accountOwner.RefreshHomeAsync(_account) :
            _layoutOwner.RunViewAsync(() => RefreshAccountAndLayoutAsync(CancellationToken.None));
    }

    public bool Open(HomeFeatureNavigationRequest request)
    {
        if (_disposed || !CanOpen(request)) return false;
        var route = ShellActions.Values.FirstOrDefault(value => HomeRouteIds.For(value) == request.RouteId);
        _navigation.Navigate(route);
        _navigation.State(route).SelectedObjectId = request.EntityId;
        _surface.ApplyNavigation(_navigation);
        return true;
    }

    internal static bool CanOpen(HomeFeatureNavigationRequest request) =>
        ShellActions.Values.Any(route => HomeRouteIds.For(route) == request.RouteId);

    private object? AccountValue(string path) =>
        !_disposed && _account is not null && _account.TryGetValue(path, out var value) ? value : null;

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "AccountHeading" => AccountValue("HasAccount") is true && AccountValue("DisplayName") is string name
                ? "Welcome, " + name : "Welcome to 9-1",
            "AccountStatus" => AccountValue("Status") ?? "Account services are unavailable.",
            "AccountAvailable" => AccountValue("HasAccount") is true,
            "AccountConfirmation" => AccountValue("Confirmation") ?? "",
            "HasAccountConfirmation" => AccountValue("HasConfirmation") is true,
            "BriefSummary" => _layout is null ? "Sign in to reopen your saved dashboard." :
                $"{_layout.Tiles.Count(tile => tile.Visibility == HomeTileVisibility.Visible)} visible dashboard tiles. " + _layoutStatus,
            "CatalogSummary" => "Local app installation is unavailable in this browser.",
            "RuntimeSummary" => "Cloud model services are unavailable.",
            "OperationSummary" => (AccountValue("Status") ?? "Account services are unavailable.") +
                " Home files and activity have not been loaded.",
            "LibrarySummary" => "Library is unavailable. Your files have not been loaded.",
            "LibraryItemsSummary" => "",
            "EventsSummary" => "Activity is unavailable. Your events have not been loaded.",
            "EventItemsSummary" => "",
            "DashboardTilesSummary" => _layoutOwner is null ? "Dashboard customisation requires a Home layout provider." : _layoutStatus,
            "DashboardTilesItems" => _disposed || _layout is null ? Array.Empty<string>() : _layout.Tiles.OrderBy(tile => tile.Order)
                .Select(tile => $"{(tile.ProviderId == AccountTileProvider ? "Account status" : "Unavailable provider")} · {tile.Size} · {tile.Visibility}" +
                    (tile.Pinned ? " · pinned" : "") + (tile.Locked ? " · locked" : "")).ToArray(),
            "SelectedDashboardTileIndex" => _selectedTile,
            "DashboardLayoutAvailable" => LayoutAvailable,
            "DashboardHasLayout" => !_disposed && _layout is not null,
            "AllowAIGeneratedTiles" => _layout?.AllowAiGeneratedTiles == true,
            "AllowAIReorder" => _layout?.AllowAiReorder == true,
            "LibraryAvailable" or "ActivityAvailable" or "ModelAvailable" or "PackageAvailable" => false,
            _ => null,
        };
        return value is not null || _surface.TryGetValue(path, out value);
    }

    public bool? IsActionAvailable(string command)
    {
        if (_disposed) return false;
        if (LayoutCommands.Contains(command))
        {
            if (!LayoutAvailable) return false;
            if (command == "AddDashboardTile") return _layout!.Tiles.Count < 256;
            if (command.StartsWith("Enable", StringComparison.Ordinal) || command.StartsWith("Disable", StringComparison.Ordinal) ||
                command == "ResetDashboardLayout") return true;
            return SelectedTile is not null;
        }
        if (command == "AccountRefresh") return _account?.IsActionAvailable("Refresh") == true;
        if (AccountActions.TryGetValue(command, out var accountCommand))
            return _account?.IsActionAvailable(accountCommand) == true;
        if (FeatureActions.TryGetValue(command, out var featureRoute))
            return _isRegisteredRoute?.Invoke(featureRoute) == true;
        return command == "NavigateSettings" || ShellActions.ContainsKey(command);
    }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
        DispatchCore(command, parameter, cancellationToken, null);

    public ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime) =>
        DispatchCore(command, parameter, lifetime.CallerCancellation, lifetime);

    private ValueTask DispatchCore(string command, object? parameter, CancellationToken caller,
        CuiActionDispatchLifetime? lifetime)
    {
        caller.ThrowIfCancellationRequested();
        if (_disposed) return ValueTask.CompletedTask;
        if (command == "AccountRefresh" && _account is not null && _accountOwner is not null)
            return _layoutOwner is null ? new(_accountOwner.RefreshHomeAsync(_account, caller, lifetime)) :
                new(_layoutOwner.RunViewAsync(() => RefreshAccountAndLayoutAsync(caller, lifetime), caller));
        if (LayoutCommands.Contains(command) && _layoutOwner is not null)
        {
            if (IsActionAvailable(command) != true) return ValueTask.CompletedTask;
            return new(_layoutOwner.RunViewAsync(() => ChangeLayoutAsync(command, caller), caller));
        }
        if (AccountActions.TryGetValue(command, out var accountCommand) && _account is not null)
            // Preserve the maintained broker transfer and explicit caller/view distinction.
            return lifetime is { } owned ? _account.DispatchWithLifetimeAsync(accountCommand, parameter, owned)
                : _account.DispatchAsync(accountCommand, parameter, caller);
        if (command == "NavigateSettings") _navigate(new(HomeFeatureRouteIds.Settings));
        else if (ShellActions.TryGetValue(command, out var route)) _navigate(new(HomeRouteIds.For(route)));
        else if (FeatureActions.TryGetValue(command, out var featureRoute) &&
            _isRegisteredRoute?.Invoke(featureRoute) == true)
            _navigate(new(featureRoute));
        else Program.ShowStatus("HomeServiceUnavailable", "This action requires an available Home service.");
        return ValueTask.CompletedTask;
    }

    private const string AccountTileProvider = "home.browser.account";
    private static readonly HashSet<string> LayoutCommands = new(StringComparer.Ordinal)
    {
        "AddDashboardTile", "RemoveSelectedDashboardTile", "MoveSelectedDashboardTileUp", "MoveSelectedDashboardTileDown",
        "PinSelectedDashboardTile", "UnpinSelectedDashboardTile", "LockSelectedDashboardTile", "UnlockSelectedDashboardTile",
        "HideSelectedDashboardTile", "ShowSelectedDashboardTile", "RefreshSelectedDashboardTile",
        "ResizeSelectedDashboardTileSmall", "ResizeSelectedDashboardTileMedium", "ResizeSelectedDashboardTileLarge", "ResizeSelectedDashboardTileWide",
        "EnableAIGeneratedTiles", "DisableAIGeneratedTiles", "EnableAIReorder", "DisableAIReorder", "ResetDashboardLayout",
    };
    private HomeTileInstance? SelectedTile => _layout?.Tiles.OrderBy(tile => tile.Order).ElementAtOrDefault(_selectedTile);
    private bool LayoutAvailable => !_disposed && !_layoutBusy && _layout is not null && _layoutBinding is { Revoked: false, Actor: { } actor } &&
        _layoutOwner is { IsRevoked: false } &&
        AccountValue("HasAccount") is true && AccountValue("AccountId") is string id && id == actor.AccountId?.ToString("D");
    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || path != "SelectedDashboardTileIndex" || value is not int index || _layout is null || index < -1 || index >= _layout.Tiles.Count)
            return false;
        _selectedTile = index; Notify(); return true;
    }
    private void ClearLayout()
    {
        _layout = null; _selectedTile = -1; _layoutBinding?.Dispose(); _layoutBinding = null;
    }
    private void Notify() { if (!_disposed) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    private void OnChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed) return;
        if (_layoutBinding?.Actor is { } actor && (AccountValue("HasAccount") is not true ||
            (AccountValue("AccountId") as string) != actor.AccountId?.ToString("D")))
        { ClearLayout(); _layoutStatus = "The account changed. Refresh Home to reopen its dashboard."; }
        Notify();
    }
    private async Task RefreshAccountAndLayoutAsync(CancellationToken caller, CuiActionDispatchLifetime? lifetime = null)
    {
        if (_disposed || _account is null || _accountOwner is null || _layoutOwner is null) return;
        if (_layoutBusy) return;
        _layoutBusy = true; ClearLayout(); Notify();
        try
        {
            await _accountOwner.RefreshHomeAsync(_account, caller, lifetime);
            caller.ThrowIfCancellationRequested();
            if (_disposed) return;
            if (AccountValue("HasAccount") is not true)
            { _layoutStatus = "Sign in to save and reopen your dashboard on this browser."; return; }
            var binding = _layoutOwner.CreateBinding(); _layoutBinding = binding;
            var actual = await binding.LoadAsync(caller);
            _layoutOwner.DemandCurrent(); caller.ThrowIfCancellationRequested();
            if (_disposed || !ReferenceEquals(binding, _layoutBinding)) return;
            if (binding.Actor?.AccountId?.ToString("D") != (AccountValue("AccountId") as string))
                throw new InvalidOperationException("The displayed account and verified Home profile differ.");
            _layout = actual ?? new HomeDashboardLayout(1, 0, [], false, false);
            _selectedTile = _layout.Tiles.Count == 0 ? -1 : 0;
            _layoutStatus = actual is null ? "Your dashboard is ready to customise. Changes are saved for this account on this browser."
                : $"Reopened saved dashboard revision {actual.Revision}.";
        }
        catch (Exception cause)
        {
            if (!_disposed) { ClearLayout(); _layoutStatus = "Dashboard could not be reopened. " + cause.Message; }
            throw; // The actual view/source Task remains owned; UI presentation never substitutes settlement.
        }
        finally { _layoutBusy = false; Notify(); }
    }
    private async Task ChangeLayoutAsync(string command, CancellationToken caller)
    {
        if (!LayoutAvailable || _layoutBinding is not { } binding || _layout is not { } before) return;
        var selected = SelectedTile;
        _layoutBusy = true; Notify();
        try
        {
            if (command == "RefreshSelectedDashboardTile")
            {
                var current = await binding.LoadAsync(caller);
                _layoutOwner!.DemandCurrent(); caller.ThrowIfCancellationRequested();
                if (!_disposed && ReferenceEquals(binding, _layoutBinding))
                { _layout = current ?? new(1, 0, [], false, false); _layoutStatus = "Dashboard read from its original saved profile."; }
                return;
            }
            var tiles = before.Tiles.OrderBy(tile => tile.Order).ToList();
            var next = before;
            if (command == "AddDashboardTile")
                tiles.Add(new(Guid.NewGuid().ToString("D"), "AccountStatus", AccountTileProvider, tiles.Count,
                    HomeTileSize.Medium, HomeTileVisibility.Visible, false, false, new Dictionary<string, string>(),
                    "Verified account profile observation", [binding.Actor!.AccountId!.Value.ToString("D")], HomeTileLifetime.Persistent));
            else if (command == "ResetDashboardLayout") { tiles.Clear(); next = next with { AllowAiGeneratedTiles = false, AllowAiReorder = false }; }
            else if (command is "EnableAIGeneratedTiles" or "DisableAIGeneratedTiles") next = next with { AllowAiGeneratedTiles = command == "EnableAIGeneratedTiles" };
            else if (command is "EnableAIReorder" or "DisableAIReorder") next = next with { AllowAiReorder = command == "EnableAIReorder" };
            else if (selected is not null)
            {
                var index = tiles.FindIndex(tile => tile.TileInstanceId == selected.TileInstanceId);
                if (command == "RemoveSelectedDashboardTile") tiles.RemoveAt(index);
                else if (command is "MoveSelectedDashboardTileUp" or "MoveSelectedDashboardTileDown")
                {
                    var target = index + (command == "MoveSelectedDashboardTileUp" ? -1 : 1);
                    if (target < 0 || target >= tiles.Count) return;
                    (tiles[index], tiles[target]) = (tiles[target], tiles[index]);
                }
                else tiles[index] = command switch
                {
                    "PinSelectedDashboardTile" => selected with { Pinned = true }, "UnpinSelectedDashboardTile" => selected with { Pinned = false },
                    "LockSelectedDashboardTile" => selected with { Locked = true }, "UnlockSelectedDashboardTile" => selected with { Locked = false },
                    "HideSelectedDashboardTile" => selected with { Visibility = HomeTileVisibility.Hidden }, "ShowSelectedDashboardTile" => selected with { Visibility = HomeTileVisibility.Visible },
                    "ResizeSelectedDashboardTileSmall" => selected with { Size = HomeTileSize.Small }, "ResizeSelectedDashboardTileMedium" => selected with { Size = HomeTileSize.Medium },
                    "ResizeSelectedDashboardTileLarge" => selected with { Size = HomeTileSize.Large }, "ResizeSelectedDashboardTileWide" => selected with { Size = HomeTileSize.Wide },
                    _ => throw new InvalidOperationException("The original Home manual layout command is unknown."),
                };
            }
            next = next with { Revision = checked(before.Revision + 1), ParentRevision = before.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ChangeKind = HomeTileChangeKind.Manual, Tiles = tiles.Select((tile, index) => tile with { Order = index }).ToArray() };
            var saved = await binding.TrySaveAsync(before.Revision, next, caller);
            var observed = saved ? next : await binding.LoadAsync(caller);
            _layoutOwner!.DemandCurrent(); caller.ThrowIfCancellationRequested();
            if (!_disposed && ReferenceEquals(binding, _layoutBinding))
            {
                _layout = observed ?? new(1, 0, [], false, false);
                _selectedTile = _layout.Tiles.Count == 0 ? -1 : Math.Clamp(_selectedTile, 0, _layout.Tiles.Count - 1);
                _layoutStatus = saved ? $"Saved dashboard revision {next.Revision}." : "Another view changed this dashboard. Your proposal was retained as a conflict; the saved layout was reopened.";
            }
        }
        catch (Exception cause)
        {
            if (!_disposed) { ClearLayout(); _layoutStatus = "Dashboard operation failed. " + cause.Message; }
            throw;
        }
        finally { _layoutBusy = false; Notify(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PropertyChanged = null;
        ClearLayout();
        // Actual account fields are cleared before cancellation/disposal callbacks.
        _account?.RevokePrivateContext();
        _surface.PropertyChanged -= OnChanged;
        if (_account is not null) _account.PropertyChanged -= OnChanged;
        _account?.Dispose(); // The registered feature retains and joins the SAME binding.
    }
}
