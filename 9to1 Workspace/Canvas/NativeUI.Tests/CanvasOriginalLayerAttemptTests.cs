using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed partial class CanvasHomeNativeInsertionOperationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Layer_acknowledgement_survives_real_audit_failure_or_pre_reservation_cancellation_without_owner_replay(bool cancellation)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));var token=timeout.Token;
        await using var fixture=await Fixture.Create();var opened=await fixture.Files.OpenAsync(fixture.FileId);
        using var display=await fixture.CaptureDisplay();var page=opened.Artifact.Pages.Single();var store=fixture.LayerStore();
        var intent=store.Capture(display,new(CanvasNativeLayerEditKind.SetLocked,page.PageId,page.LayerOrder[0],Value:true));
        var cap=await fixture.ApproveLayer(intent);
        var attempt=CanvasOriginalLayerAttempt.Capture(new(fixture.Home,fixture.Actors,store),fixture.Home,intent,cap);
        var ack=await attempt.ExecuteOnceAsync(token);Assert.Same(ack,attempt.Acknowledged);
        Assert.Equal(CanvasOriginalLayerAttemptState.Acknowledged,attempt.State);
        var metadata=await File.ReadAllBytesAsync(fixture.StatePath,token);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>attempt.ExecuteOnceAsync(token));
        if(cancellation)
        {
            await using var held=await fixture.HoldLayerClaimCompletion(cap,token);
            using var cancelled=new CancellationTokenSource();
            var completion=attempt.CompleteAcknowledgedAsync(cancelled.Token);
            Assert.False(completion.IsCompleted);cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>completion);
            Assert.NotNull(fixture.Home.CaptureClaimedAttestation(cap));
            await held.DisposeAsync();
        }
        else
        {
            using(var heldAuditLock=new FileStream(fixture.LayerHomeLockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                await Assert.ThrowsAsync<IOException>(()=>attempt.CompleteAcknowledgedAsync(token));
        }
        Assert.Equal(CanvasOriginalLayerAttemptState.AuditPending,attempt.State);Assert.Same(ack,attempt.Acknowledged);
        Assert.True((await attempt.RetryCompletionAuditAsync(token)).Succeeded);
        Assert.True((await attempt.RetryCompletionAuditAsync(token)).Succeeded);
        Assert.Equal(CanvasOriginalLayerAttemptState.Completed,attempt.State);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>attempt.ExecuteOnceAsync(token));
        Assert.Equal(metadata,await File.ReadAllBytesAsync(fixture.StatePath,token));
        var current=await fixture.Files.OpenAsync(fixture.FileId,intent.StoreId);
        Assert.Equal(ack.FilesRevision.Id,current.CasRevisionId);
        using var native=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(current.Artifact));
        Assert.True(native.Snapshot.Pages.Single().Layers.Single().IsLocked);
        Assert.Equal(HomePermissionRequestState.Succeeded,(await fixture.Home.GetExecutionDecisionAsync(cap,token))!.State);
    }
    [Fact]
    public async Task Layer_attempt_rejects_foreign_completion_broker_before_claim_and_retains_same_original_capability()
    {
        await using var fixture=await Fixture.Create();var opened=await fixture.Files.OpenAsync(fixture.FileId);
        using var display=await fixture.CaptureDisplay();var page=opened.Artifact.Pages.Single();var store=fixture.LayerStore();
        var intent=store.Capture(display,new(CanvasNativeLayerEditKind.SetLocked,page.PageId,page.LayerOrder[0],Value:true));
        var cap=await fixture.ApproveLayer(intent);var owner=new CanvasHomeNativeLayerOperation(fixture.Home,fixture.Actors,store);
        var foreign=new HomeResourceOperationBroker(fixture.Resources,fixture.Permissions);
        var before=await File.ReadAllBytesAsync(fixture.StatePath);var homeBefore=await fixture.HomeBytes();
        Assert.Throws<ArgumentException>(()=>CanvasOriginalLayerAttempt.Capture(owner,foreign,intent,cap));
        Assert.Equal(before,await File.ReadAllBytesAsync(fixture.StatePath));Assert.Equal(homeBefore,await fixture.HomeBytes());
        var original=CanvasOriginalLayerAttempt.Capture(owner,fixture.Home,intent,cap);var ack=await original.ExecuteOnceAsync();
        Assert.Same(ack,original.Acknowledged);Assert.True((await original.CompleteAcknowledgedAsync()).Succeeded);
    }
    [Fact]
    public async Task Layer_attempt_owner_exception_remains_unknown_and_cannot_reexecute_or_invent_acknowledgement()
    {
        await using var fixture=await Fixture.Create();var opened=await fixture.Files.OpenAsync(fixture.FileId);
        using var display=await fixture.CaptureDisplay();var page=opened.Artifact.Pages.Single();var store=fixture.LayerStore();
        var intent=store.Capture(display,new(CanvasNativeLayerEditKind.SetLocked,page.PageId,page.LayerOrder[0],Value:true));
        var cap=await fixture.ApproveLayer(intent);var attempt=CanvasOriginalLayerAttempt.Capture(new(fixture.Home,fixture.Actors,store),fixture.Home,intent,cap);
        display.Dispose();var before=await File.ReadAllBytesAsync(fixture.StatePath);var homeBefore=await fixture.HomeBytes();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>attempt.ExecuteOnceAsync());
        Assert.Equal(CanvasOriginalLayerAttemptState.Unknown,attempt.State);Assert.Null(attempt.Acknowledged);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>attempt.ExecuteOnceAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>attempt.CompleteAcknowledgedAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>attempt.RetryCompletionAuditAsync());
        Assert.Equal(before,await File.ReadAllBytesAsync(fixture.StatePath));Assert.Equal(homeBefore,await fixture.HomeBytes());
        // This genuine prewrite refusal checks conservative generic handling; it does not simulate a lost durable return.
    }
    private sealed partial class Fixture
    {
        public string LayerHomeLockPath=>Path.Combine(_root,"home.json.lock");
        public async Task<HomeClaimedResourceCommitFence> HoldLayerClaimCompletion(HomeResourceExecutionCapability cap,CancellationToken token)
        {
            var config=await _authority.CaptureOriginalConfigurationRecordAsync(_workspace,token);
            var fence=Assert.IsType<HomeClaimedResourceCommitFence>(await HomeClaimedResourceCommitFence.CaptureAsync(Home,_homeStore,_actors,
                _ownershipAuthority,"files",_workspace.Configuration.StoreId.ToString("D"),cap,_workspace.Actor,[config],()=>true,token));
            try{Assert.True(await fence.ValidateAsync(token));return fence;}catch{await fence.DisposeAsync();throw;}
        }
    }
}
