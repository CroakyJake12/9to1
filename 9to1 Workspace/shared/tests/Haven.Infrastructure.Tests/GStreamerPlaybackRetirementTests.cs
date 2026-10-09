using Haven.Core.Media;
using Haven.Infrastructure.Media;
using Xunit;

namespace Haven.Infrastructure.Tests;

// The same production session with explicit native-port callbacks; no actual
// device, GStreamer library or platform audio is started by these controls.
public sealed class GStreamerPlaybackRetirementTests
{
    private static readonly List<(GStreamerPlaybackSession Parent, GStreamerPlaybackSession Child, Task Original)> RetainedPendingOwners = [];
    [Fact]
    public async Task Actual_stop_fault_retains_pipeline_and_same_failed_close_without_release_or_replay()
    {
        var ports = new Ports { StopFailure = new IOException("Original stop callback failed") };
        var session = ports.Create(); var close = session.DisposeAsync().AsTask();
        await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Same(close, session.OriginalClose); Assert.Same(close, session.DisposeAsync().AsTask());
        Assert.Equal((nint)71, session.RetainedPipeline); Assert.Equal(1, ports.Stops); Assert.Equal(0, ports.Unrefs);
        Assert.NotNull(session.OriginalStop); Assert.True(session.OriginalStop!.IsFaulted); Assert.Null(session.OriginalUnref);
    }
    [Fact]
    public async Task Actual_unref_fault_preserves_unknown_pipeline_and_same_original_release_task()
    {
        var ports = new Ports { UnrefFailure = new IOException("Original physical release failed") };
        var session = ports.Create(); var close = session.DisposeAsync().AsTask();
        await Assert.ThrowsAsync<AggregateException>(() => close); var rawRelease = session.OriginalUnref;
        Assert.NotNull(rawRelease); Assert.True(rawRelease!.IsFaulted); Assert.True(session.OriginalStop!.IsCompletedSuccessfully);
        Assert.Same(close, session.DisposeAsync().AsTask()); Assert.Same(rawRelease, session.OriginalUnref);
        Assert.Equal((nint)71, session.RetainedPipeline); Assert.Equal(1, ports.Stops); Assert.Equal(1, ports.Unrefs);
    }
    [Fact]
    public async Task Pending_native_state_does_not_authorize_unref_of_original_pipeline()
    {
        var ports = new Ports { CurrentState = 3, PendingState = 1, StateResult = 2 };
        var session = ports.Create(); var close = session.DisposeAsync().AsTask();
        await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Equal(1, ports.StateObservations); Assert.Equal(0, ports.Unrefs); Assert.Equal((nint)71, session.RetainedPipeline);
        Assert.Equal((ulong)0, ports.LastStateTimeout); Assert.Same(close, session.DisposeAsync().AsTask());
    }
    [Fact]
    public async Task Original_native_callback_rejects_restored_context_self_join_before_close_publication()
    {
        var ports = new Ports(); var session = ports.Create(); var neutral = ExecutionContext.Capture()!;
        ports.DuringState = state =>
        {
            if (state != 4) return;
            ExecutionContext.Run(neutral, _ =>
            { Assert.Throws<InvalidOperationException>(() => { _ = session.DisposeAsync(); }); Assert.Null(session.OriginalClose); }, null);
        };
        var original = session.SetStateAsync(MediaPlaybackState.Playing); await original;
        Assert.True(original.IsCompletedSuccessfully); Assert.Equal(MediaPlaybackState.Playing, session.State);
        var close = session.DisposeAsync().AsTask(); await close;
        Assert.Equal((nint)0, session.RetainedPipeline); Assert.Equal(1, ports.Stops); Assert.Equal(1, ports.Unrefs);
    }
    [Fact]
    public async Task Original_close_callback_cannot_join_its_published_cached_close_after_context_restore()
    {
        var ports = new Ports(); var session = ports.Create(); var neutral = ExecutionContext.Capture()!;
        Task? observed = null;
        ports.DuringState = state =>
        {
            if (state != 1) return;
            ExecutionContext.Run(neutral, _ =>
            {
                observed = session.OriginalClose; Assert.NotNull(observed);
                Assert.Throws<InvalidOperationException>(() => { _ = session.DisposeAsync(); });
            }, null);
        };
        var close = session.DisposeAsync().AsTask(); await close;
        Assert.Same(close, observed); Assert.Same(close, session.DisposeAsync().AsTask());
        Assert.Equal((nint)0, session.RetainedPipeline); Assert.Equal((nint)71, session.OriginalPipeline);
    }
    [Fact]
    public async Task Failed_original_operation_is_joined_even_when_physical_stop_and_release_succeed()
    {
        var failure = new IOException("Original seek callback failed"); var ports = new Ports { SeekFailure = failure };
        var session = ports.Create(); var original = session.SeekAsync(MediaTimebase.Nanoseconds.At(17));
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => original));
        var sameObservation = Assert.Single(session.OriginalOperationObservations);
        Assert.False(await sameObservation);
        var close = session.DisposeAsync().AsTask(); var retained = await Assert.ThrowsAsync<AggregateException>(() => close);
        Assert.Contains(failure, retained.InnerExceptions); Assert.Contains(original, session.OriginalOperations);
        Assert.Equal((nint)0, session.RetainedPipeline); Assert.True(session.OriginalStop!.IsCompletedSuccessfully);
        Assert.True(session.OriginalUnref!.IsCompletedSuccessfully); Assert.Same(close, session.DisposeAsync().AsTask());
    }
    [Fact]
    public async Task Actual_child_native_callback_retains_owning_parent_guard_under_restored_context()
    {
        var parentPorts = new Ports(); var childPorts = new Ports(); var parent = parentPorts.Create(); var child = childPorts.Create();
        var neutral = ExecutionContext.Capture()!; var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        childPorts.DuringState = state =>
        {
            if (state != 4) return;
            ExecutionContext.Run(neutral, _ =>
            { Assert.Throws<InvalidOperationException>(() => { _ = parent.DisposeAsync(); }); Assert.Null(parent.OriginalClose); observed.SetResult(); }, null);
        };
        parentPorts.DuringState = state =>
        {
            if (state == 4) child.SetStateAsync(MediaPlaybackState.Playing).GetAwaiter().GetResult();
        };
        var original = parent.SetStateAsync(MediaPlaybackState.Playing);
        var first = await Task.WhenAny(original, Task.Delay(TimeSpan.FromSeconds(10)));
        if (!ReferenceEquals(original, first))
        {
            RetainedPendingOwners.Add((parent, child, original));
            throw new TimeoutException("The SAME native parent/child drivers remain held; no cleanup or replacement is attempted.");
        }
        await original; await observed.Task;
        await parent.DisposeAsync(); await child.DisposeAsync();
        Assert.Equal(1, parentPorts.Unrefs); Assert.Equal(1, childPorts.Unrefs);
    }
    [Fact]
    public async Task Healthy_native_calls_are_pruned_only_after_same_independent_observation()
    {
        var ports = new Ports(); var session = ports.Create();
        for (var index = 0; index < 160; index++)
        {
            var sameOriginal = session.SeekAsync(MediaTimebase.Nanoseconds.At(index));
            var sameObservation = Assert.Single(session.OriginalOperationObservations);
            Assert.Contains(sameOriginal, session.OriginalOperations);
            await sameOriginal;
            Assert.True(await sameObservation);
            Assert.True(sameOriginal.IsCompletedSuccessfully);
        }
        var close = session.DisposeAsync().AsTask(); await close;
        Assert.Equal(1, ports.Unrefs); Assert.Equal((nint)0, session.RetainedPipeline);
    }
    private sealed class Ports
    {
        internal Exception? StopFailure, UnrefFailure, SeekFailure;
        internal int Stops, Unrefs, StateObservations, CurrentState = 1, PendingState, StateResult = 1;
        internal ulong LastStateTimeout; internal Action<int>? DuringState;
        internal GStreamerPlaybackSession Create() => new((nint)71, SetState, GetState, Query, Seek, Unref);
        private int SetState(nint pointer, int state)
        {
            Assert.Equal((nint)71, pointer); DuringState?.Invoke(state);
            if (state == 1) { Stops++; if (StopFailure is not null) throw StopFailure; }
            return 1;
        }
        private int GetState(nint pointer, out int state, out int pending, ulong timeout)
        { Assert.Equal((nint)71, pointer); StateObservations++; LastStateTimeout = timeout; state = CurrentState; pending = PendingState; return StateResult; }
        private static int Query(nint pointer, int format, out long position) { Assert.Equal((nint)71, pointer); Assert.Equal(3, format); position = 17; return 1; }
        private int Seek(nint pointer, int format, int flags, long position)
        { Assert.Equal((nint)71, pointer); if (SeekFailure is not null) throw SeekFailure; return 1; }
        private void Unref(nint pointer) { Assert.Equal((nint)71, pointer); Unrefs++; if (UnrefFailure is not null) throw UnrefFailure; }
    }
}
