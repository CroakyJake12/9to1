using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>
/// CUI binding owner for the icon-only Home/NewTab rail in <c>UnifiedHome.cui</c>.
///
/// The authored rail only reads selected-state booleans. This class projects the current in-page
/// section (Apps / Dashboard / Models / New Tab) so the authored rail can carry the theme-owned
/// Selected state without hardcoding selection in C#. Nothing here owns navigation: every rail
/// action still dispatches through the Home page (ICuiActionDispatcher), exactly as before.
/// </summary>
internal sealed class HomeRailCuiBindings : ICuiWritableBindingContext, INotifyPropertyChanged
{
    private readonly Func<string> _section;
    private readonly Func<bool> _settingsHasSidebar;
    private readonly CuiViewModel _values = new();
    private long _revision;

    public HomeRailCuiBindings(Func<string> section, Func<bool>? settingsHasSidebar=null)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _settingsHasSidebar=settingsHasSidebar??(()=>false);
        _values.PropertyChanged += (_, args) => PropertyChanged?.Invoke(this, args);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        _values.Set("Revision", _revision++);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public bool TryGetValue(string path, out object? value)
    {
        var section = _section();
        value = path switch
        {
            "rail.visible" => section != "settings" || !_settingsHasSidebar(),
            "rail.pagePadding" => section == "settings" && _settingsHasSidebar() ? "0" : "16",
            "rail.appsSelected" => section == "apps",
            "rail.dashboardSelected" => section == "dashboard",
            "rail.modelsSelected" => section == "models",
            "rail.settingsSelected" => section == "settings",
            "rail.eventsSelected" => section == "events",
            "rail.librarySelected" => section == "library",
            "rail.newtabSelected" => section == "newtab",
            _ => Unresolved
        };
        return !ReferenceEquals(value, Unresolved);
    }

    public bool TrySetValue(string path, object? value) => false;

    private static readonly object Unresolved = new();
}
