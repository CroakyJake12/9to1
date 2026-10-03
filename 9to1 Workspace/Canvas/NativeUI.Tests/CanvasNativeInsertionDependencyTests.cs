using Haven.Application;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed class CanvasNativeInsertionDependencyTests
{
    [Fact]
    public void Genuine_selector_preserves_original_canonical_native_bytes_order_and_history()
    {
        using var doc = CanvasRnoteDocument.Create();
        var first = doc.DrawStroke([new(100,100,.5),new(300,100,.5)],doc.Identity.RevisionId);
        var last = doc.DrawStroke([new(100,100,.5),new(300,100,.5)],doc.Identity.RevisionId);
        var before = doc.Serialize(); var native = doc.ExportRnote();
        Assert.Equal(new[] {last},doc.PreviewSelection(CanvasSelectionStyle.Single,[new(200,100,.5)],doc.Identity.RevisionId));
        Assert.Equal(new[] {first,last},doc.PreviewSelection(CanvasSelectionStyle.Rectangle,[new(50,50,.5),new(350,150,.5)],doc.Identity.RevisionId));
        Assert.Empty(doc.PreviewSelection(CanvasSelectionStyle.Single,[new(200,300,.5)],doc.Identity.RevisionId));
        Assert.Equal(before,doc.Serialize()); Assert.Equal(native,doc.ExportRnote());
        using var reopened = CanvasRnoteDocument.Open(before);
        Assert.Equal(new[] {last},reopened.PreviewSelection(CanvasSelectionStyle.Single,[new(200,100,.5)],reopened.Identity.RevisionId));
        Assert.Equal(before,reopened.Serialize());
    }

    [Theory]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public void Hidden_or_locked_selection_refuses_without_state_change(bool hidden,bool locked)
    {
        using var doc = CanvasRnoteDocument.Create();
        doc.DrawStroke([new(100,100,.5),new(300,100,.5)],doc.Identity.RevisionId);
        var artifact = doc.Snapshot; artifact.SemanticHistory = null;
        artifact.Pages[0].Layers[0] = artifact.Pages[0].Layers[0] with {IsVisible=!hidden,IsLocked=locked};
        using var restricted = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(artifact));
        var bytes = restricted.Serialize(); var native=restricted.ExportRnote();
        Assert.Throws<InvalidOperationException>(()=>restricted.PreviewSelection(CanvasSelectionStyle.Single,[new(200,100,.5)],restricted.Identity.RevisionId));
        Assert.Equal(bytes,restricted.Serialize()); Assert.Equal(native,restricted.ExportRnote());
    }

    [Fact]
    public void Stale_and_malformed_selector_input_never_changes_native_or_canonical_state()
    {
        using var doc = CanvasRnoteDocument.Create();
        doc.DrawStroke([new(100,100,.5),new(300,100,.5)],doc.Identity.RevisionId);
        var before=doc.Serialize(); var native=doc.ExportRnote();
        Assert.Throws<InvalidOperationException>(()=>doc.PreviewSelection(CanvasSelectionStyle.Single,[new(200,100,.5)],Guid.NewGuid()));
        Assert.Throws<ArgumentException>(()=>doc.PreviewSelection(CanvasSelectionStyle.Polygon,[new(200,100,.5)],doc.Identity.RevisionId));
        Assert.Throws<ArgumentException>(()=>doc.PreviewSelection(CanvasSelectionStyle.Single,[new(double.NaN,100,.5)],doc.Identity.RevisionId));
        Assert.Equal(before,doc.Serialize()); Assert.Equal(native,doc.ExportRnote());
    }
    [Fact]
    public void Exact_native_layer_dependencies_preserve_unrelated_empty_locked_layer_and_original_order()
    {
        using var doc = CanvasRnoteDocument.Create();
        var page = doc.Snapshot.Pages.Single(); var original = page.LayerOrder[0]; var unrelated = Guid.NewGuid();
        CanvasMutationRequest Request() => new(doc.Identity.RevisionId, Guid.NewGuid(), new("controlled-native-test", "Controlled native test"));
        doc.CreateNativeUserLayer(page.PageId, unrelated, "Unrelated locked layer", null, Request());
        doc.SetNativeUserLayerLocked(page.PageId, unrelated, true, Request());
        var stroke = doc.DrawStrokeIntoNativeUserLayer([new(100,100,.5),new(300,100,.5)], Request(), original);
        var before = doc.Serialize(); var native = doc.ExportRnote();
        Assert.Equal(new[] { stroke }, doc.PreviewSelection(CanvasSelectionStyle.Single, [new(200,100,.5)], doc.Identity.RevisionId));
        Assert.Empty(doc.PreviewSelection(CanvasSelectionStyle.Single, [new(200,300,.5)], doc.Identity.RevisionId));
        Assert.Equal(before, doc.Serialize()); Assert.Equal(native, doc.ExportRnote());
        using var reopened = CanvasRnoteDocument.Open(before);
        Assert.Equal(new[] { stroke }, reopened.PreviewSelection(CanvasSelectionStyle.Single, [new(200,100,.5)], reopened.Identity.RevisionId));
        Assert.Equal(before, reopened.Serialize());
    }
}
