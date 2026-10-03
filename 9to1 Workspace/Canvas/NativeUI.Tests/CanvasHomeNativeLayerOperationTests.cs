using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed partial class CanvasHomeNativeInsertionOperationTests
{
    [Theory]
    [InlineData(CanvasNativeLayerEditKind.Create)]
    [InlineData(CanvasNativeLayerEditKind.SetVisibility)]
    [InlineData(CanvasNativeLayerEditKind.SetLocked)]
    public async Task Genuine_original_layer_proposal_commits_exact_native_edit_and_reopens_history(CanvasNativeLayerEditKind kind)
    {
        await using var fixture=await Fixture.Create();
        await Add(fixture,[new(100,100,.5),new(300,100,.5)]);
        var opened=await fixture.Files.OpenAsync(fixture.FileId);using var display=await fixture.CaptureDisplay();var original=opened.Artifact;
        var page=original.Pages.Single();var layer=page.LayerOrder[0];var added=Guid.NewGuid();
        var store=fixture.LayerStore();
        CanvasNativeLayerEditCommand command=kind switch
        {
            CanvasNativeLayerEditKind.Create=>new(kind,page.PageId,added,"Exact new layer",0),
            CanvasNativeLayerEditKind.SetVisibility=>new(kind,page.PageId,layer,Value:false),
            _=>new CanvasNativeLayerEditCommand(kind,page.PageId,layer,Value:true)
        };
        var originalHome=await fixture.HomeBytes();var originalFiles=await File.ReadAllBytesAsync(fixture.StatePath);
        var intent=store.Capture(display,command);
        Assert.Equal(originalHome,await fixture.HomeBytes());Assert.Equal(originalFiles,await File.ReadAllBytesAsync(fixture.StatePath));
        // Replacing a caller's command variable cannot alter the captured typed proposal.
        command=command with {LayerId=Guid.NewGuid(),Value=!command.Value.GetValueOrDefault(),Name="Substitute"};
        var cap=await fixture.ApproveLayer(intent);
        var ack=await new CanvasHomeNativeLayerOperation(fixture.Home,fixture.Actors,store).ExecuteAsync(intent,cap);
        Assert.Equal(intent.OperationId,ack.OperationId);Assert.Equal(opened.CasRevisionId,ack.FilesRevision.ParentRevisionId);
        var current=await fixture.Files.OpenAsync(fixture.FileId,intent.StoreId);
        Assert.Equal(ack.FilesRevision.Id,current.CasRevisionId);
        Assert.Equal(intent.PreparedHash,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(CanvasArtifactCodec.Serialize(current.Artifact))));
        Assert.Equal(page.StrokeOrder,current.Artifact.Pages.Single().StrokeOrder);
        Assert.Equal(page.Strokes.Single().StrokeId,current.Artifact.Pages.Single().Strokes.Single().StrokeId);
        using var native=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(current.Artifact));
        if(kind==CanvasNativeLayerEditKind.Create)Assert.Equal(added,native.Snapshot.Pages.Single().LayerOrder[0]);
        if(kind==CanvasNativeLayerEditKind.SetVisibility)Assert.False(native.Snapshot.Pages.Single().Layers.Single().IsVisible);
        if(kind==CanvasNativeLayerEditKind.SetLocked)Assert.True(native.Snapshot.Pages.Single().Layers.Single().IsLocked);
        native.Undo(LayerRequest(native));
        Assert.Equal(page.LayerOrder,native.Snapshot.Pages.Single().LayerOrder);
        Assert.True(native.Snapshot.Pages.Single().Layers.Single().IsVisible);
        Assert.False(native.Snapshot.Pages.Single().Layers.Single().IsLocked);
        native.Redo(LayerRequest(native));Assert.NotEqual(original.RevisionId,native.Identity.RevisionId);
        Assert.True((await fixture.Home.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,
            "CANVAS_LAYER_COMMITTED","Actual original layer revision acknowledged.",[new("files.item",fixture.FileId.ToString())]))).Succeeded);
        var acknowledged=await File.ReadAllBytesAsync(fixture.StatePath);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new CanvasHomeNativeLayerOperation(fixture.Home,fixture.Actors,store).ExecuteAsync(intent,cap));
        Assert.Equal(acknowledged,await File.ReadAllBytesAsync(fixture.StatePath));
    }
    [Fact]
    public async Task Strong_layer_port_denies_other_typed_command_under_exact_original_claim_then_original_still_commits()
    {
        await using var fixture=await Fixture.Create();var opened=await fixture.Files.OpenAsync(fixture.FileId);using var display=await fixture.CaptureDisplay();
        var page=opened.Artifact.Pages.Single();var store=fixture.LayerStore();
        var original=store.Capture(display,new(CanvasNativeLayerEditKind.SetVisibility,page.PageId,page.LayerOrder[0],Value:false));
        var substitute=store.Capture(display,new(CanvasNativeLayerEditKind.SetLocked,page.PageId,page.LayerOrder[0],Value:true));
        var cap=await fixture.ApproveLayer(original);
        var claim=await fixture.Home.ClaimExecutionWithOriginalWriteObservedAsync(cap,CanvasStrokeWriteIntent.TargetAppId,CanvasStrokeWriteIntent.ActionId,
            original.Scopes,original.Arguments,original.Display.OriginalWrite);Assert.Equal(HomeResourceClaimDisposition.Claimed,claim.Disposition);Assert.Equal(original.OriginalActor,claim.Actor);
        var homeBefore=await fixture.HomeBytes();var before=await File.ReadAllBytesAsync(fixture.StatePath);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.SaveAsync(substitute,cap,original.OriginalActor));
        Assert.Equal(homeBefore,await fixture.HomeBytes());Assert.Equal(before,await File.ReadAllBytesAsync(fixture.StatePath));
        var revision=await store.SaveAsync(original,cap,original.OriginalActor);Assert.NotEqual(original.FilesRevision,revision.Id);
        Assert.True((await fixture.Home.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,"CANVAS_LAYER_COMMITTED",
            "Original exact layer proposal acknowledged.",[]))).Succeeded);
        using var native=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize((await fixture.Files.OpenAsync(fixture.FileId,original.StoreId)).Artifact));
        Assert.False(native.Snapshot.Pages.Single().Layers.Single().IsVisible);Assert.False(native.Snapshot.Pages.Single().Layers.Single().IsLocked);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Strong_layer_save_denies_retired_private_display_or_changed_original_configuration(bool configurationChanged)
    {
        await using var fixture=await Fixture.Create();var opened=await fixture.Files.OpenAsync(fixture.FileId);using var display=await fixture.CaptureDisplay();
        var page=opened.Artifact.Pages.Single();var store=fixture.LayerStore();
        var intent=store.Capture(display,new(CanvasNativeLayerEditKind.SetLocked,page.PageId,page.LayerOrder[0],Value:true));
        var cap=await fixture.ApproveLayer(intent);
        var claim=await fixture.Home.ClaimExecutionWithOriginalWriteObservedAsync(cap,CanvasStrokeWriteIntent.TargetAppId,
            CanvasStrokeWriteIntent.ActionId,intent.Scopes,intent.Arguments,intent.Display.OriginalWrite);
        Assert.Equal(HomeResourceClaimDisposition.Claimed,claim.Disposition);Assert.Equal(intent.OriginalActor,claim.Actor);
        if(configurationChanged)await fixture.ChangeOriginalConfiguration();else display.Dispose();
        var before=await File.ReadAllBytesAsync(fixture.StatePath);var homeBefore=await fixture.HomeBytes();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>store.SaveAsync(intent,cap,intent.OriginalActor));
        Assert.Equal(before,await File.ReadAllBytesAsync(fixture.StatePath));Assert.Equal(homeBefore,await fixture.HomeBytes());
    }
    [Fact]
    public async Task Foreign_broker_composition_denies_before_capture_or_claim_and_same_original_capability_still_commits()
    {
        await using var fixture=await Fixture.Create();var opened=await fixture.Files.OpenAsync(fixture.FileId);
        using var display=await fixture.CaptureDisplay();var page=opened.Artifact.Pages.Single();var store=fixture.LayerStore();
        var command=new CanvasNativeLayerEditCommand(CanvasNativeLayerEditKind.SetLocked,page.PageId,page.LayerOrder[0],Value:true);
        var intent=store.Capture(display,command);var cap=await fixture.ApproveLayer(intent);
        var foreign=new HomeResourceOperationBroker(fixture.Resources,fixture.Permissions);
        var homeBefore=await fixture.HomeBytes();var filesBefore=await File.ReadAllBytesAsync(fixture.StatePath);
        Assert.Throws<NotSupportedException>(()=>fixture.LayerStoreForHome(foreign).Capture(display,command));
        await Assert.ThrowsAsync<NotSupportedException>(()=>new CanvasHomeNativeLayerOperation(foreign,fixture.Actors,store).ExecuteAsync(intent,cap));
        Assert.Equal(homeBefore,await fixture.HomeBytes());Assert.Equal(filesBefore,await File.ReadAllBytesAsync(fixture.StatePath));
        var ack=await new CanvasHomeNativeLayerOperation(fixture.Home,fixture.Actors,store).ExecuteAsync(intent,cap);
        Assert.NotEqual(intent.FilesRevision,ack.FilesRevision.Id);
        Assert.True((await fixture.Home.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,
            "CANVAS_LAYER_COMMITTED","Original layer acknowledged after foreign broker refusal.",[]))).Succeeded);
    }
    private static CanvasMutationRequest LayerRequest(CanvasRnoteDocument document)=>new(document.Identity.RevisionId,Guid.NewGuid(),new("layer-history-test","Layer history"));
    private sealed partial class Fixture
    {
        public CanvasHomeClaimedNativeLayerStore LayerStore()=>LayerStoreForHome(Home);
        public CanvasHomeClaimedNativeLayerStore LayerStoreForHome(HomeResourceOperationBroker broker)=>new(Files,broker,_homeStore,_actors,_ownershipAuthority,ClaimedStore,()=>WritesAllowed);
        public async Task<HomeResourceExecutionCapability> ApproveLayer(CanvasNativeLayerEditIntent intent)
        {
            var pending=await intent.Issuer.AuthorizeAsync(intent,"Review this exact original native layer change",null,"original-layer-owner");
            Assert.Equal(HomePermissionRequestState.PendingApproval,pending.State);
            Assert.True((await Permissions.DecideAsync(pending.RequestId,HomeApprovalChoice.Accept)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await intent.Issuer.BeginExecutionAsync(intent,pending.RequestId));
        }
    }
}
