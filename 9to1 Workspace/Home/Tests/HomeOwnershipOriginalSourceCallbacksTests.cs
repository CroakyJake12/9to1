using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeOwnershipOriginalSourceCallbacksTests
{
    [Fact]
    public async Task Successful_validation_reclamation_does_not_discard_pending_or_failed_children()
    {
        var held = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cause = new IOException("Original retained validation failed.");
        var failed = Task.FromException<string>(cause);
        var callbacks = new HomeOwnershipOriginalSourceCallbacks(body => body(), _ => { });
        callbacks.Retain(held.Task); callbacks.Retain(failed);
        for (var index = 0; index < 256; index++) callbacks.Retain(Task.FromResult(index));
        Assert.False(callbacks.HasIndependentlyJoinedHealthySources);
        var read = callbacks.ReadAsync(() => Task.FromResult("current"));
        Assert.False(read.IsCompleted);
        held.SetResult("released");
        var observed = await Assert.ThrowsAsync<AggregateException>(() => read);
        Assert.Contains(cause, observed.InnerExceptions);
        Assert.False(callbacks.HasIndependentlyJoinedHealthySources);
        var repeated = await Assert.ThrowsAsync<AggregateException>(() => callbacks.ReadAsync(() => Task.FromResult("later")));
        Assert.Contains(cause, repeated.InnerExceptions);
    }

    [Fact]
    public async Task Actual_canceled_child_and_independent_scope_OCE_remain_faulted_with_each_original_occurrence()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var held = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var foreign = new OperationCanceledException("Independent scope post-callback refusal.");
        var originalRetained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retained = new List<Task>();
        var callbacks = new HomeOwnershipOriginalSourceCallbacks(body => { body(); throw foreign; }, raw =>
        {
            lock (retained) retained.Add(raw);
            if (ReferenceEquals(raw, held.Task)) originalRetained.TrySetResult();
        });
        var actual = callbacks.ReadAsync(() => held.Task);
        Exception? expectedOriginalFailure = null;
        try
        {
            await originalRetained.Task;
            Assert.False(actual.IsCompleted);
            held.TrySetCanceled(canceled.Token);
            var cause = await Record.ExceptionAsync(() => actual);
            expectedOriginalFailure = cause;
            Assert.True(actual.IsFaulted);
            var aggregate = Assert.IsType<AggregateException>(cause);
            Assert.Contains(aggregate.InnerExceptions, value => ReferenceEquals(value, foreign));
            Assert.Contains(aggregate.InnerExceptions, value => value is TaskCanceledException taskCanceled &&
                ReferenceEquals(taskCanceled.Task, held.Task) && taskCanceled.CancellationToken == canceled.Token);
            Assert.Contains(retained, value => ReferenceEquals(value, held.Task));
        }
        finally
        {
            held.TrySetCanceled(canceled.Token);
            try { await held.Task; } catch (TaskCanceledException cause) when (ReferenceEquals(cause.Task, held.Task)) { }
            // Keep the accepted complete body terminal even if an assertion above failed.
            try { await actual; }
            catch (Exception cause) when (ReferenceEquals(cause, expectedOriginalFailure) &&
                actual.Exception is { InnerExceptions.Count: 1 } raw && ReferenceEquals(raw.InnerExceptions[0], cause)) { }
        }
    }
}
