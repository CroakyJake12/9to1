using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

// PRIVATE/UNRUN. SAME protected SQLite/current Home import and actual individually
// reviewed WRITE. A page lifetime never supplies permission or cancels SQL.
public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [LinuxOriginalStoreFact]
    public Task Home_setup_withdraws_only_its_exact_pending_review_and_closes_without_SQL() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        await WithCapabilityInitializer(rig, async graph =>
        {
            var intent = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor,
                Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            ICapabilityOriginalInitializationHomeWriteClaim? captured = null;
            var acquisition = rig.Keep(graph.Writes.AcquireOriginalWriteWithinSourceAsync(intent,
                rig.Scope, graph.Retain, value => captured = value, rig.Token));
            var request = await graph.WaitForApprovalAsync(acquisition);
            Assert.NotNull(captured); Assert.Same(acquisition, captured!.OriginalAcquisition);
            var withdrawal = rig.Keep(graph.Writes.WithdrawOriginalPendingWriteWithinSourceAsync(captured,
                rig.Scope, graph.Retain, rig.Token));
            await withdrawal.WaitAsync(TimeSpan.FromSeconds(15), rig.Token);
            Assert.Same(withdrawal, graph.Writes.WithdrawOriginalPendingWriteWithinSourceAsync(captured,
                rig.Scope, graph.Retain, rig.Token));
            var refusal = await Assert.ThrowsAnyAsync<Exception>(() => acquisition); rig.Expect(refusal);
            Assert.True(graph.Writes.IsAcknowledgedOriginalWriteRefusal(acquisition));
            var alias = Task.FromException(acquisition.Exception!.InnerExceptions[0]);
            Assert.False(graph.Writes.IsAcknowledgedOriginalWriteRefusal(alias));
            await Assert.ThrowsAnyAsync<Exception>(() => alias);
            Assert.Equal(0L, await CapabilityCount(rig));
            var actual = await rig.Keep(graph.Permissions.ReadRequestObservationAsync(request.RequestId, rig.Token));
            Assert.Equal(HomePermissionRequestState.Cancelled, actual!.State);
            Assert.Equal("HOME_CAPABILITY_SETUP_REVIEW_WITHDRAWN", actual.ResultCode);
            await rig.Keep(captured.CloseAndDrainOriginalAsync());
            Assert.True(captured.OriginalClose!.IsCompletedSuccessfully);
            await rig.Keep(graph.Writes.CloseAndDrainOriginalAsync());
            Assert.True(graph.Writes.OriginalClose!.IsCompletedSuccessfully);
        });
    });

    [LinuxOriginalStoreFact]
    public Task Home_setup_preserves_an_individually_approved_claim_when_process_requests_withdrawal() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        await WithCapabilityInitializer(rig, async graph =>
        {
            var intent = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor,
                Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            ICapabilityOriginalInitializationHomeWriteClaim? captured = null;
            var acquisition = rig.Keep(graph.Writes.AcquireOriginalWriteWithinSourceAsync(intent,
                rig.Scope, graph.Retain, value => captured = value, rig.Token));
            var request = await graph.WaitForApprovalAsync(acquisition);
            Assert.True((await rig.Keep(graph.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token))).Succeeded);
            var actualClaim = await acquisition;
            Assert.Same(captured, actualClaim); Assert.True(graph.Writes.IsIssuedOriginalWriteClaim(actualClaim, intent));
            await rig.Keep(graph.Writes.WithdrawOriginalPendingWriteWithinSourceAsync(actualClaim,
                rig.Scope, graph.Retain, rig.Token));
            Assert.True(graph.Writes.IsIssuedOriginalWriteClaim(actualClaim, intent));
            Assert.False(graph.Writes.IsAcknowledgedOriginalWriteRefusal(acquisition));
            var decision = await rig.Keep(graph.Permissions.ReadRequestObservationAsync(request.RequestId, rig.Token));
            Assert.Equal(HomePermissionRequestState.Executing, decision!.State);
            Assert.Equal(0L, await CapabilityCount(rig));
            // This actual owner deliberately dispatches no SQL. SAME claim cleanup
            // records its actual no-dispatch failure, rather than a fake catalogue success.
            await rig.Keep(actualClaim.CloseAndDrainOriginalAsync());
            var terminal = await rig.Keep(graph.Permissions.ReadRequestObservationAsync(request.RequestId, rig.Token));
            Assert.Equal(HomePermissionRequestState.Failed, terminal!.State);
            Assert.Equal("HOME_CAPABILITY_SETUP_NOT_DISPATCHED", terminal.ResultCode);
        });
    });
}
