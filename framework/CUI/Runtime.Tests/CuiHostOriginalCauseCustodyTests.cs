using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>Actual host/readiness/native-callback controls. These controlled services provide
/// no Home authority, native window presentation or installed acceptance witness.</summary>
public sealed class CuiHostOriginalCauseCustodyTests
{
    [Fact]
    public Task Synchronous_readiness_OCE_is_a_fault_with_the_original_identity_at_Show_and_Close() => Run(async rig =>
    {
        var cause = new OperationCanceledException("original synchronous readiness fault");
        var show = rig.Host.ShowAsync(Scene(new Readiness(_ => throw cause)));
        var showError = await Record.ExceptionAsync(() => show);
        Assert.True(show.IsFaulted);
        Assert.False(show.IsCanceled);
        var group = Assert.IsType<AggregateException>(showError);
        Assert.Same(cause, Assert.Single(group.InnerExceptions));
        rig.Expect(group);
        var close = rig.Host.CloseOriginalAsync();
        Assert.Same(close, rig.Host.CloseOriginalAsync());
        var closeError = await Record.ExceptionAsync(() => close);
        Assert.True(close.IsFaulted);
        Assert.Same(group, closeError);
        Assert.Same(cause, Assert.Single(Assert.IsType<AggregateException>(closeError).InnerExceptions));
    });

    [Fact]
    public Task Actual_faulted_readiness_Task_keeps_OCE_fault_status_and_identity() => Run(async rig =>
    {
        var cause = new OperationCanceledException("original fault payload, not canceled Task");
        var actual = Task.FromException<CuiSceneAvailability>(cause);
        var show = rig.Host.ShowAsync(Scene(new Readiness(_ => new(actual))));
        var error = Assert.IsType<AggregateException>(await Record.ExceptionAsync(() => show));
        rig.Expect(error);
        Assert.True(actual.IsFaulted);
        Assert.False(actual.IsCanceled);
        Assert.Same(actual, PublicationTask(rig.Host, "Readiness"));
        Assert.True(show.IsFaulted);
        Assert.Same(cause, Assert.Single(error.InnerExceptions));
        var close = rig.Host.CloseOriginalAsync();
        Assert.Same(error, await Record.ExceptionAsync(() => close));
        Assert.True(close.IsFaulted);
    });

