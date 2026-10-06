using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

// Actual headless windows and controlled content originals. Private enrollment
// observes native-owner custody only; it never overrides Windows-only Present.
public sealed class FloatingOriginalRetirementTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);
    [Fact]
    public void Maintained_sealed_wrapper_captures_same_immutable_content_without_a_producer()
    {
        var actual = new object(); var wrapper = new DesktopFloatingActivityContent(actual, "Actual content");
        Assert.Same(actual, wrapper.Content); Assert.Equal("Actual content", wrapper.AutomationName);
        Assert.True(typeof(DesktopFloatingActivityContent).IsSealed);
        Assert.False((object)wrapper is IDesktopOriginalRetirementParticipant);
    }
    [AvaloniaFact]
    public async Task Held_actual_content_close_keeps_same_native_window_content_until_terminal()
    {
        var host = new DesktopFloatingActivityHost(new FloatingActivityStateStore());
        var content = new ActualContent(); var window = new Window(); Task? close = null; var failures = new List<Exception>();
        try
        {
            window.Content = content;
            Enroll(host, new DesktopFloatingActivityContent(content, "Actual held child"), content, window); window.Show();
            close = host.CloseAndDrainAsync(); Assert.True(content.RetirementRequested);
            Assert.False(close.IsCompleted); Assert.Same(content, window.Content);
            Assert.False(content.OriginalClose.IsCanceled); content.Release.TrySetResult();
            await close.WaitAsync(Bound); Assert.Null(window.Content);
            Assert.Same(close, host.CloseAndDrainAsync()); Assert.True(content.OriginalClose.IsCompletedSuccessfully);
        }
        catch (Exception primary) { failures.Add(primary); }
        finally
        {
            content.Release.TrySetResult();
            await RetireFixture(host, [window], failures, childTasks: [content.OriginalClose]);
        }
        ThrowFixture(failures);
    }
    [AvaloniaFact]
    public async Task Arbitrary_wrapper_does_not_certify_even_static_underlying_content()
    {
        var host = new DesktopFloatingActivityHost(new FloatingActivityStateStore()); var wrapper = new UnknownWrapper("Static");
        Invoke(host, "RetainOriginalFloatingWrapper", wrapper); Invoke(host, "RetainOriginalFloatingContent", wrapper.Content);
        host.RequestRetirement(); var actual = host.OriginalClose!;
        var error = await Assert.ThrowsAsync<DesktopOriginalRetirementUnavailableException>(() => actual);
        Assert.Equal(typeof(UnknownWrapper), error.ActualOwnerType); Assert.True(actual.IsFaulted);
        Assert.Throws<DesktopOriginalRetirementUnavailableException>(() => { _ = host.CloseAndDrainAsync(); });
    }
    [AvaloniaFact]
    public async Task Ordinary_control_or_disposable_does_not_certify_child_drain()
    {
        var host = new DesktopFloatingActivityHost(new FloatingActivityStateStore());
        var content = new TextBlock { Text = "Unowned actual control" }; var window = new Window();
        var failures = new List<Exception>(); var observed = false;
        try
        {
            window.Content = content; Enroll(host, new DesktopFloatingActivityContent(content, "Unowned actual control"), content, window); window.Show();
            host.RequestRetirement(); var error = await Assert.ThrowsAsync<DesktopOriginalRetirementUnavailableException>(() => host.OriginalClose!);
            Assert.Equal(typeof(TextBlock), error.ActualOwnerType); Assert.Same(content, window.Content);
            Assert.True(window.IsVisible); observed = true;
        }
        catch (Exception primary) { failures.Add(primary); }
        finally { await RetireFixture(host, [window], failures, observed); }
        ThrowFixture(failures); // Genuine test owner closes independently; no production success inferred.
    }
    [AvaloniaFact]
    public async Task Restored_context_publication_refuses_self_join_and_returns_before_close()
    {
        var store = new FloatingActivityStateStore(); var host = new DesktopFloatingActivityHost(store);
        var external = ExecutionContext.Capture()!; var refused = false; Task? close = null;
        host.StateChanged += (_, _) => ExecutionContext.Run(external, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { host.CloseAndDrainAsync().GetAwaiter().GetResult(); });
            host.RequestRetirement(); close = host.OriginalClose; Assert.NotNull(close); Assert.False(close.IsCompleted); refused = true;
        }, null);
        try
        {
            var snapshot = Snapshot(); Assert.Equal(snapshot, await host.UpdateAsync(snapshot, CancellationToken.None));
            Assert.True(refused); await host.CloseAndDrainAsync().WaitAsync(Bound); Assert.Same(close, host.CloseAndDrainAsync());
            Assert.Equal(snapshot, store.Get(snapshot.Id));
        }
        finally { await host.CloseAndDrainAsync().WaitAsync(Bound); }
    }
    [AvaloniaFact]
    public async Task Direct_publication_cancellation_retains_exact_original_as_fault()
    {
        var host = new DesktopFloatingActivityHost(new FloatingActivityStateStore());
        var cause = new OperationCanceledException("Actual synchronous publication, no canceled I/O Task.");
        host.StateChanged += (_, _) => throw cause;
        var direct = Assert.Throws<AggregateException>(() => { _ = host.UpdateAsync(Snapshot(), CancellationToken.None); });
        Assert.Contains(Causes(direct), error => ReferenceEquals(error, cause));
        var close = host.CloseAndDrainAsync(); var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
        Assert.Contains(Causes(terminal), error => ReferenceEquals(error, cause));
        Assert.True(close.IsFaulted); Assert.False(close.IsCanceled); Assert.Same(close, host.CloseAndDrainAsync());
    }
    [AvaloniaFact]
    public async Task Whole_original_publication_fault_group_remains_at_repeated_close()
    {
        var host = new DesktopFloatingActivityHost(new FloatingActivityStateStore());
        var first = new IOException("Actual source first"); var second = new InvalidOperationException("Actual source second");
        var group = new AggregateException(first, second); host.StateChanged += (_, _) => throw group;
        Assert.Same(group, Assert.Throws<AggregateException>(() => { _ = host.UpdateAsync(Snapshot(), CancellationToken.None); }));
        var close = host.CloseAndDrainAsync(); var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
        Assert.Contains(Causes(terminal), error => ReferenceEquals(error, first));
        Assert.Contains(Causes(terminal), error => ReferenceEquals(error, second)); Assert.Same(close, host.CloseAndDrainAsync());
    }
    [AvaloniaFact]
    public async Task Actual_closed_callback_restored_context_cannot_join_its_own_cleanup()
    {
        var host = new DesktopFloatingActivityHost(new FloatingActivityStateStore()); var window = new Window(); var failures = new List<Exception>();
        var external = ExecutionContext.Capture()!; var refused = false;
        try
        {
        window.Content = "Static";
        Enroll(host, new DesktopFloatingActivityContent("Static", "Static"), "Static", window, () => ExecutionContext.Run(external, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { host.CloseAndDrainAsync().GetAwaiter().GetResult(); }); refused = true;
        }, null));
        window.Show();
        await host.CloseAndDrainAsync().WaitAsync(Bound); Assert.True(refused); Assert.Null(window.Content);
        }
        catch (Exception primary) { failures.Add(primary); }
        finally { await RetireFixture(host, [window], failures); }
        ThrowFixture(failures);
    }
    [AvaloniaFact]
    public async Task Native_close_veto_keeps_content_and_cannot_be_promoted_from_window_status()
    {
        var host = new DesktopFloatingActivityHost(new FloatingActivityStateStore()); var window = new Window(); var failures = new List<Exception>(); var observed = false;
        EventHandler<WindowClosingEventArgs> veto = (_, args) => args.Cancel = true;
        try
        {
            window.Content = "Static"; Enroll(host, new DesktopFloatingActivityContent("Static", "Static"), "Static", window);
            window.Closing += veto; window.Show();
            var close = host.CloseAndDrainAsync(); var error = await Assert.ThrowsAsync<InvalidOperationException>(() => close);
            Assert.Contains("without its owning Closed callback", error.Message); Assert.Equal("Static", window.Content);
            Assert.True(window.IsVisible); Assert.Same(close, host.CloseAndDrainAsync()); observed = true;
        }
        catch (Exception primary) { failures.Add(primary); }
        finally { await RetireFixture(host, [window], failures, observed, cleanup: [() => window.Closing -= veto]); }
        ThrowFixture(failures); // Original host failure remains retained.
    }
    [Fact]
    public async Task Finite_retention_refuses_before_another_presentation_effect_and_preserves_original_cause()
    {
        var host = new DesktopFloatingActivityHost(new FloatingActivityStateStore());
        for (var index = 0; index < 128; index++) Invoke(host, "RetainOriginalFloatingWrapper", new DesktopFloatingActivityContent(null, "Static"));
        var refusal = Assert.Throws<InvalidOperationException>(() => Invoke(host, "RetainOriginalFloatingWrapper", new DesktopFloatingActivityContent(null, "Refused")));
        var close = host.CloseAndDrainAsync(); var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
        Assert.Contains(Causes(terminal), cause => ReferenceEquals(cause, refusal)); Assert.Same(close, host.CloseAndDrainAsync());
    }
    [AvaloniaFact]
    public async Task Many_actual_healthy_closed_windows_remain_bounded_without_status_based_pruning()
    {
        var host = new DesktopFloatingActivityHost(new FloatingActivityStateStore());
        var wrapper = new DesktopFloatingActivityContent("Static", "Static"); var windows = new List<Window>(); var failures = new List<Exception>(); var observed = false;
        try
        {
            for (var index = 0; index < 128; index++)
            {
                Invoke(host, "DemandOriginalFloatingWindowCapacity");
                var actual = new Window(); windows.Add(actual); actual.Content = "Static";
                Enroll(host, wrapper, "Static", actual); actual.Show(); actual.Close();
                Assert.False(actual.IsVisible); Assert.Equal("Static", actual.Content);
            }
            // Native Closed and a healthy publication have not retired the retained
            // owning history. The 129th admission refuses before its Window factory.
            var refusal = Assert.Throws<InvalidOperationException>(() => Invoke(host, "DemandOriginalFloatingWindowCapacity"));
            var close = host.CloseAndDrainAsync(); var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.Contains(Causes(terminal), cause => ReferenceEquals(cause, refusal));
            Assert.All(windows, actual => Assert.Equal("Static", actual.Content)); Assert.Same(close, host.CloseAndDrainAsync()); observed = true;
        }
        catch (Exception primary) { failures.Add(primary); }
        finally { await RetireFixture(host, windows, failures, observed); }
        ThrowFixture(failures);
    }
    [AvaloniaFact]
    public async Task Genuine_acquired_window_is_retained_before_its_first_configuration_callback()
    {
        var host = new DesktopFloatingActivityHost(new FloatingActivityStateStore()); var failures = new List<Exception>();
        var windows = new List<Window>(); var observed = false; var cause = new IOException("Actual first Title callback.");
        try
        {
            var definition = Definition();
            var actual = (Window)InvokeValue(host, "CreateOriginalFloatingWindow", definition.Id, null)!; windows.Add(actual);
            actual.PropertyChanged += (_, args) => { if (args.Property == Window.TitleProperty) throw cause; };
            Assert.Same(cause, Assert.Throws<IOException>(() => Invoke(host, "ConfigureOriginalFloatingWindow", actual, definition, null)));
            var cohort = (System.Collections.IDictionary)typeof(DesktopFloatingActivityHost)
                .GetField("_originalFloatingWindows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
            Assert.True(cohort.Contains(actual)); Assert.Equal(definition.Title, actual.Title);
            var close = host.CloseAndDrainAsync(); var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.Contains(Causes(terminal), original => ReferenceEquals(original, cause)); observed = true;
        }
        catch (Exception primary) { failures.Add(primary); }
        finally { await RetireFixture(host, windows, failures, observed); }
        ThrowFixture(failures);
    }
    [AvaloniaFact]
    public async Task Actual_width_callback_seals_before_later_height_position_or_store_effects()
    {
        var store = new FloatingActivityStateStore(); var host = new DesktopFloatingActivityHost(store);
        var window = new Window(); var failures = new List<Exception>(); var observed = false; var id = Guid.NewGuid();
        EventHandler<AvaloniaPropertyChangedEventArgs> seal = (_, args) => { if (args.Property == Window.WidthProperty) host.RequestRetirement(); };
        try
        {
            window.Content = "Static"; window.Height = 222; Enroll(host, new DesktopFloatingActivityContent("Static", "Static"), "Static", window);
            CurrentWindows(host)[id] = window; window.PropertyChanged += seal;
            var proposal = new FloatingActivitySnapshot(id, FloatingActivityState.Presented, 555, 777, 99, 88);
            var cause = Assert.Throws<ObjectDisposedException>(() => { _ = host.UpdateAsync(proposal, CancellationToken.None); });
            Assert.Equal(555, window.Width); Assert.Equal(222, window.Height); Assert.Equal(default(PixelPoint), window.Position);
            Assert.Null(store.Get(id)); var close = host.OriginalClose!;
            var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.Contains(Causes(terminal), original => ReferenceEquals(original, cause)); observed = true;
        }
        catch (Exception primary) { failures.Add(primary); }
        finally { await RetireFixture(host, [window], failures, observed, cleanup: [() => window.PropertyChanged -= seal]); }
        ThrowFixture(failures);
    }
    [AvaloniaFact]
    public async Task Actual_store_callback_seals_before_later_ordinary_notification()
    {
        var store = new FloatingActivityStateStore(); var host = new DesktopFloatingActivityHost(store); var notifications = 0;
        store.Changed += (_, _) => host.RequestRetirement(); host.StateChanged += (_, _) => notifications++;
        var snapshot = Snapshot(); var cause = Assert.Throws<ObjectDisposedException>(() => { _ = host.UpdateAsync(snapshot, CancellationToken.None); });
        Assert.Equal(snapshot, store.Get(snapshot.Id)); Assert.Equal(0, notifications);
        var close = host.OriginalClose!; var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
        Assert.Contains(Causes(terminal), original => ReferenceEquals(original, cause)); Assert.True(close.IsFaulted);
    }
    [AvaloniaFact]
    public async Task Genuine_replacement_during_width_callback_does_not_receive_stale_later_effects()
    {
        var store = new FloatingActivityStateStore(); var host = new DesktopFloatingActivityHost(store);
        var actual = new Window(); Window? replacement = null; var windows = new List<Window> { actual };
        var failures = new List<Exception>(); var observed = false; var id = Guid.NewGuid();
        EventHandler<AvaloniaPropertyChangedEventArgs> replace = (_, args) => { if (args.Property == Window.WidthProperty) CurrentWindows(host)[id] = replacement!; };
        try
        {
            replacement = new Window(); windows.Add(replacement); // Capture each product before subsequent setters/acquisition.
            actual.Content = "Static"; replacement.Content = "Static"; actual.Height = 222; replacement.Height = 333;
            var wrapper = new DesktopFloatingActivityContent("Static", "Static"); Enroll(host, wrapper, "Static", actual); Enroll(host, wrapper, "Static", replacement);
            CurrentWindows(host)[id] = actual; actual.PropertyChanged += replace;
            var proposal = new FloatingActivitySnapshot(id, FloatingActivityState.Presented, 555, 777, 99, 88);
            var cause = Assert.Throws<InvalidOperationException>(() => { _ = host.UpdateAsync(proposal, CancellationToken.None); });
            Assert.Same(replacement, CurrentWindows(host)[id]); Assert.Equal(222, actual.Height); Assert.Equal(333, replacement.Height); Assert.Null(store.Get(id));
            var close = host.CloseAndDrainAsync(); var terminal = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.Contains(Causes(terminal), original => ReferenceEquals(original, cause)); observed = true;
        }
        catch (Exception primary) { failures.Add(primary); }
        finally { await RetireFixture(host, windows, failures, observed, cleanup: [() => actual.PropertyChanged -= replace]); }
        ThrowFixture(failures); // Private current-record probe; not a Windows Present or authority simulation.
    }
    [AvaloniaFact]
    public async Task Child_and_host_faults_still_attempt_actual_native_fixture_close_and_keep_every_cause()
    {
        var host = new DesktopFloatingActivityHost(new FloatingActivityStateStore()); var actual = new Window();
        ActualContent? child = null; var failures = new List<Exception>(); var cleanupFailures = new List<Exception>();
        var first = new IOException("Actual child first"); var second = new InvalidOperationException("Actual child second");
        var native = new IOException("Actual independent Window Closed callback"); var closed = false; var observed = false;
        EventHandler nativeCallback = (_, _) => { closed = true; throw native; };
        try
        {
            child = new ActualContent(); // The already-returned actual Window is protected before child acquisition.
            actual.Content = child; Enroll(host, new DesktopFloatingActivityContent(child, "Actual child"), child, actual); actual.Show();
            actual.Closed += nativeCallback;
            child.Release.TrySetException([first, second]);
            await RetireFixture(host, [actual], cleanupFailures, childTasks: [child.OriginalClose]);
            Assert.True(closed);
            foreach (var original in new Exception[] { first, second, native })
                Assert.Contains(cleanupFailures.SelectMany(Causes), cause => ReferenceEquals(cause, original));
            observed = true;
        }
        catch (Exception primary) { failures.Add(primary); }
        finally
        {
            child?.Release.TrySetResult();
            // Do not discard newly unobserved teardown errors; only the same already
            // inspected faulted host is qualified, and Window.Close is attempted again.
            await RetireFixture(host, [actual], failures, observedHostFault: observed,
                childTasks: child is { } acquired ? [acquired.OriginalClose] : [],
                observedChildFault: observed ? child?.OriginalClose : null, cleanup: [() => actual.Closed -= nativeCallback]);
        }
        ThrowFixture(failures);
    }
    private static Dictionary<Guid, Window> CurrentWindows(DesktopFloatingActivityHost actual) =>
        (Dictionary<Guid, Window>)typeof(DesktopFloatingActivityHost).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actual)!;
    private static FloatingActivityDefinition Definition() => new(Guid.NewGuid(), Guid.NewGuid(), "chat", "Actual title", "blue",
        FloatingActivityPresentation.DetachedWindow, true, true, DateTimeOffset.UtcNow);
    private static async Task RetireFixture(DesktopFloatingActivityHost host, IEnumerable<Window> windows, List<Exception> failures,
        bool observedHostFault = false, IEnumerable<Task>? childTasks = null, IEnumerable<Action>? cleanup = null, Task? observedChildFault = null)
    {
        foreach (var actual in cleanup ?? []) try { actual(); } catch (Exception cause) { AddFixtureCause(failures, cause); }
        try { host.RequestRetirement(); } catch (Exception cause) { AddFixtureCause(failures, cause); }
        foreach (var actual in (childTasks ?? []).Concat(host.OriginalClose is { } close ? [close] : []).Distinct<Task>(ReferenceEqualityComparer.Instance))
            try { await actual.WaitAsync(Bound); }
            catch (Exception observed)
            {
                if (observedHostFault && actual.IsFaulted && (ReferenceEquals(actual, host.OriginalClose) || ReferenceEquals(actual, observedChildFault))) continue;
                if (actual.Exception is { InnerExceptions.Count: > 0 } group) foreach (var cause in group.InnerExceptions) AddFixtureCause(failures, cause);
                else AddFixtureCause(failures, observed);
            }
        foreach (var actual in windows) try { actual.Close(); } catch (Exception cause) { AddFixtureCause(failures, cause); }
    }
    private static void AddFixtureCause(List<Exception> failures, Exception cause)
    { if (!failures.Any(actual => ReferenceEquals(actual, cause))) failures.Add(cause); }
    private static void ThrowFixture(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Floating control and independent actual teardown failed.", failures);
    }
    private static FloatingActivitySnapshot Snapshot() => new(Guid.NewGuid(), FloatingActivityState.Presented, 420, 280, 10, 20);
    private static void Enroll(DesktopFloatingActivityHost host, IFloatingActivityContent wrapper, object? content, Window window, Action? actualClosed = null)
    {
        Invoke(host, "RetainOriginalFloatingWrapper", wrapper); Invoke(host, "RetainOriginalFloatingContent", content);
        Invoke(host, "RetainOriginalFloatingWindow", Guid.NewGuid(), window);
        Invoke(host, "AttachOriginalFloatingClosed", window, actualClosed ?? (() => { }));
    }
    private static void Invoke(DesktopFloatingActivityHost host, string name, params object?[] args)
        => _ = InvokeValue(host, name, args);
    private static object? InvokeValue(DesktopFloatingActivityHost host, string name, params object?[] args)
    {
        try { return typeof(DesktopFloatingActivityHost).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, args); }
        catch (TargetInvocationException error) when (error.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }
    private static IEnumerable<Exception> Causes(Exception actual)
    {
        yield return actual;
        if (actual is AggregateException group) foreach (var child in group.InnerExceptions) foreach (var cause in Causes(child)) yield return cause;
    }
    private sealed class UnknownWrapper(object content) : IFloatingActivityContent
    { public object Content { get; } = content; public string AutomationName => "Unknown wrapper"; }
    private sealed class ActualContent : Control, IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
    {
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool RetirementRequested { get; private set; }
        internal Task OriginalClose => Release.Task; // SAME controlled fixture original, no account/permission receipt.
        public void RequestRetirement() => RetirementRequested = true;
        public void DemandExternalOriginalRetirementJoin() { }
        public Task CloseAndDrainAsync() { RequestRetirement(); return OriginalClose; }
    }
}
