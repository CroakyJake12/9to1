using System.Runtime.ExceptionServices;
using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
#if !ANDROID
using Haven.Desktop.Accounts;
using HavenOS.Apps.Browse;
#endif

namespace Haven.Desktop;

public sealed partial class App
{
    private readonly DesktopOriginalWorkLifetime _originalAppWork;
    [ThreadStatic] private static List<App>? _synchronousAppSources;
    private IUpdateService? _actualSubscribedUpdates;
    private Task? _actualStartupOriginal;
    private Task? _actualPreviewBootstrap;
    private DesktopOriginalShutdownSequence? _actualShutdownSequence;
    private Task? _actualShutdownDelivery;
    private Task? _actualDocumentClosePreflightAttempt;
    private Task? _actualDocumentClosePreflightDelivery;
    private Task? _actualDocumentClosePreflightPublication;
    private readonly List<Task> _actualDocumentClosePreflightDeliveries = [];
    private MainView? _actualDocumentClosePrimaryShell;
    private Task? _actualWindowClosure;
    private Exception? _actualShutdownFailure;
    private IClassicDesktopStyleApplicationLifetime? _actualDesktop;
    private MainWindow? _actualPrimaryWindow;
    private IReadOnlyList<object> _actualDesktopBorrowers = [];
    private ComputerUseOverlayCoordinator? _actualComputerUseOverlay;
    private readonly object _actualStartupAcquisitionGate = new();
    private readonly HashSet<Task> _actualFailedStartupAcquisitionCloses = new(ReferenceEqualityComparer.Instance);

    public App() => _originalAppWork = new(StopOriginalAppSourcesAsync, JoinOriginalFailedStartupAcquisitionsAsync);

    internal Task? OriginalStartup => _actualStartupOriginal;

    internal Task? OriginalShutdownDelivery => _actualShutdownDelivery;
    internal Task? OriginalDocumentClosePreflightAttempt => _actualDocumentClosePreflightAttempt;
    internal Task? OriginalDocumentClosePreflightDelivery => _actualDocumentClosePreflightDelivery;
    internal Exception? OriginalShutdownFailure => _actualShutdownFailure;

