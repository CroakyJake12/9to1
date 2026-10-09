using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeOwnershipOriginalSourceCallbacksTests
{
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
