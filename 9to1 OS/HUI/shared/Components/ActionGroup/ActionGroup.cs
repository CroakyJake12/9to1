namespace Haven.UI.Components;

/// <summary>
/// Compact action group: a visually joined row of small buttons sharing one
/// surface (pen tools, alignment, zoom steps). Toggle and split behaviours are
/// generic buttons, not Canvas/Boards controls.
/// </summary>
public sealed class ActionGroup : Container
{
    public ActionGroup()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.AccessibleName = "Actions";
        Layout = HavenLayout.Horizontal;
        SetValue(HavenProperties.Gap, HavenLength.Px(2), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "SurfaceRaised", HavenValueSource.Default);
        SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12)), HavenValueSource.Default);
        SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(4)), HavenValueSource.Default);
    }

    public string GroupLabel
    {
        get => Accessibility.AccessibleName ?? "Actions";
        set => Accessibility.AccessibleName = string.IsNullOrWhiteSpace(value) ? "Actions" : value;
    }

    public void AddAction(Button action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action.SetValue(HavenProperties.MinHeight, HavenLength.Px(34));
        action.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 10px"));
        action.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(8)));
        Add(action);
    }

    public void AddElement(HavenElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Add(element);
    }

    public override HavenComponentMetadata Metadata => new(
        "ActionGroup",
        "HUI/shared/Components/ActionGroup/ActionGroup.cs",
        ["ActionGroup"],
        [],
        "Compact joined-button group; members stay ordinary Haven buttons.");
}

/// <summary>
/// Generic checkable button (bold, grid snap, layer visibility). Composes the
/// sealed donor Button so pointer/keyboard activation stays canonical; checked
/// state mirrors Toggle (Checked state + accessibility + change event).
/// </summary>
public sealed class ToggleButton : Container
{
    public static readonly HavenProperty<bool> CheckedProperty =
        HavenPropertyRegistry.Register(new HavenProperty<bool>("ToggleButton.Checked", false));

    public event EventHandler? CheckedChanged;

    public ToggleButton()
    {
        Accessibility.Role = HavenAccessibleRole.CheckBox;
        Accessibility.Focusable = true;
        Layout = HavenLayout.Overlay;
        SetValue(HavenProperties.Background, "Transparent", HavenValueSource.Default);

        Inner = new Button { Variant = ButtonVariant.Ghost };
        Inner.Accessibility.Role = HavenAccessibleRole.CheckBox;
        Inner.Invoked += (_, _) => IsChecked = !IsChecked;
        Add(Inner);
        SyncChrome();
    }

    public Button Inner { get; }

    public string Content
    {
        get => Inner.Content;
        set
        {
            Inner.Content = value ?? string.Empty;
            Inner.Accessibility.AccessibleName = value;
            if (string.IsNullOrWhiteSpace(Accessibility.AccessibleName)) Accessibility.AccessibleName = value;
        }
    }

    public string IconKey
    {
        get => Inner.IconKey;
        set => Inner.IconKey = value ?? string.Empty;
    }

    public ButtonVariant Variant
    {
        get => Inner.Variant;
        set => Inner.Variant = value;
    }

    public bool IsChecked
    {
        get => GetValue(CheckedProperty);
        set
        {
            if (value == GetValue(CheckedProperty)) return;
            SetValue(CheckedProperty, value);
            Accessibility.Checked = value;
            Inner.Accessibility.Checked = value;
            SetState(HavenElementState.Checked, value);
            Inner.SetState(HavenElementState.Checked, value);
            SetState(HavenElementState.Selected, value);
            SyncChrome();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Toggle() => IsChecked = !IsChecked;

    private void SyncChrome()
    {
        Inner.Accessibility.Checked = IsChecked;
    }

    public override HavenComponentMetadata Metadata => new(
        "ToggleButton",
        "HUI/shared/Components/ActionGroup/ActionGroup.cs",
        ["ToggleButton"],
        [],
        "Checkable button composing Button, with Toggle-compatible checked semantics.");
}

/// <summary>
/// Generic split button: a primary action plus a chevron that opens an options
/// menu. The menu reuses PopupMenu, so overlay/light-dismiss stays canonical.
/// </summary>
public sealed class SplitButton : Container
{
    public SplitButton()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        Layout = HavenLayout.Horizontal;
        SetValue(HavenProperties.Gap, HavenLength.Px(1), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "Transparent", HavenValueSource.Default);

        Primary = new Button { Variant = ButtonVariant.Secondary };
        Primary.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        Add(Primary);

        Chevron = new Button { Variant = ButtonVariant.Secondary, IconKey = "chevron-down", Content = string.Empty };
        Chevron.Accessibility.AccessibleName = "More options";
        Chevron.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        Chevron.SetValue(HavenProperties.Width, HavenLength.Px(40));
        Chevron.SetValue(HavenProperties.Padding, HavenThickness.Zero);
        Chevron.Invoked += (_, _) => MenuRequested?.Invoke(this, EventArgs.Empty);
        Add(Chevron);
    }

    public Button Primary { get; }
    public Button Chevron { get; }

    public string Text
    {
        get => Primary.Content;
        set
        {
            Primary.Content = value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(Accessibility.AccessibleName)) Accessibility.AccessibleName = value;
        }
    }

    public event EventHandler? MenuRequested;

    /// <summary>
    /// Opens a canonical PopupMenu anchored to the chevron. The menu is added
    /// to <paramref name="sceneRoot"/> (overlay participation, no layout shift).
    /// </summary>
    public PopupMenu ShowMenu(HavenElement sceneRoot, IReadOnlyList<PopupMenuItem> items, double menuWidth = 220d)
    {
        ArgumentNullException.ThrowIfNull(sceneRoot);
        ArgumentNullException.ThrowIfNull(items);
        var menu = new PopupMenu(Chevron, sceneRoot, items, menuWidth, Accessibility.AccessibleName ?? "Options");
        sceneRoot.Add(menu);
        return menu;
    }

    public override HavenComponentMetadata Metadata => new(
        "SplitButton",
        "HUI/shared/Components/ActionGroup/ActionGroup.cs",
        ["SplitButton"],
        [],
        "Primary action plus menu chevron; menus reuse PopupMenu.");
}
