using Avalonia.Input;
using Avalonia.Threading;

namespace HavenOS.Apps.Browse;

/// <summary>The native window owner supplies actual geometry and its user-close entry.
/// Browse never owns a replacement window or bypasses the App close preflight.</summary>
public interface IBrowseNativeWindowControls
{
    bool IsAvailable { get; }
    bool CanMinimize { get; }
    bool CanMaximize { get; }
    bool IsMaximized { get; }
    event EventHandler? StateChanged;
    void Minimize();
    void ToggleMaximize();
    void RequestClose();
    void BeginMoveDrag(PointerPressedEventArgs args);
    // Keep the platform caption available if the authored scene cannot mount.
    void SetCaptionReady(bool ready) { }
}

public sealed partial class BrowseNativeWorkspace
{
    private readonly IBrowseNativeWindowControls? _windowControls;
    private readonly Func<string>? _tabSearchShortcut;
    private TabRow[] _allTabRows = [], _tabSearchRows = [];
    private string _tabSearchQuery = string.Empty;
    private bool _tabSearchOpen, _windowMenuOpen;
    private double _searchViewportWidth = 420, _searchViewportHeight = 520;
    internal bool TabSearchOpen => _tabSearchOpen;
    internal bool RestoreFocusAfterTabSearch { get; private set; }
    internal string TabSearchShortcut => _tabSearchShortcut?.Invoke() ?? "Ctrl+Shift+A";

    private bool TryGetCaptionSearchBinding(string path, out object? value)
    {
        var available = IsOriginalPublicationCurrent && _windowControls?.IsAvailable == true;
        value = path switch
        {
            "WindowControlsVisible" => available,
            "WindowCanMinimize" => available && _windowControls!.CanMinimize,
            "WindowCanMaximize" => available && _windowControls!.CanMaximize,
            "WindowCanClose" => available,
            "WindowMaximizeLabel" => _windowControls?.IsMaximized == true ? "Restore window" : "Maximize window",
            "WindowMaximizeGlyph" => _windowControls?.IsMaximized == true ? "❐" : "□",
            "WindowMenuOpen" => available && _windowMenuOpen,
            "TabSearchOpen" => _tabSearchOpen,
            "TabSearchQuery" => _tabSearchQuery,
            "TabSearchRows" => _tabSearchRows,
            "TabSearchEmpty" => _tabSearchRows.Length == 0,
            "TabSearchSummary" => $"{_tabSearchRows.Length} of {_allTabRows.Length} open tabs · this window",
            "TabSearchTooltip" => string.IsNullOrWhiteSpace(TabSearchShortcut) ? "Search open tabs" : $"Search open tabs ({TabSearchShortcut})",
            "TabSearchWidth" => Math.Max(260, Math.Min(440, _searchViewportWidth - 24)),
            "TabSearchMaxHeight" => Math.Max(180, Math.Min(480, _searchViewportHeight - 72)),
            "Browse.TabClusterMaximumWidth" => Math.Max(130, _searchViewportWidth - (available ? 96 : 0) - 114),
            _ => null
        };
        return value is not null;
    }

    internal void SetSearchViewport(double width, double height)
    {
        if (width <= 0 || height <= 0 || (_searchViewportWidth == width && _searchViewportHeight == height)) return;
        _searchViewportWidth = width; _searchViewportHeight = height; Changed();
    }

    private void IssueTabSearchRows()
    {
        var query = _tabSearchQuery.Trim();
        // Only issued tab titles and the same allowed destination display as the address bar.
        // This never examines document contents, messages, history or closed tabs.
        _tabSearchRows = _allTabRows.Where(row => query.Length == 0 ||
            row.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            row.SearchDestination.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    internal void CloseTabSearch(bool restoreFocus)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_tabSearchOpen) return;
        RestoreFocusAfterTabSearch = restoreFocus;
        _tabSearchOpen = false;
        Changed();
    }

    private void ToggleTabSearch()
    {
        if (_tabSearchOpen) { CloseTabSearch(restoreFocus: true); return; }
        _profileMenuOpen = _manageUsersOpen = _windowMenuOpen = false;
        RestoreFocusAfterTabSearch = false;
        _tabSearchOpen = true;
        IssueTabSearchRows();
        Changed();
    }

    internal object? FirstTabSearchTarget => _tabSearchRows.FirstOrDefault()?.Target;

    internal bool HandleCaptionPointer(PointerPressedEventArgs args)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!IsOriginalPublicationCurrent || _windowControls?.IsAvailable != true) return false;
        var properties = args.GetCurrentPoint(null).Properties;
        if (properties.IsRightButtonPressed)
        {
            _profileMenuOpen = _manageUsersOpen = false;
            CloseTabSearch(restoreFocus: false);
            _windowMenuOpen = true;
            Changed();
            return true;
        }
        if (!properties.IsLeftButtonPressed) return false;
        DismissTransientMenus();
        if (args.ClickCount == 2)
        {
            if (_windowControls.CanMaximize) _windowControls.ToggleMaximize();
        }
        else if (args.ClickCount == 1) _windowControls.BeginMoveDrag(args);
        return true;
    }

    internal void RunNativeWindowControl(string action)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!IsOriginalPublicationCurrent || _windowControls?.IsAvailable != true) return;
        DismissTransientMenus();
        switch (action)
        {
            case "minimize" when _windowControls.CanMinimize: _windowControls.Minimize(); break;
            case "maximize" when _windowControls.CanMaximize: _windowControls.ToggleMaximize(); break;
            // Called by an actionless authored native Button. The App can now join all
            // executing Browse originals without attempting to join this close callback.
            case "close": _windowControls.RequestClose(); break;
        }
    }

    private void OnWindowControlsChanged(object? sender, EventArgs args)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_disposed) Changed();
    }
}
