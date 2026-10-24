using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using System.Text.Json;

namespace HavenOS.Apps.Canvas;

/// <summary>Original native pointer selection and movement preview over retained Rnote IDs.
/// No pointer callback mutates a document, serialises Files or supplies actor/permission data.</summary>
public sealed class CanvasNativeSelectionInput : ICanvasSelectionInput, IDisposable
{
    private const double MaximumMove = 1_000_000;
    private readonly CanvasNativeViewport _viewport;
    private readonly CanvasRnoteDocument _document;
    private readonly CanvasToolState _tools;
    private readonly CanvasNativeEraserContext? _context;
    private readonly Func<bool> _isAvailable;
    private readonly CanvasOriginalWorkOwner _originalWork = new();
    private readonly CanvasStrokeSelectionSnapshot? _snapshot;
    private readonly IReadOnlyDictionary<Guid, CanvasStrokeSelectionEntry> _entries;
    private readonly string? _unavailable;
    private IPointer? _pointer;
    private Point _pressDocument;
    private Point _pressViewport;
    private Guid? _target;
    private CanvasSelectionPath? _feedbackPath;
    private double _deltaX, _deltaY;
    private bool _retiring, _disposed, _submitted, _releasing;
    public event EventHandler? Changed;
    public string Status { get; private set; } = "Select a stroke. Drag or use the arrows to preview a move, then review it in Home.";
    public string FeedbackStatus { get; private set; } = "Select a stroke to show its retained donor path.";
    public bool HasSubmittedOperation => _submitted;
    public bool HasTarget => _target is { } id && _entries.ContainsKey(id);
    public string TargetSummary => _target is { } id && _entries.TryGetValue(id, out var entry) ? entry.Summary : "No stroke selected";
    public string MoveSummary => HasMovePreview ? $"Move preview: X {_deltaX:0.##}, Y {_deltaY:0.##}. The document is unchanged until Home commits." : "No move preview";
    public bool IsAvailable => !_retiring && !_disposed && !_submitted && _unavailable is null && _snapshot is not null &&
        _isAvailable() && _document.Identity == (_snapshot.ArtifactId, _snapshot.RevisionId);
    public string CapabilityReason => _unavailable ?? (_submitted ? "Check the submitted edit in Home; reopen its committed revision." : Status);
    public bool CanCapture => IsAvailable && _tools.Selected == CanvasPrimaryTool.Select;
    public bool CanMove => CanEdit && _target is { } id && _entries[id].CanTranslate;
    public bool CanDelete => CanEdit && _target is { } id && _entries[id].CanDelete;
    public bool HasMovePreview => HasTarget && (_deltaX != 0 || _deltaY != 0);
    private bool CanEdit => CanCapture && HasTarget && _context is not null && _context.IsAvailable() && _document.Identity == _context.Identity;

    public CanvasNativeSelectionInput(CanvasNativeViewport viewport, CanvasRnoteDocument document, CanvasToolState tools,
        CanvasNativeEraserContext? context, Func<bool> isAvailable)
    {
        ArgumentNullException.ThrowIfNull(viewport); ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(tools); ArgumentNullException.ThrowIfNull(isAvailable);
        _viewport = viewport; _document = document; _tools = tools; _context = context; _isAvailable = isAvailable;
        if (context is not null && context.Identity != document.Identity)
            throw new InvalidDataException("Selection and the original edit port must retain the same Canvas revision.");
        try
        {
            _snapshot = document.CaptureStrokeSelection(document.Identity.RevisionId);
            _entries = _snapshot.Strokes.ToDictionary(value => value.StrokeId);
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException)
        {
            _unavailable = error.Message; Status = error.Message; _entries = new Dictionary<Guid, CanvasStrokeSelectionEntry>();
        }
        viewport.PointerPressed += Pressed; viewport.PointerMoved += Moved; viewport.PointerReleased += Released;
        viewport.PointerCaptureLost += CaptureLost; viewport.DetachedFromVisualTree += Detached;
        viewport.ViewChanged += ViewChanged; tools.Changed += ToolChanged;
    }

