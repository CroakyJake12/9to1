using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class DesktopOriginalShutdownSequenceTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    [Fact]
    public Task Repeated_close_joins_same_held_startup_and_does_not_prepare_early() => RunCase(async scope =>
    {
        var startup = scope.Hold(); scope.App = () => startup.Task;
        var close = scope.Close();
        Assert.Same(close, scope.Close()); Assert.False(close.IsCompleted);
        Assert.Empty(scope.Order); Assert.NotNull(scope.Owner.OriginalRequiredDrain);
        startup.TrySetResult(); await close.WaitAsync(Bound);
        Assert.Equal(["save", "prepare", "borrowers", "provider", "final"], scope.Order);
        Assert.Same(scope.Owner.OriginalRequiredDrain, scope.Writer.ObservedDrain);
    });

    [Fact]
    public Task Failed_app_original_still_joins_borrowers_and_keeps_provider_alive() => RunCase(async scope =>
    {
        var original = new IOException("Original startup error.");
        var borrower = scope.Hold(); scope.App = () => Task.FromException(original); scope.Borrowers = () => borrower.Task;
        var close = scope.Close(); await scope.BorrowerEntered.Task.WaitAsync(Bound);
        Assert.False(close.IsCompleted); Assert.DoesNotContain("provider", scope.Order); Assert.DoesNotContain("prepare", scope.Order);
        borrower.TrySetResult(); DemandCauses(await Fault(close), original);
        Assert.Equal(["borrowers"], scope.Order); Assert.Equal(0, scope.Writer.Calls); scope.ExpectFault = true;
    });

    [Fact]
    public Task Borrower_raw_siblings_and_independent_cleanup_are_preserved_before_provider_teardown() => RunCase(async scope =>
    {
        var first = new IOException("Actual borrower one."); var second = new InvalidOperationException("Actual borrower two.");
        var borrower = scope.Hold(); scope.Borrowers = () => borrower.Task;
        var close = scope.Close(); await scope.BorrowerEntered.Task.WaitAsync(Bound);
        borrower.TrySetException([first, second]); DemandCauses(await Fault(close), first, second);
        Assert.Equal(2, scope.Owner.OriginalFailures.Count); Assert.DoesNotContain("provider", scope.Order);
        Assert.Equal(0, scope.Writer.Calls); scope.ExpectFault = true;
    });

    [Fact]
    public Task Preparation_audit_must_settle_before_borrowed_diagnostics_provider_dispose() => RunCase(async scope =>
    {
        var audit = scope.Hold<IStartupRecoveryFinalCleanWriter>(); scope.Prepare = () => audit.Task;
        var close = scope.Close(); await scope.PrepareEntered.Task.WaitAsync(Bound);
        Assert.False(close.IsCompleted); Assert.DoesNotContain("provider", scope.Order); Assert.DoesNotContain("borrowers", scope.Order);
        audit.TrySetResult(scope.Writer); await close.WaitAsync(Bound);
        Assert.True(scope.Writer.ObservedDrain!.IsCompletedSuccessfully);
        Assert.Equal(["save", "prepare", "borrowers", "provider", "final"], scope.Order);
    });

    [Fact]
    public Task Held_provider_close_prevents_final_writer_even_after_audit_and_children() => RunCase(async scope =>
    {
        var provider = scope.Hold(); scope.Provider = () => provider.Task;
        var close = scope.Close(); await scope.ProviderEntered.Task.WaitAsync(Bound);
        Assert.False(close.IsCompleted); Assert.Equal(0, scope.Writer.Calls);
        provider.TrySetResult(); await close.WaitAsync(Bound);
        Assert.True(scope.Writer.ObservedDrain!.IsCompletedSuccessfully);
        Assert.Contains(provider.Task, scope.Owner.ActualSourceTasks);
    });

    [Fact]
    public Task Faulted_provider_close_is_uncertain_and_never_calls_final_writer() => RunCase(async scope =>
    {
        var first = new IOException("Actual provider disposal one."); var second = new InvalidOperationException("Actual provider disposal two.");
        var provider = scope.Hold(); scope.Provider = () => provider.Task;
        var close = scope.Close(); await scope.ProviderEntered.Task.WaitAsync(Bound);
        provider.TrySetException([first, second]); DemandCauses(await Fault(close), first, second);
        Assert.Equal(0, scope.Writer.Calls); Assert.False(scope.Owner.OriginalRequiredDrain!.IsCompletedSuccessfully);
        Assert.Same(close, scope.Close()); Assert.Equal(1, scope.ProviderCalls); scope.ExpectFault = true;
    });

    [Fact]
    public Task Direct_callback_cancellation_is_faulted_and_retains_actual_reference() => RunCase(async scope =>
    {
        var original = new OperationCanceledException("Actual direct startup callback."); scope.App = () => throw original;
        var close = scope.Close(); DemandCauses(await Fault(close), original);
        Assert.True(close.IsFaulted); Assert.Contains(scope.Owner.OriginalFailures, error => ReferenceEquals(error, original));
        Assert.Equal(0, scope.Writer.Calls); scope.ExpectFault = true;
    });

    [Fact]
    public Task Opaque_empty_original_aggregate_is_not_replaced_by_success() => RunCase(async scope =>
    {
        var original = new AggregateException("Actual opaque source group."); scope.App = () => Task.FromException(original);
        DemandCauses(await Fault(scope.Close()), original); Assert.Equal(0, scope.Writer.Calls); scope.ExpectFault = true;
    });

    [Fact]
    public Task Actual_callback_join_is_refused_before_any_self_join_task_is_returned() => RunCase(async scope =>
    {
        Exception? refusal = null;
        scope.App = () =>
        {
            try { scope.Close(); } catch (Exception error) { refusal = error; }
            Assert.NotNull(scope.Owner.OriginalShutdown); Assert.NotNull(scope.Owner.OriginalRequiredDrain);
            return Task.CompletedTask;
        };
        await scope.Close().WaitAsync(Bound);
        Assert.IsType<InvalidOperationException>(refusal); Assert.Equal(1, scope.Writer.Calls);
    });

    [Fact]
    public Task Detached_context_after_actual_callback_retires_can_join_normally() => RunCase(async scope =>
    {
        var entered = scope.Hold(); var release = scope.Hold(); Task? external = null;
        scope.App = () =>
        {
            external = scope.Track(Task.Run(async () => { entered.TrySetResult(); await release.Task; await scope.Close(); }));
            return Task.CompletedTask;
        };
        var close = scope.Close(); await entered.Task.WaitAsync(Bound); await close.WaitAsync(Bound);
        release.TrySetResult(); await external!.WaitAsync(Bound);
        Assert.Equal(1, scope.Writer.Calls);
    });

    [Fact]
    public Task Final_writer_fault_is_preserved_and_never_retried_or_described_as_no_effect() => RunCase(async scope =>
    {
        var actual = new IOException("Actual final writer may have committed before failure."); scope.Writer.Complete = () => Task.FromException(actual);
        var close = scope.Close(); DemandCauses(await Fault(close), actual);
        Assert.Same(close, scope.Close()); Assert.Equal(1, scope.Writer.Calls);
        Assert.Contains(scope.Owner.OriginalFailures, error => ReferenceEquals(error, actual)); scope.ExpectFault = true;
    });

    [Fact]
    public Task External_start_callback_must_return_its_actual_task_before_any_phase_joins() => RunCase(async scope =>
    {
        var scheduling = scope.Hold(); Action? signal = null;
        scope.Schedule = action => { signal = action; scope.ReleaseOnCleanup(action); return scheduling.Task; };
        var close = scope.Close(); Assert.NotNull(signal); Assert.Empty(scope.Order);
        signal!(); Assert.Empty(scope.Order); Assert.False(close.IsCompleted);
        scheduling.TrySetResult(); await close.WaitAsync(Bound);
        Assert.Contains(scheduling.Task, scope.Owner.ActualSourceTasks);
        Assert.Equal(1, scope.Writer.Calls);
    });

    [Fact]
    public Task Failed_actual_external_start_retains_all_siblings_and_never_begins_teardown() => RunCase(async scope =>
    {
        var a = new IOException("Actual dispatcher original one."); var b = new InvalidOperationException("Actual dispatcher original two.");
        var scheduling = scope.Hold();
        scope.Schedule = signal => { signal(); return scheduling.Task; };
        var close = scope.Close(); scheduling.TrySetException([a, b]);
        DemandCauses(await Fault(close), a, b);
        Assert.Empty(scope.Order); Assert.Equal(0, scope.Writer.Calls); scope.ExpectFault = true;
    });

    [Fact]
    public Task Direct_external_start_cancellation_preserves_original_unknown_source_fault() => RunCase(async scope =>
    {
        var actual = new OperationCanceledException("Actual scheduling callback returned no canceled original task.");
        scope.Schedule = _ => throw actual;
        var close = scope.Close(); DemandCauses(await Fault(close), actual);
        Assert.True(close.IsFaulted); Assert.Empty(scope.Order); scope.ExpectFault = true;
    });

    [Fact]
    public Task Actual_async_scheduling_source_refuses_its_own_encompassing_join() => RunCase(async scope =>
    {
        InvalidOperationException? refusal = null;
        scope.Schedule = signal => scope.Track(Task.Run(async () =>
        {
            await Task.Yield();
            refusal = Assert.Throws<InvalidOperationException>(() => { _ = scope.Close(); });
            signal();
        }));
        await scope.Close().WaitAsync(Bound);
        Assert.NotNull(refusal); Assert.Equal(1, scope.Writer.Calls);
    });

    private sealed class Writer(Scope scope) : IStartupRecoveryFinalCleanWriter
    {
        internal int Calls; internal Task? ObservedDrain; internal Func<Task> Complete = () => Task.CompletedTask;
        public Task CompleteAfterOriginalDrainAsync(Task actualRequiredOriginalDrain, CancellationToken cancellationToken)
        {
            Calls++; ObservedDrain = actualRequiredOriginalDrain; scope.Order.Add("final");
            Assert.True(actualRequiredOriginalDrain.IsCompletedSuccessfully);
            Assert.Contains("provider", scope.Order); Assert.Equal(CancellationToken.None, cancellationToken);
            return Complete();
        }
    }
    private sealed class Scope
    {
        private readonly List<Action> _release = [];
        private readonly List<Task> _extraOriginals = [];
        internal readonly List<string> Order = [];
        internal readonly TaskCompletionSource BorrowerEntered = NewHold();
        internal readonly TaskCompletionSource ProviderEntered = NewHold();
        internal readonly TaskCompletionSource PrepareEntered = NewHold();
        internal readonly DesktopOriginalShutdownSequence Owner;
        internal readonly Writer Writer;
        internal Func<Task> App = () => Task.CompletedTask;
        internal Func<Task> Save = () => Task.CompletedTask;
        internal Func<Task<IStartupRecoveryFinalCleanWriter>> Prepare;
        internal Func<Task> Borrowers = () => Task.CompletedTask;
        internal Func<Task> Provider = () => Task.CompletedTask;
        internal Func<Action, Task> Schedule = action => { action(); return Task.CompletedTask; };
        internal bool ExpectFault; internal int ProviderCalls;
        internal Scope()
        {
            Writer = new(this); Prepare = () => Task.FromResult<IStartupRecoveryFinalCleanWriter>(Writer);
            Owner = new(() => App(), () => { Order.Add("save"); return Save(); },
                () => { Order.Add("prepare"); PrepareEntered.TrySetResult(); return Prepare(); },
                () => { Order.Add("borrowers"); BorrowerEntered.TrySetResult(); return Borrowers(); },
                () => { ProviderCalls++; Order.Add("provider"); ProviderEntered.TrySetResult(); return Provider(); },
                action => Schedule(action));
        }
        internal Task Close() => Owner.CloseAndDrainAsync();
        internal Task Track(Task actual) { _extraOriginals.Add(actual); return actual; }
        internal void ReleaseOnCleanup(Action actual) => _release.Add(actual);
        internal TaskCompletionSource Hold() { var held = NewHold(); _release.Add(() => held.TrySetResult()); return held; }
        internal TaskCompletionSource<T> Hold<T>()
        {
            var held = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _release.Add(() => held.TrySetResult((T)(object)Writer)); return held;
        }
        internal async Task Cleanup()
        {
            foreach (var release in _release) release();
            var actual = Close(); var failures = new List<Exception>();
            foreach (var original in _extraOriginals.Append(actual).Distinct<Task>(ReferenceEqualityComparer.Instance))
                try { await original.WaitAsync(Bound); }
                catch (Exception error) { if (!(ExpectFault && original.IsFaulted)) failures.Add(error); }
            if (failures.Count > 0) throw new AggregateException("Actual fixture close and every detached original are independently joined.", failures);
        }
    }
    private static TaskCompletionSource NewHold() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task RunCase(Func<Scope, Task> body)
    {
        var scope = new Scope(); var failures = new List<Exception>();
        try { await body(scope); } catch (Exception primary) { failures.Add(primary); }
        finally { try { await scope.Cleanup(); } catch (Exception cleanup) { failures.Add(cleanup); } }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Actual fixture body and cleanup preserved.", failures);
    }
    private static async Task<Exception> Fault(Task actual)
    {
        try { await actual.WaitAsync(Bound); } catch (Exception error) { return error; }
        throw new InvalidOperationException("Expected an actual original failure.");
    }
    private static void DemandCauses(Exception actual, params Exception[] expected)
    {
        var all = new List<Exception>();
        void Walk(Exception error) { all.Add(error); if (error is AggregateException group) foreach (var inner in group.InnerExceptions) Walk(inner); }
        Walk(actual);
        foreach (var cause in expected) Assert.Contains(all, error => ReferenceEquals(error, cause));
    }
}
