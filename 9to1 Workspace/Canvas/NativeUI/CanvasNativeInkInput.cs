using Avalonia.Input;
using HavenOS.Files;

namespace HavenOS.Apps.Canvas;

/// <summary>Collects real native input against the displayed canonical viewport. The callback must request
/// the owning Home operation; this controller never mutates the document or grants storage access.</summary>
public sealed class CanvasNativeInkInput : IDisposable
{
    private readonly CanvasNativeViewport _viewport;
    private readonly CanvasRnoteDocument _document;
    private readonly CanvasToolState _tools;
    private readonly HostedItemId _fileId;
    private readonly FilesRevisionId _filesRevision;
    private readonly Guid _expectedStoreId;
    private readonly Func<bool> _isAvailable;
    private readonly Func<CanvasStrokeWriteIntent, CancellationToken, Task> _requestOperation;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CanvasOriginalWorkOwner _originalWork = new();
    private readonly List<RnotePointerSample> _samples = [];
    private IPointer? _pointer;
    private (Guid ArtifactId, Guid RevisionId)? _captured;
    private CanvasRnoteInkStyle? _style;
    private bool _retiring;
    private bool _disposed, _submitting, _submitted, _releasing;
    public event EventHandler? Changed;
    public string Status { get; private set; } = "Pen input: native pressure and tilt when available; mouse and touch use pressure 0.5.";
    public bool IsCapturing => _pointer is not null;
    public bool HasSubmittedStroke => _submitted;

    public CanvasNativeInkInput(CanvasNativeViewport viewport, CanvasRnoteDocument document, CanvasToolState tools,
        HostedItemId fileId, FilesRevisionId filesRevision, Func<bool> isAvailable,
        Func<CanvasStrokeWriteIntent, CancellationToken, Task> requestOperation,Guid expectedStoreId=default)
    {
        if (fileId.Value == Guid.Empty || filesRevision.Value == Guid.Empty)
            throw new ArgumentException("Native ink input requires the exact displayed Files revision.");
        _viewport = viewport; _document = document; _tools = tools; _fileId = fileId; _filesRevision = filesRevision;
        _expectedStoreId=expectedStoreId;_isAvailable = isAvailable; _requestOperation = requestOperation;
        viewport.PointerPressed += Pressed;
        viewport.PointerMoved += Moved;
        viewport.PointerReleased += Released;
        viewport.PointerCaptureLost += CaptureLost;
        viewport.DetachedFromVisualTree += Detached;
        tools.Changed += ToolChanged;
        viewport.ViewChanged += ViewChanged;
    }

