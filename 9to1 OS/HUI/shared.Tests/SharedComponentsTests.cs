using Haven.UI;
using Haven.UI.Components;
using Xunit;

namespace CakeOS.Hui.SharedComponents.Tests;

/// <summary>
/// Focused API + semantics tests for the generic shared kit (LAGUNA WORKER 4).
/// Keyboard activation reuses the public Button KeyDown/KeyUp path, mirroring
/// how HavenInputRouter drives donors.
/// </summary>
public sealed class SharedComponentsTests
{
    private static readonly HavenKeyInput Enter = new(HavenKey.Enter, HavenKeyModifiers.None);

    private static void Activate(Button button)
    {
        Assert.True(button.KeyDown(Enter));
        Assert.True(button.KeyUp(Enter));
    }

    // ---- HeaderBar ----

    [Fact]
    public void HeaderBar_title_subtitle_icon_actions_sync()
    {
        var header = new HeaderBar { Title = "Canvas", Subtitle = "Rnote sheet", IconKey = "brush" };
        Assert.Equal("Canvas", header.TitleText.Content);
        Assert.Equal("Rnote sheet", header.SubtitleText.Content);
        Assert.Equal("brush", header.LeadingIcon.Key);
        Assert.Equal("Canvas", header.Accessibility.AccessibleName);
        Assert.Equal(HavenVisibility.Visible, header.SubtitleText.GetValue(HavenProperties.Visibility));
        Assert.Equal(HavenVisibility.Visible, header.LeadingIcon.GetValue(HavenProperties.Visibility));

        var action = new Button { Content = "Undo" };
        header.AddAction(action);
        Assert.Contains(action, header.Actions.Children);

        header.Subtitle = "";
        Assert.Equal(HavenVisibility.Collapsed, header.SubtitleText.GetValue(HavenProperties.Visibility));
        header.IconKey = "";
        Assert.Equal(HavenVisibility.Collapsed, header.LeadingIcon.GetValue(HavenProperties.Visibility));
        header.ShowDivider = false;
        Assert.Equal(HavenVisibility.Collapsed, header.Divider.GetValue(HavenProperties.Visibility));
    }

    // ---- Toolbar ----

    [Fact]
    public void Toolbar_add_buttons_separators_and_orientation()
    {
        var toolbar = new Toolbar();
        Assert.Equal(HavenAccessibleRole.Group, toolbar.Accessibility.Role);
        var save = new Button { Content = "Save" };
        toolbar.AddButton(save);
        Assert.Contains(save, toolbar.Children);
        var separator = toolbar.AddSeparator();
        Assert.Equal(SeparatorOrientation.Vertical, separator.Orientation);
        Assert.Contains(separator, toolbar.Children);

        toolbar.Orientation = ToolbarOrientation.Vertical;
        Assert.Equal(HavenLayout.Vertical, toolbar.Layout);
        var separator2 = toolbar.AddSeparator();
        Assert.Equal(SeparatorOrientation.Horizontal, separator2.Orientation);
    }

    // ---- ActionGroup / ToggleButton / SplitButton ----

    [Fact]
    public void ActionGroup_hosts_compact_actions()
    {
        var group = new ActionGroup { GroupLabel = "Pen tools" };
        var pen = new Button { Content = "Pen" };
        group.AddAction(pen);
        Assert.Contains(pen, group.Children);
        Assert.Equal("Pen tools", group.Accessibility.AccessibleName);
    }

