using System.Collections;
using System.Reflection;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Real coordinator/frame registration and CAS originals. Actor/repository/provider
/// bodies are controlled; these controls do not claim native process or remote effect readiness.</summary>
public sealed partial class TaskRunLateAdmissionProcessTests
{
    [Fact]
    public async Task Late_original_authorization_returns_after_seal_without_new_CAS_and_closes_its_unissued_lease_once()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        var begin = await h.BeginAsync(token);
        var actualAuthorizationGate = new TaskCompletionSource<ITaskRunAdmissionLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Authority.AttemptGate = actualAuthorizationGate;
        var actual = h.Coordinator.StartAttemptAsync(begin.TaskId, begin.ExecutionId, Route(), token);
        await h.Authority.AttemptEntered.Task.WaitAsync(token);
        var rawAuthorization = actualAuthorizationGate.Task;
        var lease = Assert.Single(h.Authority.Leases);
        h.Coordinator.RequestOriginalProcessRetirement();
        Assert.Throws<InvalidOperationException>(() => { h.Coordinator.StartAttemptAsync(begin.TaskId, begin.ExecutionId, Route(), token); });
        actualAuthorizationGate.SetResult(lease);
        var failure = await Record.ExceptionAsync(() => actual.WaitAsync(token));
        Assert.NotNull(failure);
        Assert.True(actual.IsCompleted);
        Assert.Equal(1, lease.Disposes);
        Assert.True(lease.ActualClose.IsCompletedSuccessfully);
        Assert.Equal(1, h.Rows.Writes);
        Assert.Empty((await h.Rows.GetAsync(begin.TaskId, token))!.Attempts);
        Assert.Contains(AllSources(h.Coordinator), task => ReferenceEquals(task, rawAuthorization));
        var drainFailure = await Record.ExceptionAsync(() => h.Coordinator.CloseAndSuspendOriginalProducersAsync().WaitAsync(token));
        Assert.NotNull(drainFailure);
        var saved = (await h.Rows.GetAsync(begin.TaskId, token))!;
        Assert.Equal(begin.TaskId, saved.TaskId);
        Assert.Equal(begin.ContextId, saved.ContextId);
        Assert.Equal(begin.ExecutionId, saved.ExecutionId);
        Assert.Equal(TaskExecutionLifecycle.Suspended, saved.State);
        Assert.Empty(saved.Attempts);
        await h.Runtime.CloseAndDrainAsync().WaitAsync(token);
        Assert.Equal(1, lease.Disposes);
    }

    [Fact]
    public async Task Late_original_admission_CAS_uses_exact_never_owned_receipt_and_keeps_same_suspended_Task_Run()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        var begin = await h.BeginAsync(token);
        var actualWriteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Rows.AttemptWriteGate = actualWriteGate;
        var original = h.Coordinator.StartAttemptAsync(begin.TaskId, begin.ExecutionId, Route(), token);
        await h.Rows.AttemptWriteEntered.Task.WaitAsync(token);
        var proposed = Assert.IsType<TaskExecutionSnapshot>(h.Rows.HeldAttemptWrite);
        h.Coordinator.RequestOriginalProcessRetirement();
        actualWriteGate.SetResult(); // Controlled raw repository ACK ignores withdrawal after its actual invocation.
        var error = await Record.ExceptionAsync(() => original.WaitAsync(token));
        Assert.NotNull(error);
        var saved = (await h.Rows.GetAsync(begin.TaskId, token))!;
        Assert.Equal(begin.TaskId, saved.TaskId);
        Assert.Equal(begin.ContextId, saved.ContextId);
        Assert.Equal(begin.ExecutionId, saved.ExecutionId);
        Assert.Equal(TaskExecutionLifecycle.Suspended, saved.State);
        var attempt = Assert.Single(saved.Attempts);
        Assert.Equal(proposed.Attempts.Single().Id, attempt.Id);
        Assert.Equal(TaskRunAttemptState.Suspended, attempt.State);
        var issued = ReadIssued(h.Coordinator, attempt.Id);
        var source = (ITaskRunOriginalAttemptRegistrationSource)h.Runtime;
        var receipt = source.RegisterOriginalAttemptDisposition(issued, CancellationToken.None);
        Assert.True(source.IsIssuedOriginalAttemptRegistrationDisposition(receipt, issued));
        Assert.Equal(TaskRunOriginalAttemptRegistrationKind.RefusedBeforeOwnership, receipt.Kind);
        Assert.Null(receipt.OriginalRegistration);
        Assert.Null(receipt.OriginalSettlement);
        Assert.Null(receipt.OriginalLeaseClose);
        Assert.Null(receipt.OriginalRetirementAcknowledgment);
        Assert.NotNull(receipt.OriginalRefusal);
        var lease = Assert.Single(h.Authority.Leases);
        Assert.Equal(1, lease.Disposes);
        Assert.True(lease.ActualClose.IsCompletedSuccessfully);
        _ = await Record.ExceptionAsync(() => h.Runtime.RegisterOriginalAttemptAsync(issued, token));
        Assert.Equal(1, lease.Disposes);
        _ = await Record.ExceptionAsync(() => h.Coordinator.CloseAndSuspendOriginalProducersAsync().WaitAsync(token));
        await h.Runtime.CloseAndDrainAsync().WaitAsync(token);
        Assert.Equal(1, lease.Disposes);
        Assert.Equal(begin.ExecutionId, (await h.Rows.GetAsync(begin.TaskId, token))!.ExecutionId);
    }

    [Fact]
    public async Task Registered_lookup_raw_fault_preserves_direct_siblings_and_never_disposes_an_unknown_frame_owned_lease()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        var begin = await h.BeginAsync(token);
        var first = new OperationCanceledException("actual registered issuer lookup fault");
        var sibling = new IOException("actual registered issuer lookup sibling");
        var raw = new TaskCompletionSource<TaskRunAttemptAdmission?>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([first, sibling]);
        h.OverrideIssuedLookup = raw.Task;
        var original = h.Coordinator.StartAttemptAsync(begin.TaskId, begin.ExecutionId, Route(), token);
        var error = await Record.ExceptionAsync(() => original.WaitAsync(token));
        Assert.NotNull(error);
        Assert.True(original.IsFaulted);
        Assert.False(original.IsCanceled);
        Assert.Contains(Leaves(error!), cause => ReferenceEquals(cause, first));
        Assert.Contains(Leaves(error!), cause => ReferenceEquals(cause, sibling));
        var saved = (await h.Rows.GetAsync(begin.TaskId, token))!;
        Assert.Equal(TaskExecutionLifecycle.Suspended, saved.State);
        var attempt = Assert.Single(saved.Attempts);
        var issued = ReadIssued(h.Coordinator, attempt.Id);
        var source = (ITaskRunOriginalAttemptRegistrationSource)h.Runtime;
        var receipt = source.RegisterOriginalAttemptDisposition(issued, token);
        Assert.True(source.IsIssuedOriginalAttemptRegistrationDisposition(receipt, issued));
        Assert.Equal(TaskRunOriginalAttemptRegistrationKind.Registered, receipt.Kind);
        Assert.True(receipt.OriginalRegistration!.IsFaulted);
        Assert.True(receipt.OriginalSettlement!.IsFaulted);
        Assert.Null(receipt.OriginalLeaseClose);
        Assert.Null(receipt.OriginalRetirementAcknowledgment);
        Assert.Equal(0, Assert.Single(h.Authority.Leases).Disposes);
        var close = h.Coordinator.CloseAndSuspendOriginalProducersAsync();
        var closeError = await Record.ExceptionAsync(() => close.WaitAsync(token));
        Assert.NotNull(closeError);
        Assert.Contains(Leaves(closeError!), cause => ReferenceEquals(cause, first));
        Assert.Contains(Leaves(closeError!), cause => ReferenceEquals(cause, sibling));
        _ = await Record.ExceptionAsync(() => h.Runtime.CloseAndDrainAsync().WaitAsync(token));
        Assert.Equal(0, Assert.Single(h.Authority.Leases).Disposes);
        Assert.Equal(begin.TaskId, saved.TaskId);
        Assert.Equal(begin.ExecutionId, saved.ExecutionId);
    }

    [Fact]
    public async Task More_than_128_genuinely_completed_attempts_retire_stage_capacity_using_actual_registered_terminal_witnesses()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        TaskRunAttemptAdmission? last = null;
        for (var index = 0; index < 136; index++)
        {
            var begin = await h.BeginAsync(token);
            var admitted = await h.Coordinator.StartAttemptAsync(begin.TaskId, begin.ExecutionId, Route(), token);
            var actual = h.Runtime.StartOriginalFrameAsync(admitted, _ => Task.FromResult(index), token);
            Assert.Equal(index, await actual.WaitAsync(token));
            var completed = await h.Coordinator.CompleteAttemptAsync(begin.TaskId, begin.ExecutionId, admitted.AttemptId, token);
            Assert.Equal(TaskExecutionLifecycle.Completed, completed.State);
            Assert.Equal(begin.TaskId, completed.TaskId);
            Assert.Equal(begin.ExecutionId, completed.ExecutionId);
            var receipt = ((ITaskRunOriginalAttemptRegistrationSource)h.Runtime).RegisterOriginalAttemptDisposition(admitted, token);
            Assert.True(((ITaskRunOriginalAttemptRegistrationSource)h.Runtime).IsIssuedOriginalAttemptRegistrationDisposition(receipt, admitted));
            Assert.True(receipt.OriginalRegistration!.IsCompletedSuccessfully);
            Assert.True(receipt.OriginalSettlement!.IsCompletedSuccessfully);
            Assert.True(receipt.OriginalLeaseClose!.IsCompletedSuccessfully);
            Assert.Same(admitted, receipt.OriginalRetirementAcknowledgment!.OriginalAdmission);
            Assert.Same(((Lease)admitted.Lease).ActualClose, receipt.OriginalLeaseClose);
            Assert.Equal(1, ((Lease)admitted.Lease).Disposes);
            last = admitted;
        }
        Assert.Equal(136, h.Authority.Leases.Count);
        Assert.InRange(ReadStages(h.Coordinator).Count(), 1, 2);
        Assert.Empty(h.Coordinator.ObservationFailures);
        var lastBefore = (await h.Rows.GetAsync(last!.Snapshot.TaskId, token))!;
        Assert.Equal(TaskExecutionLifecycle.Completed, lastBefore.State);
        await h.Coordinator.CloseAndSuspendOriginalProducersAsync().WaitAsync(token);
        await h.Runtime.CloseAndDrainAsync().WaitAsync(token);
        Assert.All(h.Authority.Leases, lease => Assert.Equal(1, lease.Disposes));
        var lastAfter = (await h.Rows.GetAsync(last.Snapshot.TaskId, token))!;
        Assert.Equal(TaskExecutionLifecycle.Completed, lastAfter.State);
        Assert.Equal(lastBefore.PersistenceRevision, lastAfter.PersistenceRevision);
        Assert.Equal(lastBefore.ExecutionId, lastAfter.ExecutionId);
    }

    [Fact]
    public async Task Actual_begin_CAS_returning_after_process_seal_records_same_original_suspension_before_return()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        var actualWriteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Rows.BeginWriteGate = actualWriteGate;
        var context = Guid.NewGuid(); var run = Guid.NewGuid();
        var original = h.Coordinator.BeginAuthorizedAsync(context, run, "controlled late begin", TaskExecutionDurability.PersistedPlan, [], token);
        await h.Rows.BeginWriteEntered.Task.WaitAsync(token);
        var proposed = Assert.IsType<TaskExecutionSnapshot>(h.Rows.HeldBeginWrite);
        h.Coordinator.RequestOriginalProcessRetirement();
        actualWriteGate.SetResult();
        var error = await Record.ExceptionAsync(() => original.WaitAsync(token));
        Assert.NotNull(error);
        var saved = (await h.Rows.GetAsync(proposed.TaskId, token))!;
        Assert.Equal(proposed.TaskId, saved.TaskId);
        Assert.Equal(context, saved.ContextId);
        Assert.Equal(run, saved.ExecutionId);
        Assert.Equal(TaskExecutionLifecycle.Suspended, saved.State);
        Assert.Empty(saved.Attempts);
        Assert.Empty(h.Authority.Leases);
        _ = await Record.ExceptionAsync(() => h.Coordinator.CloseAndSuspendOriginalProducersAsync().WaitAsync(token));
        await h.Runtime.CloseAndDrainAsync().WaitAsync(token);
        Assert.Equal(proposed.TaskId, (await h.Rows.GetByContextAsync(context, token))!.TaskId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_authorization_raw_faulted_OCE_group_and_genuine_canceled_task_remain_distinct(bool genuineCancellation)
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        var begin = await h.BeginAsync(token);
        var first = new OperationCanceledException("actual authorization failure");
        var sibling = new IOException("actual authorization sibling");
        var raw = new TaskCompletionSource<ITaskRunAdmissionLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var withdrawn = new CancellationTokenSource();
        withdrawn.Cancel();
        if (genuineCancellation) raw.SetCanceled(withdrawn.Token); else raw.SetException([first, sibling]);
        h.Authority.OverrideAttempt = raw.Task;
        var original = h.Coordinator.StartAttemptAsync(begin.TaskId, begin.ExecutionId, Route(), token);
        var failure = await Record.ExceptionAsync(() => original.WaitAsync(token));
        Assert.NotNull(failure);
        Assert.Equal(genuineCancellation, original.IsCanceled);
        Assert.Equal(!genuineCancellation, original.IsFaulted);
        if (!genuineCancellation)
        {
            Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, first));
            Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, sibling));
        }
        Assert.Contains(AllSources(h.Coordinator), task => ReferenceEquals(task, raw.Task));
        Assert.Empty(h.Authority.Leases);
        Assert.Empty((await h.Rows.GetAsync(begin.TaskId, token))!.Attempts);
        _ = await Record.ExceptionAsync(() => h.Coordinator.CloseAndSuspendOriginalProducersAsync().WaitAsync(token));
        await h.Runtime.CloseAndDrainAsync().WaitAsync(token);
    }

    private static TaskRunRouteCandidate Route() => new("controlled-route", 1, "controlled-provider", "controlled-model", null, false, []);
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException group ? group.InnerExceptions.SelectMany(Leaves) : [error];
    private static object Read(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(value)
        ?? value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(value)
        ?? throw new InvalidOperationException($"Actual private source member {name} missing.");
    private static IEnumerable<object> ReadStages(TaskExecutionCoordinator owner) => ((IEnumerable)Read(owner, "_originalProcessStages")).Cast<object>();
    private static IEnumerable<Task> AllSources(TaskExecutionCoordinator owner) => ReadStages(owner).SelectMany(stage => (IReadOnlyList<Task>)Read(stage, "ActualSources"));
    private static TaskRunAttemptAdmission ReadIssued(TaskExecutionCoordinator owner, Guid attempt) =>
        ((System.Collections.Concurrent.ConcurrentDictionary<Guid, TaskRunAttemptAdmission>)Read(owner, "_issuedAdmissions"))[attempt];

    private sealed class Harness
    {
        internal readonly Rows Rows = new();
        internal readonly Authority Authority = new();
        internal readonly TaskExecutionCoordinator Coordinator;
        internal readonly TaskRunOriginalFrameOwner Runtime;
        internal Task<TaskRunAttemptAdmission?>? OverrideIssuedLookup = null;
        internal Harness()
        {
            Runtime = new((task, run, attempt, token) => OverrideIssuedLookup ?? Coordinator.GetIssuedAttemptAsync(task, run, attempt, token));
            Coordinator = new(Rows, new Sink(), admissionAuthority: Authority, runtimeSettlement: Runtime);
        }
        internal Task<TaskExecutionSnapshot> BeginAsync(CancellationToken token) => Coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(),
            "controlled original begin", TaskExecutionDurability.PersistedPlan, [], token);
    }
    private sealed class Rows : ITaskExecutionRepository
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, string> _rows = [];
        internal int Writes;
        internal Task<TaskExecutionSnapshot?>? OverrideRead = null;
        internal TaskCompletionSource? AttemptWriteGate = null;
        internal TaskCompletionSource? BeginWriteGate = null;
        internal readonly TaskCompletionSource AttemptWriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource BeginWriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskExecutionSnapshot? HeldAttemptWrite = null;
        internal TaskExecutionSnapshot? HeldBeginWrite = null;
        public async Task UpsertAsync(TaskExecutionSnapshot value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (value.Attempts.Count != 0 && AttemptWriteGate is { } attemptGate)
            { AttemptWriteGate = null; HeldAttemptWrite = value; AttemptWriteEntered.SetResult(); await attemptGate.Task.ConfigureAwait(false); }
            if (value.Attempts.Count == 0 && BeginWriteGate is { } beginGate)
            { BeginWriteGate = null; HeldBeginWrite = value; BeginWriteEntered.SetResult(); await beginGate.Task.ConfigureAwait(false); }
            lock (_gate)
            {
                var previous = _rows.TryGetValue(value.TaskId, out var text) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(text) : null;
                if (previous is null && value.PersistenceRevision != 1 || previous is not null && value.PersistenceRevision != previous.PersistenceRevision + 1)
                    throw new TaskExecutionRevisionConflictException(value.TaskId, value.PersistenceRevision - 1, previous?.PersistenceRevision ?? 0);
                _rows[value.TaskId] = JsonSerializer.Serialize(value); Writes++;
            }
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid task, CancellationToken token)
        { if (OverrideRead is { } actual) return actual; lock (_gate) return Task.FromResult(_rows.TryGetValue(task, out var text) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(text) : null); }
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid context, CancellationToken token)
        { lock (_gate) return Task.FromResult(_rows.Values.Select(text => JsonSerializer.Deserialize<TaskExecutionSnapshot>(text)!).FirstOrDefault(value => value.ContextId == context)); }
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token)
        { lock (_gate) return Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_rows.Values.Select(text => JsonSerializer.Deserialize<TaskExecutionSnapshot>(text)!).ToArray()); }
    }
    private sealed class Authority : ITaskRunCommandAuthority
    {
        internal readonly List<Lease> Leases = [];
        internal TaskCompletionSource<ITaskRunAdmissionLease>? AttemptGate = null;
        internal Task<ITaskRunAdmissionLease>? OverrideAttempt = null;
        internal readonly TaskCompletionSource AttemptEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot value, CancellationToken token) => Task.FromResult(
            new TaskExecutionOwnerBinding(value.TaskId, value.ContextId, value.ExecutionId, "controlled-actor", "controlled-profile", null, null, "controlled-auth", "controlled-start"));
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot value, Guid id, TaskRunRouteCandidate candidate, Guid? previous, CancellationToken token)
        {
            if (OverrideAttempt is { } actual) return actual;
            var lease = new Lease(value.OwnerBinding!, id, candidate); Leases.Add(lease);
            AttemptEntered.TrySetResult();
            return AttemptGate?.Task ?? Task.FromResult<ITaskRunAdmissionLease>(lease);
        }
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot value, string command, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot value, Guid attempt, Guid action, string receipt, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Lease(TaskExecutionOwnerBinding binding, Guid id, TaskRunRouteCandidate candidate) : ITaskRunAdmissionLease
    {
        private readonly TaskCompletionSource _originalClose = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposes;
        internal Task ActualClose => _originalClose.Task;
        public TaskExecutionOwnerBinding Owner => binding;
        public Guid AttemptId => id;
        public TaskRunRouteCandidate Candidate => candidate;
        public string ReceiptReference => "controlled-private-admission";
        public ValueTask RevalidateAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (Disposes != 0) throw new ObjectDisposedException(nameof(Lease)); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposes++; _originalClose.TrySetResult(); return new(ActualClose); }
    }
    private sealed class Sink : IExecutionEventSink { public bool TryPublish(ExecutionEvent value) => true; }
}
