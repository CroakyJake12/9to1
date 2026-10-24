using Avalonia.Input;
using HavenOS.Files;

namespace HavenOS.Apps.Canvas;

/// <summary>Collects real native input against the displayed canonical viewport. The callback must request
/// the owning Home operation; this controller never mutates the document or grants storage access.</summary>
public sealed class CanvasNativeInkInput : IDisposable
{
    internal sealed record QueuedRelease(long Generation, object Pointer, CanvasStrokeWriteIntent Intent);
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
    private long _gestureGeneration;
    private QueuedRelease? _activeRelease;
    private (Guid ArtifactId, Guid RevisionId)? _captured;
    private CanvasRnoteInkStyle? _style;
    private bool _retiring;
    private bool _disposed, _submitting, _submitted, _releasing, _releaseQueued;
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
        if (!CanBeginPointerGesture(CanCapture, _pointer is not null, _releaseQueued)) return;
        var point = args.GetCurrentPoint(_viewport);
        if (!BeginsInkGesture(args.Pointer.Type, point.Properties.IsLeftButtonPressed, point.Properties.IsEraser) ||
            _viewport.ToDocumentPoint(point.Position) is not { } position) return;
        _captured = _document.Identity; _style = _tools.InkStyle;
        _gestureGeneration=checked(_gestureGeneration+1);_activeRelease=null;_releaseQueued=false;
        _pointer = args.Pointer; _samples.Clear();
        if (!Add(position.X, position.Y, point.Properties, args.Pointer.Type)) return;
        args.Pointer.Capture(_viewport);
        args.PreventGestureRecognition();
        Status = "Stroke captured locally. Release to review the exact owning operation.";
        Changed?.Invoke(this, EventArgs.Empty); args.Handled = true;
    }
    internal static bool CanBeginPointerGesture(bool canCapture, bool pointerCaptured, bool releaseQueued)
        => canCapture && !pointerCaptured && !releaseQueued;
    internal static bool BeginsInkGesture(PointerType type, bool primaryPressed, bool eraserEnd)
        => !eraserEnd && (primaryPressed || type == PointerType.Touch);
    private void Moved(object? sender, PointerEventArgs args)
    {
        if (_pointer != args.Pointer) return;
        if (_releaseQueued) { args.Handled=true; return; }
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
        if (!CanQueueRelease(_pointer, args.Pointer, _disposed, _retiring, _releaseQueued)) return;
        if (!CanCapture || _captured is null || _style is null) { CancelCapture("Pen input is no longer available."); return; }
        var point=args.GetCurrentPoint(_viewport);
        if(_viewport.ToDocumentPoint(point.Position) is not {} position || !Add(position.X,position.Y,point.Properties,args.Pointer.Type))
        { CancelCapture("The stroke ended outside the displayed document; no change was submitted."); return; }
        var identity=_captured.Value;var style=_style;
        if(_document.Identity!=identity){CancelCapture("The Canvas revision changed during input. Reopen its current revision.");return;}
        CanvasStrokeWriteIntent intent;
        try
        {
            intent=_expectedStoreId==Guid.Empty
                ? CanvasStrokeWriteIntent.Capture(_fileId,_filesRevision,identity.ArtifactId,identity.RevisionId,Guid.NewGuid(),_samples,style)
                : CanvasStrokeWriteIntent.Capture(_fileId,_filesRevision,identity.ArtifactId,identity.RevisionId,Guid.NewGuid(),_expectedStoreId,_samples,style);
        }
        catch(Exception error) when(error is ArgumentException or InvalidDataException or InvalidOperationException)
        { CancelCapture("The captured stroke could not be prepared. "+error.Message);return; }
        var release=new QueuedRelease(_gestureGeneration,args.Pointer,intent);
        _activeRelease=release;
        // Claim and snapshot the release before publishing original work.
        _releaseQueued = true;
        args.Handled = true;
        var actual = _originalWork.RunOriginalAsync(token => ReleasedOriginalAsync(release, token), _lifetime.Token);
        _ = ObserveReleasedAsync(actual);
    }
    internal static bool CanQueueRelease(object? capturedPointer, object releasedPointer, bool disposed, bool retiring, bool alreadyQueued)
        => capturedPointer is not null && ReferenceEquals(capturedPointer, releasedPointer) && !disposed && !retiring && !alreadyQueued;
    internal static bool IsRelevantCaptureLoss(object? capturedPointer,object lostPointer,bool releasing)
        => !releasing && capturedPointer is not null && ReferenceEquals(capturedPointer,lostPointer);
    internal static bool OwnsRelease(long activeGeneration, object? activeRelease, long releaseGeneration, object release)
        => activeGeneration==releaseGeneration && ReferenceEquals(activeRelease,release);
    private bool Owns(QueuedRelease release)=>OwnsRelease(_gestureGeneration,_activeRelease,release.Generation,release);
    private async Task ObserveReleasedAsync(Task actual)
    { try { await actual; } catch { /* The SAME failed source remains retained and joined. */ } }
    private async Task ReleasedOriginalAsync(QueuedRelease release, CancellationToken originalToken)
    {
        if (!Owns(release)) return;
        try
        {
            var intent=release.Intent;
            if (_retiring || _disposed || !CanCapture || _captured!=(intent.ArtifactId,intent.ExpectedArtifactRevision) ||
                _document.Identity!=(intent.ArtifactId,intent.ExpectedArtifactRevision))
            { CancelCapture("The Canvas revision changed during input. Reopen its current revision."); return; }
            ReleaseCapture(); _samples.Clear(); _captured = null; _style = null;
            _submitting = true; Status = "Review this captured stroke in Home."; Changed?.Invoke(this, EventArgs.Empty);
            // Changed is synchronous and may re-enter CloseAndDrainAsync, change
            // tools, or revoke access. Do not mark or issue a Home request until
            // the captured release still owns the active input at this boundary.
            if(!CanSubmitCapturedRelease(release,originalToken))
            {
                if(Owns(release))CancelCapture("The captured stroke was retired before Home submission.");
                return;
            }
            _submitted = true;
            if(Owns(release))await _originalWork.ObserveOriginalAsync(_requestOperation(intent, originalToken));
            if(Owns(release))Status = "The stroke request was submitted. The owning transaction determines its committed revision.";
        }
        catch (Exception error)
        {
            // A request callback can already have created durable Home state. Never automatically resubmit.
            if(Owns(release))Status = "Check Home for this stroke request before trying again. " + error.Message; throw;
        }
        finally
        {
            if(Owns(release))
            {
                if (!_submitted) { _releaseQueued = false; _activeRelease=null; }
                ReleaseCapture(); _samples.Clear(); _captured = null; _style = null; _submitting = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    internal bool CanSubmitCapturedRelease(QueuedRelease release,CancellationToken token)
        => Owns(release) && !_retiring && !_disposed && !token.IsCancellationRequested &&
           _tools.Selected==CanvasPrimaryTool.Pen && _tools.Capability(CanvasPrimaryTool.Pen).Available &&
           _isAvailable() && _document.Identity==(release.Intent.ArtifactId,release.Intent.ExpectedArtifactRevision);
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
    { _gestureGeneration=checked(_gestureGeneration+1);_activeRelease=null;_releaseQueued = false;if(!_submitted)_submitting=false;ReleaseCapture(); _samples.Clear(); _captured = null; _style = null; Status = reason; Changed?.Invoke(this, EventArgs.Empty); }
    private void CaptureLost(object? sender, PointerCaptureLostEventArgs args)
    { if (IsRelevantCaptureLoss(_pointer,args.Pointer,_releasing)) CancelCapture("Native pointer capture ended; no change was submitted."); }
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
        if(!_submitted && (_pointer is not null || _activeRelease is not null))CancelCapture("Canvas input closed.");
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
