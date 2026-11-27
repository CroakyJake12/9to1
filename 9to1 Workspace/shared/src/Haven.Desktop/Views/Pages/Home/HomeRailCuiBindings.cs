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

    public HomeRailCuiBindings(Func<string> section, bool settingsAvailable, bool libraryAvailable, bool assistantsAvailable,
        bool exploreAvailable = false, bool specialistsAvailable = false)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));
        _values.Set("rail.settingsAvailable", settingsAvailable);
        _values.Set("rail.libraryAvailable", libraryAvailable);
        _values.Set("rail.assistantsAvailable", assistantsAvailable);
        _values.Set("rail.exploreAvailable", exploreAvailable);
        _values.Set("rail.specialistsAvailable", specialistsAvailable);
        _values.Set("rail.exploreTooltip", exploreAvailable ? "Explore" : "Explore is unavailable in this host.");
        _values.Set("rail.specialistsTooltip", specialistsAvailable ? "Specialists" : "The Specialists owner is unavailable in this host.");
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
        _showLabels = width > 750;
        _values.Set("rail.showLabels", _showLabels);
        UpdateRailDimensions();
        _values.Set("newTab.contentPadding", _showLabels ? "34 26 34 24" : "12 16 12 16");
        _values.Set("newTab.showActionLabels", width >= 470);
    }

    private void UpdateRailDimensions()
    {
        _values.Set("rail.sidebarWidth", _showLabels ? 234d : 68d);
        _values.Set("rail.itemWidth", _showLabels ? 210d : 52d);
        _values.Set("rail.brandWidth", _showLabels ? 132d : 52d);
        _values.Set("rail.sidebarPadding", _showLabels ? "12 10 12 12" : "8 10 8 12");
        _values.Set("rail.itemPadding", _showLabels ? "14 9" : "16 9");
        _values.Set("rail.brandPadding", _showLabels ? "10 5 14 5" : "11 5");
    }

    public bool TryGetValue(string path, out object? value)
    {
        var section = _section();
        var selectedSection = section is "hubs" or "hubs-customise" ? "apps" : section;
        if (path.StartsWith("rail.", StringComparison.Ordinal) && path.EndsWith("FontWeight", StringComparison.Ordinal))
        {
            value = path.AsSpan(5, path.Length - 5 - "FontWeight".Length).SequenceEqual(selectedSection) ? "750" : "550";
            return true;
        }
        value = path switch
        {
            "rail.appsSelected" => selectedSection == "apps",
            "rail.exploreSelected" => section == "explore",
            "rail.specialistsSelected" => section == "specialists",
            "rail.assistantsSelected" => section == "assistants",
            "rail.dashboardSelected" => section == "dashboard",
            "rail.modelsSelected" => section == "models",
            "rail.settingsSelected" => section == "settings",
            "rail.homeVisible" => section != "settings",
            "rail.pageGap" => section is "settings" or "newtab" or "apps" or "hubs" or "hubs-customise" or "explore" or "specialists" ? 0d : 8d,
            "rail.customCanvas" => section == "newtab",
            "rail.pagePadding" => section is "settings" or "newtab" or "apps" or "hubs" or "hubs-customise" or "explore" or "specialists" ? "0" : "16",
            "rail.eventsSelected" => section == "events",
            "rail.librarySelected" => section == "library",
            "rail.newtabSelected" => section == "newtab",
            _ => Unresolved
        };
        return !ReferenceEquals(value, Unresolved) || _values.TryGetValue(path, out value);
    }

    public bool TrySetValue(string path, object? value) => false;

    internal void SetNewTabShortcut(string key, object? value) => _values.Set("newTab.shortcut." + key, value);

    private static readonly object Unresolved = new();
}
