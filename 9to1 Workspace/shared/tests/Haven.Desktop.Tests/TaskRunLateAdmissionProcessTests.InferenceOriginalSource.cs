using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual maintained coordinator/frame/CAS fixture; repository and actor bodies are
/// controlled. No model execution, current installed actor or native provider grant is claimed.</summary>
public sealed partial class TaskRunLateAdmissionProcessTests
{
    [Fact]
    public async Task Scoped_inference_lookup_returns_same_private_admission_and_retains_actual_row_read()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        Exception? expected = null, primary = null;
        try
        {
            var admission = await MintInferenceAdmissionAsync(h, token);
            var saved = (await h.Rows.GetAsync(admission.Snapshot.TaskId, token))!;
            var raw = Task.FromResult<TaskExecutionSnapshot?>(saved);
            h.Rows.OverrideRead = raw;
            var retained = new List<Task>();
            var callbacks = 0;
            var original = ((ITaskRunOriginalInferenceAttemptSource)h.Coordinator).GetIssuedAttemptWithinOriginalSourceAsync(
                admission, callback => { callbacks++; callback(); }, retained.Add, token);
            Assert.Same(admission, await original.WaitAsync(token));
            Assert.Equal(3, callbacks);
            Assert.Same(raw, Assert.Single(retained));
            Assert.Contains(AllSources(h.Coordinator), actual => ReferenceEquals(actual, raw));
            Assert.Contains(ReadStages(h.Coordinator), stage => ReferenceEquals(Read(stage, "ActualDriver"), original));
            Assert.Equal(admission.Snapshot.ExecutionId, saved.ExecutionId);
            Assert.Equal(admission.AttemptId, saved.Attempts.Last().Id);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await JoinInferenceControlOriginalsAsync(h, expected, primary); }
    }

    [Fact]
    public async Task Copied_inference_admission_cannot_acquire_raw_repository_source_or_lease_permission()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        Exception? expected = null, primary = null;
        try
        {
            var admission = await MintInferenceAdmissionAsync(h, token);
            var copied = admission with { };
            var retained = new List<Task>();
            var writes = h.Rows.Writes;
            var result = await h.Coordinator.GetIssuedAttemptWithinOriginalSourceAsync(copied, callback => callback(), retained.Add, token);
            Assert.NotSame(admission, copied);
            Assert.Null(result);
            Assert.Empty(retained);
            Assert.Equal(writes, h.Rows.Writes);
            Assert.Same(admission, ReadIssued(h.Coordinator, admission.AttemptId));
            Assert.Equal(0, ((Lease)admission.Lease).Disposes);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await JoinInferenceControlOriginalsAsync(h, expected, primary); }
    }

    [Fact]
    public async Task Raw_inference_read_acquired_before_scope_fault_and_process_seal_remains_pending_and_keeps_full_causes()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        Exception? expected = null, primary = null;
        var raw = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var admission = await MintInferenceAdmissionAsync(h, token);
            h.Rows.OverrideRead = raw.Task;
            var retained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var scopeFailure = new IOException("actual caller scope failed after returned raw read");
            var first = new OperationCanceledException("actual faulted read first cause");
            var sibling = new IOException("actual faulted read sibling");
            var scopes = 0;
            var original = h.Coordinator.GetIssuedAttemptWithinOriginalSourceAsync(admission, callback =>
            {
                callback();
                if (++scopes == 2) throw scopeFailure;
            }, actual => { Assert.Same(raw.Task, actual); retained.SetResult(); }, token);
            await retained.Task.WaitAsync(token);
            h.Coordinator.RequestOriginalProcessRetirement();
            var close = h.Coordinator.CloseAndSuspendOriginalProducersAsync();
            Assert.False(original.IsCompleted);
            Assert.False(close.IsCompleted);
            Assert.Contains(AllSources(h.Coordinator), actual => ReferenceEquals(actual, raw.Task));
            h.Rows.OverrideRead = null; // Later owning cleanup reads the actual saved row independently.
            raw.SetException([first, sibling]);
            var failure = await Record.ExceptionAsync(() => original.WaitAsync(token));
            expected = failure;
            Assert.NotNull(failure);
            Assert.True(raw.Task.IsFaulted);
            Assert.True(original.IsFaulted);
            Assert.False(original.IsCanceled);
            Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, scopeFailure));
            Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, first));
            Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, sibling));
            var closeFailure = await Record.ExceptionAsync(() => close.WaitAsync(token));
            Assert.NotNull(closeFailure);
            Assert.Contains(Leaves(closeFailure!), cause => ReferenceEquals(cause, first));
            Assert.Contains(Leaves(closeFailure!), cause => ReferenceEquals(cause, sibling));
            Assert.Equal(admission.Snapshot.ExecutionId, (await h.Rows.GetAsync(admission.Snapshot.TaskId, token))!.ExecutionId);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { raw.TrySetResult(null); await JoinInferenceControlOriginalsAsync(h, expected, primary); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scoped_inference_raw_faulted_OCE_and_genuinely_canceled_task_keep_distinct_status(bool genuineCancellation)
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        Exception? expected = null, primary = null;
        using var withdrawn = new CancellationTokenSource();
        withdrawn.Cancel();
        try
        {
            var admission = await MintInferenceAdmissionAsync(h, token);
            var raw = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = new OperationCanceledException("actual raw inference read fault");
            var sibling = new IOException("actual raw inference read sibling");
            if (genuineCancellation) raw.SetCanceled(withdrawn.Token); else raw.SetException([first, sibling]);
            h.Rows.OverrideRead = raw.Task;
            var retained = new List<Task>();
            var original = h.Coordinator.GetIssuedAttemptWithinOriginalSourceAsync(admission, callback => callback(), retained.Add, token);
            var failure = await Record.ExceptionAsync(() => original.WaitAsync(token));
            expected = failure;
            Assert.NotNull(failure);
            Assert.Equal(genuineCancellation, original.IsCanceled);
            Assert.Equal(!genuineCancellation, original.IsFaulted);
            Assert.Same(raw.Task, Assert.Single(retained));
            Assert.Contains(AllSources(h.Coordinator), actual => ReferenceEquals(actual, raw.Task));
            if (!genuineCancellation)
            {
                Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, first));
                Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, sibling));
            }
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await JoinInferenceControlOriginalsAsync(h, expected, primary); }
    }

    [Fact]
    public async Task Post_await_inference_scope_keeps_physical_guard_when_caller_restores_pre_owner_context()
    {
        var token = TestContext.Current.CancellationToken;
        var previous = ExecutionContext.Capture()!;
        var h = new Harness();
        Exception? expected = null, primary = null;
        var raw = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var admission = await MintInferenceAdmissionAsync(h, token);
            var saved = (await h.Rows.GetAsync(admission.Snapshot.TaskId, token))!;
            h.Rows.OverrideRead = raw.Task;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var scopes = 0;
            Exception? refusal = null;
            var original = h.Coordinator.GetIssuedAttemptWithinOriginalSourceAsync(admission, callback =>
            {
                var index = ++scopes;
                ExecutionContext.Run(previous, _ =>
                {
                    if (index == 3) refusal = Record.Exception((Action)(() => h.Coordinator.DemandExternalOriginalProcessJoin()));
                    callback();
                }, null);
            }, _ => entered.SetResult(), token);
            await entered.Task.WaitAsync(token);
            Assert.False(original.IsCompleted);
            raw.SetResult(saved);
            Assert.Same(admission, await original.WaitAsync(token));
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.False((bool)Read(h.Coordinator, "_processProducerAdmissionSealed"));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { raw.TrySetResult(null); await JoinInferenceControlOriginalsAsync(h, expected, primary); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_or_duplicate_finite_inference_callbacks_refuse_even_if_caller_swallows_refusal(bool duplicate)
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        Exception? expected = null, primary = null;
        try
        {
            var admission = await MintInferenceAdmissionAsync(h, token);
            Action? late = null;
            var retained = new List<Task>();
            Exception? swallowed = null;
            var original = h.Coordinator.GetIssuedAttemptWithinOriginalSourceAsync(admission, callback =>
            {
                late = callback;
                if (duplicate) { callback(); swallowed = Record.Exception((Action)callback); }
            }, retained.Add, token);
            var failure = await Record.ExceptionAsync(() => original.WaitAsync(token));
            expected = failure;
            Assert.NotNull(failure);
            Assert.True(original.IsFaulted);
            Assert.Empty(retained);
            Assert.NotNull(late);
            Assert.Throws<InvalidOperationException>((Action)late!);
            if (duplicate) Assert.Same(swallowed, Assert.Single(Leaves(failure!)));
            Assert.Same(admission, ReadIssued(h.Coordinator, admission.AttemptId));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await JoinInferenceControlOriginalsAsync(h, expected, primary); }
    }

    [Fact]
    public async Task Caller_scope_sealing_process_before_raw_factory_denies_new_read_and_preserves_original_admission()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        Exception? expected = null, primary = null;
        try
        {
            var admission = await MintInferenceAdmissionAsync(h, token);
            var scopes = 0;
            var retained = new List<Task>();
            var original = h.Coordinator.GetIssuedAttemptWithinOriginalSourceAsync(admission, callback =>
            {
                if (++scopes == 2) h.Coordinator.RequestOriginalProcessRetirement();
                callback();
            }, retained.Add, token);
            var failure = await Record.ExceptionAsync(() => original.WaitAsync(token));
            expected = failure;
            Assert.NotNull(failure);
            Assert.True(original.IsFaulted);
            Assert.Empty(retained);
            Assert.True((bool)Read(h.Coordinator, "_processProducerAdmissionSealed"));
            Assert.Same(admission, ReadIssued(h.Coordinator, admission.AttemptId));
            Assert.Throws<InvalidOperationException>((Action)(() =>
            { _ = h.Coordinator.GetIssuedAttemptWithinOriginalSourceAsync(admission, callback => callback(), retained.Add, token); }));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await JoinInferenceControlOriginalsAsync(h, expected, primary); }
    }

    [Fact]
    public async Task Current_inference_row_route_change_during_held_read_refuses_same_old_issued_admission()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        Exception? expected = null, primary = null;
        var raw = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var admission = await MintInferenceAdmissionAsync(h, token);
            var saved = (await h.Rows.GetAsync(admission.Snapshot.TaskId, token))!;
            h.Rows.OverrideRead = raw.Task;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = h.Coordinator.GetIssuedAttemptWithinOriginalSourceAsync(admission, callback => callback(), _ => entered.SetResult(), token);
            await entered.Task.WaitAsync(token);
            var changed = saved with
            {
                PersistenceRevision = saved.PersistenceRevision + 1,
                Attempts = saved.Attempts.Select(attempt => attempt with { Candidate = attempt.Candidate with { ModelId = "different-current-model" } }).ToArray()
            };
            await h.Rows.UpsertAsync(changed, token);
            raw.SetResult((await h.Rows.GetByContextAsync(saved.ContextId, token))!);
            var failure = await Record.ExceptionAsync(() => original.WaitAsync(token));
            expected = failure;
            Assert.IsType<InvalidOperationException>(failure);
            Assert.True(original.IsFaulted);
            Assert.Same(admission, ReadIssued(h.Coordinator, admission.AttemptId));
            Assert.Equal("different-current-model", (await h.Rows.GetByContextAsync(saved.ContextId, token))!.Attempts.Last().Candidate.ModelId);
            Assert.Equal(saved.ExecutionId, changed.ExecutionId);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { raw.TrySetResult(null); await JoinInferenceControlOriginalsAsync(h, expected, primary); }
    }

    [Fact]
    public async Task Pre_callback_inference_withdrawal_invokes_no_scope_or_repository_factory()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        Exception? expected = null, primary = null;
        using var withdrawn = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            var admission = await MintInferenceAdmissionAsync(h, token);
            withdrawn.Cancel();
            var calls = 0;
            var retained = new List<Task>();
            var original = h.Coordinator.GetIssuedAttemptWithinOriginalSourceAsync(admission,
                callback => { calls++; callback(); }, retained.Add, withdrawn.Token);
            var failure = await Record.ExceptionAsync(() => original.WaitAsync(token));
            expected = failure;
            Assert.IsAssignableFrom<OperationCanceledException>(failure);
            Assert.True(original.IsCanceled);
            Assert.False(original.IsFaulted);
            Assert.Equal(0, calls);
            Assert.Empty(retained);
            Assert.Equal(0, ((Lease)admission.Lease).Disposes);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await JoinInferenceControlOriginalsAsync(h, expected, primary); }
    }

    [Fact]
    public async Task Final_inference_caller_scope_retiring_after_callback_cannot_disclose_the_issued_admission()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        Exception? expected = null, primary = null;
        try
        {
            var admission = await MintInferenceAdmissionAsync(h, token);
            var scopes = 0;
            var retained = new List<Task>();
            var original = h.Coordinator.GetIssuedAttemptWithinOriginalSourceAsync(admission, callback =>
            { callback(); if (++scopes == 3) h.Coordinator.RequestOriginalProcessRetirement(); }, retained.Add, token);
            var failure = await Record.ExceptionAsync(() => original.WaitAsync(token));
            expected = failure;
            Assert.IsType<InvalidOperationException>(failure);
            Assert.True(original.IsFaulted);
            _ = Assert.Single(retained);
            Assert.True(retained[0].IsCompletedSuccessfully);
            Assert.Same(admission, ReadIssued(h.Coordinator, admission.AttemptId));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await JoinInferenceControlOriginalsAsync(h, expected, primary); }
    }

    [Fact]
    public async Task Unexpected_issuer_lease_cleanup_fault_fails_positive_control_and_still_joins_both_owners()
    {
        var token = TestContext.Current.CancellationToken;
        var h = new Harness();
        Exception? expected = null, primary = null;
        try
        {
            var admission = await MintInferenceAdmissionAsync(h, token);
            var cause = new IOException("unexpected actual issuer lease close");
            var lease = Assert.Single(h.Authority.Leases);
            ((TaskCompletionSource)Read(lease, "_originalClose")).SetException(cause);
            expected = cause; // Only this exact successfully injected original is deliberate.
            var error = await Record.ExceptionAsync(() => JoinInferenceControlOriginalsAsync(h, null, null));
            Assert.NotNull(error);
            Assert.Contains(Leaves(error!), actual => ReferenceEquals(actual, cause));
            Assert.True(((Task)Read(h.Runtime, "_close")).IsCompleted);
            Assert.True(lease.ActualClose.IsFaulted);
            Assert.Equal(1, lease.Disposes);
            Assert.Same(admission, ReadIssued(h.Coordinator, admission.AttemptId));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally { await JoinInferenceControlOriginalsAsync(h, expected, primary); }
    }

    private static async Task<TaskRunAttemptAdmission> MintInferenceAdmissionAsync(Harness h, CancellationToken token)
    {
        var begin = await h.BeginAsync(token);
        return await h.Coordinator.StartAttemptAsync(begin.TaskId, begin.ExecutionId, Route(), token);
    }

    private static async Task JoinInferenceControlOriginalsAsync(Harness h, Exception? expected, Exception? primary)
    {
        h.Rows.OverrideRead = null;
        var known = expected is null ? new HashSet<Exception>(ReferenceEqualityComparer.Instance)
            : new HashSet<Exception>(Leaves(expected), ReferenceEqualityComparer.Instance);
        var errors = new List<Exception>();
        Task? coordinatorClose = null, frameClose = null;
        // Acquire both actual owners before either join, even if an acquisition itself faults.
        try { coordinatorClose = h.Coordinator.CloseAndSuspendOriginalProducersAsync(); }
        catch (Exception cause) { errors.Add(cause); }
        try { frameClose = h.Runtime.CloseAndDrainAsync(); }
        catch (Exception cause) { errors.Add(cause); }
        if (coordinatorClose is not null)
        {
            await Join(coordinatorClose);
            try { Assert.True(coordinatorClose.IsCompleted); } catch (Exception cause) { errors.Add(cause); }
        }
        if (frameClose is not null)
        {
            await Join(frameClose);
            try { Assert.True(frameClose.IsCompleted); } catch (Exception cause) { errors.Add(cause); }
        }
        if (errors.Count != 0)
            throw new AggregateException("Unknown original cleanup failure; expected negative causes grant no healthy drain.",
                primary is null ? errors : new[] { primary }.Concat(errors));

        async Task Join(Task actual)
        {
            try { await actual.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
            catch (Exception cause)
            {
                // Preserve each whole raw fault group, and classify its leaves by exact source
                // reference only. Canceled Task exceptions may be new wrappers for SAME Task.
                var original = actual.IsFaulted && actual.Exception is { } group ? group : cause;
                if (!Leaves(original).All(IsKnown)) errors.Add(original);
            }
        }
        bool IsKnown(Exception cause) => known.Contains(cause) || cause is TaskCanceledException canceled
            && canceled.Task is { IsCanceled: true } && known.OfType<TaskCanceledException>().Any(prior =>
                ReferenceEquals(prior.Task, canceled.Task) && prior.CancellationToken == canceled.CancellationToken);
    }
}
