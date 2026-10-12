using Avalonia.Controls;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Input.GestureRecognizers;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Present;
using Haven.UI;

namespace NineToOne.Web.Productivity.Present;

public interface IPresentCanvasTextCommands
{
    bool IsTextEditing { get; }
    bool HasSelectedText { get; }
    Task<bool> ExecuteTextCommandAsync(string command, CancellationToken cancellationToken);
}

/// <summary>Retains the actual owning canvas, scene and editor; this class supplies only event/lifetime/input composition.</summary>
public sealed class PresentRetainedSceneControl : Panel, IDisposable, IPresentCanvasTextCommands
{
    private readonly HavenSceneControl _scene;
    private readonly PresentSlideCanvas _canvas = new();
    private PresentEditor? _editor;
    private Guid? _renderedSlide;
    private bool _allowed, _disposed;
    private bool _restoreCanvasFocus;
    private ScrollViewer? _viewport;
    private double _zoom = 1;
    private bool _legacyZoom = true;
    private bool _fitToWindow = true;
    private IPointer? _panPointer;
    private Point _panStart;
    private Vector _panOffset;
    private bool _spacePressed;
    private double? _pinchStartZoom;
    private long _zoomGeneration;
    private Window? _inputWindow;
    private Guid[] _renderedSelection = [];
    private Point? _pointerStartInWindow;
    private bool _pointerMovedInWindow;
    public bool IsTextEditing => _canvas.IsTextEditing;
    public bool HasSelectedText => !string.IsNullOrEmpty(_canvas.SelectedText);
    public PresentInteractionState InteractionState => _panPointer is not null ? PresentInteractionState.CanvasPanning
        : _pinchStartZoom is not null ? PresentInteractionState.CanvasZooming : _canvas.InteractionState;
    public Guid? HoveredObjectId => _canvas.HoveredObjectId;
    public IReadOnlyList<PresentObjectInteractionGeometry> LiveGeometry => _canvas.LiveGeometry;
    public double EffectiveZoom => _editor is null ? 1 : _canvas.PaperBounds.Width / (_editor.Document.SlideSize.WidthInches * 96);
    public event EventHandler? InteractionChanged;
    public event EventHandler? GeometryPreviewed;
    public event Action<string, double, double, double, double>? ShapeInsertionRequested;
    public event Action<double>? ZoomRequested;
    public event EventHandler? LiveTextPreviewed;
    public event EventHandler? SelectionChanged;
    public static Action<HavenDrawingContext, Haven.Core.PresentDocument, Haven.Core.PresentSlide, HavenRect, double> CreatePreviewRenderer()
    {
        var preview = new PresentSlideCanvas();
        return preview.DrawPreview;
    }

