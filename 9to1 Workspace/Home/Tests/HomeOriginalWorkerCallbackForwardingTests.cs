using System.Reflection;
using System.Runtime.ExceptionServices;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

// These controls exercise the maintained nested broker callback helper. They
// qualify original-task/callback custody only, never installed worker authority.
public sealed class HomeOriginalWorkerCallbackForwardingTests
{
    [Fact]
    public async Task Nested_setup_envelope_joins_same_child_without_enrolling_parent_and_late_callback_survives_healthy_prune()
    {
        var probe = new Probe(); Task<int>? driver = null; Exception? body = null;
        try
        {
            driver = probe.Start();
            Assert.Same(probe.RawAcquired.Task, await Task.WhenAny(probe.RawAcquired.Task, driver));
            Assert.False(driver.IsCompleted);
            probe.Raw.TrySetResult(7);
            Assert.Equal(7, await probe.Raw.Task);
            Assert.Equal(7, await driver);
            probe.PruneIndependentlyJoinedHealthyTasks();
            Assert.Empty(probe.Retained);
            var callback = Assert.IsType<Action>(probe.Saved);
            var late = Assert.Throws<InvalidOperationException>(callback);
            Assert.Equal("The original ownership callback is inactive, foreign-thread or consumed.", late.Message);
            Assert.Null(late.InnerException);
            Assert.Contains(probe.Unexpected, actual => ReferenceEquals(actual, late));
            var refused = Assert.ThrowsAny<Exception>(() => { _ = probe.Start(); });
            Assert.All(Leaves(refused), leaf => Assert.Same(late, leaf));
            probe.Expected.Add(late);
            Assert.Equal(1, probe.CoreCalls);
        }
        catch (Exception cause) { body = cause; }
        finally
        {
            probe.Raw.TrySetResult(7);
            await probe.JoinActuals(body, driver);
        }
    }

    [Fact]
    public async Task Nested_retainer_postfailure_still_joins_actual_child_before_driver_and_preserves_exact_occurrence()
    {
        var refusal = new IOException("The actual foreign retainer refused this exact raw publication.");
        var probe = new Probe { RetainerFailure = refusal }; Task<int>? driver = null; Exception? body = null;
        try
        {
            driver = probe.Start();
            Assert.Same(probe.RetainerEntered.Task, await Task.WhenAny(probe.RetainerEntered.Task, driver));
            Assert.False(driver.IsCompleted);
            probe.Raw.TrySetResult(7);
            Assert.Equal(7, await probe.Raw.Task);
            var actual = await Observe(driver);
            Assert.NotNull(actual);
            Assert.All(Leaves(actual!), leaf => Assert.Same(refusal, leaf));
            Assert.Contains(probe.Unexpected, cause => ReferenceEquals(cause, refusal));
            probe.Expected.Add(refusal);
        }
        catch (Exception cause) { body = cause; }
        finally
        {
            probe.Raw.TrySetResult(7);
            await probe.JoinActuals(body, driver);
        }
    }

    [Fact]
    public async Task Already_admitted_nested_factory_rechecks_same_owner_failure_before_productive_callback()
    {
        var probe = new Probe(); Task<int>? first = null, admitted = null; Exception? body = null;
        using var release = new ManualResetEventSlim();
        try
        {
            first = probe.Start();
            Assert.Same(probe.RawAcquired.Task, await Task.WhenAny(probe.RawAcquired.Task, first));
            probe.Raw.TrySetResult(7); Assert.Equal(7, await probe.Raw.Task); Assert.Equal(7, await first);
            var saved = Assert.IsType<Action>(probe.Saved);
            probe.PruneIndependentlyJoinedHealthyTasks(); Assert.Empty(probe.Retained);
            probe.GuardSource = true; probe.HoldNextScope = release;
            admitted = Task.Run(probe.Start);
            Assert.Same(probe.ScopeEntered.Task, await Task.WhenAny(probe.ScopeEntered.Task, admitted));
            Assert.False(admitted.IsCompleted);
            var late = Assert.Throws<InvalidOperationException>(saved);
            Assert.Equal("The original ownership callback is inactive, foreign-thread or consumed.", late.Message);
            Assert.Null(late.InnerException);
            probe.Expected.Add(late); release.Set();
            var actual = await Observe(admitted);
            Assert.NotNull(actual);
            var leaves = Leaves(actual!).Distinct(ReferenceEqualityComparer.Instance).ToArray();
            Assert.Contains(leaves, leaf => ReferenceEquals(leaf, late));
            foreach (var leaf in leaves)
            {
                if (ReferenceEquals(leaf, late)) continue;
                var protocol = Assert.IsType<InvalidOperationException>(leaf);
                Assert.Equal("The original ownership callback was not invoked.", protocol.Message);
                Assert.Null(protocol.InnerException); probe.Expected.Add(protocol);
            }
            Assert.Equal(1, probe.CoreCalls);
        }
        catch (Exception cause) { body = cause; }
        finally
        {
            release.Set(); probe.Raw.TrySetResult(7);
            await probe.JoinActuals(body, admitted ?? first);
        }
    }

