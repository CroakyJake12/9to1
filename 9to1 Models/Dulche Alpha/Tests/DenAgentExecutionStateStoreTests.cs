using System.Text.Json;
using NineToOne.Dulche.Den;
using Xunit;

namespace Dulche.Runtime.Agents.Tests;

public sealed class DenAgentExecutionStateStoreTests
{
    [Fact]
    public async Task Original_Den_run_reopens_with_whole_snapshot_and_contiguous_events_without_another_registry()
    {
        await WithDenAsync(async (den, _) =>
        {
            var definition = await SaveDefinitionAsync(den);
            var adapter = new DenAgentExecutionStateStore(den, "personal");
            var run = Run(definition, den.PrincipalId);
            Assert.Null(await den.GetAsync<AgentRunRecord>("personal", run.AgentRunId));
            var creation = new AgentExecutionChangeSet(run.AgentRunId, 0, "create:original", Run: run,
                Events: [Event(run, 1, "queued")]);
            var initial = await adapter.CommitAsync(creation);
            Assert.Equal(0, creation.ExpectedRevision); // The absent-row comparison remains zero.
            Assert.Equal(1, initial.Revision);
            var firstRecord = Assert.IsType<AgentRunRecord>(await den.GetAsync<AgentRunRecord>("personal", run.AgentRunId));
            Assert.Equal(1, firstRecord.Revision);
            Assert.Equal(run.AgentRunId, firstRecord.Id);
            var repeated = await adapter.CommitAsync(creation);
            Assert.Equal(initial.Revision, repeated.Revision);
            Assert.Single(repeated.Events);
            var staleCreation = await Assert.ThrowsAsync<DenException>(() => adapter.CommitAsync(new(run.AgentRunId,
                0, "create:stale", Run: run, Events: [Event(run, 1, "duplicate")]), cancellationToken: DulcheOriginalTestCancellation.Current).AsTask());
            Assert.Equal(DenErrorCode.Conflict, staleCreation.Code);
            var output = new AgentOutputReference("canonical-output", "Files", "artifact", "original-file", 4);
            var next = await adapter.CommitAsync(new(run.AgentRunId, initial.Revision, "result:original",
                Run: run with { State = AgentRunState.Completed, Revision = 1 },
                Outputs: [output], Events: [Event(run, 2, "completed")],
                CompletedConsequentialActionIds: new HashSet<string> { "original-action" }));
            Assert.Equal(2, next.Revision);
            var staleNext = await Assert.ThrowsAsync<DenException>(() => adapter.CommitAsync(new(run.AgentRunId,
                initial.Revision, "result:stale", Events: [Event(run, 2, "duplicate-next")]), cancellationToken: DulcheOriginalTestCancellation.Current).AsTask());
            Assert.Equal(DenErrorCode.Conflict, staleNext.Code);
            Assert.Equal(next.Revision, (await adapter.ReadAsync(run.AgentRunId))!.Revision);
            Assert.Equal(2, next.LastEventSequence);
            Assert.Equal(output, Assert.Single(next.Outputs));
            Assert.Equal("original-action", Assert.Single(next.CompletedConsequentialActionIds));
            await WithOriginalReopenAsync(den.Store.RootPath, async reopened =>
            {
                var current = new DenAgentExecutionStateStore(new(reopened, den.AccessPolicy, den.PrincipalId), "personal");
                var actual = Assert.IsType<AgentExecutionSnapshot>(await current.ReadAsync(run.AgentRunId));
                Assert.Equal(next.Revision, actual.Revision);
                Assert.Equal(AgentRunState.Completed, actual.Run.State);
                Assert.Equal(run.AgentRunId, actual.Run.AgentRunId);
                Assert.Equal(run.SessionId, actual.Run.SessionId);
                Assert.Equal(new long[] { 1, 2 }, actual.Events.Select(item => item.Sequence));
                Assert.Equal(output, Assert.Single(actual.Outputs));
                var record = Assert.IsType<AgentRunRecord>(await den.GetAsync<AgentRunRecord>("personal", run.AgentRunId));
                Assert.Equal(record.Revision, actual.Revision);
                Assert.Equal(run.AgentId, record.AgentDefinitionId);
                Assert.Equal(run.AgentRunId, Assert.Single(await current.ListRunsAsync(run.AgentId, 10)).AgentRunId);
                Assert.Single(await den.ListAsync<AgentRunRecord>("personal"));
            });
        });
    }

