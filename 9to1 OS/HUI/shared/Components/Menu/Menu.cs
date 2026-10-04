namespace Haven.UI.Components;

/// <summary>Generic menu item: identity and presentation only, no product types.</summary>
public sealed record HavenMenuItem(string Key, string Label, string IconKey = "", bool Enabled = true);

/// <summary>
/// Generic inline menu: a keyboard-navigable list of actions with Menu/MenuItem
/// accessibility. Overlay presentation reuses PopupMenu (see MenuButton); this
/// is the embeddable list for popovers, dialogs, and command palettes.
/// </summary>
public sealed class Menu : Container, IHavenKeyboardInputTarget
{
    private readonly List<Button> _buttons = [];
    private IReadOnlyList<HavenMenuItem> _items = [];
    private int _selectedIndex = -1;

    public Menu()
    {
        Accessibility.Role = HavenAccessibleRole.Menu;
        Accessibility.AccessibleName = "Menu";
        Accessibility.Focusable = true;
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Gap, HavenLength.Px(2), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "Transparent", HavenValueSource.Default);
    }

    public event EventHandler<string>? ItemInvoked;

    public IReadOnlyList<HavenMenuItem> Items => _items;
    public IReadOnlyList<Button> ItemButtons => _buttons;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var next = value >= 0 && value < _items.Count ? value : -1;
            if (_selectedIndex == next) return;
            _selectedIndex = next;
            SyncSelection();
        }
    }

    public void SetItems(IReadOnlyList<HavenMenuItem> items)
    {
        _items = items?.ToArray() ?? [];
        foreach (var child in Children.ToArray()) Remove(child);
        _buttons.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var index = i;
            var button = new Button
            {
                Content = item.Label,
                IconKey = item.IconKey,
                Variant = ButtonVariant.Navigation
            };
            button.Accessibility.Role = HavenAccessibleRole.MenuItem;
            button.Accessibility.AccessibleName = item.Label;
            button.Accessibility.Selected = index == _selectedIndex;
            button.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
            button.SetValue(HavenProperties.Padding, HavenThickness.Parse("7px 10px"));
            button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(10)));
            button.SetValue(HavenProperties.FontSize, 13d);
            button.SetValue(HavenProperties.Enabled, item.Enabled);
            button.SetState(HavenElementState.Disabled, !item.Enabled);
            button.Invoked += (_, _) =>
            {
                if (!item.Enabled) return;
                _selectedIndex = index;
                SyncSelection();
                ItemInvoked?.Invoke(this, item.Key);
            };
            Add(button);
            _buttons.Add(button);
        }
        if (_selectedIndex >= _items.Count) _selectedIndex = -1;
        SyncSelection();
    }

    public bool ActivateSelected()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _items.Count) return false;
        var item = _items[_selectedIndex];
        if (!item.Enabled) return false;
        ItemInvoked?.Invoke(this, item.Key);
        return true;
    }

    public bool KeyDown(HavenKeyInput input)
    {
        if (_items.Count == 0) return false;
        switch (input.Key)
        {
            case HavenKey.Down:
                MoveSelection(1);
                return true;
            case HavenKey.Up:
                MoveSelection(-1);
                return true;
            case HavenKey.Home:
                SelectFirstEnabled();
                return true;
            case HavenKey.End:
                SelectLastEnabled();
                return true;
            case HavenKey.Enter or HavenKey.Space:
                return ActivateSelected();
            default:
                return false;
        }
    }

    private void MoveSelection(int direction)
    {
        if (_items.Count == 0) return;
        var start = _selectedIndex < 0 ? (direction >= 0 ? -1 : 0) : _selectedIndex;
        for (var step = 1; step <= _items.Count; step++)
        {
            var candidate = start + direction * step;
            if (candidate < 0 || candidate >= _items.Count) break;
            if (!_items[candidate].Enabled) continue;
            SelectedIndex = candidate;
            return;
        }
    }

    private void SelectFirstEnabled()
    {
        for (var i = 0; i < _items.Count; i++)
            if (_items[i].Enabled) { SelectedIndex = i; return; }
    }

    private void SelectLastEnabled()
    {
        for (var i = _items.Count - 1; i >= 0; i--)
            if (_items[i].Enabled) { SelectedIndex = i; return; }
    }

    private void SyncSelection()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            var selected = i == _selectedIndex;
            _buttons[i].SetState(HavenElementState.Selected, selected);
            _buttons[i].Accessibility.Selected = selected;
        }
    }

    public override HavenComponentMetadata Metadata => new(
        "Menu",
        "HUI/shared/Components/Menu/Menu.cs",
        ["Menu"],
        [],
        "Inline keyboard-navigable menu; overlay menus reuse PopupMenu.");
}

/// <summary>
/// Generic button that opens a canonical PopupMenu. Composes the sealed donor
/// Button; Canvas tool presets and Boards card/group "more" actions compose this
/// instead of copying overlay code.
/// </summary>
public sealed class MenuButton : Container
{
    private IReadOnlyList<PopupMenuItem> _menuItems = [];

    public MenuButton()
    {
        Accessibility.Role = HavenAccessibleRole.Button;
        Accessibility.AccessibleName = "More actions";
        Layout = HavenLayout.Overlay;
        SetValue(HavenProperties.Background, "Transparent", HavenValueSource.Default);

        Inner = new Button { IconKey = "more" };
        Inner.Accessibility.AccessibleName = "More actions";
        Inner.Invoked += (_, _) => MenuRequested?.Invoke(this, EventArgs.Empty);
        Add(Inner);
    }

    public Button Inner { get; }

    public string Content
    {
        get => Inner.Content;
        set
        {
            Inner.Content = value ?? string.Empty;
            Inner.Accessibility.AccessibleName = value;
        }
    }

    public string IconKey
    {
        get => Inner.IconKey;
        set => Inner.IconKey = value ?? string.Empty;
    }

    public IReadOnlyList<PopupMenuItem> MenuItems
    {
        get => _menuItems;
        set => _menuItems = value ?? [];
    }

    public double MenuWidth { get; set; } = 220d;

    public event EventHandler? MenuRequested;

    /// <summary>Opens the menu anchored to this button; added to the scene root as an overlay.</summary>
    public PopupMenu OpenMenu(HavenElement sceneRoot, string accessibleName = "Actions menu")
    {
        ArgumentNullException.ThrowIfNull(sceneRoot);
        var menu = new PopupMenu(Inner, sceneRoot, MenuItems, MenuWidth, accessibleName);
        sceneRoot.Add(menu);
        return menu;
    }

    public override HavenComponentMetadata Metadata => new(
        "MenuButton",
        "HUI/shared/Components/Menu/Menu.cs",
        ["MenuButton"],
        [],
        "Button that opens a canonical PopupMenu overlay.");
}
