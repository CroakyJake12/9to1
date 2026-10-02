using Avalonia.Input;
namespace HavenOS.Apps.Canvas;

/// <summary>Real native pointer collection and accessible exact-key targeting. No document mutation or grants.</summary>
public sealed class CanvasNativeEraserInput : ICanvasEraserInput, IDisposable
{
    private readonly CanvasNativeViewport _viewport;
    private readonly CanvasRnoteDocument _document;
    private readonly CanvasToolState _tools;
    private readonly CanvasNativeEraserContext _context;
    private readonly CancellationTokenSource _lifetime=new();
    private readonly List<RnotePointerSample> _samples=[];
    private IPointer? _pointer;
    private CanvasEraserOptions? _capturedOptions;
    private (Guid ArtifactId,Guid RevisionId)? _captured;
    private Avalonia.Point _press;
    private Guid? _target;
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
    public bool CanCapture=>!_disposed&&!_submitting&&!_submitted&&_tools.Selected==CanvasPrimaryTool.Eraser&&_tools.Capability(CanvasPrimaryTool.Eraser).Available&&_context.Capability(_document,_tools.EraserOptions).Available;
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
        if(!CanCapture || _tools.EraserOptions.Mode!=CanvasEraserMode.Quick || _target is not {} id)
            throw new NotSupportedException("Choose an exact stroke first.");
        var intent=_context.CaptureTarget(id); // locked/invalid targets refuse before any Home request.
        await SubmitAsync(token=>_context.RequestTarget(intent,token),cancellationToken);
    }
    private void Pressed(object? sender,PointerPressedEventArgs args)
    {
        if(!CanCapture || _pointer is not null)return;
        var point=args.GetCurrentPoint(_viewport);
        if(!point.Properties.IsLeftButtonPressed || _viewport.ToDocumentPoint(point.Position) is not {} position)return;
        _captured=_document.Identity;_capturedOptions=_tools.EraserOptions;_press=point.Position;
        _pointer=args.Pointer;_samples.Clear();
        if(!Add(position.X,position.Y,point.Properties,args.Pointer.Type))return;
        args.Pointer.Capture(_viewport);args.PreventGestureRecognition();args.Handled=true;
        Status="Eraser gesture captured locally. Release to review the change.";Changed?.Invoke(this,EventArgs.Empty);
    }
    private void Moved(object? sender,PointerEventArgs args)
    {
        if(_pointer!=args.Pointer)return;
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
    private async void Released(object? sender,PointerReleasedEventArgs args)
    {
        if(_pointer!=args.Pointer)return;
        try
        {
            if(!StillCaptured()){Cancel("The eraser context changed; reopen the current revision.");return;}
            var point=args.GetCurrentPoint(_viewport);
            if(_capturedOptions!.Mode==CanvasEraserMode.Quick && ((point.Position.X-_press.X)*(point.Position.X-_press.X)+(point.Position.Y-_press.Y)*(point.Position.Y-_press.Y))>64)
            {Cancel("Quick erasing requires a tap; no request was submitted.");return;}
            if(_viewport.ToDocumentPoint(point.Position) is not {} position || !Add(position.X,position.Y,point.Properties,args.Pointer.Type))
            {Cancel("The gesture ended outside the document; no request was submitted.");return;}
            Func<CancellationToken,Task> request;
            if(_capturedOptions.Mode==CanvasEraserMode.Quick)
            {
                var first=_samples[0];var intent=_context.CaptureQuick(first.X,first.Y);
                request=token=>_context.RequestQuick(intent,token);
            }
            else if(_capturedOptions.NaturalStyle==CanvasNaturalEraserStyle.PartialStroke)
            {
                var intent=_context.CapturePartial(_samples.ToArray(),_capturedOptions.Width);
                request=token=>_context.RequestPartial(intent,token);
            }
            else
            {
                var intent=_context.CaptureWhole(_samples.ToArray(),_capturedOptions.Width);
                request=token=>_context.RequestWhole(intent,token);
            }
            Release();_samples.Clear();_captured=null;_capturedOptions=null;
            await SubmitAsync(request,_lifetime.Token);
        }
        catch(Exception error)
        {Status=_submitted ? "Check Home for this eraser request before trying again. "+error.Message : error.Message;}
        finally {Release();_samples.Clear();_captured=null;_capturedOptions=null;Changed?.Invoke(this,EventArgs.Empty);args.Handled=true;}
    }
    public bool CanHistory(CanvasHistoryKind kind)=>!_disposed&&!_submitting&&!_submitted&&_context.CanHistory(_document,kind);
    public Task RequestHistoryAsync(CanvasHistoryKind kind,CancellationToken token)
    {
        if(!CanHistory(kind))throw new NotSupportedException("This history operation is unavailable in the displayed revision.");
        var intent=_context.CaptureHistory(kind);
        return SubmitAsync(cancel=>_context.RequestHistory!(intent,cancel),token,requireEraser:false);
    }
    private async Task SubmitAsync(Func<CancellationToken,Task> request,CancellationToken token,bool requireEraser=true)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,_lifetime.Token);
        if(requireEraser ? !CanCapture : _disposed||_submitting||_submitted||!_context.IsAvailable()||_document.Identity!=_context.Identity)throw new UnauthorizedAccessException("Eraser input is no longer writable.");
        _submitting=true;_submitted=true;Status="Review this exact eraser change in Home.";Changed?.Invoke(this,EventArgs.Empty);
        try {await request(linked.Token);Status="The eraser request was submitted. Home determines its committed revision.";}
        finally {_submitting=false;Changed?.Invoke(this,EventArgs.Empty);}
    }
    private bool StillCaptured()=>CanCapture&&_captured==_document.Identity&&_capturedOptions==_tools.EraserOptions;
    private bool Add(double x,double y,PointerPointProperties properties,PointerType type)
    {
        if(_samples.Count==RnoteCanvasEngine.MaximumStrokeSamples){Cancel("The gesture exceeds its sample limit; no partial request was submitted.");return false;}
        var sample=new RnotePointerSample(x,y,type==PointerType.Pen ? properties.Pressure : .5,type==PointerType.Pen ? properties.XTilt : 0,type==PointerType.Pen ? properties.YTilt : 0);
        if(!sample.IsValid){Cancel("The device returned invalid input; no request was submitted.");return false;}
        _samples.Add(sample);return true;
    }
    private void Release(){var pointer=_pointer;_pointer=null;_releasing=true;try{pointer?.Capture(null);}finally{_releasing=false;}}
    private void Cancel(string reason){Release();_samples.Clear();_captured=null;_capturedOptions=null;Status=reason;Changed?.Invoke(this,EventArgs.Empty);}
    private void CaptureLost(object? sender,PointerCaptureLostEventArgs args){if(!_releasing&&_pointer is not null)Cancel("Pointer capture ended; no request was submitted.");}
    private void ToolChanged(object? sender,EventArgs args){if(_pointer is not null&&!StillCaptured())Cancel("The eraser tool changed; no request was submitted.");}
    private void ViewChanged(object? sender,EventArgs args){if(_pointer is not null)Cancel("The view changed; no request was submitted.");}
    private void Detached(object? sender,Avalonia.VisualTreeAttachmentEventArgs args){if(_pointer is not null)Cancel("The surface closed; no request was submitted.");}
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_lifetime.Cancel();Cancel("Eraser input closed.");
        _viewport.PointerPressed-=Pressed;_viewport.PointerMoved-=Moved;_viewport.PointerReleased-=Released;
        _viewport.PointerCaptureLost-=CaptureLost;_viewport.DetachedFromVisualTree-=Detached;
        _viewport.ViewChanged-=ViewChanged;_tools.Changed-=ToolChanged;_lifetime.Dispose();
    }
}
