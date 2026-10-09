using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HomePermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;
using Xunit;

namespace Haven.Infrastructure.Tests;

// These controls use the maintained protected SQLite/Home fixture and its real
// separate manual setup WRITE. They do not supply installation or UI authority.
public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [LinuxOriginalStoreFact]
    public Task Independently_joined_actual_declines_cross_process_quota_while_pending_review_remains_owned() => Run(async rig =>
    {
        rig.SetControlDeadline(TimeSpan.FromMinutes(3));
        await rig.Import("canonical.sqlite");
        await WithCapabilityInitializer(rig, async graph =>
        {
            var pendingIntent = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor,
                Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            var pending = await rig.Keep(graph.Creator.StartOriginalInitializationProcessWithinSourceAsync(pendingIntent,
                rig.Scope, graph.Retain, rig.Token));
            var pendingWait = rig.Keep(pending.WaitOriginalCompletionAsync(rig.Token));
            var pendingRequest = await graph.WaitForApprovalAsync(pendingWait);
            var pendingDriver = rig.Keep(graph.Creator.CommitOriginalInitializationWithinSourceAsync(pendingIntent,
                rig.Scope, graph.Retain, rig.Token));
            var requests = new HashSet<string>(StringComparer.Ordinal) { pendingRequest.RequestId };

            // 129 distinct real declines cross the unchanged 128-process bound.
            // The separate pending process must never be awaited or forgotten by
            // new Start admission, and every declined delivery closes first.
            for (var index = 0; index < 129; index++)
            {
                var intent = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor,
                    Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
                var observation = await rig.Keep(graph.Creator.StartOriginalInitializationProcessWithinSourceAsync(intent,
                    rig.Scope, graph.Retain, rig.Token));
                var wait = rig.Keep(observation.WaitOriginalCompletionAsync(rig.Token));
                var request = await WaitForExactSetupReviewAsync(rig, graph, intent, wait);
                Assert.True(requests.Add(request.RequestId));
                Assert.True(request.Policy.RequiresPerActionApproval);
                Assert.True((await rig.Keep(graph.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Decline,
                    cancellationToken: rig.Token))).Succeeded);
                var completion = await wait.WaitAsync(TimeSpan.FromSeconds(15), rig.Token);
                Assert.Equal(CapabilityOriginalInitializationCompletionKind.DeclinedBeforeEffect, completion.Kind);
                Assert.True(observation.IsIssuedOriginalCompletion(completion)); Assert.Null(completion.Acknowledgment);
                var driver = rig.Keep(graph.Creator.CommitOriginalInitializationWithinSourceAsync(intent,
                    rig.Scope, graph.Retain, rig.Token));
                var cause = await Assert.ThrowsAnyAsync<Exception>(() => driver);
                Assert.True(graph.Creator.IsAcknowledgedOriginalInitializationSourceRefusal(driver));
                Assert.Same(cause, Assert.Single(driver.Exception!.InnerExceptions)); rig.Expect(cause);
                await rig.Keep(observation.CloseAndDrainOriginalAsync());
                Assert.Same(observation.OriginalClose, observation.CloseAndDrainOriginalAsync());
                Assert.False(pendingWait.IsCompleted); Assert.False(pendingDriver.IsCompleted);
            }

            Assert.Equal(130, requests.Count); Assert.Equal(0L, await CapabilityCount(rig));
            var state = await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token));
            Assert.Equal(pendingRequest.RequestId, Assert.Single(state.PendingRequests).RequestId);
            await rig.Keep(pending.CloseAndDrainOriginalAsync());
            Assert.Equal(CapabilityOriginalInitializationCompletionKind.ObservationRetired, (await pendingWait).Kind);
            // Global retirement must still find and withdraw the actual pending
            // process after all the declined cohorts have left the quota.
            await rig.Keep(graph.Creator.CloseAndDrainOriginalAsync());
            var withdrawn = await Assert.ThrowsAnyAsync<Exception>(() => pendingDriver);
            Assert.True(graph.Creator.IsAcknowledgedOriginalInitializationSourceRefusal(pendingDriver));
            rig.Expect(withdrawn);
            await rig.Keep(graph.Writes.CloseAndDrainOriginalAsync());
            Assert.Empty((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests);
            Assert.Equal(0L, await CapabilityCount(rig));
            Assert.True(graph.Creator.OriginalClose!.IsCompletedSuccessfully);
        });
    });

    [LinuxOriginalStoreFact]
    public Task Changed_actual_catalogue_process_and_foreign_same_cause_alias_remain_unacknowledged_and_retained() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        await WithCapabilityInitializer(rig, async graph =>
        {
            var intent = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor,
                Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            var observation = await rig.Keep(graph.Creator.StartOriginalInitializationProcessWithinSourceAsync(intent,
                rig.Scope, graph.Retain, rig.Token));
            var wait = rig.Keep(observation.WaitOriginalCompletionAsync(rig.Token));
            var request = await graph.WaitForApprovalAsync(wait);
            // The fixture's owning repository changes a real row before approval;
            // no READ or saved preference creates this baseline mutation.
            var changed = CapabilityRegistryCatalog.BuiltIns[0] with { IsEnabled = false, UpdatedAt = DateTimeOffset.UtcNow };
            await rig.Keep(graph.Catalogue.Repository.UpsertCapabilityAsync(changed, rig.Token));
            var bytes = await CapabilityRowsBytesAsync(rig, [changed.Id]);
            Assert.True((await rig.Keep(graph.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: rig.Token))).Succeeded);
            var deliveryFailure = await Assert.ThrowsAnyAsync<Exception>(() => wait);
            var driver = rig.Keep(graph.Creator.CommitOriginalInitializationWithinSourceAsync(intent,
                rig.Scope, graph.Retain, rig.Token));
            var driverFailure = await Assert.ThrowsAnyAsync<Exception>(() => driver);
            Assert.False(graph.Creator.IsAcknowledgedOriginalInitializationSourceRefusal(driver));
            var alias = Task.FromException<ICapabilityOriginalInitializationAcknowledgment>(driverFailure);
            Assert.False(graph.Creator.IsAcknowledgedOriginalInitializationSourceRefusal(alias));
            Assert.Same(driverFailure, await Assert.ThrowsAnyAsync<Exception>(() => alias));
            var known = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
            foreach (var cause in References(wait.Exception ?? deliveryFailure).Concat(References(driver.Exception ?? driverFailure)))
                known.Add(cause);
            rig.Expect(deliveryFailure); rig.Expect(driverFailure);
            var deliveryClose = rig.Keep(observation.CloseAndDrainOriginalAsync());
            var deliveryCloseFailure = await Assert.ThrowsAnyAsync<Exception>(() => deliveryClose);
            AssertActualProcessCloseCauses(deliveryClose.Exception ?? deliveryCloseFailure, known);
            rig.Expect(deliveryCloseFailure);
            var close = rig.Keep(graph.Creator.CloseAndDrainOriginalAsync());
            var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => close);
            AssertActualProcessCloseCauses(close.Exception ?? closeFailure, known);
            rig.Expect(closeFailure);
            Assert.Same(close, graph.Creator.CloseAndDrainOriginalAsync());
            Assert.True(close.IsFaulted);
            var homeClose = rig.Keep(graph.Writes.CloseAndDrainOriginalAsync());
            var homeCloseFailure = await Assert.ThrowsAnyAsync<Exception>(() => homeClose);
            AssertActualProcessCloseCauses(homeClose.Exception ?? homeCloseFailure, known);
            rig.Expect(homeCloseFailure);
            Assert.Same(homeClose, graph.Writes.CloseAndDrainOriginalAsync());
            Assert.True(homeClose.IsFaulted);
            Assert.Equal(1L, await CapabilityCount(rig));
            Assert.Equal(bytes, await CapabilityRowsBytesAsync(rig, [changed.Id]));
        });
    });

    private static async Task<HomePermissionRequest> WaitForExactSetupReviewAsync(Rig rig,
        CapabilityInitializationGraph graph, ICapabilityOriginalInitializationIntent intent, Task wait)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            var snapshot = await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token));
            var pending = snapshot.PendingRequests.Where(request =>
                request.Scope.ActionName == HomeCapabilityCatalogueInitializationWriteSource.WriteAction &&
                request.Scope.Objects.Any(item => item.ObjectType == graph.Writes.ResourceKind &&
                    item.ObjectId == intent.OriginalStoreIdentity.StoreId.ToString("D") + ":" + intent.OperationId.ToString("N"))).ToArray();
            if (pending.Length != 0) return Assert.Single(pending);
            if (wait.IsCompleted) { await wait; throw new InvalidOperationException("The actual process ended before its separate manual review."); }
            await Task.Delay(TimeSpan.FromMilliseconds(10), rig.Token);
        }
        throw new TimeoutException("The new actual process did not publish its exact separate manual Home setup review.");
    }

    private static void AssertActualProcessCloseCauses(Exception actual, HashSet<Exception> known)
    {
        if (known.Contains(actual)) return;
        if (actual is AggregateException { InnerExceptions.Count: > 0 } combining)
        {
            foreach (var cause in combining.InnerExceptions) AssertActualProcessCloseCauses(cause, known);
            return;
        }
        Assert.True(known.Contains(actual), "An independently unobserved process/cleanup cause must remain a fixture failure.");
    }
}
