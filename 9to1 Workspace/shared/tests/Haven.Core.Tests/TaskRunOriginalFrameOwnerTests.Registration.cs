using System.Reflection;
using System.Text.Json;
using Haven.Core;
using Haven.Application;
using Xunit;

namespace Haven.Application.Tests;

public sealed partial class TaskRunOriginalFrameOwnerTests
{
    [Fact]
    public async Task Registration_disposition_and_same_original_are_published_before_issuer_callback()
    {
        var h = Harness.Create();
        TaskRunOriginalFrameOwner? owner = null;
        TaskRunOriginalAttemptRegistrationDisposition? callbackReceipt = null;
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        owner = new((_, _, _, _) =>
        {
            callbackReceipt = owner!.RegisterOriginalAttemptDisposition(h.Admission, canceled.Token);
            Assert.True(owner.IsIssuedOriginalAttemptRegistrationDisposition(callbackReceipt, h.Admission));
            return Task.FromResult<TaskRunAttemptAdmission?>(h.Admission);
        });
        var first = owner.RegisterOriginalAttemptDisposition(h.Admission, default);
        try
        {
            Assert.Equal(TaskRunOriginalAttemptRegistrationKind.Registered, first.Kind);
            Assert.Null(first.OriginalRefusal);
            await first.OriginalRegistration!;
            Assert.NotNull(callbackReceipt);
            Assert.Same(first.OriginalRegistration, callbackReceipt.OriginalRegistration);
        }
        finally { await owner.CloseAndDrainAsync(); }
        Assert.Equal(1, h.Lease.Disposes);
    }

    [Fact]
    public async Task Already_registered_original_stays_registered_after_cancellation_and_global_close()
    {
        var h = Harness.Create();
        var original = h.Runtime.RegisterOriginalAttemptAsync(h.Admission, default);
        await original;
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var during = h.Runtime.RegisterOriginalAttemptDisposition(h.Admission, canceled.Token);
        await h.Runtime.CloseAndDrainAsync();
        var after = h.Runtime.RegisterOriginalAttemptDisposition(h.Admission, canceled.Token);
        foreach (var receipt in new[] { during, after })
        {
            Assert.Equal(TaskRunOriginalAttemptRegistrationKind.Registered, receipt.Kind);
            Assert.Same(original, receipt.OriginalRegistration);
            Assert.Null(receipt.OriginalRefusal);
            Assert.True(h.Runtime.IsIssuedOriginalAttemptRegistrationDisposition(receipt, h.Admission));
        }
        Assert.Equal(1, h.Lease.Disposes);
    }

    [Fact]
    public async Task Canceled_never_enrolled_original_cannot_be_adopted_after_issuer_closes_its_lease()
    {
        var h = Harness.Create(); var lookups = 0;
        var owner = new TaskRunOriginalFrameOwner((_, _, _, _) =>
        { lookups++; return Task.FromResult<TaskRunAttemptAdmission?>(h.Admission); });
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var receipt = owner.RegisterOriginalAttemptDisposition(h.Admission, canceled.Token);
        Assert.Equal(TaskRunOriginalAttemptRegistrationKind.RefusedBeforeOwnership, receipt.Kind);
        Assert.Null(receipt.OriginalRegistration);
        var refusal = Assert.IsType<OperationCanceledException>(receipt.OriginalRefusal);
        Assert.Equal(canceled.Token, refusal.CancellationToken);
        Assert.True(owner.IsIssuedOriginalAttemptRegistrationDisposition(receipt, h.Admission));
        await h.Lease.DisposeAsync(); // Actual issuer remains sole close owner for this exact refusal.
        Assert.Same(refusal, Assert.Throws<OperationCanceledException>(() => { _ = owner.RegisterOriginalAttemptAsync(h.Admission, default); }));
        var repeated = owner.RegisterOriginalAttemptDisposition(h.Admission, default);
        Assert.Same(refusal, repeated.OriginalRefusal);
        Assert.Null(repeated.OriginalRegistration);
        await owner.CloseAndDrainAsync();
        Assert.Equal(0, lookups);
        Assert.Equal(1, h.Lease.Disposes);
    }

    [Fact]
    public async Task Global_close_before_enrollment_issues_exact_refusal_without_lease_or_lookup_custody()
    {
        var h = Harness.Create(); var called = false;
        var owner = new TaskRunOriginalFrameOwner((_, _, _, _) =>
        { called = true; return Task.FromResult<TaskRunAttemptAdmission?>(h.Admission); });
        await owner.CloseAndDrainAsync();
        var receipt = owner.RegisterOriginalAttemptDisposition(h.Admission, default);
        Assert.Equal(TaskRunOriginalAttemptRegistrationKind.RefusedBeforeOwnership, receipt.Kind);
        Assert.IsType<ObjectDisposedException>(receipt.OriginalRefusal);
        Assert.Null(receipt.OriginalRegistration);
        Assert.True(owner.IsIssuedOriginalAttemptRegistrationDisposition(receipt, h.Admission));
        Assert.False(called);
        Assert.Equal(0, h.Lease.Disposes);
        await h.Lease.DisposeAsync();
        Assert.Equal(1, h.Lease.Disposes);
    }

