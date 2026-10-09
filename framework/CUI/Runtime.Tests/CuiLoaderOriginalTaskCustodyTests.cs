using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>Actual loader producer controls. No native presentation or permission witness.</summary>
public sealed class CuiLoaderOriginalTaskCustodyTests
{
    [Fact]
    public Task Dispatcher_reentry_observes_the_already_retained_actual_pipeline() => Run(async rig =>
    {
        var held = rig.Hold();
        Task? snapshot = null;
        Task? original = null;
        var calls = 0;
        var button = rig.Mount(new CallbackDispatcher((_, _, _) =>
        {
            calls++;
            snapshot = rig.Loader.WhenActionsIdleAsync();
            original = Assert.Single(Field<HashSet<Task>>(rig.Loader, "_pendingActionTasks"));
            return new ValueTask(held.Task);
        }));
        Click(button);
        Assert.Equal(1, calls);
        Assert.NotNull(snapshot);
        Assert.NotNull(original);
        Assert.False(snapshot.IsCompleted);
        Assert.False(original.IsCompleted);
        Assert.Same(original, Assert.Single(Field<HashSet<Task>>(rig.Loader, "_pendingActionTasks")));
        held.TrySetResult();
        await snapshot;
        Assert.True(original.IsCompletedSuccessfully);
    });

    [Fact]
    public Task Actual_dispatcher_Task_preserves_both_fault_siblings_and_observer_cause() => Run(async rig =>
    {
        var first = new InvalidOperationException("original first dispatcher cause");
        var second = new IOException("original second dispatcher cause");
        var observer = new ApplicationException("original observer cause");
        rig.Expect(first, second, observer);
        var actual = Task.WhenAll(Task.FromException(first), Task.FromException(second));
        var button = rig.Mount(new CallbackDispatcher((_, _, _) => new ValueTask(actual)));
        rig.Loader.ActionFailed += (_, _) => throw observer;
        Click(button);
        var pipeline = Assert.Single(Field<HashSet<Task>>(rig.Loader, "_pendingActionTasks"));
        var error = await Record.ExceptionAsync(() => pipeline);
        Assert.NotNull(error);
        Assert.True(pipeline.IsFaulted);
        var retained = Assert.IsType<AggregateException>(Assert.Single(pipeline.Exception!.InnerExceptions));
        Assert.Equal(3, retained.InnerExceptions.Count);
        Assert.Contains(retained.InnerExceptions, cause => ReferenceEquals(cause, first));
        Assert.Contains(retained.InnerExceptions, cause => ReferenceEquals(cause, second));
        Assert.Contains(retained.InnerExceptions, cause => ReferenceEquals(cause, observer));
        Assert.Equal(2, actual.Exception!.InnerExceptions.Count);
    });

    [Fact]
    public Task Opaque_empty_aggregate_is_retained_as_the_same_original_cause() => Run(async rig =>
    {
        var opaque = new AggregateException("opaque original cause has no child list");
        rig.Expect(opaque);
        var actual = Task.FromException(opaque);
        var button = rig.Mount(new CallbackDispatcher((_, _, _) => new ValueTask(actual)));
        Click(button);
        var pipeline = Assert.Single(Field<HashSet<Task>>(rig.Loader, "_pendingActionTasks"));
        var error = await Record.ExceptionAsync(() => pipeline);
        Assert.Same(opaque, error);
        Assert.True(pipeline.IsFaulted);
        Assert.Same(opaque, Assert.Single(pipeline.Exception!.InnerExceptions));
        Assert.Empty(opaque.InnerExceptions);
    });

