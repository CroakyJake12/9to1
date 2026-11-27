using Haven.Core;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Prefabs;

internal sealed partial class ChatAddMenuSurface
{
    private sealed record AttachCategory(AddMenu.AddMenuAction Action, HavenButton Header,
        Container Section, Icon Disclosure);

    private AttachCategory[] _attachCategoryRows = [];
    private Container? _attachCategories;
    private bool _attachCompact;
    private double _attachMaximumHeight = 470;

    // Go retains its response-management menu. Only the genuine Chat constructor opts into
    // inline Attach categories; mention search still belongs to the original composer input.
    private bool UsesAttachCategories => _composerOwnsSearch && !_showThreadSettings;

    private void InitializeAttachCategories()
    {
        _attachCategories = Prefab.GetComponent<Container>("AttachCategories");
        _attachCategoryRows =
        [
            Category(AddMenu.AddMenuAction.File, AttachFilesButton, "AttachFiles", "Files"),
            Category(AddMenu.AddMenuAction.Instruction, InstructionsButton, "AttachSkills", "Skills"),
            Category(AddMenu.AddMenuAction.Capability, CapabilitiesButton, "AttachApps", "Plugins & Apps"),
            Category(AddMenu.AddMenuAction.Agent, AgentsButton, "AttachSpecialists", "Specialists"),
            Category(AddMenu.AddMenuAction.Assistant, AssistantsButton, "AttachAssistants", "Assistants")
        ];
        MenuTitle.Content = "Attach to Chat";
        MenuTitle.SetValue(HavenProperties.FontSize, 16d);
        MenuTitle.SetValue(HavenProperties.FontWeight, 800);
        MenuTitle.SetValue(HavenProperties.Foreground, "TextPrimary");
        var header = Prefab.GetComponent<Container>("MenuHeader");
        header.Layout = HavenLayout.Grid;
        header.Columns = "1fr Auto";
        header.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(0)));
        header.SetValue(HavenProperties.Height, HavenLength.Px(36));
        DismissButton.SetValue(HavenProperties.Column, 1);
        DismissButton.SetValue(HavenProperties.Width, HavenLength.Px(36));
        DismissButton.SetValue(HavenProperties.Height, HavenLength.Px(36));
        DismissButton.Accessibility.AccessibleName = "Close Attach menu";
        DismissButton.Accessibility.Description = "Close Attach and return to the chat draft.";
        Overlay.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        CatalogSearch.SetValue(HavenProperties.Height, HavenLength.Px(34));
        CatalogSearch.SetValue(HavenProperties.FontSize, 12d);
        CatalogSearch.SetValue(HavenProperties.FontWeight, 600);
        UpdateAttachViewport(820, 470, compact: false);

        AttachCategory Category(AddMenu.AddMenuAction action, HavenButton button, string stem, string label)
        {
            var section = Prefab.GetComponent<Container>(stem + "Section");
            var slot = Prefab.GetComponent<Container>(stem + "Header");
            var disclosure = Prefab.GetComponent<Icon>(stem + "Disclosure");
            // Reuse the original authored action control and ID. There is only one copy of
            // each action and one shared catalogue/search owner, including after reopening.
            MoveTo(button, slot);
            disclosure.SetValue(HavenProperties.ZIndex, 1);
            button.SetValue(HavenProperties.Column, 0);
            button.SetValue(HavenProperties.ColumnSpan, 2);
            button.SetValue(HavenProperties.FontSize, 13d);
            button.SetValue(HavenProperties.FontWeight, 700);
            button.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 30px 8px 7px"));
            button.Content = label;
            button.Accessibility.AccessibleName = label;
            return new(action, button, section, disclosure);
        }
    }

    internal void UpdateAttachViewport(double width, double maximumHeight, bool compact)
    {
        if (!UsesAttachCategories || _attachCategories is null) return;
        _attachCompact = compact;
        _attachMaximumHeight = Math.Max(80, double.IsFinite(maximumHeight) ? maximumHeight : 470);
        Overlay.SetValue(HavenProperties.Width, HavenLength.Px(Math.Max(80, double.IsFinite(width) ? width : 820)));
        Overlay.SetValue(HavenProperties.MaxHeight, HavenLength.Px(_attachMaximumHeight));
        Overlay.SetValue(HavenProperties.Padding, HavenThickness.Parse(compact ? "10px 12px" : "12px 14px"));
        Overlay.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(compact ? 20 : 22)));
        var bodyHeight = Math.Max(16, _attachMaximumHeight - 36 - 8 - (compact ? 20 : 24));
        _attachCategories.SetValue(HavenProperties.MaxHeight, HavenLength.Px(bodyHeight));
        CatalogPanel.SetValue(HavenProperties.Padding,
            HavenThickness.Parse(_searchAll ? "0px" : compact ? "3px 7px 8px 28px" : "3px 7px 8px 37px"));
        if (_searchAll) CatalogResults.SetValue(HavenProperties.MaxHeight, HavenLength.Px(bodyHeight));
        foreach (var category in _attachCategoryRows)
            category.Header.SetValue(HavenProperties.MinHeight, HavenLength.Px(compact ? 44 : 36));
        foreach (var button in CatalogResults.DescendantsAndSelf().OfType<HavenButton>())
            ConfigureAttachResultButton(button);
    }

    private void ShowAttachCategories()
    {
        if (_attachCategories is null) return;
        _catalogAction = null;
        _searchAll = false;
        _lastSearch = string.Empty;
        CatalogSearch.Text = string.Empty;
        _attachCategories.ScrollY = 0;
        _attachCategories.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        MainMenu.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        CatalogPanel.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        CatalogBackButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        CatalogSearch.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        RefreshAttachDisclosures();
    }

    private void CloseAttachCategories()
    {
        _attachCategories?.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        RefreshAttachDisclosures();
    }

    private void ShowMentionCatalogue()
    {
        _attachCategories?.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        MoveTo(CatalogPanel, Overlay);
        CatalogPanel.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(0)));
        CatalogResults.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        CatalogResults.SetValue(HavenProperties.MaxHeight,
            HavenLength.Px(Math.Max(16, _attachMaximumHeight - 36 - 8 - (_attachCompact ? 20 : 24))));
        RefreshAttachDisclosures();
    }

    private static bool IsAttachCategory(AddMenu.AddMenuAction action) => action is
        AddMenu.AddMenuAction.File or AddMenu.AddMenuAction.Instruction or AddMenu.AddMenuAction.Capability
        or AddMenu.AddMenuAction.App or AddMenu.AddMenuAction.Agent or AddMenu.AddMenuAction.Assistant;

    private void ToggleAttachCategory(AddMenu.AddMenuAction action)
    {
        if (!_isOpen || _attachCategories is null) return;
        if (action == AddMenu.AddMenuAction.App) action = AddMenu.AddMenuAction.Capability;
        var next = _catalogAction == action && !_searchAll ? (AddMenu.AddMenuAction?)null : action;
        _catalogAction = next;
        _searchAll = false;
        _externalQuery = string.Empty;
        _lastSearch = string.Empty;
        CatalogSearch.Text = string.Empty;
        MainMenu.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _attachCategories.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        CatalogBackButton.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        CatalogPanel.SetValue(HavenProperties.Visibility, next is null ? HavenVisibility.Collapsed : HavenVisibility.Visible);
        RefreshAttachDisclosures();
        if (next is null) return;

        var category = _attachCategoryRows.Single(item => item.Action == next);
        MoveTo(CatalogPanel, category.Section);
        CatalogPanel.SetValue(HavenProperties.Padding,
            HavenThickness.Parse(_attachCompact ? "3px 7px 8px 28px" : "3px 7px 8px 37px"));
        CatalogResults.SetValue(HavenProperties.Overflow, HavenOverflow.Visible);
        CatalogResults.SetValue(HavenProperties.MaxHeight, HavenLength.Auto);
        CatalogSearch.Placeholder = next == AddMenu.AddMenuAction.File
            ? "Search file sources" : "Search " + category.Header.Content;
        CatalogSearch.Accessibility.AccessibleName = CatalogSearch.Placeholder;
        CatalogSearch.SetValue(HavenProperties.Visibility, HavenVisibility.Visible);
        RebuildCatalogue();
    }

    private void RefreshAttachDisclosures()
    {
        foreach (var category in _attachCategoryRows)
        {
            var expanded = _isOpen && !_searchAll && _catalogAction == category.Action;
            category.Header.SetState(HavenElementState.Expanded, expanded);
            category.Disclosure.Key = expanded ? "chevron-down" : "chevron-right";
            var purpose = category.Action == AddMenu.AddMenuAction.Assistant
                ? "Browse configured Assistants; choosing one opens its workspace."
                : category.Action == AddMenu.AddMenuAction.File
                    ? "Choose files from this device."
                    : "Browse context for this chat. Browsing does not run tools or change permissions.";
            category.Header.Accessibility.Description = (expanded ? "Expanded. " : "Collapsed. ") + purpose;
        }
    }

    internal bool TryGetAdjacentAttachCategory(HavenKey key, out HavenButton? target)
    {
        target = null;
        if (!UsesAttachCategories || !_isOpen || _searchAll
            || key is not (HavenKey.Up or HavenKey.Down or HavenKey.Home or HavenKey.End)) return false;
        var current = Array.FindIndex(_attachCategoryRows, item => item.Header.State.HasFlag(HavenElementState.Focused));
        if (current < 0) return false; // Arrow keys in the search input keep their normal caret semantics.
        var next = key switch
        {
            HavenKey.Home => 0,
            HavenKey.End => _attachCategoryRows.Length - 1,
            HavenKey.Up => (current + _attachCategoryRows.Length - 1) % _attachCategoryRows.Length,
            _ => (current + 1) % _attachCategoryRows.Length
        };
        target = _attachCategoryRows[next].Header;
        return true;
    }

    internal void RevealFocusedAttachControl()
    {
        if (!UsesAttachCategories || !_isOpen || _attachCategories is null || _searchAll) return;
        var focused = _attachCategories.DescendantsAndSelf().FirstOrDefault(item => item.State.HasFlag(HavenElementState.Focused));
        if (focused is null) return;
        var viewport = _attachCategories.Bounds;
        if (focused.Bounds.Y < viewport.Y) _attachCategories.ScrollBy(0, focused.Bounds.Y - viewport.Y);
        else if (focused.Bounds.Y + focused.Bounds.Height > viewport.Y + viewport.Height)
            _attachCategories.ScrollBy(0, focused.Bounds.Y + focused.Bounds.Height - viewport.Y - viewport.Height);
    }

    private void AddFileSourceRows(string query)
    {
        if (!Matches("Choose files…", "Attach files from this device", query)) return;
        var button = MenuButton("Choose files…", 260, "file");
        button.Accessibility.Description = "Open the existing device file chooser. Selecting files uses this chat's attachment owner.";
        button.Invoked += OnChooseFilesInvoked;
        AddResultRow(button, AddMenu.AddMenuAction.File, null);
    }

    private void AddResultRow(HavenButton button, AddMenu.AddMenuAction action, object? item)
    {
        if (!UsesAttachCategories) { CatalogResults.Add(button); return; }
        ConfigureAttachResultButton(button);
        var row = new Container { Layout = HavenLayout.Grid, Columns = "1fr 24px", Rows = "Auto Auto" };
        row.SetValue(HavenProperties.Width, HavenLength.Parse("100%"));
        button.SetValue(HavenProperties.ColumnSpan, 2);
        row.Add(button);
        var mark = new Icon { Key = action is AddMenu.AddMenuAction.Assistant or AddMenu.AddMenuAction.File ? "chevron-right" : "plus" };
        mark.SetValue(HavenProperties.Column, 1);
        mark.SetValue(HavenProperties.Width, HavenLength.Px(14));
        mark.SetValue(HavenProperties.Height, HavenLength.Px(14));
        mark.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
        mark.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None);
        mark.SetValue(HavenProperties.Foreground, "TextSecondary");
        row.Add(mark);
        if (item is CapabilityDefinition { Availability: not CapabilityAvailability.Available } capability)
        {
            var status = new HavenText(ReadinessText(capability.Availability)) { Level = TextLevel.Caption };
            status.SetValue(HavenProperties.Row, 1);
            status.SetValue(HavenProperties.ColumnSpan, 2);
            status.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 9px 6px 39px"));
            status.SetValue(HavenProperties.FontSize, 11d);
            status.SetValue(HavenProperties.Foreground, "TextSecondary");
            row.Add(status);
        }
        CatalogResults.Add(row);
    }

    private void ConfigureAttachResultButton(HavenButton button)
    {
        button.SetValue(HavenProperties.Width, HavenLength.Parse("100%"));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(_attachCompact ? 40 : 32));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("7px 30px 7px 9px"));
        button.SetValue(HavenProperties.FontWeight, 600);
    }

    private static string ContextDescription(object item, string? description)
    {
        var readiness = item is CapabilityDefinition capability ? ReadinessText(capability.Availability) + ". " : string.Empty;
        return (string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim() + " ")
            + readiness + "Attach as chat context; permissions are unchanged and no tool runs here.";
    }

    private static string ReadinessText(CapabilityAvailability availability) => availability switch
    {
        CapabilityAvailability.Available => "Available",
        CapabilityAvailability.PermissionRequired => "Needs permission",
        CapabilityAvailability.DependencyRequired => "Needs dependency",
        CapabilityAvailability.Restricted => "Restricted",
        CapabilityAvailability.Unsupported => "Unsupported",
        _ => "Readiness unavailable"
    };

    private static void MoveTo(HavenElement child, Container parent)
    {
        if (ReferenceEquals(child.Parent, parent)) return;
        child.Parent?.Remove(child);
        parent.Add(child);
    }
}
