using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CakeOS.Cui.Runtime;

/// <summary>Canonical menu mechanics return focus to the live editor after dismissal.</summary>
public sealed class CuiMenu : Menu
{
    private TopLevel? _root;
    private WeakReference<Control>? _outsideFocus;
    private long _generation;
    private long _spaceOpenGeneration;

    public CuiMenu() => AddHandler(KeyDownEvent, ActivateWithSpace, RoutingStrategies.Tunnel);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _generation++; _root = TopLevel.GetTopLevel(this);
        if (_root is Window window) window.PropertyChanged += OnOwnerWindowChanged;
        _root?.AddHandler(GotFocusEvent, RootFocused, RoutingStrategies.Bubble, handledEventsToo: true);
        RememberOutsideFocus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _generation++; _root?.RemoveHandler(GotFocusEvent, RootFocused);
        if (_root is Window window) window.PropertyChanged -= OnOwnerWindowChanged;
        _spaceOpenGeneration++;
        _root = null; _outsideFocus = null;
        // Native popup windows outlive visual detachment unless the menu closes.
        // The old editor no longer owns focus, so bypass focus-return scheduling.
        base.Close();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name == "IsEffectivelyVisible" && !IsEffectivelyVisible) Close();
    }

    private void OnOwnerWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == Window.WindowStateProperty && sender is Window { WindowState: WindowState.Minimized })
            Close();
    }

    private void ActivateWithSpace(object? sender, KeyEventArgs args)
    {
        if (args.Handled || args.Key != Key.Space || args.KeyModifiers != KeyModifiers.None
            || args.Source is not CuiMenuItem item
            || !Items.OfType<CuiMenuItem>().Any(child => ReferenceEquals(child, item))) return;
        args.Handled = true;
        if (!item.IsEffectivelyEnabled || !item.IsEffectivelyVisible) return;
        if (item.ItemCount == 0)
        {
            item.ActivateFromCui();
            return;
        }

        var root = TopLevel.GetTopLevel(item);
        var focusBefore = root?.FocusManager?.GetFocusedElement();
        var generation = ++_spaceOpenGeneration;
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
                if (generation != _spaceOpenGeneration || root is null || !item.IsSubMenuOpen
                    || openTransition != openedTransition || !ReferenceEquals(TopLevel.GetTopLevel(item), root)
                    || !ReferenceEquals(root.FocusManager?.GetFocusedElement(), focusBefore)) return;
                item.Items.OfType<CuiMenuItem>().FirstOrDefault(child =>
                    child.IsEffectivelyEnabled && child.IsEffectivelyVisible)?.Focus();
            }
            finally { item.PropertyChanged -= submenuChanged; }
        }, DispatcherPriority.Input);
    }

    private void RootFocused(object? sender, FocusChangedEventArgs e) => RememberOutsideFocus();

    private bool OwnsFocus(Control control) => ReferenceEquals(control, this) || this.IsVisualAncestorOf(control)
        || control.GetLogicalAncestors().Any(ancestor => ReferenceEquals(ancestor, this));

    private void RememberOutsideFocus()
    {
        if (_root?.FocusManager?.GetFocusedElement() is Control control
            && ReferenceEquals(TopLevel.GetTopLevel(control), _root) && !OwnsFocus(control)
            && control is not MenuItem && !control.GetLogicalAncestors().Any(ancestor => ancestor is Menu))
            _outsideFocus = new(control);
    }

    public override void Open()
    {
        if (!IsOpen) { _generation++; RememberOutsideFocus(); }
        base.Open();
    }

    public override void Close()
    {
        if (!IsOpen) return;
        var root = _root; Control? target = null; _outsideFocus?.TryGetTarget(out target);
        var before = root?.FocusManager?.GetFocusedElement() as Control;
        base.Close();
        var after = root?.FocusManager?.GetFocusedElement() as Control;
        // Commands can deliberately focus the canvas, a dialog or another field.
        // Neither a delayed close nor a subsequent pointer gesture may undo that.
        if (root is null || target is null || before is null || !OwnsFocus(before)
            || after is not null && !OwnsFocus(after)) return;
        var generation = ++_generation;
        Dispatcher.UIThread.Post(() =>
        {
            if (_generation != generation || IsOpen || !ReferenceEquals(_root, root)
                || !ReferenceEquals(TopLevel.GetTopLevel(this), root)
                || !ReferenceEquals(TopLevel.GetTopLevel(target), root)
                || !target.Focusable || !target.IsEffectivelyEnabled || !target.IsEffectivelyVisible
                || !ReferenceEquals(root.FocusManager?.GetFocusedElement(), after)) return;
            target.Focus(NavigationMethod.Unspecified);
        }, DispatcherPriority.Input);
    }
}
