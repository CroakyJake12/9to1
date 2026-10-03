using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ChatOriginalEtaDrainTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Original_ETA_callback_remains_owned_until_same_cleanup_settles_and_cancel_failure_survives(bool throwingCancellation)
    {
        // Exercise the actual maintained visibility and one-minute ETA timers. No substituted
        // timer, reflection field, fabricated completed task or synchronous dispatcher wait.
        using var timeout = new CancellationTokenSource(ChatExecutionTracker.EtaDelay + TimeSpan.FromSeconds(60));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actualCancellationFailure = new InvalidOperationException("synthetic registered cancellation callback failure");
        var originalSettled = false;
        var tracker = new ChatExecutionTracker(etaProvider: async (_, token) =>
        {
            using var registration = throwingCancellation ? token.Register(() => throw actualCancellationFailure) : default;
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); return "1 minute"; }
            finally { await released.Task.WaitAsync(timeout.Token); originalSettled = true; }
        });
        Exception? primary = null; Exception? expectedShutdownFailure = null; Task? originalClose = null; Task? reenteredClose = null;
        try
        {
            await entered.Task.WaitAsync(ChatExecutionTracker.EtaDelay + TimeSpan.FromSeconds(15), timeout.Token);
            tracker.Changed += snapshot => { if (snapshot.Stage == ChatExecutionStage.Cancelled) reenteredClose = tracker.DisposeAsync().AsTask(); };
            var close = tracker.DisposeAsync().AsTask(); originalClose = close;
            if (!throwingCancellation) Assert.Same(close, reenteredClose);
            Assert.Same(originalClose, tracker.DisposeAsync().AsTask());
            Assert.False(originalClose.IsCompleted); Assert.False(originalSettled);
            released.TrySetResult();
            if (throwingCancellation)
            {
                var failure = await Assert.ThrowsAsync<AggregateException>(() => close);
                expectedShutdownFailure = failure;
                Assert.Contains(failure.Flatten().InnerExceptions, item => ReferenceEquals(item, actualCancellationFailure));
            }
            else await originalClose.WaitAsync(TimeSpan.FromSeconds(20), timeout.Token);
            Assert.True(originalSettled);
            Assert.Same(originalClose, tracker.DisposeAsync().AsTask());
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            released.TrySetResult();
            originalClose ??= tracker.DisposeAsync().AsTask();
            try { await originalClose.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (Exception failure) when (ReferenceEquals(failure, primary) || ReferenceEquals(failure, expectedShutdownFailure)) { }
            catch (Exception cleanup)
            {
                if (primary is not null) throw new AggregateException(primary, cleanup);
                throw;
            }
        }
    }
}
