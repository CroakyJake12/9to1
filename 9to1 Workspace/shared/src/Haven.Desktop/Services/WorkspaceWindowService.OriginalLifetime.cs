using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop.Services;

public sealed partial class WorkspaceWindowService
{
    private readonly object _originalGate = new();
    private DesktopOriginalWorkLifetime? _originalWork;
    private readonly HashSet<object> _originalChildren = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Window> _originalWindows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Window, PopupOriginal> _originalPopups = new(ReferenceEqualityComparer.Instance);
    private readonly List<Exception> _originalFailures = [];
    [ThreadStatic] private static List<WorkspaceWindowService>? _physicalSources;

    private DesktopOriginalWorkLifetime OriginalWindowWork
    {
        get { lock (_originalGate) return _originalWork ??= new(StopWindowSourcesAsync, DrainWindowResourcesAsync); }
    }

    private sealed class PopupOriginal(Window window, ContentControl host, WorkspaceTabViewModel tab, MainView? source)
    {
        internal Window Window { get; } = window;
        internal ContentControl Host { get; } = host;
        internal WorkspaceTabViewModel Tab { get; } = tab;
        internal MainView? Source { get; } = source;
        internal bool AllowClose;
        internal bool Closed;
        internal Task? Delivery;
    }

    private void RunWindowSource(Action source) => OriginalWindowWork.RunSynchronous(_ =>
        AcquireWindowSource(() => { source(); return true; }));

    private T AcquireWindowSource<T>(Func<T> source)
    {
        (_physicalSources ??= []).Add(this);
        try { return source(); }
        catch (Exception cause)
        {
            lock (_originalGate) AddWindowCause(_originalFailures, cause);
            if (cause is OperationCanceledException)
                throw new AggregateException("A synchronous workspace source supplied no canceled original Task.", cause);
            throw;
        }
        finally { _physicalSources.RemoveAt(_physicalSources.Count - 1); }
    }

    private T RetainChild<T>(T actual) where T : class
    {
        lock (_originalGate) _originalChildren.Add(actual);
        return actual;
    }
    private MainView RetainShell(MainView actual) => RetainChild(actual);
    private WorkspaceTabViewModel RetainDetachedTab(WorkspaceTabViewModel actual) => RetainChild(actual);
    private void ReleaseTransferredTab(WorkspaceTabViewModel actual)
    { lock (_originalGate) _originalChildren.Remove(actual); }
    private T RetainWindow<T>(T actual) where T : Window
    { lock (_originalGate) _originalWindows.Add(actual); return actual; }
    private void ConfigureWindow(Action source) => AcquireWindowSource(() => { source(); return true; });

