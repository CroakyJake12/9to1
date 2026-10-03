using System.Text;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed partial class CanvasHomeNativeInsertionOperationTests
{
    [Fact]
    public async Task Exact_reviewed_layer_delete_commits_real_Files_native_ink_removal_and_reopens_restore_history()
    {
        await using var f=await Fixture.Create();await Add(f,[new(100,100,.5),new(300,100,.5)]);
        var store=f.LayerStore();var first=await f.Files.OpenAsync(f.FileId);var page=first.Artifact.Pages.Single();var deletedLayer=page.LayerOrder[0];var retainedLayer=Guid.NewGuid();
        using(var createDisplay=await f.CaptureDisplay())
        {
            var create=store.Capture(createDisplay,new(CanvasNativeLayerEditKind.Create,page.PageId,retainedLayer,"Retained",null));
            var createCap=await f.ApproveLayer(create);var createAck=await new CanvasHomeNativeLayerOperation(f.Home,f.Actors,store).ExecuteAsync(create,createCap);
            Assert.True((await f.Home.CompleteExecutionAsync(createCap,new(HomePermissionRequestState.Succeeded,"CANVAS_LAYER_CREATED","Actual retained layer committed.",[new("files.item",f.FileId.ToString())]))).Succeeded);
            Assert.NotEqual(create.FilesRevision,createAck.FilesRevision.Id);
        }
        var opened=await f.Files.OpenAsync(f.FileId);var before=CanvasArtifactCodec.Serialize(opened.Artifact);using var originalNative=CanvasRnoteDocument.Open(before);
        var originalInk=Assert.Single(opened.Artifact.Pages.Single().Strokes).StrokeId;
        using var display=await f.CaptureDisplay();var home=await f.HomeBytes();var drive=await File.ReadAllBytesAsync(f.StatePath);
        Assert.Throws<ArgumentException>(()=>store.Capture(display,new(CanvasNativeLayerEditKind.Delete,page.PageId,deletedLayer,Name:"Ignored substitute")));
        var intent=store.Capture(display,new(CanvasNativeLayerEditKind.Delete,page.PageId,deletedLayer));
        Assert.Equal(home,await f.HomeBytes());Assert.Equal(drive,await File.ReadAllBytesAsync(f.StatePath));
        var cap=await f.ApproveLayer(intent);var operation=new CanvasHomeNativeLayerOperation(f.Home,f.Actors,store);var ack=await operation.ExecuteAsync(intent,cap);
        var current=await f.Files.OpenAsync(f.FileId,intent.StoreId);Assert.Equal(ack.FilesRevision.Id,current.CasRevisionId);
        Assert.Equal(retainedLayer,Assert.Single(current.Artifact.Pages.Single().Layers).LayerId);Assert.Empty(current.Artifact.Pages.Single().Strokes);
        Assert.Equal(intent.PreparedHash,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(CanvasArtifactCodec.Serialize(current.Artifact))));
        using var native=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(current.Artifact));using(var donor=RnoteCanvasEngine.Open(native.ExportRnote()))Assert.Empty(donor.ReadStrokeKeys());
        native.Undo(LayerRequest(native));Assert.Equal(originalInk,Assert.Single(native.Snapshot.Pages.Single().Strokes).StrokeId);
        using(var donor=RnoteCanvasEngine.Open(native.ExportRnote()))Assert.Single(donor.ReadStrokeKeys());
        native.Redo(LayerRequest(native));Assert.Empty(native.Snapshot.Pages.Single().Strokes);Assert.Equal(retainedLayer,Assert.Single(native.Snapshot.Pages.Single().Layers).LayerId);
        Assert.True((await f.Home.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,"CANVAS_LAYER_DELETED","Exact native layer deletion acknowledged.",[new("files.item",f.FileId.ToString())]))).Succeeded);
        var committed=await File.ReadAllBytesAsync(f.StatePath);await Assert.ThrowsAsync<InvalidOperationException>(()=>operation.ExecuteAsync(intent,cap));Assert.Equal(committed,await File.ReadAllBytesAsync(f.StatePath));
    }
}
