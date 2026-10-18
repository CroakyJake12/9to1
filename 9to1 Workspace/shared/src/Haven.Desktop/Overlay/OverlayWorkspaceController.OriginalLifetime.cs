#if !ANDROID
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Desktop.Services;

namespace Haven.Desktop.Overlay;

internal sealed partial class OverlayWorkspaceController
{
    private readonly object _originalGate = new();
    private DesktopOriginalWorkLifetime? _originalWork;
    private readonly HashSet<object> _originalPages = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<OverlayWorkspaceWindow, HashSet<Task>> _windowBorrowers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<OverlayWorkspaceWindow, Task> _windowDrains = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Guid> _closingSessions = [];
    private readonly List<Exception> _originalFailures = [];
    [ThreadStatic] private static List<OverlayWorkspaceController>? _physicalSources;
    private DesktopOriginalWorkLifetime ControllerWork
    { get { lock (_originalGate) return _originalWork ??= new(StopControllerSourcesAsync, CleanupControllerAsync); } }

    private T ControllerSource<T>(Func<T> actual)
    {
        (_physicalSources ??= []).Add(this);
        try { return actual(); }
        catch (Exception cause)
        {
            lock (_originalGate) AddControllerCause(_originalFailures, cause);
            if (cause is OperationCanceledException) throw new AggregateException("An actual synchronous Overlay source supplied no canceled original task.", cause);
            throw;
        }
        finally { _physicalSources.RemoveAt(_physicalSources.Count - 1); }
    }
    private Task RunControllerAsync(Func<Task> actual) => ControllerWork.Executing is not null
        ? ControllerSource(actual)
        : ControllerWork.RunAsync(original => ObserveControllerTaskAsync(original, ControllerSource(actual)));
    private Task<T> RunControllerAsync<T>(Func<Task<T>> actual) => ControllerWork.Executing is not null
        ? ControllerSource(actual)
        : ControllerWork.RunAsync(original => ObserveControllerTaskAsync(original, ControllerSource(actual)));
    private Task AwaitControllerAsync(Func<Task> actual) => ObserveControllerTaskAsync(
        ControllerWork.Executing ?? throw new InvalidOperationException("No admitted Overlay original owns this source."), ControllerSource(actual));
    private Task<T> AwaitControllerAsync<T>(Func<Task<T>> actual) => ObserveControllerTaskAsync(
        ControllerWork.Executing ?? throw new InvalidOperationException("No admitted Overlay original owns this source."), ControllerSource(actual));
    private async Task ObserveControllerTaskAsync(DesktopOriginalWorkLifetime.Original original, Task actual)
    {
        try { await original.AwaitAsync(actual); }
        catch (Exception cause) { CaptureControllerFailure(actual, cause); throw; }
    }
    private async Task<T> ObserveControllerTaskAsync<T>(DesktopOriginalWorkLifetime.Original original, Task<T> actual)
    {
        try { return await original.AwaitAsync(actual); }
        catch (Exception cause) { CaptureControllerFailure(actual, cause); throw; }
    }
    private void CaptureControllerFailure(Task actual, Exception observed)
    {
        lock (_originalGate)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                foreach (var cause in group.InnerExceptions) AddControllerCause(_originalFailures, cause);
            else AddControllerCause(_originalFailures, observed);
        }
    }
    private T RetainControllerPage<T>(T actual) where T : class
    { lock (_originalGate) _originalPages.Add(actual); return actual; }

    private void RecordWindowBorrower(OverlayWorkspaceWindow window, Task? actual = null)
    {
        actual ??= ControllerWork.Executing?.Task;
        if (actual is null) return;
        lock (_originalGate)
        {
            if (!_windowBorrowers.TryGetValue(window, out var tasks)) _windowBorrowers.Add(window, tasks = []);
            tasks.RemoveWhere(task => task.IsCompletedSuccessfully);
            tasks.Add(actual);
        }
    }
    private bool CanPublish(Guid id, OverlayWorkspaceWindow actual) => !_disposed && !_closingSessions.Contains(id)
        && _windows.TryGetValue(id, out var current) && ReferenceEquals(current, actual);

    private void RunOnUi(Func<Task> operation) => QueueControllerUi(null, operation);
    private void RunOnUi(Guid sessionId, Func<Task> operation) => QueueControllerUi(sessionId, operation);
    private void QueueControllerUi(Guid? sessionId, Func<Task> operation)
    {
        if (_disposed || ControllerWork.IsRetiring) return;
        OverlayWorkspaceWindow? captured = null;
        if (sessionId is { } id && (_closingSessions.Contains(id) || !_windows.TryGetValue(id, out captured))) return;
        try { _ = ControllerWork.RunAsync(async original =>
        {
            await original.AwaitAsync(ControllerSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || (sessionId is { } id && (captured is null || !CanPublish(id, captured)))) return Task.CompletedTask;
                return ControllerSource(() => RunGuardedAsync(operation));
            })));
        }, actual => { if (captured is not null) RecordWindowBorrower(captured, actual); }); }
        catch (ObjectDisposedException) when (ControllerWork.IsRetiring)
        { /* The SAME owner refused this admission before creating a task. No source ran. */ }
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.Any(actual => ReferenceEquals(actual, this)) == true)
            throw new InvalidOperationException("An actual Overlay source must return before its encompassing controller retirement join.");
        ControllerWork.DemandExternalClose();
        _hotkey.DemandExternalOriginalRetirementJoin();
        object[] pages; lock (_originalGate) pages = _originalPages.ToArray();
        foreach (var page in pages)
            if (page is IDesktopOriginalRetirementJoinGuard guard) guard.DemandExternalOriginalRetirementJoin();
    }
    public void RequestRetirement() => ControllerWork.RequestRetirement();
    public Task CloseAndDrainAsync()
    { DemandExternalOriginalRetirementJoin(); return ControllerWork.CloseAndDrainAsync(); }

    private async Task StopControllerSourcesAsync()
    {
        _disposed = true; // The lifetime sealed admission before invoking this stop.
        _registry.Changed -= OnRegistryChanged;
        _hotkey.Pressed -= OnHotkeyPressed;
        // Geometry cancellation is logical supersession, not abandonment of the
        // actual admitted delay or a waiver of a canceled/faulted provider task.
        foreach (var owner in _geometryUpdates.Values.ToArray()) owner.Cancel();
        var actualHotkeyClose = ControllerSource(() => _hotkey.DisposeAsync().AsTask());
        try { await actualHotkeyClose; }
        catch (Exception cause) { CaptureControllerFailure(actualHotkeyClose, cause); throw; }
    }

    private Task DrainOverlayWindowAsync(Guid id)
    {
        if (!_windows.TryGetValue(id, out var window)) return Task.CompletedTask;
        lock (_originalGate)
        {
            if (_windowDrains.TryGetValue(window, out var existing)) return existing;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var closing = ControllerWork.Executing?.Task;
            Task[] borrowers = _windowBorrowers.TryGetValue(window, out var current)
                ? current.Where(task => !ReferenceEquals(task, closing)).ToArray() : [];
            var actual = DrainOverlayWindowCoreAsync(start.Task, window, borrowers);
            _windowDrains.Add(window, actual);
            start.SetResult();
            return actual;
        }
    }
    private async Task DrainOverlayWindowCoreAsync(Task start, OverlayWorkspaceWindow window, Task[] borrowers)
    {
        await start;
        var failures = new List<Exception>();
        foreach (var actual in borrowers) await JoinControllerOriginalAsync(actual, failures);
        // A controller borrower may still be in a noncancelable OS picker. It must
        // return before the page admission seal, release, or native window close.
        var page = window.OriginalPage;
        if (page is not IDesktopOriginalRetirementParticipant participant || page is not IDesktopOriginalRetirementJoinGuard guard)
            failures.Add(new DesktopOriginalRetirementUnavailableException(page.GetType()));
        else
        {
            try
            {
                guard.DemandExternalOriginalRetirementJoin();
                ControllerSource(() => { participant.RequestRetirement(); return true; });
                await JoinControllerOriginalAsync(ControllerSource(participant.CloseAndDrainAsync), failures);
            }
            catch (Exception cause) { AddControllerCause(failures, cause); }
        }
        if (failures.Count > 0) ReportRetainedWindowFailure(window, failures);
        ThrowControllerCauses(failures);
    }

    private void ReportRetainedWindowFailure(OverlayWorkspaceWindow window, List<Exception> failures)
    {
        try { ControllerSource(() => { window.SetRegionStatus("This Overlay could not close cleanly. Its original work is retained for diagnosis."); return true; }); }
        catch (Exception cause) { AddControllerCause(failures, cause); }
    }

    private async Task CleanupControllerAsync()
    {
        // Every queued dispatcher/provider/picker original is terminal before
        // acquiring child close. Never make a page wait on its own encompassing
        // controller close, and never discard failed/unpublished acquired pages.
        var failures = new List<Exception>();
        object[] pages; lock (_originalGate) pages = _originalPages.ToArray();
        foreach (var page in pages)
            if (page is IDesktopOriginalRetirementJoinGuard guard) guard.DemandExternalOriginalRetirementJoin();
        foreach (var page in pages)
            if (page is IDesktopOriginalRetirementParticipant participant)
                try { ControllerSource(() => { participant.RequestRetirement(); return true; }); }
                catch (Exception cause) { AddControllerCause(failures, cause); }
        var children = new List<Task>();
        foreach (var page in pages)
        {
            if (page is not IDesktopOriginalRetirementParticipant participant || page is not IDesktopOriginalRetirementJoinGuard)
            { failures.Add(new DesktopOriginalRetirementUnavailableException(page.GetType())); continue; }
            try { children.Add(ControllerSource(participant.CloseAndDrainAsync)); }
            catch (Exception cause) { AddControllerCause(failures, cause); }
        }
        foreach (var child in children.Distinct<Task>(ReferenceEqualityComparer.Instance)) await JoinControllerOriginalAsync(child, failures);
        Task[] windowDrains; lock (_originalGate) windowDrains = _windowDrains.Values.ToArray();
        foreach (var actual in windowDrains) await JoinControllerOriginalAsync(actual, failures);
        lock (_originalGate) foreach (var cause in _originalFailures) AddControllerCause(failures, cause);
        if (failures.Count > 0)
            foreach (var window in _windows.Values.ToArray()) ReportRetainedWindowFailure(window, failures);
        ThrowControllerCauses(failures); // Preserve failed originals, child views and captures.
        foreach (var id in _registry.Snapshot.Sessions.Select(session => session.Id).Concat(_captureDrafts.Keys).Distinct().ToArray())
            try
            {
                var task = ControllerSource(() => CleanupSessionArtifactsForRetirementAsync(id));
                await JoinControllerOriginalAsync(task, failures);
            }
            catch (Exception cause) { AddControllerCause(failures, cause); }
        ThrowControllerCauses(failures);
        foreach (var pair in _windows.ToArray())
            try { ControllerSource(() => { pair.Value.CloseFromController(); return true; }); PruneHealthyClosedOverlay(pair.Value); }
            catch (Exception cause) { AddControllerCause(failures, cause); }
        ThrowControllerCauses(failures);
    }
    private void PruneHealthyClosedOverlay(OverlayWorkspaceWindow window)
    {
        // A physical Closed event alone is no child retirement receipt. This is
        // the exact coalesced task obtained from the real page by owning close.
        if (!window.OriginalNativeClosed || window.OriginalPageClose?.IsCompletedSuccessfully != true) return;
        lock (_originalGate)
        {
            if (_windows.TryGetValue(window.SessionId, out var current) && ReferenceEquals(current, window))
                _windows.Remove(window.SessionId);
            _windowBorrowers.Remove(window);
            _windowDrains.Remove(window);
            if (!_windows.ContainsKey(window.SessionId)) _closingSessions.Remove(window.SessionId);
            var page = window.OriginalPage;
            if (!_windows.Values.Any(other => ReferenceEquals(other.OriginalPage, page)))
                _originalPages.Remove(page);
        }
    }

    private async Task CleanupSessionArtifactsForRetirementAsync(Guid id)
    {
        var current = _registry.Snapshot.Sessions.FirstOrDefault(session => session.Id == id)?.Context;
        if (current is not null)
        {
            var failures = new List<Exception>();
            await JoinControllerOriginalAsync(ControllerSource(() => _registry.SetContextAsync(id, null, CancellationToken.None)), failures);
            ThrowControllerCauses(failures);
            ControllerSource(() => { _regionCapture.CleanupRegion(current); return true; });
        }
        if (_captureDrafts.Remove(id, out var draft)) ControllerSource(() => { _visualCapture.CleanupCapture(draft.SourceContext); return true; });
    }
    private static async Task JoinControllerOriginalAsync(Task actual, List<Exception> failures)
    {
        try { await actual; }
        catch (Exception cause)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                foreach (var sibling in group.InnerExceptions) AddControllerCause(failures, sibling);
            else AddControllerCause(failures, cause);
        }
    }
    private static void AddControllerCause(List<Exception> failures, Exception cause)
    { if (!failures.Any(actual => ReferenceEquals(actual, cause))) failures.Add(cause); }
    private static void ThrowControllerCauses(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Actual Overlay originals remain unresolved.", failures);
    }
}
#endif