    [Fact]
    public async Task Held_then_faulted_issuer_lookup_remains_registered_unknown_and_blocks_healthy_close()
    {
        var h = Harness.Create();
        var lookup = new TaskCompletionSource<TaskRunAttemptAdmission?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new TaskRunOriginalFrameOwner((_, _, _, _) => { entered.SetResult(); return lookup.Task; });
        var receipt = owner.RegisterOriginalAttemptDisposition(h.Admission, default);
        var cause = new IOException("Actual issuer lookup failed before lease ownership validation");
        Task? close = null;
        try
        {
            await entered.Task;
            close = owner.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            var during = owner.RegisterOriginalAttemptDisposition(h.Admission, default);
            Assert.Equal(TaskRunOriginalAttemptRegistrationKind.Registered, during.Kind);
            Assert.Same(receipt.OriginalRegistration, during.OriginalRegistration);
            lookup.SetException(cause);
            Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => receipt.OriginalRegistration!));
            var failure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(failure);
            Assert.Contains(Causes(failure!), error => ReferenceEquals(error, cause));
            Assert.True(owner.IsIssuedOriginalAttemptRegistrationDisposition(receipt, h.Admission));
            Assert.Null(receipt.OriginalRefusal);
            Assert.Equal(0, h.Lease.Disposes); // Unknown is retained; neither owner fabricates a close proof.
        }
        finally
        {
            lookup.TrySetException(cause);
            _ = await Record.ExceptionAsync(() => receipt.OriginalRegistration!);
            if (close is not null) _ = await Record.ExceptionAsync(() => close);
        }
    }

    [Fact]
    public async Task Copied_receipt_cross_owner_and_colliding_admission_never_certify_absence()
    {
        var h = Harness.Create(); var other = Harness.Create();
        var receipt = h.Runtime.RegisterOriginalAttemptDisposition(h.Admission, default);
        await receipt.OriginalRegistration!;
        try
        {
            var copy = (TaskRunOriginalAttemptRegistrationDisposition)typeof(object)
                .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(receipt, null)!;
            Assert.False(h.Runtime.IsIssuedOriginalAttemptRegistrationDisposition(copy, h.Admission));
            Assert.False(other.Runtime.IsIssuedOriginalAttemptRegistrationDisposition(receipt, h.Admission));
            Assert.False(h.Runtime.IsIssuedOriginalAttemptRegistrationDisposition(receipt, h.Admission with { }));
            Assert.Throws<UnauthorizedAccessException>(() => { _ = h.Runtime.RegisterOriginalAttemptDisposition(h.Admission with { }, default); });
            Assert.Same(receipt.OriginalRegistration, h.Runtime.RegisterOriginalAttemptDisposition(h.Admission, default).OriginalRegistration);
        }
        finally { await h.Runtime.CloseAndDrainAsync(); await other.Runtime.CloseAndDrainAsync(); }
        Assert.Equal(1, h.Lease.Disposes);
        Assert.Equal(0, other.Lease.Disposes);
    }

    [Fact]
    public async Task Healthy_retirement_keeps_same_original_registration_and_terminal_tasks_after_canceled_query()
    {
        var repository = new RetirementRepository();
        var authority = new RetirementAuthority();
        TaskExecutionCoordinator? coordinator = null;
        var owner = new TaskRunOriginalFrameOwner((task, run, attempt, token) =>
            coordinator!.GetIssuedAttemptAsync(task, run, attempt, token));
        coordinator = new(repository, new RetirementSink(), admissionAuthority: authority, runtimeSettlement: owner);
        var token = CancellationToken.None;
        try
        {
            var task = await coordinator.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "actual healthy retirement",
                TaskExecutionDurability.PersistedPlan, [], token);
            var admission = await coordinator.StartAttemptAsync(task.TaskId, task.ExecutionId,
                new("synthetic-retirement-route", 1, "synthetic-provider", "synthetic-model", null, false, []), token);
            var first = owner.RegisterOriginalAttemptDisposition(admission, token);
            await first.OriginalRegistration!;
            Assert.Null(first.OriginalRetirementAcknowledgment);
            Assert.Equal(7, await owner.StartOriginalFrameAsync(admission, _ => Task.FromResult(7), token));
            var completed = await coordinator.CompleteAttemptAsync(task.TaskId, task.ExecutionId, admission.AttemptId, token);
            Assert.Equal(TaskExecutionLifecycle.Completed, completed.State);
            Assert.Empty(coordinator.ObservationFailures);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            var historical = owner.RegisterOriginalAttemptDisposition(admission, canceled.Token);
            Assert.Equal(TaskRunOriginalAttemptRegistrationKind.Registered, historical.Kind);
            Assert.Same(first.OriginalRegistration, historical.OriginalRegistration);
            Assert.Null(historical.OriginalRefusal);
            Assert.True(owner.IsIssuedOriginalAttemptRegistrationDisposition(first, admission));
            Assert.True(owner.IsIssuedOriginalAttemptRegistrationDisposition(historical, admission));
            Assert.True(historical.OriginalSettlement!.IsCompletedSuccessfully);
            Assert.True(historical.OriginalLeaseClose!.IsCompletedSuccessfully);
            var acknowledged = Assert.IsType<TaskRunOriginalRetirementAcknowledgment>(historical.OriginalRetirementAcknowledgment);
            Assert.Same(admission, acknowledged.OriginalAdmission);
            Assert.Equal(completed.PersistenceRevision, acknowledged.AcknowledgedRevision);
            Assert.Equal(1, Assert.Single(authority.Leases).Disposes);
            Assert.Throws<InvalidOperationException>(() => { _ = owner.RegisterOriginalAttemptAsync(admission, token); });
            var copy = (TaskRunOriginalAttemptRegistrationDisposition)typeof(object)
                .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(historical, null)!;
            Assert.False(owner.IsIssuedOriginalAttemptRegistrationDisposition(copy, admission));
            await owner.CloseAndDrainAsync();
            var closed = owner.RegisterOriginalAttemptDisposition(admission, canceled.Token);
            Assert.Same(historical.OriginalRegistration, closed.OriginalRegistration);
            Assert.Same(historical.OriginalSettlement, closed.OriginalSettlement);
            Assert.Same(historical.OriginalLeaseClose, closed.OriginalLeaseClose);
            Assert.Same(acknowledged, closed.OriginalRetirementAcknowledgment);
            Assert.Null(closed.OriginalRefusal);
            Assert.Equal(1, Assert.Single(authority.Leases).Disposes);
        }
        finally { await owner.CloseAndDrainAsync(); }
    }

    private sealed class RetirementRepository : ITaskExecutionRepository
    {
        private readonly Dictionary<Guid, string> _rows = [];
        public Task UpsertAsync(TaskExecutionSnapshot proposed, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var stored = _rows.TryGetValue(proposed.TaskId, out var json) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(json) : null;
            if (proposed.PersistenceRevision < 1 || stored is null && proposed.PersistenceRevision != 1
                || stored is not null && (stored.PersistenceRevision != proposed.PersistenceRevision - 1
                    || stored.ContextId != proposed.ContextId || stored.ExecutionId != proposed.ExecutionId))
                throw new TaskExecutionRevisionConflictException(proposed.TaskId, proposed.PersistenceRevision - 1, proposed.PersistenceRevision);
            _rows[proposed.TaskId] = JsonSerializer.Serialize(proposed);
            return Task.CompletedTask;
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid task, CancellationToken token) => Task.FromResult(
            _rows.TryGetValue(task, out var row) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(row) : null);
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid context, CancellationToken token) => Task.FromResult(
            _rows.Values.Select(value => JsonSerializer.Deserialize<TaskExecutionSnapshot>(value)!).FirstOrDefault(value => value.ContextId == context));
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(
            _rows.Values.Select(value => JsonSerializer.Deserialize<TaskExecutionSnapshot>(value)!).ToArray());
    }
    private sealed class RetirementAuthority : ITaskRunCommandAuthority
    {
        public List<RetirementLease> Leases { get; } = [];
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot snapshot, CancellationToken token) => Task.FromResult(
            new TaskExecutionOwnerBinding(snapshot.TaskId, snapshot.ContextId, snapshot.ExecutionId, "synthetic-actor", "synthetic-profile", null, null, "synthetic-auth", "synthetic-owner"));
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot snapshot, Guid id, TaskRunRouteCandidate candidate,
            Guid? previous, CancellationToken token)
        {
            var lease = new RetirementLease(snapshot.OwnerBinding!, id, candidate); Leases.Add(lease);
            return Task.FromResult<ITaskRunAdmissionLease>(lease);
        }
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot snapshot, Guid attempt, Guid action, string receipt, CancellationToken token) => Task.CompletedTask;
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot snapshot, string command, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
    private sealed class RetirementLease(TaskExecutionOwnerBinding owner, Guid id, TaskRunRouteCandidate candidate) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => owner;
        public Guid AttemptId => id;
        public TaskRunRouteCandidate Candidate => candidate;
        public string ReceiptReference => "synthetic-actual-retirement";
        public int Disposes { get; private set; }
        public ValueTask RevalidateAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (Disposes != 0) throw new ObjectDisposedException(nameof(RetirementLease)); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() { Disposes++; return ValueTask.CompletedTask; }
    }
    private sealed class RetirementSink : IExecutionEventSink { public bool TryPublish(ExecutionEvent value) => true; }
}
