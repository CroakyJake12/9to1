using System.Runtime.ExceptionServices;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
#if !ANDROID
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
        await JoinOriginalAppTaskAsync(_originalAppWork.CloseAndDrainAsync(), failures);
        ThrowAppCauses(failures);
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
            () => actualRecovery.PrepareFinalCleanWriterAsync(CancellationToken.None),
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
        window.Closing += OnOriginalPrimaryWindowClosing;
        desktop.ShutdownRequested += OnOriginalDesktopShutdownRequested;
        window.Closed += OnOriginalUnexpectedPrimaryWindowClosed;
    }
    private void OnOriginalPrimaryWindowClosing(object? sender, Avalonia.Controls.WindowClosingEventArgs args)
    {
        if (_actualWindowClosure is not null) return; // Privately issued native-close original AFTER resource owners joined, never a clean-authority boolean.
        args.Cancel = true;
        RequestOriginalDocumentClosePreflight();
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
                foreach (var candidate in attempt.Candidates)
                    if (!await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                        () => candidate.PrepareAsync(CancellationToken.None)))) return;
#if !ANDROID
                attempt.Unified = _actualUnifiedWorkspace;
                if (attempt.Unified is { } unified)
                {
                    attempt.UnifiedPreparation = AcquireOriginalAppSynchronous(original,
                        () => unified.PrepareCloseAsync(CancellationToken.None));
                    try { await original.AwaitAsync(attempt.UnifiedPreparation); }
                    finally
                    {
                        // AwaitAsync may reject publication AFTER the actual child
                        // yielded a lease. Recover that SAME result for external abort.
                        if (attempt.UnifiedPreparation.IsCompletedSuccessfully)
                            attempt.UnifiedLease = attempt.UnifiedPreparation.GetAwaiter().GetResult();
                    }
                    if (attempt.UnifiedLease is null) return;
                }
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
            if (!IsOriginalDocumentCloseAttemptCurrent(attempt)) return;
#if !ANDROID
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
            TaskCompletionSource? shutdownStart = null;
            (_synchronousAppSources ??= []).Add(this);
            try
            {
                if (!IsOriginalDocumentCloseAttemptCurrent(attempt)) return;
#if !ANDROID
                if (attempt.Unified is { OriginalClose: null }) return;
#endif
                shutdownStart = AcquireOriginalDesktopShutdownDelivery();
            }
            finally { _synchronousAppSources.RemoveAt(_synchronousAppSources.Count - 1); }
            // No borrowed page/provider work follows this physical callback release.
            shutdownStart?.TrySetResult();
        }
    }
    private bool IsOriginalDocumentCloseAttemptCurrent(OriginalDocumentCloseAttempt attempt)
    {
        var desktop = attempt.Desktop ?? throw new InvalidOperationException("The actual document desktop is unavailable.");
        if (_originalAppWork.IsRetiring || !ReferenceEquals(desktop, _actualDesktop) ||
            !attempt.Windows.SequenceEqual(desktop.Windows, ReferenceEqualityComparer.Instance) ||
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
