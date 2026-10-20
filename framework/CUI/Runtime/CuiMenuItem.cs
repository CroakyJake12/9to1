using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CakeOS.Cui.Runtime;

/// <summary>Native menu behavior with CUI-owned automation for opening and invoking it.</summary>
public sealed class CuiMenuItem : MenuItem
{
    public static readonly StyledProperty<Avalonia.Layout.VerticalAlignment> VerticalContentAlignmentProperty =
        ContentControl.VerticalContentAlignmentProperty.AddOwner<CuiMenuItem>();
    public static readonly StyledProperty<Avalonia.Layout.HorizontalAlignment> HorizontalContentAlignmentProperty =
        ContentControl.HorizontalContentAlignmentProperty.AddOwner<CuiMenuItem>();
    public Avalonia.Layout.VerticalAlignment VerticalContentAlignment
    { get => GetValue(VerticalContentAlignmentProperty); set => SetValue(VerticalContentAlignmentProperty, value); }
    public Avalonia.Layout.HorizontalAlignment HorizontalContentAlignment
    { get => GetValue(HorizontalContentAlignmentProperty); set => SetValue(HorizontalContentAlignmentProperty, value); }
    public static readonly StyledProperty<int> MenuColumnsProperty = AvaloniaProperty.Register<CuiMenuItem, int>(nameof(MenuColumns), 1,
        validate: value => value is >= 1 and <= 8);
    public static readonly StyledProperty<bool> IconOnlyProperty = AvaloniaProperty.Register<CuiMenuItem, bool>(nameof(IconOnly));
    public int MenuColumns { get => GetValue(MenuColumnsProperty); set => SetValue(MenuColumnsProperty, value); }
    public bool IconOnly { get => GetValue(IconOnlyProperty); set => SetValue(IconOnlyProperty, value); }

    public CuiMenuItem()
    {
        AddHandler(KeyDownEvent, NavigateGallery, RoutingStrategies.Tunnel);
        PropertyChanged += (_, args) =>
        {
            if (args.Property == MenuColumnsProperty)
                ItemsPanel = new FuncTemplate<Panel?>(() => new UniformGrid { Columns = MenuColumns });
        };
    }

    private void NavigateGallery(object? sender, KeyEventArgs args)
    {
        if (!IsSubMenuOpen || MenuColumns <= 1 || args.KeyModifiers != KeyModifiers.None
            || args.Source is not CuiMenuItem current) return;
        var entries = Items.OfType<CuiMenuItem>().ToArray();
        var index = Array.IndexOf(entries, current);
        if (index < 0) return;
        var delta = args.Key switch { Key.Left => -1, Key.Right => 1, Key.Up => -MenuColumns, Key.Down => MenuColumns, _ => 0 };
        var next = args.Key switch { Key.Home => 0, Key.End => entries.Length - 1, _ => index + delta };
        if (delta == 0 && args.Key is not (Key.Home or Key.End)) return;
        while (next >= 0 && next < entries.Length && (!entries[next].IsEffectivelyEnabled || !entries[next].IsVisible))
            next += delta == 0 ? (args.Key == Key.Home ? 1 : -1) : delta;
        if (next >= 0 && next < entries.Length) { SelectedIndex = next; entries[next].Focus(); }
        args.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(CuiMenuItem owner) : MenuItemAutomationPeer(owner), IInvokeProvider, IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.ItemCount == 0 ? ExpandCollapseState.LeafNode
            : owner.IsSubMenuOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public bool ShowsMenu => true;
        public void Expand() { if (owner.IsEffectivelyEnabled && owner.ItemCount > 0) owner.IsSubMenuOpen = true; }
        public void Collapse() => owner.IsSubMenuOpen = false;
        public void Invoke()
        {
            if (!owner.IsEffectivelyEnabled) return;
            if (owner.ItemCount > 0) { Expand(); return; }
            // Same routed event as the native pointer and keyboard menu handlers.
            Dispatcher.UIThread.Post(() =>
            {
                if (owner.IsEffectivelyEnabled) owner.RaiseEvent(new RoutedEventArgs(ClickEvent));
            });
        }
    }
}
