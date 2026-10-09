using Avalonia.Headless;
using CakeOS.Cui.Runtime;
using Xunit;

namespace HavenOS.Apps.Canvas.NativeUI.Tests;

[Collection("Canvas native UI")]
public sealed class CanvasFailedSurfaceInitializationTests
{
    [Fact]
    public async Task Failed_acquired_surface_initialization_joins_close_and_preserves_all_original_faults()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch(async () =>
        {
            var canceledLikeFault = new OperationCanceledException("Faulted readiness payload.");
            var sibling = new IOException("Original readiness sibling.");
            var readiness = new FaultedReadiness(canceledLikeFault, sibling);
            var opened = false;
            var surface = new CanvasNativeCuiSurface(_ =>
            {
                opened = true;
                throw new InvalidOperationException("A denied source must never open the native document.");
            }, readiness);

            var failed = CanvasHostWindow.InitializeOwnedSurfaceAsync(surface, CancellationToken.None);
            var error = await Assert.ThrowsAsync<AggregateException>(() => failed);
            Assert.True(failed.IsFaulted);
            Assert.False(opened);
            Assert.True(ContainsSameCause(error, canceledLikeFault));
            Assert.True(ContainsSameCause(error, sibling));
            var close = Assert.IsAssignableFrom<Task>(surface.OriginalCloseTask);
            Assert.True(close.IsCompleted);
            Assert.Same(close, surface.CloseAndDrainAsync());
            Assert.Null(surface.Content);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_acquired_surface_keeps_foreign_aggregate_identity_and_its_close_faulted(bool nested)
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(CanvasInputTestApplication));
        await native.Dispatch(async () =>
        {
            var empty = new AggregateException("Foreign empty readiness fault.");
            var source = nested ? new AggregateException("Foreign nested readiness fault.", empty, new IOException("Nested readiness sibling.")) : empty;
            var surface = new CanvasNativeCuiSurface(_ => throw new InvalidOperationException("Denied source must not open."), new FaultedReadiness(source));
            var failed = CanvasHostWindow.InitializeOwnedSurfaceAsync(surface, CancellationToken.None);
            var error = await Record.ExceptionAsync(() => failed);
            Assert.True(failed.IsFaulted);
            Assert.True(ContainsSameCause(error!, source));
            var close = Assert.IsAssignableFrom<Task>(surface.OriginalCloseTask);
            Assert.True(close.IsFaulted);
            Assert.Same(source, await Record.ExceptionAsync(() => close));
            Assert.Same(close, surface.CloseAndDrainAsync());
            Assert.Null(surface.Content);
        }, CancellationToken.None);
    }

    private static bool ContainsSameCause(Exception actual, Exception sought)
        => ReferenceEquals(actual, sought) || actual is AggregateException group && group.InnerExceptions.Any(child => ContainsSameCause(child, sought));

    private sealed class FaultedReadiness(params Exception[] errors) : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            var actual = new TaskCompletionSource<CuiSceneAvailability>(TaskCreationOptions.RunContinuationsAsynchronously);
            actual.SetException(errors);
            return new(actual.Task);
        }
    }
}
