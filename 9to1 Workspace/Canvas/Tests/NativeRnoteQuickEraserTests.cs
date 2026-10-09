using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class NativeRnoteQuickEraserTests
{
    [Fact]
    public void Genuine_hitboxes_choose_last_rendered_overlap_read_only_and_survive_restart()
    {
        using var engine = RnoteCanvasEngine.Create();
        Assert.True(engine.SupportsQuickEraseTarget);
        engine.DrawStroke([new(100, 100, .4), new(300, 100, .8)]);
        var first = Assert.Single(engine.ReadStrokeKeys());
        engine.DrawStroke([new(100, 100, .7), new(300, 100, .5)]);
        var last = Assert.Single(engine.ReadStrokeKeys().Where(key => key != first));
        var before = engine.Save();
        Assert.Equal(new[] { first, last }, engine.ReadRenderedStrokeKeys());
        Assert.Equal(last, engine.FindQuickEraseTarget(200, 100));
        Assert.Null(engine.FindQuickEraseTarget(200, 300));
        Assert.Throws<ArgumentException>(() => engine.FindQuickEraseTarget(double.NaN, 100));
        Assert.Equal(before, engine.Save());
        using var reopened = RnoteCanvasEngine.Open(before);
        Assert.Equal(last, reopened.FindQuickEraseTarget(200, 100));
        reopened.DeleteStroke(last);
        Assert.Equal(first, reopened.FindQuickEraseTarget(200, 100));
        Assert.Equal(last, engine.FindQuickEraseTarget(200, 100));
    }

    [Fact]
    public void Quick_erase_uses_shared_keyed_deletion_and_durable_common_undo_without_retargeting()
    {
        using var document = CanvasRnoteDocument.Create();
        var first = document.DrawStroke([new(100, 100, .4), new(300, 100, .8)], document.Snapshot.RevisionId);
        var last = document.DrawStroke([new(100, 100, .7), new(300, 100, .5)], document.Snapshot.RevisionId);
        var before = document.Serialize();
        Assert.Equal(last, document.PreviewQuickErase(200, 100, document.Identity.RevisionId));
        Assert.Null(document.PreviewQuickErase(200, 300, document.Identity.RevisionId));
        Assert.Equal(before, document.Serialize());
        var actor = new CanvasActorContext("quick-test", "Quick eraser owner");
        var request = new CanvasMutationRequest(document.Identity.RevisionId, Guid.NewGuid(), actor);
        document.DeleteStroke(last, request);
        var after = document.Serialize();
        document.DeleteStroke(last, request); // Same exact operation does not erase the newly exposed neighbor.
        Assert.Equal(after, document.Serialize());
        Assert.Equal(first, document.PreviewQuickErase(200, 100, document.Identity.RevisionId));
        using var restarted = CanvasRnoteDocument.Open(after);
        restarted.Undo(new(restarted.Identity.RevisionId, Guid.NewGuid(), actor));
        Assert.Equal(last, restarted.PreviewQuickErase(200, 100, restarted.Identity.RevisionId));
        Assert.Equal(2, restarted.Snapshot.Pages[0].Strokes.Count);
        Assert.Throws<InvalidOperationException>(() => restarted.PreviewQuickErase(200, 100, request.BaseRevisionId));
    }
    [Fact]
    public void Donor_hitboxes_do_not_use_whole_entity_bounds_and_unbound_or_locked_top_hits_never_fall_through()
    {
        using var engine = RnoteCanvasEngine.Create();
        engine.DrawStroke([new(100,100,.5),new(300,300,.5)]);
        Assert.Null(engine.FindQuickEraseTarget(100,300)); // Inside entity AABB, outside donor segment hitboxes.
        using var imported = CanvasRnoteDocument.Import(engine.Save(), "rnote");
        var importedBytes = imported.Serialize();
        Assert.Throws<NotSupportedException>(() => imported.PreviewQuickErase(200,200,imported.Identity.RevisionId));
        Assert.Equal(importedBytes, imported.Serialize());
        using var document = CanvasRnoteDocument.Create();
        document.DrawStroke([new(100,100,.5),new(300,100,.5)], document.Identity.RevisionId);
        document.DrawStroke([new(100,100,.5),new(300,100,.5)], document.Identity.RevisionId);
        var artifact = document.Snapshot;
        artifact.SemanticHistory = null; // Controlled lock fixture; no live execution authority is encoded.
        var page = artifact.Pages[0];
        page.Layers[0] = page.Layers[0] with { IsLocked = true };
        using var locked = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(artifact));
        var lockedBytes = locked.Serialize();
        Assert.Throws<InvalidOperationException>(() => locked.PreviewQuickErase(200,100,locked.Identity.RevisionId));
        Assert.Equal(lockedBytes, locked.Serialize());
    }

    [Fact]
    public void Quick_erase_refuses_canonical_donor_order_disagreement_without_guessing_a_topmost_target()
    {
        using var document = CanvasRnoteDocument.Create();
        document.DrawStroke([new(100,100,.5),new(300,100,.5)], document.Identity.RevisionId);
        document.DrawStroke([new(100,100,.5),new(300,100,.5)], document.Identity.RevisionId);
        var artifact = document.Snapshot;
        artifact.SemanticHistory = null;
        artifact.Pages[0].StrokeOrder.Reverse();
        using var reordered = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(artifact));
        var bytes = reordered.Serialize();
        Assert.Throws<NotSupportedException>(() => reordered.PreviewQuickErase(200,100,reordered.Identity.RevisionId));
        Assert.Equal(bytes, reordered.Serialize());
    }

}
