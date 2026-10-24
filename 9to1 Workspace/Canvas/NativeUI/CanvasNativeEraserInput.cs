using Avalonia.Input;
namespace HavenOS.Apps.Canvas;

/// <summary>Real native pointer collection and accessible exact-key targeting. No document mutation or grants.</summary>
public sealed class CanvasNativeEraserInput : ICanvasEraserInput, IDisposable
{
    internal sealed record QueuedRelease(long Generation, object Pointer,
        (Guid ArtifactId,Guid RevisionId) Identity, CanvasEraserOptions Options,
        object Intent, Func<CancellationToken,Task> Request);
    internal sealed class ReleaseGeneration
    {
        private long _generation;
        private object? _pointer;
        private QueuedRelease? _release;
        internal long Current=>_generation;
        internal bool HasQueued=>_release is not null;
        internal long Begin(object pointer)
        { _generation=checked(_generation+1);_pointer=pointer;_release=null;return _generation; }
        internal bool Queue(QueuedRelease release)
        {
            if(release.Generation!=_generation || !ReferenceEquals(release.Pointer,_pointer) || _release is not null)return false;
            _release=release;return true;
        }
        internal bool Owns(QueuedRelease release)
            => OwnsQueuedRelease(_generation,_release,release.Generation,release) && ReferenceEquals(_pointer,release.Pointer);
        internal void Invalidate()
        { _generation=checked(_generation+1);_pointer=null;_release=null; }
    }

