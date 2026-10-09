using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace HavenOS.Apps.Browse;

/// <summary>The actual launcher retains this startup attempt through window
/// retirement, including all original failed readiness/create/close sources.
/// Constructor inputs are observations/services, never a fabricated grant.</summary>
public sealed class BrowseWindowsStartupAttempt
{
    private static readonly AsyncLocal<BrowseWindowsStartupAttempt?> LogicalStartup = new();
    private readonly ICuiSceneReadiness _originalHome;
    private readonly IAppPaths _originalPaths;
    private readonly Func<string, bool> _originalAvailability;
    private readonly IBrowserAutomationService? _originalAutomation;
    private readonly BrowserDownloadTransport? _originalTransport;
    private readonly IBrowserNativeDownloadService? _originalDownloads;
    private readonly CuiAppearance? _appearance;
    private readonly IBrowserOriginalDownloadFilesService? _originalDownloadFiles;
    private readonly List<Task> _originalUiSources = [];
    private Task<bool>? _originalClose;
    private readonly object _closeGate = new();
    public Task<BrowseNativeWindow> OriginalWindow { get; }
    public Task<CuiSceneAvailability>? OriginalReadiness { get; private set; }
    public Task<BrowseChrome>? OriginalChromeCreate { get; private set; }
    public BrowseChrome? OriginalChrome { get; private set; }
    public BrowseNativeWindow? OriginalNativeWindow { get; private set; }
    public Task<bool>? OriginalClose => _originalClose;
    public Task? OriginalChromeClose { get; private set; }
    public IReadOnlyList<Task> OriginalUiSources => _originalUiSources.AsReadOnly();
    public BrowseWindowsStartupAttempt(IAppPaths originalPaths, ICuiSceneReadiness originalHomeReadiness,
        Func<string, bool> originalActionAvailability, IBrowserAutomationService? originalAutomation = null,
        BrowserDownloadTransport? originalDownloadTransport = null, IBrowserNativeDownloadService? originalNativeDownloads = null,
        CuiAppearance? appearance = null, CancellationToken token = default,
        IBrowserOriginalDownloadFilesService? originalDownloadFiles = null)
    {
        _originalPaths = originalPaths ?? throw new ArgumentNullException(nameof(originalPaths));
        _originalHome = originalHomeReadiness ?? throw new ArgumentNullException(nameof(originalHomeReadiness));
        _originalAvailability = originalActionAvailability ?? throw new ArgumentNullException(nameof(originalActionAvailability));
        _originalAutomation = originalAutomation; _originalTransport = originalDownloadTransport; _originalDownloads = originalNativeDownloads; _appearance = appearance; _originalDownloadFiles = originalDownloadFiles;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OriginalWindow = StartOriginalAsync(start.Task, token); start.SetResult();
    }
    private async Task<BrowseNativeWindow> StartOriginalAsync(Task start, CancellationToken token)
    {
        using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
        var previous = LogicalStartup.Value; LogicalStartup.Value = this;
        try
        {
            await start;
            OriginalReadiness = OriginalNativeInvocation.Acquire(this, () => _originalHome.CheckAsync(token)).AsTask();
            var actualReadiness = await OriginalReadiness;
            if (actualReadiness.State != CuiSceneAvailabilityState.Ready)
                throw new InvalidOperationException(actualReadiness.Code + ": " + actualReadiness.Message);
            var factory = new WindowsChromiumEngineFactory((tabId, sameEngine, origin, kind) =>
                (OriginalChrome ?? throw new InvalidOperationException("The original Browse owner is not attached."))
                    .ObserveOriginPermission(tabId, sameEngine, origin, kind), _originalTransport, _originalDownloads);
            OriginalChromeCreate = OriginalNativeInvocation.Acquire(this, () => BrowseChrome.CreateAsync(_originalPaths, factory, _originalAutomation, token));
            OriginalChrome = await OriginalChromeCreate;
            var originalUi = Dispatcher.UIThread.InvokeAsync(() =>
            {
                using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
                var workspace = new BrowseNativeWorkspace(OriginalChrome, _originalAvailability, _originalDownloadFiles);
                OriginalNativeWindow = BrowseNativeSurface.CreateWindow(workspace, _originalHome, _appearance);
                return OriginalNativeWindow;
            }).GetTask();
            _originalUiSources.Add(originalUi); return await originalUi;
        }
        finally { LogicalStartup.Value = previous; }
    }
    internal void DemandExternalJoin()
    {
        OriginalNativeInvocation.DemandExternalJoin(this);
        if (OriginalChrome is { } currentChrome)
            foreach (var tab in currentChrome.State.Tabs)
                if (currentChrome.ObserveOriginalEngine(tab.Id) is IBrowseEngineRetirementGuard guard) guard.DemandExternalJoin();
        if (ReferenceEquals(LogicalStartup.Value, this)) throw new InvalidOperationException("The original startup callback cannot join its own retirement.");
    }
    public Task<bool> CloseOriginalAsync()
    {
        DemandExternalJoin();
        // Only a successful explicit pre-admission refusal may start a later
        // intent. Pending/faulted/canceled original close sources are reused.
        lock (_closeGate)
        {
            if (_originalClose is not null && !(_originalClose.IsCompletedSuccessfully && !_originalClose.Result)) return _originalClose;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalClose = CloseOriginalCoreAsync(start.Task); start.SetResult(); return _originalClose;
        }
    }
    private async Task<bool> CloseOriginalCoreAsync(Task start)
    {
        using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
        await start; var failures = new List<Exception>();
        try { await OriginalWindow; } catch (Exception failure) { failures.Add(failure); }
        if (OriginalNativeWindow is { } window)
        {
            var originalRequest = Dispatcher.UIThread.InvokeAsync(() =>
            {
                using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
                if (window.OriginalClose is null && !window.CanAdmitOriginalClose()) return false;
                window.Close(); return true;
            }).GetTask();
            _originalUiSources.Add(originalRequest);
            var admitted = await originalRequest;
            if (!admitted && failures.Count == 0) return false;
            if (window.OriginalClose is { } originalWindowClose)
                try { await originalWindowClose; } catch (Exception failure) { failures.Add(failure); }
            else if (admitted) failures.Add(new InvalidOperationException("The native owner did not issue its original close source."));
        }
        else if (OriginalChrome is not null)
        {
            try { OriginalChromeClose = OriginalChrome.DisposeAsync().AsTask(); await OriginalChromeClose; }
            catch (Exception failure) { failures.Add(failure); }
        }
        foreach (var source in _originalUiSources.ToArray()) try { await source; } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new AggregateException("Browse startup retained its actual original source/resource failures.", failures);
        return true;
    }
}
