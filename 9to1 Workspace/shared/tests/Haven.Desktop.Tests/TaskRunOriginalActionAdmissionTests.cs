using System.Reflection;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual coordinator action CAS and private receipt controls with controlled repositories,
/// actors and two separate preparation owners. Physical workspace/Cloudflare effects remain separate.</summary>
public sealed partial class TaskRunOriginalActionAdmissionTests
{
    [Fact]
    public async Task Two_distinct_owners_cannot_dispatch_the_same_action_even_with_identical_tool_intent()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var actionId = Guid.NewGuid();
        var first = h.First.Prepare(h.Attempt, actionId);
        var second = h.Second.Prepare(h.Attempt, actionId);
        h.Owners.HoldBothPreparations = true;
        var one = h.Coordinator.RegisterOriginalToolActionAsync(first, null, "same controlled intent", token);
        var two = h.Coordinator.RegisterOriginalToolActionAsync(second, null, "same controlled intent", token);
        await h.Owners.BothPreparationsEntered.Task.WaitAsync(token);
        h.Owners.ReleasePreparations.TrySetResult();
        Assert.NotNull(await Record.ExceptionAsync(async () => await Task.WhenAll(one, two)));
        Assert.True(one.IsCompletedSuccessfully ^ two.IsCompletedSuccessfully);
        var winner = one.IsCompletedSuccessfully ? first : second;
        var loser = ReferenceEquals(winner, first) ? second : first;
        var winnerOwner = ReferenceEquals(winner, first) ? h.First : h.Second;
        var loserOwner = ReferenceEquals(winner, first) ? h.Second : h.First;
        var acknowledged = one.IsCompletedSuccessfully ? await one : await two;
        Assert.Same(one.IsCompletedSuccessfully ? one : two, h.Coordinator.RegisterOriginalToolActionAsync(
            winner, null, "same controlled intent", token));
        await winnerOwner.ExecuteAsync(winner, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => loserOwner.ExecuteAsync(loser, token));
        Assert.Throws<InvalidOperationException>(() => h.Coordinator.RequireOriginalActionAdmission(loser, h.Attempt));
        var current = await h.Rows.GetAsync(h.Task.TaskId, token);
        Assert.Equal(h.Task.TaskId, current!.TaskId);
        Assert.Equal(h.Task.ContextId, current.ContextId);
        Assert.Equal(h.Task.ExecutionId, current.ExecutionId);
        Assert.Equal(acknowledged.PersistenceRevision, current.PersistenceRevision);
        var node = Assert.Single(current.Plan);
        Assert.Equal(actionId, node.ActionId);
        Assert.Equal(winner.OriginalToolIntent, node.OriginalToolIntent);
        Assert.Equal(TaskPlanNodeState.Running, node.State);
        Assert.Equal(1, h.First.Effects + h.Second.Effects);
        Assert.Equal(0, loserOwner.Effects);
    }

    [Fact]
    public async Task Commit_then_faulted_original_cas_keeps_exact_siblings_and_refuses_both_replay_and_effect()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var actionId = Guid.NewGuid();
        var first = h.First.Prepare(h.Attempt, actionId);
        var oce = new OperationCanceledException("actual faulted repository OCE");
        var sibling = new IOException("actual repository sibling");
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([oce, sibling]);
        h.Rows.NextWriteOutcome = raw.Task;
        var original = h.Coordinator.RegisterOriginalToolActionAsync(first, null, "unknown write outcome", token);
        var cause = await Record.ExceptionAsync(() => original);
        Assert.NotNull(cause);
        Assert.True(original.IsFaulted);
        Assert.False(original.IsCanceled);
        Assert.Contains(Causes(cause!), error => ReferenceEquals(error, oce));
        Assert.Contains(Causes(cause!), error => ReferenceEquals(error, sibling));
        Assert.Contains(OriginalSources(h.Coordinator, first), source => ReferenceEquals(source, raw.Task));
        Assert.Throws<InvalidOperationException>(() => h.Coordinator.RequireOriginalActionAdmission(first, h.Attempt));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.First.ExecuteAsync(first, token));
        var another = h.Second.Prepare(h.Attempt, actionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.RegisterOriginalToolActionAsync(
            another, null, "unknown write outcome", token));
        var current = await h.Rows.GetAsync(h.Task.TaskId, token);
        Assert.Equal(h.Task.TaskId, current!.TaskId);
        Assert.Equal(h.Task.ExecutionId, current.ExecutionId);
        Assert.Equal(TaskPlanNodeState.Running, Assert.Single(current.Plan).State);
        Assert.Null(current.LastCheckpointActionId);
        Assert.Equal(0, h.First.Effects + h.Second.Effects);
    }

    [Fact]
    public async Task Copied_receipt_or_attempt_cannot_claim_a_successful_original_registration()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var preparation = h.First.Prepare(h.Attempt, Guid.NewGuid());
        await h.Coordinator.RegisterOriginalToolActionAsync(preparation, null, "copy refusal", token);
        var receipt = h.Coordinator.RequireOriginalActionAdmission(preparation, h.Attempt);
        var copy = (TaskRunOriginalActionAdmission)typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(receipt, null)!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.ValidateOriginalActionAdmissionAsync(
            copy, preparation, h.Attempt, token));
        Assert.Throws<InvalidOperationException>(() => h.Coordinator.DemandOriginalActionAdmission(copy, preparation, h.Attempt));
        var copiedAttempt = h.Attempt with { };
        Assert.Throws<InvalidOperationException>(() => h.Coordinator.RequireOriginalActionAdmission(preparation, copiedAttempt));
        await h.Coordinator.ValidateOriginalActionAdmissionAsync(receipt, preparation, h.Attempt, token);
        h.Coordinator.DemandOriginalActionAdmission(receipt, preparation, h.Attempt);
        Assert.Equal(0, h.First.Effects + h.Second.Effects);
    }

    [Fact]
    public async Task Planned_pending_action_acquires_once_and_completed_original_does_not_replay_after_healthy_retirement()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var actionId = Guid.NewGuid();
        var current = (await h.Rows.GetAsync(h.Task.TaskId, token))!;
        await h.Rows.UpsertAsync(current with { PersistenceRevision = current.PersistenceRevision + 1,
            Plan = [new(actionId, null, "planned action", TaskPlanNodeState.Pending,
                TaskActionInterruptionPolicy.ReadOnlyCancellable, current.PlanVersion)] }, token);
        var preparation = h.First.Prepare(h.Attempt, actionId, readOnly: true);
        await h.Coordinator.RegisterOriginalToolActionAsync(preparation, null, "planned action", token);
        var result = await h.First.ExecuteAsync(preparation, token);
        var acknowledged = await h.Coordinator.RecordObservedActionOutcomeAsync(preparation, result, token);
        Assert.Equal(TaskPlanNodeState.Completed, Assert.Single(acknowledged.Plan).State);
        await h.Coordinator.RetireAcknowledgedToolOriginalAsync(preparation, acknowledged);
        await h.Coordinator.RetireAcknowledgedToolOriginalAsync(preparation, acknowledged);
        Assert.Equal(1, h.First.Retirements);
        Assert.Throws<InvalidOperationException>(() => h.Coordinator.RequireOriginalActionAdmission(preparation, h.Attempt));
        var replay = h.Second.Prepare(h.Attempt, actionId, readOnly: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.RegisterOriginalToolActionAsync(
            replay, null, "planned action", token));
        var saved = await h.Rows.GetAsync(h.Task.TaskId, token);
        Assert.Equal(TaskPlanNodeState.Completed, Assert.Single(saved!.Plan).State);
        Assert.Equal(acknowledged.PersistenceRevision, saved.PersistenceRevision);
        Assert.Equal(h.Task.TaskId, saved.TaskId);
        Assert.Equal(h.Task.ExecutionId, saved.ExecutionId);
        Assert.Equal(1, h.First.Effects + h.Second.Effects);
        Assert.NotEmpty(OriginalSources(h.Coordinator, preparation));
    }

    [Fact]
    public async Task Failed_fresh_validation_cannot_reuse_an_earlier_successful_claim()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var preparation = h.First.Prepare(h.Attempt, Guid.NewGuid());
        await h.Coordinator.RegisterOriginalToolActionAsync(preparation, null, "authority change", token);
        var receipt = h.Coordinator.RequireOriginalActionAdmission(preparation, h.Attempt);
        await h.Coordinator.ValidateOriginalActionAdmissionAsync(receipt, preparation, h.Attempt, token);
        h.Coordinator.DemandOriginalActionAdmission(receipt, preparation, h.Attempt);
        h.Authority.CurrentActor = false;
        var actualValidation = h.Coordinator.ValidateOriginalActionAdmissionAsync(receipt, preparation, h.Attempt, token);
        var refused = await Record.ExceptionAsync(() => actualValidation);
        Assert.NotNull(refused);
        Assert.True(actualValidation.IsFaulted);
        Assert.False(actualValidation.IsCanceled);
        Assert.Contains(Causes(refused!), error => ReferenceEquals(error, h.Authority.RevokedCause));
        Assert.Throws<InvalidOperationException>(() => h.Coordinator.DemandOriginalActionAdmission(receipt, preparation, h.Attempt));
        Assert.Equal(0, h.First.Effects + h.Second.Effects);
    }

    [Fact]
    public async Task Actual_steer_during_held_lease_validation_refuses_the_superseded_original_before_effect()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var preparation = h.First.Prepare(h.Attempt, Guid.NewGuid(), readOnly: true);
        await h.Coordinator.RegisterOriginalToolActionAsync(preparation, null, "held currentness", token);
        var receipt = h.Coordinator.RequireOriginalActionAdmission(preparation, h.Attempt);
        var lease = (Lease)h.Attempt.Lease;
        lease.HoldRevalidation = true;
        var actualValidation = h.Coordinator.ValidateOriginalActionAdmissionAsync(receipt, preparation, h.Attempt, token);
        try
        {
            await lease.RevalidationEntered.Task.WaitAsync(token);
            var steer = await h.Coordinator.SubmitFollowUpAsync(h.Task.TaskId, "use the replacement read",
                TaskFollowUpMode.Steer, [preparation.ActionId], [], token);
            Assert.Equal(TaskPlanNodeState.Superseded, Assert.Single(steer.Snapshot.Plan, node => node.ActionId == preparation.ActionId).State);
            lease.ReleaseRevalidation.TrySetResult();
            Assert.NotNull(await Record.ExceptionAsync(() => actualValidation));
            Assert.True(actualValidation.IsFaulted);
            Assert.Throws<InvalidOperationException>(() => h.Coordinator.DemandOriginalActionAdmission(receipt, preparation, h.Attempt));
            Assert.Equal(0, h.First.Effects + h.Second.Effects);
            Assert.Equal(h.Task.TaskId, steer.Snapshot.TaskId);
            Assert.Equal(h.Task.ExecutionId, steer.Snapshot.ExecutionId);
        }
        finally
        {
            lease.ReleaseRevalidation.TrySetResult();
            _ = await Record.ExceptionAsync(() => actualValidation);
        }
    }

    [Fact]
    public async Task Acknowledged_steer_after_validation_invalidates_pure_demand_without_any_effect_or_new_policy_read()
    {
        var token = TestContext.Current.CancellationToken;
        var h = await Harness.CreateAsync(token);
        var preparation = h.First.Prepare(h.Attempt, Guid.NewGuid(), readOnly: true);
        await h.Coordinator.RegisterOriginalToolActionAsync(preparation, null, "late acknowledged steer", token);
        var receipt = h.Coordinator.RequireOriginalActionAdmission(preparation, h.Attempt);
        await h.Coordinator.ValidateOriginalActionAdmissionAsync(receipt, preparation, h.Attempt, token);
        h.Coordinator.DemandOriginalActionAdmission(receipt, preparation, h.Attempt);
        await h.Coordinator.SubmitFollowUpAsync(h.Task.TaskId, "replace the current read", TaskFollowUpMode.Steer,
            [preparation.ActionId], [], token);
        var reads = h.Rows.Reads;
        var policyReads = h.Authority.CommandReads;
        Assert.Throws<InvalidOperationException>(() => h.Coordinator.DemandOriginalActionAdmission(receipt, preparation, h.Attempt));
        Assert.Equal(reads, h.Rows.Reads);
        Assert.Equal(policyReads, h.Authority.CommandReads);
        Assert.Equal(0, h.First.Effects + h.Second.Effects);
    }

    private static IEnumerable<Exception> Causes(Exception cause) => cause is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(Causes) : [cause];
    private static IReadOnlyList<Task> OriginalSources(TaskExecutionCoordinator actualCoordinator, ITaskRunToolActionPreparation preparation)
    {
        var table = typeof(TaskExecutionCoordinator).GetField("_originalActionPreparations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actualCoordinator)!;
        object?[] arguments = [preparation, null];
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, arguments)!);
        return ((IEnumerable<Task>)arguments[1]!.GetType().GetField("Sources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(arguments[1])!).ToArray();
    }

    private sealed class Harness
    {
        public required TaskExecutionCoordinator Coordinator { get; init; }
        public required Rows Rows { get; init; }
        public required Authority Authority { get; init; }
        public required Owners Owners { get; init; }
        public required Owner First { get; init; }
        public required Owner Second { get; init; }
        public required TaskExecutionSnapshot Task { get; init; }
        public required TaskRunAttemptAdmission Attempt { get; init; }
        public static async Task<Harness> CreateAsync(CancellationToken token)
        {
            var rows = new Rows();
            var authority = new Authority();
            var owners = new Owners();
            var coordinator = new TaskExecutionCoordinator(rows, new Sink(), admissionAuthority: authority, toolActionOwner: owners);
            owners.Coordinator = coordinator;
            var first = new Owner(owners);
            var second = new Owner(owners);
            var task = await coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "actual coordinator control",
                TaskExecutionDurability.PersistedPlan, [], token);
            var route = new TaskRunRouteCandidate("controlled route", 1, "local", "controlled model", null, false, []);
            var attempt = await coordinator.StartAttemptAsync(task.TaskId, task.ExecutionId, route, token);
            task = await coordinator.MarkAttemptRunningAsync(task.TaskId, task.ExecutionId, attempt.AttemptId, token);
            return new() { Rows = rows, Authority = authority, Owners = owners, First = first, Second = second,
                Coordinator = coordinator, Task = task, Attempt = attempt };
        }
    }
    private sealed class Preparation(Owner owner, TaskRunAttemptAdmission attempt, Guid action, bool readOnly) : ITaskRunToolActionPreparation
    {
        internal readonly Owner Owner = owner;
        internal int Started;
        internal TaskRunToolActionResult? Result;
        public TaskRunAttemptAdmission OriginalAttempt => attempt;
        public Guid ActionId => action;
        public TaskActionInterruptionPolicy InterruptionPolicy => readOnly ? TaskActionInterruptionPolicy.ReadOnlyCancellable : TaskActionInterruptionPolicy.AtomicCommit;
        public IReadOnlyList<string> RequiredPermissionScopes => [readOnly ? "capability:read-file" : "capability:write-file"];
        public TaskOriginalToolIntent OriginalToolIntent { get; } = new("Workspace", readOnly ? "read_file" : "write_file", "/controlled", "controlled-exact-argument-digest");
    }
    private sealed class Owner(Owners source)
    {
        internal int Effects;
        internal int Retirements;
        internal Preparation Prepare(TaskRunAttemptAdmission attempt, Guid action, bool readOnly = false)
        {
            var preparation = new Preparation(this, attempt, action, readOnly);
            lock (source.Gate) source.Preparations.Add(preparation);
            return preparation;
        }
        internal async Task<TaskRunToolActionResult> ExecuteAsync(Preparation preparation, CancellationToken token)
        {
            var receipt = source.Coordinator.RequireOriginalActionAdmission(preparation, preparation.OriginalAttempt);
            await source.Coordinator.ValidateOriginalActionAdmissionAsync(receipt, preparation, preparation.OriginalAttempt, token);
            source.Coordinator.DemandOriginalActionAdmission(receipt, preparation, preparation.OriginalAttempt);
            if (Interlocked.CompareExchange(ref preparation.Started, 1, 0) != 0)
                throw new InvalidOperationException("The actual controlled body cannot replay.");
            Interlocked.Increment(ref Effects);
            var raw = new WorkspaceToolResult(new(Guid.NewGuid(), "controlled owner effect", "synthetic", true,
                TimeSpan.Zero, DateTimeOffset.UtcNow), "controlled result");
            return preparation.Result = new(raw, null, preparation.InterruptionPolicy == TaskActionInterruptionPolicy.ReadOnlyCancellable, false);
        }
    }
    private sealed class Owners : ITaskRunToolActionOwner
    {
        internal readonly object Gate = new();
        internal readonly HashSet<Preparation> Preparations = [];
        internal Task? OriginalRetirementClose = null;
        internal TaskExecutionCoordinator Coordinator = null!;
        internal bool HoldBothPreparations;
        internal int Entered;
        internal readonly TaskCompletionSource BothPreparationsEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleasePreparations = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool SupportsCanonicalInvocation(ToolRuntimeKind runtime, string toolName) => runtime == ToolRuntimeKind.Workspace;
        public Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(TaskRunAttemptAdmission attempt, TaskExecutionSnapshot snapshot,
            Guid action, OllamaToolCall call, ToolRuntimeKind runtime, PermissionMode permission, string? root, CancellationToken token)
            => throw new NotSupportedException("Each controlled owner issues its own preparation.");
        public Task<TaskRunToolActionResult> ExecuteOriginalAsync(ITaskRunToolActionPreparation preparation,
            Func<CancellationToken, Task<WorkspaceToolResult>> originalBody, CancellationToken token)
            => throw new NotSupportedException("The controlled owner explicitly executes its registered body.");
        public async ValueTask ValidateOriginalPreparationAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot current, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (Gate) if (preparation is not Preparation exact || !Preparations.Contains(exact))
                throw new InvalidOperationException("No actual controlled preparation exists.");
            if (HoldBothPreparations)
            {
                if (Interlocked.Increment(ref Entered) == 2) BothPreparationsEntered.TrySetResult();
                await ReleasePreparations.Task.WaitAsync(token);
            }
        }
        public ValueTask ValidateOriginalResultAsync(ITaskRunToolActionPreparation preparation, TaskRunToolActionResult result, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (preparation is not Preparation exact || !ReferenceEquals(exact.Result, result))
                throw new InvalidOperationException("No exact actual result exists.");
            return ValueTask.CompletedTask;
        }
        public ValueTask RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot acknowledged, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (preparation is not Preparation exact || exact.Result is null || exact.Started != 1)
                throw new InvalidOperationException("The actual original body has not completed.");
            Interlocked.Increment(ref exact.Owner.Retirements);
            return OriginalRetirementClose is { } raw ? new ValueTask(raw) : ValueTask.CompletedTask;
        }
    }
    private sealed class Authority : ITaskRunCommandAuthority
    {
        internal bool CurrentActor = true;
        internal string? AcceptedReceipt = null;
        internal int CommandReads;
        internal readonly UnauthorizedAccessException RevokedCause = new("Actual controlled actor revoked");
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot proposed, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return System.Threading.Tasks.Task.FromResult(new TaskExecutionOwnerBinding(proposed.TaskId, proposed.ContextId,
                proposed.ExecutionId, "controlled actor", "controlled profile", null, null, "controlled current auth", "controlled issuer"));
        }
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot snapshot, Guid attemptId,
            TaskRunRouteCandidate candidate, Guid? previous, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return System.Threading.Tasks.Task.FromResult<ITaskRunAdmissionLease>(new Lease(snapshot.OwnerBinding!, attemptId, candidate));
        }
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot snapshot, Guid attemptId, Guid actionId, string receipt, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (AcceptedReceipt is not null && AcceptedReceipt == receipt)
                return System.Threading.Tasks.Task.CompletedTask;
            throw new NotSupportedException("These controls never infer accepted external mutation.");
        }
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot snapshot, string command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref CommandReads);
            return CurrentActor ? System.Threading.Tasks.Task.CompletedTask
                : System.Threading.Tasks.Task.FromException(RevokedCause);
        }
    }
    private sealed class Lease(TaskExecutionOwnerBinding owner, Guid id, TaskRunRouteCandidate candidate) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => owner;
        public Guid AttemptId => id;
        public TaskRunRouteCandidate Candidate => candidate;
        public string ReceiptReference => "controlled source lease";
        internal bool HoldRevalidation;
        internal readonly TaskCompletionSource RevalidationEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseRevalidation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask RevalidateAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!HoldRevalidation) return ValueTask.CompletedTask;
            RevalidationEntered.TrySetResult();
            return new(ReleaseRevalidation.Task.WaitAsync(token));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Rows : ITaskExecutionRepository
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, string> _rows = [];
        internal Task? NextWriteOutcome;
        internal int Reads;
        public Task UpsertAsync(TaskExecutionSnapshot proposed, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var previous = _rows.TryGetValue(proposed.TaskId, out var raw) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(raw) : null;
                if (previous is null ? proposed.PersistenceRevision != 1 : previous.PersistenceRevision + 1 != proposed.PersistenceRevision
                    || previous.ContextId != proposed.ContextId || previous.ExecutionId != proposed.ExecutionId)
                    throw new TaskExecutionRevisionConflictException(proposed.TaskId, proposed.PersistenceRevision - 1, proposed.PersistenceRevision);
                _rows[proposed.TaskId] = JsonSerializer.Serialize(proposed);
                var actual = NextWriteOutcome;
                NextWriteOutcome = null;
                return actual ?? System.Threading.Tasks.Task.CompletedTask;
            }
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Reads);
            lock (_gate) return System.Threading.Tasks.Task.FromResult(_rows.TryGetValue(id, out var raw) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(raw) : null);
        }
        public async Task<TaskExecutionSnapshot?> GetByContextAsync(Guid context, CancellationToken token)
            => (await GetResumableAsync(token)).FirstOrDefault(row => row.ContextId == context);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_gate) return System.Threading.Tasks.Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_rows.Values.Select(raw => JsonSerializer.Deserialize<TaskExecutionSnapshot>(raw)!).ToArray());
        }
    }
    private sealed class Sink : IExecutionEventSink { public bool TryPublish(ExecutionEvent executionEvent) => true; }
}