    private readonly CanvasNativeViewport _viewport;
    private readonly CanvasRnoteDocument _document;
    private readonly CanvasToolState _tools;
    private readonly CanvasNativeEraserContext _context;
    private readonly CancellationTokenSource _lifetime=new();
    private readonly CanvasOriginalWorkOwner _originalWork = new();
    private readonly ReleaseGeneration _releaseGeneration=new();
    private readonly List<RnotePointerSample> _samples=[];
    private IPointer? _pointer;
    private CanvasEraserOptions? _capturedOptions;
    private (Guid ArtifactId,Guid RevisionId)? _captured;
    private Avalonia.Point _press;
    private Guid? _target;
    private bool _retiring;
    private bool _disposed,_submitting,_submitted,_releasing;
    public event EventHandler? Changed;
    public string Status {get;private set;}="Choose Natural or Quick erasing, then review each change in Home.";
    public bool HasSubmittedOperation=>_submitted;
    public bool HasTarget=>_target is not null;
    public string TargetSummary
    {
        get
        {
            if(_target is not {} id)return "No stroke chosen";
            var page=_document.Snapshot.Pages[0];var stroke=page.Strokes.Single(value=>value.StrokeId==id);
            var layer=page.Layers.Single(value=>value.LayerId==stroke.LayerId);
            var color=stroke.ResolvedBrushProperties.Color.ToUpperInvariant() switch
            {"#FF000000"=>"black","#FFFF0000"=>"red","#FF0000FF"=>"blue",var other=>other};
            return $"Stroke {page.StrokeOrder.IndexOf(id)+1} of {page.StrokeOrder.Count}, {color}, width {stroke.ResolvedBrushProperties.BaseWidth}, {layer.Name}{(layer.IsLocked ? ", locked" : "")}";
        }
    }
    public bool CanCapture=>!_retiring&&!_disposed&&!_submitting&&!_submitted&&_tools.Selected==CanvasPrimaryTool.Eraser&&_tools.Capability(CanvasPrimaryTool.Eraser).Available&&_context.Capability(_document,_tools.EraserOptions).Available;
    public CanvasNativeEraserInput(CanvasNativeViewport viewport,CanvasRnoteDocument document,CanvasToolState tools,CanvasNativeEraserContext context)
    {
        if(document.Identity!=context.Identity) throw new InvalidDataException("Eraser context differs from displayed revision.");
        _viewport=viewport;_document=document;_tools=tools;_context=context;
        viewport.PointerPressed+=Pressed;viewport.PointerMoved+=Moved;viewport.PointerReleased+=Released;
        viewport.PointerCaptureLost+=CaptureLost;viewport.DetachedFromVisualTree+=Detached;
        viewport.ViewChanged+=ViewChanged;tools.Changed+=ToolChanged;
    }
    public void ChooseTarget(int direction)
    {
        if(!CanCapture || _tools.EraserOptions.Mode!=CanvasEraserMode.Quick) throw new NotSupportedException("Choose Quick erasing on a writable mapped document.");
        var ids=_document.Snapshot.Pages[0].StrokeOrder;
        if(ids.Count==0) { _target=null;Status="There are no strokes to choose.";Changed?.Invoke(this,EventArgs.Empty);return; }
        var index=_target is {} target ? ids.IndexOf(target) : -1;
        index=index<0 ? (direction<0 ? ids.Count-1 : 0) : (index+(direction<0 ? -1 : 1)+ids.Count)%ids.Count;
        _target=ids[index];Status=TargetSummary;Changed?.Invoke(this,EventArgs.Empty);
    }
    public async Task RequestChosenTargetAsync(CancellationToken cancellationToken=default)
    {
        if(!CanCapture || _pointer is not null || _releaseGeneration.HasQueued || _tools.EraserOptions.Mode!=CanvasEraserMode.Quick || _target is not {} id)
            throw new NotSupportedException("Choose an exact stroke first.");
        var intent=_context.CaptureTarget(id); // locked/invalid targets refuse before any Home request.
        await SubmitAsync(token=>_context.RequestTarget(intent,token),cancellationToken);
    }
    private void Pressed(object? sender,PointerPressedEventArgs args)
    {
        var point=args.GetCurrentPoint(_viewport);
        if(!CanBeginPointerGesture(args.Pointer.Type,point.Properties.IsLeftButtonPressed,point.Properties.IsEraser))return;
        // An inverted stylus reports its eraser end separately from the primary
        // button. Preserve that donor input when the explicit Canvas Eraser tool
        // is selected; do not reinterpret it as ink or switch tools implicitly.
        if(_viewport.ToDocumentPoint(point.Position) is not {} position)return;
        _releaseGeneration.Begin(args.Pointer);
        _captured=_document.Identity;_capturedOptions=_tools.EraserOptions;_press=point.Position;
        _pointer=args.Pointer;_samples.Clear();
        if(!Add(position.X,position.Y,point.Properties,args.Pointer.Type))return;
        args.Pointer.Capture(_viewport);args.PreventGestureRecognition();args.Handled=true;
        Status="Eraser gesture captured locally. Release to review the change.";Changed?.Invoke(this,EventArgs.Empty);
    }
    internal static bool BeginsEraserGesture(PointerType type,bool primaryPressed,bool eraserEnd)
        => primaryPressed || type==PointerType.Touch || type==PointerType.Pen && eraserEnd;
    internal bool CanBeginPointerGesture(PointerType type,bool primaryPressed,bool eraserEnd)
        => CanBeginPointerGesture(CanCapture,_pointer is not null,_releaseGeneration.HasQueued,type,primaryPressed,eraserEnd);
    internal static bool CanBeginPointerGesture(bool canCapture,bool pointerCaptured,bool releaseQueued,
        PointerType type,bool primaryPressed,bool eraserEnd)
        => canCapture && !pointerCaptured && !releaseQueued && BeginsEraserGesture(type,primaryPressed,eraserEnd);
    private void Moved(object? sender,PointerEventArgs args)
    {
        if(_pointer!=args.Pointer)return;
        if(_releaseGeneration.HasQueued){args.Handled=true;return;}
        if(!StillCaptured()){Cancel("Eraser settings or document access changed; no request was submitted.");return;}
        foreach(var point in args.GetIntermediatePoints(_viewport))
        {
            if(_capturedOptions!.Mode==CanvasEraserMode.Quick && ((point.Position.X-_press.X)*(point.Position.X-_press.X)+(point.Position.Y-_press.Y)*(point.Position.Y-_press.Y))>64)
            {Cancel("Quick erasing requires a tap; no request was submitted.");return;}
            if(_viewport.ToDocumentPoint(point.Position) is not {} position){Cancel("The gesture left the displayed document; no request was submitted.");return;}
            if(!Add(position.X,position.Y,point.Properties,args.Pointer.Type))return;
        }
        args.Handled=true;
    }
    private void Released(object? sender, PointerReleasedEventArgs args)
    {
        if (!CanQueueRelease(_pointer,args.Pointer,_disposed,_retiring,_releaseGeneration.HasQueued)) return;
        args.Handled=true;
        QueuedRelease? release;
        try { release=CaptureRelease(args); }
        catch(Exception error) { Cancel("The eraser release could not be captured. "+error.Message); return; }
        if(release is null)return;
        // Capture the immutable intent and generation synchronously. The queued
        // original callback must never consult mutable gesture buffers or args.
        if(!_releaseGeneration.Queue(release))return;
        var actual = _originalWork.RunOriginalAsync(token => ReleasedOriginalAsync(release,token), _lifetime.Token);
        _ = ObserveReleasedAsync(actual);
    }
    internal static bool CanQueueRelease(object? capturedPointer,object releasedPointer,bool disposed,bool retiring,bool alreadyQueued)
        => capturedPointer is not null && ReferenceEquals(capturedPointer,releasedPointer) && !disposed && !retiring && !alreadyQueued;
    internal static bool IsRelevantCaptureLoss(object? capturedPointer,object lostPointer,bool releasing)
        => !releasing && capturedPointer is not null && ReferenceEquals(capturedPointer,lostPointer);
    internal static bool OwnsQueuedRelease(long currentGeneration,object? currentRelease,long releaseGeneration,object release)
        => currentGeneration==releaseGeneration && ReferenceEquals(currentRelease,release);
    private bool Owns(QueuedRelease release)
        => _releaseGeneration.Owns(release);