    private T AcquireOriginalAppSynchronous<T>(DesktopOriginalWorkLifetime.Original original, Func<T> actualSource)
    {
        (_synchronousAppSources ??= []).Add(this);
        try { return actualSource(); }
        catch (Exception error)
        {
            original.Retain(error);
            if (error is OperationCanceledException)
                throw new AggregateException("The synchronous App source supplied no canceled original Task.", error);
            throw;
        }
        finally { _synchronousAppSources.RemoveAt(_synchronousAppSources.Count - 1); }
    }
    private ComputerUseOverlayCoordinator ResolveOriginalComputerUseOverlay(IServiceProvider actualProvider)
    {
        ComputerUseOverlayCoordinator? actual = null;
        _originalAppWork.RunSynchronous(original =>
        {
            try { actual = AcquireOriginalAppSynchronous(original, actualProvider.GetRequiredService<ComputerUseOverlayCoordinator>); }
            catch (Exception failure)
            {
                // A constructor can throw AFTER returning native products internally.
                // Capture their SAME privately issued cleanup Tasks before this original callback exits.
                foreach (var acquisition in CaptureActualComputerUseAcquisitionFailures(failure)
                    .Distinct<ComputerUseOverlayAcquisitionException>(ReferenceEqualityComparer.Instance))
                    lock (_actualStartupAcquisitionGate) _actualFailedStartupAcquisitionCloses.Add(acquisition.OriginalRetirement);
                // No new wrapper/admission is needed after acquiring a raw cleanup original.
                // Even retirement reentry cannot drop this actual already-owned Task.
                throw;
            }
        });
        return actual ?? throw new InvalidOperationException("The original App Computer Use resolution supplied no actual singleton.");
    }
    private static IEnumerable<ComputerUseOverlayAcquisitionException> CaptureActualComputerUseAcquisitionFailures(Exception original)
    {
        if (original is ComputerUseOverlayAcquisitionException acquisition) yield return acquisition;
        if (original is AggregateException group)
        {
            foreach (var direct in group.InnerExceptions)
                foreach (var owned in CaptureActualComputerUseAcquisitionFailures(direct)) yield return owned;
        }
        else if (original.InnerException is { } direct)
            foreach (var owned in CaptureActualComputerUseAcquisitionFailures(direct)) yield return owned;
    }
    private async Task JoinOriginalFailedStartupAcquisitionsAsync()
    {
        Task[] originals;
        lock (_actualStartupAcquisitionGate) originals = _actualFailedStartupAcquisitionCloses.ToArray();
        var failures = new List<Exception>();
#if !ANDROID
        // Pure Home preflight precedes every failed-startup retirement effect.
        _originalAppWork.RunCloseCallback(DemandOriginalWindowsHomeRetirementJoin);
        _originalAppWork.RunCloseCallback(DemandOriginalWindowsNativeRouteRetirementJoin);
        _originalAppWork.RunCloseCallback(DemandOriginalWindowsDeveloperRetirementJoin);
        _originalAppWork.RunCloseCallback(DemandOriginalNativeCakeAccountRetirementJoin);
        _originalAppWork.RunCloseCallback(DemandOriginalNativeHomeApprovalRetirementJoin);
        _originalAppWork.RunCloseCallback(DemandOriginalAssistantRetirementJoin);
        Task? actualCanonicalProcessDrain = null;
        try { _originalAppWork.RunCloseCallback(() => actualCanonicalProcessDrain = JoinOriginalUntransferredCanonicalProcessBorrowersAsync()); }
        catch (Exception error) { AddAppCause(failures, error); }
        RequestOriginalWindowsDeveloperRetirement(failures);
        Task? actualAssistantBusinessDrain = null;
        try { _originalAppWork.RunCloseCallback(() => actualAssistantBusinessDrain = JoinOriginalUntransferredAssistantBusinessAsync()); }
        catch (Exception error) { AddAppCause(failures, error); }
        Task? actualAssistantScopeDrain = null;
        try { _originalAppWork.RunCloseCallback(() => actualAssistantScopeDrain = JoinOriginalUntransferredAssistantScopesAsync()); }
        catch (Exception error) { AddAppCause(failures, error); }
        Task? actualNativeHomeApprovalDrain = null;
        try { _originalAppWork.RunCloseCallback(() => actualNativeHomeApprovalDrain = JoinOriginalUntransferredNativeHomeApprovalsAsync()); }
        catch (Exception error) { AddAppCause(failures, error); }
        Task? actualNativeCakeAccountDrain = null;
        try { _originalAppWork.RunCloseCallback(() => actualNativeCakeAccountDrain = JoinOriginalUntransferredNativeCakeAccountAsync()); }
        catch (Exception error) { AddAppCause(failures, error); }
        Task? actualWindowsNativeRoutesDrain = null;
        try { _originalAppWork.RunCloseCallback(() => actualWindowsNativeRoutesDrain = JoinOriginalUntransferredWindowsNativeRoutesAsync()); }
        catch (Exception error) { AddAppCause(failures, error); }
        Task? actualNativeDevelopmentDrain = null;
        try { _originalAppWork.RunCloseCallback(() => actualNativeDevelopmentDrain = JoinOriginalUntransferredNativeDevelopmentBorrowersAsync()); }
        catch (Exception error) { AddAppCause(failures, error); }
#endif
        foreach (var actual in originals) await JoinOriginalAppTaskAsync(actual, failures);
#if !ANDROID
        if (actualCanonicalProcessDrain is not null)
            await JoinOriginalAppTaskAsync(actualCanonicalProcessDrain, failures);
        if (actualAssistantBusinessDrain is not null)
            await JoinOriginalAppTaskAsync(actualAssistantBusinessDrain, failures);
        if (actualAssistantScopeDrain is not null)
            await JoinOriginalAppTaskAsync(actualAssistantScopeDrain, failures);
        if (actualWindowsNativeRoutesDrain is not null)
            await JoinOriginalAppTaskAsync(actualWindowsNativeRoutesDrain, failures);
        if (actualNativeDevelopmentDrain is not null)
            await JoinOriginalAppTaskAsync(actualNativeDevelopmentDrain, failures);
        if (actualNativeCakeAccountDrain is not null)
            await JoinOriginalAppTaskAsync(actualNativeCakeAccountDrain, failures);
        if (actualNativeHomeApprovalDrain is not null)
            await JoinOriginalAppTaskAsync(actualNativeHomeApprovalDrain, failures);
        Task? actualWindowsDeveloperDrain = null;
        if ((actualCanonicalProcessDrain is null || actualCanonicalProcessDrain.IsCompletedSuccessfully) &&
            (actualWindowsNativeRoutesDrain is null || actualWindowsNativeRoutesDrain.IsCompletedSuccessfully) &&
            (actualNativeDevelopmentDrain is null || actualNativeDevelopmentDrain.IsCompletedSuccessfully))
        {
            try { _originalAppWork.RunCloseCallback(() => actualWindowsDeveloperDrain = JoinOriginalUntransferredWindowsDeveloperBorrowersAsync()); }
            catch (Exception error) { AddAppCause(failures, error); }
            if (actualWindowsDeveloperDrain is not null) await JoinOriginalAppTaskAsync(actualWindowsDeveloperDrain, failures);
        }
        else AddAppCause(failures, new InvalidOperationException("Original Windows developer completion dependencies remain live because business/source retirement did not settle successfully."));
#endif
#if !ANDROID
        // A failed actual business/source join keeps the SAME Den and Home alive.
        // Null is absence only when no owner was acquired; acquisition failures remain in failures.
        Task? actualAssistantDenDrain = null;
        if (failures.Count == 0 &&
            (actualAssistantBusinessDrain is null || actualAssistantBusinessDrain.IsCompletedSuccessfully) &&
            (actualAssistantScopeDrain is null || actualAssistantScopeDrain.IsCompletedSuccessfully))
        {
            try { _originalAppWork.RunCloseCallback(() => actualAssistantDenDrain = JoinOriginalUntransferredAssistantPersonalDenAsync()); }
            catch (Exception error) { AddAppCause(failures, error); }
            if (actualAssistantDenDrain is not null) await JoinOriginalAppTaskAsync(actualAssistantDenDrain, failures);
        }
        // Do not retire Home while any acquired borrower or the Den lease remains unresolved.
        if (failures.Count == 0 &&
            (actualAssistantBusinessDrain is null || actualAssistantBusinessDrain.IsCompletedSuccessfully) &&
            (actualAssistantScopeDrain is null || actualAssistantScopeDrain.IsCompletedSuccessfully) &&
            (actualAssistantDenDrain is null || actualAssistantDenDrain.IsCompletedSuccessfully) &&
            (actualCanonicalProcessDrain is null || actualCanonicalProcessDrain.IsCompletedSuccessfully) &&
            (actualNativeDevelopmentDrain is null || actualNativeDevelopmentDrain.IsCompletedSuccessfully) &&
            (actualWindowsNativeRoutesDrain is null || actualWindowsNativeRoutesDrain.IsCompletedSuccessfully) &&
            (actualWindowsDeveloperDrain is { IsCompletedSuccessfully: true }) &&
            (actualNativeHomeApprovalDrain is null || actualNativeHomeApprovalDrain.IsCompletedSuccessfully))
        {
            Task? actualHomeDrain = null;
            try { _originalAppWork.RunCloseCallback(() => actualHomeDrain = JoinOriginalUntransferredWindowsHomeAsync()); }
            catch (Exception error) { AddAppCause(failures, error); }
            if (actualHomeDrain is not null) await JoinOriginalAppTaskAsync(actualHomeDrain, failures);
        }
#endif
        ThrowAppCauses(failures); // Failed partial construction remains failed, never inferred as no effect.
    }
    private Task StopOriginalAppSourcesAsync()
    {
        StopDiagnosticSnapshot();
        // Stop admission/subscriptions only. Diagnostics/provider remain borrowed
        // until final save, prepared audit and ALL actual child originals settle.
        _originalAppWork.RunCloseCallback(DetachExceptionHooks);
        if (_actualSubscribedUpdates is { } updates)
            _originalAppWork.RunCloseCallback(() => updates.StatusChanged -= OnUpdateStatusChanged);
        _originalAppWork.RunCloseCallback(() => UpdateOrchestrator.PendingUpdateDetectedOnStartup -= OnPendingStartupUpdateDetected);
        return Task.CompletedTask;
    }
    private async Task JoinOriginalAppProducersAsync()
    {
        DemandOriginalDesktopBorrowerJoins(_actualDesktopBorrowers.Concat(_actualDesktop?.Windows.OfType<MainWindow>() ?? [])
            .Distinct(ReferenceEqualityComparer.Instance).ToArray());
        if (_synchronousAppSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual App callback must return before its external retirement join.");
        var failures = new List<Exception>();
        foreach (var actual in _actualDocumentClosePreflightDeliveries.ToArray())
            await JoinOriginalAppTaskAsync(actual, failures);
#if !ANDROID
        // Browse closes its actual tab/presentation producers while App/Home and
        // window lifetimes still exist. Its canonical projection then seals/drains
        // before the coordinator captures the final workspace snapshot.
        if (_actualUnifiedWorkspace is { } unified)
        {
            try { await JoinOriginalAppTaskAsync(unified.CloseAndDrainAsync(), failures); }
            catch (Exception error) { AddAppCause(failures, error); }
        }
#endif
        // A close may arrive while the App startup/bootstrap originals are STILL live (early close during
        // Loading). Those originals are cooperative: retiring the App lifetime cancels their token (so no
        // late startup/model-bootstrap work is admitted after close) and lets them settle. The external-close
        // guard rejects draining while they execute, so start the cooperative retirement FIRST; the SAME
        // published close is then drained idempotently. Genuine faults are retained and rethrown.
        await JoinEarlyStartupCooperativelyAsync(_originalAppWork, OriginalStartup, failures);
        ThrowAppCauses(failures);
    }

    /// <summary>Starts the cooperative App retirement when an early close arrives while the startup
    /// bootstrap original is still live, then drains the SAME published close idempotently. This is the
    /// exact production early-close branch used by <see cref="JoinOriginalAppProducersAsync"/>; exposed
    /// internally so the maintained App owner rig can drive the real path while a held startup source is
    /// live. The raw startup/bootstrap source cancels under the owner lifetime token, so no late
    /// startup/child work is admitted after close. Genuine faults are retained in
    /// <paramref name="failures"/> and rethrown by the caller.</summary>
    internal static async Task JoinEarlyStartupCooperativelyAsync(
        DesktopOriginalWorkLifetime appWork,
        Task? startup,
        List<Exception> failures)
    {
        if (startup is not { IsCompleted: true } && appWork.OriginalClose is null)
        {
            appWork.RequestRetirement();
            var startupClose = appWork.OriginalClose;
            if (startupClose is not null)
            {
                try { await JoinOriginalAppTaskAsync(startupClose, failures); }
                catch (Exception error) { AddAppCause(failures, error); }
            }
        }
        await JoinOriginalAppTaskAsync(appWork.CloseAndDrainAsync(), failures);
    }
    private void ConfigureOriginalDesktopShutdown(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, MainView? shell)
    {
        var services = _services ?? throw new InvalidOperationException("The actual original App provider is unavailable.");
        var sessions = services.GetRequiredService<WorkspaceSessionCoordinator>();
        var windows = services.GetRequiredService<WorkspaceWindowService>();
        var notifications = services.GetRequiredService<NotificationService>();
        var actualRecovery = _startupRecovery as IStartupRecoveryFinalCleanWriterSource
            ?? throw new InvalidOperationException("The actual startup owner has no prepared final-writer port.");
        var actualBorrowers = new List<object>();
#if !ANDROID
        // After whole-cohort pure preflight, seal canonical business admission
        // FIRST, before shell/window retirement callbacks can initiate more work.
        actualBorrowers.Add(CaptureOriginalCanonicalProcessBorrower());
        if (_actualAssistantConversationProcess is { } actualAssistantProcess)
            actualBorrowers.Add(actualAssistantProcess);
#endif
        if (shell is not null) actualBorrowers.Add(shell);
#if !ANDROID
        else actualBorrowers.Add(_actualUnifiedWorkspace ?? throw new InvalidOperationException("The original unified borrower is unavailable."));
#endif
        actualBorrowers.AddRange([windows, notifications]);
#if !ANDROID
        actualBorrowers.AddRange(CaptureOriginalNativeDevelopmentBorrowers(services));
        actualBorrowers.Add(CaptureOriginalNativeCakeAccountBorrower(services));
        actualBorrowers.AddRange(CaptureOriginalNativeHomeApprovalBorrowers(services));
#endif
        if (_actualComputerUseOverlay is { } actualComputerUse) actualBorrowers.Add(actualComputerUse);
        else throw new InvalidOperationException("The original App has not retained its actual Computer Use singleton.");
#if !ANDROID
        // This is the exact existing singleton alias, used by later app activities too.
        // The maintained host constructor creates no Windows; its actual admission is retired here.
        actualBorrowers.Add(services.GetRequiredService<IFloatingActivityHost>());
#endif
#if !ANDROID
        // This is the SAME already-resolved controller from startup, never a substitute.
        actualBorrowers.Add(services.GetRequiredService<Haven.Desktop.Overlay.OverlayWorkspaceController>());
#endif
        _actualDesktop = desktop; _actualPrimaryWindow = window;
        _actualDesktopBorrowers = actualBorrowers.ToArray();
        _actualDocumentClosePrimaryShell = shell;
        _actualShutdownSequence = new(JoinOriginalAppProducersAsync,
            () => sessions.SaveFinalSnapshotAndSealAsync(CancellationToken.None),
            () => PrepareFinalWriterAfterOriginalAppRetirementAsync(_originalAppWork, actualRecovery),
            () => JoinOriginalDesktopBorrowersAsync(actualBorrowers, desktop),
            () => services.DisposeAsync().AsTask(),
            signal => Dispatcher.UIThread.InvokeAsync(signal, DispatcherPriority.Background).GetTask());
#if !ANDROID
        lock (_actualStartupAcquisitionGate)
        {
            _canonicalProcessBorrowersTransferred = true;
            _assistantOwnersTransferred = true;
            _nativeDevelopmentBorrowersTransferred = true;
            _windowsHomeBorrowerTransferred = true;
            _windowsNativeRoutesTransferred = true;
            _windowsDeveloperBorrowersTransferred = true;
            _nativeCakeBorrowerTransferred = true;
            _nativeHomeApprovalBorrowerTransferred = true;
        }
#endif
        ConfigurePrimaryNativeClosingPolicy(window);
        window.Closing += OnOriginalPrimaryWindowClosing;
        desktop.ShutdownRequested += OnOriginalDesktopShutdownRequested;
        window.Closed += OnOriginalUnexpectedPrimaryWindowClosed;
    }

    internal static Task<IStartupRecoveryFinalCleanWriter> PrepareFinalWriterAfterOriginalAppRetirementAsync(
        DesktopOriginalWorkLifetime appWork, IStartupRecoveryFinalCleanWriterSource recovery)
    {
        ArgumentNullException.ThrowIfNull(appWork);
        ArgumentNullException.ThrowIfNull(recovery);
        var actualRetirement = appWork.OriginalClose;
        if (actualRetirement is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The actual App startup retirement must finish before final writer preparation.");
        // Older implementations keep their strict completed-startup contract. The
        // real recovery owner additionally preserves a deliberately interrupted run.
        return recovery is IStartupRecoveryRetiredStartupFinalCleanWriterSource interrupted
            ? interrupted.PrepareFinalCleanWriterAfterStartupRetirementAsync(actualRetirement, CancellationToken.None)
            : recovery.PrepareFinalCleanWriterAsync(CancellationToken.None);
    }

    /// <summary>Applies the pinned a390 primary-native closing policy to the actual configured PRIMARY window:
    /// <see cref="Avalonia.Controls.WindowClosingBehavior.OwnerWindowOnly"/>. With the default
    /// OwnerAndChildWindows behavior, Avalonia <see cref="Avalonia.Controls.Window.ShouldCancelClose"/> first asks
    /// every owned child; when an owned Account child cancels/defers its own closure (as
    /// <see cref="NativeCakeAccountWindow.OnClosing"/> does), the parent's <c>OnClosing</c> is skipped entirely, so
    /// the FIRST native main close would retire the child without ever reaching the App-owned canonical
    /// document/cohort preflight. OwnerWindowOnly makes the primary's OWN close reach that entry while the child
    /// stays alive/pending until the canonical owner settles. Genuine production wiring
    /// (<see cref="ConfigureOriginalDesktopShutdown"/>) calls this; it is the ONLY caller. This is an
    /// infrastructure/windowing policy, not new application authorship: global Window defaults, secondary-window
    /// policy, the child's own cancel/defere guard, and all veto/cancel/fault semantics are untouched.</summary>
    internal static void ConfigurePrimaryNativeClosingPolicy(Avalonia.Controls.Window primary)
    {
        primary.ClosingBehavior = Avalonia.Controls.WindowClosingBehavior.OwnerWindowOnly;
    }
    private void OnOriginalPrimaryWindowClosing(object? sender, Avalonia.Controls.WindowClosingEventArgs args)
    {
        if (_actualWindowClosure is not null) return; // Privately issued native-close original AFTER resource owners joined, never a clean-authority boolean.
        args.Cancel = true;
        // The window silently refusing to close is the owner-reported symptom; without a
        // durable record of every preflight stage nobody can tell whether a hosted owner
        // (Unified preflight, shell preflight, or model routing) stalled. This writer is
        // append-only, bounded, and never replaces the cancelled close itself.
        WriteOriginalCloseDiagnostic("window-close requested; preflight starts");
        RequestOriginalDocumentClosePreflight();
    }
    private static void WriteOriginalCloseDiagnostic(string message)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var directory = Path.Combine(appData, "Haven", "Logs");
            Directory.CreateDirectory(directory);
            var line = $"[{DateTimeOffset.Now:O}] close-preflight: {message}{Environment.NewLine}";
            var path = Path.Combine(directory, "close-preflight-diagnostic.log");
            // Bounded tail: keep the file useful instead of unbounded across restarts.
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }
        catch { /* A diagnostic writer must never block or throw into the close path. */ }
    }
    private void OnOriginalDesktopShutdownRequested(object? sender, ShutdownRequestedEventArgs args)
    {
        if (_actualShutdownSequence?.OriginalShutdown is { IsCompletedSuccessfully: true } &&
            _actualWindowClosure is { IsCompletedSuccessfully: true }) return;
        args.Cancel = true;
        RequestOriginalDocumentClosePreflight();
    }
    private void OnOriginalUnexpectedPrimaryWindowClosed(object? sender, EventArgs args)
    {
        if (_actualWindowClosure is null)
            ReportOriginalShutdownFailure(_actualShutdownFailure ??= new InvalidOperationException(
                "The native primary window closed without the actual original host drain. Clean remains unacknowledged."));
    }
    private void RequestOriginalDocumentClosePreflight()
    {
        if (_actualShutdownDelivery is not null || _actualDocumentClosePreflightPublication is { IsCompleted: false }) return;
        var start = new TaskCompletionSource();
        var attempt = new OriginalDocumentCloseAttempt();
        _actualDocumentClosePreflightDelivery = DeliverOriginalDocumentClosePreflightAsync(start.Task, attempt);
        _actualDocumentClosePreflightDeliveries.RemoveAll(actual => actual.IsCompletedSuccessfully);
        _actualDocumentClosePreflightDeliveries.Add(_actualDocumentClosePreflightDelivery);
        _actualDocumentClosePreflightPublication = PublishOriginalDocumentClosePreflightAsync(
            _actualDocumentClosePreflightDelivery, attempt);
        // This SAME App-owned attempt exists before any page/service callback. An
        // ordinary declined save does not acquire or poison permanent shutdown.
        try
        {
            _ = _originalAppWork.RunAsync(async original =>
            {
                Dispatcher.UIThread.VerifyAccess();
                var desktop = _actualDesktop ?? throw new InvalidOperationException("The actual native desktop owner is unavailable.");
                attempt.Desktop = desktop;
                attempt.Windows = desktop.Windows.ToArray();
                attempt.Shells = CaptureOriginalDocumentCloseShells(desktop);
                attempt.Candidates = attempt.Shells.Select(shell => AcquireOriginalAppSynchronous(original,
                    shell.CaptureOriginalDocumentClosePreflight)).ToArray();
                WriteOriginalCloseDiagnostic($"captured {attempt.Shells.Length} close shells and {attempt.Candidates.Length} preflight candidates");
                foreach (var candidate in attempt.Candidates)
                {
                    WriteOriginalCloseDiagnostic($"shell preflight {candidate.GetType().Name} joining");
                    if (!await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                        () => candidate.PrepareAsync(CancellationToken.None))))
                    {
                        WriteOriginalCloseDiagnostic($"shell preflight {candidate.GetType().Name} refused; close stays cancelled");
                        return;
                    }
                    WriteOriginalCloseDiagnostic($"shell preflight {candidate.GetType().Name} joined");
                }
#if !ANDROID
                attempt.Unified = _actualUnifiedWorkspace;
                if (attempt.Unified is { } unified)
                {
                    attempt.UnifiedPreparation = AcquireOriginalAppSynchronous(original,
                        () => unified.PrepareCloseAsync(CancellationToken.None));
                    WriteOriginalCloseDiagnostic("unified workspace close preparation acquired");
                    try { await original.AwaitAsync(attempt.UnifiedPreparation); }
                    finally
                    {
                        // AwaitAsync may reject publication AFTER the actual child
                        // yielded a lease. Recover that SAME result for external abort.
                        if (attempt.UnifiedPreparation.IsCompletedSuccessfully)
                            attempt.UnifiedLease = attempt.UnifiedPreparation.GetAwaiter().GetResult();
                    }
                    WriteOriginalCloseDiagnostic(attempt.UnifiedLease is null
                        ? "unified workspace close preparation returned no lease"
                        : $"unified workspace close lease accepted={attempt.UnifiedLease.IsAcceptedOriginal}");
                    if (attempt.UnifiedLease is null)
                    {
                        WriteOriginalCloseDiagnostic("unified workspace close lease missing; close stays cancelled");
                        return;
                    }
                }
                WriteOriginalCloseDiagnostic("all close preflight candidates and the unified lease joined");
#endif
                original.DemandPublication();
                attempt.Prepared = true; // Candidate only; delivery still joins this SAME actual Task.
            }, actual => attempt.Original = _actualDocumentClosePreflightAttempt = actual);
        }
        catch (Exception failure) { attempt.AcquisitionFailure = failure; throw; }
        finally { start.SetResult(); }
    }
    private sealed class OriginalDocumentCloseAttempt
    {
        internal Task? Original;
        internal Exception? AcquisitionFailure;
        internal IClassicDesktopStyleApplicationLifetime? Desktop;
        internal Avalonia.Controls.Window[] Windows = [];
        internal MainView[] Shells = [];
        internal OriginalDocumentClosePreflight[] Candidates = [];
#if !ANDROID
        internal UnifiedWorkspaceHost? Unified;
        internal Task<UnifiedWorkspaceHost.ClosePreparation?>? UnifiedPreparation;
        internal UnifiedWorkspaceHost.ClosePreparation? UnifiedLease;
        internal Task? UnifiedRetirement, UnifiedReportedRetirement, UnifiedAbort;
#endif
        internal bool Prepared;
    }
    private async Task DeliverOriginalDocumentClosePreflightAsync(Task start, OriginalDocumentCloseAttempt attempt)
    {
        await start;
        if (attempt.AcquisitionFailure is { } acquisitionFailure) ExceptionDispatchInfo.Capture(acquisitionFailure).Throw();
        var actual = attempt.Original ?? throw new InvalidOperationException("The original document preflight returned no Task.");
        var failures = new List<Exception>();
        await JoinOriginalAppTaskAsync(actual, failures);
        ThrowAppCauses(failures); // Exact terminal task, including every raw fault sibling.
    }
    private async Task PublishOriginalDocumentClosePreflightAsync(Task actualHandoff, OriginalDocumentCloseAttempt attempt)
    {
        var failures = new List<Exception>();
        try { await PublishSettledAttemptAsync(); }
        catch (Exception failure) { AddAppCause(failures, failure); }
        finally
        {
#if !ANDROID
            // This outer driver owns restoration even when the actual originating
            // preflight/handoff faulted or any later currentness/borrower check vetoed.
            if (attempt.UnifiedLease is { IsAcceptedOriginal: false } unaccepted)
            {
                try
                {
                    attempt.UnifiedAbort ??= unaccepted.AbortAsync();
                    await JoinOriginalAppTaskAsync(attempt.UnifiedAbort, failures);
                }
                catch (Exception cleanup) { AddAppCause(failures, cleanup); }
            }
#endif
        }
        ThrowAppCauses(failures);

        async Task PublishSettledAttemptAsync()
        {
            await JoinOriginalAppTaskAsync(actualHandoff, failures);
            ThrowAppCauses(failures); // SAME actual origin/handoff terminal before encompassing retirement.
            if (!attempt.Prepared) return;
            Dispatcher.UIThread.VerifyAccess();
            if (!IsOriginalDocumentCloseAttemptCurrent(attempt))
            {
#if !ANDROID
                // A startup lease (captured while initialization/loading was pending) becomes non-current
                // when the actual owned startup transition completes and creates chrome/workspace. That
                // is a benign invalidation, not a failure: retain the ONE approved user close by joining/
                // aborting the old unaccepted lease and reacquiring the CURRENT preparation WITHOUT waiting
                // for the whole startup or any user Welcome input. Genuine save-vetoes / declined closes,
                // foreign OCE and IO/fault siblings are never re-driven or cleared.
                if (attempt.UnifiedLease is { Startup: true } && attempt.Unified is not null)
                {
                    if (!await TryReplanStaleStartupLeaseAsync(attempt, failures)) return;
                    // The startup replan settled (TRUE on a successful swap). A positively witnessed owned-
                    // child retirement may ALSO have landed during the SAME close: route the fallback to the
                    // SAME unified replan so the same fresh canonical preflight + exactly-once completion run
                    // without losing the request or waiting on startup/Welcome.
                    if (!IsOriginalDocumentCloseAttemptCurrent(attempt)
                        && !await TryReplanRetiredOwnedChildWindowAsync(attempt, failures))
                        return;
                }
                // Non-startup invalidation: ONLY the positively witnessed retirement of an ALREADY-owned
                // native Account child (captured in the original window cohort) may re-preflight the REAL
                // current cohort and complete the ONE close. Every other cohort change (new/unowned or
                // document window, veto/decline, abort/preflight/callback fault) keeps its real authority
                // and ends the request below.
                else if (!await TryReplanRetiredOwnedChildWindowAsync(attempt, failures))
                {
                    return;
                }
#else
                return;
#endif
            }
#if !ANDROID
            // Immediately before the destructive BeginClose, recheck FULL canonical currentness/cohort so an
            // unrelated window addition/removal/reorder/doc edit that landed during the pending fresh preflight
            // or lease reacquire ends the request here rather than being swept into retirement. Checking after
            // accepted retirement is too late.
            if (!IsOriginalDocumentCloseAttemptCurrent(attempt)) return;
            if (attempt.Unified is { } unified)
            {
                var desktop = attempt.Desktop!;
                DemandOriginalDesktopBorrowerJoins(_actualDesktopBorrowers.Concat(desktop.Windows.OfType<MainWindow>())
                    .Distinct(ReferenceEqualityComparer.Instance).ToArray());
                try { attempt.UnifiedRetirement = attempt.UnifiedLease!.BeginClose(); }
                catch (Exception failure) { AddAppCause(failures, failure); }
                // A throwing acquisition/observer can already have issued the real
                // original. Retain both references before deciding whether to abort.
                try { attempt.UnifiedReportedRetirement = unified.OriginalClose; }
                catch (Exception observation) { AddAppCause(failures, observation); }
                if (attempt.UnifiedRetirement is { } actual)
                    await JoinOriginalAppTaskAsync(actual, failures);
                try { attempt.UnifiedReportedRetirement ??= unified.OriginalClose; }
                catch (Exception observation) { AddAppCause(failures, observation); }
                if (attempt.UnifiedReportedRetirement is { } reported
                    && !ReferenceEquals(reported, attempt.UnifiedRetirement))
                    await JoinOriginalAppTaskAsync(reported, failures);
                if (!attempt.UnifiedLease!.IsAcceptedOriginal)
                {
                    // A legacy bool owner can still veto its current close. Its
                    // weak token never claimed frozen edits or accepted retirement.
                    if (failures.All(failure => failure is BrowseCloseVetoException or UnifiedWorkspaceHost.CloseVetoException)) failures.Clear();
                    ThrowAppCauses(failures); return;
                }
                // Accepted failures remain in this receipt AND the SAME host original
                // joined again by the shutdown sequence, which withholds clean/save.
            }
#endif
            // Post-BeginClose reconcile: the unified close has now begun and may already be accepted. If the
            // ONLY window-cohort delta is the positively witnessed retirement of an already-owned Account child
            // that landed DURING the awaited BeginClose, run the SAME production sequencer through fresh
            // canonical preflight and exactly-once completion WITHOUT re-invoking or undoing the accepted
            // unified close. Every other invalidation keeps its real authority and ends the request below.
            if (!IsOriginalDocumentCloseAttemptCurrent(attempt))
            {
                if (!await TryRefreshCohortAfterWitnessedRetirementAsync(attempt, failures)) return;
                // The post-accept sequencer's complete-once stage already delivered the ONE shutdown exactly
                // once; do not deliver again below.
            }
            else
            {
                // No reconcile was needed (already current): deliver the ONE approved shutdown exactly once.
                DeliverPostAcceptShutdownOnce(attempt);
            }
        }
    }
    private bool IsOriginalDocumentCloseAttemptCurrent(OriginalDocumentCloseAttempt attempt)
    {
        var desktop = attempt.Desktop ?? throw new InvalidOperationException("The actual document desktop is unavailable.");
        return IsOriginalDocumentCloseAttemptCoreCurrent(attempt)
            && attempt.Windows.SequenceEqual(desktop.Windows, ReferenceEqualityComparer.Instance);
    }

    /// <summary>Every currentness invariant of <see cref="IsOriginalDocumentCloseAttemptCurrent"/> EXCEPT
    /// the window-cohort equality. A positively witnessed owned-child retirement only changes the window
    /// cohort, so a replan must first prove everything else is still current.</summary>
    private bool IsOriginalDocumentCloseAttemptCoreCurrent(OriginalDocumentCloseAttempt attempt)
    {
        var desktop = attempt.Desktop ?? throw new InvalidOperationException("The actual document desktop is unavailable.");
        if (_originalAppWork.IsRetiring || !ReferenceEquals(desktop, _actualDesktop) ||
            !attempt.Shells.SequenceEqual(CaptureOriginalDocumentCloseShells(desktop), ReferenceEqualityComparer.Instance) ||
            attempt.Candidates.Any(candidate => !candidate.IsCurrentAndPrepared)) return false;
#if !ANDROID
        if (!ReferenceEquals(attempt.Unified, _actualUnifiedWorkspace) ||
            (attempt.Unified is not null && (attempt.UnifiedPreparation is not { IsCompletedSuccessfully: true }
                || attempt.UnifiedLease is not { } lease
                || !ReferenceEquals(attempt.UnifiedPreparation.GetAwaiter().GetResult(), lease)
                || !(lease.IsAcceptedOriginal || lease.IsCurrent)))) return false;
#endif
        return true;
    }
    private MainView[] CaptureOriginalDocumentCloseShells(IClassicDesktopStyleApplicationLifetime desktop) =>
        (_actualDocumentClosePrimaryShell is { } primary ? new[] { primary } : Array.Empty<MainView>())
            .Concat(desktop.Windows.OfType<MainWindow>().Select(window => window.DataContext).OfType<MainView>())
            .Distinct<MainView>(ReferenceEqualityComparer.Instance).ToArray();

    /// <summary>Re-captures the current primary-shell/document obligations with genuine production authority
    /// after a positively witnessed owned-child retirement and re-prepares them. Raw faults are retained in
    /// <paramref name="failures"/>; a real save-veto / declined preparation returns false so the caller ends
    /// the request without retrying.</summary>
    private async Task<bool> ReprepareCurrentDocumentsAndShellsAsync(OriginalDocumentCloseAttempt attempt,
        IClassicDesktopStyleApplicationLifetime desktop, List<Exception> failures)
    {
        MainView[] shells = CaptureOriginalDocumentCloseShells(desktop);
        var candidates = new List<OriginalDocumentClosePreflight>();
        foreach (var shell in shells)
        {
            OriginalDocumentClosePreflight? candidate;
            try { candidate = shell.CaptureOriginalDocumentClosePreflight(); }
            catch (Exception failure) { AddAppCause(failures, failure); continue; }
            candidates.Add(candidate);
            // Capture the RAW prepare Task before awaiting so its complete terminal state stays inspectable
            // and EVERY aggregate sibling (foreign OCE/cancel) is retained by the SAME production custody
            // rule as the main wiring. A declined / save-vetoed preparation ends the request without
            // retrying; a faulted/canceled preparation retains all causes and also ends it.
            Task<bool> prepare;
            try { prepare = candidate.PrepareAsync(CancellationToken.None); }
            catch (Exception sync) { AddAppCause(failures, sync); continue; }
            await JoinOriginalAppTaskAsync(prepare, failures);
            if (failures.Count == 0 && prepare.Status == TaskStatus.RanToCompletion && !prepare.Result)
                return false; // save veto / not prepared: end the request
        }
        if (failures.Count > 0) return false;
        attempt.Shells = shells;
        attempt.Candidates = candidates.ToArray();
        return true;
    }

    /// <summary>Retains ONE approved user close across ONLY the benign loading→Apps startup-transition
    /// invalidation. A startup lease captured while initialization/loading was pending becomes non-current
    /// once the actual owned startup completes and creates chrome/workspace. Instead of dropping the close
    /// (the pre-fix loss), this joins/aborts the old unaccepted lease and reacquires the CURRENT
    /// preparation immediately — it does NOT wait for the whole startup or any user Welcome action. A
    /// genuine save-veto / declined close ends the request without auto-retry; foreign OCE and IO/fault
    /// siblings stay retained as exact causes and are never cleared or repeated.</summary>
    private async Task<bool> TryReplanStaleStartupLeaseAsync(OriginalDocumentCloseAttempt attempt, List<Exception> failures)
    {
#if !ANDROID
        var stale = attempt.UnifiedLease;
        var unified = attempt.Unified;
        if (stale is not { Startup: true } || unified is null) return false;
        if (_originalAppWork.IsRetiring || _actualShutdownDelivery is not null) return false;
        Task<UnifiedWorkspaceHost.ClosePreparation?> reprepared = Task.FromResult<UnifiedWorkspaceHost.ClosePreparation?>(null);
        var replanned = await StartupTransitionCloseReplanner.ReplanAsync<UnifiedWorkspaceHost.ClosePreparation>(
            stale,
            () => attempt.UnifiedAbort ??= stale.AbortAsync(),
            () => { var t = unified.PrepareCloseAsync(CancellationToken.None); reprepared = t; return t; },
            failures);
        if (replanned is null) return false; // declined / genuine fault retained; request ends
        // The old lease's actual abort was joined by the sequencer above. Release the alias so a NEW
        // (replaced) lease that later fails/refuses gets its OWN exact abort original rather than reusing
        // the old completed one. Raw custody of the old lease/task remains with the already-tracked source.
        attempt.UnifiedAbort = null;
        // Point the attempt at the reacquired CURRENT lease so the existing begin-close path uses it.
        attempt.UnifiedPreparation = reprepared;
        attempt.UnifiedLease = replanned;
        // The startup swap succeeded: return TRUE so the caller continues to the unified replan fallback
        // (a positively witnessed owned-child retirement may also have landed) instead of returning early
        // and making the combined startup+child-retirement branch unreachable.
        return true;
#else
        return false;
#endif
    }

    /// <summary>Retains ONE approved user close across ONLY the benign, positively witnessed retirement of an
    /// ALREADY-owned native Account child that was part of the original captured window cohort. That
    /// retirement is a real ownership event, not a fault: the child settled cleanly and left the cohort.
    /// Unlike a new/unowned/document window, a veto, a decline or any fault (which keep their real authority
    /// and end the request), this loses no owner fidelity, so the SAME close re-preflights the REAL current
    /// cohort and completes once. This runs BEFORE the unified close is begun (so the old unaccepted lease
    /// can still be joined/aborted) through the production stage sequencer; a retirement that lands DURING
    /// the awaited accepted BeginClose is reconciled separately by
    /// <see cref="TryRefreshCohortAfterWitnessedRetirementAsync"/>, which runs the SAME sequencer through
    /// fresh canonical preflight and exactly-once completion without re-invoking an accepted unified close.</summary>
    private async Task<bool> TryReplanRetiredOwnedChildWindowAsync(OriginalDocumentCloseAttempt attempt, List<Exception> failures)
    {
#if !ANDROID
        var desktop = attempt.Desktop ?? throw new InvalidOperationException("The actual document desktop is unavailable.");
        if (_originalAppWork.IsRetiring || _actualShutdownDelivery is not null) return false;
        if (!IsOriginalDocumentCloseAttemptCoreCurrent(attempt)) return false;
        var originalCohort = attempt.Windows;

        var sequencer = new FirstMainCloseStageSequencer<Avalonia.Controls.Window>(
            cohortAuthorize: () => OwnedChildRetirementCloseReplanner.TryRefreshCohortForWitnessedRetirement(
                originalCohort,
                desktop.Windows,
                window => IsWitnessedRetiredOwnedAccountChild(_actualNativeCakeAccountOwner, window, originalCohort)),
            applyCohort: refreshed => attempt.Windows = refreshed,
            canonicalCurrent: () => IsOriginalDocumentCloseAttemptCoreCurrent(attempt),
            isChildCloseSettled: () => WitnessedChildCloseSettled(originalCohort, desktop.Windows),
            freshCanonicalPreflight: () => ReprepareCurrentDocumentsAndShellsAsync(attempt, desktop, failures),
            resolveUnifiedLease: () => ResolveReplanUnifiedLeaseAsync(attempt, failures),
            isFullCurrent: () => IsOriginalDocumentCloseAttemptCurrent(attempt),
            completeShutdown: () => IsOriginalDocumentCloseAttemptCurrent(attempt)); // pre-BeginClose gate: App fires BeginClose next; completion (delivery) is not here.

        return await sequencer.RunAsync();
#else
        return false;
#endif
    }

    /// <summary>Recovers the ONE approved close after the unified BeginClose has begun (and may already be
    /// accepted) by running the SAME production stage sequencer when the sole window-cohort delta is a
    /// positively witnessed owned-child retirement. It performs a FRESH canonical document/shell re-preflight
    /// (not just a snapshot refresh) but NEVER re-invokes, aborts, or undoes an accepted unified close, then
    /// delivers the one shutdown exactly once. Any other invalidation keeps its real authority and ends the
    /// request.</summary>
    private async Task<bool> TryRefreshCohortAfterWitnessedRetirementAsync(OriginalDocumentCloseAttempt attempt, List<Exception> failures)
    {
#if !ANDROID
        var desktop = attempt.Desktop ?? throw new InvalidOperationException("The actual document desktop is unavailable.");
        if (_originalAppWork.IsRetiring || _actualShutdownDelivery is not null) return false;
        var originalCohort = attempt.Windows;

        var sequencer = new FirstMainCloseStageSequencer<Avalonia.Controls.Window>(
            cohortAuthorize: () => OwnedChildRetirementCloseReplanner.TryRefreshCohortForWitnessedRetirement(
                originalCohort,
                desktop.Windows,
                window => IsWitnessedRetiredOwnedAccountChild(_actualNativeCakeAccountOwner, window, originalCohort)),
            applyCohort: refreshed => attempt.Windows = refreshed,
            canonicalCurrent: () => IsOriginalDocumentCloseAttemptCoreCurrent(attempt),
            isChildCloseSettled: () => WitnessedChildCloseSettled(originalCohort, desktop.Windows),
            freshCanonicalPreflight: () => ReprepareCurrentDocumentsAndShellsAsync(attempt, desktop, failures),
            resolveUnifiedLease: () =>
            {
                // An ALREADY-accepted unified close is never re-invoked, aborted, or undone here: retain/
                // join it as-is so exactly-once completion holds.
                return Task.FromResult(true);
            },
            isFullCurrent: () => IsOriginalDocumentCloseAttemptCurrent(attempt),
            completeShutdown: () => DeliverPostAcceptShutdownOnce(attempt));

        return await sequencer.RunAsync();
#else
        return false;
#endif
    }

    /// <summary>Unified-lease leg shared by the pre- and post-BeginClose sequencer gates. For an OLD
    /// UNACCEPTED lease it joins/aborts it and reacquires the CURRENT preparation exactly once, retaining
    /// every raw fault sibling. An ALREADY-accepted unified close is never re-invoked, aborted, or undone:
    /// it is retained (and joined by the shutdown sequence) exactly once, so completion remains exactly-once.</summary>
    private async Task<bool> ResolveReplanUnifiedLeaseAsync(OriginalDocumentCloseAttempt attempt, List<Exception> failures)
    {
#if !ANDROID
        if (attempt.UnifiedLease is { IsAcceptedOriginal: false } stale && attempt.Unified is { } unified)
        {
            Task<UnifiedWorkspaceHost.ClosePreparation?> reprepared =
                Task.FromResult<UnifiedWorkspaceHost.ClosePreparation?>(null);
            var replanned = await OwnedChildRetirementCloseReplanner.ReplanAsync(
                stale,
                () => attempt.UnifiedAbort ??= stale.AbortAsync(),
                () => { var t = unified.PrepareCloseAsync(CancellationToken.None); reprepared = t; return t; },
                failures);
            if (replanned is null) return false; // declined / genuine fault retained; request ends
            attempt.UnifiedAbort = null;
            attempt.UnifiedPreparation = reprepared;
            attempt.UnifiedLease = replanned;
        }
        return true;
#else
        // Android has no desktop unified-lease/preparation members; the replan path is unused there.
        await Task.CompletedTask;
        return true;
#endif
    }

    /// <summary>Verifies that every originally-owned member now absent from the live cohort had its native
    /// child close settle cleanly (task completed successfully + native Closed fired). If any removal did
    /// not settle this way the child gate refuses, ending the request.</summary>
    private static bool WitnessedChildCloseSettled(IReadOnlyList<Avalonia.Controls.Window> originalCohort,
        IReadOnlyList<Avalonia.Controls.Window> currentCohort)
    {
#if !ANDROID
        foreach (var window in originalCohort)
            if (!ReferencesAny(currentCohort, window))
            {
                if (window is not NativeCakeAccountWindow
                    {
                        OriginalClose.IsCompletedSuccessfully: true,
                        HasNativelyClosed: true,
                    })
                    return false;
            }
        return true;
#else
        return true;
#endif
    }

    /// <summary>Delivers the ONE approved shutdown exactly once. Returns true ONLY when the real original
    /// shutdown delivery was actually issued for this SAME request; a late currentness refusal or a missing
    /// real <c>OriginalClose</c> returns false so completion is never claimed without admission. Actual
    /// shutdown Task failures/pending remain authoritative and are retained by the caller.</summary>
    private bool DeliverPostAcceptShutdownOnce(OriginalDocumentCloseAttempt attempt)
    {
        TaskCompletionSource? shutdownStart = null;
        (_synchronousAppSources ??= []).Add(this);
        try
        {
            if (!IsOriginalDocumentCloseAttemptCurrent(attempt)) return false;
#if !ANDROID
            if (attempt.Unified is { OriginalClose: null }) return false;
#endif
            shutdownStart = AcquireOriginalDesktopShutdownDelivery();
        }
        finally { _synchronousAppSources.RemoveAt(_synchronousAppSources.Count - 1); }
        if (shutdownStart is null) return false;
        shutdownStart.TrySetResult();
        return true;
    }

    /// <summary>The exact production witness used by the App close wiring: a native Account child counts as
    /// positively witnessed retired only when it is owned by the actual parent owner captured for this App,
    /// its own close request drained cleanly (completed-successfully), its native window actually fired the
    /// <see cref="Avalonia.Controls.Window.Closed"/> callback, AND its real native parent belonged to the
    /// SAME initial close cohort / current request. A parentless, wrong-parent, detached, other-owner, or
    /// canceled/failed window never authorizes a replan. <see cref="NativeCakeAccountWindow.IsRetired"/>
    /// alone is insufficient: it flips before the native close returns and its close task completes.</summary>
