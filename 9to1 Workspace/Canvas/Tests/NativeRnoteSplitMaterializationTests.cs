using Haven.Application;
using Haven.Application.Canvas;
using System.IO.Compression;
using System.Text.Json;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed class NativeRnoteSplitMaterializationTests
{
    internal static RnotePointerSample[] LongStroke() => Enumerable.Range(0,36)
        .Select(index => new RnotePointerSample(20 + index*18,100,.2+index*.01,.1,.2)).ToArray();
    internal static RnotePointerSample[] GenuineMiddleGesture(byte[] native)
    {
        using var zip = new GZipStream(new MemoryStream(native), CompressionMode.Decompress);
        using var document = JsonDocument.Parse(zip);
        var brush = document.RootElement.GetProperty("data").GetProperty("engine_snapshot").GetProperty("stroke_components")
            .EnumerateArray().Select(slot => slot.GetProperty("value")).First(value => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("brushstroke",out _));
        var segments = brush.GetProperty("brushstroke").GetProperty("path").GetProperty("segments");
        Assert.True(segments.GetArrayLength()>8,"The genuine fixture must have retained segments on both sides.");
        // Natural's time-sensitive smoothing may retain many tiny segments
        // near the start. Segment-count midpoint is not a spatial middle cut.
        // Target an actual retained endpoint nearest the straight fixture's
        // spatial midpoint; no geometry is manufactured or approximated.
        var path = brush.GetProperty("brushstroke").GetProperty("path");
        var start = path.GetProperty("start").GetProperty("pos");
        var endpoints = segments.EnumerateArray().Select(segment =>
            segment.EnumerateObject().Single().Value.GetProperty("end").GetProperty("pos")).ToArray();
        var end = endpoints[^1];
        var targetX = (start[0].GetDouble()+end[0].GetDouble())*.5;
        var targetY = (start[1].GetDouble()+end[1].GetDouble())*.5;
        var spanX = end[0].GetDouble()-start[0].GetDouble();
        Assert.True(spanX>100,"The genuine straight fixture must retain spatial extent.");
        Assert.All(endpoints,point=>Assert.Equal(start[1].GetDouble(),point[1].GetDouble()));
        var index = Enumerable.Range(2,endpoints.Length-4).MinBy(index =>
            Math.Pow(endpoints[index][0].GetDouble()-targetX,2)+Math.Pow(endpoints[index][1].GetDouble()-targetY,2));
        var point = endpoints[index];
        Assert.InRange((point[0].GetDouble()-start[0].GetDouble())/spanX,.25,.75);
        Assert.InRange(index,2,endpoints.Length-3);
        return [new(point[0].GetDouble(),point[1].GetDouble(),.5),new(point[0].GetDouble(),point[1].GetDouble(),.5)];
    }
    [Fact]
    public void Genuine_split_candidate_preserves_original_and_reports_exact_retained_paths_and_keys()
    {
        using var engine = RnoteCanvasEngine.Create();
        Assert.True(engine.SupportsSplitEraseCandidate);
        engine.DrawStroke(LongStroke());
        var original = engine.Save(); var originalKey = Assert.Single(engine.ReadStrokeKeys());
        var candidate = engine.CreateSplitEraseCandidate(GenuineMiddleGesture(original),1);
        Assert.Equal(original,engine.Save());
        using var receipt = JsonDocument.Parse(candidate.CopyReceipt());
        var changes = receipt.RootElement.GetProperty("changes").EnumerateArray().ToArray();
        Assert.Equal(2,changes.Length);
        Assert.All(changes,change => Assert.Equal(originalKey,change.GetProperty("sourceNativeKey").GetUInt64()));
        Assert.All(changes,change => Assert.NotEmpty(change.GetProperty("matchingSourceSegmentOffsets").EnumerateArray()));
        using var reopened = RnoteCanvasEngine.Open(candidate.CopyNative());
        Assert.Equal(2,reopened.ReadStrokeKeys().Length);
        Assert.Contains(originalKey,reopened.ReadStrokeKeys());
        Assert.Throws<ArgumentOutOfRangeException>(()=>engine.CreateSplitEraseCandidate(GenuineMiddleGesture(original),double.NaN));
        Assert.Equal(original,engine.Save());
    }
    [Fact]
    public void Split_is_one_schema2_transaction_with_exact_input_provenance_full_order_replay_and_fresh_history()
    {
        using var document = CanvasRnoteDocument.Create();
        var sourceId = document.DrawStroke(LongStroke(),document.Identity.RevisionId);
        var source = document.Snapshot.Pages[0].Strokes.Single();
        document.DrawStroke([new(1000,1000,.4),new(1100,1100,.8)],document.Identity.RevisionId);
        var before = document.Serialize();
        var preview = document.PreviewSplitErase(GenuineMiddleGesture(document.ExportRnote()),1,document.Identity.RevisionId);
        Assert.Equal(before,document.Serialize());
        Assert.Single(preview.FragmentIdentities);
        var request = new CanvasMutationRequest(document.Identity.RevisionId,Guid.NewGuid(),new("split-owner","Split owner"));
        document.ApplySplitErase(preview,request);
        var snapshot = document.Snapshot;
        Assert.Equal(2,snapshot.SchemaVersion);
        Assert.Equal(3,snapshot.Pages[0].Strokes.Count);
        Assert.Contains(snapshot.Pages[0].Strokes,stroke=>stroke.StrokeId==sourceId);
        var fragments = snapshot.Pages[0].Strokes.Where(stroke=>stroke.PathGeometry is not null).ToArray();
        Assert.Equal(2,fragments.Length);
        Assert.All(fragments,stroke=>Assert.Equal(source.Samples,stroke.Samples));
        Assert.All(fragments,stroke=>Assert.Equal(source.LayerId,stroke.LayerId));
        Assert.Equal(preview.StrokeOrder,snapshot.Pages[0].StrokeOrder);
        var after = document.Serialize(); document.ApplySplitErase(preview,request); Assert.Equal(after,document.Serialize());
        using var reopened = CanvasRnoteDocument.Open(after);
        reopened.Undo(new(reopened.Identity.RevisionId,Guid.NewGuid(),request.ActorContext));
        Assert.Equal(2,reopened.Snapshot.Pages[0].Strokes.Count);
        Assert.Equal(source.Samples,reopened.Snapshot.Pages[0].Strokes.Single(stroke=>stroke.StrokeId==sourceId).Samples);
        reopened.Redo(new(reopened.Identity.RevisionId,Guid.NewGuid(),request.ActorContext));
        Assert.Equal(3,reopened.Snapshot.Pages[0].Strokes.Count);
        Assert.All(reopened.Snapshot.Pages[0].Strokes.Where(stroke=>stroke.PathGeometry is not null),stroke=>Assert.Equal(source.Samples,stroke.Samples));
    }
    [Fact]
    public void Locked_source_and_stale_revision_refuse_without_changing_original_native_or_history()
    {
        using var document = CanvasRnoteDocument.Create(); document.DrawStroke(LongStroke(),document.Identity.RevisionId);
        var samples = GenuineMiddleGesture(document.ExportRnote()); var artifact = document.Snapshot;
        artifact.SemanticHistory = null; artifact.Pages[0].Layers[0] = artifact.Pages[0].Layers[0] with { IsLocked = true };
        using var locked = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(artifact)); var bytes = locked.Serialize();
        Assert.Throws<InvalidOperationException>(()=>locked.PreviewSplitErase(samples,1,locked.Identity.RevisionId));
        Assert.Throws<InvalidOperationException>(()=>locked.PreviewSplitErase(samples,1,Guid.NewGuid()));
        Assert.Equal(bytes,locked.Serialize());
    }
    [Fact]
    public void Authoritative_path_native_mismatch_and_unimplemented_path_translation_refuse()
    {
        using var document = CanvasRnoteDocument.Create();
        document.DrawStroke(LongStroke(),document.Identity.RevisionId);
        var preview = document.PreviewSplitErase(GenuineMiddleGesture(document.ExportRnote()),1,document.Identity.RevisionId);
        var request = new CanvasMutationRequest(document.Identity.RevisionId,Guid.NewGuid(),new("split-owner","Split owner"));
        var actor = request.ActorContext;
        document.ApplySplitErase(preview,request);
        var before = document.Serialize(); var fragment = document.Snapshot.Pages[0].Strokes.First(stroke=>stroke.PathGeometry is not null);
        Assert.Throws<NotSupportedException>(()=>document.TranslateStroke(fragment.StrokeId,.00001,0,new(document.Identity.RevisionId,Guid.NewGuid(),actor)));
        Assert.Equal(before,document.Serialize());
        var tampered = document.Snapshot;
        tampered.SemanticHistory = null;
        var index = tampered.Pages[0].Strokes.FindIndex(stroke=>stroke.StrokeId==fragment.StrokeId);
        var geometry = fragment.PathGeometry!;
        tampered.Pages[0].Strokes[index] = fragment with
        {
            PathGeometry = geometry with { Start = geometry.Start with { X = geometry.Start.X + .125 } }
        };
        var bytes = CanvasArtifactCodec.Serialize(tampered);
        Assert.Throws<InvalidDataException>(()=>CanvasRnoteDocument.Open(bytes));
        Assert.Equal(before,document.Serialize());
    }
    [Fact]
    public void Oversized_adversarial_provenance_refuses_preview_before_clone_history_or_Home_intent()
    {
        using var original=CanvasRnoteDocument.Create();original.DrawStroke(LongStroke(),original.Identity.RevisionId);
        var oversized=original.Snapshot;oversized.SemanticHistory=null;
        var stroke=oversized.Pages[0].Strokes[0];
        // Adversarial data admission fixture, not claimed genuine recorded input or large-stroke parity.
        oversized.Pages[0].Strokes[0]=stroke with {Samples=Enumerable.Repeat(stroke.Samples[0],
            CanvasStructuredStrokeTransactionLimits.MaximumAggregateSampleAndSegmentEntries+1).ToList()};
        using var document=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(oversized));
        var before=document.Serialize();var gesture=GenuineMiddleGesture(document.ExportRnote());
        Assert.Throws<InvalidDataException>(()=>document.PreviewSplitErase(gesture,1,document.Identity.RevisionId));
        Assert.Equal(before,document.Serialize());
    }
}
