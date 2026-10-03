using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Haven.Application;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
/// <summary>Genuine maintained native document owner; no Home/Files grant or native UI activation.</summary>
public sealed class NativeRnoteUserLayerDocumentTests
{
    [Fact]
    public void Native_layer_creation_order_history_and_physical_reopen_keep_original_ids_strokes_and_colours()
    {
        var directory=Path.Combine(Path.GetTempPath(),"canvas-native-layers-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            using var document=CanvasRnoteDocument.Create("Native layer owner");var original=document.Snapshot;var page=original.Pages[0];
            var red=document.DrawStroke(Samples(),Request(document),new(CanvasRnoteInkKind.Solid,"#FFFF0000",20));
            var redOnly=document.Serialize();var layer=Guid.NewGuid();
            document.CreateNativeUserLayer(page.PageId,layer,page.Layers[0].Name,0,Request(document));
            var created=document.Snapshot;Assert.Equal(new[]{layer,page.Layers[0].LayerId},created.Pages[0].LayerOrder);
            Assert.Equal(red,Assert.Single(created.Pages[0].Strokes).StrokeId);
            Assert.Equal(page.Layers[0].LayerId,Assert.Single(created.Pages[0].Strokes).LayerId);
            var blue=document.DrawStroke(Samples(),Request(document),new(CanvasRnoteInkKind.Solid,"#FF0000FF",20));
            var before=document.Snapshot;var beforeFrame=Stable(document.Render().Svg);
            var request=Request(document);document.ReorderNativeUserLayer(page.PageId,page.Layers[0].LayerId,0,request);
            var after=document.Snapshot;var afterFrame=Stable(document.Render().Svg);Assert.NotEqual(beforeFrame,afterFrame);
            Assert.Equal(before.Pages[0].StrokeOrder,after.Pages[0].StrokeOrder);
            Assert.Equal(new[]{page.Layers[0].LayerId,layer},after.Pages[0].LayerOrder);
            Assert.Equal(page.Layers[0].LayerId,after.Pages[0].Strokes.Single(stroke=>stroke.StrokeId==red).LayerId);
            Assert.Equal(layer,after.Pages[0].Strokes.Single(stroke=>stroke.StrokeId==blue).LayerId);
            var bytes=document.Serialize();var native=document.ExportRnote();
            document.ReorderNativeUserLayer(page.PageId,page.Layers[0].LayerId,0,request);
            Assert.Equal(bytes,document.Serialize());Assert.Equal(native,document.ExportRnote());
            var path=Path.Combine(directory,"layers.9to1c");File.WriteAllBytes(path,bytes);
            using var reopened=CanvasRnoteDocument.Open(File.ReadAllBytes(path));
            Assert.Equal(afterFrame,Stable(reopened.Render().Svg));Assert.Equal(after.Pages[0].LayerOrder,reopened.Snapshot.Pages[0].LayerOrder);
            reopened.Undo(Request(reopened));Assert.Equal(beforeFrame,Stable(reopened.Render().Svg));
            Assert.Equal(before.Pages[0].LayerOrder,reopened.Snapshot.Pages[0].LayerOrder);
            reopened.Redo(Request(reopened));Assert.Equal(afterFrame,Stable(reopened.Render().Svg));
            using var redReopened=CanvasRnoteDocument.Open(redOnly);Assert.Equal(red,Assert.Single(redReopened.Snapshot.Pages[0].Strokes).StrokeId);
        }
        finally{Directory.Delete(directory,true);}
    }
    [Fact]
    public void Forged_retained_layer_rank_is_rejected_on_actual_native_reopen_and_original_copy_still_renders()
    {
        using var document=CanvasRnoteDocument.Create();var page=document.Snapshot.Pages[0];
        document.DrawStroke(Samples(),Request(document));var layer=Guid.NewGuid();
        document.CreateNativeUserLayer(page.PageId,layer,"Second",null,Request(document));
        var bytes=document.Serialize();var frame=Stable(document.Render().Svg);var artifact=CanvasArtifactCodec.Deserialize(bytes);
        var state=JsonNode.Parse(artifact.DocumentSettings.Properties["9to1.Canvas.RnoteState"].GetRawText())!.AsObject();
        state["NativeLayerRanks"]![layer.ToString("D")]=0;
        artifact.DocumentSettings.Properties["9to1.Canvas.RnoteState"]=JsonSerializer.SerializeToElement(state);
        Assert.Throws<CanvasArtifactFormatException>(()=>CanvasArtifactCodec.Serialize(artifact));
        artifact.SemanticHistory=null; // Reach the same native rank refusal with a controlled older history-free envelope.
        Assert.Throws<InvalidDataException>(()=>CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(artifact)));
        Assert.Equal(bytes,document.Serialize());Assert.Equal(frame,Stable(document.Render().Svg));
        using var original=CanvasRnoteDocument.Open(bytes);Assert.Equal(frame,Stable(original.Render().Svg));
    }
    [Fact]
    public void Native_visibility_keeps_durable_ink_history_and_lock_denies_actual_stroke_before_native_effect()
    {
        using var document=CanvasRnoteDocument.Create("Native visible layers");var page=document.Snapshot.Pages[0];var originalLayer=page.Layers[0].LayerId;
        var red=document.DrawStroke(Samples(),Request(document),new(CanvasRnoteInkKind.Solid,"#FFFF0000",20));
        var blueLayer=Guid.NewGuid();document.CreateNativeUserLayer(page.PageId,blueLayer,"Blue",0,Request(document));
        var blue=document.DrawStroke(Samples(),Request(document),new(CanvasRnoteInkKind.Solid,"#FF0000FF",20));
        var full=Stable(document.Render().Svg);var native=document.ExportRnote();
        var hide=Request(document);document.SetNativeUserLayerVisibility(page.PageId,blueLayer,false,hide);
        var hidden=Stable(document.Render().Svg);Assert.NotEqual(full,hidden);Assert.Equal(native,document.ExportRnote());
        Assert.Equal(new[]{red,blue}.Order(),document.Snapshot.Pages[0].Strokes.Select(stroke=>stroke.StrokeId).Order());
        var hiddenBytes=document.Serialize();document.SetNativeUserLayerVisibility(page.PageId,blueLayer,false,hide);Assert.Equal(hiddenBytes,document.Serialize());
        using(var reopened=CanvasRnoteDocument.Open(hiddenBytes))
        {
            Assert.Equal(hidden,Stable(reopened.Render().Svg));reopened.Undo(Request(reopened));Assert.Equal(full,Stable(reopened.Render().Svg));
            reopened.Redo(Request(reopened));Assert.Equal(hidden,Stable(reopened.Render().Svg));Assert.Equal(native,reopened.ExportRnote());
        }
        document.SetNativeUserLayerVisibility(page.PageId,originalLayer,false,Request(document));
        var background=Stable(document.Render().Svg);Assert.NotEqual(hidden,background);Assert.Equal(native,document.ExportRnote());
        Assert.Equal(2,document.Snapshot.Pages[0].Strokes.Count);
        document.SetNativeUserLayerVisibility(page.PageId,blueLayer,true,Request(document));
        document.SetNativeUserLayerLocked(page.PageId,blueLayer,true,Request(document));
        var locked=document.Serialize();var lockedFrame=Stable(document.Render().Svg);
        Assert.Throws<InvalidOperationException>(()=>document.DrawStroke(Samples(),Request(document)));
        Assert.Equal(locked,document.Serialize());Assert.Equal(native,document.ExportRnote());Assert.Equal(lockedFrame,Stable(document.Render().Svg));
        document.SetNativeUserLayerLocked(page.PageId,blueLayer,false,Request(document));
        document.DrawStroke(Samples(),Request(document));Assert.Equal(3,document.Snapshot.Pages[0].Strokes.Count);
    }
    [Fact]
    public void Native_stroke_layer_move_changes_real_overlap_order_with_stable_ink_and_locked_destination_refusal()
    {
        using var document=CanvasRnoteDocument.Create("Native move layer");var page=document.Snapshot.Pages[0];var originalLayer=page.Layers[0].LayerId;
        var red=document.DrawStroke(Samples(),Request(document),new(CanvasRnoteInkKind.Solid,"#FFFF0000",20));
        var destination=Guid.NewGuid();document.CreateNativeUserLayer(page.PageId,destination,"Destination",0,Request(document));
        document.DrawStroke(Samples(),Request(document),new(CanvasRnoteInkKind.Solid,"#FF0000FF",20));
        document.RenameNativeUserLayer(page.PageId,destination," Renamed destination ",Request(document));
        Assert.Equal("Renamed destination",document.Snapshot.Pages[0].Layers.Single(layer=>layer.LayerId==destination).Name);
        document.SetNativeUserLayerLocked(page.PageId,destination,true,Request(document));
        var locked=document.Serialize();var lockedNative=document.ExportRnote();
        Assert.Throws<InvalidOperationException>(()=>document.MoveNativeStrokeToUserLayer(page.PageId,red,destination,Request(document)));
        Assert.Equal(locked,document.Serialize());Assert.Equal(lockedNative,document.ExportRnote());
        document.SetNativeUserLayerLocked(page.PageId,destination,false,Request(document));
        var before=document.Snapshot;var beforeFrame=Stable(document.Render().Svg);var request=Request(document);
        document.MoveNativeStrokeToUserLayer(page.PageId,red,destination,request);
        var after=document.Snapshot;var afterFrame=Stable(document.Render().Svg);Assert.NotEqual(beforeFrame,afterFrame);
        var original=before.Pages[0].Strokes.Single(stroke=>stroke.StrokeId==red);var moved=after.Pages[0].Strokes.Single(stroke=>stroke.StrokeId==red);
        Assert.Equal(destination,moved.LayerId);Assert.Equal(original.StrokeId,moved.StrokeId);Assert.Equal(original.Samples,moved.Samples);
        Assert.Equal(before.Pages[0].StrokeOrder,after.Pages[0].StrokeOrder);Assert.Equal(before.Pages[0].LayerOrder,after.Pages[0].LayerOrder);
        var bytes=document.Serialize();var native=document.ExportRnote();document.MoveNativeStrokeToUserLayer(page.PageId,red,destination,request);
        Assert.Equal(bytes,document.Serialize());Assert.Equal(native,document.ExportRnote());
        using var reopened=CanvasRnoteDocument.Open(bytes);Assert.Equal(afterFrame,Stable(reopened.Render().Svg));
        reopened.Undo(Request(reopened));Assert.Equal(beforeFrame,Stable(reopened.Render().Svg));Assert.Equal(originalLayer,reopened.Snapshot.Pages[0].Strokes.Single(stroke=>stroke.StrokeId==red).LayerId);
        reopened.Redo(Request(reopened));Assert.Equal(afterFrame,Stable(reopened.Render().Svg));Assert.Equal(destination,reopened.Snapshot.Pages[0].Strokes.Single(stroke=>stroke.StrokeId==red).LayerId);
    }
    [Fact]
    public void Editor_layer_selection_is_not_canonical_and_exact_captured_ink_target_survives_later_selection()
    {
        using var document=CanvasRnoteDocument.Create("Active native layer");var page=document.Snapshot.Pages[0];var original=page.Layers[0].LayerId;
        document.DrawStroke(Samples(),Request(document));var first=Guid.NewGuid();document.CreateNativeUserLayer(page.PageId,first,"First",0,Request(document));
        var bytes=document.Serialize();var native=document.ExportRnote();
        document.SelectNativeUserLayer(page.PageId,original);Assert.Equal(original,document.ActiveNativeUserLayerId);
        Assert.Equal(bytes,document.Serialize());Assert.Equal(native,document.ExportRnote());
        Assert.Throws<ArgumentException>(()=>document.SelectNativeUserLayer(page.PageId,Guid.NewGuid()));Assert.Equal(original,document.ActiveNativeUserLayerId);Assert.Equal(bytes,document.Serialize());
        // An old prepared caller retains first-layer semantics despite local selection.
        document.SelectNativeUserLayer(page.PageId,first);var originalRequest=Request(document);document.SelectNativeUserLayer(page.PageId,original);
        var oldApiStroke=document.DrawStroke(Samples(),originalRequest);
        Assert.Equal(first,document.Snapshot.Pages[0].Strokes.Single(value=>value.StrokeId==oldApiStroke).LayerId);
        var captured=Request(document);document.SelectNativeUserLayer(page.PageId,first);
        var stroke=document.DrawStrokeIntoNativeUserLayer(Samples(),captured,original,new(CanvasRnoteInkKind.Solid,"#FFFF0000",20));
        Assert.Equal(original,document.Snapshot.Pages[0].Strokes.Single(value=>value.StrokeId==stroke).LayerId);
        var committed=document.Serialize();var committedNative=document.ExportRnote();document.SelectNativeUserLayer(page.PageId,original);
        Assert.Equal(stroke,document.DrawStrokeIntoNativeUserLayer(Samples(),captured,original,new(CanvasRnoteInkKind.Solid,"#FFFF0000",20)));
        Assert.Equal(committed,document.Serialize());Assert.Equal(committedNative,document.ExportRnote());
        using(var reopened=CanvasRnoteDocument.Open(committed))
        {
            Assert.Equal(first,reopened.ActiveNativeUserLayerId);Assert.Equal(original,reopened.Snapshot.Pages[0].Strokes.Single(value=>value.StrokeId==stroke).LayerId);
            Assert.Equal(Stable(document.Render().Svg),Stable(reopened.Render().Svg));
        }
        document.SetNativeUserLayerLocked(page.PageId,original,true,Request(document));var locked=document.Serialize();var lockedNative=document.ExportRnote();
        Assert.Throws<InvalidOperationException>(()=>document.DrawStrokeIntoNativeUserLayer(Samples(),Request(document),original));
        Assert.Equal(locked,document.Serialize());Assert.Equal(lockedNative,document.ExportRnote());
    }
    [Fact]
    public void Empty_native_layers_use_real_background_frame_without_invented_strokes_or_nonempty_rank_batch()
    {
        using var document=CanvasRnoteDocument.Create("Empty native layers");var page=document.Snapshot.Pages[0];var blank=Stable(document.Render().Svg);
        var layer=Guid.NewGuid();document.CreateNativeUserLayer(page.PageId,layer,"Empty",0,Request(document));
        Assert.Empty(document.Snapshot.Pages[0].Strokes);Assert.Equal(blank,Stable(document.Render().Svg));
        document.ReorderNativeUserLayer(page.PageId,layer,1,Request(document));document.SetNativeUserLayerVisibility(page.PageId,layer,false,Request(document));
        document.SetNativeUserLayerVisibility(page.PageId,page.Layers[0].LayerId,false,Request(document));Assert.Equal(blank,Stable(document.Render().Svg));
        using var reopened=CanvasRnoteDocument.Open(document.Serialize());Assert.Empty(reopened.Snapshot.Pages[0].Strokes);Assert.Equal(blank,Stable(reopened.Render().Svg));
        Assert.Equal(new[]{page.Layers[0].LayerId,layer},reopened.Snapshot.Pages[0].LayerOrder);
        reopened.SelectNativeUserLayer(page.PageId,layer);reopened.SetNativeUserLayerVisibility(page.PageId,layer,true,Request(reopened));
        var stroke=reopened.DrawStrokeIntoNativeUserLayer(Samples(),Request(reopened),reopened.ActiveNativeUserLayerId);Assert.Equal(layer,Assert.Single(reopened.Snapshot.Pages[0].Strokes).LayerId);
        using var written=CanvasRnoteDocument.Open(reopened.Serialize());Assert.Equal(stroke,Assert.Single(written.Snapshot.Pages[0].Strokes).StrokeId);Assert.NotEqual(blank,Stable(written.Render().Svg));
    }
    [Fact]
    public void Original_single_layer_ink_can_hide_lock_and_reopen_without_creating_a_fictitious_extra_layer()
    {
        using var document=CanvasRnoteDocument.Create("Original single layer");var page=document.Snapshot.Pages[0];var layer=page.Layers[0].LayerId;
        var stroke=document.DrawStroke(Samples(),Request(document));var before=document.Serialize();var native=document.ExportRnote();var visible=Stable(document.Render().Svg);
        var request=Request(document);document.SetNativeUserLayerVisibility(page.PageId,layer,false,request);
        var hidden=Stable(document.Render().Svg);Assert.NotEqual(visible,hidden);Assert.Equal(native,document.ExportRnote());
        Assert.Equal(layer,Assert.Single(document.Snapshot.Pages[0].Layers).LayerId);Assert.Equal(stroke,Assert.Single(document.Snapshot.Pages[0].Strokes).StrokeId);
        var bytes=document.Serialize();document.SetNativeUserLayerVisibility(page.PageId,layer,false,request);Assert.Equal(bytes,document.Serialize());
        using(var reopened=CanvasRnoteDocument.Open(bytes))
        {
            Assert.Equal(hidden,Stable(reopened.Render().Svg));reopened.Undo(Request(reopened));Assert.Equal(visible,Stable(reopened.Render().Svg));
            reopened.Redo(Request(reopened));Assert.Equal(hidden,Stable(reopened.Render().Svg));Assert.Single(reopened.Snapshot.Pages[0].Layers);
        }
        using var original=CanvasRnoteDocument.Open(before);original.SetNativeUserLayerLocked(page.PageId,layer,true,Request(original));
        var locked=original.Serialize();var lockedNative=original.ExportRnote();Assert.Throws<InvalidOperationException>(()=>original.DrawStroke(Samples(),Request(original)));
        Assert.Equal(locked,original.Serialize());Assert.Equal(lockedNative,original.ExportRnote());Assert.Single(original.Snapshot.Pages[0].Layers);
        using var lockedReopened=CanvasRnoteDocument.Open(locked);Assert.True(Assert.Single(lockedReopened.Snapshot.Pages[0].Layers).IsLocked);
        lockedReopened.Undo(Request(lockedReopened));Assert.False(Assert.Single(lockedReopened.Snapshot.Pages[0].Layers).IsLocked);Assert.Equal(visible,Stable(lockedReopened.Render().Svg));
    }
    [Fact]
    public void Actual_native_layer_delete_retains_other_ink_and_physical_reopen_restores_deleted_ink_and_render_history()
    {
        var directory=Path.Combine(Path.GetTempPath(),"canvas-layer-delete-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            using var document=CanvasRnoteDocument.Create("Actual native deletion");var page=document.Snapshot.Pages[0];var originalLayer=page.Layers[0].LayerId;
            var removed=document.DrawStroke(Samples(),Request(document),new(CanvasRnoteInkKind.Solid,"#FFFF0000",20));
            var retainedLayer=Guid.NewGuid();document.CreateNativeUserLayer(page.PageId,retainedLayer,"Retained",null,Request(document));
            var retained=document.DrawStrokeIntoNativeUserLayer([new(200,20,.8),new(260,60,.8),new(320,100,.8)],Request(document),retainedLayer,new(CanvasRnoteInkKind.Solid,"#FF0000FF",20));
            document.SelectNativeUserLayer(page.PageId,originalLayer);
            var before=document.Serialize();var frameBefore=Stable(document.Render().Svg);var request=Request(document);
            document.DeleteNativeUserLayer(page.PageId,originalLayer,request);
            Assert.Equal(retainedLayer,Assert.Single(document.Snapshot.Pages[0].Layers).LayerId);
            Assert.Equal(retained,Assert.Single(document.Snapshot.Pages[0].Strokes).StrokeId);
            Assert.Equal(retainedLayer,document.ActiveNativeUserLayerId);
            var after=document.Serialize();var nativeAfter=document.ExportRnote();var frameAfter=Stable(document.Render().Svg);Assert.NotEqual(frameBefore,frameAfter);
            using(var engine=RnoteCanvasEngine.Open(nativeAfter))Assert.Single(engine.ReadStrokeKeys());
            document.DeleteNativeUserLayer(page.PageId,originalLayer,request);Assert.Equal(after,document.Serialize());Assert.Equal(nativeAfter,document.ExportRnote());
            var path=Path.Combine(directory,"deleted.9to1c");File.WriteAllBytes(path,after);
            using var reopened=CanvasRnoteDocument.Open(File.ReadAllBytes(path));Assert.Equal(frameAfter,Stable(reopened.Render().Svg));
            reopened.Undo(Request(reopened));Assert.Equal(frameBefore,Stable(reopened.Render().Svg));
            Assert.Equal(new[]{removed,retained}.Order(),reopened.Snapshot.Pages[0].Strokes.Select(stroke=>stroke.StrokeId).Order());
            using(var engine=RnoteCanvasEngine.Open(reopened.ExportRnote()))Assert.Equal(2,engine.ReadStrokeKeys().Length);
            reopened.Redo(Request(reopened));Assert.Equal(frameAfter,Stable(reopened.Render().Svg));Assert.Equal(retained,Assert.Single(reopened.Snapshot.Pages[0].Strokes).StrokeId);
            using var original=CanvasRnoteDocument.Open(before);Assert.Equal(frameBefore,Stable(original.Render().Svg));
            original.SetNativeUserLayerLocked(page.PageId,originalLayer,true,Request(original));var locked=original.Serialize();var lockedNative=original.ExportRnote();
            Assert.Throws<InvalidOperationException>(()=>original.DeleteNativeUserLayer(page.PageId,originalLayer,Request(original)));
            Assert.Equal(locked,original.Serialize());Assert.Equal(lockedNative,original.ExportRnote());
            Assert.Throws<InvalidOperationException>(()=>reopened.DeleteNativeUserLayer(page.PageId,retainedLayer,Request(reopened)));
            Assert.Equal(retained,Assert.Single(reopened.Snapshot.Pages[0].Strokes).StrokeId);
        }
        finally{Directory.Delete(directory,true);}
    }
    private static CanvasMutationRequest Request(CanvasRnoteDocument document)=>new(document.Snapshot.RevisionId,Guid.NewGuid(),new("actual-native-layer-owner","Native layer owner"));
    private static RnotePointerSample[] Samples()=>[new(20,20,.8),new(80,60,.8),new(140,100,.8)];
    private static string Stable(byte[] bytes)
    {
        var svg=Encoding.UTF8.GetString(bytes);var ids=Regex.Matches(svg,"id=\"([^\"]+)\"").Select(match=>match.Groups[1].Value).Distinct().ToArray();
        for(var index=0;index<ids.Length;index++)svg=svg.Replace(ids[index],"nativeLayerResource"+index,StringComparison.Ordinal);
        return svg;
    }
}
