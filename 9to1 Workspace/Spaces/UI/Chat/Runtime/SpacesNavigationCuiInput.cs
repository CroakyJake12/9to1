using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Spaces.Chat.Rendering;

/// <summary>Input/focus adapter for the authored navigation dialog. Native input
/// routing supplies no operation or persistence authority.</summary>
public sealed class SpacesNavigationCuiInput : IDisposable
{
    private readonly Control _root, _dialog;
    private readonly TextBox _text;
    private readonly Button _confirm, _cancel;
    private Control? _returnFocus;
    private long _generation;
    private bool _disposed;

    public SpacesNavigationCuiInput(Control root)
    {
        _root = root;
        var authored = SpacesShellCuiRuntime.AuthoredControls(root).ToArray();
        _dialog = authored.Single(control => control.Name == "sidebar-dialog");
        _text = authored.OfType<TextBox>().Single(control => control.Name == "sidebar-dialog-text");
        _confirm = authored.OfType<Button>().Single(control => control.Name == "confirm-sidebar-dialog");
        _cancel = authored.OfType<Button>().Single(control => control.Name == "cancel-sidebar-dialog");
        KeyboardNavigation.SetTabNavigation(_dialog, KeyboardNavigationMode.Cycle);
        _dialog.PropertyChanged += DialogChanged;
        _root.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
        _root.AttachedToVisualTree += Attached;
        _root.DetachedFromVisualTree += Detached;
    }

    private void Attached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (_dialog.IsVisible) OpenFocus();
    }
    private void DialogChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (_disposed || args.Property != Visual.IsVisibleProperty) return;
        if (_dialog.IsVisible) OpenFocus();
        else RestoreFocus();
    }
    private void OpenFocus()
    {
        var top = TopLevel.GetTopLevel(_root);
        if (top is null) return;
        var generation = ++_generation;
        var initial = top.FocusManager?.GetFocusedElement() as Control;
        var candidate = initial is not null && Owns(initial) ? initial
            : initial?.GetLogicalAncestors().OfType<Control>().FirstOrDefault(control => Owns(control) && control.Focusable);
        _returnFocus = candidate is not null && !_dialog.IsVisualAncestorOf(candidate) ? candidate : null;
        var returnFocus = _returnFocus;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || generation != _generation || TopLevel.GetTopLevel(_root) != top || !_dialog.IsEffectivelyVisible) return;
            var current = top.FocusManager?.GetFocusedElement();
            if (current is not null && !ReferenceEquals(current, initial) && !ReferenceEquals(current, returnFocus)) return;
            Control? first = _text.IsEffectivelyVisible ? _text : _dialog.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled);
            if (first is not { IsEffectivelyEnabled: true, Focusable: true }) return;
            first.Focus(NavigationMethod.Unspecified);
            if (ReferenceEquals(first, _text)) _text.SelectAll();
            first.BringIntoView();
        }, DispatcherPriority.Loaded);
    }
    private void RestoreFocus()
    {
        var generation = ++_generation;
        var target = _returnFocus; _returnFocus = null;
        var top = TopLevel.GetTopLevel(_root);
        var previous = top?.FocusManager?.GetFocusedElement() as Control;
        if (top is null || (previous is not null && !_dialog.IsVisualAncestorOf(previous))) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || generation != _generation || TopLevel.GetTopLevel(_root) != top || target is null ||
                !Owns(target) || !target.IsEffectivelyVisible || !target.IsEffectivelyEnabled || !target.Focusable) return;
            var current = top.FocusManager?.GetFocusedElement();
            if (current is not null && !ReferenceEquals(current, previous)) return;
            target.Focus(NavigationMethod.Unspecified); target.BringIntoView();
        }, DispatcherPriority.Loaded);
    }
    private void KeyDown(object? sender, KeyEventArgs args)
    {
        if (_disposed || args.Handled || !_dialog.IsEffectivelyVisible || !_dialog.IsEffectivelyEnabled) return;
        var focused = TopLevel.GetTopLevel(_root)?.FocusManager?.GetFocusedElement() as Control;
        if (focused is null || !_dialog.IsVisualAncestorOf(focused)) return;
        var action = args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None ? _cancel
            : args.Key == Key.Enter && args.KeyModifiers == KeyModifiers.Control ? _confirm : null;
        if (action is null) return;
        args.Handled = true;
        if (action.IsEffectivelyVisible && action.IsEffectivelyEnabled) action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private bool Owns(Control control) => ReferenceEquals(control, _root) || _root.IsVisualAncestorOf(control);
    private void Detached(object? sender, VisualTreeAttachmentEventArgs args) { ++_generation; _returnFocus = null; }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_generation; _returnFocus = null;
        _dialog.PropertyChanged -= DialogChanged;
        _root.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        _root.AttachedToVisualTree -= Attached;
        _root.DetachedFromVisualTree -= Detached;
    }
}
