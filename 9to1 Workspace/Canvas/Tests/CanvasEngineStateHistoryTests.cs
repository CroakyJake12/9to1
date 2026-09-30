using System.Text.Json;
using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasEngineStateHistoryTests
{
    [Fact]
    public void Stroke_and_derived_settings_commit_once_and_restore_as_the_same_history_operation()
    {
        var artifact = CanvasArtifact.Create();
        artifact.DocumentSettings.Properties["engine"] = JsonSerializer.SerializeToElement("before");
        var session = new CanvasArtifactSession(artifact);
        var page = artifact.Pages[0];
        var actor = new CanvasActorContext("verified-test-actor", "Actor");
        var request = new CanvasMutationRequest(artifact.RevisionId, Guid.NewGuid(), actor);
        var stroke = new CanvasInkStroke { LayerId = page.LayerOrder[0], Samples = [new(10, 20), new(30, 40)] };
        var captures = 0;
        CanvasDocumentSettings Capture()
        {
            captures++;
            return new() { Properties = new() { ["engine"] = JsonSerializer.SerializeToElement("after") } };
        }
        var committed = session.AddStructuredStroke(request, page.PageId, stroke, Capture);
        Assert.True(committed.IsSuccess);
        Assert.Equal(1, captures);
        Assert.True(session.AddStructuredStroke(request, page.PageId, stroke, Capture).IsSuccess);
        Assert.Equal(1, captures);
        var snapshot = session.GetArtifactSnapshot();
        Assert.Equal("after", snapshot.DocumentSettings.Properties["engine"].GetString());
        Assert.Equal(stroke.StrokeId, Assert.Single(snapshot.Pages[0].Strokes).StrokeId);
        Assert.True(session.Undo(new(snapshot.RevisionId, Guid.NewGuid(), actor)).IsSuccess);
        snapshot = session.GetArtifactSnapshot();
        Assert.Empty(snapshot.Pages[0].Strokes);
        Assert.Equal("before", snapshot.DocumentSettings.Properties["engine"].GetString());
        Assert.True(session.Redo(new(snapshot.RevisionId, Guid.NewGuid(), actor)).IsSuccess);
        snapshot = session.GetArtifactSnapshot();
        Assert.Equal(stroke.StrokeId, Assert.Single(snapshot.Pages[0].Strokes).StrokeId);
        Assert.Equal("after", snapshot.DocumentSettings.Properties["engine"].GetString());
        var stale = session.AddStructuredStroke(new(artifact.RevisionId, Guid.NewGuid(), actor), page.PageId,
            stroke with { StrokeId = Guid.NewGuid() }, Capture);
        Assert.Equal(CanvasApiErrorCode.RevisionConflict, stale.Error!.Code);
        Assert.Equal(1, captures);
    }
}
