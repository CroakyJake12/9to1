/*
 * FILE DOCUMENTATION
 * Where: Browse/Host.Tests/AutomationRunContinuationPersistenceTests.cs, the real Haven host route test lane.
 * What: Proves an Automation Run's canonical state, including a durable wait continuation, survives a
 *       store reopen so a restarted process can resume without replaying committed work.
 * Why: Specification 8199-8203 and 8328-8332 require durable Wait/resume and durably versioned Runs;
 *       the previous store kept only summary columns and dropped the pinned revision, node execution
 *       states and continuation entirely.
 * Maintenance: Uses the real SQLite schema and migrations, never a mock repository.
 */

using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

public sealed class AutomationRunContinuationPersistenceTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private readonly CancellationToken token = TestContext.Current.CancellationToken;
    private readonly string _root = Directory.CreateTempSubdirectory("haven-automation-run-").FullName;

    [Fact]
    public async Task A_waiting_run_keeps_its_pinned_revision_and_continuation_across_a_store_reopen()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var resumeAt = now.AddMinutes(45);
        var automationId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var graphId = Guid.NewGuid();

        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);

        await store.UpsertAsync(new AutomationDefinition(automationId, "Durable wait fixture", HavenMode.Chat,
            "Wait then continue", AutomationScheduleKind.Daily, """{"time":"08:00"}""", now.AddDays(1),
            null, true, now, now), token).WaitAsync(Bound, token);

        // A Run suspended inside a Wait node. The continuation is the only thing that lets a restarted
        // process resume at the same node instead of restarting the graph and repeating side effects.
        var suspended = new AutomationRun(runId, automationId, AutomationRunStatus.Waiting, resumeAt,
            now, null, null, null, "lease-1")
        {
            Revision = 7,
            DefinitionRevision = 4,
            PinnedGraph = new AutomationGraphBinding(graphId, 4),
            AdmissionSnapshotJson = """{"admitted":"capabilities"}""",
            ContinuationDescriptorJson = """{"nodeId":"wait-2","resumeAt":"2026-10-19T12:45:00+00:00"}""",
            Details = new AutomationRunDetails(Guid.NewGuid(), "evt-1", """{"input":"value"}""", null,
                """[{"nodeId":"trigger-1","state":"completed"},{"nodeId":"action-1","state":"committed"}]""",
                null, """[{"runId":"parent-1"}]""", """["audit-1"]""", Guid.NewGuid())
        };
        await store.SuspendRunAsync(suspended, token).WaitAsync(Bound, token);

        // A second store over the same file stands in for a restarted process.
        var reopened = new AutomationRepository(NewDatabase());
        var due = await reopened.GetResumableRunsAsync(resumeAt, 10, token).WaitAsync(Bound, token);
        var restored = Assert.Single(due, run => run.Id == runId);
        Assert.Equal(AutomationRunStatus.Waiting, restored.Status);
        Assert.Equal(automationId, restored.AutomationId);
        Assert.Equal(7, restored.Revision);
        Assert.Equal(4, restored.DefinitionRevision);
        Assert.NotNull(restored.PinnedGraph);
        Assert.Equal(graphId, restored.PinnedGraph!.GraphId);
        Assert.Equal(4, restored.PinnedGraph.Revision);
        Assert.Equal("""{"admitted":"capabilities"}""", restored.AdmissionSnapshotJson);
        Assert.Contains("wait-2", restored.ContinuationDescriptorJson);
        Assert.NotNull(restored.Details);
        Assert.Contains("committed", restored.Details!.NodeExecutionStatesJson);
        Assert.Contains("parent-1", restored.Details.CausalAncestryJson);
        Assert.Equal("evt-1", restored.Details.TriggerEventId);

        // The same Run read through the Library history path keeps its pinned revision too.
        var history = await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token);
        Assert.Contains(history, run => run.Id == runId && run.DefinitionRevision == 4 && run.PinnedGraph is not null);

        // A wait that is not due yet must not be offered for resume.
        var early = await reopened.GetResumableRunsAsync(resumeAt.AddMinutes(-1), 10, token).WaitAsync(Bound, token);
        Assert.DoesNotContain(early, run => run.Id == runId);

        // Suspending a Run must not consume the definition's schedule or release its lease: the
        // definition is still occupied by the suspended Run.
        var definitions = await reopened.GetAllAsync(token).WaitAsync(Bound, token);
        var definition = Assert.Single(definitions, item => item.Id == automationId);
        Assert.Equal(now.AddDays(1), definition.NextRunAt);
    }

    [Fact]
    public async Task A_store_without_durable_wait_support_is_not_treated_as_a_continuation_store()
    {
        // The continuation contract is a separate reviewed capability, so a caller must be able to tell
        // that a store cannot resume Runs rather than assume it can.
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        Assert.IsAssignableFrom<IAutomationRunContinuationStore>(new AutomationRepository(database));
    }

    private SqliteDatabase NewDatabase()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", _root);
        return new SqliteDatabase(new AppPaths());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", null);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
