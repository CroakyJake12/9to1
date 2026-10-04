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
    private bool _focusInitialEditorOnEnable;
    private bool _disposed;

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
        _editor = editor;
        _focusInitialEditorOnEnable = false;
        _scene.Root = null;
        _surface = null;
        if (editor is null) return;
        _surface = new WriteDocumentSurface();
        _surface.SetEditor(editor);
        _surface.SetValue(HavenProperties.Enabled, _inputAllowed);
        _surface.Accessibility.Enabled = _inputAllowed;
        _scene.Root = _surface;
        editor.Changed += OnEditorChanged;
        if (_inputAllowed) _scene.FocusElement(_surface);
        else _focusInitialEditorOnEnable = true;
    }

    public void SetInputAllowed(bool allowed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inputAllowed == allowed) return;
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
        else if (_focusInitialEditorOnEnable)
        {
            // An initial attach may happen while Create/import is busy. Consume
            // that single real owner focus request when input first becomes legal.
            // An unchanged editor during later Save has no pending request, so
            // re-enabling it preserves the current toolbar focus and viewport.
            _focusInitialEditorOnEnable = false;
            _scene.FocusElement(_surface);
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
        _scene.Root = null;
        _editor = null;
        _focusInitialEditorOnEnable = false;
        _surface = null;
        _disposed = true;
    }
}
