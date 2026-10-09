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
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static IEnumerable<Exception> Leaves(Exception error)
        => error is AggregateException group ? group.InnerExceptions.SelectMany(Leaves) : [error];
}
