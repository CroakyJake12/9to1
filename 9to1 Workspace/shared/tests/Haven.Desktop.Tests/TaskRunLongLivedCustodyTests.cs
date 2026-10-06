using System.Reflection;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual coordinator and frame-owner originals; controlled CAS repository/actor only. No real provider or installed authority proof.</summary>
public sealed class TaskRunLongLivedCustodyTests
{
    [Fact]
    public async Task More_than_128_healthy_completed_attempts_retire_actual_originals_and_dispose_each_lease_once()
    {
        var h = Harness.Create();
        for (var index = 0; index < 136; index++)
        {
            var task = await h.Coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "synthetic healthy run",
                TaskExecutionDurability.PersistedPlan, [], TestContext.Current.CancellationToken);
            var original = await h.Coordinator.StartAttemptAsync(task.TaskId, task.ExecutionId, Route(), TestContext.Current.CancellationToken);
            await h.Runtime.RegisterOriginalAttemptAsync(original, TestContext.Current.CancellationToken);
            var frame = h.Runtime.StartOriginalFrameAsync(original, _ => Task.FromResult(index), TestContext.Current.CancellationToken);
            Assert.Equal(index, await frame);
            var done = await h.Coordinator.CompleteAttemptAsync(task.TaskId, task.ExecutionId, original.AttemptId, TestContext.Current.CancellationToken);
            Assert.Equal(task.TaskId, done.TaskId);
            Assert.Equal(task.ExecutionId, done.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Completed, done.State);
            Assert.Equal(1, ((Lease)original.Lease).Disposes);
            await Assert.ThrowsAsync<InvalidOperationException>(() => h.Runtime.AwaitSettlementAsync(task.TaskId,
                task.ExecutionId, original.AttemptId, TestContext.Current.CancellationToken)); // Missing retired registry is refusal, never a fake completed task.
        }
        Assert.Equal(136, h.Runtime.Retirements.Count);
        Assert.Equal(136, h.Authority.Leases.Count);
        Assert.Empty(h.Coordinator.ObservationFailures);
        await h.Runtime.CloseAndDrainAsync();
        Assert.All(h.Authority.Leases, lease => Assert.Equal(1, lease.Disposes));
    }

    [Fact]
    public async Task Terminal_CAS_loss_keeps_exact_live_settlement_and_explicit_retry_preserves_concurrent_queue()
    {
        var h = await Harness.StartAsync();
        var original = h.Current!;
        var action = await h.AcceptActionAsync();
        h.Repository.BeforeNextTerminal = async () =>
        {
            await h.Coordinator.SubmitFollowUpAsync(original.Snapshot.TaskId, "Actual concurrent queued followup",
                TaskFollowUpMode.Queue, null, [], default);
        };
        await Assert.ThrowsAsync<TaskExecutionRevisionConflictException>(() => h.Coordinator.CompleteAttemptAsync(
            original.Snapshot.TaskId, original.Snapshot.ExecutionId, original.AttemptId, TestContext.Current.CancellationToken));
        Assert.Equal(1, ((Lease)original.Lease).Disposes);
        Assert.Empty(h.Runtime.Retirements);
        var saved = await h.Coordinator.GetAsync(original.Snapshot.TaskId, TestContext.Current.CancellationToken);
        Assert.Equal(action, saved!.LastCheckpointActionId);
        Assert.Single(saved.Queue);
        Assert.NotEqual(TaskExecutionLifecycle.Completed, saved.State);
        var done = await h.Coordinator.CompleteAttemptAsync(saved.TaskId, saved.ExecutionId, original.AttemptId, TestContext.Current.CancellationToken);
        Assert.Equal(original.Snapshot.TaskId, done.TaskId);
        Assert.Equal(original.Snapshot.ExecutionId, done.ExecutionId);
        Assert.Equal(action, done.LastCheckpointActionId);
        Assert.NotNull(done.Plan.Single().Acceptance);
        Assert.Single(done.Queue);
        Assert.Equal(TaskExecutionLifecycle.Completed, done.State);
        Assert.Single(h.Runtime.Retirements);
        Assert.Equal(1, ((Lease)original.Lease).Disposes);
        Assert.True(h.Authority.CommandChecks >= 3);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Changed_checkpoint_after_closed_original_CAS_loss_cannot_be_projected_as_old_completion()
    {
        var h = await Harness.StartAsync();
        var original = h.Current!;
        h.Repository.BeforeNextTerminal = async () =>
        {
            var checkpoint = new CheckpointInfo(Guid.NewGuid(), original.Snapshot.ContextId, null, "synthetic-workspace",
                "concurrent actual scope", CheckpointMode.BeforeFileChanges, 1, DateTimeOffset.UtcNow);
            h.Checkpoints.Current = checkpoint;
            h.Checkpoints.Execution = original.Snapshot.ExecutionId;
            await h.Coordinator.RecordCheckpointAsync(original.Snapshot.TaskId, original.Snapshot.ExecutionId, checkpoint.Id, default);
        };
        await Assert.ThrowsAsync<TaskExecutionRevisionConflictException>(() => h.Coordinator.CompleteAttemptAsync(
            original.Snapshot.TaskId, original.Snapshot.ExecutionId, original.AttemptId, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.CompleteAttemptAsync(
            original.Snapshot.TaskId, original.Snapshot.ExecutionId, original.AttemptId, TestContext.Current.CancellationToken));
        var saved = await h.Coordinator.GetAsync(original.Snapshot.TaskId, TestContext.Current.CancellationToken);
        Assert.Equal(h.Checkpoints.Current!.Id, saved!.CheckpointId);
        Assert.NotEqual(TaskExecutionLifecycle.Completed, saved.State);
        Assert.Empty(h.Runtime.Retirements);
        Assert.Equal(1, ((Lease)original.Lease).Disposes);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Retry_requires_fresh_actor_and_failed_settlement_never_becomes_completion_witness()
    {
        var h = await Harness.StartAsync();
        var original = h.Current!;
        var cleanup = new IOException("Exact original lease cleanup failure");
        ((Lease)original.Lease).CleanupFailure = cleanup;
        var first = await Record.ExceptionAsync(() => h.Coordinator.CompleteAttemptAsync(original.Snapshot.TaskId,
            original.Snapshot.ExecutionId, original.AttemptId, TestContext.Current.CancellationToken));
        Assert.Contains(Causes(first!), error => ReferenceEquals(error, cleanup));
        h.Authority.AllowCurrentActor = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => h.Coordinator.CompleteAttemptAsync(original.Snapshot.TaskId,
            original.Snapshot.ExecutionId, original.AttemptId, TestContext.Current.CancellationToken));
        h.Authority.AllowCurrentActor = true;
        var retry = await Record.ExceptionAsync(() => h.Coordinator.CompleteAttemptAsync(original.Snapshot.TaskId,
            original.Snapshot.ExecutionId, original.AttemptId, TestContext.Current.CancellationToken));
        Assert.Contains(Causes(retry!), error => ReferenceEquals(error, cleanup));
        Assert.Empty(h.Runtime.Retirements);
        Assert.Equal(1, ((Lease)original.Lease).Disposes);
        Assert.NotEqual(TaskExecutionLifecycle.Completed, (await h.Coordinator.GetAsync(original.Snapshot.TaskId, TestContext.Current.CancellationToken))!.State);
        var closed = await Record.ExceptionAsync(() => h.Runtime.CloseAndDrainAsync());
        Assert.Contains(Causes(closed!), error => ReferenceEquals(error, cleanup));
    }

    [Fact]
    public async Task Copied_live_retirement_and_late_original_retirement_refuse_without_retiring_another_attempt()
    {
        var h = await Harness.StartAsync();
        var original = h.Current!;
        h.Runtime.ProbeCopiedReceipt = true;
        await h.Coordinator.CompleteAttemptAsync(original.Snapshot.TaskId, original.Snapshot.ExecutionId, original.AttemptId, TestContext.Current.CancellationToken);
        Assert.Single(h.Runtime.Retirements);
        Assert.NotNull(h.Runtime.CopiedReceiptRefusal);
        var receipt = h.Runtime.Retirements.Single();
        var late = await Record.ExceptionAsync(() => h.Runtime.Inner.RetireAcknowledgedOriginalAttemptAsync(receipt, TestContext.Current.CancellationToken).AsTask());
        Assert.NotNull(late);
        var nextTask = await h.Coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "other real admission",
            TaskExecutionDurability.PersistedPlan, [], TestContext.Current.CancellationToken);
        var next = await h.Coordinator.StartAttemptAsync(nextTask.TaskId, nextTask.ExecutionId, Route(), TestContext.Current.CancellationToken);
        await h.Runtime.RegisterOriginalAttemptAsync(next, TestContext.Current.CancellationToken);
        Assert.Equal(0, ((Lease)next.Lease).Disposes);
        Assert.Equal(1, ((Lease)original.Lease).Disposes);
        await h.Coordinator.CompleteAttemptAsync(nextTask.TaskId, nextTask.ExecutionId, next.AttemptId, TestContext.Current.CancellationToken);
        await h.Runtime.CloseAndDrainAsync();
    }

    private static IEnumerable<Exception> Causes(Exception cause) => cause is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(Causes) : [cause];
    private static TaskRunRouteCandidate Route() => new("synthetic-route", 1, "synthetic-provider", "synthetic-model", null, false, []);

    private sealed class Harness
    {
        public required TaskExecutionCoordinator Coordinator { get; init; }
        public required RecordingRuntime Runtime { get; init; }
        public required Repository Repository { get; init; }
        public required Authority Authority { get; init; }
        public required Checkpoints Checkpoints { get; init; }
        public TaskRunAttemptAdmission? Current;
        public static Harness Create()
        {
            var repository = new Repository(); var authority = new Authority(); var checkpoints = new Checkpoints();
            TaskExecutionCoordinator? coordinator = null;
            var runtime = new RecordingRuntime(new TaskRunOriginalFrameOwner((task, run, attempt, token) =>
                coordinator!.GetIssuedAttemptAsync(task, run, attempt, token)));
            coordinator = new(repository, new Sink(), admissionAuthority: authority, runtimeSettlement: runtime,
                checkpointRepository: checkpoints, checkpointObservationSource: checkpoints);
            return new() { Coordinator = coordinator, Runtime = runtime, Repository = repository, Authority = authority, Checkpoints = checkpoints };
        }
        public static async Task<Harness> StartAsync()
        {
            var h = Create();
            var task = await h.Coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "synthetic",
                TaskExecutionDurability.PersistedPlan, [], default);
            h.Current = await h.Coordinator.StartAttemptAsync(task.TaskId, task.ExecutionId, Route(), default);
            await h.Runtime.RegisterOriginalAttemptAsync(h.Current, default);
            return h;
        }
        public async Task<Guid> AcceptActionAsync()
        {
            var original = Current!; var action = Guid.NewGuid();
            await Coordinator.RegisterActionAsync(original.Snapshot.TaskId, action, null, "actual synthetic owner receipt",
                TaskActionInterruptionPolicy.AtomicCommit, null, [], default, original.AttemptId);
            await Coordinator.AcceptActionAsync(original.Snapshot.TaskId, original.Snapshot.ExecutionId, original.AttemptId, action, "synthetic-receipt", default);
            return action;
        }
    }
    private sealed class Repository : ITaskExecutionRepository
    {
        private readonly Dictionary<Guid, string> _rows = [];
        public Func<Task>? BeforeNextTerminal;
        public async Task UpsertAsync(TaskExecutionSnapshot proposed, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (proposed.State == TaskExecutionLifecycle.Completed && BeforeNextTerminal is { } before)
            { BeforeNextTerminal = null; await before(); }
            var stored = await GetAsync(proposed.TaskId, token);
            if (proposed.PersistenceRevision < 1 || stored is null && proposed.PersistenceRevision != 1
                || stored is not null && (stored.PersistenceRevision != proposed.PersistenceRevision - 1
                    || stored.ContextId != proposed.ContextId || stored.ExecutionId != proposed.ExecutionId))
                throw new TaskExecutionRevisionConflictException(proposed.TaskId, proposed.PersistenceRevision - 1, proposed.PersistenceRevision);
            _rows[proposed.TaskId] = JsonSerializer.Serialize(proposed);
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid task, CancellationToken token) => Task.FromResult(
            _rows.TryGetValue(task, out var body) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(body) : null);
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid context, CancellationToken token) => Task.FromResult(
            _rows.Values.Select(value => JsonSerializer.Deserialize<TaskExecutionSnapshot>(value)!).FirstOrDefault(value => value.ContextId == context));
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(
            _rows.Values.Select(value => JsonSerializer.Deserialize<TaskExecutionSnapshot>(value)!).ToArray());
    }
    private sealed class Authority : ITaskRunCommandAuthority
    {
        public List<Lease> Leases { get; } = [];
        public bool AllowCurrentActor = true;
        public int CommandChecks;
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot snapshot, CancellationToken token) => Task.FromResult(
            new TaskExecutionOwnerBinding(snapshot.TaskId, snapshot.ContextId, snapshot.ExecutionId, "synthetic-actor", "synthetic-profile", null, null, "synthetic-auth", "synthetic-owner"));
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot snapshot, Guid id, TaskRunRouteCandidate candidate,
            Guid? previous, CancellationToken token)
        { var lease = new Lease(snapshot.OwnerBinding!, id, candidate); Leases.Add(lease); return Task.FromResult<ITaskRunAdmissionLease>(lease); }
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot snapshot, Guid attempt, Guid action, string receipt, CancellationToken token)
        { if (receipt != "synthetic-receipt") throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot snapshot, string command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); CommandChecks++;
            if (!AllowCurrentActor || snapshot.OwnerBinding is not { ActorId: "synthetic-actor", AuthenticationRevision: "synthetic-auth" })
                throw new UnauthorizedAccessException("Fresh synthetic actor denied");
            return Task.CompletedTask;
        }
    }
    private sealed class Lease(TaskExecutionOwnerBinding owner, Guid id, TaskRunRouteCandidate candidate) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => owner;
        public Guid AttemptId => id;
        public TaskRunRouteCandidate Candidate => candidate;
        public string ReceiptReference => "synthetic-live-original";
        public int Disposes;
        public Exception? CleanupFailure;
        public ValueTask RevalidateAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (Disposes != 0) throw new ObjectDisposedException(nameof(Lease)); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposes++; return CleanupFailure is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask; }
    }
    private sealed class RecordingRuntime(TaskRunOriginalFrameOwner inner) : ITaskRunOriginalFrameOwner, ITaskRunOriginalAttemptRetirement, ITaskRunOriginalAttemptRegistrationSource
    {
        public TaskRunOriginalFrameOwner Inner => inner;
        public TaskRunOriginalAttemptRegistrationDisposition RegisterOriginalAttemptDisposition(TaskRunAttemptAdmission original, CancellationToken token) =>
            inner.RegisterOriginalAttemptDisposition(original, token);
        public bool IsIssuedOriginalAttemptRegistrationDisposition(TaskRunOriginalAttemptRegistrationDisposition disposition, TaskRunAttemptAdmission original) =>
            inner.IsIssuedOriginalAttemptRegistrationDisposition(disposition, original);
        public List<TaskRunOriginalRetirementAcknowledgment> Retirements { get; } = [];
        public bool ProbeCopiedReceipt;
        public Exception? CopiedReceiptRefusal;
        public Task RegisterOriginalAttemptAsync(TaskRunAttemptAdmission a, CancellationToken t) => inner.RegisterOriginalAttemptAsync(a, t);
        public Task<T> StartOriginalFrameAsync<T>(TaskRunAttemptAdmission a, Func<CancellationToken, Task<T>> b, CancellationToken t) => inner.StartOriginalFrameAsync(a, b, t);
        public Task<T> StartOriginalResourceFrameAsync<T, TResource>(TaskRunAttemptAdmission a,
            Func<CancellationToken, Task<TResource>> acquire, Func<TResource, CancellationToken, Task> revalidate,
            Func<CancellationToken, Task<T>> body, CancellationToken token) where TResource : class, IAsyncDisposable =>
            inner.StartOriginalResourceFrameAsync<T, TResource>(a, acquire, revalidate, body, token);
        public Task<T> StartOriginalResourceFrameAsync<T, TResource>(TaskRunAttemptAdmission a,
            Func<CancellationToken, Task<TResource>> acquire, Func<TResource, CancellationToken, Task> revalidate,
            Func<TResource, CancellationToken, Task<T>> body, CancellationToken token) where TResource : class, IAsyncDisposable =>
            inner.StartOriginalResourceFrameAsync<T, TResource>(a, acquire, revalidate, body, token);
        public Task<T> StartOriginalToolFrameAsync<T>(TaskRunAttemptAdmission a, Func<CancellationToken, Task<T>> b, CancellationToken t) => inner.StartOriginalToolFrameAsync(a, b, t);
        public IAsyncEnumerable<T> StreamOriginalFrame<T>(TaskRunAttemptAdmission a, Func<CancellationToken, IAsyncEnumerable<T>> b, CancellationToken t) => inner.StreamOriginalFrame(a, b, t);
        public TaskRunOriginalFailureObservation CreateProviderFailureObservation(TaskRunAttemptAdmission a, Task f, Exception c) => inner.CreateProviderFailureObservation(a, f, c);
        public TaskRunOriginalFailureObservation? TryObserveProviderFailure(TaskRunAttemptAdmission a, Task f) => inner.TryObserveProviderFailure(a, f);
        public bool ValidateProviderFailureObservation(TaskRunOriginalFailureObservation o, TaskRunAttemptAdmission a) => inner.ValidateProviderFailureObservation(o, a);
        public void AcknowledgeProviderFailure(TaskRunOriginalFailureObservation o, TaskRunAttemptAdmission a, TaskRunFailurePersistenceAcknowledgment r) => inner.AcknowledgeProviderFailure(o, a, r);
        public Task AwaitSettlementAsync(Guid t, Guid r, Guid a, CancellationToken c) => inner.AwaitSettlementAsync(t, r, a, c);
        public async ValueTask RetireAcknowledgedOriginalAttemptAsync(TaskRunOriginalRetirementAcknowledgment receipt, CancellationToken token)
        {
            Retirements.Add(receipt);
            if (ProbeCopiedReceipt)
            {
                var copied = (TaskRunOriginalRetirementAcknowledgment)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(receipt, null)!;
                CopiedReceiptRefusal = await Record.ExceptionAsync(() => inner.RetireAcknowledgedOriginalAttemptAsync(copied, token).AsTask());
                Assert.NotNull(CopiedReceiptRefusal);
            }
            await inner.RetireAcknowledgedOriginalAttemptAsync(receipt, token);
        }
        public Task CloseAndDrainAsync() => inner.CloseAndDrainAsync();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
    private sealed class Checkpoints : ICheckpointRepository, ICheckpointExecutionObservationSource
    {
        public CheckpointInfo? Current;
        public Guid Execution;
        public Task<CheckpointInfo?> GetOriginalCheckpointAsync(Guid execution, Guid checkpoint, CancellationToken token) => Task.FromResult(
            Execution == execution && Current?.Id == checkpoint ? Current : null);
        public Task<CheckpointInfo?> GetAsync(Guid checkpoint, CancellationToken token) => Task.FromResult(Current?.Id == checkpoint ? Current : null);
        public Task SaveAsync(CheckpointInfo c, CancellationToken t) => throw new NotSupportedException();
        public Task<CheckpointInfo?> GetLatestAsync(Guid? c, string r, CancellationToken t) => throw new NotSupportedException();
        public Task<long> GetLatestVersionSequenceAsync(string r, CancellationToken t) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkspaceRestoreEntry>> GetVersionsSinceAsync(string r, long s, CancellationToken t) => throw new NotSupportedException();
        public Task<WorkspaceRestoreEntry?> GetLatestVersionAsync(string r, CancellationToken t) => throw new NotSupportedException();
    }
    private sealed class Sink : IExecutionEventSink { public bool TryPublish(ExecutionEvent e) => true; }
}
