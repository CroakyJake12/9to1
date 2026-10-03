using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

[Collection("CuiNativeBackend")]
public sealed class CuiSceneActionCompletionObservationTests
{
    [Fact]
    public async Task Uninitialized_and_disposed_scene_refuse_new_completion_snapshot()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            using var host = new CuiSceneHost();
            Assert.Throws<InvalidOperationException>(() => { _ = host.WhenActionsIdleAsync(); });
            var model = new CuiViewModel();
            await host.ShowAsync(Scene(model, model), timeout.Token);
            var originalEmptySnapshot = host.WhenActionsIdleAsync();
            Assert.True(originalEmptySnapshot.IsCompleted);
            await originalEmptySnapshot.WaitAsync(timeout.Token);
            host.Dispose();
            Assert.Throws<ObjectDisposedException>(() => { _ = host.WhenActionsIdleAsync(); });
            Assert.True(originalEmptySnapshot.IsCompleted);
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Captured_original_scene_action_waits_for_cleanup_after_disposal_or_replacement(bool replace)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await native.Dispatch<bool>(async () =>
        {
            var host = new CuiSceneHost();
            var action = new HeldCleanupAction();
            Task? originalSnapshot = null;
            Exception? primary = null;
            try
            {
                var model = new CuiViewModel();
                await host.ShowAsync(Scene(model, action), token);
                var button = Assert.IsType<Button>(host.Content);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                originalSnapshot = host.WhenActionsIdleAsync();
                await action.Entered.Task.WaitAsync(token);
                Assert.Equal(1, action.Calls);
                Assert.False(originalSnapshot.IsCompleted);
                if (replace)
                {
                    var next = new CuiViewModel();
                    await host.ShowAsync(Scene(next, next), token);
                    var newSceneSnapshot = host.WhenActionsIdleAsync();
                    await newSceneSnapshot.WaitAsync(token);
                    Assert.True(newSceneSnapshot.IsCompleted);
                }
                else
                {
                    host.Dispose();
                    Assert.Throws<ObjectDisposedException>(() => { _ = host.WhenActionsIdleAsync(); });
                }
                await action.Cancelled.Task.WaitAsync(token);
                Assert.False(originalSnapshot.IsCompleted);
                Assert.False(action.CleanupFinished);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, action.Calls);
                action.ReleaseCleanup.TrySetResult();
                await originalSnapshot.WaitAsync(token);
                Assert.True(action.CleanupFinished);
                Assert.Equal(1, action.Calls);
                Assert.NotNull(action.OriginalCancellation);
                Assert.Equal(action.OriginalToken, action.OriginalCancellation.CancellationToken);
                Assert.True(action.OriginalToken.IsCancellationRequested);
            }
            catch (Exception error) { primary = error; }

            var cleanup = new List<Exception>();
            try { host.Dispose(); } catch (Exception error) { Add(cleanup, error, primary); }
            try { action.ReleaseCleanup.TrySetResult(); } catch (Exception error) { Add(cleanup, error, primary); }
            if (originalSnapshot is not null)
                try { await originalSnapshot.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (Exception error) { Add(cleanup, error, primary); }
            if (action.OriginalDispatch is not null)
                try { await action.OriginalDispatch.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (OperationCanceledException error) when (ReferenceEquals(error, action.OriginalCancellation) &&
                    error.CancellationToken == action.OriginalToken && action.OriginalToken.IsCancellationRequested) { }
                catch (Exception error) { Add(cleanup, error, primary); }
            Throw(primary, cleanup);
            return true;
        }, CancellationToken.None);
    }

    private static CuiNativeScene Scene(ICuiBindingContext model, ICuiActionDispatcher actions) =>
        new("fixture", "Fixture", "Home", new CuiRichParser().Parse(
            "<Cui><Button id=\"held-scene-action\" action=\"Wait\" content=\"Run\" /></Cui>"),
            model, actions, new Ready());

    private sealed class Ready : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready,
                "fixture-readiness", "Original scene available."));
        }
    }

    private sealed class HeldCleanupAction : ICuiActionDispatcher
    {
        public int Calls;
        public bool CleanupFinished;
        public CancellationToken OriginalToken;
        public OperationCanceledException? OriginalCancellation;
        public Task? OriginalDispatch;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReleaseCleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default)
        {
            Calls++;
            OriginalToken = token;
            OriginalDispatch = RunAsync(token);
            return new ValueTask(OriginalDispatch);
        }

        private async Task RunAsync(CancellationToken token)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException error)
            {
                OriginalCancellation = error;
                Cancelled.TrySetResult();
                throw;
            }
            finally
            {
                await ReleaseCleanup.Task;
                CleanupFinished = true;
            }
        }
    }

    private static void Add(List<Exception> failures, Exception error, Exception? primary)
    {
        if (!ReferenceEquals(error, primary) && !failures.Any(previous => ReferenceEquals(previous, error)))
            failures.Add(error);
    }

    private static void Throw(Exception? primary, List<Exception> cleanup)
    {
        if (primary is not null && cleanup.Count == 0) ExceptionDispatchInfo.Capture(primary).Throw();
        if (primary is not null) cleanup.Insert(0, primary);
        if (cleanup.Count != 0) throw new AggregateException("Original scene completion and cleanup failures retained.", cleanup);
    }
}
