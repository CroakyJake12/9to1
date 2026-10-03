using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed partial class CanvasHomeNativeInsertionOperationTests
{
    [Theory]
    [InlineData(CanvasNativeLayerEditKind.Rename)]
    [InlineData(CanvasNativeLayerEditKind.Reorder)]
    [InlineData(CanvasNativeLayerEditKind.MoveStroke)]
    public async Task Original_Home_rename_reorder_or_stroke_move_reopens_exact_native_layer_and_history(CanvasNativeLayerEditKind kind)
    {
        await using var fixture=await Fixture.Create();await Add(fixture,[new(100,100,.5),new(300,100,.5)]);
        var first=await fixture.Files.OpenAsync(fixture.FileId);var originalLayer=first.Artifact.Pages.Single().LayerOrder[0];
        var added=Guid.NewGuid();var store=fixture.LayerStore();
        using(var display=await fixture.CaptureDisplay())
        {
            var create=store.Capture(display,new(CanvasNativeLayerEditKind.Create,first.Artifact.Pages.Single().PageId,added,"Second original layer",0));
            var cap=await fixture.ApproveLayer(create);
            var ack=await new CanvasHomeNativeLayerOperation(fixture.Home,fixture.Actors,store).ExecuteAsync(create,cap);
            Assert.True((await fixture.Home.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,"CANVAS_LAYER_CREATED",
                "Actual original layer created before next captured edit.",[new("files.item",fixture.FileId.ToString())]))).Succeeded);
            Assert.NotEqual(first.CasRevisionId,ack.FilesRevision.Id);
        }
        var baseline=await fixture.Files.OpenAsync(fixture.FileId);var page=baseline.Artifact.Pages.Single();var stroke=Assert.Single(page.Strokes);
        using var original=await fixture.CaptureDisplay();
        CanvasNativeLayerEditCommand command=kind switch
        {
            CanvasNativeLayerEditKind.Rename=>new(kind,page.PageId,originalLayer,Name:"Renamed exact original"),
            CanvasNativeLayerEditKind.Reorder=>new(kind,page.PageId,originalLayer,Position:0),
            _=>new(kind,page.PageId,added,StrokeId:stroke.StrokeId)
        };
        var drive=await File.ReadAllBytesAsync(fixture.StatePath);var home=await fixture.HomeBytes();
        var intent=store.Capture(original,command);Assert.Equal(drive,await File.ReadAllBytesAsync(fixture.StatePath));Assert.Equal(home,await fixture.HomeBytes());
        var capability=await fixture.ApproveLayer(intent);
        var committed=await new CanvasHomeNativeLayerOperation(fixture.Home,fixture.Actors,store).ExecuteAsync(intent,capability);
        Assert.Equal(baseline.CasRevisionId,committed.FilesRevision.ParentRevisionId);
        var reopened=await fixture.Files.OpenAsync(fixture.FileId,intent.StoreId);
        Assert.Equal(committed.FilesRevision.Id,reopened.CasRevisionId);
        Assert.Equal(intent.PreparedHash,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(CanvasArtifactCodec.Serialize(reopened.Artifact))));
        using var native=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(reopened.Artifact));var after=native.Snapshot.Pages.Single();
        Assert.Equal(page.StrokeOrder,after.StrokeOrder);Assert.Equal(stroke.StrokeId,Assert.Single(after.Strokes).StrokeId);
        Assert.Equal(stroke.Samples,Assert.Single(after.Strokes).Samples);
        if(kind==CanvasNativeLayerEditKind.Rename)Assert.Equal("Renamed exact original",after.Layers.Single(x=>x.LayerId==originalLayer).Name);
        if(kind==CanvasNativeLayerEditKind.Reorder)Assert.Equal(new[]{originalLayer,added},after.LayerOrder);
        if(kind==CanvasNativeLayerEditKind.MoveStroke)Assert.Equal(added,Assert.Single(after.Strokes).LayerId);
        native.Undo(LayerRequest(native));var undone=native.Snapshot.Pages.Single();
        Assert.Equal(page.LayerOrder,undone.LayerOrder);Assert.Equal(stroke.LayerId,Assert.Single(undone.Strokes).LayerId);
        Assert.Equal(page.Layers.Single(x=>x.LayerId==originalLayer).Name,undone.Layers.Single(x=>x.LayerId==originalLayer).Name);
        native.Redo(LayerRequest(native));var restored=native.Snapshot.Pages.Single();
        Assert.Equal(after.LayerOrder,restored.LayerOrder);Assert.Equal(after.Layers.Single(x=>x.LayerId==originalLayer).Name,restored.Layers.Single(x=>x.LayerId==originalLayer).Name);
        Assert.Equal(Assert.Single(after.Strokes).LayerId,Assert.Single(restored.Strokes).LayerId);
        Assert.True((await fixture.Home.CompleteExecutionAsync(capability,new(HomePermissionRequestState.Succeeded,"CANVAS_LAYER_COMMITTED",
            "Exact original native layer edit acknowledged.",[new("files.item",fixture.FileId.ToString())]))).Succeeded);
        var final=await File.ReadAllBytesAsync(fixture.StatePath);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new CanvasHomeNativeLayerOperation(fixture.Home,fixture.Actors,store).ExecuteAsync(intent,capability));
        Assert.Equal(final,await File.ReadAllBytesAsync(fixture.StatePath));
    }
}
