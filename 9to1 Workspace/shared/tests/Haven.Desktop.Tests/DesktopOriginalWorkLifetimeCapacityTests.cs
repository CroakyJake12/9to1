using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class DesktopOriginalWorkLifetimeCapacityTests
{
    [Fact]
    public async Task Recorded_full_cohort_refusal_stays_exact_after_held_healthy_original_settles()
    {
        var owner = new DesktopOriginalWorkLifetime(() => Task.CompletedTask, () => Task.CompletedTask);
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = owner.RunAsync(original => original.AwaitAsync(raw.Task));
        var faults = new List<Task>();
        var causes = new List<Exception>();
        var callbacks = 0;
        try
        {
            for (var index = 0; index < 127; ++index)
            {
                var cause = new IOException("Original retained fault " + index);
                causes.Add(cause);
                var actual = owner.RunAsync(_ => Task.FromException(cause));
                faults.Add(actual);
                Assert.Same(cause, await Assert.ThrowsAsync<IOException>(() => actual));
            }
            var refusal = Assert.Throws<InvalidOperationException>(() =>
            { _ = owner.RunAsync(_ => { ++callbacks; return Task.CompletedTask; }); });
            Assert.False(held.IsCompleted);
            Assert.Equal(0, callbacks);
            raw.SetResult();
            await held;
            Assert.True(held.IsCompletedSuccessfully);
            Assert.False(owner.IsRetiring);
            Assert.Same(refusal, Assert.Throws<InvalidOperationException>(() =>
            { _ = owner.RunAsync(_ => { ++callbacks; return Task.CompletedTask; }); }));
            Assert.Same(refusal, Assert.Throws<InvalidOperationException>(() =>
            { _ = owner.RunAsync<int>(_ => { ++callbacks; return Task.FromResult(1); }); }));
            Assert.Same(refusal, Assert.Throws<InvalidOperationException>(() =>
                owner.RunSynchronous(_ => ++callbacks)));
            Assert.Equal(0, callbacks);
            var close = owner.CloseAndDrainAsync();
            Assert.Same(close, owner.CloseAndDrainAsync());
            var terminal = await Assert.ThrowsAsync<AggregateException>(() => close);
            Assert.Equal(128, terminal.InnerExceptions.Count);
            for (var index = 0; index < causes.Count; ++index)
            {
                Assert.True(faults[index].IsFaulted);
                Assert.Same(causes[index], Assert.Single(faults[index].Exception!.InnerExceptions));
                Assert.Same(causes[index], terminal.InnerExceptions[index]);
            }
            Assert.Same(refusal, terminal.InnerExceptions[127]);
            Assert.Same(close, owner.OriginalClose);
        }
        finally
        {
            raw.TrySetResult();
            await held;
            // The exact fault originals above remain asserted; this independently
            // observes close on an early assertion failure without stranding work.
            _ = await Record.ExceptionAsync(() => owner.CloseAndDrainAsync());
        }
    }
}