    [Fact]
    public void ToggleButton_keyboard_activation_toggles_checked_semantics()
    {
        var toggle = new ToggleButton { Content = "Bold" };
        var changed = 0;
        toggle.CheckedChanged += (_, _) => changed++;
        Assert.False(toggle.IsChecked);

        Activate(toggle.Inner);
        Assert.True(toggle.IsChecked);
        Assert.True(toggle.Accessibility.Checked);
        Assert.True(toggle.Inner.Accessibility.Checked);
        Assert.True(toggle.Inner.State.HasFlag(HavenElementState.Checked));
        Assert.Equal(1, changed);

        Activate(toggle.Inner);
        Assert.False(toggle.IsChecked);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void ToggleButton_toggle_method_syncs_state()
    {
        var toggle = new ToggleButton();
        toggle.Toggle();
        Assert.True(toggle.IsChecked);
        toggle.IsChecked = true;
        Assert.True(toggle.IsChecked);
    }

    [Fact]
    public void SplitButton_menu_request_and_show_menu_overlay()
    {
        var root = new Page();
        var split = new SplitButton { Text = "Export" };
        Assert.Equal("Export", split.Primary.Content);

        var requested = 0;
        split.MenuRequested += (_, _) => requested++;
        Activate(split.Chevron);
        Assert.Equal(1, requested);

        var menu = split.ShowMenu(root, [new PopupMenuItem("PNG", () => { })]);
        Assert.Contains(menu, root.Children);
        Assert.Equal(HavenLayoutParticipation.Overlay, menu.GetValue(HavenProperties.LayoutParticipation));
        menu.Dismiss();
        Assert.DoesNotContain(menu, root.Children);
    }

    // ---- Popover ----

    [Fact]
    public void Popover_show_dismiss_content_and_light_dismiss()
    {
        var root = new Page();
        var anchor = new Button { Content = "Anchor" };
        root.Add(anchor);
        var popover = new Popover(anchor, root, "Brush options");
        popover.SetContent(new Text { Content = "Size" });
        Assert.Single(popover.Content.Children);

        var dismissed = 0;
        popover.Dismissed += (_, _) => dismissed++;
        popover.ShowIn(root);
        Assert.Contains(popover, root.Children);
        popover.Dismiss();
        Assert.DoesNotContain(popover, root.Children);
        Assert.Equal(1, dismissed);

        Assert.True(popover.LightDismiss);
        popover.LightDismiss = false;
        Assert.False(popover.LightDismiss);
    }

    // ---- Dialog ----

    [Fact]
    public void Dialog_show_close_escape_and_accessibility()
    {
        var root = new Page();
        var dialog = new Dialog("Delete group?");
        dialog.SetContent(new Text { Content = "This cannot be undone." });
        var ok = new Button { Content = "Delete" };
        dialog.AddAction(ok);
        Assert.Equal(HavenAccessibleRole.Dialog, dialog.Accessibility.Role);
        Assert.Equal("Delete group?", dialog.Accessibility.AccessibleName);

        var requested = 0;
        var closed = 0;
        dialog.RequestCloseInvoked += (_, _) => requested++;
        dialog.Closed += (_, _) => closed++;

        dialog.ShowIn(root);
        Assert.Contains(dialog, root.Children);
        Assert.True(dialog.KeyDown(new HavenKeyInput(HavenKey.Escape, HavenKeyModifiers.None)));
        Assert.Equal(1, requested);
        Assert.Contains(dialog, root.Children);

        dialog.Close();
        Assert.DoesNotContain(dialog, root.Children);
        Assert.Equal(1, closed);

        dialog.IsDismissable = false;
        Assert.True(dialog.KeyDown(new HavenKeyInput(HavenKey.Escape, HavenKeyModifiers.None)));
        Assert.Equal(1, requested);
    }

    // ---- Tooltip ----

    [Fact]
    public void Tooltip_attach_sets_description_and_detaches()
    {
        var root = new Page();
        var anchor = new Button { Content = "Pen" };
        root.Add(anchor);
        var tooltip = Tooltip.Attach(anchor, root, "Freehand pen");
        Assert.Equal("Freehand pen", anchor.Accessibility.Description);
        Assert.Contains(tooltip, root.Children);
        Assert.Equal(HavenVisibility.Collapsed, tooltip.GetValue(HavenProperties.Visibility));
        tooltip.Detach();
        Assert.DoesNotContain(tooltip, root.Children);
    }

    // ---- Menu / MenuButton ----

    [Fact]
    public void Menu_keyboard_navigation_skips_disabled_and_activates()
    {
        var menu = new Menu();
        menu.SetItems([
            new HavenMenuItem("cut", "Cut"),
            new HavenMenuItem("copy", "Copy", Enabled: false),
            new HavenMenuItem("paste", "Paste")
        ]);
        Assert.Equal(3, menu.ItemButtons.Count);
        Assert.Equal(HavenAccessibleRole.Menu, menu.Accessibility.Role);
        Assert.Equal(HavenAccessibleRole.MenuItem, menu.ItemButtons[0].Accessibility.Role);

        string? invoked = null;
        menu.ItemInvoked += (_, key) => invoked = key;

        Assert.True(menu.KeyDown(new HavenKeyInput(HavenKey.Down, HavenKeyModifiers.None)));
        Assert.Equal(0, menu.SelectedIndex);
        Assert.True(menu.KeyDown(new HavenKeyInput(HavenKey.Down, HavenKeyModifiers.None)));
        Assert.Equal(2, menu.SelectedIndex);
        Assert.True(menu.KeyDown(new HavenKeyInput(HavenKey.Up, HavenKeyModifiers.None)));
        Assert.Equal(0, menu.SelectedIndex);
        Assert.True(menu.KeyDown(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
        Assert.Equal("cut", invoked);
        Assert.True(menu.ItemButtons[0].Accessibility.Selected);
    }

    [Fact]
    public void Menu_pointer_activation_reports_key_and_rejects_disabled()
    {
        var menu = new Menu();
        menu.SetItems([new HavenMenuItem("ok", "OK"), new HavenMenuItem("no", "No", Enabled: false)]);
        string? invoked = null;
        menu.ItemInvoked += (_, key) => invoked = key;
        Activate(menu.ItemButtons[0]);
        Assert.Equal("ok", invoked);
        Assert.False(menu.ActivateSelected() && invoked != "ok");
        menu.SelectedIndex = 1;
        Assert.False(menu.ActivateSelected());
    }

    [Fact]
    public void MenuButton_open_menu_adds_overlay_and_reports_request()
    {
        var root = new Page();
        var button = new MenuButton();
        button.MenuItems = [new PopupMenuItem("Rename", () => { })];
        var requested = 0;
        button.MenuRequested += (_, _) => requested++;
        Activate(button.Inner);
        Assert.Equal(1, requested);
        var menu = button.OpenMenu(root);
        Assert.Contains(menu, root.Children);
        menu.Dismiss();
    }

    // ---- Colour ----

    [Fact]
    public void HavenColour_parse_format_roundtrip()
    {
        Assert.True(HavenColour.TryParse("#E81123", out var red));
        Assert.Equal("#E81123", red.ToHex());
        Assert.Equal(new HavenColour(0xE8, 0x11, 0x23), red);
        Assert.True(HavenColour.TryParse("2563eb", out var blue));
        Assert.Equal("#2563EB", blue.ToHex());
        Assert.False(HavenColour.TryParse(null, out _));
        Assert.False(HavenColour.TryParse("", out _));
        Assert.False(HavenColour.TryParse("#XYZ", out _));
        Assert.False(HavenColour.TryParse("#12345", out _));
        Assert.Throws<FormatException>(() => HavenColour.FromHex("not-a-colour"));
        Assert.NotEmpty(HavenColour.DefaultPalette);
    }

    [Fact]
    public void ColourPicker_selection_swatches_custom_hex_and_keyboard()
    {
        var picker = new ColourPicker();
        Assert.NotEmpty(picker.SwatchButtons);
        var changed = 0;
        picker.SelectedChanged += (_, _) => changed++;

        picker.SelectedHex = "#2563EB";
        Assert.Equal("#2563EB", picker.SelectedHex);
        Assert.Equal(new HavenColour(0x25, 0x63, 0xEB), picker.SelectedColour);
        Assert.Equal(1, changed);

        picker.CustomField.Text = "#E81123";
        Assert.Equal("#E81123", picker.SelectedHex);

        picker.CustomField.Text = "not-a-colour";
        Assert.Equal("#E81123", picker.SelectedHex);

        Assert.True(picker.KeyDown(new HavenKeyInput(HavenKey.Right, HavenKeyModifiers.None)));
        Assert.NotEqual("#E81123", picker.SelectedHex);
        Assert.True(picker.KeyDown(new HavenKeyInput(HavenKey.Home, HavenKeyModifiers.None)));
        Assert.Equal(picker.Palette[0], picker.SelectedHex);
        Assert.True(picker.KeyDown(new HavenKeyInput(HavenKey.End, HavenKeyModifiers.None)));
        Assert.Equal(picker.Palette[^1], picker.SelectedHex);
    }

    [Fact]
    public void ColourPicker_invalid_hex_normalizes_and_palette_filters()
    {
        var picker = new ColourPicker { SelectedHex = "bogus" };
        Assert.Equal("#000000", picker.SelectedHex);
        picker.Palette = ["#FFFFFF", "bogus", "#000000"];
        Assert.Equal(2, picker.Palette.Count);
    }

    // ---- NumericInput ----

    [Fact]
    public void NumericInput_clamps_snaps_and_syncs_field()
    {
        var numeric = new NumericInput { Minimum = 0, Maximum = 10, Step = 0.5 };
        var changed = 0;
        numeric.ValueChanged += (_, _) => changed++;
        numeric.Value = 3.26;
        Assert.Equal(3.5, numeric.Value, precision: 9);
        Assert.Equal("3.5", numeric.Field.Text);
        Assert.Equal(1, changed);

        numeric.Value = 99;
        Assert.Equal(10, numeric.Value, precision: 9);
        numeric.Value = -5;
        Assert.Equal(0, numeric.Value, precision: 9);
    }

    [Fact]
    public void NumericInput_field_text_parses_and_steppers_nudge()
    {
        var numeric = new NumericInput { Minimum = 0, Maximum = 100, Step = 1, Label = "Stroke width" };
        numeric.Field.Text = "12";
        Assert.Equal(12, numeric.Value, precision: 9);
        numeric.Field.Text = "not-a-number";
        Assert.Equal(12, numeric.Value, precision: 9);

        numeric.Nudge(1);
        Assert.Equal(13, numeric.Value, precision: 9);
        numeric.Nudge(-1);
        Assert.Equal(12, numeric.Value, precision: 9);
        Assert.Equal("Stroke width", numeric.Field.Accessibility.AccessibleName);

        numeric.Suffix = "px";
        Assert.Equal("px", numeric.SuffixLabel.Content);
        Assert.Equal(HavenVisibility.Visible, numeric.SuffixLabel.GetValue(HavenProperties.Visibility));
    }

    // ---- Sidebar / Panel / StatusBar ----

    [Fact]
    public void Sidebar_select_collapse_and_invoke()
    {
        var sidebar = new Sidebar();
        sidebar.SetItems([
            new SidebarItem("todo", "To do", "tasks", true),
            new SidebarItem("doing", "Doing", "tasks")
        ]);
        Assert.Equal(2, sidebar.ItemButtons.Count);
        Assert.Equal("To do", sidebar.ItemButtons[0].Content);

        string? invoked = null;
        sidebar.ItemInvoked += (_, key) => invoked = key;
        Activate(sidebar.ItemButtons[1]);
        Assert.Equal("doing", invoked);

        Assert.True(sidebar.Select("doing"));
        Assert.True(sidebar.ItemButtons[1].State.HasFlag(HavenElementState.Selected));
        Assert.False(sidebar.Select("missing"));

        sidebar.IsCollapsed = true;
        Assert.Equal(string.Empty, sidebar.ItemButtons[0].Content);
        Assert.Equal(HavenLength.Px(64), sidebar.GetValue(HavenProperties.Width));
        sidebar.IsCollapsed = false;
        Assert.Equal("To do", sidebar.ItemButtons[0].Content);
    }

    [Fact]
    public void Panel_expand_collapse_and_content()
    {
        var panel = new Panel { Title = "Stroke" };
        Assert.Equal("Stroke", panel.TitleText.Content);
        panel.SetContent(new Text { Content = "Width" });
        Assert.Single(panel.Content.Children);

        var changed = 0;
        panel.ExpandedChanged += (_, _) => changed++;
        panel.IsExpanded = false;
        Assert.Equal(HavenVisibility.Collapsed, panel.Content.GetValue(HavenProperties.Visibility));
        Assert.Equal(1, changed);
        panel.IsExpanded = true;
        Assert.Equal(HavenVisibility.Visible, panel.Content.GetValue(HavenProperties.Visibility));

        panel.IsCollapsible = false;
        Assert.True(panel.IsExpanded);
        Assert.Equal(HavenVisibility.Collapsed, panel.CollapseButton.GetValue(HavenProperties.Visibility));
    }

    [Fact]
    public void StatusBar_status_collapse_and_indicators()
    {
        var status = new StatusBar();
        status.SetStatus("Saved locally");
        Assert.Equal("Saved locally", status.StatusText.Content);
        Assert.Equal(HavenVisibility.Visible, status.StatusText.GetValue(HavenProperties.Visibility));
        status.SetStatus(null);
        Assert.Equal(HavenVisibility.Collapsed, status.StatusText.GetValue(HavenProperties.Visibility));

        var indicator = new Text { Content = "Synced" };
        status.AddIndicator(indicator);
        Assert.Contains(indicator, status.Trailing.Children);
    }

    // ---- PropertySurface ----

    [Fact]
    public void PropertySurface_builds_all_editor_kinds_and_reports_changes()
    {
        var surface = new PropertySurface();
        surface.SetProperties([
            new HavenTextProperty("title", "Title", "Card A"),
            new HavenNumberProperty("width", "Width", 120, 0, 1000, 1),
            new HavenToggleProperty("visible", "Visible", true),
            new HavenChoiceProperty("status", "Status", ["Todo", "Doing", "Done"], 0),
            new HavenColourProperty("accent", "Accent", "#2563EB")
        ]);
        Assert.Equal(5, surface.Children.Count);

        var changes = new List<HavenPropertyChanged>();
        surface.PropertyChanged += (_, change) => changes.Add(change);

        var title = surface.DescendantsAndSelf().OfType<Input>().First(i => i.Accessibility.AccessibleName == "Title");
        title.Text = "Card B";
        var numeric = surface.DescendantsAndSelf().OfType<NumericInput>().Single();
        numeric.Value = 200;
        var toggle = surface.DescendantsAndSelf().OfType<Toggle>().Single();
        toggle.IsChecked = false;
        var select = surface.DescendantsAndSelf().OfType<Select>().Single();
        select.SelectedIndex = 2;
        var picker = surface.DescendantsAndSelf().OfType<ColourPicker>().Single();
        picker.SelectedHex = "#E81123";

        Assert.Contains(changes, c => c.Key == "title" && (string)c.Value! == "Card B");
        Assert.Contains(changes, c => c.Key == "width" && (double)c.Value! == 200);
        Assert.Contains(changes, c => c.Key == "visible" && (bool)c.Value! == false);
        Assert.Contains(changes, c => c.Key == "status" && (int)c.Value! == 2);
        Assert.Contains(changes, c => c.Key == "accent" && (string)c.Value! == "#E81123");

        Assert.True(surface.TryGetValue<string>("title", out var titleValue));
        Assert.Equal("Card B", titleValue);
        Assert.True(surface.TryGetValue<double>("width", out var widthValue));
        Assert.Equal(200, widthValue);
    }

    [Fact]
    public void PropertySurface_rejects_unknown_descriptors()
    {
        var surface = new PropertySurface();
        Assert.Throws<ArgumentException>(() => surface.SetProperties([new UnknownProperty("x", "X")]));
    }

    private sealed record UnknownProperty(string Key, string Label) : HavenPropertyDescriptor(Key, Label);
}
