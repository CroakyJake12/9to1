using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual coordinator and original-frame owner controls. Repository and permission authority are synthetic; SQLite, provider and UI acceptance remain separate.</summary>
public sealed class TaskRunCoordinatorRecoveryTests
{
    [Fact]
    public async Task Failure_acknowledgment_preserves_same_run_accepted_checkpoint_and_disposes_original_once()
    {
        var h = await Harness.CreateAsync();
        var action = await h.AcceptSyntheticOwnerActionAsync();
        var cause = new HttpRequestException("Synthetic raw quota", null, System.Net.HttpStatusCode.TooManyRequests);
        var frame = h.Runtime.StartOriginalFrameAsync<string>(h.Admission, _ => Task.FromException<string>(cause), default);
        Assert.Same(cause, await Assert.ThrowsAsync<HttpRequestException>(() => frame));
        var observation = h.Runtime.CreateProviderFailureObservation(h.Admission, frame, cause);
        await h.Coordinator.RecordAttemptFailureAsync(h.Task.TaskId, h.Task.ExecutionId, h.Admission.AttemptId,
            new("QUOTA", "Quota", "Synthetic raw quota"), default, observation);
        var resumed = await h.Coordinator.ResumeAttemptAsync(h.Task.TaskId, h.Task.ExecutionId, h.Admission.AttemptId, Route("local"), default);
        Assert.Equal(h.Task.TaskId, resumed.Snapshot.TaskId);
        Assert.Equal(h.Task.ContextId, resumed.Snapshot.ContextId);
        Assert.Equal(h.Task.ExecutionId, resumed.Snapshot.ExecutionId);
        Assert.Equal(action, resumed.Snapshot.LastCheckpointActionId);
        Assert.NotNull(resumed.Snapshot.Plan.Single().Acceptance);
        Assert.Equal(1, ((SyntheticLease)h.Admission.Lease).DisposeCount);
        Assert.NotEqual(h.Admission.AttemptId, resumed.AttemptId);
    }

    [Fact]
    public async Task Already_failed_early_return_does_not_acknowledge_another_original_body_sibling()
    {
        var h = await Harness.CreateAsync();
        var first = new HttpRequestException("Synthetic primary", null, System.Net.HttpStatusCode.TooManyRequests);
        var sibling = new HttpRequestException("Synthetic sibling", null, System.Net.HttpStatusCode.ServiceUnavailable);
        var firstFrame = h.Runtime.StartOriginalFrameAsync<string>(h.Admission, _ => Task.FromException<string>(first), default);
        var siblingFrame = h.Runtime.StartOriginalFrameAsync<string>(h.Admission, _ => Task.FromException<string>(sibling), default);
        Assert.Same(first, await Assert.ThrowsAsync<HttpRequestException>(() => firstFrame));
        Assert.Same(sibling, await Assert.ThrowsAsync<HttpRequestException>(() => siblingFrame));
        var firstObservation = h.Runtime.CreateProviderFailureObservation(h.Admission, firstFrame, first);
        var siblingObservation = h.Runtime.CreateProviderFailureObservation(h.Admission, siblingFrame, sibling);
        var failed = await h.Coordinator.RecordAttemptFailureAsync(h.Task.TaskId, h.Task.ExecutionId, h.Admission.AttemptId,
            new("PRIMARY", "Primary", "Synthetic primary"), default, firstObservation);
        var unchanged = await h.Coordinator.RecordAttemptFailureAsync(h.Task.TaskId, h.Task.ExecutionId, h.Admission.AttemptId,
            new("SIBLING", "Sibling", "Synthetic sibling"), default, siblingObservation);
        Assert.Equal(failed.PersistenceRevision, unchanged.PersistenceRevision);
        var blocked = await Record.ExceptionAsync(() => h.Coordinator.ResumeAttemptAsync(h.Task.TaskId, h.Task.ExecutionId,
            h.Admission.AttemptId, Route("local"), default));
        Assert.NotNull(blocked);
        Assert.Contains(Causes(blocked!), error => ReferenceEquals(error, sibling));
        Assert.Single(h.Authority.Leases);
    }

