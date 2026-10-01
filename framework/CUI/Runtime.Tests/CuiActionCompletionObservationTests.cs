using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

[Collection("CuiNativeBackend")]
public sealed class CuiActionCompletionObservationTests
{
    [Fact]
    public async Task Actual_click_snapshot_awaits_dispatcher_cleanup_after_disposal_and_retained_click_is_inert()
    {
        var token = TestContext.Current.CancellationToken;
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            var dispatcher = new CleanupAction();
            using var loader = new CuiControlLoader(); loader.SetActionDispatcher(dispatcher);
            var button = Assert.IsType<Button>(loader.Load(new CuiRichParser().Parse("<Cui><Button action=\"Wait\" content=\"Run\" /></Cui>")));
            loader.WireBindings(button);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var completion = loader.WhenActionsIdleAsync();
            await dispatcher.Entered.Task.WaitAsync(token);
            Assert.False(completion.IsCompleted);
            loader.Dispose();
            await dispatcher.Cancelled.Task.WaitAsync(token);
            Assert.False(completion.IsCompleted); // Cancellation observed is not the end of dispatcher cleanup.
            dispatcher.ReleaseCleanup.TrySetResult();
            await completion.WaitAsync(token);
            Assert.True(dispatcher.CleanupFinished);
            Assert.Equal(1, dispatcher.Calls);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync().WaitAsync(token);
            Assert.Equal(1, dispatcher.Calls);
            return true;
        }, token);
    }
    [Fact]
    public async Task Snapshot_does_not_dispatch_or_wait_for_a_future_click()
    {
        var token = TestContext.Current.CancellationToken;
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            var dispatcher = new CleanupAction();
            using var loader = new CuiControlLoader(); loader.SetActionDispatcher(dispatcher);
            var button = Assert.IsType<Button>(loader.Load(new CuiRichParser().Parse("<Cui><Button action=\"Wait\" content=\"Run\" /></Cui>")));
            loader.WireBindings(button);
            var before = loader.WhenActionsIdleAsync(); await before.WaitAsync(token); Assert.Equal(0, dispatcher.Calls);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var accepted = loader.WhenActionsIdleAsync(); await dispatcher.Entered.Task.WaitAsync(token);
            Assert.True(before.IsCompleted); Assert.False(accepted.IsCompleted);
            loader.Dispose(); dispatcher.ReleaseCleanup.TrySetResult(); await accepted.WaitAsync(token);
            Assert.Equal(1, dispatcher.Calls);
            return true;
        }, token);
    }
    private sealed class CleanupAction : ICuiActionDispatcher
    {
        public int Calls; public bool CleanupFinished;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReleaseCleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
        {
            Calls++; Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
            finally { await ReleaseCleanup.Task; CleanupFinished = true; }
        }
    }
}