#if !ANDROID
    internal static bool IsWitnessedRetiredOwnedAccountChild(NativeCakeAccountUiOwner? owner,
        Avalonia.Controls.Window window, IReadOnlyList<Avalonia.Controls.Window> originalCohort) =>
        window is NativeCakeAccountWindow
        {
            IsRetired: true,
            OriginalClose.IsCompletedSuccessfully: true,
            HasNativelyClosed: true,
        } account && ReferenceEquals(account.UiOwner, owner)
        && account.UiParent is { } parent && ReferencesAny(originalCohort, parent);
#else
    internal static bool IsWitnessedRetiredOwnedAccountChild(object? owner,
        Avalonia.Controls.Window window, IReadOnlyList<Avalonia.Controls.Window> originalCohort) => false;
#endif

    private static bool ReferencesAny(IReadOnlyList<Avalonia.Controls.Window> cohort, Avalonia.Controls.Window target)
    {
        foreach (var member in cohort) if (ReferenceEquals(member, target)) return true;
        return false;
    }

    /// <summary>Production stage sequencer shared by the App's real first/final close gates and the portable
    /// tests. It owns the ordered close-lifecycle stages for a positively witnessed owned-child retirement:
    /// (1) cohort authorization — prove the ONLY delta is the witnessed retirement of originally-owned children;
    /// (2) apply the refreshed cohort; (3) verify every non-window canonical currentness invariant still holds;
    /// (4) child-close gate — each witnessed child's close task settled cleanly and its native Closed fired;
    /// (5) fresh canonical preflight — re-prepare the REAL current documents/shells with actual authority;
    /// (6) unified-lease leg — resolve the current lease (for an unaccepted lease: join/abort the old and
    /// reacquire the current one; for an already-accepted lease: retain/join it and NEVER abort/rebegin);
    /// (7) complete-once — terminate the ONE approved close exactly once. Stage inputs are injected via
    /// callbacks so focused portable tests drive the SAME production sequencing with controlled gate values
    /// while production passes the real Avalonia Windows, ClosePreparation leases, and shutdown delivery.
    /// no BeginClose here: the App fires it once in the unified block, and accepted-retention is proven by
    /// <see cref="FirstMainCloseStageSequencer{T}"/> never aborting or re-beginning an accepted lease.</summary>
    internal sealed class FirstMainCloseStageSequencer<T> where T : class
    {
        private readonly Func<T[]?> _cohortAuthorize;
        private readonly Action<T[]> _applyCohort;
        private readonly Func<bool> _canonicalCurrent;
        private readonly Func<bool> _isChildCloseSettled;
        private readonly Func<Task<bool>> _freshCanonicalPreflight;
        private readonly Func<Task<bool>> _resolveUnifiedLease;
        private readonly Func<bool> _isFullCurrent;
        private readonly Func<bool> _completeShutdown;

        internal FirstMainCloseStageSequencer(
            Func<T[]?> cohortAuthorize,
            Action<T[]> applyCohort,
            Func<bool> canonicalCurrent,
            Func<bool> isChildCloseSettled,
            Func<Task<bool>> freshCanonicalPreflight,
            Func<Task<bool>> resolveUnifiedLease,
            Func<bool> isFullCurrent,
            Func<bool> completeShutdown)
        {
            _cohortAuthorize = cohortAuthorize;
            _applyCohort = applyCohort;
            _canonicalCurrent = canonicalCurrent;
            _isChildCloseSettled = isChildCloseSettled;
            _freshCanonicalPreflight = freshCanonicalPreflight;
            _resolveUnifiedLease = resolveUnifiedLease;
            _isFullCurrent = isFullCurrent;
            _completeShutdown = completeShutdown;
        }

        /// <summary>Number of times the terminal complete-once stage positively resolved (real production admission /
        /// delivery issued for this SAME request). A refused/late currentness or missing-OriginalClose completion
        /// never increments this — it increments <see cref="EndCount"/> instead.</summary>
        public int CompleteCount { get; private set; }
        /// <summary>Number of times a stage refused and the run ended before completion.</summary>
        public int EndCount { get; private set; }
        /// <summary>Number of times the child-close gate was evaluated.</summary>
        public int ChildGateCount { get; private set; }
        /// <summary>Number of times the fresh canonical preflight stage was invoked.</summary>
        public int FreshPreflightCount { get; private set; }
        /// <summary>Number of times the unified-lease leg was resolved.</summary>
        public int LeaseLegCount { get; private set; }

        /// <summary>Runs the production stage sequence in order. Returns true only when every stage passed and the
        /// terminal complete-once stage positively resolved; otherwise false (each refusal ends counts toward
        /// <see cref="EndCount"/>). FULL canonical currentness/cohort is re-checked after EACH awaited stage
        /// (fresh preflight and unified-lease leg) and immediately before the terminal completion, so an unrelated
        /// window addition/removal/reorder/doc edit that lands while a pending operation runs ends the request
        /// before any destructive BeginClose/retirement or delivered shutdown.</summary>
        public async Task<bool> RunAsync()
        {
            var refreshed = _cohortAuthorize();
            if (refreshed is null) { EndCount++; return false; }
            _applyCohort(refreshed);
            if (!_canonicalCurrent()) { EndCount++; return false; }
            ChildGateCount++;
            if (!_isChildCloseSettled()) { EndCount++; return false; }
            FreshPreflightCount++;
            if (!await _freshCanonicalPreflight()) { EndCount++; return false; }
            // Re-check FULL canonical currentness/cohort after the awaited preflight: an unrelated window
            // addition/removal/reorder/doc edit during it must END the request before any lease reacquire.
            if (!_isFullCurrent()) { EndCount++; return false; }
            LeaseLegCount++;
            if (!await _resolveUnifiedLease()) { EndCount++; return false; }
            // Re-check FULL currentness after the awaited lease leg too, immediately before completion.
            if (!_isFullCurrent()) { EndCount++; return false; }
            // Terminal complete-once: true/CompleteCount only when the REAL production admission/delivery was
            // issued for this SAME request. A late currentness refusal or missing real OriginalClose returns
            // false and counts as an end, never as a completion.
            if (!_completeShutdown()) { EndCount++; return false; }
            CompleteCount++;
            return true;
        }
    }

    /// <summary>Production sequencer actually called by the App close wiring for a positively witnessed
    /// owned-native-Account-child retirement. It owns (a) the pure cohort authorization that decides whether
    /// the ONLY delta authorizes a refresh, and (b) the ordered join/abort-old-then-reacquire flow with raw
    /// fault-sibling retention. Generic over the cohort/lease element types so focused portable tests drive
    /// the real operations with controlled gate values while production passes the real Avalonia Windows and
    /// ClosePreparation leases.</summary>
    internal static class OwnedChildRetirementCloseReplanner
    {
        /// <summary>Authorizes refreshing to the real current cohort when the ONLY window-cohort delta is the
        /// positively witnessed retirement of owned children that were already in the original cohort. Returns
        /// the refreshed cohort when authorized; otherwise null. A new/unowned/document window or any other
        /// change keeps its real authority and is never dropped.</summary>
        internal static T[]? TryRefreshCohortForWitnessedRetirement<T>(
            IReadOnlyList<T> originalCohort,
            IReadOnlyList<T> currentCohort,
            Func<T, bool> isWitnessedRetiredOwnedChild)
            where T : class
        {
            // The SOLE authorized delta is the removal of specifically witnessed originally-owned children.
            // Every survivor must remain EXACTLY the same reference in the SAME relative order it had in
            // the original cohort (current must be a strict subsequence of original). Any addition, an
            // unrelated removal, a survivor reorder, or a detached/unowned/document window keeps its real
            // authority and refuses the refresh.
            int removed = 0;
            int originalIndex = 0;
            foreach (var window in currentCohort)
            {
                bool found = false;
                while (originalIndex < originalCohort.Count)
                {
                    var originalMember = originalCohort[originalIndex++];
                    if (ReferenceEquals(originalMember, window)) { found = true; break; }
                    // This original member is now absent from current: it must be a witnessed-retired owned child.
                    if (!isWitnessedRetiredOwnedChild(originalMember)) return null;
                    removed++;
                }
                if (!found) return null; // a live member was not an original member (addition) -> refuse
            }
            // Any original members remaining after the live cohort are the removed set; each must be
            // witnessed-retired. A detached/unowned/unrelated removal refuses the refresh.
            while (originalIndex < originalCohort.Count)
            {
                if (!isWitnessedRetiredOwnedChild(originalCohort[originalIndex++])) return null;
                removed++;
            }
            if (removed == 0) return null; // no cohort delta: nothing changed to refresh
            return currentCohort.ToArray();
        }

        /// <summary>Joins/aborts the OLD unaccepted lease and then reacquires the CURRENT preparation, each
        /// exactly once, with every raw fault/cancellation sibling retained in <paramref name="failures"/>
        /// by the SAME production custody policy as the main App wiring. Returns the reacquired lease on
        /// clean success; on any abort/acquire fault or a declined/no-prep it returns null (the caller ends
        /// the request without retrying).</summary>
        internal static async Task<T?> ReplanAsync<T>(T staleLease, Func<Task> startAbort,
            Func<Task<T?>> startAcquire, List<Exception> failures)
            where T : class
        {
            // Abort leg: capture the RAW actual task OUTSIDE the await/try so its complete terminal state
            // stays inspectable; synchronous acquisition exceptions are genuine too. No normalization here
            // and never a fake completed task.
            Task abortTask;
            try { abortTask = startAbort(); }
            catch (Exception sync) { AddAppCause(failures, sync); return null; }
            await JoinOwnedChildObservationAsync(abortTask, failures);
            if (failures.Count > 0) return null; // genuine abort fault(s) retained; never reacquire/retry.

            // Reacquire leg: same raw-task capture/join; extract the result only on clean success.
            Task<T?> acquireTask;
            try { acquireTask = startAcquire(); }
            catch (Exception sync) { AddAppCause(failures, sync); return null; }
            await JoinOwnedChildObservationAsync(acquireTask!, failures);
            if (failures.Count > 0) return null; // genuine prep fault(s) retained; no commit.
            return acquireTask!.GetAwaiter().GetResult();
        }

        /// <summary>Localized recursive-leaf observation for THIS replanner's abort/reacquire legs only. It
        /// captures/joins the SAME raw tasks the main App wiring joins, but recursively projects every original
        /// leaf reference of a faulted AggregateException (including nested/sibling OCE / cancellation) with no
        /// type/token normalization. The shared <see cref="App.JoinOriginalAppTaskAsync"/> intentionally retains
        /// its non-flattening owner envelope policy; this localized projection is what preserves the nested/sibling
        /// leaf assertions without disturbing that shared contract.</summary>
        private static async Task JoinOwnedChildObservationAsync(Task actual, List<Exception> failures)
        {
            try { await actual; }
            catch (Exception observed)
            {
                if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                    foreach (var original in group.InnerExceptions) AddRecursiveLeafAppCause(failures, original);
                else AddRecursiveLeafAppCause(failures, observed);
            }
        }

        private static void AddRecursiveLeafAppCause(List<Exception> failures, Exception error)
        {
            if (error is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
            {
                foreach (var inner in aggregate.InnerExceptions) AddRecursiveLeafAppCause(failures, inner);
            }
            else if (!failures.Any(original => ReferenceEquals(original, error))) failures.Add(error);
        }
    }

    /// <summary>Extracted production-path sequencer used by both the App wiring and the portable tests. It
    /// owns the ordering (join/abort the old unaccepted lease BEFORE reacquiring a current one) and the
    /// raw-fault retention rule (the SAME actual task is captured outside the await/try and fully joined, so
    /// EVERY AggregateException.InnerExceptions reference is preserved; synchronous acquisition faults are
    /// genuine too). A genuine abort/reprepare fault is retained and never auto-repeated; a declined/no-prep
    /// ends the request. It performs NO waiting on startup/Welcome and no detached fire-and-forget — the
    /// caller owns and awaits its dispatch. Generic over the lease type so tests drive it with controlled
    /// gate values while production passes the real ClosePreparation leases.</summary>
    internal static class StartupTransitionCloseReplanner
    {
        internal static async Task<T?> ReplanAsync<T>(T staleLease, Func<Task> startAbort,
            Func<Task<T?>> startAcquire, List<Exception> failures)
            where T : class
        {
            // Abort leg: capture the RAW actual task OUTSIDE the await/try so its complete terminal state
            // stays inspectable; synchronous acquisition exceptions are genuine too. No normalization here
            // and never a fake completed task.
            Task abortTask;
            try { abortTask = startAbort(); }
            catch (Exception sync) { Add(failures, sync); return null; }
            await JoinRawFaultsAsync(abortTask, failures);
            if (failures.Count > 0) return null; // genuine abort fault(s) retained; never reacquire/retry.

            // Reacquire leg: same raw-task capture/join; extract the result only on clean success.
            Task<T?> acquireTask;
            try { acquireTask = startAcquire(); }
            catch (Exception sync) { Add(failures, sync); return null; }
            await JoinRawFaultsAsync(acquireTask!, failures);
            if (failures.Count > 0) return null; // genuine prep fault(s) retained; no commit.
            return acquireTask!.GetAwaiter().GetResult();
        }

        private static async Task JoinRawFaultsAsync(Task actual, List<Exception> failures)
        {
            try { await actual; }
            catch (Exception error)
            {
                if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                    foreach (var inner in group.InnerExceptions) Add(failures, inner);
                else Add(failures, error);
            }
        }

        internal static void Add(List<Exception> failures, Exception error)
        { if (!failures.Any(item => ReferenceEquals(item, error))) failures.Add(error); }
    }

    private TaskCompletionSource? AcquireOriginalDesktopShutdownDelivery()
    {
        if (_actualShutdownDelivery is not null) return null;
        var start = new TaskCompletionSource();
        _actualShutdownDelivery = DeliverOriginalDesktopShutdownAsync(start.Task);
        return start; // The SAME delivery exists; caller releases its gate outside physical sources.
    }
    private async Task DeliverOriginalDesktopShutdownAsync(Task start)
    {
        await start;
        try
        {
            var sequence = _actualShutdownSequence ?? throw new InvalidOperationException("The actual native shutdown source was not configured.");
            var actualSequence = sequence.CloseAndDrainAsync();
            var sequenceFailures = new List<Exception>();
            await JoinOriginalAppTaskAsync(actualSequence, sequenceFailures);
            ThrowAppCauses(sequenceFailures);
            var desktop = _actualDesktop ?? throw new InvalidOperationException("The actual native desktop owner is unavailable.");
            // Actual native Closed callbacks settled BEFORE provider disposal and
            // final clean. OnExplicitShutdown kept the real dispatcher alive.
            if (_actualWindowClosure is not { IsCompletedSuccessfully: true })
                throw new InvalidOperationException("The actual native window cohort has not supplied its terminal close witness.");
            var actualExit = Dispatcher.UIThread.InvokeAsync(() => desktop.TryShutdown()).GetTask();
            var exitFailures = new List<Exception>();
            await JoinOriginalAppTaskAsync(actualExit, exitFailures);
            ThrowAppCauses(exitFailures);
            var accepted = actualExit.GetAwaiter().GetResult();
            if (!accepted) throw new InvalidOperationException("The actual desktop refused its original native shutdown request.");
        }
        catch (Exception error)
        {
            _actualShutdownFailure = error;
            ReportOriginalShutdownFailure(error);
            ShowOriginalShutdownRecovery(error);
            if (error is OperationCanceledException)
                throw new AggregateException("The actual native shutdown retains its uncertain cancellation cause.", error);
            throw; // SAME failed original remains inspectable, never retried or described as no effect.
        }
    }
    // A retained shutdown failure has to be diagnosable outside a debugger: Debug.WriteLine never
    // reaches an apphost log, so the SAME retained original is also written to the process error
    // stream. This adds no recovery authority, retries nothing and never relabels the failure.
    private void ReportOriginalShutdownFailure(Exception failure)
    {
        System.Diagnostics.Debug.WriteLine("[9to1 original shutdown] " + failure);
        try { Console.Error.WriteLine("[9to1 original shutdown] " + failure); }
        catch (Exception diagnosticFailure) { System.Diagnostics.Debug.WriteLine("[9to1 original shutdown diagnostic] " + diagnosticFailure); }
    }

    /// <summary>
    /// Presents a CUI-authored recovery state for an unresolved close obligation instead of
    /// leaving blank disabled chrome. This does not retry, relabel or manufacture a clean
    /// receipt; it only exposes the retained failure and a truthful recovery affordance.
    /// </summary>
    private void ShowOriginalShutdownRecovery(Exception failure)
    {
        try
        {
            if (_actualDesktop is not { } desktop) return;
            var window = desktop.MainWindow ?? desktop.Windows.OfType<MainWindow>().FirstOrDefault();
            if (window is null) return;
            var loader = new CakeOS.Cui.Runtime.CuiControlLoader();
            var status = new CakeOS.Cui.Runtime.CuiViewModel();
            status.Set("RecoveryMessage", "9to1 could not finish closing: " + failure.Message);
            loader.SetBindingContext(status);
            var document = new CakeOS.Cui.Language.CuiRichParser().Parse(
                "<Cui><Container Type=\"Vertical Stack\" Padding=\"24\" CellGap=\"12\">" +
                "<Text Text=\"Shutdown did not complete\" FontWeight=\"Bold\" FontSize=\"18\" />" +
                "<Text Text=\"{Binding RecoveryMessage}\" TextWrapping=\"Wrap\" />" +
                "<Text Text=\"The window remains open so your work and data are preserved. Review the reported failure, then close again or restart 9to1.\" TextWrapping=\"Wrap\" />" +
                "</Container></Cui>");
            var loaded = loader.TryLoad(document);
            if (loaded.Root is null) { loader.Dispose(); return; }
            loader.WireBindings(loaded.Root);
            Dispatcher.UIThread.Invoke(() => { window.Content = loaded.Root; });
        }
        catch (Exception diagnosticFailure)
        {
            System.Diagnostics.Debug.WriteLine("[9to1 original shutdown recovery] " + diagnosticFailure);
            try { Console.Error.WriteLine("[9to1 original shutdown recovery] " + diagnosticFailure); }
            catch { }
        }
    }
    private void DemandOriginalDesktopBorrowerJoins(IReadOnlyList<object> actualOwners)
    {
        // Pure whole-cohort preflight before ANY stop/close acquisition. A live
        // child's encompassing self-join cannot partially retire its siblings.
        var preflightFailures = new List<Exception>();
#if !ANDROID
        try { DemandOriginalWindowsDeveloperRetirementJoin(); }
        catch (Exception error) { AddAppCause(preflightFailures, error); }
        try { DemandOriginalWindowsHomeRetirementJoin(); }
        catch (Exception error) { AddAppCause(preflightFailures, error); }
        try { DemandOriginalAssistantRetirementJoin(); }
        catch (Exception error) { AddAppCause(preflightFailures, error); }
#endif
        foreach (var owner in actualOwners)
            if (owner is IDesktopOriginalRetirementJoinGuard guard)
                try { guard.DemandExternalOriginalRetirementJoin(); }
                catch (Exception error) { AddAppCause(preflightFailures, error); }
        ThrowAppCauses(preflightFailures);
    }

    private async Task JoinOriginalDesktopBorrowersAsync(IReadOnlyList<object> actualBorrowers,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var failures = new List<Exception>();
        var actualOwners = actualBorrowers.Concat(desktop.Windows.OfType<MainWindow>())
            .Distinct(ReferenceEqualityComparer.Instance).ToArray();
        DemandOriginalDesktopBorrowerJoins(actualOwners);
        foreach (var owner in actualOwners)
        {
            if (owner is not IDesktopOriginalRetirementParticipant participant || owner is not IDesktopOriginalRetirementJoinGuard)
            { failures.Add(new DesktopOriginalRetirementUnavailableException(owner.GetType())); continue; }
            try { participant.RequestRetirement(); } catch (Exception error) { AddAppCause(failures, error); }
        }
#if !ANDROID
        RequestOriginalWindowsDeveloperRetirement(failures);
        RequestOriginalAssistantScopeRetirement(failures);
#endif
        var actualTasks = new List<Task>();
        foreach (var owner in actualOwners)
            if (owner is IDesktopOriginalRetirementParticipant participant && owner is IDesktopOriginalRetirementJoinGuard guard)
                try { guard.DemandExternalOriginalRetirementJoin(); actualTasks.Add(participant.CloseAndDrainAsync()); }
                catch (Exception error) { AddAppCause(failures, error); }
        foreach (var actual in actualTasks.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinOriginalAppTaskAsync(actual, failures);
#if !ANDROID
        // Independently join actual late/unmounted page/controller/source siblings
        // even if another borrower failed. None of their failures permits Den/Home closure.
        try { await JoinOriginalAppTaskAsync(JoinOriginalAssistantScopesAfterPresentationsAsync(), failures); }
        catch (Exception error) { AddAppCause(failures, error); }
#endif
        ThrowAppCauses(failures); // Unknown borrowers still own/borrow the live diagnostics sink.
        // Originals admitted before the source seal may finish creating a native
        // window. Capture and join the genuine final cohort after those producers.
        var actualFinalWindows = desktop.Windows.OfType<MainWindow>().ToArray();
        foreach (var window in actualFinalWindows) window.DemandExternalOriginalRetirementJoin();
        foreach (var window in actualFinalWindows) window.RequestRetirement();
        var actualWindowResources = new List<Task>();
        foreach (var window in actualFinalWindows)
            try { actualWindowResources.Add(window.CloseAndDrainAsync()); }
            catch (Exception error) { AddAppCause(failures, error); }
        foreach (var actual in actualWindowResources.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinOriginalAppTaskAsync(actual, failures);
        ThrowAppCauses(failures);
#if !ANDROID
        try { await JoinOriginalAppTaskAsync(JoinOriginalWindowsDeveloperBorrowersAsync(), failures); }
        catch (Exception error) { AddAppCause(failures, error); }
        ThrowAppCauses(failures);
#endif
        // Closed callbacks are original work too. Retain them while diagnostics,
        // provider and dispatcher still exist; no final marker precedes this phase.
        var closeStart = new TaskCompletionSource();
        _actualWindowClosure = CloseOriginalNativeWindowsAsync(desktop, closeStart.Task);
        closeStart.SetResult();
        await JoinOriginalAppTaskAsync(_actualWindowClosure, failures);
        ThrowAppCauses(failures);
#if !ANDROID
        // Actual ordinary Chat/canonical business, scoped presentation originals
        // and native Closed callbacks settled. Den is retired before its Home dependency.
        try { await JoinOriginalAppTaskAsync(JoinOriginalAssistantPersonalDenAfterBorrowersAsync(), failures); }
        catch (Exception error) { AddAppCause(failures, error); }
        ThrowAppCauses(failures);
        // Only now may the SAME Home owner dispose its actual permission runtime.
        try { await JoinOriginalAppTaskAsync(JoinOriginalWindowsHomeAfterBorrowersAsync(), failures); }
        catch (Exception error) { AddAppCause(failures, error); }
        ThrowAppCauses(failures);
#endif
        // Concrete diagnostics source custody02 must be selected and verified by
        // Root. A generic IDisposable/boolean cannot certify another sink's writers.
        if (_productionDiagnostics is ProductionDiagnostics diagnostics)
            try { await JoinOriginalAppTaskAsync(diagnostics.DisposeAsync().AsTask(), failures); }
            catch (Exception error) { AddAppCause(failures, error); }
        else if (_productionDiagnostics is not null)
            failures.Add(new DesktopOriginalRetirementUnavailableException(_productionDiagnostics.GetType()));
        ThrowAppCauses(failures); // Unknown child keeps UI/provider alive; no early clean.
    }
    private static async Task CloseOriginalNativeWindowsAsync(IClassicDesktopStyleApplicationLifetime desktop, Task start)
    {
        await start;
        var failures = new List<Exception>();
        var actualNativeCohort = desktop.Windows.ToArray();
        // Invoke's SAME callback Task captures synchronous Closing/Closed faults.
        // Successful invocation is not closure proof: inspect actual Closed owners
        // and the genuine desktop cohort afterward, without arbitrary polling.
        var actualClose = Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var native in actualNativeCohort)
                try { native.Close(); }
                catch (Exception error) { AddAppCause(failures, error); }
        }).GetTask();
        await JoinOriginalAppTaskAsync(actualClose, failures);
        foreach (var actual in actualNativeCohort.OfType<MainWindow>())
        {
            if (!actual.OriginalNativeClosedCallback.IsCompleted)
                AddAppCause(failures, new InvalidOperationException("An actual managed native window did not supply its Closed callback settlement."));
            else await JoinOriginalAppTaskAsync(actual.OriginalNativeClosedCallback, failures);
        }
        if (desktop.Windows.Count > 0)
            AddAppCause(failures, new InvalidOperationException("Actual native windows remain after the original close request; callback success is not closure proof."));
        ThrowAppCauses(failures);
    }
    private static async Task JoinOriginalAppTaskAsync(Task actual, List<Exception> failures)
    {
        try { await actual; }
        catch (Exception observed)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                foreach (var original in group.InnerExceptions) AddAppCause(failures, original);
            else AddAppCause(failures, observed);
        }
    }
    private static void AddAppCause(List<Exception> failures, Exception error)
    { if (!failures.Any(original => ReferenceEquals(original, error))) failures.Add(error); }
    private static void ThrowAppCauses(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Actual native App borrower ownership remains unresolved.", failures);
    }
}
