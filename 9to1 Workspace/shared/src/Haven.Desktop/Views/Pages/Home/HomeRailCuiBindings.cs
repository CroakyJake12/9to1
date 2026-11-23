using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Pages.Home;

/// <summary>Projects Home's current destination and available space into the authored rail.</summary>
internal sealed class HomeRailCuiBindings : ICuiWritableBindingContext, INotifyPropertyChanged
{
    private readonly Func<string> _section;
    private readonly CuiViewModel _values = new();
    private long _revision;

    public HomeRailCuiBindings(Func<string> section)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _values.PropertyChanged += (_, args) => PropertyChanged?.Invoke(this, args);
        SetViewportWidth(0);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        _values.Set("Revision", _revision++);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public void SetViewportWidth(double width)
    {
        var showLabels = width >= 840;
        _values.Set("rail.showLabels", showLabels);
        _values.Set("rail.itemWidth", showLabels ? 196d : 44d);
    }

    public bool TryGetValue(string path, out object? value)
    {
        var section = _section();
        value = path switch
        {
            "rail.appsSelected" => section == "apps",
            "rail.dashboardSelected" => section == "dashboard",
            "rail.modelsSelected" => section == "models",
            "rail.settingsSelected" => section == "settings",
            "rail.homeVisible" => section != "settings",
            "rail.pageGap" => section == "settings" ? 0d : 8d,
            "rail.pagePadding" => section == "settings" ? "0" : "16",
            "rail.eventsSelected" => section == "events",
            "rail.librarySelected" => section == "library",
            "rail.newtabSelected" => section == "newtab",
            _ => Unresolved
        };
        return !ReferenceEquals(value, Unresolved) || _values.TryGetValue(path, out value);
    }

    public bool TrySetValue(string path, object? value) => false;

    private static readonly object Unresolved = new();
}
