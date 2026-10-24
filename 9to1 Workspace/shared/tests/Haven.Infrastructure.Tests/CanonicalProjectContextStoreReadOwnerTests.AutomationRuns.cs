using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [LinuxOriginalStoreFact]
    public Task Original_automation_run_history_pages_actual_tied_keys_without_disclosing_payloads_or_writing() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database);
        var owner = ActualAutomationReadSource(rig, repository);
        var definitions = await SeedAutomationReadRows(rig, repository);
        await AutomationFixtureSql(rig, "UPDATE automations SET id=upper(id) WHERE id=$id;", definitions[0].Id);
        var saved = await SeedAutomationRunHistory(rig, definitions[0].Id, upperAutomationId: true);
        await SeedAutomationRunHistory(rig, definitions[1].Id, upperAutomationId: false,
            firstId: Guid.Parse("de000000-0000-0000-0000-000000000001"), count: 1);
        await rig.Import("canonical.sqlite");
        var library = (ICanonicalAutomationLibraryOriginalReadSource)owner;
        var source = Assert.IsAssignableFrom<ICanonicalAutomationRunOriginalReadSource>(owner);
        var page = await rig.Keep(library.ReadOriginalLibraryWithinSourceAsync(rig.Actor, new(Limit: 8), rig.Scope, rig.Retain, rig.Token));
        var row = Assert.Single(page.Definitions, value => value.Value.Id == definitions[0].Id);
        var before = await AutomationRunFixtureBytes(rig);
        var homeBefore = await File.ReadAllBytesAsync(Path.Combine(rig.Root, "home.json"), rig.Token);
        var originals = new List<Task>();
        void Retain(Task actual) { rig.Retain(actual); originals.Add(actual); }
        var observed = new List<CanonicalAutomationRunSummary>();
        ICanonicalAutomationRunOriginalContinuation? cursor = null;
        do
        {
            var actual = source.ReadOriginalRunsWithinSourceAsync(page, row, rig.Actor, 2, rig.Scope, Retain, rig.Token, cursor);
            var history = await rig.Keep(actual);
            Assert.True(source.IsIssuedOriginalRunObservation(history));
            Assert.Equal(CanonicalAutomationLibraryReadState.Available, history.State);
            Assert.Equal(rig.Actor, history.Actor); Assert.Equal(rig.Identity, history.OriginalStoreIdentity);
            Assert.Equal(row.Value.Id, history.AutomationId);
            Assert.False(history.Runs is CanonicalAutomationRunSummary[]);
            Assert.InRange(history.Runs.Count, 1, 2);
            observed.AddRange(history.Runs);
            cursor = history.NextContinuation;
            if (cursor is not null) Assert.True(source.IsIssuedOriginalRunContinuation(cursor));
        } while (cursor is not null);
        Assert.Equal(saved.Select(value => value.Id), observed.Select(value => value.RunId));
        Assert.Equal(saved.Length, observed.Select(value => value.RunId).Distinct().Count());
        Assert.All(observed, run =>
        {
            Assert.Equal(row.Value.Id, run.AutomationId);
            Assert.Equal(7L, run.Revision); Assert.Equal(3L, run.DefinitionRevision);
            Assert.Equal(new AutomationGraphBinding(Guid.Parse("f0000000-0000-0000-0000-000000000001"), 3), run.PinnedGraph);
            Assert.True(run.HasResult); Assert.True(run.HasError);
            Assert.Equal(true, run.HasExecutionDetails); Assert.Equal(true, run.HasContinuation);
        });
        var serialized = JsonSerializer.Serialize(observed);
        Assert.DoesNotContain(AutomationRunSecret, serialized, StringComparison.Ordinal);
        Assert.Equal(before, await AutomationRunFixtureBytes(rig));
        Assert.Equal(homeBefore, await File.ReadAllBytesAsync(Path.Combine(rig.Root, "home.json"), rig.Token));
        var leases = originals.OfType<Task<CanonicalSqliteOriginalStoreLease>>().ToArray();
        Assert.NotEmpty(leases);
        foreach (var raw in leases)
        {
            await raw; Assert.NotNull(raw.Result.OriginalClose);
            Assert.True(raw.Result.OriginalClose!.IsCompletedSuccessfully);
        }
        Assert.All(originals, actual => Assert.True(actual.IsCompletedSuccessfully));
    });

    [LinuxOriginalStoreFact]
    public Task Original_automation_run_history_refuses_copied_page_row_wrong_actor_and_changed_definition() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database);
        var owner = ActualAutomationReadSource(rig, repository);
        var definitions = await SeedAutomationReadRows(rig, repository);
        await SeedAutomationRunHistory(rig, definitions[0].Id, upperAutomationId: false);
        await rig.Import("canonical.sqlite");
        var library = (ICanonicalAutomationLibraryOriginalReadSource)owner;
        var source = Assert.IsAssignableFrom<ICanonicalAutomationRunOriginalReadSource>(owner);
        var page = await rig.Keep(library.ReadOriginalLibraryWithinSourceAsync(rig.Actor, new(Limit: 8), rig.Scope, rig.Retain, rig.Token));
        var row = Assert.Single(page.Definitions, value => value.Value.Id == definitions[0].Id);
        async Task Refused(ICanonicalAutomationLibraryOriginalObservation selectedPage, AutomationOwnerRead<AutomationDefinition> selectedRow,
            AuthenticatedResourceActor actor)
        {
            var actual = rig.Keep(source.ReadOriginalRunsWithinSourceAsync(selectedPage, selectedRow, actor,
                2, rig.Scope, rig.Retain, rig.Token));
            rig.Expect(await Assert.ThrowsAnyAsync<Exception>(() => actual));
        }
        var metadataBefore = rig.MetadataReaderTasks;
        await Refused(new CopiedAutomationRunLibrary(page), row, rig.Actor);
        Assert.Equal(metadataBefore, rig.MetadataReaderTasks);
        await Refused(page, row with { }, rig.Actor);
        Assert.Equal(metadataBefore, rig.MetadataReaderTasks);
        await Refused(page, row, rig.Actor with { AuthenticationRevision = "copied-authentication-revision" });
        Assert.Equal(metadataBefore, rig.MetadataReaderTasks);
        await rig.Keep(repository.UpsertAsync(definitions[0] with { Instruction = "Actual changed saved definition" }, rig.Token));
        await Refused(page, row, rig.Actor);
        var fresh = await rig.Keep(library.ReadOriginalLibraryWithinSourceAsync(rig.Actor, new(Limit: 8), rig.Scope, rig.Retain, rig.Token));
        var current = Assert.Single(fresh.Definitions, value => value.Value.Id == definitions[0].Id);
        var actualHistory = await rig.Keep(source.ReadOriginalRunsWithinSourceAsync(fresh, current, rig.Actor,
            2, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(2, actualHistory.Runs.Count);
        Assert.Equal("Actual changed saved definition", current.Value.Instruction);
    });

    [LinuxOriginalStoreFact]
    public Task Original_automation_run_continuation_is_opaque_and_bound_to_its_original_selection_and_limit() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database);
        var owner = ActualAutomationReadSource(rig, repository);
        var definitions = await SeedAutomationReadRows(rig, repository);
        await SeedAutomationRunHistory(rig, definitions[0].Id, upperAutomationId: false);
        await rig.Import("canonical.sqlite");
        var library = (ICanonicalAutomationLibraryOriginalReadSource)owner;
        var source = Assert.IsAssignableFrom<ICanonicalAutomationRunOriginalReadSource>(owner);
        var page = await rig.Keep(library.ReadOriginalLibraryWithinSourceAsync(rig.Actor, new(Limit: 8), rig.Scope, rig.Retain, rig.Token));
        var row = Assert.Single(page.Definitions, value => value.Value.Id == definitions[0].Id);
        var otherRow = Assert.Single(page.Definitions, value => value.Value.Id == definitions[1].Id);
        var first = await rig.Keep(source.ReadOriginalRunsWithinSourceAsync(page, row, rig.Actor,
            2, rig.Scope, rig.Retain, rig.Token));
        var cursor = Assert.IsAssignableFrom<ICanonicalAutomationRunOriginalContinuation>(first.NextContinuation);
        var copied = new CopiedAutomationRunContinuation();
        Assert.False(source.IsIssuedOriginalRunContinuation(copied));
        async Task Refused(AutomationOwnerRead<AutomationDefinition> selectedRow, int limit,
            ICanonicalAutomationRunOriginalContinuation selectedCursor)
        {
            var actual = rig.Keep(source.ReadOriginalRunsWithinSourceAsync(page, selectedRow, rig.Actor,
                limit, rig.Scope, rig.Retain, rig.Token, selectedCursor));
            rig.Expect(await Assert.ThrowsAnyAsync<Exception>(() => actual));
        }
        await Refused(row, 2, copied);
        await Refused(otherRow, 2, cursor);
        await Refused(row, 1, cursor);
        var second = await rig.Keep(source.ReadOriginalRunsWithinSourceAsync(page, row, rig.Actor,
            2, rig.Scope, rig.Retain, rig.Token, cursor));
        Assert.Null(second.NextContinuation);
        Assert.Equal(4, first.Runs.Concat(second.Runs).Select(value => value.RunId).Distinct().Count());
    });

    [LinuxOriginalStoreFact]
    public Task Missing_original_automation_run_table_is_setup_required_and_is_not_an_empty_success_or_repaired() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database);
        var owner = ActualAutomationReadSource(rig, repository);
        var definitions = await SeedAutomationReadRows(rig, repository);
        await rig.Import("canonical.sqlite");
        var library = (ICanonicalAutomationLibraryOriginalReadSource)owner;
        var source = Assert.IsAssignableFrom<ICanonicalAutomationRunOriginalReadSource>(owner);
        var page = await rig.Keep(library.ReadOriginalLibraryWithinSourceAsync(rig.Actor, new(Limit: 8), rig.Scope, rig.Retain, rig.Token));
        var row = Assert.Single(page.Definitions, value => value.Value.Id == definitions[0].Id);
        var empty = await rig.Keep(source.ReadOriginalRunsWithinSourceAsync(page, row, rig.Actor,
            2, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(CanonicalAutomationLibraryReadState.Available, empty.State);
        Assert.Empty(empty.Runs); Assert.Null(empty.NextContinuation);
        await AutomationFixtureSql(rig, "DROP TABLE automation_runs;");
        var missing = await rig.Keep(source.ReadOriginalRunsWithinSourceAsync(page, row, rig.Actor,
            2, rig.Scope, rig.Retain, rig.Token));
        Assert.True(source.IsIssuedOriginalRunObservation(missing));
        Assert.Equal(CanonicalAutomationLibraryReadState.SetupRequired, missing.State);
        Assert.Empty(missing.Runs); Assert.Null(missing.NextContinuation);
        Assert.NotEqual(empty.Detail, missing.Detail);
        var existing = await WithAutomationRunFixtureConnection(rig, async (connection, resources) =>
        {
            var command = connection.CreateCommand(); resources.Add(command);
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='automation_runs';";
            return Convert.ToInt64(await rig.Keep(command.ExecuteScalarAsync(rig.Token)));
        });
        Assert.Equal(0L, existing);
    });

    [LinuxOriginalStoreFact]
    public Task Original_legacy_run_schema_keeps_unavailable_revision_and_continuation_distinct_from_false_or_zero() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database);
        var owner = ActualAutomationReadSource(rig, repository);
        var definitions = await SeedAutomationReadRows(rig, repository);
        await AutomationFixtureSql(rig, """
            DROP TABLE automation_runs;
            CREATE TABLE automation_runs(id TEXT PRIMARY KEY,automation_id TEXT NOT NULL,status INTEGER NOT NULL,
                scheduled_for TEXT NOT NULL,started_at TEXT,completed_at TEXT,result TEXT,error TEXT,lease_token TEXT);
            """);
        await WithAutomationRunFixtureConnection(rig, async (connection, resources) =>
        {
            var command = connection.CreateCommand(); resources.Add(command);
            command.CommandText = "INSERT INTO automation_runs(id,automation_id,status,scheduled_for) VALUES($run,$automation,$status,$scheduled);";
            command.Parameters.AddWithValue("$run", "ee000000-0000-0000-0000-000000000001");
            command.Parameters.AddWithValue("$automation", definitions[0].Id.ToString("D"));
            command.Parameters.AddWithValue("$status", (int)AutomationRunStatus.Pending);
            command.Parameters.AddWithValue("$scheduled", "2025-02-03T04:05:06.0000000+00:00");
            Assert.Equal(1, await rig.Keep(command.ExecuteNonQueryAsync(rig.Token))); return true;
        });
        await rig.Import("canonical.sqlite");
        var library = (ICanonicalAutomationLibraryOriginalReadSource)owner;
        var page = await rig.Keep(library.ReadOriginalLibraryWithinSourceAsync(rig.Actor, new(Limit: 8), rig.Scope, rig.Retain, rig.Token));
        var selected = Assert.Single(page.Definitions, row => row.Value.Id == definitions[0].Id);
        var before = await AutomationRunFixtureBytes(rig);
        var observed = await rig.Keep(owner.ReadOriginalRunsWithinSourceAsync(page, selected, rig.Actor, 2, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(CanonicalAutomationLibraryReadState.Available, observed.State);
        var run = Assert.Single(observed.Runs);
        Assert.Equal(AutomationRunStatus.Pending, run.Status); Assert.Null(run.StartedAt); Assert.Null(run.CompletedAt);
        Assert.Null(run.Revision); Assert.Null(run.DefinitionRevision); Assert.Null(run.PinnedGraph);
        Assert.Null(run.HasExecutionDetails); Assert.Null(run.HasContinuation);
        Assert.False(run.HasResult); Assert.False(run.HasError);
        Assert.Equal(before, await AutomationRunFixtureBytes(rig));
    });

    private const string AutomationRunSecret = "original-secret-that-must-never-appear-in-run-summary";
    private static async Task<AutomationRun[]> SeedAutomationRunHistory(Rig rig, Guid automationId, bool upperAutomationId,
        Guid? firstId = null, int count = 4)
    {
        var now = new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero);
        string[] ids = firstId is { } supplied ? [supplied.ToString("D")] :
            ["AB000000-0000-0000-0000-000000000001", "CB000000-0000-0000-0000-000000000003",
                "bb000000-0000-0000-0000-000000000002", "dd000000-0000-0000-0000-000000000004"];
        var rows = ids.Take(count).Select((id, index) => new AutomationRun(Guid.Parse(id), automationId,
            index % 2 == 0 ? AutomationRunStatus.Waiting : AutomationRunStatus.Failed,
            index < 3 ? now : now.AddDays(-1), now.AddMinutes(-1), index % 2 == 0 ? null : now.AddMinutes(1),
            AutomationRunSecret, AutomationRunSecret, AutomationRunSecret)
        {
            Revision = 7, DefinitionRevision = 3,
            PinnedGraph = new(Guid.Parse("f0000000-0000-0000-0000-000000000001"), 3),
            AdmissionSnapshotJson = JsonSerializer.Serialize(new { secret = AutomationRunSecret }),
            ContinuationDescriptorJson = JsonSerializer.Serialize(new { secret = AutomationRunSecret }),
            Details = new(null, null, JsonSerializer.Serialize(new { secret = AutomationRunSecret }), null,
                "[]", null, "[]", "[]", null)
        }).ToArray();
        await WithAutomationRunFixtureConnection(rig, async (connection, resources) =>
        {
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index]; var command = connection.CreateCommand(); resources.Add(command);
                command.CommandText = """
                    INSERT INTO automation_runs(id,automation_id,status,scheduled_for,started_at,completed_at,result,error,lease_token,
                        revision,definition_revision,pinned_graph_json,admission_snapshot_json,continuation_descriptor_json,details_json)
                    VALUES($id,$automation,$status,$scheduled,$started,$completed,$result,$error,$lease,
                        $revision,$definition,$graph,$admission,$continuation,$details);
                    """;
                command.Parameters.AddWithValue("$id", ids[index]);
                command.Parameters.AddWithValue("$automation", upperAutomationId ? automationId.ToString("D").ToUpperInvariant() : automationId.ToString("D"));
                command.Parameters.AddWithValue("$status", (int)row.Status);
                command.Parameters.AddWithValue("$scheduled", row.ScheduledFor.ToString("O"));
                command.Parameters.AddWithValue("$started", row.StartedAt!.Value.ToString("O"));
                command.Parameters.AddWithValue("$completed", row.CompletedAt is { } completed ? completed.ToString("O") : (object)DBNull.Value);
                command.Parameters.AddWithValue("$result", row.Result!); command.Parameters.AddWithValue("$error", row.Error!);
                command.Parameters.AddWithValue("$lease", row.LeaseToken!); command.Parameters.AddWithValue("$revision", row.Revision);
                command.Parameters.AddWithValue("$definition", row.DefinitionRevision!.Value);
                command.Parameters.AddWithValue("$graph", JsonSerializer.Serialize(row.PinnedGraph));
                command.Parameters.AddWithValue("$admission", row.AdmissionSnapshotJson!);
                command.Parameters.AddWithValue("$continuation", row.ContinuationDescriptorJson!);
                command.Parameters.AddWithValue("$details", JsonSerializer.Serialize(row.Details));
                Assert.Equal(1, await rig.Keep(command.ExecuteNonQueryAsync(rig.Token)));
            }
            return true;
        });
        return rows;
    }
    private static Task<byte[]> AutomationRunFixtureBytes(Rig rig) => WithAutomationRunFixtureConnection(rig, async (connection, resources) =>
    {
        var command = connection.CreateCommand(); resources.Add(command);
        command.CommandText = "SELECT * FROM automations ORDER BY id; SELECT * FROM automation_runs ORDER BY id; SELECT key,value,updated_at FROM settings ORDER BY key;";
        var reader = await rig.Keep(command.ExecuteReaderAsync(rig.Token)); resources.Add(reader);
        var values = new List<object?[]>();
        do
        {
            while (await rig.Keep(reader.ReadAsync(rig.Token)))
                values.Add(Enumerable.Range(0, reader.FieldCount).Select(index => reader.IsDBNull(index) ? null : reader.GetValue(index)).ToArray());
        } while (await rig.Keep(reader.NextResultAsync(rig.Token)));
        return JsonSerializer.SerializeToUtf8Bytes(values);
    });
    // Fixture-owned setup/assertion IO only. Each actual operation and asynchronous
    // close is retained independently; failure closing one resource cannot skip another.
    private static async Task<T> WithAutomationRunFixtureConnection<T>(Rig rig,
        Func<SqliteConnection, List<IAsyncDisposable>, Task<T>> body)
    {
        var resources = new List<IAsyncDisposable>(); var failures = new List<Exception>(); T result = default!;
        try
        {
            var connection = await rig.Keep(rig.Database.OpenAsync(rig.Token)); resources.Add(connection);
            result = await body(connection, resources);
        }
        catch (Exception cause) { failures.Add(cause); }
        for (var index = resources.Count - 1; index >= 0; index--)
        {
            Task? close = null;
            try { close = rig.Keep(resources[index].DisposeAsync().AsTask()); await close; }
            catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual Automation Run fixture IO/close failures remain retained.", failures);
        return result;
    }
    private sealed class CopiedAutomationRunLibrary(ICanonicalAutomationLibraryOriginalObservation original)
        : ICanonicalAutomationLibraryOriginalObservation
    {
        public AuthenticatedResourceActor Actor => original.Actor;
        public ResourceStoreIdentity? OriginalStoreIdentity => original.OriginalStoreIdentity;
        public CanonicalAutomationLibraryReadState State => original.State;
        public string Detail => original.Detail;
        public IReadOnlyList<AutomationOwnerRead<AutomationDefinition>> Definitions => original.Definitions;
        public ICanonicalAutomationLibraryOriginalContinuation? NextContinuation => original.NextContinuation;
    }
    private sealed class CopiedAutomationRunContinuation : ICanonicalAutomationRunOriginalContinuation { }
}