    [Fact]
    public async Task Lease_cleanup_fault_remains_blocking_after_known_provider_failure_acknowledgment()
    {
        var h = await Harness.CreateAsync();
        var cleanup = new IOException("Synthetic exact original lease cleanup");
        ((SyntheticLease)h.Admission.Lease).CleanupFailure = cleanup;
        var body = new HttpRequestException("Synthetic terminal body", null, System.Net.HttpStatusCode.TooManyRequests);
        var frame = h.Runtime.StartOriginalFrameAsync<string>(h.Admission, _ => Task.FromException<string>(body), default);
        Assert.Same(body, await Assert.ThrowsAsync<HttpRequestException>(() => frame));
        var observation = h.Runtime.CreateProviderFailureObservation(h.Admission, frame, body);
        await h.Coordinator.RecordAttemptFailureAsync(h.Task.TaskId, h.Task.ExecutionId, h.Admission.AttemptId,
            new("QUOTA", "Quota", "Synthetic terminal body"), default, observation);
        var blocked = await Record.ExceptionAsync(() => h.Coordinator.ResumeAttemptAsync(h.Task.TaskId, h.Task.ExecutionId,
            h.Admission.AttemptId, Route("local"), default));
        Assert.NotNull(blocked);
        Assert.Contains(Causes(blocked!), error => ReferenceEquals(error, cleanup));
        Assert.Single(h.Authority.Leases);
        Assert.Equal(1, ((SyntheticLease)h.Admission.Lease).DisposeCount);
    }

    [Fact]
    public async Task Unresolved_mutation_refuses_completion_and_unknown_result_refuses_fallback()
    {
        var h = await Harness.CreateAsync();
        var action = Guid.NewGuid();
        await h.Coordinator.RegisterActionAsync(h.Task.TaskId, action, null, "Synthetic unknown committed effect",
            TaskActionInterruptionPolicy.AtomicCommit, null, [], default, h.Admission.AttemptId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.CompleteAttemptAsync(
            h.Task.TaskId, h.Task.ExecutionId, h.Admission.AttemptId, default));
        Assert.Equal(0, ((SyntheticLease)h.Admission.Lease).DisposeCount);
        await h.Coordinator.RecordAttemptFailureAsync(h.Task.TaskId, h.Task.ExecutionId, h.Admission.AttemptId,
            new("OWNER_UNKNOWN", "Unknown", "No owner acceptance observed"), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.ResumeAttemptAsync(
            h.Task.TaskId, h.Task.ExecutionId, h.Admission.AttemptId, Route("local"), default));
        var saved = await h.Repository.GetAsync(h.Task.TaskId, default);
        Assert.Equal(TaskPlanNodeState.Running, saved!.Plan.Single().State);
        Assert.Null(saved.LastCheckpointActionId);
        Assert.Single(h.Authority.Leases);
    }

