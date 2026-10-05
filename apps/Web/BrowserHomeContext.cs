using System.ComponentModel;
using CakeOS.Cui;
using HavenOS.Home;
using HavenOS.Home.Core;
using NineToOne.Web.Accounts;

namespace NineToOne.Web;

/// <summary>Projects the existing verified account owner; other Home providers stay unavailable.</summary>
internal sealed class BrowserHomeContext : ICuiBindingContext, ICuiLifetimeAwareActionDispatcher,
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
    private readonly HomeCuiSurface _surface;
    private readonly HomeNavigationState _navigation = new();
    private readonly Action<HomeFeatureNavigationRequest> _navigate;
    private readonly AccountSettingsFeature? _accountOwner;
    private readonly AccountBrowserBindings? _account;
    private Task? _activation;
    private bool _disposed;

    public BrowserHomeContext(CuiDocument document, Action<HomeFeatureNavigationRequest> navigate,
        AccountSettingsFeature? accountOwner = null)
    {
        _surface = new(document);
        _navigate = navigate;
        _accountOwner = accountOwner;
        // The SAME registered feature enrols this binding before any original service callback.
        _account = accountOwner?.CreateHomeBinding();
        _surface.PropertyChanged += OnChanged;
        if (_account is not null) _account.PropertyChanged += OnChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Task ActivateAsync()
    {
        if (_disposed || _account is null || _accountOwner is null) return Task.CompletedTask;
        return _activation ??= _accountOwner.RefreshHomeAsync(_account);
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
            "BriefSummary" => "Your briefing is unavailable. Authorised activity services are not connected.",
            "CatalogSummary" => "Local app installation is unavailable in this browser.",
            "RuntimeSummary" => "Cloud model services are unavailable.",
            "OperationSummary" => (AccountValue("Status") ?? "Account services are unavailable.") +
                " Home files and activity have not been loaded.",
            "LibrarySummary" => "Library is unavailable. Your files have not been loaded.",
            "LibraryItemsSummary" => "",
            "EventsSummary" => "Activity is unavailable. Your events have not been loaded.",
            "EventItemsSummary" => "",
            "DashboardTilesSummary" => "Dashboard customisation requires a Home layout provider.",
            "LibraryAvailable" or "ActivityAvailable" or "ModelAvailable" or "PackageAvailable" => false,
            _ => null,
        };
        return value is not null || _surface.TryGetValue(path, out value);
    }

    public bool? IsActionAvailable(string command)
    {
        if (_disposed) return false;
        if (command == "AccountRefresh") return _account?.IsActionAvailable("Refresh") == true;
        if (AccountActions.TryGetValue(command, out var accountCommand))
            return _account?.IsActionAvailable(accountCommand) == true;
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
            return new(_accountOwner.RefreshHomeAsync(_account, caller, lifetime));
        if (AccountActions.TryGetValue(command, out var accountCommand) && _account is not null)
            // Preserve the maintained broker transfer and explicit caller/view distinction.
            return lifetime is { } owned ? _account.DispatchWithLifetimeAsync(accountCommand, parameter, owned)
                : _account.DispatchAsync(accountCommand, parameter, caller);
        if (command == "NavigateSettings") _navigate(new(HomeFeatureRouteIds.Settings));
        else if (ShellActions.TryGetValue(command, out var route)) _navigate(new(HomeRouteIds.For(route)));
        else Program.ShowStatus("HomeServiceUnavailable", "This action requires an available Home service.");
        return ValueTask.CompletedTask;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs args)
    { if (!_disposed) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PropertyChanged = null;
        // Actual account fields are cleared before cancellation/disposal callbacks.
        _account?.RevokePrivateContext();
        _surface.PropertyChanged -= OnChanged;
        if (_account is not null) _account.PropertyChanged -= OnChanged;
        _account?.Dispose(); // The registered feature retains and joins the SAME binding.
    }
}