    [Fact]
    public async Task Independent_original_Den_instances_refuse_stale_revision_and_mismatched_replay_without_duplicate_event()
    {
        await WithDenAsync(async (den, _) =>
        {
            var definition = await SaveDefinitionAsync(den);
            var first = new DenAgentExecutionStateStore(den, "personal");
            var run = Run(definition, den.PrincipalId);
            var creation = new AgentExecutionChangeSet(run.AgentRunId, 0, "create:once", Run: run,
                Events: [Event(run, 1, "queued")]);
            await first.CommitAsync(creation);
            await WithOriginalReopenAsync(den.Store.RootPath, async reopened =>
            {
                var second = new DenAgentExecutionStateStore(new(reopened, den.AccessPolicy, den.PrincipalId), "personal");
                var stale = Assert.IsType<AgentExecutionSnapshot>(await second.ReadAsync(run.AgentRunId));
                var accepted = await first.CommitAsync(new(run.AgentRunId, stale.Revision, "event:accepted",
                    Events: [Event(run, 2, "original-event")]));
                var error = await Assert.ThrowsAsync<DenException>(() => second.CommitAsync(new(run.AgentRunId,
                    stale.Revision, "event:stale", Events: [Event(run, 2, "stale-event")]), cancellationToken: DulcheOriginalTestCancellation.Current).AsTask());
                Assert.Equal(DenErrorCode.Conflict, error.Code);
                var replay = await second.CommitAsync(creation);
                Assert.Equal(accepted.Revision, replay.Revision);
                Assert.Equal(2, replay.Events.Count);
                Assert.Equal("original-event", replay.Events[1].Kind);
                var mismatched = await Assert.ThrowsAsync<DenException>(() => second.CommitAsync(
                    creation with { Run = run with { Objective = "different invocation" } }, cancellationToken: DulcheOriginalTestCancellation.Current).AsTask());
                Assert.Equal(DenErrorCode.IdempotencyMismatch, mismatched.Code);
                var unchanged = Assert.IsType<AgentExecutionSnapshot>(await first.ReadAsync(run.AgentRunId));
                Assert.Equal(accepted.Revision, unchanged.Revision);
                Assert.Equal(new[] { "queued", "original-event" }, unchanged.Events.Select(item => item.Kind));
                Assert.Single(await den.ListAsync<AgentRunRecord>("personal"));
            });
        });
    }

    [Fact]
    public async Task Execute_revocation_at_original_Den_write_admission_refuses_publication_then_current_permission_can_retry()
    {
        await WithDenAsync(async (den, policy) =>
        {
            var definition = await SaveDefinitionAsync(den);
            var adapter = new DenAgentExecutionStateStore(den, "personal");
            var run = Run(definition, den.PrincipalId);
            var initial = await adapter.CommitAsync(new(run.AgentRunId, 0, "create:original", Run: run,
                Events: [Event(run, 1, "queued")]));
            policy.ExecuteChecks = 0; policy.RevokeOnExecuteCheck = 3;
            var change = new AgentExecutionChangeSet(run.AgentRunId, initial.Revision, "event:revoked",
                Events: [Event(run, 2, "actual-next-event")]);
            var error = await Assert.ThrowsAsync<DenException>(() => adapter.CommitAsync(change, cancellationToken: DulcheOriginalTestCancellation.Current).AsTask());
            Assert.Equal(DenErrorCode.Forbidden, error.Code);
            Assert.Equal(3, policy.ExecuteChecks);
            var unchanged = Assert.IsType<AgentExecutionSnapshot>(await adapter.ReadAsync(run.AgentRunId));
            Assert.Equal(initial.Revision, unchanged.Revision);
            Assert.Single(unchanged.Events);
            policy.RevokeOnExecuteCheck = null;
            var accepted = await adapter.CommitAsync(change);
            Assert.Equal(initial.Revision + 1, accepted.Revision);
            Assert.Equal("actual-next-event", accepted.Events[1].Kind);
        });
    }

    [Fact]
    public async Task Foreign_namespace_and_noncontiguous_or_foreign_root_events_cannot_mutate_original_run()
    {
        await WithDenAsync(async (den, _) =>
        {
            var definition = await SaveDefinitionAsync(den);
            var adapter = new DenAgentExecutionStateStore(den, "personal");
            var run = Run(definition, den.PrincipalId);
            var initial = await adapter.CommitAsync(new(run.AgentRunId, 0, "create:original", Run: run,
                Events: [Event(run, 1, "queued")]));
            var foreign = new DenAgentExecutionStateStore(den, "organisation");
            var denied = await Assert.ThrowsAsync<DenException>(() => foreign.ReadAsync(run.AgentRunId, cancellationToken: DulcheOriginalTestCancellation.Current).AsTask());
            Assert.Equal(DenErrorCode.Forbidden, denied.Code);
            var invalidSequence = await Assert.ThrowsAsync<DenException>(() => adapter.CommitAsync(new(run.AgentRunId,
                initial.Revision, "event:gap", Events: [Event(run, 3, "gap")]), cancellationToken: DulcheOriginalTestCancellation.Current).AsTask());
            Assert.Equal(DenErrorCode.InvalidRecord, invalidSequence.Code);
            var invalidRoot = await Assert.ThrowsAsync<DenException>(() => adapter.CommitAsync(new(run.AgentRunId,
                initial.Revision, "event:foreign", Events: [Event(run, 2, "foreign") with { RootRequestId = "foreign-run" }]), cancellationToken: DulcheOriginalTestCancellation.Current).AsTask());
            Assert.Equal(DenErrorCode.InvalidRecord, invalidRoot.Code);
            Assert.Equal(initial.Revision, (await adapter.ReadAsync(run.AgentRunId))!.Revision);
            Assert.Single(await den.ListAsync<AgentRunRecord>("personal"));
        });
    }

