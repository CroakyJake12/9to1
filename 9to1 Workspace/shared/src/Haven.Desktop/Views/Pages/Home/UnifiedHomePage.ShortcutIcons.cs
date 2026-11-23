using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class UnifiedHomePage
{
    private bool _shortcutIconsOpen, _shortcutIconPending;
    private string? _shortcutIconDraft;

    private NewTabShortcutPlacement? EditedShortcut => SavedShortcuts.FirstOrDefault(shortcut => shortcut.Id == _editingShortcutId);
    private static bool CanWriteShortcutIcon(string? key) => key is null || NewTabShortcutPlacement.IsKnownIconKey(key);

    private string CurrentShortcutIcon(NewTabShortcutPlacement shortcut, string? overrideKey)
    {
        if (overrideKey is not null && NewTabShortcutPlacement.IsKnownIconKey(overrideKey)) return "icon:" + overrideKey;
        var destination = ShortcutDestinations.FirstOrDefault(entry => entry.Target == shortcut.Target);
        return ShortcutIcon(shortcut.Target, destination.Icon ?? "globe");
    }

    private void OpenShortcutIcons()
    {
        if (EditedShortcut is not { } shortcut) return;
        _shortcutIconsOpen = true;
        _shortcutIconDraft = shortcut.IconKey;
        _shortcutStatus = null;
        PublishNewTabShortcuts();
        FocusShortcutIconControl("home-newtab-shortcut-icon-default");
    }

    private void SelectShortcutIcon(string? key)
    {
        if (!CanWriteShortcutIcon(key)) return;
        _shortcutIconDraft = key;
        _shortcutStatus = null;
        PublishNewTabShortcuts();
        if (key is null) FocusShortcutIconControl("home-newtab-shortcut-icon-default");
        else FocusAfterSelection(() => this.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
            AutomationProperties.GetAutomationId(button) == "new-tab-shortcut-icon-" + key)?.Focus(NavigationMethod.Tab));
    }

    private void ClearShortcutIconDraft()
    {
        _shortcutIconsOpen = _shortcutIconPending = false;
        _shortcutIconDraft = null;
    }

    private void CloseShortcutIcons(string? status = null)
    {
        ClearShortcutIconDraft();
        _shortcutStatus = status;
        PublishNewTabShortcuts();
        FocusShortcutIconControl("home-newtab-shortcut-icon-open");
    }

    private void FocusShortcutIconControl(string id) => FocusAfterSelection(() => this.GetVisualDescendants()
        .OfType<Button>().FirstOrDefault(button => button.Name == id && button.IsEffectivelyVisible)?.Focus(NavigationMethod.Tab));

    private async Task ApplyShortcutIconAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (EditedShortcut is not { } shortcut || shortcut.IconKey == _shortcutIconDraft || !CanWriteShortcutIcon(_shortcutIconDraft)) return;
        var id = shortcut.Id;
        var key = _shortcutIconDraft;
        _shortcutIconPending = true;
        await ChangeNewTabLayoutAsync(layout =>
        {
            var index = FindShortcutIndex(layout.Shortcuts, id);
            return index >= 0
                ? layout with { Shortcuts = layout.Shortcuts.SetItem(index, layout.Shortcuts[index] with { IconKey = key }) }
                : layout;
        }, original, token);
        if (original.IsAcceptedPublicationCurrent) CompleteShortcutIconChange();
    }

    private void CompleteShortcutIconChange()
    {
        _shortcutIconPending = _retryNewTabLayout is not null;
        if (_shortcutIconPending)
        {
            _shortcutStatus = _newTabEditorStatus;
            PublishNewTabShortcuts();
            FocusShortcutIconControl("home-newtab-shortcut-retry");
            return;
        }
        CloseShortcutIcons(EditedShortcut is null ? "This shortcut was removed in another tab." : null);
    }

    private void PublishShortcutIcons()
    {
        var shortcut = EditedShortcut;
        var canChoose = CanEditShortcuts && _shortcutIconsOpen;
        _railBindings.SetNewTabShortcut("HasIcon", shortcut is not null);
        _railBindings.SetNewTabShortcut("IconOptionsOpen", _shortcutIconsOpen);
        _railBindings.SetNewTabShortcut("CanChooseIcon", canChoose);
        _railBindings.SetNewTabShortcut("IconDefaultSelected", _shortcutIconDraft is null);
        _railBindings.SetNewTabShortcut("CanApplyIcon",
            canChoose && shortcut is not null && shortcut.IconKey != _shortcutIconDraft && CanWriteShortcutIcon(_shortcutIconDraft));
        _railBindings.SetNewTabShortcut("IconPreview", shortcut is null ? null :
            CurrentShortcutIcon(shortcut, _shortcutIconsOpen ? _shortcutIconDraft : shortcut.IconKey));
        _railBindings.SetNewTabShortcut("IconChoices", _shortcutIconsOpen
            ? NewTabShortcutPlacement.IconChoices.Select(choice => new Dictionary<string, object?>
            {
                ["Key"] = choice.Key, ["Label"] = choice.Label, ["Source"] = "icon:" + choice.Key,
                ["AutomationId"] = "new-tab-shortcut-icon-" + choice.Key,
                ["Selected"] = choice.Key == _shortcutIconDraft
            }).ToArray()
            : Array.Empty<Dictionary<string, object?>>());
    }
}
