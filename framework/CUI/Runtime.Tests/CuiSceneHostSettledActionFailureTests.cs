using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiSceneHostSettledActionFailureTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);
    private static readonly CuiSceneAvailability Ready = new(CuiSceneAvailabilityState.Ready, "fixture-ready", "Controlled readiness result.");

    private sealed class TestSettledFailure : Exception, ICuiSettledActionFailure
    {
        public TestSettledFailure() : base("verified effect-free admission refusal") { }
        public bool IsVerified => true;
    }

    private sealed class SettlingAction : ICuiActionDispatcher
    {
        public int Calls { get; private set; }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromException(new TestSettledFailure());
        }
    }

    private sealed class ForgedAction : ICuiActionDispatcher
    {
        public int Calls { get; private set; }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromException(new ForgedSettledFailure());
        }
    }

    private sealed class ForgedSettledFailure : Exception, ICuiSettledActionFailure
    {
        public ForgedSettledFailure() : base("forged settled failure") { }
        public bool IsVerified => true;
    }

    private static CuiNativeScene Scene(ICuiSceneReadiness readiness, ICuiActionDispatcher actions) =>
        new("fixture", "Settled action", "Home",
            new CuiRichParser().Parse("<Cui><Actions><Action name=\"go\" command=\"go\"/></Actions><Page><Button Text=\"Go\"><ActionEvent Type=\"Clicked\" Action=\"go\"/></Button></Page></Cui>"),
            new CuiViewModel(), new CuiViewModel(), readiness)
        { IsPublicationCurrent = () => true, Actions = actions };

    private static async Task Run(Func<Rig, Task> body, Func<Exception, bool>? validator = null)
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
                    rig.Host = new CuiSceneHost(rig.Registry, validator);
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

    private sealed class Readiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Ready);
    }

    [Fact]
    public Task Verified_settled_action_failure_does_not_poison_close_and_stays_inspectable() => Run(async rig =>
    {
        var action = new SettlingAction();
        var scene = Scene(new Readiness(), action);
        await rig.Host.ShowAsync(scene).WaitAsync(Bound);
        var panel = rig.Host.Content as Avalonia.Controls.Panel;
        var buttons = (panel?.Children.OfType<Button>() ?? Enumerable.Empty<Button>()).ToArray();
        var button = Assert.Single(buttons);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        // Flush the retained action so its fault is recorded before close.
        for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Assert.NotNull(rig.Host.LastActionFailure);
        Assert.Equal("CUIA_FAILED", rig.Host.LastActionFailure!.Code);
        // Close must complete because the verified settled failure is not a retirement duty.
        var close = rig.Host.CloseOriginalAsync();
        await close.WaitAsync(Bound);
        Assert.True(close.IsCompletedSuccessfully);
    }, validator: e => e is TestSettledFailure);

    [Fact]
    public Task Forged_marker_without_verified_proof_keeps_close_failing() => Run(async rig =>
    {
        var action = new ForgedAction();
        var scene = Scene(new Readiness(), action);
        await rig.Host.ShowAsync(scene).WaitAsync(Bound);
        var panel = rig.Host.Content as Avalonia.Controls.Panel;
        var buttons = (panel?.Children.OfType<Button>() ?? Enumerable.Empty<Button>()).ToArray();
        var button = Assert.Single(buttons);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Assert.NotNull(rig.Host.LastActionFailure);
        // The forged marker must NOT be honoured: close must fault.
        var close = rig.Host.CloseOriginalAsync();
        var failure = await Record.ExceptionAsync(() => close.WaitAsync(Bound));
        Assert.NotNull(failure);
        rig.Expect(failure!);
    }, validator: e => false);
}
