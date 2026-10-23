using Xunit;

namespace HavenOS.Apps.Canvas.NativeUI.Tests;

public sealed class CanvasSharedInkSubmissionTests
{
    [Fact]
    public async Task Drain_waits_for_a_later_success_and_retains_the_exact_earlier_failure()
    {
        var ledger = new CanvasSharedInkSubmissionLedger();
        var originalCause = new IOException("The first accepted stroke submission failed.");
        var firstOriginal = Task.FromException(originalCause);
        var laterCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var laterOriginal = laterCompletion.Task;
        ledger.Observe(firstOriginal);
        ledger.Observe(laterOriginal);

        Assert.Collection(ledger.Originals,
            original => Assert.Same(firstOriginal, original),
            original => Assert.Same(laterOriginal, original));
        var drain = ledger.DrainAsync();
        try
        {
            Assert.False(drain.IsCompleted);
            Assert.False(laterOriginal.IsCompleted);
        }
        finally { laterCompletion.TrySetResult(); }

        var failure = await Assert.ThrowsAsync<AggregateException>(() => drain);
        Assert.True(laterOriginal.IsCompletedSuccessfully);
        Assert.Same(originalCause, Assert.Single(failure.InnerExceptions));
        Assert.Collection(ledger.Originals,
            original => Assert.Same(firstOriginal, original),
            original => Assert.Same(laterOriginal, original));
    }

    [Fact]
    public async Task Drain_awaits_both_failed_originals_and_preserves_each_actual_cause()
    {
        var ledger = new CanvasSharedInkSubmissionLedger();
        var firstCause = new IOException("The first original submission failed.");
        var secondCause = new InvalidOperationException("The second original submission also failed.");
        var firstOriginal = Task.FromException(firstCause);
        var secondCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondOriginal = secondCompletion.Task;
        ledger.Observe(firstOriginal);
        ledger.Observe(secondOriginal);

        var drain = ledger.DrainAsync();
        try
        {
            Assert.False(drain.IsCompleted);
            Assert.False(secondOriginal.IsCompleted);
        }
        finally { secondCompletion.TrySetException(secondCause); }

        var failure = await Assert.ThrowsAsync<AggregateException>(() => drain);
        Assert.True(firstOriginal.IsFaulted);
        Assert.True(secondOriginal.IsFaulted);
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Contains(failure.InnerExceptions, cause => ReferenceEquals(cause, firstCause));
        Assert.Contains(failure.InnerExceptions, cause => ReferenceEquals(cause, secondCause));
        Assert.Collection(ledger.Originals,
            original => Assert.Same(firstOriginal, original),
            original => Assert.Same(secondOriginal, original));
    }
}
