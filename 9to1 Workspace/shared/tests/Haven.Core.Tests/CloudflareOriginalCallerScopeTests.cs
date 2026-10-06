using Haven.Application;
using Xunit;
namespace Haven.Core.Tests;

public sealed class CloudflareOriginalCallerScopeTests
{
    [Fact] public void Returned_actual_Task_is_retained_before_a_post_callback_scope_failure()
    {
        var stages = new CloudflareOriginalTaskLedger(); var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalCause = new IOException("scope after actual Task factory");
        stages.BindOriginalCallerCallback(body => { body(); throw originalCause; });
        Assert.Same(originalCause, Assert.Throws<IOException>(() => stages.Invoke(() => returned.Task)));
        Assert.Same(returned.Task, Assert.Single(stages.OriginalTasks)); Assert.Contains(originalCause, stages.OriginalErrors);
        returned.SetResult(); Assert.True(returned.Task.IsCompletedSuccessfully);
    }
    [Fact] public void Synchronous_cancel_then_OCE_remains_fault_evidence_with_exact_cause()
    {
        var stages = new CloudflareOriginalTaskLedger(); using var token = new CancellationTokenSource();
        var original = new OperationCanceledException("independent synchronous factory", token.Token);
        stages.BindOriginalCallerCallback(body => body());
        var error = Assert.Throws<AggregateException>(() => stages.Invoke<Task>(() => { token.Cancel(); throw original; }));
        Assert.Contains(original, error.InnerExceptions); Assert.Contains(original, stages.OriginalErrors); Assert.Empty(stages.OriginalTasks);
    }
    [Fact] public async Task Faulted_actual_Task_keeps_all_direct_siblings_under_same_caller_scope()
    {
        var stages = new CloudflareOriginalTaskLedger(); stages.BindOriginalCallerCallback(body => body());
        var first = new OperationCanceledException("faulted OCE"); var second = new IOException("sibling");
        var actual = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously); actual.SetException([first, second]);
        var observed = stages.AwaitAsync(stages.Invoke(() => actual.Task));
        var group = await Assert.ThrowsAsync<AggregateException>(() => observed);
        Assert.True(observed.IsFaulted); Assert.Same(actual.Task, Assert.Single(stages.OriginalTasks));
        Assert.Contains(first, group.InnerExceptions); Assert.Contains(second, group.InnerExceptions);
    }
    [Fact] public async Task Failed_scope_driver_joins_the_held_actual_returned_Task_before_terminal_fault()
    {
        var stages = new CloudflareOriginalTaskLedger(); var returned = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cause = new IOException("failed scope after acquisition"); stages.BindOriginalCallerCallback(body => { body(); throw cause; });
        var actual = stages.RunToOriginalSettlementAsync(() => returned.Task);
        try { Assert.False(actual.IsCompleted); Assert.Contains(returned.Task, stages.OriginalTasks); }
        finally { returned.TrySetResult(42); }
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.True(actual.IsFaulted); Assert.Contains(cause, error.InnerExceptions); Assert.True(returned.Task.IsCompletedSuccessfully);
    }
    [Fact] public async Task Actual_late_resource_returned_before_scope_failure_is_retained_and_closed_once()
    {
        var ledger = new CloudflareOriginalTaskLedger(); var returned = new TaskCompletionSource<LateResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cause = new IOException("scope failed after resource factory returned"); var resource = new LateResource(); LateResource? captured = null;
        ledger.BindOriginalCallerCallback(body => { body(); throw cause; });
        var actual = ledger.CaptureOriginalAcquisitionAsync(() => returned.Task, same => captured = same);
        try { Assert.False(actual.IsCompleted); Assert.Contains(returned.Task, ledger.OriginalTasks); }
        finally { returned.TrySetResult(resource); }
        try { var error = await Assert.ThrowsAsync<AggregateException>(() => actual); Assert.Contains(cause, error.InnerExceptions); Assert.Same(resource, captured); }
        finally { if (captured is not null) await captured.DisposeAsync(); }
        Assert.True(returned.Task.IsCompletedSuccessfully); Assert.Equal(1, resource.Closes);
    }
    private sealed class LateResource : IAsyncDisposable
    { internal int Closes; public ValueTask DisposeAsync() { Closes++; return ValueTask.CompletedTask; } }
    [Fact] public async Task Actual_close_returned_before_scope_failure_is_joined_before_parent_fault()
    {
        var ledger = new CloudflareOriginalTaskLedger(); var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cause = new IOException("scope after original close start"); ledger.BindOriginalCallerCallback(body => { body(); throw cause; });
        var actual = ledger.ObserveOriginalCloseAsync(() => new ValueTask(raw.Task));
        try { Assert.False(actual.IsCompleted); Assert.Same(raw.Task, Assert.Single(ledger.OriginalTasks)); }
        finally { raw.TrySetResult(); }
        var error = await Assert.ThrowsAsync<AggregateException>(() => actual); Assert.Contains(cause, error.InnerExceptions);
        Assert.True(raw.Task.IsCompletedSuccessfully); Assert.True(actual.IsFaulted);
    }
    [Fact] public async Task Exact_partial_close_list_survives_post_scope_failure_and_both_real_closes_run()
    {
        var ledger = new CloudflareOriginalTaskLedger(); var first = new LateResource(); var second = new LateResource();
        var bodyCause = new IOException("actual body after partial acquisition"); var gatheringCause = new IOException("scope after close-list callback"); int calls = 0;
        ledger.BindOriginalCallerCallback(body => { body(); if (++calls == 2) throw gatheringCause; });
        var actual = CloudflareOriginalPartialEntryCustody.RunOriginalAsync<object>(ledger, () => Task.FromException<object>(bodyCause),
            () => new Func<ValueTask>[] { first.DisposeAsync, second.DisposeAsync });
        await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.True(actual.IsFaulted); Assert.Equal(1, first.Closes); Assert.Equal(1, second.Closes);
        Assert.Contains(bodyCause, ledger.OriginalErrors); Assert.Contains(gatheringCause, ledger.OriginalErrors);
        Assert.All(ledger.OriginalTasks, task => Assert.True(task.IsCompleted));
    }
}
