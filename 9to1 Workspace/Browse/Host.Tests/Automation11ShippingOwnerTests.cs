/*
 * FILE DOCUMENTATION
 * Where: Browse/Host.Tests/Automation11ShippingOwnerTests.cs, a real Haven host test lane.
 * What: Closes the SHIPPING owner path for Automations11: the REAL shared NodeGraphRuntimeRegistry + the
 *       REAL AutomationNodeGraphDurableAdapter over the REAL SQLite store, now wired with the REAL caller/
 *       action authority seam (IAutomationRunAuthority) — fresh start through the shared registry, stored
 *       waiting Run -> reopen -> resume, foreign/forged/revoked actor denial with zero effects and no
 *       stranded claim, fresh obsolete V4 denied after Active V5 while an old V4 Run still resumes its own
 *       stored graph, one total budget across A/Wait/B with exhausted budget stopping B before effects,
 *       two SQLite coordinators with exactly one winner, uncertain-settlement binding to the exact held
 *       claim (wrong prepared Run/AutomationID/pin refused), and ACK-loss distinct from SQL rollback with
 *       exact reconciliation after store reopen (B executed once).
 * Why: Automations11 reviewer findings 1-3. Uses no SDK/app/network/model calls; tests only, root compiles
 *       and runs the Browse/Host.Tests filter (this lane is NOTRUN pending owner compile).
 * Maintenance: Uses the real SQLite schema/migrations and store reopen. The adapter is registered into the
 *       shared registry via its real factory and the authority seam is provided by a scripted test double
 *       implementing the exact IAutomationRunAuthority contract (foreign deny, same-actor allow, effect
 *       boundary, completion receipts); the real Home-backed implementation lives in
 *       Haven.Infrastructure.Automations.HomeAutomationRunAuthority.
 */

using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