    [Fact]
    public async Task Zero_frame_completion_requires_actual_registration_and_refuses_legacy_boolean()
    {
        var h = await Harness.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.ReachCheckpointAsync(h.Task.TaskId, null, true, default));
        var done = await h.Coordinator.CompleteAttemptAsync(h.Task.TaskId, h.Task.ExecutionId, h.Admission.AttemptId, default);
        Assert.Equal(TaskExecutionLifecycle.Completed, done.State);
        Assert.Equal(TaskRunAttemptState.Completed, done.Attempts.Single().State);
        Assert.Equal(1, ((SyntheticLease)h.Admission.Lease).DisposeCount);
        Assert.Null(await h.Coordinator.TryGetIssuedAttemptAsync(h.Task.TaskId, h.Task.ExecutionId, h.Admission.AttemptId, default));
    }

    [Fact]
    public async Task Same_context_old_checkpoint_and_cross_run_issuance_cannot_replace_current_checkpoint()
    {
        var h = await Harness.CreateAsync();
        var old = new CheckpointInfo(Guid.NewGuid(), h.Task.ContextId, null, "synthetic-workspace", "old",
            CheckpointMode.BeforeFileChanges, 0, DateTimeOffset.UtcNow.AddDays(-1));
        h.Checkpoints.Records.Add(old.Id, old);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.RecordCheckpointAsync(h.Task.TaskId,
            h.Task.ExecutionId, old.Id, default));
        h.Checkpoints.Originals.Add(Guid.NewGuid(), old.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.RecordCheckpointAsync(h.Task.TaskId,
            h.Task.ExecutionId, old.Id, default));
        Assert.Null((await h.Repository.GetAsync(h.Task.TaskId, default))!.CheckpointId);
        var current = old with { Id = Guid.NewGuid(), Label = "current" };
        h.Checkpoints.Records.Add(current.Id, current);
        h.Checkpoints.Originals.Add(h.Task.ExecutionId, current.Id);
        var acknowledged = await h.Coordinator.RecordCheckpointAsync(h.Task.TaskId, h.Task.ExecutionId, current.Id, default);
        Assert.Equal(current.Id, acknowledged.CheckpointId);
    }

    [Fact]
    public async Task Runtime_return_or_legacy_completion_is_not_a_mutation_acceptance_projection()
    {
        var repository = new SyntheticRepository();
        var coordinator = new TaskExecutionCoordinator(repository, new NullSink());
        var task = await coordinator.BeginAsync(Guid.NewGuid(), Guid.NewGuid(), "legacy metadata", TaskExecutionDurability.PersistedPlan, [], default);
        var action = Guid.NewGuid();
        await coordinator.RegisterActionAsync(task.TaskId, action, null, "runtime return", TaskActionInterruptionPolicy.AtomicCommit, null, [], default);
        task = await coordinator.CompleteActionAsync(task.TaskId, action, true, default);
        var projection = TaskExecutionProjection.From(task);
        Assert.Empty(projection.AcceptedActions);
        Assert.Null(projection.LastAcceptedActionId);
    }

    private static IEnumerable<Exception> Causes(Exception cause) => cause is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(Causes) : [cause];
    private static TaskRunRouteCandidate Route(string provider) => new("synthetic-route", 1, provider, "synthetic-model", null, false, []);

    private sealed class Harness
    {
        public required TaskExecutionCoordinator Coordinator { get; init; }
        public required TaskRunOriginalFrameOwner Runtime { get; init; }
        public required SyntheticRepository Repository { get; init; }
        public required SyntheticAuthority Authority { get; init; }
        public required SyntheticCheckpoints Checkpoints { get; init; }
        public required TaskExecutionSnapshot Task { get; init; }
        public required TaskRunAttemptAdmission Admission { get; init; }
        public static async Task<Harness> CreateAsync()
        {
            var repository = new SyntheticRepository();
            var authority = new SyntheticAuthority();
            var checkpoints = new SyntheticCheckpoints();
            TaskExecutionCoordinator? coordinator = null;
            var runtime = new TaskRunOriginalFrameOwner((task, run, attempt, token) => coordinator!.GetIssuedAttemptAsync(task, run, attempt, token));
            coordinator = new(repository, new NullSink(), admissionAuthority: authority, runtimeSettlement: runtime,
                checkpointRepository: checkpoints, checkpointObservationSource: checkpoints);
            var task = await coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "synthetic control",
                TaskExecutionDurability.RecoverableCheckpoint, [], default);
            var admission = await coordinator.StartAttemptAsync(task.TaskId, task.ExecutionId, Route("cloud"), default);
            await runtime.RegisterOriginalAttemptAsync(admission, default);
            task = await coordinator.MarkAttemptRunningAsync(task.TaskId, task.ExecutionId, admission.AttemptId, default);
            return new() { Coordinator = coordinator, Runtime = runtime, Repository = repository, Authority = authority,
                Checkpoints = checkpoints, Task = task, Admission = admission };
        }
        public async Task<Guid> AcceptSyntheticOwnerActionAsync()
        {
            var action = Guid.NewGuid();
            await Coordinator.RegisterActionAsync(Task.TaskId, action, null, "synthetic accepted owner action",
                TaskActionInterruptionPolicy.AtomicCommit, null, [], default, Admission.AttemptId);
            await Coordinator.AcceptActionAsync(Task.TaskId, Task.ExecutionId, Admission.AttemptId, action, "synthetic-accepted", default);
            return action;
        }
    }

    private sealed class SyntheticAuthority : ITaskRunCommandAuthority
    {
        public List<SyntheticLease> Leases { get; } = [];
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot snapshot, string command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.OwnerBinding is not { ActorId: "synthetic-actor", ProfileId: "synthetic-profile", AuthenticationRevision: "synthetic-auth" })
                throw new UnauthorizedAccessException("Synthetic current owner mismatch");
            return System.Threading.Tasks.Task.CompletedTask;
        }
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot proposed, CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult(new TaskExecutionOwnerBinding(proposed.TaskId, proposed.ContextId, proposed.ExecutionId,
                "synthetic-actor", "synthetic-profile", null, null, "synthetic-auth", "synthetic-authority"));
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot snapshot, Guid attemptId,
            TaskRunRouteCandidate candidate, Guid? expectedPreviousAttemptId, CancellationToken cancellationToken)
        {
            var lease = new SyntheticLease(snapshot.OwnerBinding!, attemptId, candidate);
            Leases.Add(lease);
            return System.Threading.Tasks.Task.FromResult<ITaskRunAdmissionLease>(lease);
        }
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot snapshot, Guid attemptId, Guid actionId,
            string ownerReceiptReference, CancellationToken cancellationToken)
        {
            if (ownerReceiptReference != "synthetic-accepted") throw new UnauthorizedAccessException("Synthetic receipt mismatch");
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private sealed class SyntheticLease(TaskExecutionOwnerBinding owner, Guid attempt, TaskRunRouteCandidate candidate) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => owner;
        public Guid AttemptId => attempt;
        public TaskRunRouteCandidate Candidate => candidate;
        public string ReceiptReference => "synthetic-original-lease";
        public int DisposeCount { get; private set; }
        public Exception? CleanupFailure { get; set; }
        public ValueTask RevalidateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DisposeCount != 0) throw new ObjectDisposedException(nameof(SyntheticLease));
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return CleanupFailure is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask;
        }
    }

    private sealed class SyntheticRepository : ITaskExecutionRepository
    {
        private readonly Dictionary<Guid, string> _payloads = [];
        public Task UpsertAsync(TaskExecutionSnapshot proposed, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = _payloads.TryGetValue(proposed.TaskId, out var payload) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(payload) : null;
            if (proposed.PersistenceRevision < 1 || existing is null && proposed.PersistenceRevision != 1
                || existing is not null && (existing.PersistenceRevision != proposed.PersistenceRevision - 1
                    || existing.ContextId != proposed.ContextId || existing.ExecutionId != proposed.ExecutionId))
                throw new TaskExecutionRevisionConflictException(proposed.TaskId, proposed.PersistenceRevision - 1, proposed.PersistenceRevision);
            _payloads[proposed.TaskId] = JsonSerializer.Serialize(proposed);
            return System.Threading.Tasks.Task.CompletedTask;
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid taskId, CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult(_payloads.TryGetValue(taskId, out var payload) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(payload) : null);
        public async Task<TaskExecutionSnapshot?> GetByContextAsync(Guid contextId, CancellationToken cancellationToken)
            => (await GetResumableAsync(cancellationToken)).FirstOrDefault(task => task.ContextId == contextId);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_payloads.Values.Select(payload => JsonSerializer.Deserialize<TaskExecutionSnapshot>(payload)!).ToArray());
    }

    private sealed class SyntheticCheckpoints : ICheckpointRepository, ICheckpointExecutionObservationSource
    {
        public Dictionary<Guid, CheckpointInfo> Records { get; } = [];
        public Dictionary<Guid, Guid> Originals { get; } = [];
        public Task<CheckpointInfo?> GetOriginalCheckpointAsync(Guid executionId, Guid checkpointId, CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult(Originals.TryGetValue(executionId, out var original) && original == checkpointId ? Records[checkpointId] : null);
        public Task<CheckpointInfo?> GetAsync(Guid checkpointId, CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult(Records.GetValueOrDefault(checkpointId));
        public Task SaveAsync(CheckpointInfo checkpoint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CheckpointInfo?> GetLatestAsync(Guid? conversationId, string workspaceRoot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<long> GetLatestVersionSequenceAsync(string workspaceRoot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkspaceRestoreEntry>> GetVersionsSinceAsync(string workspaceRoot, long sequence, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspaceRestoreEntry?> GetLatestVersionAsync(string workspaceRoot, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class NullSink : IExecutionEventSink { public bool TryPublish(ExecutionEvent executionEvent) => true; }
}
