using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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
    private long _focusRecoveryGeneration;
    private long _pendingDisableGeneration;

    public CuiMenuItem()
    {
        AddHandler(KeyDownEvent, NavigateGallery, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, ActivateMenuItemWithSpace, RoutingStrategies.Tunnel);
        PropertyChanged += (_, args) =>
        {
            if (args.Property == MenuColumnsProperty)
                ItemsPanel = new FuncTemplate<Panel?>(() => new UniformGrid { Columns = MenuColumns });
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        var effectiveEnabledChanged = change.Property.Name == "IsEffectivelyEnabled";
        // Capture focus before InputElement's effective-disable setter clears it through the
        // FocusManager after OnPropertyChanged returns.
        var focusedItemBeingDisabled = effectiveEnabledChanged && IsFocused && !IsEffectivelyEnabled;
        if (effectiveEnabledChanged && IsEffectivelyEnabled)
        {
            ++_focusRecoveryGeneration;
            _pendingDisableGeneration = 0;
        }
        var generation = focusedItemBeingDisabled ? ++_focusRecoveryGeneration : 0;
        if (focusedItemBeingDisabled)
        {
            _pendingDisableGeneration = generation;
        }
        base.OnPropertyChanged(change);
        if (focusedItemBeingDisabled) RecoverMenuItemFocus(generation);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ++_focusRecoveryGeneration;
        _pendingDisableGeneration = 0;
        base.OnDetachedFromVisualTree(e);
    }

    private void RecoverMenuItemFocus(long generation)
    {
        var owner = this.GetLogicalAncestors().OfType<ItemsControl>()
            .FirstOrDefault(item => item.Items.OfType<CuiMenuItem>().Any(child => ReferenceEquals(child, this)));
        var root = TopLevel.GetTopLevel(this);
        if (owner is null || root is null || !IsMenuOwnerOpen(owner)) return;

        long ownerTransition = 0;
        AvaloniaProperty? ownerOpenProperty = owner switch
        {
            MenuItem => MenuItem.IsSubMenuOpenProperty,
            ContextMenu => ContextMenu.IsOpenProperty,
            _ => null,
        };
        EventHandler<AvaloniaPropertyChangedEventArgs>? ownerChanged = null;
        if (ownerOpenProperty is not null)
        {
            ownerChanged = (_, changed) =>
            {
                if (changed.Property == ownerOpenProperty) ++ownerTransition;
            };
            owner.PropertyChanged += ownerChanged;
        }
        long focusTransition = 0;
        long focusClearedByDisableTransition = 0;
        EventHandler<FocusChangedEventArgs> focusChanged = (_, args) =>
        {
            ++focusTransition;
            if (!IsEffectivelyEnabled && ReferenceEquals(args.OldFocusedElement, this)
                && args.NewFocusedElement is null)
                focusClearedByDisableTransition = focusTransition;
        };
        root.AddHandler(InputElement.GotFocusEvent, focusChanged, RoutingStrategies.Bubble, handledEventsToo: true);
        root.AddHandler(InputElement.LostFocusEvent, focusChanged, RoutingStrategies.Bubble, handledEventsToo: true);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_focusRecoveryGeneration != generation || _pendingDisableGeneration != generation
                    || IsEffectivelyEnabled || !ReferenceEquals(TopLevel.GetTopLevel(this), root)
                    || TopLevel.GetTopLevel(owner) is null || ownerTransition != 0
                    || !IsMenuOwnerOpen(owner) || !owner.IsEffectivelyEnabled || !owner.IsEffectivelyVisible)
                    return;
                var manager = root.FocusManager;
                if (manager is null) return;
                var focused = manager.GetFocusedElement();
                var stillFocused = ReferenceEquals(focused, this)
                    && focusTransition == 0;
                var frameworkClearedFocus = focused is null
                    && focusClearedByDisableTransition != 0
                    && focusTransition == focusClearedByDisableTransition;
                if (!stillFocused && !frameworkClearedFocus) return;

                var entries = owner.Items.OfType<CuiMenuItem>().ToArray();
                var index = Array.IndexOf(entries, this);
                if (index < 0) return;
                for (var distance = 1; distance < entries.Length; distance++)
                {
                    var next = index + distance;
                    if (next < entries.Length && entries[next].IsEffectivelyEnabled
                        && entries[next].IsEffectivelyVisible && entries[next].Focus()) return;
                    var previous = index - distance;
                    if (previous >= 0 && entries[previous].IsEffectivelyEnabled
                        && entries[previous].IsEffectivelyVisible && entries[previous].Focus()) return;
                }

                if (owner.Focusable && owner.IsEffectivelyEnabled && owner.IsEffectivelyVisible)
                    owner.Focus();
            }
            finally
            {
                root.RemoveHandler(InputElement.GotFocusEvent, focusChanged);
                root.RemoveHandler(InputElement.LostFocusEvent, focusChanged);
                if (ownerChanged is not null) owner.PropertyChanged -= ownerChanged;
                if (_pendingDisableGeneration == generation) _pendingDisableGeneration = 0;
            }
        }, DispatcherPriority.Input);
    }

    private static bool IsMenuOwnerOpen(ItemsControl owner) => owner switch
    {
        MenuItem menuItem => menuItem.IsSubMenuOpen,
        ContextMenu contextMenu => contextMenu.IsOpen,
        _ => true,
    };

    // The pinned menu interaction handler handles Enter but has no Space activation; CUI fills that
    // keyboard path without introducing a second action or checked-state implementation.
    private void ActivateMenuItemWithSpace(object? sender, KeyEventArgs args)
    {
        if (args.Handled || args.Key != Key.Space || args.KeyModifiers != KeyModifiers.None
            || args.Source is not CuiMenuItem item)
            return;
        if (!ReferenceEquals(item, this)
            && (!IsSubMenuOpen || !Items.OfType<CuiMenuItem>().Any(child => ReferenceEquals(child, item)))) return;
        if (!item.IsEffectivelyEnabled || !item.IsEffectivelyVisible)
        {
            args.Handled = true;
            return;
        }
        if (item.ItemCount > 0)
        {
            var root = TopLevel.GetTopLevel(item);
            var focusBefore = root?.FocusManager?.GetFocusedElement();
            long openTransition = 0;
            EventHandler<AvaloniaPropertyChangedEventArgs> submenuChanged = (_, change) =>
            {
                if (change.Property == MenuItem.IsSubMenuOpenProperty) ++openTransition;
            };
            item.PropertyChanged += submenuChanged;
            item.IsSubMenuOpen = true;
            var openedTransition = openTransition;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (root is null || !ReferenceEquals(focusBefore, item) || !item.IsSubMenuOpen
                        || openTransition != openedTransition || !ReferenceEquals(TopLevel.GetTopLevel(item), root)
                        || !ReferenceEquals(root.FocusManager?.GetFocusedElement(), focusBefore)) return;
                    var first = item.Items.OfType<CuiMenuItem>().FirstOrDefault(child =>
                        child.IsEffectivelyEnabled && child.IsEffectivelyVisible);
                    first?.Focus();
                }
                finally { item.PropertyChanged -= submenuChanged; }
            }, DispatcherPriority.Input);
        }
        else
        {
            item.ActivateFromCui();
        }
        args.Handled = true;
    }

    /// <summary>Apply check/radio state and raise the same authored Click event used by CUI commands.</summary>
    internal void ActivateFromCui()
    {
        if (!IsEffectivelyEnabled) return;
        if (ToggleType == MenuItemToggleType.CheckBox)
            SetCurrentValue(MenuItem.IsCheckedProperty, !IsChecked);
        else if (ToggleType == MenuItemToggleType.Radio && !IsChecked)
            SetCurrentValue(MenuItem.IsCheckedProperty, true);
        RaiseEvent(new RoutedEventArgs(ClickEvent));
        if (!StaysOpenOnClick)
            this.GetLogicalAncestors().OfType<MenuBase>().FirstOrDefault()?.Close();
    }

    private void NavigateGallery(object? sender, KeyEventArgs args)
    {
        if (!IsSubMenuOpen || MenuColumns <= 1 || args.KeyModifiers != KeyModifiers.None
            || args.Source is not CuiMenuItem current) return;
        var entries = Items.OfType<CuiMenuItem>().ToArray();
        var index = Array.IndexOf(entries, current);
        if (index < 0) return;
        if (args.Key is Key.Up or Key.Down)
        {
            var row = index / MenuColumns + (args.Key == Key.Down ? 1 : -1);
            var rowStart = row * MenuColumns;
            if (row < 0 || rowStart >= entries.Length) { args.Handled = true; return; }

            // Preserve the current column where possible. A short final row (or a
            // disabled cell) must not make an otherwise reachable gallery choice
            // keyboard-inaccessible: choose the nearest enabled cell in that row.
            var rowEnd = Math.Min(rowStart + MenuColumns, entries.Length);
            var column = Math.Min(index % MenuColumns, rowEnd - rowStart - 1);
            var target = rowStart + column;
            for (var distance = 0; distance < MenuColumns; distance++)
            {
                var right = target + distance;
                if (right < rowEnd && entries[right].IsEffectivelyEnabled && entries[right].IsVisible)
                {
                    SelectedIndex = right; entries[right].Focus(); args.Handled = true; return;
                }
                var left = target - distance;
                if (distance > 0 && left >= rowStart && entries[left].IsEffectivelyEnabled && entries[left].IsVisible)
                {
                    SelectedIndex = left; entries[left].Focus(); args.Handled = true; return;
                }
            }
            args.Handled = true;
            return;
        }
        var delta = args.Key switch { Key.Left => -1, Key.Right => 1, Key.Up => -MenuColumns, Key.Down => MenuColumns, _ => 0 };
        var next = args.Key switch { Key.Home => 0, Key.End => entries.Length - 1, _ => index + delta };
        if (delta == 0 && args.Key is not (Key.Home or Key.End)) return;
        while (next >= 0 && next < entries.Length && (!entries[next].IsEffectivelyEnabled || !entries[next].IsVisible))
            next += delta == 0 ? (args.Key == Key.Home ? 1 : -1) : delta;
        if (next >= 0 && next < entries.Length) { SelectedIndex = next; entries[next].Focus(); }
        args.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer : MenuItemAutomationPeer, IInvokeProvider, IExpandCollapseProvider, IToggleProvider
    {
        private readonly CuiMenuItem _owner;

        public Peer(CuiMenuItem owner) : base(owner)
        {
            _owner = owner;
            owner.PropertyChanged += (_, change) =>
            {
                if (change.Property == MenuItem.IsCheckedProperty)
                    RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty,
                        ToToggleState((bool)change.OldValue!), ToToggleState((bool)change.NewValue!));
            };
        }

        public ExpandCollapseState ExpandCollapseState => _owner.ItemCount == 0 ? ExpandCollapseState.LeafNode
            : _owner.IsSubMenuOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public Avalonia.Automation.Provider.ToggleState ToggleState => ToToggleState(_owner.IsChecked);
        public bool ShowsMenu => true;
        public void Expand()
        {
            if (!_owner.IsEffectivelyEnabled || _owner.ItemCount == 0) return;
            var first = _owner.Items.OfType<CuiMenuItem>().FirstOrDefault(item =>
                item.IsEffectivelyEnabled && item.IsEffectivelyVisible);
            if (first is not null) _owner.SelectedIndex = _owner.Items.IndexOf(first);
            _owner.IsSubMenuOpen = true;
        }
        public void Collapse() => _owner.IsSubMenuOpen = false;
        public void Toggle()
        {
            EnsureEnabled();
            if (_owner.ToggleType == MenuItemToggleType.Radio && _owner.IsChecked) return;
            if (_owner.ToggleType is not (MenuItemToggleType.CheckBox or MenuItemToggleType.Radio))
                throw new InvalidOperationException("This menu item does not expose a toggle action.");
            _owner.ActivateFromCui();
        }

        protected override object? GetProviderCore(Type providerType)
        {
            if (providerType == typeof(IToggleProvider) && _owner.ToggleType == MenuItemToggleType.None) return null;
            return base.GetProviderCore(providerType);
        }

        private static Avalonia.Automation.Provider.ToggleState ToToggleState(bool isChecked) => isChecked
            ? Avalonia.Automation.Provider.ToggleState.On : Avalonia.Automation.Provider.ToggleState.Off;

        public void Invoke()
        {
            if (!_owner.IsEffectivelyEnabled) return;
            if (_owner.ItemCount > 0) { Expand(); return; }
            Dispatcher.UIThread.Post(() =>
            {
                if (_owner.IsEffectivelyEnabled) _owner.ActivateFromCui();
            });
        }
    }
}