    private sealed class Probe
    {
        private readonly object _gate = new();
        private readonly List<Task> _retained = [];
        private readonly List<Exception> _unexpected = [];
        internal readonly List<Exception> Expected = [];
        internal readonly TaskCompletionSource<int> Raw = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource RawAcquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource RetainerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? Saved;
        internal Exception? RetainerFailure;
        internal int CoreCalls;
        internal ManualResetEventSlim? HoldNextScope;
        internal bool GuardSource;
        internal readonly TaskCompletionSource ScopeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task[] Retained { get { lock (_gate) return _retained.ToArray(); } }
        internal Exception[] Unexpected { get { lock (_gate) return _unexpected.ToArray(); } }
        private void Keep(Exception cause)
        { lock (_gate) if (!_unexpected.Contains(cause, ReferenceEqualityComparer.Instance)) _unexpected.Add(cause); }
        private void Demand()
        {
            var errors = Unexpected;
            if (errors.Length != 0) throw new AggregateException("The exact original worker callback occurrences remain owned.", errors);
        }
        private void Scope(Action callback)
        {
            var hold = Interlocked.Exchange(ref HoldNextScope, null);
            if (hold is not null) { ScopeEntered.TrySetResult(); hold.Wait(); }
            if (GuardSource) Demand(); Saved = callback; callback(); if (GuardSource) Demand();
        }
        private void Retain(Task actual)
        {
            lock (_gate) if (!_retained.Contains(actual, ReferenceEqualityComparer.Instance)) _retained.Add(actual);
            if (ReferenceEquals(actual, Raw.Task) && RetainerFailure is { } cause)
            { RetainerEntered.TrySetResult(); throw cause; }
        }
        internal Task<int> Start()
        {
            Demand();
            var helper = typeof(HomeResourceOperationBroker).Assembly.GetType("HavenOS.Home.Core.HomeOwnershipOriginalSourceCallbacks", true)!;
            var coreType = typeof(Func<,>).MakeGenericType(helper, typeof(Task<int>));
            var core = GetType().GetMethod(nameof(Core), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(helper).CreateDelegate(coreType, this);
            var method = typeof(HomeResourceOperationBroker).GetMethod("RunOriginalSetupCoreEnvelopeAsync", BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeof(int));
            return (Task<int>)Unwrap(() => method.Invoke(null, new object[] { (Action<Action>)Scope, (Action<Task>)Retain, core, (Action<Exception>)Keep }))!;
        }
        private Task<int> Core<T>(T actualSource)
        {
            CoreCalls++;
            var method = actualSource!.GetType().GetMethod("ReadAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(typeof(int));
            Func<Task<int>> factory = () => { RawAcquired.TrySetResult(); return Raw.Task; };
            return (Task<int>)Unwrap(() => method.Invoke(actualSource, new object[] { factory }))!;
        }
        internal void PruneIndependentlyJoinedHealthyTasks()
        {
            lock (_gate) _retained.RemoveAll(actual =>
            { if (!actual.IsCompletedSuccessfully) return false; actual.GetAwaiter().GetResult(); return true; });
        }
        internal async Task JoinActuals(Exception? body, Task? driver)
        {
            var errors = new List<Exception>(); if (body is not null) errors.Add(body);
            var seen = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            foreach (var actual in Retained.Concat(new Task?[] { Raw.Task, driver }.OfType<Task>()).Where(seen.Add))
            {
                var cause = await Observe(actual);
                if (cause is null) continue;
                if (!Leaves(cause).All(leaf => Expected.Contains(leaf, ReferenceEqualityComparer.Instance))) errors.Add(cause);
            }
            // Unexpected protocol occurrences are expected only after their exact
            // contract/assertion above; assertion/body failures remain ordinary.
            if (errors.Count != 0) throw new AggregateException("Body/actual original joins did not settle.", errors);
        }
    }
    private static object? Unwrap(Func<object?> call)
    {
        try { return call(); }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }
    private static async Task<Exception?> Observe(Task actual)
    { try { await actual; return null; } catch (Exception cause) { return actual.Exception ?? cause; } }
    private static IEnumerable<Exception> Leaves(Exception actual)
    {
        var pending = new Stack<Exception>(); pending.Push(actual); var visited = 0; var enqueued = 1;
        while (pending.TryPop(out var same))
        {
            if (++visited > 4096) { yield return same; yield break; }
            if (same.GetType() == typeof(AggregateException) && same is AggregateException { InnerExceptions.Count: > 0 } group)
            {
                if (group.InnerExceptions.Count > 4096 - enqueued) { yield return group; continue; }
                foreach (var child in group.InnerExceptions) { enqueued++; pending.Push(child); }
            }
            else yield return same;
        }
    }
}