    private static Task<AgentDefinitionRecord> SaveDefinitionAsync(DulcheDen den) => den.SaveAsync(new AgentDefinitionRecord
    {
        Id = Guid.NewGuid().ToString("D"), NamespaceId = "personal", DisplayName = "Canonical", Version = "display",
        Instructions = "original", Enabled = true,
        ModelPolicyJson = JsonSerializer.Serialize(new AgentModelPolicy(Inherit: true, RequiredCapabilities: new HashSet<string>()), DenJson.Options),
        BudgetJson = JsonSerializer.Serialize(new AgentBudgetLimits(MaxToolCalls: 3), DenJson.Options)
    }, 0, Guid.NewGuid().ToString("N"));

    private static AgentRunSnapshot Run(AgentDefinitionRecord definition, string caller)
    {
        var id = Guid.NewGuid().ToString("N"); var attempt = Guid.NewGuid().ToString("N");
        var session = Guid.NewGuid().ToString("D"); var now = DateTimeOffset.UtcNow;
        var budget = new AgentBudgetLimits(MaxToolCalls: 3);
        return new(id, definition.Id, definition.Revision, "Original objective", AgentTriggerKind.User, "actual-trigger",
            null, null, "actual-idempotency-key", null, null, [], AgentRunState.Queued,
            caller, "Connect", null, null, session, "local", null, "actual-model", "ollama.local",
            new HashSet<string>(), [], new(new HashSet<string>(), new HashSet<string>(), false, 1, 0, budget),
            budget, AgentBudgetUsage.Empty, [], [], [], [], null, null, now, null, null, null, 0, attempt,
            [new(attempt, 1, null, session, "local", "actual-model", "ollama.local", "new", [], now,
                null, null, AgentRunState.Queued, AgentBudgetUsage.Empty)]);
    }

    private static AgentExecutionEventEnvelope Event(AgentRunSnapshot run, long sequence, string kind) =>
        new(run.CurrentAttemptId, null, null, run.AgentRunId, run.CurrentAttemptId, sequence, run.CreatedAtUtc, kind, null);

    private static async Task WithOriginalReopenAsync(string root, Func<DenStore, Task> action)
    {
        DenStore? store = null; Exception? primary = null; var cleanup = new List<Exception>();
        try { store = await DenStore.OpenAsync(root); await action(store); }
        catch (Exception error) { primary = error; }
        try { if (store is not null) await store.DisposeAsync(); }
        catch (Exception error) { cleanup.Add(error); }
        Rethrow(primary, cleanup);
    }

    private static async Task WithDenAsync(Func<DulcheDen, MutablePolicy, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-den-agent-state-" + Guid.NewGuid().ToString("N"));
        var policy = new MutablePolicy(); DenStore? store = null; Exception? primary = null;
        try
        {
            store = await DenStore.CreateAsync(root, [new("personal", "personal"), new("organisation", "organisation")]);
            await action(new(store, policy, "actual-principal"), policy);
        }
        catch (Exception error) { primary = error; }
        var cleanup = new List<Exception>();
        try { if (store is not null) await store.DisposeAsync(); }
        catch (Exception error) { cleanup.Add(error); }
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (Exception error) { if (!cleanup.Any(value => ReferenceEquals(value, error))) cleanup.Add(error); }
        Rethrow(primary, cleanup);
    }

    private static void Rethrow(Exception? primary, List<Exception> cleanup)
    {
        if (primary is not null && !cleanup.Any(error => ReferenceEquals(error, primary))) cleanup.Insert(0, primary);
        if (cleanup.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanup[0]).Throw();
        if (cleanup.Count > 1) throw new AggregateException(cleanup);
    }

    private sealed class MutablePolicy : IDenAccessPolicy
    {
        public int ExecuteChecks { get; set; }
        public int? RevokeOnExecuteCheck { get; set; }
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId,
            DenPermission permission, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (principalId != "actual-principal" || namespaceId != "personal") return ValueTask.FromResult(false);
            if (permission != DenPermission.Execute) return ValueTask.FromResult(true);
            ExecuteChecks++;
            return ValueTask.FromResult(RevokeOnExecuteCheck is null || ExecuteChecks < RevokeOnExecuteCheck);
        }
    }
}
