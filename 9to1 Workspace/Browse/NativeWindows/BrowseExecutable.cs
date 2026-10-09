using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Browse;

/// <summary>Standalone executable consumer. The original installed Home/root
/// producer must supply the startup attempt; this entry issues no fake session,
/// verifier, readiness, paths or permission grants.</summary>
public static class BrowseExecutable
{
    [STAThread]
    public static int Main(string[] arguments) => Build(null).StartWithClassicDesktopLifetime(arguments);
    public static int RunFromOriginalStartup(BrowseWindowsStartupAttempt originalStartup, string[] arguments)
    { ArgumentNullException.ThrowIfNull(originalStartup); return Build(originalStartup).StartWithClassicDesktopLifetime(arguments); }
    public static AppBuilder Build(BrowseWindowsStartupAttempt? originalStartup) =>
        CuiNativeHost.ConfigureFonts(AppBuilder.Configure(() => new BrowseDesktopApplication(originalStartup)).UsePlatformDetect());
}

internal sealed class BrowseDesktopApplication(BrowseWindowsStartupAttempt? originalStartup) : Application
{
    private readonly List<Task> _originalSources = [];
    private BrowseStartupWindow? _originalSetup;
    private BrowseNativeWindow? _originalBrowser;
    private Task? _originalTransition;
    private Exception? _originalStartupFailure;
    private bool _originalNativePublished;
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Browse");
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _originalSetup = new BrowseStartupWindow(originalStartup);
            desktop.MainWindow = _originalSetup;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalTransition = ObserveOriginalStartupAsync(start.Task, desktop, _originalSetup);
            _originalSources.Add(_originalTransition); _originalSetup.BindOriginalTransition(_originalTransition, () => OriginalNativeInvocation.DemandExternalJoin(this)); start.SetResult();
            _originalSetup.Opened += ObserveTransition;
        }
        base.OnFrameworkInitializationCompleted();
    }
    private async void ObserveTransition(object? sender, EventArgs args)
    {
        if (_originalTransition is null) return;
        try
        {
            await _originalTransition;
            // The publication driver is terminal before this separate original
            // close is acquired. Setup retirement can join that driver without
            // the publication driver waiting on its own close.
            if (_originalNativePublished && _originalSetup is { RetirementRequested: false } originalSetup)
            {
                var originalHandoff = originalSetup.CloseForOriginalHandoffAsync();
                _originalSources.Add(originalHandoff); await originalHandoff;
            }
        }
        catch (Exception failure) { System.Diagnostics.Trace.TraceError("Browse retains its original executable startup failure: {0}", failure); }
    }
    private async Task ObserveOriginalStartupAsync(Task start, IClassicDesktopStyleApplicationLifetime desktop, BrowseStartupWindow originalSetup)
    {
        using var originalDriver = OriginalNativeInvocation.EnterDriver(this);
        await start;
        var originalSetupShow = OriginalNativeInvocation.Acquire(this, originalSetup.InitializeAsync); _originalSources.Add(originalSetupShow);
        await originalSetupShow;
        if (originalStartup is null) return;
        try
        {
            var actualWindow = await originalStartup.OriginalWindow;
            _originalBrowser = actualWindow;
            if (originalSetup.RetirementRequested) return;
            var originalInitialization = OriginalNativeInvocation.Acquire(this, () => actualWindow.InitializeAsync()); _originalSources.Add(originalInitialization);
            var actualAvailability = await originalInitialization;
            if (originalSetup.RetirementRequested) return;
            if (actualAvailability.State != CuiSceneAvailabilityState.Ready)
            {
                var actualStatus = originalSetup.ShowOriginalStatusAsync(actualAvailability); _originalSources.Add(actualStatus); await actualStatus; return;
            }
            var originalNativePublication = Dispatcher.UIThread.InvokeAsync(() =>
            {
                using var originalInvocation = OriginalNativeInvocation.EnterExternal(this);
                if (!ReferenceEquals(_originalBrowser, actualWindow)) throw new InvalidOperationException("The original native Browse window changed before publication.");
                if (originalSetup.RetirementRequested) return;
                desktop.MainWindow = actualWindow; actualWindow.Show(); _originalNativePublished = true;
            }).GetTask();
            _originalSources.Add(originalNativePublication); await originalNativePublication;
        }
        catch (Exception failure)
        {
            _originalStartupFailure = failure;
            if (originalSetup.RetirementRequested) throw;
            var actualStatus = originalSetup.ShowOriginalStatusAsync(new(CuiSceneAvailabilityState.Unavailable,
                "BrowseOriginalStartupFailed", "Browse could not connect to Home. Your existing tabs and profiles are preserved. Open Home to recover this launch."));
            _originalSources.Add(actualStatus);
            try { await actualStatus; }
            catch (Exception statusFailure) { throw new AggregateException("Browse retains startup and status publication failures.", _originalStartupFailure ?? failure, statusFailure); }
            throw;
        }
    }
}
