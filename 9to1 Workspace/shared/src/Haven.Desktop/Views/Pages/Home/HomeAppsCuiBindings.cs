using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>One launcher entry, projected from the registered mode record. The identity, name,
/// description and icon key all come from stored product state; nothing here is illustrative.</summary>
public sealed record HomeAppItem(string Key, string Name, string Description, string IconKey)
{
    /// <summary>The accessible action a tile performs, so the launcher never relies on the visible
    /// name alone to say what activation does.</summary>
    public string OpenLabel => "Open " + Name;
}

/// <summary>The current launcher projection the authored document reads: the filtered catalogue,
/// the live query and the truthful availability/empty state.</summary>
public sealed record HomeAppsViewState(
    IReadOnlyList<HomeAppItem> All,
    IReadOnlyList<HomeAppItem> Visible,
    string Query,
    string Status)
{
    public static HomeAppsViewState Empty { get; } =
        new([], [], string.Empty, "Loading the installed apps.");

    public bool HasQuery => Query.Length > 0;
    public bool HasVisibleApps => Visible.Count > 0;
    public bool HasNoVisibleApps => Visible.Count == 0;

    public string CountLabel => All.Count == 1 ? "1 app" : All.Count.ToString(System.Globalization.CultureInfo.CurrentCulture) + " apps";

    /// <summary>Distinct, actionable empty states: an unmatched search is not the same condition as
    /// a profile with nothing installed, and the launcher never shows a blank region for either.</summary>
    public string EmptyTitle => HasQuery
        ? "No app matches “" + Query + "”."
        : "No apps are available in this profile.";

    public string EmptyDetail => HasQuery
        ? "Clear the search, or use the address bar to open an app by name."
        : "Install an app from Home, then open it here.";

    public static IReadOnlyList<HomeAppItem> Filter(IReadOnlyList<HomeAppItem> all, string query) =>
        query.Length == 0
            ? all
            : all.Where(app => app.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase)
                || app.Key.Contains(query, System.StringComparison.OrdinalIgnoreCase)
                || app.Description.Contains(query, System.StringComparison.OrdinalIgnoreCase)).ToArray();
}

/// <summary>
/// CUI binding owner for <c>UnifiedHomeApps.cui</c>.
///
/// The authored launcher only reads values and raises commands. This class publishes the current
/// registered-catalogue projection and dispatches every action back through the Home page, so the
/// CUI document never calls domain methods directly and the page never substitutes a legacy list
/// control for the authored tiles.
/// </summary>
public sealed class HomeAppsCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private static readonly IReadOnlySet<string> KnownCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "home.open-app", "home.set-app-query", "home.clear-app-query"
    };

    private readonly CuiViewModel _values = new();
    private readonly Func<HomeAppsViewState> _state;
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private HomeAppsViewState _snapshot = HomeAppsViewState.Empty;
    private long _revision;

    public HomeAppsCuiBindings(
        Func<HomeAppsViewState> state,
        Func<string, object?, CancellationToken, ValueTask> dispatch)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _values.PropertyChanged += (_, args) => PropertyChanged?.Invoke(this, args);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HomeAppsViewState Snapshot => _state();

    public void Refresh()
    {
        _snapshot = _state();
        _values.Set("Revision", _revision++);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public bool TryGetValue(string path, out object? value)
    {
        var snapshot = Snapshot;
        value = path switch
        {
            "apps.all" => snapshot.All,
            "apps.visible" => snapshot.Visible,
            "apps.query" => snapshot.Query,
            "apps.hasQuery" => snapshot.HasQuery,
            "apps.hasVisibleApps" => snapshot.HasVisibleApps,
            "apps.hasNoVisibleApps" => snapshot.HasNoVisibleApps,
            "apps.countLabel" => snapshot.CountLabel,
            "apps.emptyTitle" => snapshot.EmptyTitle,
            "apps.emptyDetail" => snapshot.EmptyDetail,
            "apps.status" => snapshot.Status,
            _ => Unresolved
        };
        return !ReferenceEquals(value, Unresolved);
    }

    public bool TrySetValue(string path, object? value)
    {
        if (!path.Equals("apps.query", StringComparison.OrdinalIgnoreCase)) return false;
        _ = _dispatch("home.set-app-query", value, CancellationToken.None);
        return true;
    }

    public bool? IsActionAvailable(string command)
    {
        if (!KnownCommands.Contains(command)) return null;
        var snapshot = Snapshot;
        return command switch
        {
            // Clearing is only offered when a query is actually narrowing the catalogue.
            "home.clear-app-query" => snapshot.HasQuery,
            // Opening needs a real entry; an empty launcher never presents a live but useless control.
            "home.open-app" => snapshot.HasVisibleApps,
            _ => true
        };
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (!KnownCommands.Contains(command))
            throw new InvalidOperationException($"Home apps CUI command '{command}' is not owned by this binding.");
        await _dispatch(command, parameter, cancellationToken);
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is not HomeAppItem app) return false;
        value = path switch
        {
            _ when path.Equals("key", StringComparison.OrdinalIgnoreCase) => app.Key,
            _ when path.Equals("name", StringComparison.OrdinalIgnoreCase) => app.Name,
            _ when path.Equals("description", StringComparison.OrdinalIgnoreCase) => app.Description,
            _ when path.Equals("openLabel", StringComparison.OrdinalIgnoreCase) => app.OpenLabel,
            _ when path.Equals("iconSource", StringComparison.OrdinalIgnoreCase) => IconSource(app.IconKey),
            _ => null
        };
        return value is not null;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    private static string IconSource(string? key)
    {
        // The existing resolver accepts bounded symbolic keys. Unknown or malformed custom keys
        // use the catalogue's visible fallback without rewriting stored mode data.
        var symbolic = key?.Trim() ?? string.Empty;
        return symbolic.Length is > 0 and <= 64 && symbolic.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            ? "icon:" + symbolic : "icon:unknown";
    }

    private static readonly object Unresolved = new();
}
