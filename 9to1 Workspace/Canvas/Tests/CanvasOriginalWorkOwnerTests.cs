using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

/// <summary>Controlled original Tasks test local callback custody only; no Home actor,
/// business acceptance, Files commit, native engine or package readiness is supplied.</summary>
public sealed class CanvasOriginalWorkOwnerTests
{
    [Fact]
    public async Task Same_close_task_waits_held_original_finally_and_runs_cleanup_after_release()
    {
        var owner = new CanvasOriginalWorkOwner();
        var entered = NewSignal(); var finallyEntered = NewSignal(); var release = NewSignal();
        var cleaned = false; Task? reentered = null;
        var original = owner.RunOriginalAsync(async token =>
        {
            using var registration = token.Register(() => reentered = owner.CloseAndDrainAsync());
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { finallyEntered.SetResult(); await release.Task; }
        });
        Task? close = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            close = owner.CloseAndDrainAsync(() => { cleaned = true; return Task.CompletedTask; });
            await finallyEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(close, owner.CloseAndDrainAsync()); Assert.Same(close, reentered);
            Assert.False(original.IsCompleted); Assert.False(close.IsCompleted); Assert.False(cleaned);
            Assert.Throws<ObjectDisposedException>(() => { _ = owner.RunOriginalAsync(_ => Task.CompletedTask); });
        }
        finally { release.TrySetResult(); }
        await Record.ExceptionAsync(() => original);
        Assert.NotNull(await Record.ExceptionAsync(() => close!));
        Assert.True(original.IsCanceled); Assert.True(cleaned); Assert.True(close!.IsCompleted);
    }

    [Fact]
    public async Task Faulted_original_OCE_and_sibling_survive_independent_faulted_cleanup()
    {
        var owner = new CanvasOriginalWorkOwner();
        var canceledObservation = new OperationCanceledException("faulted original; not an actual canceled Task");
        var sibling = new IOException("same original sibling"); var cleanup = new IOException("independent cleanup");
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = NewSignal(); var cleanupCalls = 0;
        var original = owner.RunOriginalAsync(_ => { entered.SetResult(); return raw.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var close = owner.CloseAndDrainAsync(() => { cleanupCalls++; return Task.FromException(cleanup); });
        try { Assert.False(close.IsCompleted); }
        finally { raw.TrySetException([canceledObservation, sibling]); }
        var sourceFailure = await Record.ExceptionAsync(() => original);
        var closeFailure = await Record.ExceptionAsync(() => close);
        Assert.True(raw.Task.IsFaulted); Assert.True(original.IsFaulted); Assert.True(close.IsFaulted);
        Assert.Contains(Leaves(sourceFailure!), value => ReferenceEquals(value, canceledObservation));
        Assert.Contains(Leaves(sourceFailure!), value => ReferenceEquals(value, sibling));
        Assert.Contains(Leaves(closeFailure!), value => ReferenceEquals(value, canceledObservation));
        Assert.Contains(Leaves(closeFailure!), value => ReferenceEquals(value, sibling));
        Assert.Contains(Leaves(closeFailure!), value => ReferenceEquals(value, cleanup)); Assert.Equal(1, cleanupCalls);
        Assert.Same(close, owner.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Successful_original_result_remains_joined_and_cleanup_runs_once()
    {
        var owner = new CanvasOriginalWorkOwner(); var raw = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = NewSignal(); var cleanupCalls = 0;
        var original = owner.RunOriginalAsync(_ => { entered.SetResult(); return raw.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var close = owner.CloseAndDrainAsync(() => { cleanupCalls++; return Task.CompletedTask; });
        try { Assert.False(close.IsCompleted); }
        finally { raw.TrySetResult(7); }
        Assert.Equal(7, await original); await close;
        Assert.Equal(1, cleanupCalls); Assert.Same(close, owner.OriginalCloseTask);
        Assert.Throws<ObjectDisposedException>(() => { _ = owner.RunOriginalAsync(_ => Task.FromResult(9)); });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Foreign_empty_or_nested_aggregate_keeps_its_identity_and_close_faulted(bool nested)
    {
        var empty = new AggregateException("Foreign empty source fault.");
        var source = nested ? new AggregateException("Foreign nested source fault.", empty, new IOException("Nested sibling.")) : empty;
        var raw = Task.FromException(source);
        var owner = new CanvasOriginalWorkOwner();
        var original = owner.RunOriginalAsync(_ => raw);
        var originalFailure = await Record.ExceptionAsync(() => original);
        Assert.True(original.IsFaulted);
        Assert.True(ContainsSameCause(originalFailure!, source));
        var cleanupCalls = 0;
        var close = owner.CloseAndDrainAsync(() => { ++cleanupCalls; return Task.CompletedTask; });
        var closeFailure = await Record.ExceptionAsync(() => close);
        Assert.True(close.IsFaulted);
        Assert.Same(source, closeFailure);
        Assert.Equal(1, cleanupCalls);
        Assert.Same(close, owner.CloseAndDrainAsync());
        Assert.Same(source, await Record.ExceptionAsync(() => close));
    }

    [Fact]
    public async Task Foreign_empty_independent_cleanup_fault_cannot_become_a_successful_close()
    {
        var source = new AggregateException("Foreign empty independent cleanup fault.");
        var owner = new CanvasOriginalWorkOwner();
        await owner.RunOriginalAsync(_ => Task.CompletedTask);
        var close = owner.CloseAndDrainAsync(() => Task.FromException(source));
        Assert.Same(source, await Record.ExceptionAsync(() => close));
        Assert.True(close.IsFaulted);
        Assert.Same(close, owner.OriginalCloseTask);
    }
    [Fact]
    public async Task Issued_task_snapshot_is_read_only_and_rejects_a_foreign_task_with_the_same_cause()
    {
        var cause = new IOException("Exact source failure.");
        var owner = new CanvasOriginalWorkOwner();
        var original = owner.RunOriginalAsync(_ => Task.FromException(cause));
        await Record.ExceptionAsync(() => original);
        var foreign = Task.FromException(cause);
        var snapshot = owner.RetainedOriginalTasks;
        Assert.Same(original, Assert.Single(snapshot));
        Assert.True(owner.OwnsOriginalTask(original)); Assert.False(owner.OwnsOriginalTask(foreign));
        Assert.Throws<NotSupportedException>(() => ((IList<Task>)snapshot).Add(foreign));
        await owner.RunOriginalAsync(_ => Task.CompletedTask);
        Assert.Same(original, Assert.Single(snapshot)); // the same retained immutable task snapshot
        var close = owner.CloseAndDrainAsync();
        Assert.Same(cause, await Record.ExceptionAsync(() => close));
        Assert.True(close.IsFaulted); Assert.True(original.IsFaulted); Assert.Same(close, owner.OriginalCloseTask);
    }

    [Fact]
    public async Task Failure_query_recognises_only_the_captured_wrapper_and_returns_immutable_direct_causes()
    {
        var owner = new CanvasOriginalWorkOwner(); var cause = new IOException("Observed source.");
        var foreignTask = Task.FromException(cause);
        var observed = await Record.ExceptionAsync(() => owner.ObserveOriginalAsync(foreignTask));
        Assert.NotNull(observed); Assert.False(owner.OwnsOriginalTask(foreignTask));
        Assert.True(owner.TryGetOwnedFailureCauses(observed!, out var children));
        Assert.Same(cause, Assert.Single(children));
        Assert.Throws<NotSupportedException>(() => ((IList<Exception>)children)[0] = new IOException("Clone."));
        Assert.False(owner.TryGetOwnedFailureCauses(new AggregateException(cause), out var clone)); Assert.Empty(clone);
        Assert.False(owner.TryGetOwnedFailureCauses(new AggregateException(), out _));
        Assert.False(owner.TryGetOwnedFailureCauses(cause, out _));
        var close = owner.CloseAndDrainAsync();
        Assert.Same(cause, await Record.ExceptionAsync(() => close)); Assert.True(close.IsFaulted);
    }

    [Fact]
    public async Task Owned_group_preserves_foreign_empty_and_nested_payloads_without_blessing_their_children()
    {
        var owner = new CanvasOriginalWorkOwner();
        var opaque = new AggregateException("Foreign opaque payload.", new AggregateException("Empty foreign cause."));
        var sibling = new IOException("Independent source.");
        var raw = Task.WhenAll(Task.FromException(opaque), Task.FromException(sibling));
        var observed = await Record.ExceptionAsync(() => owner.ObserveOriginalAsync(raw));
        Assert.True(owner.TryGetOwnedFailureCauses(observed!, out var children)); Assert.Equal(2, children.Count);
        Assert.Contains(children, error => ReferenceEquals(error, opaque));
        Assert.Contains(children, error => ReferenceEquals(error, sibling));
        Assert.False(owner.TryGetOwnedFailureCauses(opaque, out _));
        Assert.False(owner.TryGetOwnedFailureCauses(opaque.InnerException!, out _));
        var close = owner.CloseAndDrainAsync(); var failure = await Record.ExceptionAsync(() => close);
        Assert.True(owner.TryGetOwnedFailureCauses(failure!, out var closed));
        Assert.Contains(closed, error => ReferenceEquals(error, opaque));
        Assert.Contains(closed, error => ReferenceEquals(error, sibling));
        Assert.True(close.IsFaulted);
    }

    [Fact]
    public async Task Two_issued_tasks_rethrowing_one_cause_remain_distinct_after_strict_close_deduplicates_the_cause()
    {
        var owner = new CanvasOriginalWorkOwner(); var cause = new IOException("One reused cause.");
        var first = owner.RunOriginalAsync(_ => Task.FromException(cause));
        var second = owner.RunOriginalAsync(_ => Task.FromException(cause));
        await Record.ExceptionAsync(() => first); await Record.ExceptionAsync(() => second);
        var snapshot = owner.RetainedOriginalTasks;
        Assert.Equal(2, snapshot.Count); Assert.NotSame(first, second);
        Assert.Contains(snapshot, task => ReferenceEquals(task, first)); Assert.Contains(snapshot, task => ReferenceEquals(task, second));
        var close = owner.CloseAndDrainAsync();
        Assert.Same(cause, await Record.ExceptionAsync(() => close)); Assert.True(close.IsFaulted);
        Assert.True(first.IsFaulted); Assert.True(second.IsFaulted);
        // A caller must validate EACH exact task; a deduplicated cause is not enough.
    }

    [Fact]
    public async Task Independent_cleanup_rethrowing_the_source_cause_remains_a_faulted_cleanup_and_close()
    {
        var owner = new CanvasOriginalWorkOwner(); var cause = new IOException("Reused cleanup cause.");
        var original = owner.RunOriginalAsync(_ => Task.FromException(cause));
        await Record.ExceptionAsync(() => original);
        var actualCleanup = Task.FromException(cause);
        var close = owner.CloseAndDrainAsync(() => actualCleanup);
        Assert.Same(cause, await Record.ExceptionAsync(() => close));
        Assert.True(actualCleanup.IsFaulted); Assert.True(close.IsFaulted); Assert.Same(close, owner.OriginalCloseTask);
        Assert.False(owner.OwnsOriginalTask(actualCleanup));
    }

    [Fact]
    public async Task Swallowed_private_cancellation_fault_stays_opaque_and_joins_original_and_cleanup()
    {
        var owner = new CanvasOriginalWorkOwner();
        var callbackCause = new IOException("Actual private-token cancellation callback.");
        var foreign = new AggregateException("Foreign cancellation payload.", new AggregateException(), callbackCause);
        var cleanupCause = new IOException("Independent cleanup after swallowed cancellation.");
        using var registration = owner.Token.Register(() => throw foreign);
        var entered = NewSignal(); var raw = NewSignal(); var canceled = NewSignal();
        var cleanupCalls = 0; Exception? swallowed = null; Exception? failure = null;
        Exception? originalFailure = null; Task? close = null;
        var original = owner.RunOriginalAsync(_ => { entered.SetResult(); return raw.Task; });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            close = owner.CloseAndDrainAsync(
                () => { ++cleanupCalls; return Task.FromException(cleanupCause); },
                cancel =>
                {
                    try { cancel(); } catch (Exception error) { swallowed = error; }
                    finally { canceled.SetResult(); }
                });
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(original.IsCompleted); Assert.False(close.IsCompleted); Assert.Equal(0, cleanupCalls);
            Assert.Same(close, owner.CloseAndDrainAsync());
            Assert.True(owner.Token.IsCancellationRequested);
        }
        finally
        {
            raw.TrySetResult();
            originalFailure = await Record.ExceptionAsync(() => original.WaitAsync(TimeSpan.FromSeconds(10)));
            failure = await Record.ExceptionAsync(() => (close ?? owner.CloseAndDrainAsync()).WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Assert.Null(originalFailure); Assert.True(original.IsCompletedSuccessfully); Assert.Equal(1, cleanupCalls);
        var cancellation = Assert.IsType<AggregateException>(swallowed);
        Assert.Same(foreign, Assert.Single(cancellation.InnerExceptions));
        Assert.True(owner.TryGetOwnedFailureCauses(failure!, out var causes)); Assert.Equal(2, causes.Count);
        Assert.Contains(causes, error => ReferenceEquals(error, cancellation));
        Assert.Contains(causes, error => ReferenceEquals(error, cleanupCause));
        Assert.False(owner.TryGetOwnedFailureCauses(cancellation, out _));
        Assert.False(owner.TryGetOwnedFailureCauses(foreign, out _));
        Assert.Same(callbackCause, foreign.InnerExceptions[1]);
        Assert.True(close!.IsFaulted); Assert.Same(close, owner.OriginalCloseTask);
    }

    [Fact]
    public async Task Swallowed_repeat_cancellation_refusals_still_fault_the_same_joined_close()
    {
        var owner = new CanvasOriginalWorkOwner();
        var entered = NewSignal(); var raw = NewSignal(); var attempted = NewSignal();
        var cleanupCalls = 0; Exception? firstRefusal = null; Exception? secondRefusal = null;
        Exception? originalFailure = null; Exception? failure = null; Task? close = null;
        var original = owner.RunOriginalAsync(_ => { entered.SetResult(); return raw.Task; });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            close = owner.CloseAndDrainAsync(
                () => { ++cleanupCalls; return Task.CompletedTask; },
                cancel =>
                {
                    cancel();
                    try { cancel(); } catch (Exception error) { firstRefusal = error; }
                    try { cancel(); } catch (Exception error) { secondRefusal = error; }
                    attempted.SetResult();
                });
            await attempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(original.IsCompleted); Assert.False(close.IsCompleted); Assert.Equal(0, cleanupCalls);
            Assert.Same(close, owner.CloseAndDrainAsync());
            Assert.Throws<ObjectDisposedException>(() => { _ = owner.RunOriginalAsync(_ => Task.CompletedTask); });
        }
        finally
        {
            raw.TrySetResult();
            originalFailure = await Record.ExceptionAsync(() => original.WaitAsync(TimeSpan.FromSeconds(10)));
            failure = await Record.ExceptionAsync(() => (close ?? owner.CloseAndDrainAsync()).WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Assert.Null(originalFailure); Assert.True(original.IsCompletedSuccessfully); Assert.Equal(1, cleanupCalls);
        Assert.IsType<InvalidOperationException>(firstRefusal);
        Assert.Same(firstRefusal, secondRefusal); Assert.Same(firstRefusal, failure);
        Assert.True(close!.IsFaulted); Assert.Same(close, owner.OriginalCloseTask);
    }

    [Fact]
    public async Task Missing_cancellation_invocation_joins_original_and_independent_cleanup_before_refusal()
    {
        var owner = new CanvasOriginalWorkOwner(); var token = owner.Token;
        var entered = NewSignal(); var raw = NewSignal(); var sourceReturned = NewSignal();
        var cleanupEntered = NewSignal(); var cleanupRelease = NewSignal();
        var cleanupCalls = 0; Task? close = null; Exception? originalFailure = null; Exception? failure = null;
        var original = owner.RunOriginalAsync(_ => { entered.SetResult(); return raw.Task; });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            close = owner.CloseAndDrainAsync(
                () => { ++cleanupCalls; cleanupEntered.SetResult(); return cleanupRelease.Task; },
                _ => sourceReturned.SetResult());
            await sourceReturned.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(token.IsCancellationRequested); Assert.False(close.IsCompleted);
            Assert.False(original.IsCompleted); Assert.Equal(0, cleanupCalls);
            raw.SetResult();
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(close.IsCompleted); Assert.Same(close, owner.CloseAndDrainAsync());
            Assert.Throws<ObjectDisposedException>(() => { _ = owner.RunOriginalAsync(_ => Task.CompletedTask); });
        }
        finally
        {
            raw.TrySetResult(); cleanupRelease.TrySetResult();
            originalFailure = await Record.ExceptionAsync(() => original.WaitAsync(TimeSpan.FromSeconds(10)));
            failure = await Record.ExceptionAsync(() => (close ?? owner.CloseAndDrainAsync()).WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Assert.Null(originalFailure); Assert.True(original.IsCompletedSuccessfully); Assert.Equal(1, cleanupCalls);
        var refusal = Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal("The original Canvas host did not invoke actual cancellation.", refusal.Message);
        Assert.False(token.IsCancellationRequested); Assert.True(close!.IsFaulted);
        Assert.Same(close, owner.OriginalCloseTask);
    }

    private static bool ContainsSameCause(Exception actual, Exception sought)
        => ReferenceEquals(actual, sought) || actual is AggregateException group && group.InnerExceptions.Any(child => ContainsSameCause(child, sought));
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static IEnumerable<Exception> Leaves(Exception error)
        => error is AggregateException group ? group.InnerExceptions.SelectMany(Leaves) : [error];
}
