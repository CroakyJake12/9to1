using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Write;
using Haven.UI;

namespace NineToOne.Web.Write;

/// <summary>
/// Browser input/lifetime composition over the original retained Write surface
/// and original scene renderer. No text layout, document model or editing logic
/// is recreated here. The owner supplies the real motion preference capability.
/// </summary>
public sealed class WriteRetainedSceneControl : Panel, IDisposable
{
    private readonly HavenSceneControl _scene;
    private WriteDocumentSurface? _surface;
    private WriteDocumentEditor? _editor;
    private bool _inputAllowed;
    private bool _restoreFocus;
    private bool _readOnly;
    private bool _workspaceLayoutLocked;
    private HavenBrush? _workspaceForeground;
    private bool _disposed;
    public event EventHandler? SelectionChanged;

    public WriteRetainedSceneControl(Func<bool> reduceMotion)
    {
        ArgumentNullException.ThrowIfNull(reduceMotion);
        _scene = new HavenSceneControl(new HavenAvaloniaImageResolver(),
            new HavenAvaloniaNativeControlResolver(), reduceMotion)
        {
            // Shared owner currently has no Web platform enum. Never claim Windows.
            Platform = HavenPlatform.Unknown,
            IsEnabled = false
        };
        Children.Add(_scene);
        AddHandler(KeyDownEvent, GateKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, GateKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(TextInputEvent, GateText, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, GatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, GatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, GatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void SetEditor(WriteDocumentEditor? editor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(editor, _editor)) return;
        if (_editor is not null) _editor.Changed -= OnEditorChanged;
        var hadFocus = _scene.IsKeyboardFocusWithin;
        if (_surface is not null) _surface.SelectionChanged -= OnSelectionChanged;
        _editor = editor;
        _scene.Root = null;
        _surface = null;
        if (editor is null) return;
        _surface = new WriteDocumentSurface();
        _surface.SetWorkspaceAppearance(_workspaceForeground);
        _surface.SetReadOnly(_readOnly);
        _surface.SetWorkspaceLayoutLocked(_workspaceLayoutLocked);
        _surface.SetEditor(editor);
        _surface.SelectionChanged += OnSelectionChanged;
        _surface.SetValue(HavenProperties.Enabled, _inputAllowed);
        _surface.Accessibility.Enabled = _inputAllowed;
        _scene.Root = _surface;
        editor.Changed += OnEditorChanged;
        if (_inputAllowed && hadFocus) _scene.FocusElement(_surface);
    }

    public void SetWorkspaceAppearance(HavenBrush? foreground)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Equals(_workspaceForeground, foreground)) return;
        _workspaceForeground = foreground;
        _surface?.SetWorkspaceAppearance(foreground);
    }

    public void SetReadOnly(bool readOnly)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _readOnly = readOnly;
        _surface?.SetReadOnly(readOnly);
    }

    public void SetWorkspaceLayoutLocked(bool locked)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_workspaceLayoutLocked == locked) return;
        _workspaceLayoutLocked = locked;
        _surface?.SetWorkspaceLayoutLocked(locked);
    }

    private void OnSelectionChanged(object? sender, EventArgs args) => SelectionChanged?.Invoke(this, args);

    public void SetInputAllowed(bool allowed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inputAllowed == allowed) return;
        if (!allowed) _restoreFocus = _scene.IsKeyboardFocusWithin;
        _inputAllowed = allowed;
        _scene.IsEnabled = allowed;
        if (_surface is null) return;
        _surface.SetValue(HavenProperties.Enabled, allowed);
        _surface.Accessibility.Enabled = allowed;
        if (!allowed)
        {
            // The original async clipboard code checks its captured router identity.
            // Renewing that router invalidates a pending paste before save/read-only.
            // Reattach the same retained node so this does not replace document state.
            _scene.Root = null;
            _scene.Root = _surface;
        }
        else if (_restoreFocus)
        {
            // Only restore an editor focus we actually held before temporary input suspension.
            // A title box/menu/toolbar action must keep its own keyboard focus.
            _scene.FocusElement(_surface);
            _restoreFocus = false;
        }
    }

    private void GateKey(object? sender, KeyEventArgs args) { if (!_inputAllowed) args.Handled = true; }
    private void GateText(object? sender, TextInputEventArgs args) { if (!_inputAllowed) args.Handled = true; }
    private void GatePointer(object? sender, PointerEventArgs args) { if (!_inputAllowed) args.Handled = true; }
    private void OnEditorChanged(object? sender, EventArgs args) => _surface?.InvalidateDocument();

    public void Dispose()
    {
        if (_disposed) return;
        if (_editor is not null) _editor.Changed -= OnEditorChanged;
        if (_surface is not null) _surface.SelectionChanged -= OnSelectionChanged;
        _scene.Root = null;
        _editor = null;
        _surface = null;
        _disposed = true;
    }
}
