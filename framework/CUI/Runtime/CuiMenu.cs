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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _generation++; _root = TopLevel.GetTopLevel(this);
        _root?.AddHandler(GotFocusEvent, RootFocused, RoutingStrategies.Bubble, handledEventsToo: true);
        RememberOutsideFocus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _generation++; _root?.RemoveHandler(GotFocusEvent, RootFocused);
        _root = null; _outsideFocus = null;
        base.OnDetachedFromVisualTree(e);
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
