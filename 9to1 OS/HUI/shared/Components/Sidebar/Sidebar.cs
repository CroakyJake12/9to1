namespace Haven.UI.Components;

/// <summary>Generic sidebar navigation item: identity and presentation only.</summary>
public sealed record SidebarItem(string Key, string Label, string IconKey = "", bool IsSelected = false);

/// <summary>
/// Generic sidebar: vertical navigation with selection, collapse-to-icons, and
/// overflow scroll. Boards group navigation and Canvas page/tool rails compose
/// this; hierarchy and tools stay app-owned.
/// </summary>
public sealed class Sidebar : Container
{
    private readonly List<Button> _buttons = [];
    private IReadOnlyList<SidebarItem> _items = [];
    private bool _collapsed;

    public Sidebar()
    {
        Accessibility.Role = HavenAccessibleRole.Group;
        Accessibility.AccessibleName = "Sidebar";
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Width, HavenLength.Px(232), HavenValueSource.Default);
        SetValue(HavenProperties.MinWidth, HavenLength.Px(56), HavenValueSource.Default);
        SetValue(HavenProperties.Height, HavenLength.Percent(100), HavenValueSource.Default);
        SetValue(HavenProperties.Background, "Surface", HavenValueSource.Default);
        SetValue(HavenProperties.Gap, HavenLength.Px(4), HavenValueSource.Default);
        SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(8)), HavenValueSource.Default);
        SetValue(HavenProperties.Overflow, HavenOverflow.Scroll, HavenValueSource.Default);
        SetValue(HavenProperties.Clip, true, HavenValueSource.Default);
    }

    public event EventHandler<string>? ItemInvoked;

    public IReadOnlyList<SidebarItem> Items => _items;
    public IReadOnlyList<Button> ItemButtons => _buttons;

    public bool IsCollapsed
    {
        get => _collapsed;
        set
        {
            if (_collapsed == value) return;
            _collapsed = value;
            SetValue(HavenProperties.Width, HavenLength.Px(value ? 64 : 232));
            ApplyLabels();
        }
    }

    public void SetItems(IReadOnlyList<SidebarItem> items)
    {
        _items = items?.ToArray() ?? [];
        foreach (var child in Children.ToArray()) Remove(child);
        _buttons.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var button = new Button { Variant = ButtonVariant.Navigation, IconKey = item.IconKey };
            button.Accessibility.AccessibleName = item.Label;
            button.Accessibility.Selected = item.IsSelected;
            button.SetValue(HavenProperties.Width, HavenLength.Percent(100));
            button.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
            button.SetState(HavenElementState.Selected, item.IsSelected);
            var key = item.Key;
            button.Invoked += (_, _) => ItemInvoked?.Invoke(this, key);
            Add(button);
            _buttons.Add(button);
        }
        ApplyLabels();
    }

    public bool Select(string key)
    {
        var index = -1;
        for (var i = 0; i < _items.Count; i++)
            if (string.Equals(_items[i].Key, key, StringComparison.Ordinal)) { index = i; break; }
        if (index < 0) return false;
        var updated = _items.Select((item, i) => item with { IsSelected = i == index }).ToArray();
        SetItems(updated);
        return true;
    }

    private void ApplyLabels()
    {
        for (var i = 0; i < _buttons.Count && i < _items.Count; i++)
            _buttons[i].Content = _collapsed ? string.Empty : _items[i].Label;
    }

    public override HavenComponentMetadata Metadata => new(
        "Sidebar",
        "HUI/shared/Components/Sidebar/Sidebar.cs",
        ["Sidebar"],
        [],
        "Collapsible vertical navigation; destinations stay app-owned.");
}
