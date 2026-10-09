using Avalonia.Controls;
using Avalonia.Media;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Browse;

// Setup is deliberately unavailable. It performs no browser data/profile I/O
// and cannot mount normal browser actions without the original Home supplier.
internal sealed class BrowseStartupWindow : Window
{
    private readonly BrowseWindowsStartupAttempt? _originalStartup;
    private readonly CuiSceneHost _originalScene = new();
    private readonly CuiViewModel _originalModel = new();
    private readonly List<Task> _originalShows = [];
    private Task<CuiSceneAvailability>? _originalInitialization;
    private Task<bool>? _originalClose;
    private Task? _originalSceneClose, _originalTransition;
    private Action? _originalTransitionJoinGuard;
    private bool _allowClose;
    private volatile bool _retiring;
    internal bool RetirementRequested => _retiring || (_originalClose is not null && !(_originalClose.IsCompletedSuccessfully && !_originalClose.Result));
    internal Task<bool>? OriginalClose => _originalClose;
    internal BrowseStartupWindow(BrowseWindowsStartupAttempt? originalStartup)
    {
        _originalStartup = originalStartup; Content = _originalScene; Title = "Browse";
        Width = 900; Height = 640; MinWidth = 360; MinHeight = 520;
        Closing += OnClosing;
    }
    internal void BindOriginalTransition(Task originalTransition, Action originalOwnerJoinGuard)
    {
        ArgumentNullException.ThrowIfNull(originalTransition); ArgumentNullException.ThrowIfNull(originalOwnerJoinGuard);
        if (_originalTransition is not null || _originalInitialization is not null || RetirementRequested)
            throw new InvalidOperationException("The original Browse startup transition has already been admitted.");
        _originalTransition = originalTransition; _originalTransitionJoinGuard = originalOwnerJoinGuard;
    }
    internal Task<CuiSceneAvailability> InitializeAsync()
    {
        if (_originalInitialization is not null) return _originalInitialization;
        if (_retiring) throw new InvalidOperationException("The original Browse startup window is retiring.");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _originalInitialization = InitializeCoreAsync(start.Task); _originalShows.Add(_originalInitialization); start.SetResult(); return _originalInitialization;
    }
    private async Task<CuiSceneAvailability> InitializeCoreAsync(Task start)
    {
        await start;
        var status = new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
            _originalStartup is null ? "HomeStartupAttachmentUnavailable" : "HomeStartupAttachmentPending",
            _originalStartup is null
                ? "Open Browse from Home to connect your profile and authorised browser services. Install Home if it is missing. Existing tabs, downloads and profiles are preserved."
                : "Browse is checking the original Home connection. Your existing tabs and profile are preserved while it connects.");
        return await CaptureOriginalStatus(status);
    }
    internal Task<CuiSceneAvailability> ShowOriginalStatusAsync(CuiSceneAvailability unavailable)
    {
        if (unavailable.State == CuiSceneAvailabilityState.Ready) throw new InvalidOperationException("The startup surface cannot issue Home readiness.");
        if (RetirementRequested) throw new InvalidOperationException("The original startup owner is retiring.");
        return CaptureOriginalStatus(unavailable);
    }
    private Task<CuiSceneAvailability> CaptureOriginalStatus(CuiSceneAvailability unavailable)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = ShowStatusCoreAsync(start.Task, unavailable); _originalShows.Add(original); start.SetResult(); return original;
    }
    private async Task<CuiSceneAvailability> ShowStatusCoreAsync(Task start, CuiSceneAvailability unavailable)
    {
        using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
        await start;
        var scene = new CuiNativeScene("browse", "Browse", "Browse",
            new CuiRichParser().Parse("<Cui><Page id=\"BrowseStartup\"><Text Text=\"Browse\" /></Page></Cui>"),
            _originalModel, _originalModel, new UnavailableObservation(unavailable))
            { IsPublicationCurrent = () => !_retiring };
        var original = OriginalNativeInvocation.Acquire(this, () => _originalScene.ShowAsync(scene));
        _originalShows.Add(original); var actual = await original;
        if (_originalScene.TryFindResource("CuiBackgroundBrush", out var value) && value is IBrush brush) Background = brush;
        return actual;
    }
    internal Task<bool> CloseForOriginalHandoffAsync()
    {
        if (RetirementRequested) throw new InvalidOperationException("The original setup close was already admitted before handoff.");
        return CloseOriginalAsync(handoff: true);
    }
    private Task<bool> CloseOriginalAsync(bool handoff = false)
    {
        OriginalNativeInvocation.DemandExternalJoin(this);
        _originalTransitionJoinGuard?.Invoke(); _originalStartup?.DemandExternalJoin();
        if (_originalClose is not null && !(_originalClose.IsCompletedSuccessfully && !_originalClose.Result)) return _originalClose;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _originalClose = CloseCoreAsync(start.Task, handoff); start.SetResult(); return _originalClose;
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_allowClose) return; args.Cancel = true;
        try { await CloseOriginalAsync(); }
        catch (Exception failure) { System.Diagnostics.Trace.TraceError("Browse startup retains its original close/resource failure: {0}", failure); }
    }
    private async Task<bool> CloseCoreAsync(Task start, bool handoff)
    {
        using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
        await start;
        var failures = new List<Exception>();
        if (_originalTransition is not null)
            try { await _originalTransition; } catch (Exception failure) { failures.Add(failure); }
        if (!handoff && _originalStartup is not null)
        {
            try
            {
                var originalStartupClose = _originalStartup.CloseOriginalAsync(); _originalShows.Add(originalStartupClose);
                if (!await originalStartupClose && failures.Count == 0) return false;
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        // Admitted initialization may append its actual scene child. Drain the
        // same growing raw-source ledger before withdrawing the presentation;
        // RetirementRequested already refuses unrelated new status admissions.
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < _originalShows.Count; index++)
        {
            var original = _originalShows[index];
            if (joined.Add(original))
                try { await original; } catch (Exception failure) { failures.Add(failure); }
        }
        _retiring = true;
        try { _originalSceneClose = _originalScene.CloseOriginalAsync(); } catch (Exception failure) { failures.Add(failure); }
        if (_originalSceneClose is not null)
            try { await _originalSceneClose; } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new AggregateException("Browse startup retained its actual original scene/source failures.", failures);
        _allowClose = true; Close(); return true;
    }
    private sealed class UnavailableObservation(CuiSceneAvailability originalUnavailable) : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(originalUnavailable); }
    }
}
