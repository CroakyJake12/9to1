using Haven.Application;
using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class NativeRnoteStrokeMutationTests
{
    [Fact]
    public void Keyed_edits_preserve_other_native_entities_and_reject_invalid_edits_without_changes()
    {
        using var engine = RnoteCanvasEngine.Create();
        Assert.True(engine.SupportsStructuredStrokeMutation);
        engine.DrawStroke([new(10, 20, .2), new(40, 60, .7)]);
        var first = Assert.Single(engine.ReadStrokeKeys());
        engine.DrawStroke([new(500, 600, .4), new(540, 660, .8)]);
        var second = Assert.Single(engine.ReadStrokeKeys().Where(key => key != first));
        var untouched = engine.ExportSelectedStrokes([second]);
        var firstBefore = engine.ExportSelectedStrokes([first]);
        engine.TranslateStroke(first, 100, 200);
        Assert.False(SameNative(firstBefore, engine.ExportSelectedStrokes([first])));
        engine.TranslateStroke(first, -100, -200);
        Assert.True(SameNative(firstBefore, engine.ExportSelectedStrokes([first])));
        using var reopened = RnoteCanvasEngine.Open(engine.Save());
        Assert.Equal(engine.ReadStrokeKeys().ToArray(), reopened.ReadStrokeKeys().ToArray());
        Assert.True(SameNative(untouched, reopened.ExportSelectedStrokes([second])));
        var beforeFailure = reopened.Save();
        Assert.Throws<ArgumentException>(() => reopened.TranslateStroke(first, double.NaN, 0));
        Assert.Throws<InvalidOperationException>(() => reopened.DeleteStroke(ulong.MaxValue));
        Assert.True(SameNative(beforeFailure, reopened.Save()));
        reopened.DeleteStroke(first);
        Assert.Equal(second, Assert.Single(reopened.ReadStrokeKeys()));
        Assert.True(SameNative(untouched, reopened.ExportSelectedStrokes([second])));
    }

    [Fact]
    public void Canonical_edits_atomically_retain_identity_pressure_brush_replay_and_history()
    {
        using var document = CanvasRnoteDocument.Create();
        var actor = new CanvasActorContext("stroke-owner", "Stroke owner");
        var first = document.DrawStroke([new(10, 20, .2, .1, .2), new(40, 60, .7, .3, .4)], document.Snapshot.RevisionId);
        var second = document.DrawStroke([new(500, 600, .4), new(540, 660, .8)], document.Snapshot.RevisionId);
        var original = document.Snapshot.Pages[0].Strokes.Single(stroke => stroke.StrokeId == first);
        var untouched = document.ExportCanonicalStrokeSelection([second], document.Snapshot.RevisionId);
        var translation = new CanvasMutationRequest(document.Snapshot.RevisionId, Guid.NewGuid(), actor);
        document.TranslateStroke(first, 100, 200, translation);
        var moved = document.Snapshot.Pages[0].Strokes.Single(stroke => stroke.StrokeId == first);
        Assert.Equal(original.LayerId, moved.LayerId);
        Assert.Equal(JsonSerializer.Serialize(original.ResolvedBrushProperties), JsonSerializer.Serialize(moved.ResolvedBrushProperties));
        Assert.Equal(original.Samples.Select(sample => sample with { X = sample.X + 100, Y = sample.Y + 200 }), moved.Samples);
        var afterTranslate = document.Serialize();
        document.TranslateStroke(first, 100, 200, translation);
        Assert.Equal(afterTranslate, document.Serialize());
        Assert.True(SameNative(untouched, document.ExportCanonicalStrokeSelection([second], document.Snapshot.RevisionId)));
        var deletion = new CanvasMutationRequest(document.Snapshot.RevisionId, Guid.NewGuid(), actor);
        document.DeleteStroke(first, deletion);
        Assert.Equal(second, Assert.Single(document.Snapshot.Pages[0].Strokes).StrokeId);
        var afterDelete = document.Serialize();
        document.DeleteStroke(first, deletion);
        Assert.Equal(afterDelete, document.Serialize());
        document.Undo(new(document.Snapshot.RevisionId, Guid.NewGuid(), actor));
        Assert.Equal(moved.Samples, document.Snapshot.Pages[0].Strokes.Single(stroke => stroke.StrokeId == first).Samples);
        document.Undo(new(document.Snapshot.RevisionId, Guid.NewGuid(), actor));
        Assert.Equal(original.Samples, document.Snapshot.Pages[0].Strokes.Single(stroke => stroke.StrokeId == first).Samples);
        document.Redo(new(document.Snapshot.RevisionId, Guid.NewGuid(), actor));
        document.Redo(new(document.Snapshot.RevisionId, Guid.NewGuid(), actor));
        using var restarted = CanvasRnoteDocument.Open(document.Serialize());
        Assert.Equal(second, Assert.Single(restarted.Snapshot.Pages[0].Strokes).StrokeId);
        Assert.True(SameNative(untouched, restarted.ExportCanonicalStrokeSelection([second], restarted.Snapshot.RevisionId)));
    }
    private static bool SameNative(byte[] left, byte[] right)
    {
        using var leftStream = new GZipStream(new MemoryStream(left), CompressionMode.Decompress);
        using var rightStream = new GZipStream(new MemoryStream(right), CompressionMode.Decompress);
        using var leftJson = JsonDocument.Parse(leftStream);
        using var rightJson = JsonDocument.Parse(rightStream);
        return JsonElement.DeepEquals(leftJson.RootElement, rightJson.RootElement);
    }
}