    private QueuedRelease? CaptureRelease(PointerReleasedEventArgs args)
    {
        if(!StillCaptured() || _captured is not {} identity || _capturedOptions is not {} options)
        { Cancel("The eraser context changed; reopen the current revision.");return null; }
        var point=args.GetCurrentPoint(_viewport);
        if(options.Mode==CanvasEraserMode.Quick && ((point.Position.X-_press.X)*(point.Position.X-_press.X)+
            (point.Position.Y-_press.Y)*(point.Position.Y-_press.Y))>64)
        { Cancel("Quick erasing requires a tap; no request was submitted.");return null; }
        if(_viewport.ToDocumentPoint(point.Position) is not {} position)
        { Cancel("The gesture ended outside the document; no request was submitted.");return null; }
        if(_samples.Count>=RnoteCanvasEngine.MaximumStrokeSamples)
        { Cancel("The gesture exceeds its sample limit; no partial request was submitted.");return null; }
        var final=CaptureSample(position.X,position.Y,point.Properties,args.Pointer.Type);
        if(!final.IsValid)
        { Cancel("The device returned invalid input; no request was submitted.");return null; }
        var samples=_samples.Append(final).ToArray();
        object intent;
        Func<CancellationToken,Task> request;
        if(options.Mode==CanvasEraserMode.Quick)
        {
            var captured=_context.CaptureQuick(samples[0].X,samples[0].Y);
            intent=captured;request=token=>_context.RequestQuick(captured,token);
        }
        else if(options.NaturalStyle==CanvasNaturalEraserStyle.PartialStroke)
        {
            var captured=_context.CapturePartial(samples,options.Width);
            intent=captured;request=token=>_context.RequestPartial(captured,token);
        }
        else
        {
            var captured=_context.CaptureWhole(samples,options.Width);
            intent=captured;request=token=>_context.RequestWhole(captured,token);
        }
        return new(_releaseGeneration.Current,args.Pointer,identity,options,intent,request);
    }

