namespace Haven.UI.Components;

/// <summary>A Haven tab: HAV-TAB-002 fixes the visible order as icon, left-aligned bold title, then a separately
/// focusable close control. IconKey is the leading app/page icon; IsCloseable shows the visible X.</summary>
public sealed record TabStripItem(string Key, string Label, bool IsSelected = false, bool HasContextMenu = true, string? IconKey = null, bool IsCloseable = false);

/// <summary>Canonical Haven tab strip with icon+label tabs, visible close controls, selected underline, overflow, and secondary invocation.</summary>
public sealed class TabStrip : Container
{
    private readonly Container _scroller;
    private readonly Button _left;
    private readonly Button _right;
    private readonly List<Button> _buttons = [];
    private readonly List<Button> _closeButtons = [];
    private readonly List<Container> _indicators = [];
    private IReadOnlyList<TabStripItem> _items = [];

    public TabStrip()
    {
        Accessibility.Role = HavenAccessibleRole.Tab;
        Accessibility.AccessibleName = "Tabs";
        Layout = HavenLayout.Grid; Columns = "Auto 1fr Auto"; Rows = "54px";
        SetValue(HavenProperties.Height, HavenLength.Px(54));
        SetValue(HavenProperties.MinWidth, HavenLength.Px(180));
        _left = ScrollButton("TabStrip.ScrollLeft", "chevron-left", "Scroll tabs left");
        _right = ScrollButton("TabStrip.ScrollRight", "chevron-right", "Scroll tabs right");
        _left.SetValue(HavenProperties.Column, 0); _right.SetValue(HavenProperties.Column, 2);
        _left.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed); _right.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        _scroller = new Container { Name = "TabStrip.Scroller", Layout = HavenLayout.Horizontal };
        _scroller.SetValue(HavenProperties.Column, 1); _scroller.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        _scroller.SetValue(HavenProperties.Height, HavenLength.Px(54)); _scroller.SetValue(HavenProperties.Gap, HavenLength.Px(5));
        _scroller.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        Add(_left); Add(_scroller); Add(_right);
        _left.Invoked += (_, _) => Scroll(-240); _right.Invoked += (_, _) => Scroll(240);
        _scroller.Invalidated += (_, _) => UpdateScrollButtons();
    }

    public event EventHandler<string>? ItemInvoked;
    public event EventHandler<string>? ItemSecondaryInvoked;

    /// <summary>Raised when a tab's visible close control is activated. This closes that tab only; it never selects it first.</summary>
    public event EventHandler<string>? ItemCloseRequested;

    public IReadOnlyList<TabStripItem> Items => _items;
    public IReadOnlyList<Button> ItemButtons => _buttons;
    public IReadOnlyList<Button> CloseButtons => _closeButtons;
    public IReadOnlyList<Container> SelectionIndicators => _indicators;

    public void SetItems(IReadOnlyList<TabStripItem> items)
    {
        _items = items.ToArray();
        foreach (var child in _scroller.Children.ToArray()) _scroller.Remove(child);
        _buttons.Clear(); _closeButtons.Clear(); _indicators.Clear();
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var host = new Container { Name = $"TabStrip.Item.{i}", Layout = HavenLayout.Overlay };
            host.SetValue(HavenProperties.Height, HavenLength.Px(48));
            var button = new Button { Name = $"TabStrip.Item.{i}.Button", Variant = ButtonVariant.Text, IconKey = item.IconKey ?? string.Empty, Content = Label(item.Label) };
            button.Accessibility.Role = HavenAccessibleRole.TabItem; button.Accessibility.AccessibleName = item.Label; button.Accessibility.Description = item.HasContextMenu ? "Right-click for tab options" : "Tab"; button.Accessibility.Selected = item.IsSelected;
            button.SetValue(HavenProperties.MinWidth, HavenLength.Px(72)); button.SetValue(HavenProperties.MaxWidth, HavenLength.Px(260));
            button.SetValue(HavenProperties.Height, HavenLength.Px(48)); button.SetValue(HavenProperties.MinHeight, HavenLength.Px(48));
            button.SetValue(HavenProperties.Padding, HavenThickness.Parse(item.IsCloseable ? "5px 36px 3px 12px" : "5px 12px 3px 12px")); button.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(0)));
            button.SetValue(HavenProperties.Background, "Transparent"); button.SetValue(HavenProperties.Foreground, item.IsSelected ? "AccentSecondary" : "TextSecondary");
            button.SetValue(HavenProperties.FontSize, 14d); button.SetValue(HavenProperties.FontWeight, item.IsSelected ? 800 : 700); button.SetState(HavenElementState.Selected, item.IsSelected);
            button.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start);
            button.Invoked += (_, _) => ItemInvoked?.Invoke(this, item.Key);
            button.SecondaryInvoked += (_, _) => { if (item.HasContextMenu) ItemSecondaryInvoked?.Invoke(this, item.Key); };
            var underline = new Container { Name = $"TabStrip.Item.{i}.Underline", Layout = HavenLayout.Overlay };
            underline.SetValue(HavenProperties.Width, HavenLength.Px(Math.Clamp((item.Label.Length * 7.2) + 12, 30, 170))); underline.SetValue(HavenProperties.Height, HavenLength.Px(3));
            underline.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start); underline.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.End);
            underline.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 0px 6px"));
            underline.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(2))); underline.SetValue(HavenProperties.Background, item.IsSelected ? "Accent" : "Transparent");
            underline.SetValue(HavenProperties.PointerEvents, HavenPointerEvents.None); underline.SetValue(HavenProperties.ZIndex, 1);
            host.Add(button); host.Add(underline);
            if (item.IsCloseable)
            {
                // HAV-TAB-002: the close control is a real, separately focusable button with an
                // accessible name, visible on every closable tab, not a hover-only affordance.
                var close = new Button { Name = $"TabStrip.Item.{i}.Close", Variant = ButtonVariant.Icon, IconKey = "close" };
                close.Accessibility.AccessibleName = $"Close {Label(item.Label)}";
                close.SetValue(HavenProperties.Width, HavenLength.Px(26)); close.SetValue(HavenProperties.Height, HavenLength.Px(40));
                close.SetValue(HavenProperties.MinHeight, HavenLength.Px(28));
                close.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.End); close.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);
                close.SetValue(HavenProperties.Margin, HavenThickness.Parse("0px 0px 3px 0px"));
                close.SetValue(HavenProperties.Background, "Transparent");
                close.SetValue(HavenProperties.ZIndex, 3);
                close.Invoked += (_, _) => ItemCloseRequested?.Invoke(this, item.Key);
                host.Add(close); _closeButtons.Add(close);
            }
            _scroller.Add(host); _buttons.Add(button); _indicators.Add(underline);
        }
        UpdateScrollButtons();
    }

    public double ScrollOffset => _scroller.ScrollX;
    public double MaxScrollOffset => _scroller.MaxScrollX;

    public bool EnsureVisible(Button button)
    {
        var itemIndex = -1;
        for (var i = 0; i < _buttons.Count; i++)
        {
            if (!ReferenceEquals(_buttons[i], button)) continue;
            itemIndex = i;
            break;
        }
        if (itemIndex < 0 || itemIndex >= _scroller.Children.Count) return false;

        var host = _scroller.Children[itemIndex];
        var viewportLeft = _scroller.Bounds.X;
        var viewportRight = _scroller.Bounds.Right;
        var delta = host.Bounds.X < viewportLeft
            ? host.Bounds.X - viewportLeft
            : host.Bounds.Right > viewportRight
                ? host.Bounds.Right - viewportRight
                : 0d;
        if (Math.Abs(delta) <= .5d) return false;
        var changed = _scroller.ScrollBy(delta, 0);
        if (changed) UpdateScrollButtons();
        return changed;
    }

    private static Button ScrollButton(string name, string icon, string accessible)
    {
        var button = new Button { Name = name, Variant = ButtonVariant.Icon, IconKey = icon }; button.Accessibility.AccessibleName = accessible;
        button.SetValue(HavenProperties.Width, HavenLength.Px(32)); button.SetValue(HavenProperties.Height, HavenLength.Px(40)); button.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
        return button;
    }
    private void Scroll(double delta) { _scroller.ScrollBy(delta, 0); UpdateScrollButtons(); }
    private void UpdateScrollButtons()
    {
        var measured = _scroller.ViewportSize.Width > .5d;
        _left.SetValue(HavenProperties.Visibility, _scroller.ScrollX > .5d ? HavenVisibility.Visible : HavenVisibility.Collapsed);
        var right = measured ? _scroller.MaxScrollX > _scroller.ScrollX + .5d : _items.Count > 4;
        _right.SetValue(HavenProperties.Visibility, right ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }
    private static string Label(string value) => value.Length <= 24 ? value : $"{value[..23]}…";
}
