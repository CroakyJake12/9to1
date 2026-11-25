using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class UnifiedHomePage
{
    private const string ShortcutContextAutomationPrefix = "new-tab-shortcut-options-";
    private ContextMenu? _shortcutContextMenu;
    private Task _shortcutContextAction = Task.CompletedTask;
    private bool _shortcutEditorFromMenu;

    private enum ShortcutContextAction { Open, OpenInNewTab, Rename, Icon, Move, Remove }

    private void RegisterNewTabShortcutContextMenu()
    {
        AddHandler(ContextRequestedEvent, OnShortcutContextRequested);
        AddHandler(KeyUpEvent, OnShortcutContextKey, RoutingStrategies.Tunnel);
    }

    private void StopNewTabShortcutContextMenu()
    {
        RemoveHandler(ContextRequestedEvent, OnShortcutContextRequested);
        RemoveHandler(KeyUpEvent, OnShortcutContextKey);
        _shortcutContextMenu?.Close();
    }

    private void OnShortcutContextKey(object? sender, KeyEventArgs args)
    {
        if ((args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.Shift ||
             args.Key == Key.Apps && args.KeyModifiers == KeyModifiers.None) &&
            args.Source is Button anchor && GetShortcutContextId(anchor) is { } id)
            args.Handled = ShowShortcutContextMenu(anchor, id, fromPointer: false);
    }

    private void OnShortcutContextRequested(object? sender, ContextRequestedEventArgs args)
    {
        if (args.Source is Visual hit && FindShortcutDragCard(hit) is { } tile &&
            GetShortcutDragCardId(tile) is { } id && FindShortcutContextAnchor(id) is { } anchor)
            args.Handled = ShowShortcutContextMenu(anchor, id, args.TryGetPosition(null, out _));
    }

    private static string? GetShortcutContextId(Button button)
    {
        if (AutomationProperties.GetAutomationId(button) is not { } automation) return null;
        return button.Name switch
        {
            "home-newtab-shortcut" when automation.StartsWith(ShortcutAutomationPrefix, StringComparison.Ordinal) =>
                automation[ShortcutAutomationPrefix.Length..],
            "home-newtab-shortcut-options" when automation.StartsWith(ShortcutContextAutomationPrefix, StringComparison.Ordinal) =>
                automation[ShortcutContextAutomationPrefix.Length..],
            _ => null
        };
    }

    private Button? FindShortcutContextAnchor(string id) => _newTabShortcuts?.GetVisualDescendants().OfType<Button>()
        .FirstOrDefault(button => button.IsEffectivelyEnabled && button.IsEffectivelyVisible && GetShortcutContextId(button) == id);

    private void FocusShortcutContextAnchor(string id) => FocusAfterSelection(() =>
    {
        if (FindShortcutContextAnchor(id) is { } anchor && anchor.Focus(NavigationMethod.Tab)) anchor.BringIntoView();
    });

    private bool ShowShortcutContextMenu(Button anchor, string id, bool fromPointer)
    {
        if (!IsAlive || _section != "newtab" || ShortcutSurfaceOpen || _widgetPickerOpen || _widgetOptionsId is not null ||
            _shortcutDrag is not null || _widgetDrag is not null || _checkingShortcutApp || !anchor.IsAttachedToVisualTree() ||
            SavedShortcuts.FirstOrDefault(shortcut => shortcut.Id == id) is not { } shortcut)
            return false;
        var canOpen = IsShortcutAvailable(shortcut);
        var canEdit = CanOpenNewTabShortcutEditor;
        if (!canOpen && !canEdit) return false;
        _shortcutContextMenu?.Close();
        var visit = _selection;
        var menu = new ContextMenu
        {
            Focusable = true,
            PlacementTarget = anchor,
            Placement = fromPointer ? PlacementMode.Pointer : PlacementMode.Bottom,
            ItemsSource = new[]
            {
                Item("Open", ShortcutContextAction.Open, canOpen),
                Item("Open in new tab", ShortcutContextAction.OpenInNewTab, canOpen),
                Item("Rename shortcut", ShortcutContextAction.Rename, canEdit),
                Item("Change icon", ShortcutContextAction.Icon, canEdit),
                Item("Move", ShortcutContextAction.Move, canEdit && SavedShortcuts.Length > 1),
                Item("Remove shortcut", ShortcutContextAction.Remove, canEdit)
            }
        };
        AutomationProperties.SetName(menu, "Shortcut actions for " + shortcut.Label);
        menu.AddHandler(KeyDownEvent, (_, args) => DismissShortcutContextMenu(args), RoutingStrategies.Tunnel);
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_shortcutContextMenu, menu)) _shortcutContextMenu = null;
            if (ReferenceEquals(anchor.ContextMenu, menu)) anchor.ContextMenu = null;
        };
        _shortcutContextMenu = anchor.ContextMenu = menu;
        anchor.Focus(fromPointer ? NavigationMethod.Pointer : NavigationMethod.Tab);
        menu.Open(anchor);
        return menu.IsOpen;

        MenuItem Item(string label, ShortcutContextAction action, bool enabled)
        {
            var item = new MenuItem { Header = label, IsEnabled = enabled };
            item.Click += (_, _) => RunShortcutContextAction(id, visit, action);
            return item;
        }
    }

    private bool DismissShortcutContextMenu(KeyEventArgs args)
    {
        if (args.Key != Key.Escape || args.KeyModifiers != KeyModifiers.None || _shortcutContextMenu is not { IsOpen: true } menu)
            return false;
        var id = menu.PlacementTarget is Button anchor ? GetShortcutContextId(anchor) : null;
        menu.Close();
        if (id is not null) FocusShortcutContextAnchor(id);
        args.Handled = true;
        return true;
    }

    private void RunShortcutContextAction(string id, long visit, ShortcutContextAction action)
    {
        if (!IsAlive || _section != "newtab" || visit != _selection || ShortcutSurfaceOpen || _checkingShortcutApp ||
            SavedShortcuts.FirstOrDefault(shortcut => shortcut.Id == id) is not { } shortcut)
            return;
        if (action is ShortcutContextAction.Open or ShortcutContextAction.OpenInNewTab)
        {
            if (!IsShortcutAvailable(shortcut)) return;
            _ = _work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsAlive && _section == "newtab" && _selection == visit);
                await OpenNewTabShortcutAsync(shortcut, original, original.Token, action == ShortcutContextAction.OpenInNewTab);
            }, task => _shortcutContextAction = task);
            return;
        }
        if (!CanOpenNewTabShortcutEditor) return;
        OpenNewTabShortcutEditor(shortcut);
        _shortcutEditorFromMenu = true;
        switch (action)
        {
            case ShortcutContextAction.Icon: OpenShortcutIcons(); break;
            case ShortcutContextAction.Move:
                FocusShortcutIconControl(CanMoveShortcut(id, -1) ? "home-newtab-shortcut-move-up" : "home-newtab-shortcut-move-down");
                break;
            case ShortcutContextAction.Remove:
                FocusShortcutIconControl("home-newtab-shortcut-remove");
                break;
        }
    }
}
