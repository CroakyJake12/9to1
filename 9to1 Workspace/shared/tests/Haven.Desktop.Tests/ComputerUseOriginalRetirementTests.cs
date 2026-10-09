using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

// Real controller, raw dispatcher Tasks and actual headless Window callbacks.
// Dispatcher probes observe custody only; these cases do not certify Windows activation or business-process retirement.
public sealed class ComputerUseOriginalRetirementTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);
    [AvaloniaFact]
    public async Task Held_dispatcher_original_keeps_windows_until_terminal_without_stopping_business_controller()
    {
        using var controller = new ComputerUseSessionController();
        var actual = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ComputerUseOverlayCoordinator? host = null; var windows = new List<Window>(); var failures = new List<Exception>(); IDisposable? business = null;
        Action? queued = null;
        try
        {
            host = new(controller, callback => { Assert.NotNull(host!.LastOriginalUpdate); queued = callback; return actual.Task; }, () => CaptureWindow(windows));
            Show(windows); business = controller.BeginSession(); var original = host.LastOriginalUpdate!;
            var close = host.CloseAndDrainAsync(); Assert.Same(close, host.CloseAndDrainAsync()); Assert.False(close.IsCompleted);
            Assert.True(controller.State.IsActive); Assert.False(controller.StopToken.IsCancellationRequested);
            Assert.All(windows, window => Assert.NotNull(window.Content)); queued!(); actual.TrySetResult();
            await original.WaitAsync(Bound); await close.WaitAsync(Bound); Assert.All(windows, window => Assert.Null(window.Content));
            Assert.True(controller.State.IsActive); Assert.False(controller.StopToken.IsCancellationRequested);
        }
        catch (Exception cause) { Add(failures, cause); }
        finally { actual.TrySetResult(); await Retire(host, windows, failures); business?.Dispose(); }
        Throw(failures);
    }
    [AvaloniaFact]
    public async Task Queued_callback_after_retirement_does_not_apply_visual_effects_but_its_original_still_joins()
    {
        using var controller = new ComputerUseSessionController(); var actual = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ComputerUseOverlayCoordinator? host = null; var windows = new List<Window>(); var failures = new List<Exception>(); IDisposable? business = null; Action? queued = null;
        try
        {
            host = new(controller, callback => { queued = callback; return actual.Task; }, () => CaptureWindow(windows));
            Show(windows); business = controller.BeginSession(); var detail = Field<TextBlock>(host, "_detail"); var before = detail.Text;
            host.RequestRetirement(); var close = host.OriginalClose!; queued!();
            Assert.Equal(before, detail.Text); Assert.False(close.IsCompleted); actual.TrySetResult(); await close.WaitAsync(Bound);
            Assert.False(controller.StopToken.IsCancellationRequested); Assert.True(controller.State.IsActive);
        }
        catch (Exception cause) { Add(failures, cause); }
        finally { actual.TrySetResult(); await Retire(host, windows, failures); business?.Dispose(); }
        Throw(failures);
    }
    [AvaloniaFact]
    public async Task Actual_dispatcher_fault_group_keeps_both_original_members_at_repeated_close()
    {
        using var controller = new ComputerUseSessionController(); var first = new IOException("Actual dispatcher first"); var second = new InvalidOperationException("Actual dispatcher second");
        var actual = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ComputerUseOverlayCoordinator? host = null; var windows = new List<Window>(); var failures = new List<Exception>(); IDisposable? business = null; Task? observedClose = null;
        try
        {
            host = new(controller, _ => actual.Task, () => CaptureWindow(windows)); Show(windows); business = controller.BeginSession();
            actual.TrySetException([first, second]); var close = host.CloseAndDrainAsync(); var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.Contains(Causes(terminal), cause => ReferenceEquals(cause, first)); Assert.Contains(Causes(terminal), cause => ReferenceEquals(cause, second));
            Assert.Same(close, host.CloseAndDrainAsync()); Assert.True(actual.Task.IsFaulted); observedClose = close;
        }
        catch (Exception cause) { Add(failures, cause); }
        finally { actual.TrySetResult(); await Retire(host, windows, failures, observedClose); business?.Dispose(); }
        Throw(failures);
    }
    [AvaloniaFact]
    public async Task Direct_dispatcher_cancellation_without_raw_Task_retains_same_original_as_fault()
    {
        using var controller = new ComputerUseSessionController(); var original = new OperationCanceledException("Actual synchronous dispatcher acquisition");
        ComputerUseOverlayCoordinator? host = null; var windows = new List<Window>(); var failures = new List<Exception>(); IDisposable? business = null; Task? observedClose = null;
        try
        {
            host = new(controller, _ => throw original, () => CaptureWindow(windows)); Show(windows); business = controller.BeginSession();
            var returned = host.LastOriginalUpdate!; var failure = await Assert.ThrowsAnyAsync<Exception>(() => returned);
            Assert.True(returned.IsFaulted); Assert.False(returned.IsCanceled); Assert.Contains(Causes(failure), cause => ReferenceEquals(cause, original));
            var close = host.CloseAndDrainAsync(); var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.Contains(Causes(terminal), cause => ReferenceEquals(cause, original)); Assert.True(close.IsFaulted); observedClose = close;
        }
        catch (Exception cause) { Add(failures, cause); }
        finally { await Retire(host, windows, failures, observedClose); business?.Dispose(); }
        Throw(failures);
    }
    [AvaloniaFact]
    public async Task Genuine_canceled_dispatcher_Task_remains_canceled_and_prevents_a_clean_close()
    {
        using var controller = new ComputerUseSessionController(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var actual = Task.FromCanceled(cancellation.Token); ComputerUseOverlayCoordinator? host = null; var windows = new List<Window>();
        var failures = new List<Exception>(); IDisposable? business = null; Task? observedClose = null;
        try
        {
            host = new(controller, _ => actual, () => CaptureWindow(windows)); Show(windows); business = controller.BeginSession();
            var close = host.CloseAndDrainAsync(); var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(actual.IsCanceled); Assert.Null(actual.Exception); Assert.Contains(Causes(terminal), cause => cause is OperationCanceledException);
            Assert.False(close.IsCompletedSuccessfully); Assert.False(controller.StopToken.IsCancellationRequested); observedClose = close;
        }
        catch (Exception cause) { Add(failures, cause); }
        finally { await Retire(host, windows, failures, observedClose); business?.Dispose(); }
        Throw(failures);
    }
    [AvaloniaFact]
    public async Task Restored_context_dispatcher_callback_refuses_self_join_before_returning_raw_Task()
    {
        using var controller = new ComputerUseSessionController(); var external = ExecutionContext.Capture()!;
        var actual = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var refused = false;
        ComputerUseOverlayCoordinator? host = null; var windows = new List<Window>(); var failures = new List<Exception>(); IDisposable? business = null;
        try
        {
            host = new(controller, _ =>
            {
                ExecutionContext.Run(external, _ =>
                {
                    Assert.Throws<InvalidOperationException>(() => { host!.CloseAndDrainAsync().GetAwaiter().GetResult(); });
                    host!.RequestRetirement(); Assert.False(host.OriginalClose!.IsCompleted); refused = true;
                }, null);
                return actual.Task;
            }, () => CaptureWindow(windows));
            Show(windows); business = controller.BeginSession(); Assert.True(refused); Assert.False(host.OriginalClose!.IsCompleted);
            actual.TrySetResult(); await host.CloseAndDrainAsync().WaitAsync(Bound); Assert.True(controller.State.IsActive);
        }
        catch (Exception cause) { Add(failures, cause); }
        finally { actual.TrySetResult(); await Retire(host, windows, failures); business?.Dispose(); }
        Throw(failures);
    }
    [AvaloniaFact]
    public async Task Actual_native_Closed_callback_restored_context_cannot_join_encompassing_cleanup()
    {
        using var controller = new ComputerUseSessionController(); var external = ExecutionContext.Capture()!;
        ComputerUseOverlayCoordinator? host = null; var windows = new List<Window>(); var failures = new List<Exception>(); var refused = false;
        EventHandler callback = (_, _) => ExecutionContext.Run(external, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { host!.CloseAndDrainAsync().GetAwaiter().GetResult(); }); refused = true;
        }, null);
        try
        {
            host = new(controller, action => { action(); return Task.CompletedTask; }, () => CaptureWindow(windows)); Show(windows);
            windows[0].Closed += callback; await host.CloseAndDrainAsync().WaitAsync(Bound); Assert.True(refused);
            Assert.All(windows, window => Assert.Null(window.Content));
        }
        catch (Exception cause) { Add(failures, cause); }
        finally { if (windows.Count > 0) windows[0].Closed -= callback; await Retire(host, windows, failures); }
        Throw(failures);
    }
    [AvaloniaFact]
    public async Task Failed_constructor_retains_first_returned_window_and_same_cleanup_original_before_next_effect()
    {
        using var controller = new ComputerUseSessionController(); var original = new IOException("Actual first Window Title callback");
        var windows = new List<Window>(); var failures = new List<Exception>(); Task? observedClose = null; Task? acquiredClose = null; var beforeWidth = double.NaN;
        EventHandler<AvaloniaPropertyChangedEventArgs> callback = (_, args) => { if (args.Property == Window.TitleProperty) throw original; };
        try
        {
            var failure = Assert.Throws<ComputerUseOverlayAcquisitionException>(() =>
                new ComputerUseOverlayCoordinator(controller, action => { action(); return Task.CompletedTask; }, () =>
                {
                    var actual = CaptureWindow(windows); beforeWidth = actual.Width; actual.PropertyChanged += callback; return actual;
                }));
            acquiredClose = failure.OriginalRetirement; // Capture actual partial-product cleanup before any fixture assertion.
            Assert.Same(original, failure.InnerException); Assert.Single(windows); Assert.Equal("Haven Computer Use controls", windows[0].Title);
            Assert.Equal(beforeWidth, windows[0].Width); // Later original Width setter was not admitted after this callback failure.
            var terminal = await Assert.ThrowsAnyAsync<Exception>(() => failure.OriginalRetirement);
            Assert.Contains(Causes(terminal), cause => ReferenceEquals(cause, original)); Assert.True(failure.OriginalRetirement.IsFaulted);
            observedClose = failure.OriginalRetirement;
        }
        catch (Exception cause) { Add(failures, cause); }
        finally
        {
            foreach (var actual in windows) actual.PropertyChanged -= callback;
            await Retire(null, windows, failures, observedClose, acquiredClose);
        }
        Throw(failures);
    }
    [AvaloniaFact]
    public async Task Actual_native_close_veto_retains_content_and_cannot_be_promoted_from_window_visibility()
    {
        using var controller = new ComputerUseSessionController(); ComputerUseOverlayCoordinator? host = null;
        var windows = new List<Window>(); var failures = new List<Exception>(); Task? observedClose = null;
        EventHandler<WindowClosingEventArgs> veto = (_, args) => args.Cancel = true;
        try
        {
            host = new(controller, action => { action(); return Task.CompletedTask; }, () => CaptureWindow(windows)); Show(windows); windows[0].Closing += veto;
            var close = host.CloseAndDrainAsync(); var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.Contains(Causes(terminal), cause => cause.Message.Contains("without its owning Closed callback", StringComparison.Ordinal));
            Assert.True(windows[0].IsVisible); Assert.NotNull(windows[0].Content); Assert.Same(close, host.CloseAndDrainAsync()); observedClose = close;
        }
        catch (Exception cause) { Add(failures, cause); }
        finally { if (windows.Count > 0) windows[0].Closing -= veto; await Retire(host, windows, failures, observedClose); }
        Throw(failures);
    }
    private static Window CaptureWindow(List<Window> windows) { var actual = new Window(); windows.Add(actual); return actual; }
    private static void Show(IEnumerable<Window> windows) { foreach (var actual in windows) actual.Show(); }
    private static T Field<T>(ComputerUseOverlayCoordinator host, string name) =>
        (T)typeof(ComputerUseOverlayCoordinator).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
    private static async Task Retire(ComputerUseOverlayCoordinator? host, IEnumerable<Window> windows, List<Exception> failures, Task? observedClose = null, Task? acquiredClose = null)
    {
        Task? close = host?.OriginalClose ?? acquiredClose;
        if (host is not null)
        {
            try { host.RequestRetirement(); close = host.OriginalClose; } catch (Exception cause) { Add(failures, cause); }
        }
        var closes = new List<Task>();
        if (close is not null) closes.Add(close);
        foreach (var failure in failures.ToArray())
            foreach (var original in Causes(failure).OfType<ComputerUseOverlayAcquisitionException>()) closes.Add(original.OriginalRetirement);
        foreach (var actual in closes.Distinct<Task>(ReferenceEqualityComparer.Instance))
        {
            try { await actual.WaitAsync(Bound); }
            catch (Exception cause)
            {
                if (!ReferenceEquals(actual, observedClose))
                {
                    if (actual.Exception is { InnerExceptions.Count: > 0 } group) foreach (var original in group.InnerExceptions) Add(failures, original);
                    else Add(failures, cause);
                }
            }
        }
        foreach (var actual in windows) try { actual.Close(); } catch (Exception cause) { Add(failures, cause); }
    }
    private static void Add(List<Exception> failures, Exception cause)
    { if (!failures.Any(actual => ReferenceEquals(actual, cause))) failures.Add(cause); }
    private static IEnumerable<Exception> Causes(Exception original)
    { yield return original; if (original is AggregateException group) { foreach (var child in group.InnerExceptions) foreach (var cause in Causes(child)) yield return cause; } else if (original.InnerException is { } child) foreach (var cause in Causes(child)) yield return cause; }
    private static void Throw(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Computer Use control and independent original cleanup failed.", failures);
    }
}