    public PresentRetainedSceneControl(Func<bool> reduceMotion)
    {
        ArgumentNullException.ThrowIfNull(reduceMotion);
        _scene = new HavenSceneControl(new HavenAvaloniaImageResolver(), new HavenAvaloniaNativeControlResolver(), reduceMotion)
        { Platform = HavenPlatform.Unknown, IsEnabled = false };
        Children.Add(_scene);
        // The CUI workspace owns application chrome; only the authored slide is paper.
        _canvas.SetValue(HavenProperties.Background, "Transparent");
        _canvas.SetValue(HavenProperties.BorderWidth, HavenLength.Px(0));
        _canvas.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(0)));
        _canvas.SetValue(HavenProperties.MinHeight, HavenLength.Px(0));
        _canvas.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        AddHandler(KeyDownEvent, GateKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, GateKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(TextInputEvent, GateText, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, GatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, GatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, GatePointer, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, Wheel, RoutingStrategies.Tunnel);
        GestureRecognizers.Add(new PinchGestureRecognizer());
        LostFocus += (_, _) => { if (!IsKeyboardFocusWithin) CancelInteraction(); };
        Pinch += OnPinch;
        PinchEnded += (_, _) => { _pinchStartZoom = null; InteractionChanged?.Invoke(this, EventArgs.Empty); };
        _canvas.InteractionChanged += (_, _) => InteractionChanged?.Invoke(this, EventArgs.Empty);
        _canvas.GeometryPreviewed += (_, _) => GeometryPreviewed?.Invoke(this, EventArgs.Empty);
        _canvas.ShapeInsertionRequested += (preset, x, y, width, height) =>
        { if (_allowed) ShapeInsertionRequested?.Invoke(preset, x, y, width, height); };
        _canvas.SelectionRequested += Select;
        _canvas.SelectionSetRequested += SelectMany;
        _canvas.MoveSelectionRequested += Move;
        _canvas.TransformSelectionRequested += Transform;
        _canvas.VectorHandleMoveRequested += MoveVector;
        _canvas.TitleTextPreviewRequested += PreviewTitle;
        _canvas.ElementTextPreviewRequested += PreviewText;
        _canvas.TextEditCommitRequested += Commit;
        _canvas.TextEditCancelRequested += Cancel;
        ContextRequested += (_, args) =>
        {
            if (!_allowed) { args.Handled = true; return; }
            if (args.TryGetPosition(_scene, out var point))
                _canvas.PrepareContext(new HavenPoint(point.X - _canvas.Bounds.X, point.Y - _canvas.Bounds.Y));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public void BeginShapeInsertion(string presetId, DocumentVectorShape preview)
    {
        if (!_allowed || _editor is null) return;
        _canvas.BeginShapeInsertion(presetId, preview); _scene.FocusElement(_canvas);
    }

    public void CancelInteraction()
    {
        _canvas.CancelInteraction(); _spacePressed = false; _pinchStartZoom = null;
        var pointer = _panPointer; _panPointer = null; pointer?.Capture(null);
        _scene.SetCursorOverride(null);
        InteractionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> ExecuteTextCommandAsync(string command, CancellationToken cancellationToken)
    {
        if (!_allowed || !_canvas.IsTextEditing) return false;
        if (command == "SelectAllText") return _canvas.KeyDown(HavenKey.A, new HavenInputModifiers(Control: true));
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return false;
        var editor = _editor; var slide = _renderedSlide; var textContext = _canvas.TextEditContext;
        if (command == "Paste")
        {
            var text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
            cancellationToken.ThrowIfCancellationRequested();
            return !_disposed && _allowed && ReferenceEquals(editor, _editor) && slide == _renderedSlide
                && _canvas.IsTextEditing && Equals(textContext, _canvas.TextEditContext) && _canvas.Paste(text);
        }
        var selected = _canvas.Copy();
        if (selected is null) return false;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, selected);
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || !_allowed || !ReferenceEquals(editor, _editor) || slide != _renderedSlide || !_canvas.IsTextEditing
            || !Equals(textContext, _canvas.TextEditContext)) return false;
        if (command == "Cut" && _canvas.SelectedText == selected) _canvas.DeleteSelection();
        return true;
    }

    public void SetEditor(PresentEditor? editor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(editor, _editor))
        {
            CancelInteraction(); ++_zoomGeneration; _canvas.CommitTextEdit();
            if (_editor is not null) _editor.Changed -= Changed;
            _scene.Root = null; _editor = editor; _renderedSlide = null;
            if (editor is null) return;
            editor.Changed += Changed;
            _scene.Root = _canvas;
        }
        if (_editor is not null && (_renderedSlide != _editor.Selection.SlideId || !_renderedSelection.SequenceEqual(_editor.Selection.ElementIds))) Refresh();
    }
    public void SetZoom(double zoom)
    {
        if (_legacyZoom && _zoom == zoom) return;
        _legacyZoom = true; _zoom = double.IsFinite(zoom) ? Math.Clamp(zoom, .25, 4) : 1;
        CancelInteraction(); _canvas.ManualScale = null; _canvas.ViewScale = Math.Min(_zoom, 1); UpdateViewport();
    }
    public void SetZoom(double zoom, bool fitToWindow)
    {
        var next = double.IsFinite(zoom) ? Math.Clamp(zoom, .25, 4) : 1;
        if (!_legacyZoom && _zoom == next && _fitToWindow == fitToWindow) return;
        _legacyZoom = false; _zoom = next; _fitToWindow = fitToWindow;
        CancelInteraction(); _canvas.ManualScale = fitToWindow ? null : next; _canvas.ViewScale = 1;
        UpdateViewport(); InvalidateArrange(); _scene.InvalidateMeasure();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _inputWindow = TopLevel.GetTopLevel(this) as Window;
        if (_inputWindow is not null) _inputWindow.Deactivated += WindowDeactivated;
        _viewport = this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        if (_viewport is not null) { _viewport.PropertyChanged += ViewportChanged; UpdateViewport(); }
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelInteraction();
        if (_inputWindow is not null) _inputWindow.Deactivated -= WindowDeactivated;
        _inputWindow = null;
        if (_viewport is not null) _viewport.PropertyChanged -= ViewportChanged;
        _viewport = null; base.OnDetachedFromVisualTree(e);
    }
    private void WindowDeactivated(object? sender, EventArgs args) => CancelInteraction();
    private void ViewportChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    { if (args.Property == ScrollViewer.ViewportProperty) { _canvas.CancelInteraction(); UpdateViewport(); } }
    private void UpdateViewport()
    {
        // Fit is a constrained stage, not a measured scroll extent. An explicit
        // content size at fit creates a feedback loop when scrollbars appear.
        if (_legacyZoom ? _zoom <= 1 : _fitToWindow) { Width = double.NaN; Height = double.NaN; return; }
        if (_viewport is not { Viewport.Width: > 0, Viewport.Height: > 0 }) return;
        if (_legacyZoom) { Width = _viewport.Viewport.Width * _zoom; Height = _viewport.Viewport.Height * _zoom; }
        else if (_editor is not null)
        {
            Width = Math.Max(_viewport.Viewport.Width, _editor.Document.SlideSize.WidthInches * 96 * _zoom + 32);
            Height = Math.Max(_viewport.Viewport.Height, _editor.Document.SlideSize.HeightInches * 96 * _zoom + 32);
        }
    }
    public void SetInputAllowed(bool allowed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_allowed == allowed) return;
        if (!allowed) { _restoreCanvasFocus = _scene.IsKeyboardFocusWithin; CancelInteraction(); }
        _allowed = allowed; _scene.IsEnabled = allowed;
        _canvas.SetValue(HavenProperties.Enabled, allowed); _canvas.Accessibility.Enabled = allowed;
        if (!allowed && _scene.Root is not null)
        { _scene.Root = null; _scene.Root = _canvas; } // Invalidates original scene's captured asynchronous paste router.
        else if (allowed && _restoreCanvasFocus && _scene.Root is not null) _scene.FocusElement(_canvas);
    }
    private void Refresh()
    {
        if (_disposed || _editor is null) return;
        var originalEditor = _editor;
        var originalSlide = originalEditor.Selection.SlideId;
        var hadCanvasFocus = _scene.IsKeyboardFocusWithin;
        if (_renderedSlide != originalSlide) _canvas.CommitTextEdit();
        _canvas.SetSlide(originalEditor.Document, originalEditor.SelectedSlide, originalEditor.Selection.ElementIds);
        if (_disposed || !ReferenceEquals(originalEditor, _editor) ||
            originalEditor.Selection.SlideId != originalSlide) return;
        _renderedSlide = originalSlide;
        _renderedSelection = originalEditor.Selection.ElementIds.ToArray();
        // Model refresh must not steal focus from the native speaker-notes field.
        // Preserve canvas focus only while the SAME scene still owns it after notifications.
        if (_allowed && hadCanvasFocus && _scene.IsKeyboardFocusWithin && ReferenceEquals(_scene.Root, _canvas))
            _scene.FocusElement(_canvas);
    }
    private void Changed(object? sender, EventArgs args) => Refresh();
    private void Select(Guid? id) { if (!_allowed || _editor is null) return; _editor.SelectElements(id is { } value ? [value] : []); Refresh(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    private void SelectMany(IReadOnlyCollection<Guid> ids) { if (!_allowed || _editor is null) return; _editor.SelectElements(ids); Refresh(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    private void Move(double x, double y) { if (_allowed && _pointerMovedInWindow) _editor?.MoveSelection(x, y); }
    private void Transform(double x, double y, double width, double height, double rotation)
    { if (_allowed && _pointerMovedInWindow) _editor?.TransformSelection(x, y, width, height, rotation); }
    private void MoveVector(Guid elementId, Guid nodeId, PresentVectorHandleKind kind, double x, double y)
    {
        if (!_allowed || !_pointerMovedInWindow || _editor is null) return;
        _editor.UpdateCustomShape(_editor.Selection.SlideId, elementId, vector =>
        { if (kind == PresentVectorHandleKind.Node) vector.MoveNode(nodeId, x, y); else vector.MoveControlPoint(nodeId, kind == PresentVectorHandleKind.Control1 ? 1 : 2, x, y); });
    }
    private void PreviewTitle(string text)
    { if (_allowed && _editor?.PreviewSlideTitle(_editor.Selection.SlideId, text) == true) LiveTextPreviewed?.Invoke(this, EventArgs.Empty); }
    private void PreviewText(Guid id, string text)
    { if (_allowed && _editor?.PreviewElementText(_editor.Selection.SlideId, id, text) == true) LiveTextPreviewed?.Invoke(this, EventArgs.Empty); }
    private void Commit(object? sender, EventArgs args) { if (_allowed) _editor?.CommitLiveTextEdit(); }
    private void Cancel(object? sender, EventArgs args) { if (_allowed) _editor?.CancelLiveTextEdit(); }
    private void GateKey(object? sender, KeyEventArgs args)
    {
        if (!_allowed) { args.Handled = true; return; }
        if (args.Key == Key.Space && !_canvas.IsTextEditing)
        {
            _spacePressed = args.RoutedEvent == KeyDownEvent;
            _scene.SetCursorOverride(_spacePressed ? HavenCursor.Grab : null); args.Handled = true;
        }
        if (args.Key == Key.Escape && (_panPointer is not null || _pinchStartZoom is not null))
        { CancelInteraction(); args.Handled = true; }
    }
    private void GateText(object? sender, TextInputEventArgs args) { if (!_allowed) args.Handled = true; }
    private void GatePointer(object? sender, PointerEventArgs args)
    {
        if (!_allowed) { args.Handled = true; return; }
        if (_viewport is not null && args is PointerPressedEventArgs pressed
            && (pressed.GetCurrentPoint(this).Properties.IsMiddleButtonPressed || _spacePressed))
        {
            _canvas.CancelInteraction(); _panPointer = args.Pointer;
            _panStart = args.GetPosition(_viewport); _panOffset = _viewport.Offset;
            args.Pointer.Capture(this); _scene.SetCursorOverride(HavenCursor.Grabbing);
            InteractionChanged?.Invoke(this, EventArgs.Empty); args.Handled = true; return;
        }
        if (_panPointer == args.Pointer && _viewport is not null)
        {
            var delta = args.GetPosition(_viewport) - _panStart;
            _viewport.Offset = _panOffset - delta;
            if (args is PointerReleasedEventArgs)
            {
                _panPointer = null; args.Pointer.Capture(null);
                _scene.SetCursorOverride(_spacePressed ? HavenCursor.Grab : null);
                InteractionChanged?.Invoke(this, EventArgs.Empty);
            }
            args.Handled = true; return;
        }
        var position = args.GetPosition(TopLevel.GetTopLevel(this));
        if (args is PointerPressedEventArgs) { _pointerStartInWindow = position; _pointerMovedInWindow = false; }
        else if (_pointerStartInWindow is { } start && Math.Pow(position.X - start.X, 2) + Math.Pow(position.Y - start.Y, 2) > 1) _pointerMovedInWindow = true;
        // A layout adjustment under a stationary pointer is not an authored drag.
        // The routed release reaches this tunnel before the owning canvas commits.
        if (args is PointerReleasedEventArgs) _pointerStartInWindow = null;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs args)
    { base.OnPointerCaptureLost(args); if (_panPointer == args.Pointer) CancelInteraction(); }

    private void Wheel(object? sender, PointerWheelEventArgs args)
    {
        if (!_allowed || ZoomRequested is null || !args.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (Math.Abs(args.Delta.Y) < .00001) return;
        RequestZoom(Math.Clamp(EffectiveZoom * Math.Pow(1.12, args.Delta.Y), .25, 4), args.GetPosition(_scene), args.GetPosition(TopLevel.GetTopLevel(this)));
        args.Handled = true;
    }
    private void OnPinch(object? sender, PinchEventArgs args)
    {
        if (!_allowed || ZoomRequested is null) return;
        var startZoom = _pinchStartZoom ?? EffectiveZoom;
        var point = this.TranslatePoint(args.ScaleOrigin, _scene) ?? args.ScaleOrigin;
        var windowPoint = this.TranslatePoint(args.ScaleOrigin, TopLevel.GetTopLevel(this)!) ?? args.ScaleOrigin;
        RequestZoom(Math.Clamp(startZoom * args.Scale, .25, 4), point, windowPoint);
        _pinchStartZoom = startZoom;
        args.Handled = true; InteractionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void RequestZoom(double zoom, Point scenePoint, Point windowPoint)
    {
        var paper = _canvas.PaperBounds;
        var nx = (scenePoint.X - _canvas.Bounds.X - paper.X) / paper.Width;
        var ny = (scenePoint.Y - _canvas.Bounds.Y - paper.Y) / paper.Height;
        var editor = _editor; var slide = _renderedSlide;
        var generation = ++_zoomGeneration;
        CancelInteraction(); ZoomRequested?.Invoke(zoom);
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !_allowed || generation != _zoomGeneration || _viewport is null
                || !ReferenceEquals(editor, _editor) || slide != _renderedSlide || TopLevel.GetTopLevel(this) is not { } topLevel) return;
            _viewport.UpdateLayout();
            var next = _canvas.PaperBounds;
            var anchor = _scene.TranslatePoint(new Point(_canvas.Bounds.X + next.X + nx * next.Width,
                _canvas.Bounds.Y + next.Y + ny * next.Height), topLevel);
            if (anchor is { } actual) _viewport.Offset += actual - windowPoint;
        }, DispatcherPriority.Loaded);
    }
    public void Dispose()
    {
        if (_disposed) return;
        CancelInteraction(); ++_zoomGeneration;
        if (_editor is not null) _editor.Changed -= Changed;
        _scene.Root = null; _editor = null; LiveTextPreviewed = null; _disposed = true;
    }
}