    private bool CanCapture => !_retiring && !_disposed && !_submitting && !_submitted && _tools.Selected == CanvasPrimaryTool.Pen && _tools.Capability(CanvasPrimaryTool.Pen).Available && _isAvailable();
    private void Pressed(object? sender, PointerPressedEventArgs args)
    {
        if (!CanCapture || _pointer is not null) return;
        var point = args.GetCurrentPoint(_viewport);
        if (!point.Properties.IsLeftButtonPressed || point.Properties.IsEraser || _viewport.ToDocumentPoint(point.Position) is not { } position) return;
        _captured = _document.Identity; _style = _tools.InkStyle;
        _pointer = args.Pointer; _samples.Clear();
        if (!Add(position.X, position.Y, point.Properties, args.Pointer.Type)) return;
        args.Pointer.Capture(_viewport);
        args.PreventGestureRecognition();
        Status = "Stroke captured locally. Release to review the exact owning operation.";
        Changed?.Invoke(this, EventArgs.Empty); args.Handled = true;
    }
    private void Moved(object? sender, PointerEventArgs args)
    {
        if (_pointer != args.Pointer) return;
        if (!CanCapture) { CancelCapture("Pen input is no longer available."); return; }
        foreach (var point in args.GetIntermediatePoints(_viewport))
        {
            if (_viewport.ToDocumentPoint(point.Position) is not { } position)
            { CancelCapture("The stroke left the displayed document bounds; no change was submitted."); return; }
            if (!Add(position.X, position.Y, point.Properties, args.Pointer.Type)) return;
        }
        args.Handled = true;
    }
    private void Released(object? sender, PointerReleasedEventArgs args)
    {
        if (_disposed || _retiring) return;
        var actual = _originalWork.RunOriginalAsync(token => ReleasedOriginalAsync(sender, args, token), _lifetime.Token);
        _ = ObserveReleasedAsync(actual);
    }
    private async Task ObserveReleasedAsync(Task actual)
    { try { await actual; } catch { /* The SAME failed source remains retained and joined. */ } }
    private async Task ReleasedOriginalAsync(object? sender, PointerReleasedEventArgs args, CancellationToken originalToken)
    {
        if (_pointer != args.Pointer) return;
        try
        {
            if (!CanCapture || _captured is null || _style is null) { CancelCapture("Pen input is no longer available."); return; }
            var point = args.GetCurrentPoint(_viewport);
            if (_viewport.ToDocumentPoint(point.Position) is not { } position || !Add(position.X, position.Y, point.Properties, args.Pointer.Type))
            { CancelCapture("The stroke ended outside the displayed document; no change was submitted."); return; }
            var current = _document.Identity;
            if (current.ArtifactId != _captured.Value.ArtifactId || current.RevisionId != _captured.Value.RevisionId)
            { CancelCapture("The Canvas revision changed during input. Reopen its current revision."); return; }
            var intent = _expectedStoreId==Guid.Empty
                ? CanvasStrokeWriteIntent.Capture(_fileId,_filesRevision,_captured.Value.ArtifactId,_captured.Value.RevisionId,Guid.NewGuid(),_samples,_style)
                : CanvasStrokeWriteIntent.Capture(_fileId,_filesRevision,_captured.Value.ArtifactId,_captured.Value.RevisionId,Guid.NewGuid(),_expectedStoreId,_samples,_style);
            ReleaseCapture(); _samples.Clear(); _captured = null; _style = null;
            _submitting = true; _submitted = true; Status = "Review this captured stroke in Home."; Changed?.Invoke(this, EventArgs.Empty);
            await _originalWork.ObserveOriginalAsync(_requestOperation(intent, originalToken));
            Status = "The stroke request was submitted. The owning transaction determines its committed revision.";
        }
        catch (Exception error)
        {
            // A request callback can already have created durable Home state. Never automatically resubmit.
            Status = "Check Home for this stroke request before trying again. " + error.Message; throw;
        }
        finally
        {
            ReleaseCapture(); _samples.Clear(); _captured = null; _style = null; _submitting = false;
            Changed?.Invoke(this, EventArgs.Empty); args.Handled = true;
        }
    }
    private bool Add(double x, double y, PointerPointProperties properties, PointerType type)
    {
        if (_samples.Count >= RnoteCanvasEngine.MaximumStrokeSamples)
        { CancelCapture("This stroke exceeds the supported sample count; no partial stroke was submitted."); return false; }
        var pen = type == PointerType.Pen;
        var sample = new RnotePointerSample(x, y, pen ? properties.Pressure : 0.5,
            pen ? properties.XTilt : 0, pen ? properties.YTilt : 0);
        if (!sample.IsValid) { CancelCapture("The device returned invalid pen data; no change was submitted."); return false; }
        _samples.Add(sample); return true;
    }
    private void ReleaseCapture()
    {
        var pointer = _pointer; _pointer = null;
        _releasing = true;
        try { pointer?.Capture(null); }
        finally { _releasing = false; }
    }
    private void CancelCapture(string reason)
    { ReleaseCapture(); _samples.Clear(); _captured = null; _style = null; Status = reason; Changed?.Invoke(this, EventArgs.Empty); }
    private void CaptureLost(object? sender, PointerCaptureLostEventArgs args)
    { if (!_releasing && _pointer is not null) CancelCapture("Native pointer capture ended; no change was submitted."); }
    private void ToolChanged(object? sender, EventArgs args)
    { if (_pointer is not null && !CanCapture) CancelCapture("The selected tool changed; no stroke was submitted."); }
    private void ViewChanged(object? sender, EventArgs args)
    { if (_pointer is not null) CancelCapture("The Canvas view changed; no stroke was submitted."); }
    private void Detached(object? sender, Avalonia.VisualTreeAttachmentEventArgs args)
    { if (_pointer is not null) CancelCapture("The Canvas surface was closed; no stroke was submitted."); }
    public Task CloseAndDrainAsync()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(CloseAndDrainAsync);
        _retiring = true;
        return _originalWork.CloseAndDrainAsync(() => { DisposeOriginal(); return Task.CompletedTask; });
    }
    public void Dispose() { _ = CloseAndDrainAsync(); }
    private void DisposeOriginal()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel(); CancelCapture("Canvas input closed.");
        _viewport.PointerPressed -= Pressed; _viewport.PointerMoved -= Moved; _viewport.PointerReleased -= Released;
        _viewport.PointerCaptureLost -= CaptureLost; _viewport.DetachedFromVisualTree -= Detached; _tools.Changed -= ToolChanged;
        _viewport.ViewChanged -= ViewChanged;
        _lifetime.Dispose();
    }
}
