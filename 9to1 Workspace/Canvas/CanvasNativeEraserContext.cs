using Haven.Application;
using System.Text.Json;
using HavenOS.Files;
namespace HavenOS.Apps.Canvas;
public interface ICanvasEraserInput
{
    event EventHandler? Changed;
    bool HasSubmittedOperation {get;}
    bool CanCapture {get;}
    bool HasTarget {get;}
    string TargetSummary {get;}
    string Status {get;}
    void ChooseTarget(int direction);
    Task RequestChosenTargetAsync(CancellationToken cancellationToken=default);
}
public enum CanvasEraserMode { Natural, Quick }
public enum CanvasNaturalEraserStyle { WholeStroke, PartialStroke }
public sealed record CanvasEraserOptions(CanvasEraserMode Mode = CanvasEraserMode.Natural,
    CanvasNaturalEraserStyle NaturalStyle = CanvasNaturalEraserStyle.WholeStroke, double Width = 12)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(NaturalStyle) || !double.IsFinite(Width) || Width is < 1 or > 500)
            throw new ArgumentException("Eraser options exceed the genuine donor contract.");
    }
}
/// <summary>Exact original owner snapshot and typed request ports, never an actor or grant from UI input.</summary>
public sealed class CanvasNativeEraserContext
{
    private readonly CanvasFilesOpenResult _opened;
    private readonly Dictionary<(CanvasEraserMode,CanvasNaturalEraserStyle),CanvasToolCapability> _geometryCapabilities=[];
    private readonly object _gate=new();
    public HostedItemId FileId { get; }
    public Guid StoreId => _opened.StoreId;
    public FilesRevisionId FilesRevision => _opened.CasRevisionId;
    public (Guid ArtifactId, Guid RevisionId) Identity => (_opened.Artifact.ArtifactId,_opened.Artifact.RevisionId);
    public Func<bool> IsAvailable { get; }
    public Func<CanvasEraserIntent,CancellationToken,Task> RequestWhole { get; }
    public Func<CanvasSplitEraseIntent,CancellationToken,Task> RequestPartial { get; }
    public Func<CanvasQuickEraseIntent,CancellationToken,Task> RequestQuick { get; }
    public Func<CanvasStrokeEditIntent,CancellationToken,Task> RequestTarget { get; }
    public Func<CanvasHistoryIntent,CancellationToken,Task>? RequestHistory {get;}
    public CanvasNativeEraserContext(HostedItemId fileId,CanvasFilesOpenResult opened,Func<bool> isAvailable,
        Func<CanvasEraserIntent,CancellationToken,Task> whole,Func<CanvasSplitEraseIntent,CancellationToken,Task> partial,
        Func<CanvasQuickEraseIntent,CancellationToken,Task> quick,Func<CanvasStrokeEditIntent,CancellationToken,Task> target,
        Func<CanvasHistoryIntent,CancellationToken,Task>? history=null)
    {
        ArgumentNullException.ThrowIfNull(opened);ArgumentNullException.ThrowIfNull(isAvailable);
        ArgumentNullException.ThrowIfNull(whole);ArgumentNullException.ThrowIfNull(partial);ArgumentNullException.ThrowIfNull(quick);ArgumentNullException.ThrowIfNull(target);
        if(fileId.Value==Guid.Empty || opened.StoreId==Guid.Empty || opened.CasRevisionId.Value==Guid.Empty)
            throw new ArgumentException("Eraser input requires the original Files store, item and revision.");
        FileId=fileId; _opened=opened with { Artifact=CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(opened.Artifact)) };
        IsAvailable=isAvailable; RequestWhole=whole; RequestPartial=partial; RequestQuick=quick; RequestTarget=target;RequestHistory=history;
    }
    public CanvasToolCapability Capability(CanvasRnoteDocument document,CanvasEraserOptions options)
    {
        options.Validate();
        if(!IsAvailable() || document.Identity!=Identity) return CanvasToolCapability.Unavailable("Reopen the current writable Canvas revision.");
        lock(_gate)
        {
            var key=(options.Mode,options.NaturalStyle);
            if(!_geometryCapabilities.TryGetValue(key,out var result))
                _geometryCapabilities[key]=result=EvaluateGeometry(document,options);
            return result;
        }
    }
    private static CanvasToolCapability EvaluateGeometry(CanvasRnoteDocument document,CanvasEraserOptions options)
    {
        try
        {
            var artifact=document.Snapshot;
            if(artifact.Pages.Count!=1 || artifact.Pages[0].Layers.Count!=1 || artifact.SharedResources.Count!=0 || artifact.Pages[0].Objects.Count!=0)
                return CanvasToolCapability.Unavailable("This eraser is available only for a single ink layer without referenced objects.");
            if(!artifact.Pages[0].Layers[0].IsVisible)
                return CanvasToolCapability.Unavailable("Show the ink layer before erasing.");
            if(artifact.Pages[0].Strokes.Any(stroke=>stroke.Transform!=new CanvasTransform()))
                return CanvasToolCapability.Unavailable("This eraser cannot edit transformed ink.");
            using var engine=RnoteCanvasEngine.Open(document.ExportRnote());
            var state=artifact.DocumentSettings.Properties["9to1.Canvas.RnoteState"];
            var bindings=state.GetProperty("NativeStrokeKeys").Deserialize<Dictionary<Guid,ulong>>() ?? [];
            var page=artifact.Pages[0];
            if(bindings.Count!=page.Strokes.Count || page.Strokes.Any(stroke=>!bindings.ContainsKey(stroke.StrokeId)) ||
                bindings.Values.Distinct().Count()!=bindings.Count || !bindings.Values.ToHashSet().SetEquals(engine.ReadStrokeKeys()))
                return CanvasToolCapability.Unavailable("This document needs an ink migration before erasing.");

            if(options.Mode==CanvasEraserMode.Quick && (!engine.SupportsQuickEraseTarget || !engine.SupportsStructuredStrokeMutation))
                return CanvasToolCapability.Unavailable("Quick erasing is unavailable in this drawing engine.");
            if(options.Mode==CanvasEraserMode.Natural && options.NaturalStyle==CanvasNaturalEraserStyle.PartialStroke && !engine.SupportsSplitEraseCandidate)
                return CanvasToolCapability.Unavailable("Partial-stroke erasing is unavailable in this drawing engine.");
            if(options.Mode==CanvasEraserMode.Quick && !engine.ReadRenderedStrokeKeys().SequenceEqual(page.StrokeOrder.Select(id=>bindings[id])))
                return CanvasToolCapability.Unavailable("This document's ink order needs recovery before Quick erasing.");
            return new(true,options.Mode==CanvasEraserMode.Quick ? "Tap a stroke, or choose a named stroke below, then review its deletion in Home." : "Drag to capture an eraser gesture, then review the exact change in Home.");
        }
        catch(Exception error) { return CanvasToolCapability.Unavailable(error.Message); }
    }
    public CanvasEraserIntent CaptureWhole(IReadOnlyList<RnotePointerSample> samples,double width)=>CanvasEraserIntent.Capture(FileId,_opened,samples,width);
    public CanvasSplitEraseIntent CapturePartial(IReadOnlyList<RnotePointerSample> samples,double width)=>CanvasSplitEraseIntent.Capture(FileId,_opened,samples,width);
    public CanvasQuickEraseIntent CaptureQuick(double x,double y)=>CanvasQuickEraseIntent.Capture(FileId,_opened,x,y);
    public bool CanHistory(CanvasRnoteDocument document,CanvasHistoryKind kind)=>RequestHistory is not null && IsAvailable() && document.Identity==Identity &&
        (kind==CanvasHistoryKind.Undo ? _opened.Artifact.SemanticHistory?.Undo.Count>0 : _opened.Artifact.SemanticHistory?.Redo.Count>0);
    public CanvasHistoryIntent CaptureHistory(CanvasHistoryKind kind)=>CanvasHistoryIntent.Capture(FileId,FilesRevision,Identity.ArtifactId,Identity.RevisionId,Guid.NewGuid(),kind,StoreId);
    public CanvasStrokeEditIntent CaptureTarget(Guid id)
    {
        var page=_opened.Artifact.Pages[0];
        var stroke=page.Strokes.Single(value=>value.StrokeId==id);
        if(page.Layers.Single(layer=>layer.LayerId==stroke.LayerId).IsLocked)
            throw new InvalidOperationException("The chosen stroke is locked.");
        return CanvasStrokeEditIntent.Capture(FileId,FilesRevision,Identity.ArtifactId,Identity.RevisionId,Guid.NewGuid(),id,CanvasStrokeEditKind.Delete,StoreId);
    }
}
