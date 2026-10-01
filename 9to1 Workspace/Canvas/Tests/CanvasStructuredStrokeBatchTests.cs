using System.Collections;
using System.Text.Json;
using Haven.Application;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed class CanvasStructuredStrokeBatchTests
{
    private static (CanvasArtifactSession Session, Guid Page, Guid Layer, Guid[] Keys) Fixture()
    {
        var artifact = CanvasArtifact.Create(); var page = artifact.Pages[0];
        var first = new CanvasInkStroke { LayerId = page.LayerOrder[0], Samples = [new(20,30,.2),new(80,90,.8)] };
        var second = new CanvasInkStroke { LayerId = page.LayerOrder[0], Samples = [new(500,600,.4),new(540,660,.7)] };
        page.Strokes.AddRange([first,second]); page.StrokeOrder.AddRange([first.StrokeId,second.StrokeId]);
        return (new(artifact),page.PageId,page.LayerOrder[0],[first.StrokeId,second.StrokeId]);
    }
    private static CanvasMutationRequest Request(CanvasArtifactSession session) => new(session.CurrentRevisionId,Guid.NewGuid(),new("batch-owner","Batch owner"));
    [Fact]
    public void Exact_batch_is_one_revision_history_frame_and_order_independent_replay()
    {
        var (session,page,_,keys) = Fixture(); var request = Request(session); var calls = 0;
        CanvasDocumentSettings Capture() { calls++; return new() { Properties = new() { ["donor"] = JsonSerializer.SerializeToElement("candidate") } }; }
        Assert.True(session.DeleteStructuredStrokes(request,page,keys,Capture).IsSuccess);
        var committed = CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.Single(session.GetArtifactSnapshot().SemanticHistory!.Undo);
        Assert.Empty(session.GetArtifactSnapshot().Pages[0].Strokes);
        Assert.True(session.DeleteStructuredStrokes(request,page,keys.Reverse().ToArray(),Capture).IsSuccess);
        Assert.Equal(committed,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot())); Assert.Equal(1,calls);
        Assert.Equal(CanvasApiErrorCode.OperationIdConflict,session.DeleteStructuredStrokes(request,page,[keys[0]],Capture).Error!.Code);
        Assert.True(session.Undo(Request(session)).IsSuccess);
        Assert.Equal(keys,session.GetArtifactSnapshot().Pages[0].StrokeOrder);
        Assert.Equal(2,session.GetArtifactSnapshot().Pages[0].Strokes.Count);
    }
    [Fact]
    public void Missing_duplicate_empty_locked_stale_and_throwing_candidates_preserve_entire_session()
    {
        var (session,page,layer,keys) = Fixture(); var calls = 0;
        CanvasDocumentSettings Capture() { calls++; return new(); }
        var before = CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.Equal(CanvasApiErrorCode.NotFound,session.DeleteStructuredStrokes(Request(session),page,[keys[0],Guid.NewGuid()],Capture).Error!.Code);
        Assert.Equal(CanvasApiErrorCode.InvalidArgument,session.DeleteStructuredStrokes(Request(session),page,[keys[0],keys[0]],Capture).Error!.Code);
        Assert.Equal(CanvasApiErrorCode.InvalidArgument,session.DeleteStructuredStrokes(Request(session),page,[],Capture).Error!.Code);
        Assert.Equal(CanvasApiErrorCode.RevisionConflict,session.DeleteStructuredStrokes(new(Guid.NewGuid(),Guid.NewGuid(),new("batch-owner","Batch owner")),page,keys,Capture).Error!.Code);
        Assert.Throws<InvalidOperationException>(() => session.DeleteStructuredStrokes(Request(session),page,keys,() => throw new InvalidOperationException("Actual donor candidate refused")));
        Assert.Throws<InvalidOperationException>(() => session.DeleteStructuredStrokes(Request(session),page,keys,() => new() { Properties = new() { ["donor"] = default } }));
        Assert.Equal(0,calls); Assert.Equal(before,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.True(session.SetLayerLocked(Request(session),page,layer,true).IsSuccess);
        before = CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.Equal(CanvasApiErrorCode.PermissionDenied,session.DeleteStructuredStrokes(Request(session),page,keys,Capture).Error!.Code);
        Assert.Equal(0,calls); Assert.Equal(before,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
    }
    [Fact]
    public void Actual_caller_enumeration_is_bounded_before_history_or_donor_capture()
    {
        var (session,page,_,_) = Fixture(); var oversized = new OversizedKeys(); var calls = 0;
        var before = CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.Equal(CanvasApiErrorCode.InvalidArgument,session.DeleteStructuredStrokes(Request(session),page,oversized,() => { calls++; return new(); }).Error!.Code);
        Assert.InRange(oversized.Yielded,1,1025); Assert.Equal(0,calls);
        Assert.Equal(before,CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
    }
    private sealed class OversizedKeys : IReadOnlyList<Guid>
    {
        public int Yielded { get; private set; }
        public int Count => 1;
        public Guid this[int index] => throw new InvalidOperationException("Do not trust the caller's indexer.");
        public IEnumerator<Guid> GetEnumerator() { for(var i=0;i<4096;i++){ Yielded++; yield return Guid.NewGuid(); } }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