    public void ChooseTarget(int direction)
    {
        RequireCapture();
        var strokes = _snapshot!.Strokes;
        if (strokes.Count == 0) { ClearSelection("There are no visible mapped strokes to select."); return; }
        var index = _target is { } id ? strokes.ToList().FindIndex(value => value.StrokeId == id) : -1;
        index = index < 0 ? (direction < 0 ? strokes.Count - 1 : 0) : (index + (direction < 0 ? -1 : 1) + strokes.Count) % strokes.Count;
        SetTarget(strokes[index].StrokeId);
    }
    public void ClearTarget() { RequireCapture(); CancelGesture(); ClearSelection("Selection cleared. The document is unchanged."); }
    public void AdjustMove(double deltaX, double deltaY)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY)) throw new ArgumentException("Move offsets must be finite.");
        if (!CanMove) throw new NotSupportedException("Choose a writable, unlocked sample stroke to preview movement. Split-path translation requires its exact donor transform support.");
        SetMove(_deltaX + deltaX, _deltaY + deltaY);
    }
    public Task RequestMoveAsync(CancellationToken cancellationToken = default)
    {
        if (!CanMove || !HasMovePreview) throw new NotSupportedException("Preview a nonzero move on an editable selected stroke first.");
        var intent = _context!.CaptureEdit(_target!.Value, CanvasStrokeEditKind.Translate, _deltaX, _deltaY);
        return SubmitAsync(intent, cancellationToken);
    }
    public Task RequestDeleteAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDelete) throw new NotSupportedException("Choose an editable selected stroke first.");
        var intent = _context!.CaptureEdit(_target!.Value, CanvasStrokeEditKind.Delete);
        return SubmitAsync(intent, cancellationToken);
    }
    private Task SubmitAsync(CanvasStrokeEditIntent intent, CancellationToken cancellationToken)
    {
        // Capture target, revision and offsets before any asynchronous Home review. A later
        // focus/selection can neither retarget nor duplicate an uncertain original request.
        if (!CanEdit) throw new UnauthorizedAccessException("The original selection edit owner is no longer current.");
        _submitted = true; CancelGesture(); Status = "Review this exact selected-stroke edit in Home."; Notify();
        return _originalWork.RunOriginalAsync(async token =>
        {
            try
            {
                await _originalWork.ObserveOriginalAsync(_context!.RequestTarget(intent, token));
                Status = "The selected-stroke edit was submitted. Home determines the committed revision.";
            }
            catch { Status = "Check Home for this selected-stroke request before retrying; its outcome is retained."; throw; }
            finally { Notify(); }
        }, cancellationToken);
    }

    private void Pressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.Handled || !CanCapture || _pointer is not null) return;
        var point = args.GetCurrentPoint(_viewport);
        if (!point.Properties.IsLeftButtonPressed || _viewport.ToDocumentPoint(point.Position) is not { } documentPoint) return;
        try
        {
            var id = _document.PreviewSelectionTarget(documentPoint.X, documentPoint.Y, _snapshot!.RevisionId);
            if (id is null) { ClearSelection("No stroke at this position. The document is unchanged."); args.Handled = true; return; }
            SetTarget(id.Value);
            if (CanMove)
            {
                _pointer = args.Pointer; _pressDocument = documentPoint; _pressViewport = point.Position;
                args.Pointer.Capture(_viewport); args.PreventGestureRecognition();
            }
            args.Handled = true;
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException)
        { CancelGesture(); Status = error.Message; Notify(); args.Handled = true; }
    }
    private void Moved(object? sender, PointerEventArgs args)
    {
        if (_pointer != args.Pointer) return;
        if (!CanMove) { CancelMove("The selected tool, document or edit access changed. No move was submitted."); return; }
        if (_viewport.ToDocumentPoint(args.GetPosition(_viewport)) is not { } point)
        { CancelMove("The pointer left the displayed document. No move was submitted."); return; }
        SetMove(point.X - _pressDocument.X, point.Y - _pressDocument.Y); args.Handled = true;
    }
    private void Released(object? sender, PointerReleasedEventArgs args)
    {
        if (_pointer != args.Pointer) return;
        try
        {
            if (!CanMove || _viewport.ToDocumentPoint(args.GetPosition(_viewport)) is not { } point)
            { CancelMove("The selection move context changed. No move was submitted."); return; }
            var end = args.GetPosition(_viewport);
            Vector movement = end - _pressViewport;
            if (movement.Length < 3) { _deltaX = _deltaY = 0; Status = TargetSummary; Notify(); }
            else SetMove(point.X - _pressDocument.X, point.Y - _pressDocument.Y);
        }
        finally { CancelGesture(); args.Handled = true; }
    }
    private void SetTarget(Guid id)
    {
        if (!_entries.ContainsKey(id)) throw new InvalidDataException("The resolved donor stroke has no retained current selection metadata.");
        _target = id; _deltaX = _deltaY = 0; _feedbackPath = null;
        try
        {
            _feedbackPath = _document.CaptureSelectionPath(id, _snapshot!.RevisionId);
            FeedbackStatus = "The selected donor path is outlined. A solid shifted contour previews an uncommitted move.";
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException or JsonException)
        { FeedbackStatus = "Selection contour unavailable: " + error.Message; }
        Status = _entries[id].IsLocked ? "This selected stroke is locked; editing is unavailable." : TargetSummary;
        Notify();
    }
    private void SetMove(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > MaximumMove || Math.Abs(y) > MaximumMove)
        { CancelMove("The move preview exceeds its finite interaction limit. No move was submitted."); return; }
        _deltaX = x; _deltaY = y; Status = MoveSummary; Notify();
    }
    private void ClearSelection(string reason)
    { _target = null; _feedbackPath = null; _deltaX = _deltaY = 0; Status = reason; FeedbackStatus = "No stroke selected."; Notify(); }
    private void CancelGesture() { var pointer = _pointer; _pointer = null; _releasing = true; try { pointer?.Capture(null); } finally { _releasing = false; } }
    private void CancelMove(string reason) { CancelGesture(); _deltaX = _deltaY = 0; Status = reason; Notify(); }
    private void RequireCapture() { if (!CanCapture) throw new NotSupportedException(CapabilityReason); }
    private void RefreshFeedback()
    {
        var visible = !_retiring && !_disposed && _tools.Selected == CanvasPrimaryTool.Select && _isAvailable() &&
            _snapshot is not null && _document.Identity == (_snapshot.ArtifactId, _snapshot.RevisionId);
        var shown = _viewport.SetSelectionFeedback(_feedbackPath, _deltaX, _deltaY, visible);
        if (visible && _feedbackPath is not null)
            FeedbackStatus = shown ? "The selected donor path is outlined. A solid shifted contour previews an uncommitted move." :
                "The selection contour is unavailable in this view; the retained selected target is unchanged.";
    }
    private void Notify() { RefreshFeedback(); Changed?.Invoke(this, EventArgs.Empty); }
    private void CaptureLost(object? sender, PointerCaptureLostEventArgs args) { if (!_releasing && _pointer is not null) CancelMove("Pointer capture ended. No move was submitted."); }
    private void ViewChanged(object? sender, EventArgs args) { if (_pointer is not null) CancelMove("The view changed during dragging. No move was submitted."); }
    private void ToolChanged(object? sender, EventArgs args)
    {
        if (_pointer is not null && !CanMove) CancelMove("The selected tool changed. No move was submitted.");
        else RefreshFeedback();
    }
    private void Detached(object? sender, VisualTreeAttachmentEventArgs args) { if (_pointer is not null) CancelMove("The drawing surface closed. No move was submitted."); }
    public Task CloseAndDrainAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(CloseAndDrainAsync);
        _retiring = true; CancelGesture(); _viewport.SetSelectionFeedback(null);
        return _originalWork.CloseAndDrainAsync(() =>
        {
            _disposed = true;
            _viewport.PointerPressed -= Pressed; _viewport.PointerMoved -= Moved; _viewport.PointerReleased -= Released;
            _viewport.PointerCaptureLost -= CaptureLost; _viewport.DetachedFromVisualTree -= Detached;
            _viewport.ViewChanged -= ViewChanged; _tools.Changed -= ToolChanged;
            return Task.CompletedTask;
        });
    }
    public void Dispose() { _ = CloseAndDrainAsync(); }
}
