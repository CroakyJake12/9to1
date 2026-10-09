using Haven.Application;
using Haven.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

// PRIVATE/UNRUN. These bodies use the actual owning protected SQLite/Home fixture
// and separate real manual WRITE. Delivery retirement never cancels business SQL.
public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [LinuxOriginalStoreFact]
    public Task Setup_delivery_closes_while_actual_manual_review_remains_and_second_delivery_observes_accept() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        await WithCapabilityInitializer(rig, async graph =>
        {
            var intent = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor,
                Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            var first = await rig.Keep(graph.Creator.StartOriginalInitializationProcessWithinSourceAsync(intent,
                rig.Scope, graph.Retain, rig.Token));
            var second = await rig.Keep(graph.Creator.StartOriginalInitializationProcessWithinSourceAsync(intent,
                rig.Scope, graph.Retain, rig.Token));
            Assert.NotSame(first, second); Assert.True(graph.Creator.IsIssuedOriginalInitializationObservation(first));
            var waiting = rig.Keep(first.WaitOriginalCompletionAsync(rig.Token));
            var remaining = rig.Keep(second.WaitOriginalCompletionAsync(rig.Token));
            var request = await graph.WaitForApprovalAsync(remaining);
            Assert.False(waiting.IsCompleted); Assert.False(remaining.IsCompleted);
            await rig.Keep(first.CloseAndDrainOriginalAsync());
            Assert.Same(first.OriginalClose, first.CloseAndDrainOriginalAsync());
            var detached = await waiting;
            Assert.Equal(CapabilityOriginalInitializationCompletionKind.ObservationRetired, detached.Kind);
            Assert.Null(detached.Acknowledgment); Assert.True(first.IsIssuedOriginalCompletion(detached));
            Assert.False(second.IsIssuedOriginalCompletion(detached)); Assert.False(remaining.IsCompleted);
            Assert.Equal(0L, await CapabilityCount(rig));
            var current = await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token));
            Assert.Contains(current.PendingRequests, value => value.RequestId == request.RequestId);
            Assert.True((await rig.Keep(graph.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token))).Succeeded);
            var completed = await remaining;
            Assert.Equal(CapabilityOriginalInitializationCompletionKind.Initialized, completed.Kind);
            Assert.True(second.IsIssuedOriginalCompletion(completed));
            Assert.Same(intent, completed.Acknowledgment!.OriginalIntent);
            Assert.Equal(CapabilityRegistryCatalog.BuiltIns.Count, completed.Acknowledgment.InsertedDefinitionIds.Count);
            await rig.Keep(second.CloseAndDrainOriginalAsync());
        });
    });

    [LinuxOriginalStoreFact]
    public Task Actual_process_withdraws_its_pending_review_without_SQL_and_whole_close_is_healthy() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        await WithCapabilityInitializer(rig, async graph =>
        {
            var intent = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor,
                Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            var observation = await rig.Keep(graph.Creator.StartOriginalInitializationProcessWithinSourceAsync(intent,
                rig.Scope, graph.Retain, rig.Token));
            var delivery = rig.Keep(observation.WaitOriginalCompletionAsync(rig.Token));
            var request = await graph.WaitForApprovalAsync(delivery);
            graph.Creator.RequestOriginalPendingReviewWithdrawals();
            var completion = await delivery.WaitAsync(TimeSpan.FromSeconds(15), rig.Token);
            Assert.Equal(CapabilityOriginalInitializationCompletionKind.DeclinedBeforeEffect, completion.Kind);
            Assert.True(observation.IsIssuedOriginalCompletion(completion)); Assert.Null(completion.Acknowledgment);
            Assert.Equal(0L, await CapabilityCount(rig));
            var snapshot = await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token));
            Assert.DoesNotContain(snapshot.PendingRequests, value => value.RequestId == request.RequestId);
            await rig.Keep(observation.CloseAndDrainOriginalAsync());
            await rig.Keep(graph.Creator.CloseAndDrainOriginalAsync());
            await rig.Keep(graph.Writes.CloseAndDrainOriginalAsync());
            Assert.True(graph.Creator.OriginalClose!.IsCompletedSuccessfully);
            await rig.Keep(rig.Store.CloseAndDrainAsync()); Assert.True(rig.Store.OriginalClose!.IsCompletedSuccessfully);
        });
    });

    [LinuxOriginalStoreFact]
    public Task Setup_start_never_forwards_global_business_driver_to_view_retainer() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        await WithCapabilityInitializer(rig, async graph =>
        {
            var intent = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor,
                Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            var retainedByView = new List<Task>(); var gate = new object();
            void Retain(Task actual) { lock (gate) retainedByView.Add(actual); graph.Retain(actual); }
            var observation = await rig.Keep(graph.Creator.StartOriginalInitializationProcessWithinSourceAsync(intent,
                rig.Scope, Retain, rig.Token));
            var wait = rig.Keep(observation.WaitOriginalCompletionAsync(rig.Token));
            await graph.WaitForApprovalAsync(wait);
            Task[] captured; lock (gate) captured = retainedByView.ToArray();
            Assert.DoesNotContain(captured, actual => actual is Task<ICapabilityOriginalInitializationAcknowledgment>);
            Assert.All(captured, actual => Assert.True(actual.IsCompleted));
            await rig.Keep(observation.CloseAndDrainOriginalAsync());
            Assert.Equal(CapabilityOriginalInitializationCompletionKind.ObservationRetired, (await wait).Kind);
            graph.Creator.RequestOriginalPendingReviewWithdrawals();
            await rig.Keep(graph.Creator.CloseAndDrainOriginalAsync());
            Assert.Equal(0L, await CapabilityCount(rig));
        });
    });
}