    private void ObserveWindowClosed(Action actual)
    {
        // Closed callbacks remain attached until physical close, including the App's
        // later native phase. They never admit a new window after this owner is sealed.
        if (OriginalWindowWork.IsRetiring)
            OriginalWindowWork.RunCloseCallback(() => AcquireWindowSource(() => { actual(); return true; }));
        else RunWindowSource(actual);
    }
    private void WireWindowGeometry(Window window)
    {
        window.PositionChanged += (_, _) =>
        { if (!OriginalWindowWork.IsRetiring) RunWindowSource(sessions.QueueSave); };
        window.SizeChanged += (_, _) =>
        { if (!OriginalWindowWork.IsRetiring) RunWindowSource(sessions.QueueSave); };
    }
    private void RetainPopup(Window window, ContentControl host, WorkspaceTabViewModel tab, MainView? source)
    {
        var actual = new PopupOriginal(window, host, tab, source);
        lock (_originalGate) _originalPopups.Add(window, actual);
        window.Closed += (_, _) => actual.Closed = true;
        window.Closing += (_, args) =>
        {
            if (actual.AllowClose) return;
            // A user-close may transfer the SAME live tab to its existing owner.
            // A persisted popup has no recipient and must drain its actual page first.
            if (!OriginalWindowWork.IsRetiring && actual.Source is not null) return;
            args.Cancel = true;
            if (OriginalWindowWork.IsRetiring || actual.Delivery is not null) return;
            _ = OriginalWindowWork.RunAsync(async original =>
            {
                await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask());
                await DrainWindowChildAsync(actual.Tab);
                if (OriginalWindowWork.IsRetiring) return; // The encompassing cleanup now owns native delivery.
                actual.AllowClose = true;
                AcquireWindowSource(() => { actual.Window.Close(); return true; });
                if (!actual.Closed) throw new InvalidOperationException("The actual popup refused its owning close request.");
            }, task => actual.Delivery = task);
        };
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual workspace-window source must return before its encompassing retirement join.");
        OriginalWindowWork.DemandExternalClose();
        Window[] windows; lock (_originalGate) windows = _originalWindows.ToArray();
        foreach (var window in windows)
            if (window is IDesktopOriginalRetirementJoinGuard windowGuard) windowGuard.DemandExternalOriginalRetirementJoin();
        foreach (var actual in CaptureChildren())
            if (actual is IDesktopOriginalRetirementJoinGuard guard) guard.DemandExternalOriginalRetirementJoin();
    }
    public void RequestRetirement() => OriginalWindowWork.RequestRetirement();
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        return OriginalWindowWork.CloseAndDrainAsync();
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private object[] CaptureChildren() { lock (_originalGate) return _originalChildren.ToArray(); }
    private Task StopWindowSourcesAsync() => Task.CompletedTask; // Admission is sealed; no independent timer/thread exists.

    private async Task DrainWindowChildAsync(object actual)
    {
        if (actual is not IDesktopOriginalRetirementParticipant participant || actual is not IDesktopOriginalRetirementJoinGuard guard)
            throw new DesktopOriginalRetirementUnavailableException(actual.GetType());
        guard.DemandExternalOriginalRetirementJoin();
        AcquireWindowSource(() => { participant.RequestRetirement(); return true; });
        var task = AcquireWindowSource(participant.CloseAndDrainAsync);
        await JoinWindowOriginalAsync(task);
    }
    private async Task JoinWindowOriginalAsync(Task actual)
    {
        try
        {
            if (OriginalWindowWork.Executing is { } original) await original.AwaitAsync(actual);
            else await actual;
        }
        catch (Exception observed)
        {
            lock (_originalGate) CaptureWindowCauses(_originalFailures, actual, observed);
            throw;
        }
    }
    private async Task DrainWindowResourcesAsync()
    {
        var failures = new List<Exception>();
        var children = CaptureChildren(); // Include products returned after the initial seal.
        foreach (var actual in children)
            if (actual is IDesktopOriginalRetirementJoinGuard guard) guard.DemandExternalOriginalRetirementJoin();
        foreach (var actual in children)
            if (actual is IDesktopOriginalRetirementParticipant participant)
                try { OriginalWindowWork.RunCloseCallback(() => AcquireWindowSource(() => { participant.RequestRetirement(); return true; })); }
                catch (Exception cause) { AddWindowCause(failures, cause); }
        var tasks = new List<Task>();
        foreach (var actual in children)
        {
            if (actual is not IDesktopOriginalRetirementParticipant participant || actual is not IDesktopOriginalRetirementJoinGuard)
            { AddWindowCause(failures, new DesktopOriginalRetirementUnavailableException(actual.GetType())); continue; }
            try { tasks.Add(AcquireWindowSource(participant.CloseAndDrainAsync)); }
            catch (Exception cause) { AddWindowCause(failures, cause); }
        }
        foreach (var task in tasks.Distinct<Task>(ReferenceEqualityComparer.Instance))
            try { await task; } catch (Exception observed) { CaptureWindowCauses(failures, task, observed); }
        lock (_originalGate) foreach (var cause in _originalFailures) AddWindowCause(failures, cause);
        ThrowWindowCauses(failures); // Retain actual native content when any original/child failed.
        PopupOriginal[] popups;
        lock (_originalGate) popups = _originalPopups.Values.ToArray();
        foreach (var popup in popups.Where(popup => !popup.Closed))
        {
            try
            {
                OriginalWindowWork.RunCloseCallback(() => AcquireWindowSource(() =>
                {
                    popup.AllowClose = true;
                    popup.Window.Close();
                    if (!popup.Closed) throw new InvalidOperationException("The actual popup refused its owning close request.");
                    return true;
                }));
            }
            catch (Exception cause) { AddWindowCause(failures, cause); }
        }
        ThrowWindowCauses(failures);
        // MainWindow owns its genuine shell close and the App owns its final physical
        // close phase. This service must not destroy those native windows here.
    }
    private static void CaptureWindowCauses(List<Exception> failures, Task actual, Exception observed)
    {
        if (actual.Exception is { InnerExceptions.Count: > 0 } group)
            foreach (var cause in group.InnerExceptions) AddWindowCause(failures, cause);
        else AddWindowCause(failures, observed);
    }
    private static void AddWindowCause(List<Exception> failures, Exception cause)
    { if (!failures.Any(actual => ReferenceEquals(actual, cause))) failures.Add(cause); }
    private static void ThrowWindowCauses(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Actual workspace-window originals remain unresolved.", failures);
    }
}
