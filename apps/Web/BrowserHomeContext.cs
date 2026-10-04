using System.ComponentModel;
using CakeOS.Cui;
using HavenOS.Home;
using HavenOS.Home.Core;

namespace NineToOne.Web;

/// <summary>Projects current Home-owned navigation with explicit absent cloud-provider state.</summary>
internal sealed class BrowserHomeContext : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private static readonly IReadOnlyDictionary<string, HomeRoute> ShellActions = new Dictionary<string, HomeRoute>(StringComparer.Ordinal)
    {
        ["NavigateDashboard"] = HomeRoute.Dashboard,
        ["NavigateLibrary"] = HomeRoute.Library,
        ["NavigateEvents"] = HomeRoute.Events,
    };
    private readonly HomeCuiSurface _surface;
    private readonly HomeNavigationState _navigation = new();
    private readonly Action<HomeFeatureNavigationRequest> _navigate;

    public BrowserHomeContext(CuiDocument document, Action<HomeFeatureNavigationRequest> navigate)
    {
        _surface = new(document);
        _navigate = navigate;
        _surface.PropertyChanged += (_, args) => PropertyChanged?.Invoke(this, args);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Open(HomeFeatureNavigationRequest request)
    {
        if (!CanOpen(request)) return false;
        var route = ShellActions.Values.FirstOrDefault(value => HomeRouteIds.For(value) == request.RouteId);
        _navigation.Navigate(route);
        _navigation.State(route).SelectedObjectId = request.EntityId;
        _surface.ApplyNavigation(_navigation);
        return true;
    }

    internal static bool CanOpen(HomeFeatureNavigationRequest request) =>
        ShellActions.Values.Any(route => HomeRouteIds.For(route) == request.RouteId);

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "BriefSummary" => "Your briefing is unavailable. Connect your account services to load authorised activity.",
            "CatalogSummary" => "Local app installation is unavailable in this browser.",
            "RuntimeSummary" => "Cloud model services are unavailable.",
            "OperationSummary" => "Account services are unavailable. No changes have been saved.",
            "LibrarySummary" => "Library is unavailable. Your files have not been loaded.",
            "LibraryItemsSummary" => "",
            "EventsSummary" => "Activity is unavailable. Your events have not been loaded.",
            "EventItemsSummary" => "",
            "DashboardTilesSummary" => "Dashboard customisation requires account services.",
            _ => null,
        };
        return value is not null || _surface.TryGetValue(path, out value);
    }

    public bool? IsActionAvailable(string command) => ShellActions.ContainsKey(command);

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ShellActions.TryGetValue(command, out var route)) _navigate(new(HomeRouteIds.For(route)));
        else Program.ShowStatus("HomeServiceUnavailable", "This action requires an available account service.");
        return ValueTask.CompletedTask;
    }
}
