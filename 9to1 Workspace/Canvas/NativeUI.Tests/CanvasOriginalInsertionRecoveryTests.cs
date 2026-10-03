using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed partial class CanvasHomeNativeInsertionOperationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_insertion_acknowledgement_survives_audit_failure_and_pre_reservation_cancellation_without_owner_replay(bool cancellation)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));var token=timeout.Token;
        await using var fixture=await Fixture.Create();using var display=await fixture.CaptureDisplay();
        var intent=CanvasNativeInsertionIntent.Capture(display,[new(100,100,.5),new(300,100,.5)]);
        var cap=await fixture.Approve(intent);
        var attempt=CanvasOriginalInsertionAttempt.Capture(new(fixture.Files,fixture.Home,fixture.Actors,fixture.ClaimedStore),fixture.Home,intent,cap);
        var committed=await attempt.ExecuteOnceAsync(token);
        Assert.Same(committed,attempt.Acknowledged);
        Assert.Equal(CanvasOriginalInsertionAttemptState.Acknowledged,attempt.State);
        var metadata=await File.ReadAllBytesAsync(fixture.StatePath,token);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>attempt.ExecuteOnceAsync(token));
        if(cancellation)
        {
            await using var heldFence=await fixture.HoldActualClaimCompletion(cap,token);
            using var cancelled=new CancellationTokenSource();
            var completion=attempt.CompleteAcknowledgedAsync(cancelled.Token);
            Assert.False(completion.IsCompleted);cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>completion);
            Assert.NotNull(fixture.Home.CaptureClaimedAttestation(cap));
            await heldFence.DisposeAsync();
        }
        else
        {
            using(var heldAuditLock=new FileStream(fixture.HomeLockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                await Assert.ThrowsAsync<IOException>(()=>attempt.CompleteAcknowledgedAsync(token));
        }
        Assert.Same(committed,attempt.Acknowledged);
        Assert.Equal(CanvasOriginalInsertionAttemptState.AuditPending,attempt.State);
        Assert.True((await attempt.RetryCompletionAuditAsync(token)).Succeeded);
        Assert.True((await attempt.RetryCompletionAuditAsync(token)).Succeeded);
        Assert.Equal(CanvasOriginalInsertionAttemptState.Completed,attempt.State);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>attempt.ExecuteOnceAsync(token));
        Assert.Equal(metadata,await File.ReadAllBytesAsync(fixture.StatePath,token));
        var current=await fixture.Files.OpenAsync(fixture.FileId);
        Assert.Equal(committed.FilesRevision.Id,current.CasRevisionId);
        Assert.Equal(intent.StrokeOrder,current.Artifact.Pages.Single().StrokeOrder);
        Assert.Equal(HomePermissionRequestState.Succeeded,(await fixture.Home.GetExecutionDecisionAsync(cap,token))!.State);
    }
    [Fact]
    public async Task Foreign_completion_broker_is_refused_before_attempt_or_claim_and_original_capability_remains_usable()
    {
        await using var fixture=await Fixture.Create();using var display=await fixture.CaptureDisplay();
        var intent=CanvasNativeInsertionIntent.Capture(display,[new(100,100,.5),new(300,100,.5)]);
        var cap=await fixture.Approve(intent);
        var owner=new CanvasHomeNativeInsertionOperation(fixture.Files,fixture.Home,fixture.Actors,fixture.ClaimedStore);
        var before=await File.ReadAllBytesAsync(fixture.StatePath);var homeBefore=await fixture.HomeBytes();
        var foreign=new HomeResourceOperationBroker(fixture.Resources,fixture.Permissions);
        Assert.Throws<ArgumentException>(()=>CanvasOriginalInsertionAttempt.Capture(owner,foreign,intent,cap));
        Assert.Equal(before,await File.ReadAllBytesAsync(fixture.StatePath));Assert.Equal(homeBefore,await fixture.HomeBytes());
        var original=CanvasOriginalInsertionAttempt.Capture(owner,fixture.Home,intent,cap);
        var committed=await original.ExecuteOnceAsync();Assert.Same(committed,original.Acknowledged);
        Assert.True((await original.CompleteAcknowledgedAsync()).Succeeded);
        Assert.Equal(CanvasOriginalInsertionAttemptState.Completed,original.State);
    }
    private sealed partial class Fixture
    {
        public string HomeLockPath=>Path.Combine(_root,"home.json.lock");
        public async Task<HomeClaimedResourceCommitFence> HoldActualClaimCompletion(HomeResourceExecutionCapability capability,CancellationToken token)
        {
            var config=await _authority.CaptureOriginalConfigurationRecordAsync(_workspace,token);
            var fence=Assert.IsType<HomeClaimedResourceCommitFence>(await HomeClaimedResourceCommitFence.CaptureAsync(
                Home,_homeStore,_actors,_ownershipAuthority,"files",_workspace.Configuration.StoreId.ToString("D"),
                capability,_workspace.Actor,[config],()=>true,token));
            try {Assert.True(await fence.ValidateAsync(token));return fence;}
            catch {await fence.DisposeAsync();throw;}
        }

    }
}