    [Fact]
    public Task Cancellation_reentry_sees_the_same_actual_terminal_Task_and_waits_real_dispatch() => Run(async rig =>
    {
        var held = rig.Hold();
        var callbacks = 0;
        Task? reenteredTerminal = null;
        Task? reenteredSnapshot = null;
        CancellationTokenRegistration registration = default;
        rig.Release(() => registration.Dispose());
        var button = rig.Mount(new CallbackDispatcher((_, _, token) =>
        {
            registration = token.Register(() =>
            {
                callbacks++;
                reenteredTerminal = Field<Task>(rig.Loader, "_disposalTask");
                rig.Loader.Dispose();
                reenteredSnapshot = rig.Loader.WhenActionsIdleAsync();
            });
            return new ValueTask(held.Task);
        }));
        Click(button);
        rig.Loader.Dispose();
        var terminal = Field<Task>(rig.Loader, "_disposalTask");
        Assert.Equal(1, callbacks);
        Assert.Same(terminal, reenteredTerminal);
        Assert.NotNull(reenteredSnapshot);
        Assert.False(terminal.IsCompleted);
        Assert.False(reenteredSnapshot.IsCompleted);
        Assert.False(held.Task.IsCompleted);
        rig.Loader.Dispose();
        Assert.Same(terminal, Field<Task>(rig.Loader, "_disposalTask"));
        held.TrySetResult();
        await terminal;
        await reenteredSnapshot;
        Assert.True(terminal.IsCompletedSuccessfully);
    });

    [Fact]
    public Task Retained_failure_capacity_refuses_further_real_dispatch_and_remains_sticky() => Run(async rig =>
    {
        var calls = 0;
        var button = rig.Mount(new CallbackDispatcher((_, _, _) =>
        {
            calls++;
            var cause = new InvalidOperationException($"original retained failure {calls}");
            rig.Expect(cause);
            return new ValueTask(Task.FromException(cause));
        }));
        for (var index = 0; index < 128; index++) Click(button);
        Assert.Equal(128, calls);
        Assert.Equal(128, Field<HashSet<Task>>(rig.Loader, "_pendingActionTasks").Count);
        Click(button);
        var refusal = Field<Task>(rig.Loader, "_admissionFailureTask");
        var cause = Assert.IsType<InvalidOperationException>(Assert.Single(refusal.Exception!.InnerExceptions));
        rig.Expect(cause);
        Assert.Equal(128, calls);
        Assert.True(refusal.IsFaulted);
        Click(button);
        Assert.Equal(128, calls);
        Assert.Same(refusal, Field<Task>(rig.Loader, "_admissionFailureTask"));
        var snapshot = rig.Loader.WhenActionsIdleAsync();
        Assert.NotNull(await Record.ExceptionAsync(() => snapshot));
        Assert.True(snapshot.IsFaulted);
    });

    [Fact]
    public Task Compound_cancel_unsubscribe_and_real_dispatch_faults_survive_independent_cleanup() => Run(async rig =>
    {
        var cancellation = new InvalidOperationException("original cancellation callback cause");
        var unsubscribe = new IOException("original binding unsubscribe cause");
        var dispatcher = new ApplicationException("original held dispatcher cause");
        rig.Expect(cancellation, unsubscribe, dispatcher);
        var bindings = new ThrowingBindings(unsubscribe);
        rig.Loader.SetBindingContext(bindings);
        var held = rig.Hold();
        CancellationTokenRegistration registration = default;
        rig.Release(() => registration.Dispose());
        var button = rig.Mount(new CallbackDispatcher((_, _, token) =>
        {
            registration = token.Register(() => throw cancellation);
            return new ValueTask(held.Task);
        }));
        Click(button);
        var syncError = Record.Exception(() => rig.Loader.Dispose());
        Assert.NotNull(syncError);
        Assert.True(ContainsExact(syncError, cancellation));
        Assert.True(ContainsExact(syncError, unsubscribe));
        Assert.Equal(1, bindings.RemoveCalls);
        var terminal = Field<Task>(rig.Loader, "_disposalTask");
        Assert.False(terminal.IsCompleted);
        held.TrySetException(dispatcher);
        var terminalError = await Record.ExceptionAsync(() => terminal);
        Assert.NotNull(terminalError);
        Assert.True(terminal.IsFaulted);
        Assert.True(ContainsExact(terminalError, cancellation));
        Assert.True(ContainsExact(terminalError, unsubscribe));
        Assert.True(ContainsExact(terminalError, dispatcher));
        rig.Loader.Dispose();
        Assert.Equal(1, bindings.RemoveCalls);
        Assert.Same(terminal, Field<Task>(rig.Loader, "_disposalTask"));
    });

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Field<T>(object owner, string name) where T : class =>
        Assert.IsAssignableFrom<T>((owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(owner.GetType().FullName, name)).GetValue(owner));

