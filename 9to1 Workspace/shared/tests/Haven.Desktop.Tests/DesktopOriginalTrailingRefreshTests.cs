using System.Runtime.ExceptionServices;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class DesktopOriginalTrailingRefreshTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [Fact]
    public Task Seven_hundred_inputs_share_one_actual_driver_and_latest_generation() => RunCase(async scope =>
    {
        scope.Schedule(1);
        await scope.DelayEntered.Task.WaitAsync(Bound);
        var actual = scope.Driver.OriginalDriver;
        for (var generation = 2; generation <= 700; generation++) scope.Schedule(generation);
        Assert.Same(actual, scope.Driver.OriginalDriver);
        Assert.Equal(1, scope.DelayCalls);
        scope.Clock.Advance(TimeSpan.FromMilliseconds(200));
        scope.ReleaseHeld();
        await actual!.WaitAsync(Bound);
        Assert.Equal([700], scope.RefreshGenerations);
        await scope.Close().WaitAsync(Bound);
    });

    [Fact]
    public Task Input_during_actual_refresh_gets_another_trailing_refresh_without_losing_it() => RunCase(async scope =>
    {
        var firstRefresh = scope.Hold();
        scope.OnRefresh = generation => generation == 1 ? firstRefresh.Task : Task.CompletedTask;
        scope.Schedule(1);
        scope.Clock.Advance(TimeSpan.FromMilliseconds(200)); scope.ReleaseHeldDelays();
        await scope.RefreshEntered.Task.WaitAsync(Bound);
        var actual = scope.Driver.OriginalDriver;
        scope.Schedule(2);
        firstRefresh.TrySetResult();
        await scope.SecondDelayEntered.Task.WaitAsync(Bound);
        Assert.Same(actual, scope.Driver.OriginalDriver);
        scope.Clock.Advance(TimeSpan.FromMilliseconds(200)); scope.ReleaseHeldDelays();
        await actual!.WaitAsync(Bound);
        Assert.Equal([1, 2], scope.RefreshGenerations);
    });

    [Fact]
    public Task Retirement_waits_same_held_delay_and_does_not_start_a_late_refresh() => RunCase(async scope =>
    {
        scope.Schedule(1);
        await scope.DelayEntered.Task.WaitAsync(Bound);
        var actual = scope.Driver.OriginalDriver;
        var close = scope.Close();
        Assert.False(close.IsCompleted); Assert.False(actual!.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => { scope.Schedule(2); });
        scope.ReleaseHeldDelays();
        await actual.WaitAsync(Bound); await close.WaitAsync(Bound);
        Assert.Empty(scope.RefreshGenerations);
    });

    [Fact]
    public Task Retirement_independently_waits_actual_repository_refresh_without_canceling_it() => RunCase(async scope =>
    {
        var refresh = scope.Hold(); scope.OnRefresh = _ => refresh.Task;
        scope.Schedule(1);
        scope.Clock.Advance(TimeSpan.FromMilliseconds(200)); scope.ReleaseHeldDelays();
        await scope.RefreshEntered.Task.WaitAsync(Bound);
        var close = scope.Close(); Assert.False(close.IsCompleted); Assert.False(refresh.Task.IsCompleted);
        refresh.TrySetResult(); await scope.Driver.OriginalDriver!.WaitAsync(Bound); await close.WaitAsync(Bound);
        Assert.Equal([1], scope.RefreshGenerations);
    });

    [Fact]
    public Task Actual_delay_siblings_are_retained_without_creating_a_refresh_receipt() => RunCase(async scope =>
    {
        var first = new IOException("Original delayed source one.");
        var second = new InvalidOperationException("Original delayed source two.");
        var failed = scope.Hold(); scope.OnDelay = _ => failed.Task;
        scope.Schedule(1);
        failed.TrySetException([first, second]);
        DemandCauses(await Fault(scope.Driver.OriginalDriver!), first, second);
        DemandCauses(await Fault(scope.Close()), first, second);
        Assert.Empty(scope.RefreshGenerations); scope.ExpectFault = true;
    });

    [Fact]
    public Task Direct_refresh_cancellation_is_faulted_with_the_actual_cause() => RunCase(async scope =>
    {
        var actualCause = new OperationCanceledException("Actual direct refresh source, no original canceled Task.");
        scope.OnRefresh = _ => throw actualCause;
        scope.Schedule(1); scope.Clock.Advance(TimeSpan.FromMilliseconds(200)); scope.ReleaseHeldDelays();
        var actual = scope.Driver.OriginalDriver!;
        DemandCauses(await Fault(actual), actualCause); Assert.True(actual.IsFaulted);
        DemandCauses(await Fault(scope.Close()), actualCause); scope.ExpectFault = true;
    });

    [Fact]
    public Task Tolerated_actual_refresh_fault_remains_an_external_close_failure() => RunCase(async scope =>
    {
        var actualCause = new IOException("Original UI-tolerated repository read.");
        scope.OnOriginalRefresh = original => { original.Retain(actualCause); return Task.CompletedTask; };
        scope.Schedule(1); scope.Clock.Advance(TimeSpan.FromMilliseconds(200)); scope.ReleaseHeldDelays();
        await scope.Driver.OriginalDriver!.WaitAsync(Bound);
        DemandCauses(await Fault(scope.Close()), actualCause); scope.ExpectFault = true;
    });

    [Fact]
    public Task Successful_driver_can_be_followed_by_a_distinct_original_driver() => RunCase(async scope =>
    {
        scope.Schedule(1); scope.Clock.Advance(TimeSpan.FromMilliseconds(200)); scope.ReleaseHeldDelays();
        var first = scope.Driver.OriginalDriver!; await first.WaitAsync(Bound);
        scope.Schedule(2); var second = scope.Driver.OriginalDriver!;
        Assert.NotSame(first, second);
        scope.Clock.Advance(TimeSpan.FromMilliseconds(200)); scope.ReleaseHeldDelays();
        await second.WaitAsync(Bound); Assert.Equal([1, 2], scope.RefreshGenerations);
    });

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }
    private sealed class Scope
    {
        private readonly object _holdsGate = new();
        private bool _cleaning;
        private readonly List<TaskCompletionSource> _held = [];
        private readonly List<TaskCompletionSource> _delays = [];
        private readonly List<Task> _drivers = [];
        internal readonly Clock Clock = new();
        internal readonly TaskCompletionSource DelayEntered = NewHold();
        internal readonly TaskCompletionSource SecondDelayEntered = NewHold();
        internal readonly TaskCompletionSource RefreshEntered = NewHold();
        internal readonly DesktopOriginalTrailingRefresh Driver;
        internal readonly DesktopOriginalWorkLifetime Parent;
        internal readonly List<int> RefreshGenerations = [];
        internal Func<int, Task> OnRefresh = _ => Task.CompletedTask;
        internal Func<TimeSpan, Task>? OnDelay;
        internal Func<DesktopOriginalWorkLifetime.Original, Task>? OnOriginalRefresh;
        internal int DelayCalls;
        internal bool ExpectFault;
        internal Scope()
        {
            DesktopOriginalTrailingRefresh? driver = null;
            Parent = new(() => { driver!.RequestRetirement(); return Task.CompletedTask; }, () => Task.CompletedTask);
            Driver = driver = new(Parent, TimeSpan.FromMilliseconds(200), (original, generation) =>
            {
                RefreshGenerations.Add(generation); RefreshEntered.TrySetResult();
                return OnOriginalRefresh?.Invoke(original) ?? OnRefresh(generation);
            }, Clock, delay =>
            {
                var count = Interlocked.Increment(ref DelayCalls);
                Task actual;
                if (OnDelay is not null) actual = OnDelay(delay);
                else
                {
                    var held = Hold(); lock (_holdsGate) _delays.Add(held); actual = held.Task;
                }
                DelayEntered.TrySetResult(); if (count >= 2) SecondDelayEntered.TrySetResult();
                return actual;
            });
        }
        internal void Schedule(int generation)
        {
            Driver.Schedule(generation);
            if (Driver.OriginalDriver is { } actual && !_drivers.Contains(actual)) _drivers.Add(actual);
        }
        internal TaskCompletionSource Hold()
        {
            var actual = NewHold();
            lock (_holdsGate) { _held.Add(actual); if (_cleaning) actual.TrySetResult(); }
            return actual;
        }
        internal void ReleaseHeldDelays()
        {
            TaskCompletionSource[] actuals; lock (_holdsGate) actuals = _delays.ToArray();
            foreach (var actual in actuals) actual.TrySetResult();
        }
        internal void ReleaseHeld()
        {
            TaskCompletionSource[] actuals; lock (_holdsGate) actuals = _held.ToArray();
            foreach (var actual in actuals) actual.TrySetResult();
        }
        internal Task Close() => Parent.CloseAndDrainAsync();
        internal async Task Cleanup()
        {
            lock (_holdsGate) _cleaning = true;
            Driver.RequestRetirement(); ReleaseHeld();
            var failures = new List<Exception>();
            Task? actualClose = null;
            try { actualClose = Close(); } catch (Exception error) { failures.Add(error); }
            foreach (var actual in _drivers.Concat(actualClose is null ? [] : new[] { actualClose }).Distinct())
                try { await actual.WaitAsync(Bound); } catch (Exception error) { if (!ExpectFault) failures.Add(error); }
            if (failures.Count > 0) throw new AggregateException("Original fixture tasks failed during independent cleanup.", failures);
        }
    }
    private static TaskCompletionSource NewHold() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task RunCase(Func<Scope, Task> body)
    {
        var scope = new Scope(); var failures = new List<Exception>();
        try { await body(scope); } catch (Exception primary) { failures.Add(primary); }
        finally { try { await scope.Cleanup(); } catch (Exception cleanup) { failures.Add(cleanup); } }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Original body and cleanup failures retained.", failures);
    }
    private static async Task<Exception> Fault(Task actual)
    {
        try { await actual.WaitAsync(Bound); } catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected an actual original failure.");
    }
    private static void DemandCauses(Exception actual, params Exception[] expected)
    {
        var all = new List<Exception>();
        void Walk(Exception cause) { all.Add(cause); if (cause is AggregateException group) foreach (var child in group.InnerExceptions) Walk(child); }
        Walk(actual);
        foreach (var cause in expected) Assert.Contains(all, item => ReferenceEquals(item, cause));
    }
}
