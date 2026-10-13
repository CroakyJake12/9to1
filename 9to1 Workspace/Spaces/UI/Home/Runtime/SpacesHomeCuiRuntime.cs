using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Spaces.Rendering;

/// <summary>Presentation lifetime for the canonical, inline Manage Spaces editor.
/// Owns focus and keyboard intent only; all saves and cancellation go through the
/// authored buttons and their owning action dispatcher.</summary>
public static class SpacesHomeCuiRuntime
{
    public static IDisposable AttachEditor(Control root, Action<double>? viewportWidthChanged = null) =>
        new EditorPresentation(root, viewportWidthChanged);

    private sealed class EditorPresentation : IDisposable
    {
        private readonly Control _root;
        private readonly Control _editor;
        private readonly TextBox _name;
        private readonly Button _save;
        private readonly Button _cancel;
        private readonly Action<double>? _viewportWidthChanged;
        private Control? _returnFocus;
        private long _focusRevision;
        private bool _disposed;

        public EditorPresentation(Control root, Action<double>? viewportWidthChanged)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _viewportWidthChanged = viewportWidthChanged;
            var controls = AuthoredControls(root).ToArray();
            _editor = controls.Single(control => control.Name == "space-editor");
            _name = controls.OfType<TextBox>().Single(control => control.Name == "space-editor-name");
            _save = controls.OfType<Button>().Single(control => control.Name == "confirm-space-edit");
            _cancel = controls.OfType<Button>().Single(control => control.Name == "cancel-space-edit");
            _editor.PropertyChanged += EditorChanged;
            _root.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
            _root.DetachedFromVisualTree += Detached;
            _root.SizeChanged += SizeChanged;
            ReportViewport();
        }

        private void SizeChanged(object? sender, SizeChangedEventArgs args) => ReportViewport();
        private void ReportViewport()
        {
            if (!_disposed && _root.Bounds.Width > 0 && TopLevel.GetTopLevel(_root) is not null)
                _viewportWidthChanged?.Invoke(_root.Bounds.Width);
        }

        private void EditorChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (_disposed || args.Property != Visual.IsVisibleProperty) return;
            var revision = ++_focusRevision;
            var top = TopLevel.GetTopLevel(_root);
            if (top is null) return;
            var focused = top.FocusManager?.GetFocusedElement() as Control;
            if (_editor.IsVisible)
            {
                // Submenu items live in a popup visual tree. Retain their mounted
                // owning menu item so its normal close/focus restoration may settle.
                var initiatingControl = focused is not null && Owns(focused) ? focused
                    : focused?.GetLogicalAncestors().OfType<Control>().FirstOrDefault(control => Owns(control) && control.Focusable);
                _returnFocus = initiatingControl is not null && !_editor.IsVisualAncestorOf(initiatingControl)
                    ? initiatingControl : null;
                var returnFocus = _returnFocus;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed || revision != _focusRevision || TopLevel.GetTopLevel(_root) != top
                        || !_name.IsEffectivelyVisible || !_name.IsEffectivelyEnabled) return;
                    // Do not steal focus after another owner deliberately moved it.
                    var current = top.FocusManager?.GetFocusedElement();
                    if (current is not null && !ReferenceEquals(current, focused) && !ReferenceEquals(current, returnFocus)) return;
                    _name.Focus(NavigationMethod.Unspecified);
                    _name.SelectAll();
                    _name.BringIntoView();
                });
            }
            else
            {
                var target = _returnFocus;
                _returnFocus = null;
                if (focused is not null && !ReferenceEquals(focused, _editor) && !_editor.IsVisualAncestorOf(focused)) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed || revision != _focusRevision || TopLevel.GetTopLevel(_root) != top
                        || target is null || !Owns(target) || !target.IsEffectivelyVisible || !target.IsEffectivelyEnabled
                        || !target.Focusable) return;
                    var current = top.FocusManager?.GetFocusedElement();
                    if (current is not null && !ReferenceEquals(current, focused)) return;
                    target.Focus(NavigationMethod.Unspecified);
                    target.BringIntoView();
                });
            }
        }

        private void KeyDown(object? sender, KeyEventArgs args)
        {
            if (_disposed || args.Handled || !_editor.IsEffectivelyVisible || !_editor.IsEffectivelyEnabled) return;
            var focused = TopLevel.GetTopLevel(_root)?.FocusManager?.GetFocusedElement() as Control;
            if (focused is null || !Owns(focused) || !_editor.IsVisualAncestorOf(focused)) return;
            Button? action = args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None ? _cancel
                : args.Key == Key.Enter && args.KeyModifiers == KeyModifiers.Control ? _save : null;
            if (action is null) return;
            args.Handled = true;
            if (action.IsEffectivelyVisible && action.IsEffectivelyEnabled)
                action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private bool Owns(Control control) => ReferenceEquals(control, _root) || _root.IsVisualAncestorOf(control);
        private static IEnumerable<Control> AuthoredControls(Control root)
        {
            var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<Control>(); pending.Push(root);
            while (pending.TryPop(out var control))
            {
                if (!seen.Add(control)) continue;
                yield return control;
                // Canonical containers retain inactive authored children outside
                // the native layout tree, preserving their state and identity.
                if (control is CuiContainerControl container)
                    foreach (var child in container.AuthoredChildren) pending.Push(child);
                foreach (var child in control.GetLogicalChildren().OfType<Control>()) pending.Push(child);
                foreach (var child in control.GetVisualChildren().OfType<Control>()) pending.Push(child);
            }
        }
        private void Detached(object? sender, VisualTreeAttachmentEventArgs args) { ++_focusRevision; _returnFocus = null; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; ++_focusRevision; _returnFocus = null;
            _editor.PropertyChanged -= EditorChanged;
            _root.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
            _root.DetachedFromVisualTree -= Detached;
            _root.SizeChanged -= SizeChanged;
        }
    }
}
