using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Browse;

internal sealed partial class BrowseEngineViewport
{
    private bool _observedTabSearchOpen;
    private Control? _tabSearchReturnFocus;
    private long _tabSearchFocusEpoch;

    private void OnCaptionPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.Handled || args.Source is not Control source || _findKeyScope is not { } scope) return;
        var ancestors = source.GetSelfAndVisualAncestors().OfType<Control>()
            .TakeWhile(control => !ReferenceEquals(control, scope)).ToArray();
        if (!ancestors.Any(control => control.Name == "BrowseCaptionDragRegion") ||
            ancestors.Any(control => control is Button or TextBox or SelectingItemsControl or MenuBase)) return;
        args.Handled = _workspace.HandleCaptionPointer(args);
    }

    private bool IsTabSearchGesture(KeyEventArgs args)
    {
        var gesture = _workspace.TabSearchShortcut;
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        try { return KeyGesture.Parse(gesture).Matches(args); }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private async void OnTabSearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled || !_workspace.IsReady || _findKeyScope is not { } scope ||
            args.Source is not Control source || !scope.IsVisualAncestorOf(source) ||
            !ReferenceEquals(CurrentSceneFocus(), source)) return;
        // The child's own shortcut/cancel handler runs first. Native menus and independent
        // focus scopes keep their own keys, including the operating system's Alt+Space.
        if (source.GetSelfAndVisualAncestors().TakeWhile(item => !ReferenceEquals(item, scope))
            .Any(item => item is IFocusScope or OverlayPopupHost)) return;
        try
        {
            if (IsTabSearchGesture(args))
            {
                const string command = "9to1.Browse.ToggleTabSearch";
                if (_workspace.IsActionAvailable(command) != true) return;
                args.Handled = true;
                await _workspace.DispatchAsync(command, null);
                return;
            }
            if (!_workspace.TabSearchOpen || args.KeyModifiers != KeyModifiers.None) return;
            if (args.Key == Key.Escape)
            {
                _workspace.CloseTabSearch(restoreFocus: true);
                args.Handled = true;
                return;
            }
            if (args.Key == Key.Enter && source.Name == "BrowseTabSearchQuery")
            {
                // An empty result never turns the user's private search draft into navigation.
                if (_workspace.FirstTabSearchTarget is not { } target ||
                    _workspace.IsActionAvailable("9to1.Browse.SelectTab") != true) return;
                args.Handled = true;
                await _workspace.DispatchAsync("9to1.Browse.SelectTab", target);
                return;
            }
            if (args.Key is not (Key.Down or Key.Up) ||
                source.Name is not ("BrowseTabSearchQuery" or "BrowseTabSearchResult")) return;
            var results = scope.GetVisualDescendants().OfType<Button>().Where(button =>
                button.Name == "BrowseTabSearchResult" && button.IsEffectivelyVisible && button.IsEffectivelyEnabled).ToArray();
            if (results.Length == 0) return;
            var current = Array.IndexOf(results, source);
            if (current == 0 && args.Key == Key.Up)
            {
                args.Handled = SearchQuery(scope)?.Focus(NavigationMethod.Directional) == true;
                return;
            }
            var next = current < 0 ? (args.Key == Key.Down ? 0 : results.Length - 1) :
                Math.Clamp(current + (args.Key == Key.Down ? 1 : -1), 0, results.Length - 1);
            args.Handled = results[next].Focus(NavigationMethod.Directional);
            if (args.Handled) results[next].BringIntoView();
        }
        catch (Exception failure)
        { System.Diagnostics.Trace.TraceError("Browse retained the original tab-search action: {0}", failure); }
    }

    private static TextBox? SearchQuery(CuiSceneHost scope) => scope.GetVisualDescendants().OfType<TextBox>()
        .FirstOrDefault(control => control.Name == "BrowseTabSearchQuery" && control.IsEffectivelyVisible);

    private async void ObserveTabSearchFocus()
    {
        if (_observedTabSearchOpen == _workspace.TabSearchOpen) return;
        _observedTabSearchOpen = _workspace.TabSearchOpen;
        var epoch = ++_tabSearchFocusEpoch;
        if (_findKeyScope is not { } scope || !_workspace.IsReady) return;
        var opening = _workspace.TabSearchOpen;
        if (opening)
        {
            _tabSearchReturnFocus = CurrentSceneFocus();
            if (_tabSearchReturnFocus?.Name == "BrowseTabSearchButton") _tabSearchReturnFocus = null;
        }
        else if (!_workspace.RestoreFocusAfterTabSearch) { _tabSearchReturnFocus = null; return; }
        var previous = _tabSearchReturnFocus;
        try
        {
            await _workspace.FocusFindControlAsync(() =>
            {
                if (epoch != _tabSearchFocusEpoch || !ReferenceEquals(scope, _findKeyScope) ||
                    opening != _workspace.TabSearchOpen || !scope.IsAttachedToVisualTree()) return;
                if (opening)
                {
                    if (SearchQuery(scope) is { IsEffectivelyEnabled: true } query)
                    { query.Focus(NavigationMethod.Tab); query.SelectAll(); }
                }
                else
                {
                    if (previous is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } &&
                        scope.IsVisualAncestorOf(previous) && previous.Focus(NavigationMethod.Tab)) return;
                    var selected = _workspace.State.SelectedTabId.ToString("D");
                    TabTitles().FirstOrDefault(button => TabStableKey(button) == selected)?.Focus(NavigationMethod.Tab);
                }
            });
        }
        catch (Exception failure)
        { System.Diagnostics.Trace.TraceError("Browse retained its tab-search focus operation: {0}", failure); }
        finally { if (!opening && epoch == _tabSearchFocusEpoch) _tabSearchReturnFocus = null; }
    }
}
