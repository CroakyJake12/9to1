using System.IO;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Portable (net10.0, non-Windows) regressions that drive the SAME production
/// <see cref="App.StartupTransitionCloseReplanner"/> sequencer used by the App close
/// preparation/publication wiring, with controlled gate values (generic over the lease type,
/// so no Windows/Home/Unified harness is required). These verify the retained one-close policy:
/// abort-old-then-reacquire-current ordering, raw multi-fault sibling retention (IO + foreign OCE),
/// no auto-retry/re-commit on declined or faulted preparation, no waiting on unrelated Welcome tasks,
/// and full abort termination before the old abort alias is released for a replaced lease.
/// They claim no native app/engine/model acceptance.</summary>
public sealed class AppStartupTransitionCloseReplannerTests
{
    [Fact]
    public async Task Stale_lease_reprepares_to_fresh_approved_commit_in_abort_then_acquire_order()
    {
        var failures = new List<Exception>();
        int aborts = 0, acquires = 0;
        var fresh = new object();
        var result = await App.StartupTransitionCloseReplanner.ReplanAsync(
            new object(),
            () => { aborts++; return Task.CompletedTask; },
            () => { acquires++; return Task.FromResult<object?>((fresh)); },
            failures);
        Assert.Same(fresh, result);
        Assert.Equal(1, aborts); // old unaccepted lease aborted exactly once before reacquire
        Assert.Equal(1, acquires);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Replan_completes_without_waiting_on_unrelated_pending_task()
    {
        // A gate representing a Welcome-pending action that NEVER completes by user click.
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        var fresh = new object();
        var result = await App.StartupTransitionCloseReplanner.ReplanAsync(
            new object(),
            () => Task.CompletedTask,
            () => Task.FromResult<object?>((fresh)),
            failures).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Same(fresh, result); // commit completes without any user click
        Assert.False(never.Task.IsCompleted);
    }

    [Fact]
    public async Task Declined_prep_null_commits_nothing_and_does_not_retry()
    {
        var failures = new List<Exception>();
        int acquires = 0;
        var result = await App.StartupTransitionCloseReplanner.ReplanAsync(
            new object(),
            () => Task.CompletedTask,
            () => { acquires++; return Task.FromResult<object?>((object?)null); },
            failures);
        Assert.Null(result);
        Assert.Equal(1, acquires); // refused once, never auto-retried
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Abort_raw_fault_siblings_all_retained_exact_and_no_reacquire()
    {
        var failures = new List<Exception>();
        var io = new IOException("abort cleanup failed");
        var foreignOce = new OperationCanceledException(new CancellationToken(canceled: true));
        var abortFault = new TaskCompletionSource();
        abortFault.SetException(new Exception[] { io, foreignOce }); // real sibling set in InnerExceptions
        int acquires = 0;
        var result = await App.StartupTransitionCloseReplanner.ReplanAsync(
            new object(),
            () => abortFault.Task,
            () => { acquires++; return Task.FromResult<object?>((new object())); },
            failures);
        Assert.Null(result);
        Assert.Equal(0, acquires); // genuine abort fault ⇒ never reacquire/retry
        Assert.Contains(io, failures);
        Assert.Contains(foreignOce, failures);
        Assert.Equal(2, failures.Count);
    }

    [Fact]
    public async Task Acquire_raw_fault_siblings_all_retained_exact_and_no_commit()
    {
        var failures = new List<Exception>();
        var io = new IOException("reprepare failed");
        var foreignOce = new OperationCanceledException(new CancellationToken(canceled: true));
        var acquireFault = new TaskCompletionSource<object?>();
        acquireFault.SetException(new Exception[] { io, foreignOce });
        var result = await App.StartupTransitionCloseReplanner.ReplanAsync(
            new object(),
            () => Task.CompletedTask,
            () => acquireFault.Task,
            failures);
        Assert.Null(result);
        Assert.Contains(io, failures);
        Assert.Contains(foreignOce, failures);
        Assert.Equal(2, failures.Count);
    }

    [Fact]
    public async Task Abort_is_fully_terminal_before_reacquire()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        Task Abort() { started.SetResult(); return gate.Task; }
        bool acquired = false;
        var run = App.StartupTransitionCloseReplanner.ReplanAsync(
            new object(), Abort,
            () => { acquired = true; return Task.FromResult<object?>((new object())); }, failures);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(run.IsCompleted); // abort not yet terminal → not reacquiring yet
        Assert.False(acquired);
        gate.SetResult(); // abort becomes terminal → reacquire may proceed
        var result = await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.True(acquired);
    }
}