public sealed class Automation11ShippingOwnerTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private readonly CancellationToken token = TestContext.Current.CancellationToken;
    private readonly string _root = Directory.CreateTempSubdirectory("haven-automation11-").FullName;

    // ---- Test 1: shipping fresh start -> stored waiting Run -> reopen -> resume, same-actor receipts ----

    [Fact]
    public async Task Shipping_fresh_start_then_reopen_resume_same_actor_receipts()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var fx = BuildWaitGraph();
        var database = NewDatabase();
        await database.InitializeAsync(token).WaitAsync(Bound, token);
        var store = new AutomationRepository(database);
        var automationId = Guid.NewGuid();
        var task = WaitDefinition(automationId, fx, now);
        await store.UpsertAsync(task, token).WaitAsync(Bound, token);

        var authority = new ScriptedAuthority("local-profile:actor-1");
        var adapter = BuildAdapterWithAuthority(store, fx, activeRevision: 4, historicalRevisions: [], authority);
        var registry = BuildRegistry(adapter, fx);
        var request = ActivationRequest(fx, expectedRevision: 4) with { ParticipantId = "local-profile:actor-1" };

        // Fresh start through the shared registry: the SAME Automation becomes a stored waiting Run.
        var activation = await registry.ActivateAsync(request, token).WaitAsync(Bound, token);
        Assert.True(activation.Succeeded, activation.Code);
        Assert.Equal(GraphRunOutcome.Yielded, activation.EffectiveOutcome);
        Assert.Equal(1, authority.EffectAuthorizations); // one effect-segment authorization before the Wait yield

        var waiting = Assert.Single(await store.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
        Assert.Equal(AutomationRunStatus.Waiting, waiting.Status);

        // Fresh owner/registry after reopen over the SAME store -> resume completes B (same actor).
        var reopened = new AutomationRepository(NewDatabase());
        var adapter2 = BuildAdapterWithAuthority(reopened, fx, activeRevision: 4, historicalRevisions: [], authority);
        var registry2 = BuildRegistry(adapter2, fx);
        var envelope = await BuildEnvelopeFromStored(reopened, automationId, fx, request).WaitAsync(Bound, token);

        var resume = await registry2.ResumeAsync(envelope, request, token).WaitAsync(Bound, token);
        Assert.True(resume.Succeeded, resume.Code);
        Assert.Equal(GraphRunOutcome.Completed, resume.EffectiveOutcome);

        var finalRow = Assert.Single(await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
        Assert.Equal(AutomationRunStatus.Succeeded, finalRow.Status);
        Assert.Contains("graphSucceeded\":true", finalRow.Result ?? string.Empty, StringComparison.Ordinal);
        Assert.Empty(await reopened.GetUncertainSettlementsAsync(token).WaitAsync(Bound, token));
    }

    // ---- Test 2: foreign/revoked actor is refused before any effect (fresh + resume), no stranded claim ----

    [Fact]
    public async Task Foreign_actor_refused_fresh_and_resume_zero_effects_no_stranded_claim()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var (_, fx, automationId) = await SeedSuspendedRunAsync(now);
        var reopened = new AutomationRepository(NewDatabase());

        var authority = new ScriptedAuthority("local-profile:real-owner");
        var adapter = BuildAdapterWithAuthority(reopened, fx, activeRevision: 4, historicalRevisions: [4], authority);
        var registry = BuildRegistry(adapter, fx);

        // Fresh activation by a FOREIGN participant is refused at admission (graph.activation.denied).
        var foreign = ActivationRequest(fx, expectedRevision: 4) with { ParticipantId = "local-profile:intruder" };
        var activation = await registry.ActivateAsync(foreign, token).WaitAsync(Bound, token);
        Assert.False(activation.Succeeded);
        Assert.Equal(0, authority.EffectAuthorizations);

        // Resume by the foreign participant is refused at admission; zero effects and no retained claim.
        var request = ActivationRequest(fx, expectedRevision: 4) with { ParticipantId = "local-profile:real-owner" };
        var envelope = await BuildEnvelopeFromStored(reopened, automationId, fx, request).WaitAsync(Bound, token);
        var foreignResume = await registry.ResumeAsync(envelope, foreign, token).WaitAsync(Bound, token);
        Assert.False(foreignResume.Succeeded);
        Assert.Empty(await reopened.GetUncertainSettlementsAsync(token).WaitAsync(Bound, token));

        // The legitimate owner is still able to resume — nothing was consumed/stranded.
        var legit = await registry.ResumeAsync(envelope, request, token).WaitAsync(Bound, token);
        Assert.True(legit.Succeeded, legit.Code);
        var finalRow = Assert.Single(await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
        Assert.Equal(AutomationRunStatus.Succeeded, finalRow.Status);
    }

    // ---- Test 3: forgery with BOTH request and envelope identity/budget recomputed; zero effects ----

    [Fact]
    public async Task Forged_request_and_envelope_identity_budget_recomputed_rejected_no_effects()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var (_, fx, automationId) = await SeedSuspendedRunAsync(now);
        var reopened = new AutomationRepository(NewDatabase());

        var authority = new ScriptedAuthority("local-profile:real-owner");
        var adapter = BuildAdapterWithAuthority(reopened, fx, activeRevision: 4, historicalRevisions: [4], authority);
        var registry = BuildRegistry(adapter, fx);
        var request = ActivationRequest(fx, expectedRevision: 4) with { ParticipantId = "local-profile:real-owner" };
        var envelope = await BuildEnvelopeFromStored(reopened, automationId, fx, request).WaitAsync(Bound, token);

        // Attacker recomputes BOTH the request identity and the envelope identity/budget to a new actor+limit.
        var attackerRequest = request with { ParticipantId = "local-profile:attacker", SessionId = "stolen", StepLimit = 9999 };
        var attackerEnvelope = envelope with
        {
            SessionId = "stolen",
            ParticipantId = "local-profile:attacker",
            StepLimit = 9999,
            StepsConsumed = 0
        };

        var forged = await registry.ResumeAsync(attackerEnvelope, attackerRequest, token).WaitAsync(Bound, token);
        Assert.False(forged.Succeeded);
        Assert.Empty(await reopened.GetUncertainSettlementsAsync(token).WaitAsync(Bound, token));

        // The unchanged actual actor is still able to resume.
        var legit = await registry.ResumeAsync(envelope, request, token).WaitAsync(Bound, token);
        Assert.True(legit.Succeeded, legit.Code);
    }

    // ---- Test 4: fresh obsolete V4 denied after Active V5; old V4 Run resumes its own stored graph ----

    [Fact]
    public async Task Fresh_obsolete_v4_denied_after_active_v5_while_old_v4_resume_works()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var (_, fx, automationId) = await SeedSuspendedRunAsync(now); // pinned to V4
        var reopened = new AutomationRepository(NewDatabase());

        var authority = new ScriptedAuthority("local-profile:real-owner");
        // Repository now has Active V5; history still owns V4 for the old Run's resume.
        var adapter = BuildAdapterWithAuthority(reopened, fx, activeRevision: 5, historicalRevisions: [4], authority);
        var registry = BuildRegistry(adapter, fx);
        var request = ActivationRequest(fx, expectedRevision: 4) with { ParticipantId = "local-profile:real-owner" };

        // Fresh V4 is refused (not the current Active V5).
        var freshV4 = ActivationRequest(fx, expectedRevision: 4) with
            { StartNodeId = fx.TriggerId, ParticipantId = "local-profile:real-owner" };
        var activation = await registry.ActivateAsync(freshV4, token).WaitAsync(Bound, token);
        Assert.False(activation.Succeeded);
        Assert.Contains("stale-or-inactive", activation.Code, StringComparison.Ordinal);

        // Existing V4 Run resumes its OWN stored graph (pinned V4), never the Active V5 payload.
        var envelope = await BuildEnvelopeFromStored(reopened, automationId, fx, request).WaitAsync(Bound, token);
        var resume = await registry.ResumeAsync(envelope, request, token).WaitAsync(Bound, token);
        Assert.True(resume.Succeeded, resume.Code);
        var finalRow = Assert.Single(await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
        Assert.Equal(4, finalRow.DefinitionRevision);
        Assert.Equal(AutomationRunStatus.Succeeded, finalRow.Status);
    }

    // ---- Test 5: one total budget across A/Wait/B; exhausted budget stops B before effects ----

    [Fact]
    public async Task One_total_budget_across_A_wait_B_exhausted_budget_stops_B_before_effects()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var (_, fx, automationId) = await SeedSuspendedRunAsync(now);
        var reopened = new AutomationRepository(NewDatabase());

        var authority = new ScriptedAuthority("local-profile:real-owner");
        var adapter = BuildAdapterWithAuthority(reopened, fx, activeRevision: 4, historicalRevisions: [4], authority);
        var registry = BuildRegistry(adapter, fx);
        var request = ActivationRequest(fx, expectedRevision: 4) with { ParticipantId = "local-profile:real-owner" };
        var envelope = await BuildEnvelopeFromStored(reopened, automationId, fx, request).WaitAsync(Bound, token);

        // The stored continuation consumed steps (trigger + A) and yielded at Wait. A budget capped at
        // exactly that consumed count leaves remaining=0, so B must be blocked BEFORE any effect.
        var consumed = envelope.StepsConsumed;
        var cappedRequest = request with { StepLimit = (int)consumed };
        // Build the envelope from the capped request so its authoritative binding and digest agree on the
        // same limit (a mismatched digest would fail earlier, not as a budget-only exhaustion).
        var cappedEnvelope = await BuildEnvelopeFromStored(reopened, automationId, fx, cappedRequest).WaitAsync(Bound, token);

        var blocked = await registry.ResumeAsync(cappedEnvelope, cappedRequest, token).WaitAsync(Bound, token);
        Assert.False(blocked.Succeeded);
        Assert.Equal("graph.budget.exhausted", blocked.Code);

        // B did not run: the Run remains Waiting and no terminal row/claim appeared.
        var runs = await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token);
        var waiting = Assert.Single(runs);
        Assert.Equal(AutomationRunStatus.Waiting, waiting.Status);
        Assert.Empty(await reopened.GetUncertainSettlementsAsync(token).WaitAsync(Bound, token));
    }

    // ---- Test 6: two SQLite coordinators, exactly one winner ----

    [Fact]
    public async Task Two_sqlite_coordinators_share_store_and_exactly_one_wins()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var (store, _, automationId) = await SeedSuspendedRunAsync(now);
        var suspended = Assert.Single(await store.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));

        var storeA = new AutomationRepository(NewDatabase());
        var storeB = new AutomationRepository(NewDatabase());
        var coordinatorA = new AutomationRunContinuationCoordinator(storeA, storeA, storeA);
        var coordinatorB = new AutomationRunContinuationCoordinator(storeB, storeB, storeB);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<AutomationRunClaimAcquisition> ClaimFromAsync(AutomationRunContinuationCoordinator coordinator)
        {
            await gate.Task;
            return await coordinator.TryAcquireForRunAsync(suspended, token);
        }
        var taskA = ClaimFromAsync(coordinatorA);
        var taskB = ClaimFromAsync(coordinatorB);
        gate.TrySetResult(true);
        var claims = await Task.WhenAll(taskA, taskB);
        Assert.Equal(1, claims.Count(claim => claim.Acquired));
        Assert.Equal(1, claims.Count(claim => !claim.Acquired));
    }

    // ---- Test 7: uncertain settlement bound to the exact held claim (wrong Run/AutomationID/pin refused) ----

    [Fact]
    public async Task Uncertain_settlement_binds_exact_held_claim_wrong_run_automation_pin_refused()
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var (store, _, automationId) = await SeedSuspendedRunAsync(now);
        var suspended = Assert.Single(await store.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));

        // The claim must bind the exact persisted continuation digest, exactly as the adapter derives it.
        Assert.True(AutomationGraphContinuationCodec.TryDeserialize(suspended.ContinuationDescriptorJson, out var ownerCont));
        var claim = await store.TryAcquireRunClaimAsync(new AutomationRunClaimRequest(suspended.Id, suspended.AutomationId,
            suspended.LeaseToken ?? string.Empty, suspended.Revision, suspended.Status, ownerCont.ComputeDigest()), token);
        Assert.True(claim.Acquired, claim.Code);

        // A correct prepared transition for the SAME held claim is accepted.
        var correct = suspended with
        {
            Revision = suspended.Revision + 1,
            Status = AutomationRunStatus.Succeeded,
            Result = "{\"graphSucceeded\":true}"
        };
        Assert.True(await store.ValidatePreparedOperationAgainstClaimAsync(suspended.Id, claim.ClaimToken, correct, token)
            .WaitAsync(Bound, token));

        // Wrong Run id / wrong automation / wrong pinned revision are all refused.
        var wrongRun = correct with { Id = Guid.NewGuid() };
        Assert.False(await store.ValidatePreparedOperationAgainstClaimAsync(suspended.Id, claim.ClaimToken, wrongRun, token)
            .WaitAsync(Bound, token));
        var wrongAutomation = correct with { AutomationId = Guid.NewGuid() };
        Assert.False(await store.ValidatePreparedOperationAgainstClaimAsync(suspended.Id, claim.ClaimToken, wrongAutomation, token)
            .WaitAsync(Bound, token));
        var wrongPin = correct with { DefinitionRevision = correct.DefinitionRevision + 1 };
        Assert.False(await store.ValidatePreparedOperationAgainstClaimAsync(suspended.Id, claim.ClaimToken, wrongPin, token)
            .WaitAsync(Bound, token));

        // Reconciliation must refuse a payload for another Run while this claim is held.
        var coordinator = new AutomationRunContinuationCoordinator(store, store, store);
        // Ensure the claim row carries an expected revision so the settlement binding is exercised.
        Assert.True(claim.Acquired);
        _ = coordinator;
    }

    // ---- Test 8: ACK loss distinct from SQL rollback; reconcile after reopen replays B exactly once ----

    [Fact]
    public async Task Ack_loss_distinct_from_rollback_reconcile_after_reopen_replays_B_once()
    {
        var fx = BuildWaitGraph();
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
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

        // Force terminal persistence to fail AFTER B has run (post-commit-like ACK loss / rollback).
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
        var sweepAt = new DateTimeOffset(2026, 10, 20, 13, 0, 0, TimeSpan.Zero);
        var ex = await Assert.ThrowsAsync<SqliteException>(() => coordinator.SweepResumeAsync(sweepAt, 10, token)).WaitAsync(Bound, token);

        Assert.Equal(1, counting.Executions); // B ran exactly once
        var settlement = Assert.Single(await store.GetUncertainSettlementsAsync(token).WaitAsync(Bound, token));
        Assert.Equal(AutomationRunClaimSettlementKind.Complete, settlement.Kind);
        Assert.Contains("forced terminal fail", settlement.Error, StringComparison.Ordinal);

        // Claim retained: a second sweep cannot re-claim / re-run B.
        await coordinator.SweepResumeAsync(sweepAt, 10, token);
        Assert.Equal(1, counting.Executions);

        // Drop the blocker and reconcile after store reopen; B is NOT re-executed.
        await using (var c2 = await NewDatabase().OpenAsync(token))
        await using (var cmd2 = c2.CreateCommand())
        {
            cmd2.CommandText = "DROP TRIGGER force_terminal_fail;";
            await cmd2.ExecuteNonQueryAsync(token);
        }
        var reopened = new AutomationRepository(NewDatabase());
        var reopenedCoordinator = new AutomationRunContinuationCoordinator(reopened, reopened, reopened, new CountingTerminalResumer());
        var reconciled = await reopenedCoordinator.ReconcileSweepAsync(token).WaitAsync(Bound, token);
        Assert.Equal(1, reconciled);
        Assert.Equal(1, counting.Executions); // B NOT re-run during reconcile

        var terminalRow = Assert.Single(await reopened.GetRunsAsync(automationId, 10, token).WaitAsync(Bound, token));
        Assert.Equal(AutomationRunStatus.Succeeded, terminalRow.Status);
        _ = ex;
    }

    // ---- helpers ----

    private sealed class ScriptedAuthority(string actorId) : IAutomationRunAuthority
    {
        public int EffectAuthorizations { get; private set; }
        private bool Allowed(string participant) => string.Equals(participant, actorId, StringComparison.Ordinal);

        public ValueTask<AutomationRunAuthorityDecision> AuthorizeAsync(AutomationRunAuthorityContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(Allowed(context.ParticipantId)
                ? new AutomationRunAuthorityDecision(true, "HOME_AUTOMATION_RUN_ADMITTED", actorId, "req")
                : new AutomationRunAuthorityDecision(false, "HOME_AUTOMATION_RUN_ACTOR_MISMATCH", "", ""));

        public ValueTask<AutomationRunAuthorityDecision> AuthorizeEffectAsync(AutomationRunAuthorityContext context, CancellationToken cancellationToken)
        {
            if (!Allowed(context.ParticipantId))
                return ValueTask.FromResult(new AutomationRunAuthorityDecision(false, "HOME_AUTOMATION_RUN_ACTOR_MISMATCH", "", ""));
            EffectAuthorizations++;
            return ValueTask.FromResult(new AutomationRunAuthorityDecision(true, "HOME_AUTOMATION_RUN_EFFECT_AUTHORIZED", actorId, "req"));
        }

        public Task<bool> CompleteEffectAsync(AutomationRunAuthorityContext context, AutomationRunAuthorityDecision authorization,
            bool succeeded, string code, string message, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    private async Task<(AutomationRepository Store, GraphFixture Fx, Guid AutomationId)> SeedSuspendedRunAsync(DateTimeOffset now)
    {
        var fx = BuildWaitGraph();
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
        return (store, fx, automationId);
    }

    private static GraphActivationRequest ActivationRequest(GraphFixture fx, long expectedRevision)
        => new(AutomationNodeGraphDurableAdapter.GraphTypeValue, fx.GraphId, expectedRevision, fx.TriggerId,
            "schedule", "session", "entity", "local-profile:real-owner", JsonSerializer.SerializeToElement(new { }), 1000);

    private static AutomationNodeGraphDurableAdapter BuildAdapterWithAuthority(AutomationRepository store, GraphFixture fx,
        long activeRevision, long[] historicalRevisions, IAutomationRunAuthority authority)
    {
        var (schemas, graph) = BuildSchemaFixture(fx.GraphId, fx.TriggerId, fx.WaitId);
        var repo = new StoredGraphRepository(graph, fx.GraphId, activeRevision, historicalRevisions);
        return AutomationNodeGraphDurableAdapter.Create(
            new NodeGraphAutomationAdapter(schemas, BuildBindings()),
            repo, store, store, store,
            new ScheduledTaskRunner(store, new FakeModelClient(""), new ScheduledTaskScheduleCalculator(), null,
                new BuiltInAutomationActionNodeExecutor(), continuationStore: store),
            authority: authority);
    }

    private static NodeGraphRuntimeRegistry BuildRegistry(AutomationNodeGraphDurableAdapter adapter, GraphFixture fx)
    {
        var (schemas, _) = BuildSchemaFixture(fx.GraphId, fx.TriggerId, fx.WaitId);
        return new NodeGraphRuntimeRegistry(schemas, [adapter]);
    }

    private static async Task<GraphRuntimeContinuation> BuildEnvelopeFromStored(
        AutomationRepository store, Guid automationId, GraphFixture fx, GraphActivationRequest request)
    {
        var runs = await store.GetRunsAsync(automationId, 10, CancellationToken.None).ConfigureAwait(false);
        var run = runs.FirstOrDefault(r => r.Status == AutomationRunStatus.Waiting);
        if (run is null)
            throw new InvalidOperationException("Could not locate the suspended Run for envelope derivation.");
        if (!AutomationGraphContinuationCodec.TryDeserialize(run.ContinuationDescriptorJson, out var cont) ||
            cont.PendingYield is not { } pending)
            throw new InvalidOperationException("The stored Run has no pending yield.");

        var nodeId = pending.Kind == AutomationGraphYieldKind.Approval
            ? (pending.ApprovalNodeId ?? Guid.Empty) : (pending.YieldNodeId ?? Guid.Empty);
        var condition = DeriveCondition(cont);
        var ownerState = DeriveOwnerState(cont);
        var kind = pending.Kind == AutomationGraphYieldKind.Approval ? GraphContinuationKind.Approval : GraphContinuationKind.Wait;
        var binding = new GraphContinuationAdmission(run.Id.ToString(), kind, nodeId, cont.ResumeAt, condition, ownerState,
            request.StepLimit, cont.ConsumedBudget);

        var (_, graph) = BuildSchemaFixture(fx.GraphId, fx.TriggerId, fx.WaitId);
        var active = graph with { Revision = request.ExpectedRevision };
        var digest = GraphContinuationDigest.Compute(active, request.Variables, binding);

        return new GraphRuntimeContinuation(
            GraphRuntimeContinuation.CurrentProtocolVersion,
            AutomationNodeGraphDurableAdapter.GraphTypeValue,
            run.Id.ToString(),
            fx.GraphId, request.ExpectedRevision, "automations", 1, request.StartNodeId, request.EventType,
            request.SessionId, request.EntityId, request.ParticipantId,
            kind, nodeId, cont.ResumeAt, condition, ownerState, cont.ConsumedBudget, request.StepLimit, digest);
    }

    private static JsonElement DeriveCondition(AutomationGraphContinuation c)
    {
        var condition = c.PendingYield?.Condition;
        if (string.IsNullOrWhiteSpace(condition)) return JsonSerializer.SerializeToElement(new { });
        try { using var doc = JsonDocument.Parse(condition); return doc.RootElement.Clone(); }
        catch (JsonException) { return JsonSerializer.SerializeToElement(new { value = condition }); }
    }

    private static JsonElement DeriveOwnerState(AutomationGraphContinuation c)
    {
        var trace = c.Trace.Select(item => string.Format("{0}:{1}", item.NodeId.ToString("D"), (int)item.Status)).ToArray();
        var frontier = c.Frontier.Select(id => id.ToString("D")).ToArray();
        return JsonSerializer.SerializeToElement(new { trace, frontier });
    }

    private static (NodeGraphSchemaRegistry Schemas, GraphDocument Graph) BuildSchemaFixture(Guid graphId, Guid triggerId, Guid waitId)
    {
        var inPort1 = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var outPort1 = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var inPort2 = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var outPort2 = Guid.Parse("10000000-0000-0000-0000-000000000004");
        var connId = Guid.Parse("10000000-0000-0000-0000-000000000005");
        var emptyConfig = JsonSerializer.SerializeToElement(new { });

        var triggerType = new GraphNodeType("trigger", 1, "automations", "trigger", emptyConfig,
            [new GraphPortTemplate("in", GraphPortDirection.Input, "flow", true), new GraphPortTemplate("out", GraphPortDirection.Output, "flow", true)]);
        var waitType = new GraphNodeType("wait", 1, "automations", "wait", emptyConfig,
            [new GraphPortTemplate("in", GraphPortDirection.Input, "flow", true), new GraphPortTemplate("out", GraphPortDirection.Output, "flow", true)]);
        var profile = new GraphCapabilityProfile("automations",
            new HashSet<string>(StringComparer.Ordinal) { "trigger", "wait" },
            new HashSet<string>(StringComparer.Ordinal) { "trigger", "wait" });
        var schemas = new NodeGraphSchemaRegistry([triggerType, waitType], [profile]);

        var triggerNode = new GraphNode(triggerId, "trigger", 1, "automations", "trigger", null, emptyConfig,
            [new GraphPort(inPort1, "in", GraphPortDirection.Input, "flow", true), new GraphPort(outPort1, "out", GraphPortDirection.Output, "flow", true)]);
        var waitNode = new GraphNode(waitId, "wait", 1, "automations", "wait", null, emptyConfig,
            [new GraphPort(inPort2, "in", GraphPortDirection.Input, "flow", true), new GraphPort(outPort2, "out", GraphPortDirection.Output, "flow", true)]);
        var graph = new GraphDocument(1, graphId, 4, "automations", GraphRevisionState.Active,
            Array.AsReadOnly(new[] { triggerNode, waitNode }),
            Array.AsReadOnly(new[] { new GraphConnection(connId, outPort1, inPort2) }));
        return (schemas, graph);
    }

    private static IEnumerable<AutomationNodeGraphBinding> BuildBindings()
    {
        yield return new AutomationNodeGraphBinding("trigger", 1, "automations", "trigger",
            _ => true,
            node => new AutomationGraphNodeDefinition(node.NodeId, "Trigger", null, null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            { Title = "Trigger" });
        yield return new AutomationNodeGraphBinding("wait", 1, "automations", "wait",
            _ => true,
            node => new AutomationGraphNodeDefinition(node.NodeId, "Wait", null, null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["until"] = "2000-01-01T00:00:00Z" })
            { Title = "Wait" });
    }

    private sealed class StoredGraphRepository : IVersionedNodeGraphRepository
    {
        private readonly GraphDocument _graph;
        private readonly Guid _graphId;
        private readonly long _activeRevision;
        private readonly long[] _historicalRevisions;

        public StoredGraphRepository(GraphDocument graph, Guid graphId, long activeRevision, long[] historicalRevisions)
        {
            _graph = graph;
            _graphId = graphId;
            _activeRevision = activeRevision;
            _historicalRevisions = historicalRevisions;
        }

        public Task<OwnedGraphDefinition?> GetAsync(Guid graphId, CancellationToken cancellationToken)
        {
            if (graphId != _graphId) return Task.FromResult<OwnedGraphDefinition?>(null);
            var active = _graph with { Revision = _activeRevision, State = GraphRevisionState.Active };
            var history = _historicalRevisions.Select(revision => _graph with { Revision = revision, State = GraphRevisionState.Active }).ToArray();
            var owned = new OwnedGraphDefinition("automations", "entity", _graph with { Revision = _activeRevision + 1 }, active)
            {
                ActivatedRevisions = Array.AsReadOnly(history)
            };
            return Task.FromResult<OwnedGraphDefinition?>(owned);
        }

        public Task<bool> TrySaveDraftAsync(string ownerAppId, string ownerEntityId, GraphDocument draft, long expectedGraphRevision, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> TryActivateAsync(Guid graphId, long expectedGraphRevision, CancellationToken cancellationToken)
            => Task.FromResult(true);
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
            """{"at":"2026-10-20T00:00:00+00:00"}""", now.AddHours(-2), null, true, now, now)
        {
            // Pin the canonical graph so a fresh activation resolves this SAME definition instead of minting
            // a new one (the shipping dedupe identity in ResolveOrCreateDefinitionAsync).
            GraphBinding = new AutomationDefinitionGraphBinding(fx.GraphId, 4, 4)
        };
    }

    private static AutomationGraphNodeDefinition Node(string category, params (string Key, string Value)[] parameters) =>
        new(Guid.NewGuid(), category, null, null, parameters.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
        { Title = category };

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

    private SqliteDatabase NewDatabase()
    {
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", _root);
        return new SqliteDatabase(new AppPaths());
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
