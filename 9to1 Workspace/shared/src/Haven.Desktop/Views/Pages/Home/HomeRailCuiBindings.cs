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
    private bool _showLabels;

    public HomeRailCuiBindings(Func<string> section, bool settingsAvailable, bool libraryAvailable, bool assistantsAvailable)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _values.Set("rail.settingsAvailable", settingsAvailable);
        _values.Set("rail.libraryAvailable", libraryAvailable);
        _values.Set("rail.assistantsAvailable", assistantsAvailable);
        _values.PropertyChanged += (_, args) => PropertyChanged?.Invoke(this, args);
        SetViewportWidth(0);
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh()
    {
        UpdateRailDimensions();
        _values.Set("Revision", _revision++);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public void SetViewportWidth(double width)
    {
        _showLabels = width >= 840;
        _values.Set("rail.showLabels", _showLabels);
        UpdateRailDimensions();
        _values.Set("newTab.contentPadding", _showLabels ? "34 26 34 24" : "12 16 12 16");
        _values.Set("newTab.showActionLabels", width >= 470);
    }

    private void UpdateRailDimensions()
    {
        _values.Set("rail.itemWidth", _showLabels ? 210d : 44d);
        _values.Set("rail.brandWidth", _showLabels ? 122d : 44d);
        _values.Set("rail.sidebarPadding", _showLabels ? "12 8 11 8" : "5 8 5 8");
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
            "rail.pageGap" => section is "settings" or "newtab" ? 0d : 8d,
            "rail.pagePadding" => section is "settings" or "newtab" ? "0" : "16",
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
