using System.Runtime.ExceptionServices;
using Haven.Application.Automations;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [LinuxOriginalStoreFact]
    public Task Automation_delivery_close_detaches_view_then_same_actual_approved_process_commits() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database); var rows = await SeedAutomationReadRows(rig, repository);
        await rig.Import("canonical.sqlite"); var before = await AutomationFixtureBytes(rig);
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            var intent = await rig.Keep(graph.Writer.PrepareOriginalChangeWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.Disable, Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            ICanonicalAutomationDefinitionOriginalProcessSource source = graph.Writer;
            var observation = await rig.Keep(source.StartOriginalChangeProcessWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            Assert.True(source.IsIssuedOriginalChangeObservation(observation)); Assert.Same(intent, observation.OriginalIntent);
            var wait = rig.Keep(observation.WaitOriginalCompletionAsync(rig.Token));
            var review = await graph.WaitForApprovalAsync(wait);
            // This fixture joins the actual private cached business task directly;
            // the product's view receives only the finite delivery and its own wait.
            var business = graph.Writer.CommitOriginalChangeWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token);
            Assert.DoesNotContain(graph.Raw, actual => ReferenceEquals(actual, business));
            _ = rig.Keep(business); Assert.False(business.IsCompleted);
            observation.RequestOriginalRetirement(); var close = rig.Keep(observation.CloseAndDrainOriginalAsync());
            Assert.Same(close, observation.OriginalClose); Assert.Same(close, observation.CloseAndDrainOriginalAsync());
            await close.WaitAsync(TimeSpan.FromSeconds(15), rig.Token);
            var retired = await wait; Assert.True(observation.IsIssuedOriginalCompletion(retired));
            Assert.Same(observation, retired.OriginalObservation);
            Assert.Equal(CanonicalAutomationOriginalChangeCompletionKind.ObservationRetired, retired.Kind);
            Assert.Null(retired.Acknowledgment); Assert.False(business.IsCompleted);
            Assert.Contains((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests,
                request => request.RequestId == review.RequestId);
            Assert.Equal(before, await AutomationFixtureBytes(rig));
            await graph.DecideRequestAsync(review.RequestId, HomeApprovalChoice.Accept);
            var acknowledgment = await business;
            Assert.Equal(intent.OperationId, acknowledgment.OperationId); Assert.False(acknowledgment.Definition.IsEnabled);
            Assert.True(close.IsCompletedSuccessfully);
        });
    });

    [LinuxOriginalStoreFact]
    public Task Automation_delivery_decline_is_private_known_no_effect_result_and_all_process_owners_close() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database); var rows = await SeedAutomationReadRows(rig, repository);
        await rig.Import("canonical.sqlite"); var before = await AutomationFixtureBytes(rig);
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            var intent = await rig.Keep(graph.Writer.PrepareOriginalChangeWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.RecoverLegacy, Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            ICanonicalAutomationDefinitionOriginalProcessSource source = graph.Writer;
            var observation = await rig.Keep(source.StartOriginalChangeProcessWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            var wait = rig.Keep(observation.WaitOriginalCompletionAsync(rig.Token));
            await graph.DecideAsync(wait, HomeApprovalChoice.Decline);
            var outcome = await wait; Assert.True(observation.IsIssuedOriginalCompletion(outcome));
            Assert.Equal(CanonicalAutomationOriginalChangeCompletionKind.DeclinedBeforeEffect, outcome.Kind);
            Assert.Null(outcome.Acknowledgment); Assert.Same(observation, outcome.OriginalObservation);
            var business = rig.Keep(graph.Writer.CommitOriginalChangeWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            rig.Expect(await Assert.ThrowsAnyAsync<Exception>(() => business));
            Assert.True(graph.Writer.IsAcknowledgedOriginalChangeSourceRefusal(business));
            var close = rig.Keep(observation.CloseAndDrainOriginalAsync()); await close;
            Assert.Same(close, observation.OriginalClose); Assert.True(close.IsCompletedSuccessfully);
            Assert.Equal(before, await AutomationFixtureBytes(rig));
        });
    });

    [LinuxOriginalStoreFact]
    public Task Automation_delivery_unknown_changed_row_failure_remains_in_same_wait_and_cached_closes() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database); var rows = await SeedAutomationReadRows(rig, repository);
        await rig.Import("canonical.sqlite");
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            var intent = await rig.Keep(graph.Writer.PrepareOriginalChangeWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.Disable, Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            ICanonicalAutomationDefinitionOriginalProcessSource source = graph.Writer;
            var observation = await rig.Keep(source.StartOriginalChangeProcessWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            var wait = rig.Keep(observation.WaitOriginalCompletionAsync(rig.Token));
            var review = await graph.WaitForApprovalAsync(wait);
            await AutomationFixtureSql(rig, "UPDATE automations SET instruction='independently changed while review pending' WHERE id=$id;", rows[1].Id);
            var preserved = await AutomationFixtureBytes(rig);
            await graph.DecideRequestAsync(review.RequestId, HomeApprovalChoice.Accept);
            var waitFailure = await Assert.ThrowsAnyAsync<Exception>(() => wait); rig.Expect(waitFailure);
            var business = rig.Keep(graph.Writer.CommitOriginalChangeWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            var businessFailure = await Assert.ThrowsAnyAsync<Exception>(() => business); rig.Expect(businessFailure);
            Assert.False(graph.Writer.IsAcknowledgedOriginalChangeSourceRefusal(business));
            var known = AutomationDeliveryFailureReferences(wait.Exception ?? waitFailure)
                .Concat(AutomationDeliveryFailureReferences(business.Exception ?? businessFailure))
                .ToHashSet<Exception>(ReferenceEqualityComparer.Instance);
            foreach (var acquire in new Func<Task>[] { observation.CloseAndDrainOriginalAsync,
                graph.Writer.CloseAndDrainOriginalAsync, graph.Writes.CloseAndDrainOriginalAsync })
            {
                var actual = rig.Keep(acquire()); var failure = await Assert.ThrowsAnyAsync<Exception>(() => actual);
                AssertObservedAutomationDeliveryFailure(actual.Exception ?? failure, known);
                rig.Expect(failure);
            }
            Assert.Same(observation.OriginalClose, observation.CloseAndDrainOriginalAsync());
            Assert.Equal(preserved, await AutomationFixtureBytes(rig));
        });
    });

    [LinuxOriginalStoreFact]
    public Task Automation_process_preparation_reports_exact_legacy_lease_refusal_without_view_raw_fault_or_write_review() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database); var rows = await SeedAutomationReadRows(rig, repository);
        Assert.True(await rig.Keep(repository.TryAcquireLeaseAsync(rows[1].Id, "actual-active-legacy-process",
            DateTimeOffset.UtcNow.AddMinutes(30), rig.Token)));
        await rig.Import("canonical.sqlite"); var before = await AutomationFixtureBytes(rig);
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            ICanonicalAutomationDefinitionOriginalProcessSource source = graph.Writer;
            var actual = rig.Keep(source.PrepareOriginalChangeProcessWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.Disable, Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            var result = await actual;
            Assert.True(source.IsIssuedOriginalProcessPreparation(result));
            Assert.True(result.IsDeclinedBeforeEffect); Assert.Null(result.Observation);
            Assert.All(graph.Raw, raw => Assert.False(raw.IsFaulted));
            Assert.Empty((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests);
            Assert.Equal(before, await AutomationFixtureBytes(rig));
        });
    });

    private static IEnumerable<Exception> AutomationDeliveryFailureReferences(Exception actual)
    {
        yield return actual;
        if (actual is AggregateException group)
            foreach (var child in group.InnerExceptions)
                foreach (var nested in AutomationDeliveryFailureReferences(child)) yield return nested;
    }
    private static void AssertObservedAutomationDeliveryFailure(Exception actual, HashSet<Exception> observed)
    {
        if (observed.Contains(actual)) return;
        if (actual is AggregateException { InnerExceptions.Count: > 0 } group)
        { foreach (var child in group.InnerExceptions) AssertObservedAutomationDeliveryFailure(child, observed); return; }
        ExceptionDispatchInfo.Capture(new InvalidOperationException("A new independent delivery/process/cleanup failure was not observed before close.", actual)).Throw();
    }
}
