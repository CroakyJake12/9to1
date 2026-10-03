using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed partial class CanvasHomeNativeInsertionOperationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Exact_original_native_layer_is_frozen_before_review_and_later_local_selection_cannot_redirect_ink(bool explicitTarget)
    {
        await using var fixture=await Fixture.Create();var layerStore=fixture.LayerStore();
        var original=(await fixture.Files.OpenAsync(fixture.FileId)).Artifact;var originalLayer=original.Pages.Single().LayerOrder[0];var first=Guid.NewGuid();
        using(var createDisplay=await fixture.CaptureDisplay())
        {
            var create=layerStore.Capture(createDisplay,new(CanvasNativeLayerEditKind.Create,original.Pages.Single().PageId,first,"First",0));
            var createCap=await fixture.ApproveLayer(create);
            await new CanvasHomeNativeLayerOperation(fixture.Home,fixture.Actors,layerStore).ExecuteAsync(create,createCap);
            Assert.True((await fixture.Home.CompleteExecutionAsync(createCap,new(HomePermissionRequestState.Succeeded,"LAYER_CREATED","Original native layer created.",[]))).Succeeded);
        }
        using var display=await fixture.CaptureDisplay();using var local=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(display.Opened.Artifact));
        var page=local.Snapshot.Pages.Single();local.SelectNativeUserLayer(page.PageId,explicitTarget?originalLayer:first);
        var selected=local.ActiveNativeUserLayerId;
        var intent=explicitTarget?CanvasNativeInsertionIntent.CaptureIntoNativeUserLayer(display,[new(100,100,.5),new(300,100,.5)],selected)
            :CanvasNativeInsertionIntent.Capture(display,[new(100,100,.5),new(300,100,.5)]);
        local.SelectNativeUserLayer(page.PageId,explicitTarget?first:originalLayer);
        Assert.NotEqual(selected,local.ActiveNativeUserLayerId);
        if(explicitTarget)Assert.Equal(selected,intent.Arguments.GetProperty("originalNativeLayerId").GetGuid());
        else Assert.False(intent.Arguments.TryGetProperty("originalNativeLayerId",out _));
        var cap=await fixture.Approve(intent);
        var ack=await new CanvasHomeNativeInsertionOperation(fixture.Files,fixture.Home,fixture.Actors,fixture.ClaimedStore).ExecuteAsync(intent,cap);
        var opened=await fixture.Files.OpenAsync(fixture.FileId,intent.StoreId);Assert.Equal(ack.FilesRevision.Id,opened.CasRevisionId);
        var ink=Assert.Single(opened.Artifact.Pages.Single().Strokes);Assert.Equal(intent.StrokeId,ink.StrokeId);Assert.Equal(selected,ink.LayerId);
        using var native=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        using(var donor=RnoteCanvasEngine.Open(native.ExportRnote()))Assert.Single(donor.ReadStrokeKeys());
        native.Undo(LayerRequest(native));Assert.Empty(native.Snapshot.Pages.Single().Strokes);
        native.Redo(LayerRequest(native));Assert.Equal(selected,Assert.Single(native.Snapshot.Pages.Single().Strokes).LayerId);
        Assert.True((await fixture.Home.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,"ORIGINAL_LAYER_INK_ACK","Exact original native layer ink acknowledged.",[]))).Succeeded);
    }
    [Fact]
    public async Task Locked_or_missing_original_native_layer_cannot_prepare_an_ink_request_or_change_owning_bytes()
    {
        await using var fixture=await Fixture.Create();var layerStore=fixture.LayerStore();
        var initial=(await fixture.Files.OpenAsync(fixture.FileId)).Artifact;var page=initial.Pages.Single();var locked=Guid.NewGuid();
        using(var createDisplay=await fixture.CaptureDisplay())
        {
            var create=layerStore.Capture(createDisplay,new(CanvasNativeLayerEditKind.Create,page.PageId,locked,"Locked target"));
            var cap=await fixture.ApproveLayer(create);
            await new CanvasHomeNativeLayerOperation(fixture.Home,fixture.Actors,layerStore).ExecuteAsync(create,cap);
            Assert.True((await fixture.Home.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,"LAYER_CREATED","Created original target.",[]))).Succeeded);
        }
        using(var lockDisplay=await fixture.CaptureDisplay())
        {
            var command=layerStore.Capture(lockDisplay,new(CanvasNativeLayerEditKind.SetLocked,page.PageId,locked,Value:true));
            var cap=await fixture.ApproveLayer(command);
            await new CanvasHomeNativeLayerOperation(fixture.Home,fixture.Actors,layerStore).ExecuteAsync(command,cap);
            Assert.True((await fixture.Home.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,"LAYER_LOCKED","Original target locked.",[]))).Succeeded);
        }
        using var display=await fixture.CaptureDisplay();var home=await fixture.HomeBytes();var drive=await File.ReadAllBytesAsync(fixture.StatePath);
        Assert.Throws<InvalidOperationException>(()=>CanvasNativeInsertionIntent.CaptureIntoNativeUserLayer(display,[new(100,100,.5),new(300,100,.5)],locked));
        Assert.Throws<ArgumentException>(()=>CanvasNativeInsertionIntent.CaptureIntoNativeUserLayer(display,[new(100,100,.5),new(300,100,.5)],Guid.NewGuid()));
        var legacy=CanvasNativeInsertionIntent.Capture(display,[new(100,100,.5),new(300,100,.5)]);
        Assert.Null(legacy.CapturedNativeLayerId);Assert.False(legacy.Arguments.TryGetProperty("originalNativeLayerId",out _));
        Assert.Equal(home,await fixture.HomeBytes());Assert.Equal(drive,await File.ReadAllBytesAsync(fixture.StatePath));
        Assert.Empty((await fixture.Files.OpenAsync(fixture.FileId,display.Opened.StoreId)).Artifact.Pages.Single().Strokes);
    }

}
