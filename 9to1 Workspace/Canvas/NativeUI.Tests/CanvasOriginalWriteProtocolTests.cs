using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;

public sealed partial class CanvasHomeNativeInsertionOperationTests
{
    private sealed class FabricatedWrite(ResourceScope scope):IOriginalCanonicalWriteContext
    { public ResourceScope OriginalScope=>scope; }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Malformed_or_genuine_foreign_write_context_does_not_consume_original_layer_claim(bool genuineForeign)
    {
        await using var fixture=await Fixture.Create();
        await using var foreign=await Fixture.Create();
        using var display=await fixture.CaptureDisplay();
        using var foreignDisplay=await foreign.CaptureDisplay();
        var page=display.Opened.Artifact.Pages.Single();var store=fixture.LayerStore();
        var intent=store.Capture(display,new(CanvasNativeLayerEditKind.SetLocked,page.PageId,page.LayerOrder[0],Value:true));
        var capability=await fixture.ApproveLayer(intent);
        IOriginalCanonicalWriteContext substitute=genuineForeign?foreignDisplay.OriginalWrite:new FabricatedWrite(intent.Scopes.Single());
        var home=await fixture.HomeBytes();var drive=await File.ReadAllBytesAsync(fixture.StatePath);
        var denied=await fixture.Home.ClaimExecutionWithOriginalWriteObservedAsync(capability,CanvasStrokeWriteIntent.TargetAppId,
            CanvasStrokeWriteIntent.ActionId,intent.Scopes,intent.Arguments,substitute);
        Assert.Equal(HomeResourceClaimDisposition.InputNotConsumed,denied.Disposition);Assert.Null(denied.Actor);
        Assert.Equal(home,await fixture.HomeBytes());Assert.Equal(drive,await File.ReadAllBytesAsync(fixture.StatePath));
        var acknowledged=await new CanvasHomeNativeLayerOperation(fixture.Home,fixture.Actors,store).ExecuteAsync(intent,capability);
        Assert.Equal(intent.FilesRevision,acknowledged.FilesRevision.ParentRevisionId);
        Assert.True((await fixture.Home.CompleteExecutionAsync(capability,new(HomePermissionRequestState.Succeeded,
            "CANVAS_ORIGINAL_WRITE_ACK","Exact original owning layer revision acknowledged.",[]))).Succeeded);
        var opened=await fixture.Files.OpenAsync(fixture.FileId,intent.StoreId);
        using var native=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        Assert.True(native.Snapshot.Pages.Single().Layers.Single().IsLocked);
    }

    [Fact]
    public async Task Actual_private_display_retirement_before_begin_preserves_approved_request_and_canonical_bytes()
    {
        await using var fixture=await Fixture.Create();
        using var display=await fixture.CaptureDisplay();var page=display.Opened.Artifact.Pages.Single();
        var store=fixture.LayerStore();
        var intent=store.Capture(display,new(CanvasNativeLayerEditKind.SetLocked,page.PageId,page.LayerOrder[0],Value:true));
        var pending=await store.AuthorizeAsync(intent,"Review exact original native lock",null,"original-write-retire");
        Assert.Equal(HomePermissionRequestState.PendingApproval,pending.State);
        Assert.True((await fixture.Permissions.DecideAsync(pending.RequestId,HomeApprovalChoice.Accept)).Succeeded);
        var home=await fixture.HomeBytes();var drive=await File.ReadAllBytesAsync(fixture.StatePath);
        display.Dispose();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.BeginExecutionAsync(intent,pending.RequestId));
        Assert.Equal(home,await fixture.HomeBytes());Assert.Equal(drive,await File.ReadAllBytesAsync(fixture.StatePath));
        Assert.False((await fixture.Files.OpenAsync(fixture.FileId,intent.StoreId)).Artifact.Pages.Single().Layers.Single().IsLocked);
    }
}
