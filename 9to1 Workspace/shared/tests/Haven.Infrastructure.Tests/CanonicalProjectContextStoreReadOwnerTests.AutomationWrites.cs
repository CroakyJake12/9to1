using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Haven.Infrastructure.Tests;

// SAME actual maintained SQLite/OS/Home Rig; no scheduler, Task/model or fake grant.
public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [LinuxOriginalStoreFact]
    public Task Manual_recovery_disables_only_the_selected_row_preserving_unknown_columns_and_run_history() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database); var rows = await SeedAutomationReadRows(rig, repository);
        await AutomationFixtureSql(rig, "ALTER TABLE automations ADD COLUMN future_descriptor BLOB;");
        await AutomationFixtureSql(rig, "UPDATE automations SET future_descriptor=x'00ff0102' WHERE id=$id;", rows[1].Id);
        var now = DateTimeOffset.UtcNow;
        var run = new AutomationRun(Guid.NewGuid(), rows[1].Id, AutomationRunStatus.Succeeded, now, now, now, "preserved actual history", null, null);
        await rig.Keep(repository.CompleteRunAsync(run, rows[1].NextRunAt, rig.Token)); await rig.Import("canonical.sqlite");
        var before = await AutomationFixtureBytes(rig); var history = await rig.Keep(repository.GetRunsAsync(rows[1].Id, 8, rig.Token));
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            var intent = await rig.Keep(graph.Writer.PrepareOriginalChangeWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.RecoverLegacy, Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            var actual = rig.Keep(graph.Writer.CommitOriginalChangeWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            var review = await graph.WaitForApprovalAsync(actual);
            Assert.False(actual.IsCompleted); Assert.Equal(before, await AutomationFixtureBytes(rig));
            Assert.Equal("automations", review.Scope.TargetAppId); Assert.Equal("automations.recover", review.Scope.ActionName);
            Assert.True(review.Policy.RequiresPerActionApproval); await graph.DecideRequestAsync(review.RequestId, HomeApprovalChoice.Accept);
            var ack = await actual;
            Assert.False(ack.Definition.IsEnabled); Assert.Equal(1L, ack.Definition.Revision);
            Assert.Equal(AutomationOperationalState.NeedsAttention, ack.Definition.OperationalState);
            var completedReview = await rig.Keep(graph.Permissions.ReadRequestObservationAsync(review.RequestId, rig.Token));
            Assert.NotNull(completedReview); Assert.Equal(HomePermissionRequestState.Succeeded, completedReview!.State);
            Assert.Equal("HOME_AUTOMATION_DEFINITION_RECOVERED_NEEDS_REVIEW", completedReview.ResultCode);
            Assert.Equal(rig.Actor.ActorId, ack.Definition.OwnerBinding!.ActorId);
            var atomic = Assert.Single(graph.Raw.OfType<Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>>(),
                task => graph.Writer.IsOriginalAtomicChangeTask(intent, task));
            Assert.True(graph.Writer.IsOwnedOriginalChangeAcknowledgment(intent, ack, atomic));
            Assert.True(graph.Writer.IsOwnedOriginalChangeNativeRelease(intent, atomic));
            var refreshed = await graph.SelectAsync(rows[1].Id); Assert.False(refreshed.Row.Value.IsEnabled);
            Assert.Equal(row.RetainedProtectedDescriptors.Count, refreshed.Row.RetainedProtectedDescriptors.Count);
            foreach (var pair in row.RetainedProtectedDescriptors.Where(pair => pair.Key != "is_enabled"))
                Assert.Equal(pair.Value, refreshed.Row.RetainedProtectedDescriptors[pair.Key]);
            Assert.Equal(history, await rig.Keep(repository.GetRunsAsync(rows[1].Id, 8, rig.Token)));
            Assert.Equal(rows.Where(value => value.Id != rows[1].Id).OrderBy(value => value.Id),
                (await rig.Keep(repository.GetAllAsync(rig.Token))).Where(value => value.Id != rows[1].Id).OrderBy(value => value.Id));
        });
    });
    [LinuxOriginalStoreFact]
    public Task Reopened_same_operation_observes_exact_receipt_without_a_second_row_or_descriptor_effect() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database); var rows = await SeedAutomationReadRows(rig, repository);
        await rig.Import("canonical.sqlite"); var operation = Guid.NewGuid(); byte[]? durable = null;
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            var intent = await rig.Keep(graph.Writer.PrepareOriginalChangeWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.Disable, operation, rig.Scope, graph.Retain, rig.Token));
            var actual = rig.Keep(graph.Writer.CommitOriginalChangeWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            var review = await graph.WaitForApprovalAsync(actual);
            await graph.DecideRequestAsync(review.RequestId, HomeApprovalChoice.Accept); var ack = await actual;
            var completedReview = await rig.Keep(graph.Permissions.ReadRequestObservationAsync(review.RequestId, rig.Token));
            Assert.NotNull(completedReview); Assert.Equal(HomePermissionRequestState.Succeeded, completedReview!.State);
            Assert.Equal("HOME_AUTOMATION_DEFINITION_DISABLED", completedReview.ResultCode);
            Assert.Equal(AutomationOperationalState.Disabled, ack.Definition.OperationalState); durable = await AutomationFixtureBytes(rig);
        });
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            var intent = await rig.Keep(graph.Writer.PrepareOriginalChangeWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.Disable, operation, rig.Scope, graph.Retain, rig.Token));
            Assert.True(intent.IsRecoveredOperation);
            var actual = rig.Keep(graph.Writer.CommitOriginalChangeWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            await graph.DecideAsync(actual, HomeApprovalChoice.Accept); var ack = await actual;
            Assert.True(ack.IsRecoveredOperation); Assert.Equal(operation, ack.OperationId); Assert.Equal(1L, ack.Definition.Revision);
            Assert.Equal(durable, await AutomationFixtureBytes(rig));
        });
    });
    [LinuxOriginalStoreFact]
    public Task Declined_change_keeps_every_row_and_closes_actual_original_owners_healthy() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database); var rows = await SeedAutomationReadRows(rig, repository);
        await rig.Import("canonical.sqlite"); var before = await AutomationFixtureBytes(rig);
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            var intent = await rig.Keep(graph.Writer.PrepareOriginalChangeWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.Disable, Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            var actual = rig.Keep(graph.Writer.CommitOriginalChangeWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            await graph.DecideAsync(actual, HomeApprovalChoice.Decline); rig.Expect(await Assert.ThrowsAnyAsync<Exception>(() => actual));
            Assert.True(graph.Writer.IsAcknowledgedOriginalChangeSourceRefusal(actual));
            await graph.ObserveAcknowledgedOriginalHomeRefusalAsync();
            Assert.Equal(before, await AutomationFixtureBytes(rig));
            var alias = Task.FromException<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>(actual.Exception!.InnerExceptions[0]);
            _ = rig.Keep(alias); Assert.False(graph.Writer.IsAcknowledgedOriginalChangeSourceRefusal(alias));
            await rig.Keep(graph.Writer.CloseAndDrainOriginalAsync()); await rig.Keep(graph.Writes.CloseAndDrainOriginalAsync());
            Assert.True(graph.Writer.OriginalClose!.IsCompletedSuccessfully); Assert.True(graph.Writes.OriginalClose!.IsCompletedSuccessfully);
        });
    });
    [LinuxOriginalStoreFact]
    public Task Stored_active_legacy_run_lease_refuses_before_review_without_clearing_canceling_or_enqueuing() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database); var rows = await SeedAutomationReadRows(rig, repository);
        var now = DateTimeOffset.UtcNow;
        Assert.True(await rig.Keep(repository.TryAcquireLeaseAsync(rows[1].Id, "actual-pending-legacy-run", now.AddMinutes(30), rig.Token)));
        await rig.Import("canonical.sqlite"); var before = await AutomationFixtureBytes(rig);
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            var actual = rig.Keep(graph.Writer.PrepareOriginalChangeWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.RecoverLegacy, Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            rig.Expect(await Assert.ThrowsAnyAsync<Exception>(() => actual));
            Assert.True(graph.Writer.IsAcknowledgedOriginalChangeSourceRefusal(actual));
            Assert.Empty((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests);
            Assert.Equal(before, await AutomationFixtureBytes(rig)); Assert.Empty(await rig.Keep(repository.GetRunsAsync(rows[1].Id, 8, rig.Token)));
        });
    });
    [LinuxOriginalStoreFact]
    public Task Changed_full_row_refuses_before_review_and_retains_unknown_original_failure() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database); var rows = await SeedAutomationReadRows(rig, repository);
        await rig.Import("canonical.sqlite");
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            var intent = await rig.Keep(graph.Writer.PrepareOriginalChangeWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.Disable, Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            await AutomationFixtureSql(rig, "UPDATE automations SET instruction='new actual outside instruction' WHERE id=$id;", rows[1].Id);
            var before = await AutomationFixtureBytes(rig);
            var actual = rig.Keep(graph.Writer.CommitOriginalChangeWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            rig.Expect(await Assert.ThrowsAnyAsync<Exception>(() => actual));
            Assert.False(graph.Writer.IsAcknowledgedOriginalChangeSourceRefusal(actual));
            Assert.Empty((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests);
            Assert.Equal(before, await AutomationFixtureBytes(rig));
        });
    });
    [LinuxOriginalStoreFact]
    public Task A_changed_outer_operation_identity_cannot_borrow_the_nested_original_descriptor_acknowledgment() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database); var rows = await SeedAutomationReadRows(rig, repository);
        await rig.Import("canonical.sqlite"); var operation = Guid.NewGuid();
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            var intent = await rig.Keep(graph.Writer.PrepareOriginalChangeWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.Disable, operation, rig.Scope, graph.Retain, rig.Token));
            var original = rig.Keep(graph.Writer.CommitOriginalChangeWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            await graph.DecideAsync(original, HomeApprovalChoice.Accept); await original;
        });
        // A genuine external fixture write corrupts only the outer stored operation ID.
        // Its nested original descriptor/receipt remains byte-for-byte unchanged.
        await using (var connection = await rig.Keep(rig.Database.OpenAsync(rig.Token)))
        await using (var command = connection.CreateCommand())
        {
            var key = "canonical.automation-operation.v1." + operation.ToString("N");
            command.CommandText = "SELECT value FROM settings WHERE key=$key;"; command.Parameters.AddWithValue("$key", key);
            var originalText = Assert.IsType<string>(await rig.Keep(command.ExecuteScalarAsync(rig.Token)));
            var changed = JsonNode.Parse(originalText)!.AsObject(); var preserved = changed["Descriptor"]!.ToJsonString();
            changed["OperationId"] = Guid.NewGuid().ToString("D");
            Assert.Equal(preserved, changed["Descriptor"]!.ToJsonString());
            command.CommandText = "UPDATE settings SET value=$value WHERE key=$key;";
            command.Parameters.AddWithValue("$value", changed.ToJsonString()); Assert.Equal(1, await rig.Keep(command.ExecuteNonQueryAsync(rig.Token)));
        }
        var before = await AutomationFixtureBytes(rig);
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            var original = rig.Keep(graph.Writer.PrepareOriginalChangeWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.Disable, operation, rig.Scope, graph.Retain, rig.Token));
            rig.Expect(await Assert.ThrowsAnyAsync<Exception>(() => original));
            Assert.False(graph.Writer.IsAcknowledgedOriginalChangeSourceRefusal(original));
            Assert.Empty((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests);
            Assert.Equal(before, await AutomationFixtureBytes(rig));
        });
    });
    [LinuxOriginalStoreFact]
    public Task Process_retirement_withdraws_only_its_pending_review_before_joining_the_same_original_change() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database); var rows = await SeedAutomationReadRows(rig, repository);
        await rig.Import("canonical.sqlite"); var before = await AutomationFixtureBytes(rig);
        await WithAutomationWriter(rig, repository, async graph =>
        {
            var (page, row) = await graph.SelectAsync(rows[1].Id);
            var intent = await rig.Keep(graph.Writer.PrepareOriginalChangeWithinSourceAsync(page, row,
                CanonicalAutomationOriginalChangeKind.Disable, Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            var original = rig.Keep(graph.Writer.CommitOriginalChangeWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            var review = await graph.WaitForApprovalAsync(original); Assert.False(original.IsCompleted);
            graph.Writer.RequestOriginalRetirement(); var sameClose = rig.Keep(graph.Writer.CloseAndDrainOriginalAsync());
            Assert.Same(sameClose, graph.Writer.CloseAndDrainOriginalAsync());
            rig.Expect(await Assert.ThrowsAnyAsync<Exception>(() => original));
            await sameClose.WaitAsync(TimeSpan.FromSeconds(15), rig.Token);
            Assert.True(graph.Writer.IsAcknowledgedOriginalChangeSourceRefusal(original));
            await graph.ObserveAcknowledgedOriginalHomeRefusalAsync();
            Assert.True(sameClose.IsCompletedSuccessfully); Assert.Equal(before, await AutomationFixtureBytes(rig));
            var terminal = await rig.Keep(graph.Permissions.ReadRequestObservationAsync(review.RequestId, rig.Token));
            Assert.NotNull(terminal); Assert.Equal(HomePermissionRequestState.Cancelled, terminal!.State);
            Assert.Equal("HOME_AUTOMATION_REVIEW_WITHDRAWN", terminal.ResultCode);
        });
    });
    private static async Task WithAutomationWriter(Rig rig, AutomationRepository repository, Func<AutomationWriteGraph, Task> body)
    {
        var graph = new AutomationWriteGraph(rig, repository); var failures = new List<Exception>();
        try { await body(graph); } catch (Exception cause) { failures.Add(cause); }
        foreach (var acquire in new Func<Task>[] { graph.Writer.CloseAndDrainOriginalAsync, graph.Writes.CloseAndDrainOriginalAsync })
        {
            Task? close = null;
            try { close = acquire(); graph.Retain(close); await close; }
            catch (Exception cause) { rig.AddUnexpected(close?.Exception ?? cause, failures); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Actual Automation Home/SQL originals failed; preserved at " + rig.Root, failures);
    }
    private sealed class AutomationWriteGraph
    {
        private readonly Rig _rig; private readonly List<Task> _raw = []; private readonly object _gate = new();
        internal readonly CanonicalAutomationLibraryOriginalReadOwner Library;
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Writer;
        internal readonly HomeCanonicalAutomationDefinitionWriteSource Writes;
        internal readonly HomePermissionTrustService Permissions;
        internal IReadOnlyList<Task> Raw { get { lock (_gate) return _raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray(); } }
        internal AutomationWriteGraph(Rig rig, AutomationRepository repository)
        {
            _rig = rig; Library = ActualAutomationReadSource(rig, repository); Writer = new(Library);
            var policy = new HomeCanonicalAutomationDefinitionActionPolicySource(); Permissions = new(rig.Home, policy.TryGet);
            HomeCanonicalAutomationDefinitionWriteSource? actual = null;
            var resources = new ResourceAuthorizationService(rig.Profiles, [new HomeCanonicalAutomationDefinitionResourceResolver(
                () => actual ?? throw new InvalidOperationException("Actual Automation Home WRITE is unavailable."))]);
            var broker = new HomeResourceOperationBroker(resources, Permissions);
            Writes = actual = new(rig.Home, rig.Profiles, resources, broker, Permissions, Writer); Writer.BindOriginalHomeWriteSource(Writes);
            rig.RegisterOriginalAutomationGraph(this);
        }
        internal void Retain(Task raw) { _rig.Retain(raw); lock (_gate) _raw.Add(raw); }
        internal async Task ObserveAcknowledgedOriginalHomeRefusalAsync()
        {
            // The actual Home acquisition and business Commit have distinct causes.
            // Both were retained; acknowledge only this independently joined occurrence.
            var acquisition = Assert.Single(Raw, value => Writes.IsAcknowledgedOriginalWriteRefusal(value));
            Assert.True(Writer.IsAcknowledgedOriginalChangeSourceRefusal(acquisition));
            var observed = await Assert.ThrowsAnyAsync<Exception>(() => acquisition);
            Assert.True(acquisition.IsFaulted); Assert.False(acquisition.IsCanceled);
            Assert.IsType<UnauthorizedAccessException>(observed); Assert.Null(observed.InnerException);
            var complete = Assert.IsType<AggregateException>(acquisition.Exception);
            Assert.Same(observed, Assert.Single(complete.InnerExceptions));
            AssertOriginalHomeRefusalGraph(complete, observed);
            Assert.True(Writes.IsAcknowledgedOriginalWriteRefusal(acquisition));
            Assert.True(Writer.IsAcknowledgedOriginalChangeSourceRefusal(acquisition));
            _rig.Expect(complete);
        }
        private static void AssertOriginalHomeRefusalGraph(Exception actual, Exception sameCause)
        {
            if (ReferenceEquals(actual, sameCause)) return;
            var combined = Assert.IsType<AggregateException>(actual); Assert.NotEmpty(combined.InnerExceptions);
            foreach (var child in combined.InnerExceptions) AssertOriginalHomeRefusalGraph(child, sameCause);
        }
        internal async Task<(ICanonicalAutomationLibraryOriginalObservation Page, AutomationOwnerRead<AutomationDefinition> Row)> SelectAsync(Guid id)
        {
            var page = await _rig.Keep(((ICanonicalAutomationLibraryOriginalReadSource)Library).ReadOriginalLibraryWithinSourceAsync(
                _rig.Actor, new(Limit: 8), _rig.Scope, Retain, _rig.Token));
            return (page, Assert.Single(page.Definitions, row => row.Value.Id == id));
        }
        internal async Task<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest> WaitForApprovalAsync(Task sameOperation)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(15))
            {
                var pending = (await _rig.Keep(Permissions.GetSnapshotAsync(cancellationToken: _rig.Token))).PendingRequests
                    .Where(request => HomeCanonicalAutomationDefinitionWriteSource.IsSupportedAction(request.Scope.ActionName)).ToArray();
                if (pending.Length != 0) return Assert.Single(pending);
                if (sameOperation.IsCompleted) { await sameOperation; throw new InvalidOperationException("The original terminated before individual Home approval."); }
                await Task.Delay(TimeSpan.FromMilliseconds(10), _rig.Token);
            }
            throw new TimeoutException("No actual individual Automation Home review.");
        }
        internal async Task DecideRequestAsync(string id, HomeApprovalChoice choice) =>
            Assert.True((await _rig.Keep(Permissions.DecideAsync(id, choice, cancellationToken: _rig.Token))).Succeeded);
        internal async Task DecideAsync(Task sameOperation, HomeApprovalChoice choice)
        { var request = await WaitForApprovalAsync(sameOperation); Assert.True(request.Policy.RequiresPerActionApproval); await DecideRequestAsync(request.RequestId, choice); }
    }
    private static async Task AutomationFixtureSql(Rig rig, string sql, Guid? id = null)
    {
        await using var connection = await rig.Keep(rig.Database.OpenAsync(rig.Token)); await using var command = connection.CreateCommand();
        command.CommandText = sql; if (id is { } actual) command.Parameters.AddWithValue("$id", actual.ToString());
        await rig.Keep(command.ExecuteNonQueryAsync(rig.Token));
    }
    private static async Task<byte[]> AutomationFixtureBytes(Rig rig)
    {
        await using var connection = await rig.Keep(rig.Database.OpenAsync(rig.Token)); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM automations ORDER BY id; SELECT key,value,updated_at FROM settings WHERE key LIKE 'canonical.automation-%' ORDER BY key;";
        var rows = new List<object?[]>(); await using var reader = await rig.Keep(command.ExecuteReaderAsync(rig.Token));
        do { while (await rig.Keep(reader.ReadAsync(rig.Token))) rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray()); }
        while (await rig.Keep(reader.NextResultAsync(rig.Token)));
        return JsonSerializer.SerializeToUtf8Bytes(rows);
    }
}