    [Fact]
    public Task Actual_canceled_readiness_keeps_its_state_and_close_retains_the_exact_observed_cause() => Run(async rig =>
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var actual = Task.FromCanceled<CuiSceneAvailability>(cancellation.Token);
        var show = rig.Host.ShowAsync(Scene(new Readiness(_ => new(actual))));
        var showError = await Record.ExceptionAsync(() => show);
        Assert.IsAssignableFrom<OperationCanceledException>(showError);
        Assert.True(show.IsCanceled);
        Assert.Null(show.Exception);
        Assert.Same(actual, PublicationTask(rig.Host, "Readiness"));
        var recorded = Assert.IsAssignableFrom<OperationCanceledException>(PublicationField(rig.Host, "ReadinessCancellation"));
        Assert.Equal(cancellation.Token, recorded.CancellationToken);
        // The underlying producer supplied a canceled Task with no Exception payload. This
        // asserts the actual caught observation, not reconstruction of an inaccessible cause.
        var close = rig.Host.CloseOriginalAsync();
        var closeError = Assert.IsType<AggregateException>(await Record.ExceptionAsync(() => close));
        rig.Expect(closeError);
        Assert.True(close.IsFaulted);
        Assert.False(close.IsCanceled);
        Assert.Same(recorded, Assert.Single(closeError.InnerExceptions));
    });

    [Fact]
    public Task Actual_readiness_two_faults_survive_Show_and_same_close_without_own_wrapper_duplication() => Run(async rig =>
    {
        var first = new InvalidOperationException("original readiness first");
        var second = new IOException("original readiness second");
        var returned = new TaskCompletionSource<CuiSceneAvailability>();
        returned.SetException([first, second]);
        var show = rig.Host.ShowAsync(Scene(new Readiness(_ => new(returned.Task))));
        var showError = Assert.IsType<AggregateException>(await Record.ExceptionAsync(() => show));
        rig.Expect(showError);
        Assert.Equal(2, returned.Task.Exception!.InnerExceptions.Count);
        Assert.Same(returned.Task, PublicationTask(rig.Host, "Readiness"));
        Assert.Equal(2, showError.InnerExceptions.Count);
        Assert.Same(first, showError.InnerExceptions[0]);
        Assert.Same(second, showError.InnerExceptions[1]);
        var close = rig.Host.CloseOriginalAsync();
        Assert.Same(showError, await Record.ExceptionAsync(() => close));
        Assert.Same(close, rig.Host.OriginalClose);
        Assert.True(close.IsFaulted);
    });

    [Fact]
    public Task Predicate_OCE_is_not_misclassified_as_owner_withdrawal() => Run(async rig =>
    {
        var cause = new OperationCanceledException("original predicate body fault");
        var scene = Scene(new Readiness(_ => new(Ready))) with { IsPublicationCurrent = () => throw cause };
        var show = rig.Host.ShowAsync(scene);
        var error = Assert.IsType<AggregateException>(await Record.ExceptionAsync(() => show));
        rig.Expect(error);
        Assert.True(show.IsFaulted);
        Assert.False(show.IsCanceled);
        Assert.Same(cause, Assert.Single(error.InnerExceptions));
        Assert.Null(rig.Host.Content);
        var close = rig.Host.CloseOriginalAsync();
        Assert.Same(error, await Record.ExceptionAsync(() => close));
        Assert.True(close.IsFaulted);
    });

    [Fact]
    public Task Factory_fault_and_terminal_unsubscribe_share_exact_causes_without_an_extra_close_wrapper() => Run(async rig =>
    {
        var primary = new InvalidOperationException("original factory fault");
        var unsubscribe = new IOException("original terminal unsubscribe fault");
        rig.Registry.RegisterObjectRenderer("fault", _ => throw primary);
        var bindings = new FaultingBindings(unsubscribe);
        var scene = Scene(new Readiness(_ => new(Ready))) with
        {
            Bindings = bindings,
            Document = new CuiRichParser().Parse("<Cui><Object Type=\"fault\" /></Cui>")
        };
        var show = rig.Host.ShowAsync(scene);
        var error = Assert.IsType<AggregateException>(await Record.ExceptionAsync(() => show));
        rig.Expect(error);
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Same(primary, error.InnerExceptions[0]);
        Assert.Same(unsubscribe, error.InnerExceptions[1]);
        var close = rig.Host.CloseOriginalAsync();
        var terminal = await Record.ExceptionAsync(() => close);
        Assert.Same(error, terminal);
        Assert.Equal(1, bindings.Removes);
        Assert.True(close.IsFaulted);
        Assert.Null(rig.Host.Content);
    });

    [Fact]
    public Task Predicate_reentrant_close_returning_true_refuses_the_next_native_write_with_same_owner_cause() => Run(async rig =>
    {
        Task? actualClose = null;
        var callbacks = 0;
        var scene = Scene(new Readiness(_ => new(Ready))) with
        {
            IsPublicationCurrent = () =>
            {
                callbacks++;
                actualClose = rig.Host.CloseOriginalAsync();
                return true;
            }
        };
        var show = rig.Host.ShowAsync(scene);
        var error = Assert.IsAssignableFrom<OperationCanceledException>(await Record.ExceptionAsync(() => show));
        rig.Expect(error);
        Assert.Equal(1, callbacks);
        Assert.True(show.IsCanceled);
        Assert.Null(rig.Host.Content);
        Assert.Same(actualClose, rig.Host.OriginalClose);
        var retainedOwnerCause = typeof(CuiSceneHost).GetField("_ownerClosedRefusal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(rig.Host);
        Assert.Same(retainedOwnerCause, error);
        Assert.Same(error, await Record.ExceptionAsync(() => actualClose!));
        Assert.True(actualClose!.IsCanceled);
        Assert.Null(actualClose.Exception);
    });

    [Fact]
    public Task Predicate_reentrant_token_cancel_returning_true_refuses_native_acquisition() => Run(async rig =>
    {
        using var cancellation = new CancellationTokenSource();
        var callbacks = 0;
        CancellationToken actualReadinessToken = default;
        var scene = Scene(new Readiness(token => { actualReadinessToken = token; return new(Ready); })) with
        {
            IsPublicationCurrent = () => { callbacks++; cancellation.Cancel(); return true; }
        };
        var show = rig.Host.ShowAsync(scene, cancellation.Token);
        var error = Assert.IsAssignableFrom<OperationCanceledException>(await Record.ExceptionAsync(() => show));
        rig.Expect(error);
        Assert.Equal(1, callbacks);
        Assert.Equal(actualReadinessToken, error.CancellationToken);
        Assert.True(actualReadinessToken.IsCancellationRequested);
        Assert.True(show.IsCanceled);
        Assert.Null(rig.Host.Content);
        var actualClose = rig.Host.CloseOriginalAsync();
        Assert.Same(error, await Record.ExceptionAsync(() => actualClose));
        Assert.True(actualClose.IsCanceled);
    });

    private static readonly CuiSceneAvailability Ready = new(CuiSceneAvailabilityState.Ready, "fixture-ready", "Controlled readiness result.");
    private static CuiNativeScene Scene(ICuiSceneReadiness readiness)
    {
        var model = new CuiViewModel();
        return new("fixture", "Task custody", "Home", new CuiRichParser().Parse("<Cui><TextBlock Text=\"Original\" /></Cui>"),
            model, model, readiness) { IsPublicationCurrent = () => true };
    }
    private static Task PublicationTask(CuiSceneHost host, string name) => Assert.IsAssignableFrom<Task>(PublicationField(host, name));
    private static object? PublicationField(CuiSceneHost host, string name)
    {
        var originals = Assert.IsAssignableFrom<System.Collections.IEnumerable>(typeof(CuiSceneHost)
            .GetField("_originalPublications", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host));
        var original = Assert.Single(originals.Cast<object>());
        return original.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(original);
    }
    private static async Task Run(Func<Rig, Task> body)
    {
        HeadlessUnitTestSession? session = null;
        var errors = new List<Exception>();
        try
        {
            session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
            await session.Dispatch(async () =>
            {
                var rig = new Rig();
                try
                {
                    rig.Host = new CuiSceneHost(rig.Registry);
                    await body(rig);
                }
                catch (Exception error) { Add(errors, error); }
                finally
                {
                    if (rig.Host is { } host)
                    {
                        Task? actualClose = null;
                        try { actualClose = host.CloseOriginalAsync(); } catch (Exception error) { Add(errors, error); }
                        if (actualClose is not null)
                            try { await actualClose; }
                            catch (Exception error)
                            {
                                if (!rig.Expected.Any(cause => ReferenceEquals(cause, error))) Add(errors, error);
                            }
                    }
                }
                return 0;
            }, CancellationToken.None);
        }
        catch (Exception error) { Add(errors, error); }
        finally
        {
            if (session is not null)
                try { await session.DisposeAsync(); } catch (Exception error) { Add(errors, error); }
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original host fixture and independent cleanup failed.", errors);
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(previous => ReferenceEquals(previous, error))) errors.Add(error); }
    private sealed class Rig
    {
        internal readonly CuiControlRegistry Registry = new();
        internal CuiSceneHost Host = null!;
        internal readonly List<Exception> Expected = [];
        internal void Expect(Exception error) => Expected.Add(error);
    }
    private sealed class Readiness(Func<CancellationToken, ValueTask<CuiSceneAvailability>> original) : ICuiSceneReadiness
    { public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => original(token); }
    private sealed class FaultingBindings(Exception failure) : ICuiBindingContext, INotifyPropertyChanged
    {
        internal int Removes;
        public bool TryGetValue(string path, out object? value) { value = null; return false; }
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { Removes++; throw failure; } }
    }
}
