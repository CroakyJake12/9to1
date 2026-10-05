using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Haven.Application;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Present;
using Haven.UI;

namespace NineToOne.Web.Productivity.Present;

/// <summary>Retains the actual owning canvas, scene and editor; this class supplies only event/lifetime/input composition.</summary>
public sealed class PresentRetainedSceneControl : Panel, IDisposable
{
    private readonly HavenSceneControl _scene;
    private readonly PresentSlideCanvas _canvas = new();
    private PresentEditor? _editor;
    private Guid? _renderedSlide;
    private bool _allowed, _disposed;
    public event EventHandler? LiveTextPreviewed;

    public PresentRetainedSceneControl(Func<bool> reduceMotion)
    {
        ArgumentNullException.ThrowIfNull(reduceMotion);
        _scene = new HavenSceneControl(new HavenAvaloniaImageResolver(), new HavenAvaloniaNativeControlResolver(), reduceMotion)
        { Platform = HavenPlatform.Unknown, IsEnabled = false };
        Children.Add(_scene);
        AddHandler(KeyDownEvent, GateKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, GateKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(TextInputEvent, GateText, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, GatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, GatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, GatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        _canvas.SelectionRequested += Select;
        _canvas.SelectionSetRequested += SelectMany;
        _canvas.MoveSelectionRequested += Move;
        _canvas.TransformSelectionRequested += Transform;
        _canvas.VectorHandleMoveRequested += MoveVector;
        _canvas.TitleTextPreviewRequested += PreviewTitle;
        _canvas.ElementTextPreviewRequested += PreviewText;
        _canvas.TextEditCommitRequested += Commit;
        _canvas.TextEditCancelRequested += Cancel;
    }

    public void SetEditor(PresentEditor? editor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(editor, _editor))
        {
            if (_editor is not null) _editor.Changed -= Changed;
            _scene.Root = null; _editor = editor; _renderedSlide = null;
            if (editor is null) return;
            editor.Changed += Changed;
            _scene.Root = _canvas;
        }
        if (_editor is not null && _renderedSlide != _editor.Selection.SlideId) Refresh();
    }
    public void SetInputAllowed(bool allowed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_allowed == allowed) return;
        _allowed = allowed; _scene.IsEnabled = allowed;
        _canvas.SetValue(HavenProperties.Enabled, allowed); _canvas.Accessibility.Enabled = allowed;
        if (!allowed && _scene.Root is not null)
        { _scene.Root = null; _scene.Root = _canvas; } // Invalidates original scene's captured asynchronous paste router.
        else if (allowed && _scene.Root is not null) _scene.FocusElement(_canvas);
    }
    private void Refresh()
    {
        if (_editor is null) return;
        _canvas.SetSlide(_editor.Document, _editor.SelectedSlide, _editor.Selection.ElementIds);
        _renderedSlide = _editor.Selection.SlideId;
        if (_allowed) _scene.FocusElement(_canvas);
    }
    private void Changed(object? sender, EventArgs args) => Refresh();
    private void Select(Guid? id) { if (!_allowed || _editor is null) return; _editor.SelectElements(id is { } value ? [value] : []); Refresh(); }
    private void SelectMany(IReadOnlyCollection<Guid> ids) { if (!_allowed || _editor is null) return; _editor.SelectElements(ids); Refresh(); }
    private void Move(double x, double y) { if (_allowed) _editor?.MoveSelection(x, y); }
    private void Transform(double x, double y, double width, double height, double rotation)
    { if (_allowed) _editor?.TransformSelection(x, y, width, height, rotation); }
    private void MoveVector(Guid elementId, Guid nodeId, PresentVectorHandleKind kind, double x, double y)
    {
        if (!_allowed || _editor is null) return;
        _editor.UpdateCustomShape(_editor.Selection.SlideId, elementId, vector =>
        { if (kind == PresentVectorHandleKind.Node) vector.MoveNode(nodeId, x, y); else vector.MoveControlPoint(nodeId, kind == PresentVectorHandleKind.Control1 ? 1 : 2, x, y); });
    }
    private void PreviewTitle(string text)
    { if (_allowed && _editor?.PreviewSlideTitle(_editor.Selection.SlideId, text) == true) LiveTextPreviewed?.Invoke(this, EventArgs.Empty); }
    private void PreviewText(Guid id, string text)
    { if (_allowed && _editor?.PreviewElementText(_editor.Selection.SlideId, id, text) == true) LiveTextPreviewed?.Invoke(this, EventArgs.Empty); }
    private void Commit(object? sender, EventArgs args) { if (_allowed) _editor?.CommitLiveTextEdit(); }
    private void Cancel(object? sender, EventArgs args) { if (_allowed) _editor?.CancelLiveTextEdit(); }
    private void GateKey(object? sender, KeyEventArgs args) { if (!_allowed) args.Handled = true; }
    private void GateText(object? sender, TextInputEventArgs args) { if (!_allowed) args.Handled = true; }
    private void GatePointer(object? sender, PointerEventArgs args) { if (!_allowed) args.Handled = true; }
    public void Dispose()
    {
        if (_disposed) return;
        if (_editor is not null) _editor.Changed -= Changed;
        _scene.Root = null; _editor = null; LiveTextPreviewed = null; _disposed = true;
    }
}
