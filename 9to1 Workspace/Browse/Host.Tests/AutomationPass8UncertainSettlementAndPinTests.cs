/*
 * FILE DOCUMENTATION
 * Where: Browse/Host.Tests/AutomationPass8UncertainSettlementAndPinTests.cs, a real Haven host test lane.
 * What: Covers pass8 corrections on the real migrated SQLite schema (v30/v31): claim eligibility fences
 *       (foreign definition token, terminal/unknown status), the direct TryAcquireLease active same-run
 *       guard, uncertain settlement that retains the claim and reconciles the exact persistence operation
 *       without re-executing B, the authenticated historical-pin snapshot (a newer Active payload with the
 *       same node IDs never runs under an old label), approval by-ID hidden by >100 other waiting Runs,
 *       and the real owner-backed registry adapter's fail-closed admission.
 * Why: Specification 8199-8207, 8227-8239, 8245-8251, 8330 and APP-AC-001..007.
 * Maintenance: Uses the real SQLite schema/migrations and store reopen; never a mock repository or a
 *       forged SQL token. Clock observations are explicit; no sleeps are used.
 */

using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

public sealed class AutomationPass8UncertainSettlementAndPinTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private readonly CancellationToken token = TestContext.Current.CancellationToken;
    private readonly string _root = Directory.CreateTempSubdirectory("haven-automation-pass8-").FullName;

    // ---- Test 1: claim eligibility — foreign definition token and terminal/unknown status refuse ----

    [Fact]
    public async Task Foreign_definition_token_and_terminal_status_cannot_claim()
    {
        var fx = BuildWaitGraph();
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);
        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task, now, token).WaitAsync(Bound, token);
        Assert.Equal(AutomationRunStatus.Waiting, suspended.Status);

        // Change the definition's lease token so it no longer equals the Run's original token.
        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE automations SET lease_token='foreign-token' WHERE id=$id;";
            command.Parameters.AddWithValue("$id", automationId.ToString());
            await command.ExecuteNonQueryAsync(token);
        }

        var coordinator = new AutomationRunContinuationCoordinator(store, store, store);
        var foreign = await coordinator.TryAcquireForRunAsync(suspended, token).WaitAsync(Bound, token);
        Assert.False(foreign.Acquired);
        Assert.Equal("definition.token.lost", foreign.Code);

        // Restore the token, then a terminal/unknown status must also refuse.
        await using (var c2 = await NewDatabase().OpenAsync(token))
        await using (var cmd2 = c2.CreateCommand())
        {
            cmd2.CommandText = "UPDATE automations SET lease_token=$t WHERE id=$id;";
            cmd2.Parameters.AddWithValue("$t", suspended.LeaseToken);
            cmd2.Parameters.AddWithValue("$id", automationId.ToString());
            await cmd2.ExecuteNonQueryAsync(token);
        }
        // A terminal persisted Run is ineligible regardless of what the caller requests. Set the row
        // terminal, then attempt to claim with the earlier (Waiting) descriptor.
        await using (var c3 = await NewDatabase().OpenAsync(token))
        await using (var cmd3 = c3.CreateCommand())
        {
            cmd3.CommandText = "UPDATE automation_runs SET status=2, completed_at=started_at WHERE id=$id;";
            cmd3.Parameters.AddWithValue("$id", suspended.Id.ToString());
            await cmd3.ExecuteNonQueryAsync(token);
        }
        var terminalClaim = await coordinator.TryAcquireForRunAsync(suspended, token).WaitAsync(Bound, token);
        Assert.False(terminalClaim.Acquired);
        Assert.Equal("claim.status.ineligible", terminalClaim.Code);
    }

    // ---- Test 2: direct TryAcquireLease cannot steal an expired occupied wait ----

    [Fact]
    public async Task TryAcquireLease_cannot_steal_expired_occupied_wait()
    {
        var fx = BuildWaitGraph();
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);
        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task, now, token).WaitAsync(Bound, token);

        // Age the definition lease past expiry; the active Waiting Run still owns the token.
        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE automations SET lease_until=$past WHERE id=$id;";
            command.Parameters.AddWithValue("$past", now.AddHours(-2).ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$id", automationId.ToString());
            await command.ExecuteNonQueryAsync(token);
        }

        // Direct acquisition must refuse, matching GetDue's active same-Run fence.
        var stolen = await store.TryAcquireLeaseAsync(automationId, "thief-token", now.AddMinutes(15), token).WaitAsync(Bound, token);
        Assert.False(stolen);
        var def = await ReadAutomationRowAsync(automationId);
        Assert.Equal(suspended.LeaseToken, def.LeaseToken); // original token preserved

        // Once the occupied Wait is cleared (terminal), a fresh lease works again (ordinary terminal semantics).
        await using (var c2 = await NewDatabase().OpenAsync(token))
        await using (var cmd2 = c2.CreateCommand())
        {
            cmd2.CommandText = "UPDATE automation_runs SET status=2, completed_at=started_at WHERE id=$id;";
            cmd2.Parameters.AddWithValue("$id", suspended.Id.ToString());
            await cmd2.ExecuteNonQueryAsync(token);
        }
        var afterTerminal = await store.TryAcquireLeaseAsync(automationId, "fresh-token", now.AddMinutes(15), token).WaitAsync(Bound, token);
        Assert.True(afterTerminal);
    }

    // ---- Test 3: uncertain settlement retains the claim; reconcile replays without re-executing B ----

    [Fact]
    public async Task Uncertain_settlement_retains_claim_and_exact_reconciliation_does_not_rerun_B()
    {
        var fx = BuildWaitGraph();
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);
        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task, now, token).WaitAsync(Bound, token);
        Assert.Equal(AutomationRunStatus.Waiting, suspended.Status);

        // Force terminal persistence to fail AFTER B has run (in the resumer).
        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER force_terminal_fail
                BEFORE UPDATE ON automation_runs
                WHEN NEW.status=2 AND OLD.status<>2
                BEGIN SELECT RAISE(ABORT,'forced terminal fail'); END;
                """;
            await command.ExecuteNonQueryAsync(token);
        }

        var counting = new CountingTerminalResumer();
        var coordinator = new AutomationRunContinuationCoordinator(store, store, store, counting);
        var sweepAt = new DateTimeOffset(2026, 10, 19, 13, 0, 0, TimeSpan.Zero);
        await Assert.ThrowsAsync<SqliteException>(() => coordinator.SweepResumeAsync(sweepAt, 10, token)).WaitAsync(Bound, token);

        Assert.Equal(1, counting.Executions); // B ran exactly once
        var unsettled = await store.GetUncertainSettlementsAsync(token).WaitAsync(Bound, token);
        var settlement = Assert.Single(unsettled);
        Assert.Equal(AutomationRunClaimSettlementKind.Complete, settlement.Kind);
        Assert.False(string.IsNullOrWhiteSpace(settlement.RunJson));
        Assert.False(string.IsNullOrWhiteSpace(settlement.Error));

        // The claim is retained: a second sweep cannot re-claim / re-run B.
        var secondOutcome = await coordinator.SweepResumeAsync(sweepAt, 10, token);
        _ = secondOutcome;
        Assert.Equal(1, counting.Executions);
        var stillWaiting = Assert.Single(await store.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
        Assert.Equal(AutomationRunStatus.Waiting, stillWaiting.Status);

        // Drop the blocker and reconcile the EXACT persistence operation (no fresh B execution).
        await using (var c2 = await NewDatabase().OpenAsync(token))
        await using (var cmd2 = c2.CreateCommand())
        {
            cmd2.CommandText = "DROP TRIGGER force_terminal_fail;";
            await cmd2.ExecuteNonQueryAsync(token);
        }
        var reconciled = await coordinator.ReconcileSweepAsync(token).WaitAsync(Bound, token);
        Assert.Equal(1, reconciled);
        Assert.Equal(1, counting.Executions); // B NOT re-run during reconcile
        var terminalRow = Assert.Single(await store.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
        Assert.Equal(AutomationRunStatus.Succeeded, terminalRow.Status);
        Assert.Empty(await store.GetUncertainSettlementsAsync(token).WaitAsync(Bound, token));
    }

    // ---- Test 4: malformed resumer result retains the claim (no unsafe release) ----

    [Fact]
    public async Task Malformed_resumer_result_retains_claim()
    {
        var fx = BuildWaitGraph();
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);
        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task, now, token).WaitAsync(Bound, token);

        var malformed = new MalformedResumer();
        var coordinator = new AutomationRunContinuationCoordinator(store, store, store, malformed);
        var sweepAt = new DateTimeOffset(2026, 10, 19, 13, 0, 0, TimeSpan.Zero);
        await coordinator.SweepResumeAsync(sweepAt, 10, token).WaitAsync(Bound, token);
        // The malformed resumer reported acceptance but omitted the required updated Run, so the claim
        // must be RETAINED (never unsafe-released) and an uncertain settlement recorded.
        var settlement = Assert.Single(await store.GetUncertainSettlementsAsync(token).WaitAsync(Bound, token));
        Assert.Equal(AutomationRunClaimSettlementKind.MalformedResume, settlement.Kind);
        // The claim is retained: the run is not left unsafe-released to a duplicate sweep.
        var again = await coordinator.TryAcquireForRunAsync(suspended, token).WaitAsync(Bound, token);
        Assert.False(again.Acquired);
    }

    // ---- Test 5: historical pin — newer Active payload with same node IDs never runs under old label ----

    [Fact]
    public async Task Historical_pin_executes_old_revision_not_current_payload_same_node_ids()
    {
        var fx = BuildWaitGraph(); // old graph: B emits "b" (valid)
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);
        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task, now, token).WaitAsync(Bound, token);

        // Publish a NEWER Active payload with the SAME node IDs but a B that is invalid (would fail).
        // If resume wrongly used the current payload, it would fail; using the pinned snapshot succeeds.
        var invalidB = Node("Action", ("action", "emit"), ("value", "b"), ("force_invalid", "true"));
        var newGraph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion,
            [fx.Trigger, fx.ActionA, fx.Wait, invalidB],
            [new(fx.Trigger.Id, fx.ActionA.Id), new(fx.ActionA.Id, fx.Wait.Id), new(fx.Wait.Id, invalidB.Id)]);
        var newPayload = ScheduledGraphAutomationPayloadCodec.Serialize(fx.GraphId, fx.TriggerId, "Durable wait rev2",
            AutomationGraphCodec.Serialize(newGraph), null, graphId: fx.GraphId, sourceRevision: 5,
            schemaVersion: 1, sourceDigest: "d1g3st-new", capabilityProfileId: "automations");
        var updatedTask = task with { Instruction = newPayload, UpdatedAt = now };
        await store.UpsertAsync(updatedTask, token).WaitAsync(Bound, token);

        var reopened = new AutomationRepository(NewDatabase());
        var resumedRunner = new ScheduledTaskRunner(reopened, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: reopened);
        var coordinator = new AutomationRunContinuationCoordinator(reopened, reopened, reopened,
            new ScheduledGraphContinuationResumer(resumedRunner));
        var sweepAt = new DateTimeOffset(2026, 10, 19, 13, 0, 0, TimeSpan.Zero);
        var outcome = await coordinator.SweepResumeAsync(sweepAt, 10, token).WaitAsync(Bound, token);
        Assert.True(outcome.Accepted, outcome.Message);
        var final = Assert.Single(await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
        // The run completed with the OLD pinned revision (4), never the current revision (5).
        Assert.Equal(AutomationRunStatus.Succeeded, final.Status);
        Assert.Equal(4, final.DefinitionRevision);
    }

    // ---- Test 6: approval by-ID not hidden by >100 other waiting Runs; conflicting decision refuses ----

    [Fact]
    public async Task Approval_by_id_not_hidden_by_many_waiting_runs_and_conflicting_decision_refuses()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var graphId = Guid.NewGuid();
        var approvalNodeId = Guid.NewGuid();
        await store.UpsertAsync(new AutomationDefinition(automationId, "Approval fixture", HavenMode.Chat,
            "approval", AutomationScheduleKind.Once, """{"at":"2026-10-20T00:00:00+00:00"}""", now.AddDays(1),
            null, true, now, now), token).WaitAsync(Bound, token);

        var leaseToken = "approval-lease";
        Assert.True(await store.TryAcquireLeaseAsync(automationId, leaseToken, now.AddMinutes(30), token));

        var continuation = new AutomationGraphContinuation(
            AutomationGraphContinuation.CurrentVersion, graphId, 4, 1, "digest", now,
            [], [approvalNodeId], [], new Dictionary<Guid, string?>(), [], 0, AutomationGraphRunMode.Real,
            null, new AutomationGraphYield(AutomationGraphYieldKind.Approval, YieldNodeId: approvalNodeId,
                ApprovalActionJson: """{"action":"execute","summary":"transmit"}""", ApprovalNodeId: approvalNodeId));
        var targetId = Guid.NewGuid();
        var target = new AutomationRun(targetId, automationId, AutomationRunStatus.ApprovalRequired, now, now, null, null, null, leaseToken)
        {
            Revision = 3,
            DefinitionRevision = 4,
            PinnedGraph = new AutomationGraphBinding(graphId, 4),
            ContinuationDescriptorJson = AutomationGraphContinuationCodec.Serialize(continuation)
        };
        await store.SuspendRunAsync(target, token).WaitAsync(Bound, token);

        // Crowd the store with 150 other due Waiting Runs (same definition) so pagination would hide the target.
        await using (var connection = await NewDatabase().OpenAsync(token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO automation_runs(id,automation_id,status,scheduled_for,started_at,completed_at,result,error,lease_token,
                    revision,definition_revision,pinned_graph_json,admission_snapshot_json,continuation_descriptor_json,details_json)
                VALUES($id,$automationId,6,$scheduled,$started,NULL,NULL,NULL,$leaseToken,0,4,NULL,NULL,NULL,NULL);
                """;
            for (var i = 0; i < 150; i++)
            {
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                command.Parameters.AddWithValue("$automationId", automationId.ToString());
                command.Parameters.AddWithValue("$scheduled", now.AddHours(-1).ToUniversalTime().ToString("O"));
                command.Parameters.AddWithValue("$started", now.ToUniversalTime().ToString("O"));
                command.Parameters.AddWithValue("$leaseToken", "crowd-" + i);
                await command.ExecuteNonQueryAsync(token);
            }
        }

        var reopened = new AutomationRepository(NewDatabase());
        var coordinator = new AutomationRunContinuationCoordinator(reopened, reopened, reopened, null, new StubApprovalAuth(true));
        var outcome = await coordinator.ApplyApprovalDecisionAsync(
            new AutomationRunApprovalDecision(targetId, approvalNodeId, "approved", "owner", 3), now, token).WaitAsync(Bound, token);
        Assert.True(outcome.Accepted, outcome.Message);

        // A second, conflicting decision for the same Run is refused as a duplicate. Read the target by
        // ID (never pagination) so the crowd of 150 waiting Runs cannot hide it.
        var afterFirst = await reopened.GetResumableRunByIdAsync(targetId, now, token).WaitAsync(Bound, token);
        Assert.NotNull(afterFirst);
        var duplicate = await coordinator.ApplyApprovalDecisionAsync(
            new AutomationRunApprovalDecision(targetId, approvalNodeId, "rejected", "owner", afterFirst!.Revision), now, token).WaitAsync(Bound, token);
        Assert.False(duplicate.Accepted);
        Assert.Equal("approval.decision.duplicate", duplicate.Code);
    }

    // ---- Test 7: real owner-backed adapter fails closed on forged/stale/revoked admission ----

    [Fact]
    public async Task Owner_backed_adapter_fails_closed_on_forged_stale_revoked_admission()
    {
        var fx = BuildWaitGraph();
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);
        var runner = new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
            new BuiltInAutomationActionNodeExecutor(), continuationStore: store);
        var suspended = await runner.RunOneAsync(task, now, token).WaitAsync(Bound, token);

        var adapter = new AutomationNodeGraphDurableAdapter(
            new NodeGraphAutomationAdapter(new NodeGraphSchemaRegistry([], []), []),
            new NullVersionedNodeGraphRepository(),
            store, store, store,
            new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
                new BuiltInAutomationActionNodeExecutor(), continuationStore: store));

        var request = new GraphActivationRequest(AutomationNodeGraphDurableAdapter.GraphTypeValue, fx.GraphId, 4,
            fx.Trigger.Id, "schedule", "session", "entity", "participant", System.Text.Json.JsonSerializer.SerializeToElement(new { }), 1000);

        // Legitimate pin: the adapter acquires a real claim and returns an authoritative binding.
        var legitEnvelope = EnvelopeFor(suspended, fx.GraphId, 4, fx.Wait.Id, request, now, GraphContinuationKind.Wait);
        var legit = await adapter.AdmitResumeAsync(legitEnvelope, request, token);
        Assert.NotNull(legit);
        Assert.Equal(suspended.Id.ToString(), legit!.OwnerRunId);

        // Forged pin (different graphId) refuses before any effect.
        var forged = legitEnvelope with { GraphId = Guid.NewGuid() };
        Assert.Null(await adapter.AdmitResumeAsync(forged, request, token));

        // Stale pin (wrong revision) refuses before any effect.
        var stale = legitEnvelope with { Revision = 999 };
        Assert.Null(await adapter.AdmitResumeAsync(stale, request, token));

        // Revoked authority: an ApprovalRequired Run with an unauthorised actor is denied. The authority
        // gate fails closed even when the pin matches, so no claim/effect occurs.
        var approvalAutomationId = Guid.NewGuid();
        var approvalGraphId = Guid.NewGuid();
        var approvalNodeId = Guid.NewGuid();
        await store.UpsertAsync(new AutomationDefinition(approvalAutomationId, "Approval fixture", HavenMode.Chat,
            "approval", AutomationScheduleKind.Once, """{"at":"2026-10-20T00:00:00+00:00"}""", now.AddDays(1),
            null, true, now, now), token).WaitAsync(Bound, token);
        var approvalLease = "approval-lease";
        Assert.True(await store.TryAcquireLeaseAsync(approvalAutomationId, approvalLease, now.AddMinutes(30), token));
        var approvalContinuation = new AutomationGraphContinuation(
            AutomationGraphContinuation.CurrentVersion, approvalGraphId, 4, 1, "digest", now,
            [], [approvalNodeId], [], new Dictionary<Guid, string?>(), [], 0, AutomationGraphRunMode.Real,
            null, new AutomationGraphYield(AutomationGraphYieldKind.Approval, YieldNodeId: approvalNodeId,
                ApprovalActionJson: """{"action":"execute","summary":"transmit"}""", ApprovalNodeId: approvalNodeId));
        var approvalRunId = Guid.NewGuid();
        var approvalRun = new AutomationRun(approvalRunId, approvalAutomationId, AutomationRunStatus.ApprovalRequired, now, now, null, null, null, approvalLease)
        {
            Revision = 3,
            DefinitionRevision = 4,
            PinnedGraph = new AutomationGraphBinding(approvalGraphId, 4),
            ContinuationDescriptorJson = AutomationGraphContinuationCodec.Serialize(approvalContinuation)
        };
        await store.SuspendRunAsync(approvalRun, token).WaitAsync(Bound, token);

        var approvalRequest = new GraphActivationRequest(AutomationNodeGraphDurableAdapter.GraphTypeValue, approvalGraphId, 4,
            approvalNodeId, "schedule", "session", "entity", "intruder", System.Text.Json.JsonSerializer.SerializeToElement(new { }), 1000);
        var approvalEnvelope = EnvelopeFor(approvalRun, approvalGraphId, 4, approvalNodeId, approvalRequest, now,
            GraphContinuationKind.Approval);
        var revokedAdapter = new AutomationNodeGraphDurableAdapter(
            new NodeGraphAutomationAdapter(new NodeGraphSchemaRegistry([], []), []),
            new NullVersionedNodeGraphRepository(),
            store, store, store,
            new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
                new BuiltInAutomationActionNodeExecutor(), continuationStore: store),
            new StubApprovalAuth(false));
        Assert.Null(await revokedAdapter.AdmitResumeAsync(approvalEnvelope, approvalRequest, token));

        // The same Approval run with the authorised actor proceeds past the authority gate (claim acquired).
        var authorisedRequest = approvalRequest with { ParticipantId = "owner" };
        var authorisedEnvelope = approvalEnvelope with { ParticipantId = "owner" };
        var authorisedAdapter = new AutomationNodeGraphDurableAdapter(
            new NodeGraphAutomationAdapter(new NodeGraphSchemaRegistry([], []), []),
            new NullVersionedNodeGraphRepository(),
            store, store, store,
            new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
                new BuiltInAutomationActionNodeExecutor(), continuationStore: store),
            new StubApprovalAuth(true));
        var authorised = await authorisedAdapter.AdmitResumeAsync(authorisedEnvelope, authorisedRequest, token);
        Assert.NotNull(authorised);
    }

    // ---- helpers ----

    private static GraphRuntimeContinuation EnvelopeFor(AutomationRun run, Guid graphId, long revision, Guid nodeId,
        GraphActivationRequest request, DateTimeOffset now, GraphContinuationKind kind)
    {
        var variables = System.Text.Json.JsonSerializer.SerializeToElement(new { });
        var condition = System.Text.Json.JsonSerializer.SerializeToElement(new { });
        var ownerState = System.Text.Json.JsonSerializer.SerializeToElement(new { });
        var binding = new GraphContinuationAdmission(run.Id.ToString(), kind, nodeId, now, condition, ownerState, 1000, 0);
        var document = new GraphDocument(1, graphId, revision, "automations", GraphRevisionState.Active,
            Array.AsReadOnly(new[] { new GraphNode(nodeId, "trigger", 1, "automations", "trigger", null,
                System.Text.Json.JsonSerializer.SerializeToElement(new { }), Array.Empty<GraphPort>()) }),
            Array.Empty<GraphConnection>());
        var digest = GraphContinuationDigest.Compute(document, variables, binding);
        return new GraphRuntimeContinuation(
            GraphRuntimeContinuation.CurrentProtocolVersion,
            AutomationNodeGraphDurableAdapter.GraphTypeValue,
            run.Id.ToString(),
            graphId, revision, "automations", 1, request.StartNodeId, request.EventType,
            request.SessionId, request.EntityId, request.ParticipantId,
            kind, nodeId, now, condition, ownerState, 0, 1000, digest);
    }

    private sealed class CountingTerminalResumer : IAutomationRunResumer
    {
        public int Executions { get; private set; }
        public Task<AutomationRunResumeResult> ResumeAsync(AutomationRun run, CancellationToken cancellationToken)
        {
            Executions++;
            var updated = run with
            {
                Revision = run.Revision + 1,
                Status = AutomationRunStatus.Succeeded,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = null,
                Result = "{\"graphSucceeded\":true}"
            };
            return Task.FromResult(new AutomationRunResumeResult(updated, run.ScheduledFor.AddDays(1), false,
                new(true, AutomationResumeErrorKind.None, "resume.succeeded", "ok", run.Id)));
        }
    }

    private sealed class MalformedResumer : IAutomationRunResumer
    {
        public Task<AutomationRunResumeResult> ResumeAsync(AutomationRun run, CancellationToken cancellationToken) =>
            Task.FromResult(new AutomationRunResumeResult(null, null, false,
                new(true, AutomationResumeErrorKind.None, "resume.accepted", "accepted but missing run", run.Id)));
    }

    private sealed class StubApprovalAuth(bool authorized) : IAutomationApprovalAuth
    {
        public Task<bool> IsAuthorisedAsync(Guid automationId, string actorId, CancellationToken cancellationToken) => Task.FromResult(authorized);
    }

    private sealed class NullVersionedNodeGraphRepository : IVersionedNodeGraphRepository
    {
        public Task<OwnedGraphDefinition?> GetAsync(Guid graphId, CancellationToken cancellationToken) => Task.FromResult<OwnedGraphDefinition?>(null);
        public Task<bool> TrySaveDraftAsync(string ownerAppId, string ownerEntityId, GraphDocument draft, long expectedGraphRevision, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<bool> TryActivateAsync(Guid graphId, long expectedGraphRevision, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed record GraphFixture(Guid GraphId, string Digest, AutomationGraphDefinition Graph, Guid TriggerId, Guid ActionAId, Guid WaitId, Guid ActionBId, Guid ApprovalNodeId);

    private static GraphFixture BuildWaitGraph()
    {
        var trigger = Node("Trigger");
        var actionA = Node("Action", ("action", "emit"), ("value", "a"));
        var wait = Node("Wait", ("until", "2000-01-01T00:00:00Z"));
        var actionB = Node("Action", ("action", "emit"), ("value", "b"));
        var graph = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion,
            [trigger, actionA, wait, actionB],
            [new(trigger.Id, actionA.Id), new(actionA.Id, wait.Id), new(wait.Id, actionB.Id)]);
        return new GraphFixture(Guid.NewGuid(), "d1g3st-aabbccdd", graph, trigger.Id, actionA.Id, wait.Id, actionB.Id, Guid.Empty);
    }

    private static AutomationDefinition WaitDefinition(Guid automationId, GraphFixture fx, DateTimeOffset now)
    {
        var encoded = ScheduledGraphAutomationPayloadCodec.Serialize(fx.GraphId, fx.TriggerId, "Durable wait", AutomationGraphCodec.Serialize(fx.Graph), null,
            graphId: fx.GraphId, sourceRevision: 4, schemaVersion: 1, sourceDigest: fx.Digest, capabilityProfileId: "automations");
        return new AutomationDefinition(automationId, "Durable wait", HavenMode.Tasks, encoded, AutomationScheduleKind.Once,
            """{"at":"2026-10-20T00:00:00+00:00"}""", now.AddHours(-2), null, true, now, now);
    }

    private static AutomationGraphNodeDefinition Node(string category, params (string Key, string Value)[] parameters) =>
        new(Guid.NewGuid(), category, null, null, parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
        { Title = category };

    private SqliteDatabase NewDatabase()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", _root);
        return new SqliteDatabase(new AppPaths());
    }

    private async Task<(string? LeaseToken, string? NextRunAt)> ReadAutomationRowAsync(Guid automationId)
    {
        await using var connection = await NewDatabase().OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT lease_token, next_run_at FROM automations WHERE id=$id;";
        command.Parameters.AddWithValue("$id", automationId.ToString());
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.ReadAsync(token);
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private sealed class FakeModelClient(string response) : IOllamaClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModelDescriptor>>([new ModelDescriptor(
                "qwen-test", 1, "qwen", "test", "test",
                new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow)]);
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return response;
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken cancellationToken) => Task.FromResult(response);
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new OllamaToolResponse(response, []));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", null);
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
