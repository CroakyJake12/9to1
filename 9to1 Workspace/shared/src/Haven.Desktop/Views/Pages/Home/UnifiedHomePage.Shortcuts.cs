using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class UnifiedHomePage
{
    private const string NewTabShortcutPrefix = "home.newtab-shortcut-";
    private const string ShortcutAutomationPrefix = "new-tab-shortcut-tile-";
    private const string ShortcutEditAutomationPrefix = "new-tab-shortcut-edit-";
    private static readonly (string Target, string Label, string Icon)[] ShortcutDestinations =
    [
        (NewTabShortcutPlacement.Apps, "Apps", "apps"),
        (NewTabShortcutPlacement.Library, "Library", "folder"),
        (NewTabShortcutPlacement.Dashboard, "Dashboard", "dashboard"),
        (NewTabShortcutPlacement.Models, "Models / Discover", "cpu"),
        (NewTabShortcutPlacement.Events, "Events", "calendar"),
        (NewTabShortcutPlacement.Settings, "Settings", "settings")
    ];

    private readonly TextBox _shortcutName = new(), _shortcutAddress = new();
    private string? _editingShortcutId, _shortcutStatus;
    private NewTabShortcutPlacement? _shortcutExpected;
    private Task _shortcutKeyboardAction = Task.CompletedTask;
    private bool _shortcutEditorOpen, _shortcutCloseAfterRetry;
    private int _shortcutColumns = 1;
    private double _shortcutTileHeight = 80;

    private ImmutableArray<NewTabShortcutPlacement> SavedShortcuts =>
        _newTabSavedLayout?.Layout.Shortcuts ?? NewTabShortcutPlacement.Defaults;
    private bool CanEditShortcuts => IsAlive && _section == "newtab" && CanCustomiseNewTab && CanChangeNewTabLayout;
    private bool ShortcutProjectionCanAdd => CanCustomiseNewTab && SavedShortcuts.Length < NewTabShortcutPlacement.MaximumCount;
    private bool ShortcutSurfaceOpen => _shortcutEditorOpen || _shortcutPickerOpen;

    private void RegisterNewTabShortcuts()
    {
        if (_request.Address.Authority != "newtab") return;
        _loader.BindAuthoredControl("home-newtab-shortcut-name", _ => _shortcutName);
        _loader.BindAuthoredControl("home-newtab-shortcut-target", _ => _shortcutAddress);
        AddHandler(KeyDownEvent, OnNewTabShortcutKey, RoutingStrategies.Tunnel);
        SizeChanged += OnShortcutPickerSizeChanged;
        PublishNewTabShortcuts();
    }

    private bool NewTabShortcutActionAvailable(string operation)
    {
        if (_section != "newtab" || _checkingShortcutApp) return false;
        return operation[NewTabShortcutPrefix.Length..] switch
        {
            "open" => !ShortcutSurfaceOpen,
            "add" => CanEditShortcuts && ShortcutProjectionCanAdd && !ShortcutSurfaceOpen && !_widgetPickerOpen,
            "edit" => CanEditShortcuts && IsNewTabCustomising && !ShortcutSurfaceOpen && !_widgetPickerOpen,
            "picker-close" => CanCloseShortcutPicker,
            "picker-custom" or "pick" => _shortcutPickerOpen && CanEditShortcuts && ShortcutProjectionCanAdd,
            "picker-retry" or "picker-revert" => _shortcutPickerOpen && !NewTabLayoutBusy &&
                _retryNewTabLayout is not null && _shortcutPendingPick is not null,
            "retry" or "revert" => _shortcutEditorOpen && !NewTabLayoutBusy && _retryNewTabLayout is not null,
            "cancel" or "save" or "choose" => _shortcutEditorOpen && CanEditShortcuts,
            "move-up" => _shortcutEditorOpen && CanEditShortcuts && CanMoveShortcut(_editingShortcutId, -1),
            "move-down" => _shortcutEditorOpen && CanEditShortcuts && CanMoveShortcut(_editingShortcutId, 1),
            "remove" => _shortcutEditorOpen && CanEditShortcuts && _editingShortcutId is not null,
            _ => false
        };
    }

    private async Task DispatchNewTabShortcutAsync(string operation, object? parameter,
        DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (!NewTabShortcutActionAvailable(operation)) return;
        var id = parameter as string;
        switch (operation[NewTabShortcutPrefix.Length..])
        {
            case "open":
                if (SavedShortcuts.FirstOrDefault(shortcut => shortcut.Id == id) is { } target)
                    await OpenNewTabShortcutAsync(target, original, token);
                return;
            case "add":
                var visit = _selection;
                await RefreshShortcutAppsAsync(original, token);
                if (original.IsAcceptedPublicationCurrent && _selection == visit && NewTabShortcutActionAvailable(operation)) OpenShortcutPicker();
                return;
            case "picker-close": CloseShortcutPicker(); return;
            case "picker-custom":
                HideShortcutPicker();
                _shortcutAddressFromPicker = true;
                OpenNewTabShortcutEditor(null);
                return;
            case "pick": await AddShortcutCandidateAsync(id, original, token); return;
            case "picker-retry":
                await RetryNewTabLayoutAsync(original, token);
                if (original.IsAcceptedPublicationCurrent) CompleteShortcutPick();
                return;
            case "picker-revert":
                _newTabLayoutPreview = null;
                _retryNewTabLayout = null;
                _newTabEditorStatus = _shortcutPickerStatus = null;
                _shortcutPendingPick = null;
                await RefreshNewTabLayoutAsync(original, token);
                if (original.IsAcceptedPublicationCurrent) FocusShortcutPicker("home-newtab-shortcut-picker-search");
                return;
            case "edit":
                if (SavedShortcuts.FirstOrDefault(shortcut => shortcut.Id == id) is { } selected)
                    OpenNewTabShortcutEditor(selected);
                return;
            case "choose":
                var choice = ShortcutDestinations.FirstOrDefault(destination => destination.Target == id);
                if (choice.Target is not null && IsShortcutTargetAvailable(choice.Target))
                {
                    _shortcutAddress.Text = choice.Target;
                    if (string.IsNullOrWhiteSpace(_shortcutName.Text)) _shortcutName.Text = choice.Label;
                    _shortcutStatus = null;
                    PublishNewTabShortcuts();
                }
                return;
            case "cancel": CloseNewTabShortcutEditor(); return;
            case "save": await SaveNewTabShortcutAsync(original, token); return;
            case "remove":
                var removed = _editingShortcutId;
                await ChangeNewTabLayoutAsync(layout => layout with
                {
                    Shortcuts = layout.Shortcuts.RemoveAll(shortcut => shortcut.Id == removed)
                }, original, token);
                if (original.IsAcceptedPublicationCurrent) CompleteNewTabShortcutChange(close: true);
                return;
            case "move-up": case "move-down":
                if (_editingShortcutId is { } edited)
                    await MoveNewTabShortcutAsync(edited, operation.EndsWith("up", StringComparison.Ordinal) ? -1 : 1,
                        original, token, focusTile: false);
                return;
            case "retry":
                await RetryNewTabLayoutAsync(original, token);
                if (original.IsAcceptedPublicationCurrent) CompleteNewTabShortcutChange(_shortcutCloseAfterRetry);
                return;
            case "revert":
                _newTabLayoutPreview = null; _retryNewTabLayout = null; _newTabEditorStatus = null;
                await RefreshNewTabLayoutAsync(original, token);
                if (original.IsAcceptedPublicationCurrent) CloseNewTabShortcutEditor();
                return;
        }
    }

    private async Task OpenNewTabShortcutAsync(NewTabShortcutPlacement shortcut,
        DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (!IsShortcutAvailable(shortcut) || !await IsShortcutAppCurrentAsync(shortcut, original, token)) return;
        var target = AppContentAddress.Parse(shortcut.Target);
        token.ThrowIfCancellationRequested();
        switch (shortcut.Target)
        {
            case NewTabShortcutPlacement.Apps: await SelectAsync("apps", original, token); break;
            case NewTabShortcutPlacement.Events: await SelectAsync("events", original, token); break;
            case NewTabShortcutPlacement.Dashboard: await OpenDashboardAsync(original, token); break;
            default: Queue(target, original, newTab: false); break;
        }
    }

    private bool IsShortcutTargetAvailable(string address) => address switch
    {
        NewTabShortcutPlacement.Library => _createLibrary is not null,
        NewTabShortcutPlacement.Events => _createEvents is not null,
        NewTabShortcutPlacement.Settings => _createSettings is not null,
        _ => true
    };

    private void OpenNewTabShortcutEditor(NewTabShortcutPlacement? shortcut)
    {
        _shortcutEditorOpen = true;
        _editingShortcutId = shortcut?.Id;
        _shortcutName.Text = shortcut?.Label ?? string.Empty;
        _shortcutAddress.Text = shortcut?.Target ?? string.Empty;
        _shortcutStatus = null;
        _shortcutExpected = null;
        _shortcutCloseAfterRetry = false;
        PublishNewTabShortcuts();
        FocusAfterSelection(() => _shortcutName.Focus(NavigationMethod.Tab));
    }

    private void CloseNewTabShortcutEditor(bool returnToPicker = true)
    {
        var previous = _editingShortcutId;
        var resumePicker = returnToPicker && _shortcutAddressFromPicker;
        _shortcutEditorOpen = false;
        _shortcutAddressFromPicker = false;
        _editingShortcutId = _shortcutStatus = null;
        _shortcutExpected = null;
        _shortcutCloseAfterRetry = false;
        if (resumePicker)
        {
            OpenShortcutPicker(resume: true);
            return;
        }
        PublishNewTabShortcuts();
        if (previous is not null && SavedShortcuts.Any(shortcut => shortcut.Id == previous)) FocusNewTabShortcut(previous, edit: true);
        else FocusAddShortcut();
    }

    private async Task SaveNewTabShortcutAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var label = _shortcutName.Text?.Trim() ?? string.Empty;
        if (label.Length is 0 or > NewTabShortcutPlacement.MaximumLabelLength || label.Any(char.IsControl))
        {
            ShowShortcutValidation("Enter a shortcut name.", _shortcutName);
            return;
        }
        var input = _shortcutAddress.Text?.Trim() ?? string.Empty;
        var app = _shortcutApps.GetValueOrDefault(input);
        var isStandardTarget = NewTabShortcutPlacement.TryParseTarget(input, out var target) && IsShortcutTargetAvailable(target!.Value);
        if (!isStandardTarget && app is null)
        {
            ShowShortcutValidation("Select an available destination or enter a complete http or https address.", _shortcutAddress);
            return;
        }
        var address = isStandardTarget ? target!.Value : input;
        if (SavedShortcuts.Any(shortcut => shortcut.Id != _editingShortcutId && shortcut.Target == address))
        {
            ShowShortcutValidation("This destination is already in Shortcuts.", _shortcutAddress);
            return;
        }
        var editing = _editingShortcutId;
        var previous = SavedShortcuts.FirstOrDefault(shortcut => shortcut.Id == editing);
        var item = new NewTabShortcutPlacement(editing ?? Guid.NewGuid().ToString("N"), label, address)
        { AppId = previous?.Target == address ? previous.AppId : app?.Id };
        if (!await IsShortcutAppCurrentAsync(item, original, token))
        {
            if (original.IsAcceptedPublicationCurrent) ShowShortcutValidation("This app is no longer available. Choose a different destination.", _shortcutAddress);
            return;
        }
        _shortcutExpected = item;
        await SaveNewTabShortcutPlacementAsync(item, editing, original, token);
        if (original.IsAcceptedPublicationCurrent) CompleteNewTabShortcutChange(close: true);
    }

    private Task SaveNewTabShortcutPlacementAsync(NewTabShortcutPlacement item, string? editing,
        DesktopOriginalWorkLifetime.Original original, CancellationToken token) =>
        ChangeNewTabLayoutAsync(layout =>
        {
            var index = FindShortcutIndex(layout.Shortcuts, editing);
            var duplicate = layout.Shortcuts.Any(shortcut => shortcut.Id != item.Id && shortcut.Target == item.Target);
            if (duplicate) return layout;
            if (index >= 0) return layout with { Shortcuts = layout.Shortcuts.SetItem(index, item) };
            return editing is null && layout.Shortcuts.Length < NewTabShortcutPlacement.MaximumCount
                ? layout with { Shortcuts = layout.Shortcuts.Add(item) }
                : layout;
        }, original, token);

    private void ShowShortcutValidation(string message, TextBox input)
    {
        _shortcutStatus = message;
        PublishNewTabShortcuts();
        input.Focus(NavigationMethod.Tab);
    }

    private void CompleteNewTabShortcutChange(bool close)
    {
        _shortcutCloseAfterRetry = close && _retryNewTabLayout is not null;
        _shortcutStatus = _retryNewTabLayout is null ? null : _newTabEditorStatus;
        if (close && _retryNewTabLayout is null)
        {
            if (_shortcutExpected is { } expected && !SavedShortcuts.Contains(expected))
                ShowShortcutValidation("Shortcuts changed in another tab. Review the destination and try again.", _shortcutAddress);
            else CloseNewTabShortcutEditor(returnToPicker: false);
        }
        else PublishNewTabShortcuts();
    }

    private async Task MoveNewTabShortcutAsync(string id, int direction, DesktopOriginalWorkLifetime.Original original,
        CancellationToken token, bool focusTile)
    {
        if (!CanMoveShortcut(id, direction)) return;
        await ChangeNewTabLayoutAsync(layout =>
        {
            var index = FindShortcutIndex(layout.Shortcuts, id);
            var neighbor = index + direction;
            if (index < 0 || neighbor < 0 || neighbor >= layout.Shortcuts.Length) return layout;
            return layout with { Shortcuts = layout.Shortcuts.SetItem(index, layout.Shortcuts[neighbor]).SetItem(neighbor, layout.Shortcuts[index]) };
        }, original, token);
        if (!original.IsAcceptedPublicationCurrent) return;
        if (_retryNewTabLayout is not null && !_shortcutEditorOpen)
        {
            _shortcutEditorOpen = true;
            _editingShortcutId = id;
            var shortcut = SavedShortcuts.FirstOrDefault(candidate => candidate.Id == id);
            _shortcutName.Text = shortcut?.Label ?? string.Empty;
            _shortcutAddress.Text = shortcut?.Target ?? string.Empty;
        }
        CompleteNewTabShortcutChange(close: false);
        if (_retryNewTabLayout is not null)
            FocusAfterSelection(() => this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Name == "home-newtab-shortcut-retry")?.Focus(NavigationMethod.Tab));
        else if (focusTile) FocusNewTabShortcut(id);
        else if (!CanMoveShortcut(id, direction))
            FocusAfterSelection(() => this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Name == (direction < 0 ? "home-newtab-shortcut-move-down" : "home-newtab-shortcut-move-up"))
                ?.Focus(NavigationMethod.Tab));
    }

    private static int FindShortcutIndex(ImmutableArray<NewTabShortcutPlacement> shortcuts, string? id)
    {
        for (var index = 0; index < shortcuts.Length; index++)
            if (shortcuts[index].Id == id) return index;
        return -1;
    }

    private bool CanMoveShortcut(string? id, int direction)
    {
        var index = FindShortcutIndex(SavedShortcuts, id);
        return index >= 0 && index + direction >= 0 && index + direction < SavedShortcuts.Length;
    }

    private void OnNewTabShortcutKey(object? sender, KeyEventArgs args)
    {
        if (_section != "newtab" || args.Handled) return;
        if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None && ShortcutSurfaceOpen)
        {
            args.Handled = true;
            if (CanCloseShortcutPicker) CloseShortcutPicker();
            else if (!_shortcutPickerOpen && CanEditShortcuts) CloseNewTabShortcutEditor();
            return;
        }
        if (!IsNewTabCustomising || !CanEditShortcuts || ShortcutSurfaceOpen || args.KeyModifiers != KeyModifiers.Alt ||
            args.Key is not (Key.Up or Key.Down) || args.Source is not Button button ||
            AutomationProperties.GetAutomationId(button) is not { } automation ||
            !automation.StartsWith(ShortcutAutomationPrefix, StringComparison.Ordinal)) return;
        args.Handled = true;
        var direction = args.Key == Key.Up ? -1 : 1;
        _ = _work.RunAsync(original => MoveNewTabShortcutAsync(automation[ShortcutAutomationPrefix.Length..],
            direction, original, original.Token, focusTile: true), task => _shortcutKeyboardAction = task);
    }

    private void FocusNewTabShortcut(string id, bool edit = false) => FocusAfterSelection(() => this.GetVisualDescendants()
        .OfType<Button>().FirstOrDefault(button => AutomationProperties.GetAutomationId(button) ==
            (edit && IsNewTabCustomising ? ShortcutEditAutomationPrefix : ShortcutAutomationPrefix) + id)
        ?.Focus(NavigationMethod.Tab));

    private void UpdateNewTabShortcutWidth(double width)
    {
        _shortcutColumns = width >= 660 ? 3 : width >= 312 ? 2 : 1;
        _shortcutTileHeight = _shortcutColumns == 3 ? 94 : 80;
        PublishNewTabShortcuts();
    }

    private void PublishNewTabShortcuts()
    {
        Set(_newTabCustomise, "IsEnabled", !ShortcutSurfaceOpen);
        Set(_newTabCustomiseDone, "IsEnabled", !NewTabLayoutBusy && _retryNewTabLayout is null && !ShortcutSurfaceOpen);
        Set(_newTabCustomiseAddWidget, "IsEnabled", CanEditWidgetLayout && !ShortcutSurfaceOpen);
        var shortcuts = SavedShortcuts;
        var columns = _shortcutColumns == 3 && ShortcutProjectionCanAdd ? 4 : _shortcutColumns;
        var count = shortcuts.Length + (ShortcutProjectionCanAdd ? 1 : 0);
        var rows = Math.Max(1, (count + columns - 1) / columns);
        var editorTitle = _editingShortcutId is { } id && shortcuts.FirstOrDefault(shortcut => shortcut.Id == id) is { } edited
            ? "Edit " + edited.Label : "Add shortcut";
        var targets = ShortcutDestinations.Where(destination => IsShortcutTargetAvailable(destination.Target)).Select(destination =>
            new Dictionary<string, object?> { ["Id"] = destination.Target, ["Label"] = destination.Label, ["ChoiceLabel"] = "Use " + destination.Label }).ToArray();
        var tiles = shortcuts.Select((shortcut, index) =>
        {
            var destination = ShortcutDestinations.FirstOrDefault(destination => destination.Target == shortcut.Target);
            return new Dictionary<string, object?>
            {
                ["Id"] = shortcut.Id, ["Label"] = IsShortcutAvailable(shortcut) ? shortcut.Label : shortcut.Label + " (unavailable)", ["EditLabel"] = "Edit " + shortcut.Label,
                ["AutomationId"] = ShortcutAutomationPrefix + shortcut.Id, ["EditAutomationId"] = ShortcutEditAutomationPrefix + shortcut.Id,
                ["Icon"] = ShortcutIcon(shortcut.Target, destination.Icon ?? "link"),
                ["CanOpen"] = !ShortcutSurfaceOpen && IsShortcutAvailable(shortcut),
                ["HelpText"] = IsShortcutAvailable(shortcut) ? shortcut.Label : "This destination is unavailable. Use Customise to replace or remove the shortcut.",
                ["Row"] = index / columns, ["Column"] = index % columns
            };
        }).ToArray();
        _railBindings.SetNewTabShortcut("Tiles", tiles);
        _railBindings.SetNewTabShortcut("Targets", targets);
        _railBindings.SetNewTabShortcut("Rows", string.Join(' ', Enumerable.Repeat("Auto", rows)));
        _railBindings.SetNewTabShortcut("Columns", string.Join(' ', Enumerable.Repeat("1fr", columns)));
        _railBindings.SetNewTabShortcut("AddRow", shortcuts.Length / columns);
        _railBindings.SetNewTabShortcut("AddColumn", shortcuts.Length % columns);
        _railBindings.SetNewTabShortcut("TileHeight", _shortcutTileHeight);
        _railBindings.SetNewTabShortcut("CanAdd", ShortcutProjectionCanAdd);
        _railBindings.SetNewTabShortcut("CanEdit", CanEditShortcuts);
        _railBindings.SetNewTabShortcut("CanOpenEditor", CanEditShortcuts && !ShortcutSurfaceOpen && !_widgetPickerOpen);
        _railBindings.SetNewTabShortcut("Editing", IsNewTabCustomising);
        _railBindings.SetNewTabShortcut("EditorOpen", _shortcutEditorOpen);
        _railBindings.SetNewTabShortcut("Existing", _editingShortcutId is not null);
        _railBindings.SetNewTabShortcut("EditorTitle", editorTitle);
        _railBindings.SetNewTabShortcut("CanMoveUp", CanEditShortcuts && CanMoveShortcut(_editingShortcutId, -1));
        _railBindings.SetNewTabShortcut("CanMoveDown", CanEditShortcuts && CanMoveShortcut(_editingShortcutId, 1));
        _railBindings.SetNewTabShortcut("CanRetry", _shortcutEditorOpen && _retryNewTabLayout is not null && !NewTabLayoutBusy);
        _railBindings.SetNewTabShortcut("Status", _shortcutStatus ?? string.Empty);
        _railBindings.SetNewTabShortcut("HasStatus", !string.IsNullOrEmpty(_shortcutStatus));
        PublishShortcutPicker();
    }
}