    private async Task ObserveReleasedAsync(Task actual)
    { try { await actual; } catch { /* The SAME failed source remains retained and joined. */ } }
    private async Task ReleasedOriginalAsync(QueuedRelease release,CancellationToken originalToken)
    {
        if(!Owns(release))return;
        try
        {
            if(_retiring || _disposed || !StillCaptured() || _captured!=release.Identity ||
            _capturedOptions!=release.Options || _document.Identity!=release.Identity)
            { Cancel("The eraser context changed; reopen the current revision.");return; }
            Release();_samples.Clear();_captured=null;_capturedOptions=null;
            var submitted=await SubmitAsync(release.Request,originalToken,requireEraser:false,
                validateBeforeSubmit:()=>CanSubmitRelease(release,originalToken));
            if(Owns(release))Status=submitted
                ? "The eraser request was submitted. Home determines its committed revision."
                : "The eraser gesture was retired before Home submission.";
        }
        catch(Exception error)
        {if(Owns(release))Status=_submitted ? "Check Home for this eraser request before trying again. "+error.Message : error.Message;throw;}
        finally
        {
            // A stale release must not clear a later gesture which reused the
            // same IPointer instance after cancellation.
            if(Owns(release))
            {
                if(!_submitted)_releaseGeneration.Invalidate();
                Release();_samples.Clear();_captured=null;_capturedOptions=null;Changed?.Invoke(this,EventArgs.Empty);
            }
        }
    }
    public bool CanHistory(CanvasHistoryKind kind)=>!_retiring&&!_disposed&&!_submitting&&!_submitted&&_pointer is null&&!_releaseGeneration.HasQueued&&_context.CanHistory(_document,kind);
    public async Task RequestHistoryAsync(CanvasHistoryKind kind,CancellationToken token)
    {
        if(!CanHistory(kind))throw new NotSupportedException("This history operation is unavailable in the displayed revision.");
        var intent=_context.CaptureHistory(kind);
        await SubmitAsync(cancel=>_context.RequestHistory!(intent,cancel),token,requireEraser:false);
    }
    private Task<bool> SubmitAsync(Func<CancellationToken,Task> request,CancellationToken token,bool requireEraser=true,
        Func<bool>? validateBeforeSubmit=null)
        => _originalWork.RunOriginalAsync(actual => SubmitOriginalAsync(request,actual,requireEraser,validateBeforeSubmit),token);
    private async Task<bool> SubmitOriginalAsync(Func<CancellationToken,Task> request,CancellationToken token,bool requireEraser,
        Func<bool>? validateBeforeSubmit)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,_lifetime.Token);
        if(validateBeforeSubmit is not null && !validateBeforeSubmit())return false;
        if(requireEraser ? !CanCapture : _disposed||_submitting||_submitted||!_context.IsAvailable()||_document.Identity!=_context.Identity)throw new UnauthorizedAccessException("Eraser input is no longer writable.");
        _submitting=true;_submitted=true;Status="Review this exact eraser change in Home.";Changed?.Invoke(this,EventArgs.Empty);
        try
        {
            if(!CanInvokeAfterNotification(requireEraser,validateBeforeSubmit,linked.Token))
            {
                _submitted=false;Status="The eraser gesture was retired before Home submission.";return false;
            }
            await _originalWork.ObserveOriginalAsync(request(linked.Token));
            Status="The eraser request was submitted. Home determines its committed revision.";return true;
        }
        finally {_submitting=false;Changed?.Invoke(this,EventArgs.Empty);}
    }
    private bool CanSubmitRelease(QueuedRelease release,CancellationToken token)
        => Owns(release)&&!_retiring&&!_disposed&&!token.IsCancellationRequested&&
            _document.Identity==release.Identity&&_context.Identity==release.Identity&&_context.IsAvailable()&&
            _tools.Selected==CanvasPrimaryTool.Eraser&&_tools.EraserOptions==release.Options&&
            _tools.Capability(CanvasPrimaryTool.Eraser).Available&&_context.Capability(_document,release.Options).Available;
    private bool CanInvokeAfterNotification(bool requireEraser,Func<bool>? validateBeforeSubmit,CancellationToken token)
        => !_disposed&&!_retiring&&!token.IsCancellationRequested&&_context.IsAvailable()&&_document.Identity==_context.Identity&&
            (!requireEraser||_tools.Selected==CanvasPrimaryTool.Eraser&&_tools.Capability(CanvasPrimaryTool.Eraser).Available&&
                _context.Capability(_document,_tools.EraserOptions).Available)&&
            (validateBeforeSubmit is null||validateBeforeSubmit());
    private bool StillCaptured()=>CanCapture&&_captured==_document.Identity&&_capturedOptions==_tools.EraserOptions;
    private static RnotePointerSample CaptureSample(double x,double y,PointerPointProperties properties,PointerType type)
    {
        var pen=type==PointerType.Pen;
        return new(x,y,pen ? properties.Pressure : .5,pen ? properties.XTilt : 0,pen ? properties.YTilt : 0);
    }
    private bool Add(double x,double y,PointerPointProperties properties,PointerType type)
    {
        if(_samples.Count==RnoteCanvasEngine.MaximumStrokeSamples){Cancel("The gesture exceeds its sample limit; no partial request was submitted.");return false;}
        var sample=CaptureSample(x,y,properties,type);
        if(!sample.IsValid){Cancel("The device returned invalid input; no request was submitted.");return false;}
        _samples.Add(sample);return true;
    }
    // A valid release keeps its queue latch through Home submission; only a
    // cancelled, no-request gesture clears it and permits another capture.
    private void Release(){var pointer=_pointer;_pointer=null;_releasing=true;try{pointer?.Capture(null);}finally{_releasing=false;}}
    private void Cancel(string reason)
    {
        _releaseGeneration.Invalidate();
        Release();_samples.Clear();_captured=null;_capturedOptions=null;Status=reason;Changed?.Invoke(this,EventArgs.Empty);
    }
    private void CaptureLost(object? sender,PointerCaptureLostEventArgs args){if(IsRelevantCaptureLoss(_pointer,args.Pointer,_releasing))Cancel("Pointer capture ended; no request was submitted.");}
    private void ToolChanged(object? sender,EventArgs args){if(_pointer is not null&&!StillCaptured())Cancel("The eraser tool changed; no request was submitted.");}
    private void ViewChanged(object? sender,EventArgs args){if(_pointer is not null)Cancel("The view changed; no request was submitted.");}
    private void Detached(object? sender,Avalonia.VisualTreeAttachmentEventArgs args){if(_pointer is not null)Cancel("The surface closed; no request was submitted.");}
    public Task CloseAndDrainAsync()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(CloseAndDrainAsync);
        _retiring = true;
        if(!_submitted && (_pointer is not null || _releaseGeneration.HasQueued))Cancel("Canvas input closed.");
        return _originalWork.CloseAndDrainAsync(() => { DisposeOriginal(); return Task.CompletedTask; });
    }
    public void Dispose() { _ = CloseAndDrainAsync(); }
    private void DisposeOriginal()
    {
        if(_disposed)return;_disposed=true;_lifetime.Cancel();Cancel("Eraser input closed.");
        _viewport.PointerPressed-=Pressed;_viewport.PointerMoved-=Moved;_viewport.PointerReleased-=Released;
        _viewport.PointerCaptureLost-=CaptureLost;_viewport.DetachedFromVisualTree-=Detached;
        _viewport.ViewChanged-=ViewChanged;_tools.Changed-=ToolChanged;_lifetime.Dispose();
    }
}
