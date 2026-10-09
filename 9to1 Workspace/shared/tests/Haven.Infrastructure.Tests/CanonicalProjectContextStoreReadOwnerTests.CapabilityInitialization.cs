using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Haven.Infrastructure.Tests;

// PRIVATE/UNRUN. SAME genuine initialized SQLite + Linux kernel/Home import from the
// owning fixture. Every setup effect uses the real new Home individual WRITE source.
public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [LinuxOriginalStoreFact]
    public Task Capability_setup_requires_actual_individual_accept_before_first_rows_exist() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        await WithCapabilityInitializer(rig, async graph =>
        {
            var intent = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor,
                Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            Assert.True(graph.Creator.IsIssuedOriginalInitializationIntent(intent));
            Assert.Equal(CapabilityRegistryCatalog.BuiltIns.Count, intent.MissingDefinitions.Count);
            Assert.False(intent.MissingDefinitions is CapabilityDefinition[]);
            Assert.Equal(0L, await CapabilityCount(rig));
            var actual = rig.Keep(graph.Creator.CommitOriginalInitializationWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            var request = await graph.WaitForApprovalAsync(actual);
            Assert.False(actual.IsCompleted); Assert.Equal(0L, await CapabilityCount(rig));
            Assert.True(request.Policy.RequiresPerActionApproval);
            Assert.True((await rig.Keep(graph.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: rig.Token))).Succeeded);
            var saved = await actual;
            Assert.Equal(intent.OperationId, saved.OperationId); Assert.False(saved.IsRecoveredOperation);
            Assert.Equal(CapabilityRegistryCatalog.BuiltIns.Count, saved.InsertedDefinitionIds.Count);
            var atomic = Assert.Single(graph.Raw.OfType<Task<ICapabilityOriginalInitializationAcknowledgment>>(),
                value => graph.Creator.IsOriginalAtomicInitializationTask(intent, value));
            Assert.True(graph.Creator.IsOwnedOriginalInitializationAcknowledgment(intent, saved, atomic));
            var refreshed = await rig.Keep(graph.Catalogue.Source.ReadOriginalCapabilitiesWithinSourceAsync(rig.Actor, rig.Scope, graph.Retain, rig.Token));
            Assert.Equal(CapabilityRegistryCatalog.BuiltIns.Count, refreshed.Definitions.Count);
            Assert.Equal(CapabilityOriginalCatalogueState.Available, refreshed.State);
        });
    });
    [LinuxOriginalStoreFact]
    public Task Capability_setup_preserves_disabled_and_custom_rows_exactly() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        var repository = new CapabilityRepository(rig.Database);
        var disabled = CapabilityRegistryCatalog.BuiltIns[0] with { IsEnabled = false, Name = "retained disabled builtin", UpdatedAt = DateTimeOffset.UtcNow };
        var custom = disabled with { Id = Guid.NewGuid(), Key = "fixture-custom-capability", Name = "retained custom row", IsBuiltIn = false, IsEnabled = true };
        await rig.Keep(repository.UpsertCapabilityAsync(disabled, rig.Token));
        await rig.Keep(repository.UpsertCapabilityAsync(custom, rig.Token));
        var before = await CapabilityRowsBytesAsync(rig, [disabled.Id, custom.Id]);
        await WithCapabilityInitializer(rig, async graph =>
        {
            var intent = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor,
                Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            Assert.DoesNotContain(intent.MissingDefinitions, value => value.Id == disabled.Id || value.Id == custom.Id);
            var actual = rig.Keep(graph.Creator.CommitOriginalInitializationWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            await graph.DecideAsync(actual, HomeApprovalChoice.Accept); var saved = await actual;
            Assert.Equal(CapabilityRegistryCatalog.BuiltIns.Count - 1, saved.InsertedDefinitionIds.Count);
            Assert.Equal(before, await CapabilityRowsBytesAsync(rig, [disabled.Id, custom.Id]));
            var observed = await rig.Keep(graph.Catalogue.Source.ReadOriginalCapabilitiesWithinSourceAsync(rig.Actor, rig.Scope, graph.Retain, rig.Token));
            Assert.DoesNotContain(observed.Definitions, value => value.Id == disabled.Id);
            Assert.Contains(observed.Definitions, value => value == custom);
        });
    });
    [LinuxOriginalStoreFact]
    public Task Declined_capability_setup_has_exact_refusal_receipts_and_healthy_real_close_then_retry() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        await WithCapabilityInitializer(rig, async graph =>
        {
            var first = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor,
                Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            var declined = rig.Keep(graph.Creator.CommitOriginalInitializationWithinSourceAsync(first, rig.Scope, graph.Retain, rig.Token));
            await graph.DecideAsync(declined, HomeApprovalChoice.Decline);
            var refusal = await Assert.ThrowsAnyAsync<Exception>(() => declined); rig.Expect(refusal);
            Assert.True(graph.Creator.IsAcknowledgedOriginalInitializationSourceRefusal(declined));
            Assert.Equal(0L, await CapabilityCount(rig));
            var exactAcquisition = Assert.Single(graph.Raw, value => graph.Writes.IsAcknowledgedOriginalWriteRefusal(value));
            Assert.True(graph.Creator.IsAcknowledgedOriginalInitializationSourceRefusal(exactAcquisition));
            var alias = Task.FromException<ICapabilityOriginalInitializationAcknowledgment>(declined.Exception!.InnerExceptions[0]);
            _ = rig.Keep(alias); Assert.False(graph.Creator.IsAcknowledgedOriginalInitializationSourceRefusal(alias));
            var next = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor,
                Guid.NewGuid(), rig.Scope, graph.Retain, rig.Token));
            var retry = rig.Keep(graph.Creator.CommitOriginalInitializationWithinSourceAsync(next, rig.Scope, graph.Retain, rig.Token));
            await graph.DecideAsync(retry, HomeApprovalChoice.Accept); await retry;
            Assert.Equal(CapabilityRegistryCatalog.BuiltIns.Count, await CapabilityCount(rig));
            await rig.Keep(graph.Creator.CloseAndDrainOriginalAsync());
            await rig.Keep(graph.Writes.CloseAndDrainOriginalAsync());
            Assert.True(graph.Creator.OriginalClose!.IsCompletedSuccessfully);
            await rig.Keep(rig.Store.CloseAndDrainAsync()); Assert.True(rig.Store.OriginalClose!.IsCompletedSuccessfully);
        });
    });
    [LinuxOriginalStoreFact]
    public Task Capability_setup_operation_reopens_exact_receipt_without_second_row_effect() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite"); var operation = Guid.NewGuid(); byte[]? committed = null;
        await WithCapabilityInitializer(rig, async graph =>
        {
            var first = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor, operation,
                rig.Scope, graph.Retain, rig.Token));
            var actual = rig.Keep(graph.Creator.CommitOriginalInitializationWithinSourceAsync(first, rig.Scope, graph.Retain, rig.Token));
            await graph.DecideAsync(actual, HomeApprovalChoice.Accept); await actual;
            committed = await CapabilityRowsBytesAsync(rig, CapabilityRegistryCatalog.BuiltIns.Select(value => value.Id).ToArray());
        });
        await WithCapabilityInitializer(rig, async graph =>
        {
            var recovered = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor, operation,
                rig.Scope, graph.Retain, rig.Token));
            Assert.True(recovered.IsRecoveredOperation); Assert.Empty(recovered.MissingDefinitions);
            var actual = rig.Keep(graph.Creator.CommitOriginalInitializationWithinSourceAsync(recovered, rig.Scope, graph.Retain, rig.Token));
            await graph.DecideAsync(actual, HomeApprovalChoice.Accept); var acknowledgment = await actual;
            Assert.True(acknowledgment.IsRecoveredOperation); Assert.Equal(operation, acknowledgment.OperationId);
            Assert.Equal(committed, await CapabilityRowsBytesAsync(rig, CapabilityRegistryCatalog.BuiltIns.Select(value => value.Id).ToArray()));
            Assert.Equal(CapabilityRegistryCatalog.BuiltIns.Count, await CapabilityCount(rig));
        });
    });
    [LinuxOriginalStoreFact]
    public Task Missing_store_import_declines_setup_before_metadata_and_can_be_repaired_without_close_poison() => Run(async rig =>
    {
        await WithCapabilityInitializer(rig, async graph =>
        {
            var before = rig.MetadataReaderTasks;
            var refused = rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor, Guid.NewGuid(),
                rig.Scope, graph.Retain, rig.Token));
            var error = await Assert.ThrowsAnyAsync<Exception>(() => refused); rig.Expect(error);
            Assert.True(graph.Creator.IsAcknowledgedOriginalInitializationSourceRefusal(refused));
            Assert.Equal(before, rig.MetadataReaderTasks); Assert.Equal(0L, await CapabilityCount(rig));
            Assert.Empty((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests);
            await rig.Import("canonical.sqlite");
            var ready = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor, Guid.NewGuid(),
                rig.Scope, graph.Retain, rig.Token));
            Assert.Equal(CapabilityRegistryCatalog.BuiltIns.Count, ready.MissingDefinitions.Count);
            await rig.Keep(graph.Creator.CloseAndDrainOriginalAsync());
            await rig.Keep(graph.Writes.CloseAndDrainOriginalAsync());
            Assert.True(graph.Creator.OriginalClose!.IsCompletedSuccessfully);
        });
    });

    [LinuxOriginalStoreFact]
    public Task Changed_catalogue_refuses_setup_before_approval_without_overwriting_current_rows() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        await WithCapabilityInitializer(rig, async graph =>
        {
            var intent = await rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor, Guid.NewGuid(),
                rig.Scope, graph.Retain, rig.Token));
            var changed = CapabilityRegistryCatalog.BuiltIns[0] with { IsEnabled = false, UpdatedAt = DateTimeOffset.UtcNow };
            await rig.Keep(graph.Catalogue.Repository.UpsertCapabilityAsync(changed, rig.Token));
            var before = await CapabilityRowsBytesAsync(rig, [changed.Id]);
            var actual = rig.Keep(graph.Creator.CommitOriginalInitializationWithinSourceAsync(intent, rig.Scope, graph.Retain, rig.Token));
            var error = await Assert.ThrowsAnyAsync<Exception>(() => actual); rig.Expect(error);
            Assert.False(graph.Creator.IsAcknowledgedOriginalInitializationSourceRefusal(actual));
            Assert.Empty((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests);
            Assert.Equal(1L, await CapabilityCount(rig)); Assert.Equal(before, await CapabilityRowsBytesAsync(rig, [changed.Id]));
        });
    });
    [LinuxOriginalStoreFact]
    public Task Accepted_snapshot_reader_survives_post_retainer_fault_and_is_closed_before_prepare_returns() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        await WithCapabilityInitializer(rig, async graph =>
        {
            Task<SqliteDataReader>? rawReader = null; var once = 1; var io = new IOException("actual initialization reader publication failure");
            void Retain(Task actual)
            {
                graph.Retain(actual);
                if (actual is Task<SqliteDataReader> reader && Interlocked.Exchange(ref once, 0) != 0)
                { rawReader = reader; throw io; }
            }
            var actual = rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor, Guid.NewGuid(),
                rig.Scope, Retain, rig.Token));
            var error = await Assert.ThrowsAnyAsync<Exception>(() => actual); rig.Expect(error); rig.Expect(io);
            Assert.Contains(io, References(error)); Assert.NotNull(rawReader); Assert.True((await rawReader!).IsClosed);
            Assert.False(graph.Creator.IsAcknowledgedOriginalInitializationSourceRefusal(actual)); Assert.Equal(0L, await CapabilityCount(rig));
        });
    });
    [LinuxOriginalStoreFact]
    public Task Unknown_capability_trigger_is_retained_and_never_executed_by_setup() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        await using (var connection = await rig.Keep(rig.Database.OpenAsync(rig.Token)))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fixture_capability_trigger AFTER INSERT ON capabilities BEGIN UPDATE capabilities SET is_enabled=0 WHERE id=NEW.id; END;";
            await rig.Keep(command.ExecuteNonQueryAsync(rig.Token));
        }
        await WithCapabilityInitializer(rig, async graph =>
        {
            var actual = rig.Keep(graph.Creator.PrepareOriginalInitializationWithinSourceAsync(rig.Actor, Guid.NewGuid(),
                rig.Scope, graph.Retain, rig.Token));
            var error = await Assert.ThrowsAnyAsync<Exception>(() => actual); rig.Expect(error);
            Assert.False(graph.Creator.IsAcknowledgedOriginalInitializationSourceRefusal(actual)); Assert.Equal(0L, await CapabilityCount(rig));
            Assert.Empty((await rig.Keep(graph.Permissions.GetSnapshotAsync(cancellationToken: rig.Token))).PendingRequests);
            await using var check = await rig.Keep(rig.Database.OpenAsync(rig.Token)); await using var query = check.CreateCommand();
            query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='fixture_capability_trigger' AND type='trigger';";
            Assert.Equal(1L, Convert.ToInt64(await rig.Keep(query.ExecuteScalarAsync(rig.Token))));
        });
    });

    private static async Task WithCapabilityInitializer(Rig rig, Func<CapabilityInitializationGraph, Task> body)
    {
        var graph = new CapabilityInitializationGraph(rig); var errors = new List<Exception>();
        try { await body(graph); } catch (Exception cause) { errors.Add(cause); }
        foreach (var close in new Func<Task>[] { graph.Creator.CloseAndDrainOriginalAsync, graph.Writes.CloseAndDrainOriginalAsync })
        {
            Task? raw = null;
            try { raw = close(); await rig.Keep(raw); }
            catch (Exception cause) { rig.AddUnexpected(raw?.Exception ?? cause, errors); }
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("The actual Home capability setup fixture failed; all sources remain retained.", errors);
    }
    private sealed class CapabilityInitializationGraph
    {
        private readonly Rig _rig; private readonly List<Task> _raw = []; private readonly object _gate = new();
        internal readonly (CapabilityRepository Repository, CapabilityRegistryService Registry, CanonicalCapabilityCatalogueReadOwner Source) Catalogue;
        internal readonly CanonicalCapabilityCatalogueInitializationOwner Creator;
        internal readonly HomeCapabilityCatalogueInitializationWriteSource Writes;
        internal readonly HomePermissionTrustService Permissions;
        internal IReadOnlyList<Task> Raw { get { lock (_gate) return _raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray(); } }
        internal CapabilityInitializationGraph(Rig rig)
        {
            _rig = rig; Catalogue = CanonicalProjectContextStoreReadOwnerTests.Catalogue(rig); Creator = new(Catalogue.Source);
            var policy = new HomeCapabilityCatalogueInitializationActionPolicySource();
            Permissions = new(rig.Home, policy.TryGet);
            HomeCapabilityCatalogueInitializationWriteSource? actual = null;
            var resources = new ResourceAuthorizationService(rig.Profiles, [new HomeCapabilityCatalogueInitializationResourceResolver(
                () => actual ?? throw new InvalidOperationException("Actual Home setup owner is not composed."))]);
            var broker = new HomeResourceOperationBroker(resources, Permissions);
            Writes = actual = new(rig.Home, rig.Profiles, resources, broker, Permissions, Creator);
            Creator.BindOriginalHomeWriteSource(Writes);
        }
        internal void Retain(Task actual) { _rig.Retain(actual); lock (_gate) _raw.Add(actual); }
        internal async Task<HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest> WaitForApprovalAsync(Task actual)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(15))
            {
                var snapshot = await _rig.Keep(Permissions.GetSnapshotAsync(cancellationToken: _rig.Token));
                var pending = snapshot.PendingRequests.Where(value => value.Scope.ActionName == HomeCapabilityCatalogueInitializationWriteSource.WriteAction).ToArray();
                if (pending.Length != 0) return Assert.Single(pending);
                if (actual.IsCompleted) { await actual; throw new InvalidOperationException("Setup completed without actual individual Home approval."); }
                await Task.Delay(TimeSpan.FromMilliseconds(10), _rig.Token);
            }
            throw new TimeoutException("No actual individual Home capability setup request.");
        }
        internal async Task DecideAsync(Task actual, HomeApprovalChoice choice)
        {
            var request = await WaitForApprovalAsync(actual);
            Assert.True(request.Policy.RequiresPerActionApproval);
            Assert.True((await _rig.Keep(Permissions.DecideAsync(request.RequestId, choice, cancellationToken: _rig.Token))).Succeeded);
        }
    }
    private static async Task<byte[]> CapabilityRowsBytesAsync(Rig rig, IReadOnlyList<Guid> ids)
    {
        // Fixture-only verification over its actual privately owned initialized DB.
        await using var connection = await rig.Keep(rig.Database.OpenAsync(rig.Token));
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM capabilities ORDER BY id COLLATE BINARY;";
        var rows = new List<object?[]>(); await using var reader = await rig.Keep(command.ExecuteReaderAsync(rig.Token));
        while (await rig.Keep(reader.ReadAsync(rig.Token)))
            if (ids.Contains(Guid.Parse(reader.GetString(reader.GetOrdinal("id")))))
                rows.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(rows);
    }
}
