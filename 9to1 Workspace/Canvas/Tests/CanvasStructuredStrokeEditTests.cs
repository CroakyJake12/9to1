using System.Text.Json;
using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasStructuredStrokeEditTests
{
    [Fact]
    public void Edits_check_revision_samples_locks_and_replay_before_capturing_donor_state()
    {
        var artifact = CanvasArtifact.Create();
        var page = artifact.Pages[0];
        var stroke = new CanvasInkStroke { LayerId = page.LayerOrder[0], Samples = [new(10, 20, .3), new(30, 40, .8)] };
        page.Strokes.Add(stroke); page.StrokeOrder.Add(stroke.StrokeId);
        var session = new CanvasArtifactSession(artifact);
        var actor = new CanvasActorContext("stroke-test", "Stroke test");
        CanvasMutationRequest Request() => new(session.CurrentRevisionId, Guid.NewGuid(), actor);
        var calls = 0;
        CanvasDocumentSettings Capture()
        {
            calls++;
            return new() { Properties = new() { ["donor"] = JsonSerializer.SerializeToElement("edited") } };
        }
        var before = CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot());
        Assert.Equal(CanvasApiErrorCode.RevisionConflict,
            session.DeleteStructuredStroke(new(Guid.NewGuid(), Guid.NewGuid(), actor), page.PageId, stroke.StrokeId, Capture).Error!.Code);
        Assert.Equal(CanvasApiErrorCode.InvalidArgument,
            session.TranslateStructuredStroke(Request(), page.PageId, stroke.StrokeId, double.NaN, 0, Capture).Error!.Code);
        Assert.True(session.TranslateStructuredStroke(Request(), page.PageId, stroke.StrokeId, 0, 0, Capture).IsSuccess);
        Assert.Throws<InvalidOperationException>(() => session.DeleteStructuredStroke(Request(), page.PageId, stroke.StrokeId,
            () => throw new InvalidOperationException("Actual donor candidate failure")));
        Assert.Throws<InvalidOperationException>(() => session.DeleteStructuredStroke(Request(), page.PageId, stroke.StrokeId,
            () => new CanvasDocumentSettings { Properties = new() { ["donor"] = default } }));
        Assert.Equal(before, CanvasArtifactCodec.Serialize(session.GetArtifactSnapshot()));
        Assert.Equal(0, calls);
        var translation = Request();
        Assert.True(session.TranslateStructuredStroke(translation, page.PageId, stroke.StrokeId, 100, 200, Capture).IsSuccess);
        Assert.True(session.TranslateStructuredStroke(translation, page.PageId, stroke.StrokeId, 100, 200, Capture).IsSuccess);
        Assert.Equal(1, calls);
        Assert.Equal(CanvasApiErrorCode.OperationIdConflict,
            session.TranslateStructuredStroke(translation, page.PageId, stroke.StrokeId, 900, 200, Capture).Error!.Code);
        var moved = Assert.Single(session.GetArtifactSnapshot().Pages[0].Strokes);
        Assert.Equal(stroke.Samples.Select(sample => sample with { X = sample.X + 100, Y = sample.Y + 200 }), moved.Samples);
        Assert.True(session.SetLayerLocked(Request(), page.PageId, stroke.LayerId, true).IsSuccess);
        Assert.Equal(CanvasApiErrorCode.PermissionDenied,
            session.DeleteStructuredStroke(Request(), page.PageId, stroke.StrokeId, Capture).Error!.Code);
        Assert.Equal(1, calls);
        Assert.True(session.SetLayerLocked(Request(), page.PageId, stroke.LayerId, false).IsSuccess);
        var deletion = Request();
        Assert.True(session.DeleteStructuredStroke(deletion, page.PageId, stroke.StrokeId, Capture).IsSuccess);
        Assert.True(session.DeleteStructuredStroke(deletion, page.PageId, stroke.StrokeId, Capture).IsSuccess);
        Assert.Equal(2, calls);
        Assert.Empty(session.GetArtifactSnapshot().Pages[0].Strokes);
        Assert.Empty(session.GetArtifactSnapshot().Pages[0].StrokeOrder);
        Assert.True(session.Undo(Request()).IsSuccess);
        Assert.Equal(moved.Samples, Assert.Single(session.GetArtifactSnapshot().Pages[0].Strokes).Samples);
    }
}