    // Observational traversal only: never transforms the actual Task payload or treats
    // an unknown empty group as success. An exact opaque known cause remains visible.
    private static bool ContainsExact(Exception error, Exception expected) =>
        ReferenceEquals(error, expected) || error is AggregateException group &&
        group.InnerExceptions.Any(cause => ContainsExact(cause, expected));

    private static async Task Run(Func<Rig, Task> body)
    {
        HeadlessUnitTestSession? session = null;
        var failures = new List<Exception>();
        void Add(Exception error)
        {
            if (!failures.Any(previous => ReferenceEquals(previous, error))) failures.Add(error);
        }
        try
        {
            session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
            await session.Dispatch(async () =>
            {
                var rig = new Rig();
                try
                {
                    rig.Acquire();
                    await body(rig);
                }
                catch (Exception error) { Add(error); }
                finally
                {
                    // Release actual holds on every failed assertion/acquisition path.
                    foreach (var release in rig.Releases)
                    {
                        try { release(); }
                        catch (Exception error) { Add(error); }
                    }
                    if (rig.Acquired is { } loader)
                    {
                        try { loader.Dispose(); }
                        catch (Exception error) { if (!rig.Known(error)) Add(error); }
                        Task? terminalSnapshot = null;
                        try
                        {
                            terminalSnapshot = loader.WhenActionsIdleAsync();
                            await terminalSnapshot;
                        }
                        catch (Exception error)
                        {
                            if (terminalSnapshot?.Exception is { InnerExceptions.Count: > 0 } group)
                            {
                                foreach (var cause in group.InnerExceptions)
                                    if (!rig.Known(cause)) Add(cause);
                            }
                            else if (!rig.Known(error)) Add(error);
                        }
                    }
                }
                return 0;
            }, CancellationToken.None);
        }
        catch (Exception error) { Add(error); }
        finally
        {
            if (session is not null)
            {
                try { await session.DisposeAsync(); }
                catch (Exception error) { Add(error); }
            }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual fixture and independent cleanup failures.", failures);
    }

    private sealed class Rig
    {
        internal CuiControlLoader? Acquired { get; private set; }
        internal CuiControlLoader Loader => Acquired ?? throw new InvalidOperationException("Original loader not acquired.");
        internal List<Action> Releases { get; } = [];
        private readonly List<Exception> _known = [];
        internal void Acquire() => Acquired = new CuiControlLoader();
        internal void Expect(params Exception[] causes) => _known.AddRange(causes);
        internal bool Known(Exception error) => _known.Any(cause => ReferenceEquals(cause, error)) ||
            error is AggregateException group && group.InnerExceptions.Count != 0 && group.InnerExceptions.All(Known);
        internal void Release(Action callback) => Releases.Add(callback);
        internal TaskCompletionSource Hold()
        {
            var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Release(() => held.TrySetResult());
            return held;
        }
        internal Button Mount(ICuiActionDispatcher dispatcher)
        {
            Loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = Loader.LoadMarkup("<Cui><Actions><Action name=\"Run\" command=\"test.run\" /></Actions><Page><Button action=\"Run\">Run</Button></Page></Cui>");
            Assert.Empty(diagnostics);
            Loader.WireBindings(root!);
            return Assert.IsType<Button>(Assert.Single(Assert.IsType<Panel>(root).Children));
        }
    }

    private sealed class CallbackDispatcher(Func<string, object?, CancellationToken, ValueTask> callback) : ICuiActionDispatcher
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
            callback(command, parameter, cancellationToken);
    }

    private sealed class ThrowingBindings(Exception removeCause) : ICuiBindingContext, INotifyPropertyChanged
    {
        internal int RemoveCalls { get; private set; }
        public bool TryGetValue(string path, out object? value) { value = null; return false; }
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { RemoveCalls++; throw removeCause; }
        }
    }
}
