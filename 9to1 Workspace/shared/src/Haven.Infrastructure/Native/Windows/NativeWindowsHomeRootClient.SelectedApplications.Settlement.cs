using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure.Native.Windows;

public sealed partial class NativeWindowsHomeRootClient
{
    public Task WaitOriginalSettlementDispatchWithinSourceAsync(ICanonicalInstalledApplicationLaunchIntent actual,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var work = RequireOriginalLaunch(actual); Task original; TaskCompletionSource? begin = null;
        lock (work.Gate)
        {
            if (work.DispatchWait is not null) return work.DispatchWait;
            if (work.Driver is null || work.Driver.IsCompleted) throw new UnauthorizedAccessException("The SAME live launch parent must admit its cleanup child before retirement.");
            work.WaitSources = OriginalLaunchCleanupSources(scope); begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original = work.DispatchWait = Drive(begin.Task);
        }
        try { work.WaitSources.Invoke(() => { retain(original); return true; }); }
        catch (Exception cause) { work.WaitSources.Retain(cause); }
        finally { begin.SetResult(); }
        return original;
        async Task Drive(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var source = work.WaitSources!;
            try { await source.AwaitAsync(work.Dispatch.Task).ConfigureAwait(false); }
            catch (Exception cause) { source.Capture(work.Dispatch.Task, cause); }
            await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (source.OriginalErrors.Count != 0) throw new AggregateException("The original launch dispatch child failed; Home audit remains barred.", source.OriginalErrors);
        }
    }
    public bool IsOwnedOriginalSettlementDispatch(ICanonicalInstalledApplicationLaunchIntent actual, Task same,
        Task<ICanonicalInstalledApplicationLaunchAcknowledgment>? atomic) => _launchIntents.TryGetValue(actual, out var work) &&
        ReferenceEquals(work.DispatchWait, same) && same.IsCompletedSuccessfully && work.Dispatch.Task.IsCompletedSuccessfully &&
        ReferenceEquals(work.Atomic, atomic) && (atomic is null || atomic.IsCompleted) && work.WaitSources is { } sources &&
        sources.OriginalErrors.Count == 0 && sources.OriginalTasks.All(raw => raw.IsCompletedSuccessfully);
    public Task ReleaseOriginalSettlementPinsWithinSourceAsync(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalInstalledApplicationLaunchIntent, ICanonicalInstalledApplicationLaunchAcknowledgment> phase,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        if (_launchHome?.IsIssuedOriginalSettlementReleasePhase(phase) != true)
            throw new UnauthorizedAccessException("Only the SAME actual Home launch release phase can transfer or close Root preparation custody.");
        var work = RequireOriginalLaunch(phase.OriginalIntent); Task original; TaskCompletionSource? begin = null;
        lock (work.Gate)
        {
            if (work.Phase is not null && !ReferenceEquals(work.Phase, phase)) throw new UnauthorizedAccessException("Another phase cannot replay an original launch release.");
            if (work.Release is not null) return work.Release;
            if (work.Driver is null || work.Driver.IsCompleted || !work.Dispatch.Task.IsCompletedSuccessfully ||
                !ReferenceEquals(work.Atomic, phase.OriginalAtomicSqlTask) || (work.Atomic is not null && !work.Atomic.IsCompleted))
                throw new UnauthorizedAccessException("The SAME live launch parent and terminal raw/no-effect dispatch boundary are required.");
            work.Phase = phase; work.ReleaseSources = OriginalLaunchCleanupSources(scope);
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously); original = work.Release = Drive(begin.Task);
        }
        try { work.ReleaseSources.Invoke(() => { retain(original); return true; }); }
        catch (Exception cause) { work.ReleaseSources.Retain(cause); }
        finally { begin.SetResult(); }
        return original;
        async Task Drive(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var source = work.ReleaseSources!;
            try
            {
                await source.AwaitAsync(work.Dispatch.Task).ConfigureAwait(false);
                if (work.PreparationReceipt is not null)
                {
                    var response = await ExchangeOwned(source, new(1, Guid.NewGuid(), "release-selected-application")
                    { SelectedChoice = work.Intent.ToWire() }).ConfigureAwait(false);
                    if (!response.Accepted || response.SelectedChoice != work.Intent.ToWire() ||
                        work.Acknowledgment?.Applied == true && (response.SelectedWitness is null ||
                            response.SelectedWitness.OriginalChoice != work.Intent.ToWire() ||
                            response.SelectedWitness.ProcessId != work.Acknowledgment.ProcessId ||
                            response.SelectedWitness.ProcessStartIdentity != work.Acknowledgment.ProcessStartIdentity ||
                            response.SelectedWitness.ExecutableIdentity != work.Acknowledgment.ExecutableIdentity))
                        throw new UnauthorizedAccessException("The actual Root has not acknowledged this exact preparation close or actual child custody transfer.");
                }
            }
            catch (Exception cause) { source.Retain(cause); }
            await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (source.OriginalErrors.Count != 0) throw new AggregateException("Original Root preparation/pin transfer remains failed or unknown; Home audit is unstarted.", source.OriginalErrors);
            lock (work.Gate) work.Released = true;
        }
    }
    public bool IsOwnedOriginalSettlementPinRelease(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalInstalledApplicationLaunchIntent, ICanonicalInstalledApplicationLaunchAcknowledgment> phase,
        Task same) => _launchHome?.IsIssuedOriginalSettlementReleasePhase(phase) == true &&
        _launchIntents.TryGetValue(phase.OriginalIntent, out var work) && ReferenceEquals(work.Phase, phase) && ReferenceEquals(work.Release, same) &&
        same.IsCompletedSuccessfully && work.Released && work.ReleaseSources is { } sources && sources.OriginalErrors.Count == 0 &&
        sources.OriginalTasks.All(raw => raw.IsCompletedSuccessfully);
    private CloudflareOriginalTaskLedger OriginalLaunchCleanupSources(Action<Action> scope)
    {
        var source = new CloudflareOriginalTaskLedger(); source.BindOriginalOwner(this);
        source.BindOriginalCallerCallback(body => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
            () => { RunRootCallback(source, scope, body); return true; })); return source;
    }
    private Task? RequestOriginalLaunchReviewWithdrawals()
    {
        // Seal new producer parents before capturing the accepted cohort. This
        // independent child waits only for each parent's Home claim publication,
        // never its encompassing driver or pending manual decision.
        TaskCompletionSource? begin = null; Task? original;
        lock (_gate)
        {
            if (_launchWithdrawal is not null) return _launchWithdrawal;
            _launchWithdrawalRequested = true;
            if (_launchHome is null) return null;
            var home = _launchHome; var accepted = _liveLaunches.ToArray();
            _launchWithdrawalSources = OriginalLaunchCleanupSources(body => body());
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original = _launchWithdrawal = Drive(begin.Task, home, accepted);
            _ = _closing.Track(original);
        }
        begin.SetResult(); return original;
        async Task Drive(Task start, HomeCanonicalInstalledApplicationLaunchSource home, LaunchInvocation[] accepted)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var source = _launchWithdrawalSources!;
            foreach (var work in accepted)
            {
                _ = source.Track(work.ReviewPublished.Task);
                try { await source.AwaitAsync(work.ReviewPublished.Task).ConfigureAwait(false); }
                catch (Exception cause) { source.Capture(work.ReviewPublished.Task, cause); }
            }
            Task? actual = null;
            try
            {
                source.Invoke(() =>
                {
                    home.RequestOriginalPendingReviewWithdrawals();
                    actual = home.OriginalPendingReviewWithdrawalTask
                        ?? throw new InvalidOperationException("The actual Home issuer published no pending-review withdrawal receipt.");
                    _ = source.Track(actual); return true;
                });
            }
            catch (Exception cause) { source.Retain(cause); }
            if (actual is not null)
                try { await source.AwaitAsync(actual).ConfigureAwait(false); }
                catch (Exception cause) { source.Capture(actual, cause); }
            await source.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            if (source.OriginalErrors.Count != 0)
                throw new AggregateException("The actual launch review preparation/withdrawal child failed; Root transport remains retained.", source.OriginalErrors);
        }
    }
    /// <summary>Join the actual launch business while Home can still settle its
    /// individual reviews/claims. Root host READ and transport remain live until
    /// their existing whole-Root lifetime closes after Home host retirement.</summary>
    public Task DrainOriginalApplicationLaunchesBeforeHomeCloseAsync()
    {
        DemandExternalOriginalJoin(); RequestOriginalLaunchReviewWithdrawals();
        return DrainOriginalApplicationLaunchesOwned();
    }
    private Task DrainOriginalApplicationLaunchesOwned()
    {
        TaskCompletionSource? begin = null; Task original;
        lock (_gate)
        {
            if (_launchBusinessClose is null)
            {
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                original = _launchBusinessClose = Drive(begin.Task); _ = _closing.Track(original);
            }
            else original = _launchBusinessClose;
        }
        begin?.SetResult(); return original;
        async Task Drive(Task start)
        {
            await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            await JoinOriginalLaunchesBeforeRootTransportClose().ConfigureAwait(false);
            // This cached child belongs to _closing; never observe that entire
            // ledger from within itself. Every actual launch sibling was joined
            // above and remains independently observed again by whole-Root close.
            if (_closing.OriginalErrors.Count != 0)
                throw new AggregateException("Actual launch business settlement failed; retain Home and Root downstream owners.", _closing.OriginalErrors);
        }
    }
    private async Task JoinOriginalLaunchesBeforeRootTransportClose()
    {
        var source = _closing; Task? withdrawal; LaunchInvocation[] all;
        lock (_gate) { withdrawal = _launchWithdrawal; all = _liveLaunches.ToArray(); }
        if (withdrawal is not null) try { await source.AwaitAsync(withdrawal).ConfigureAwait(false); } catch (Exception cause) { source.Capture(withdrawal, cause); }
        foreach (var work in all)
        {
            if (work.Driver is not null) try { await source.AwaitAsync(work.Driver).ConfigureAwait(false); } catch (Exception cause) { source.Capture(work.Driver, cause); }
            await CloseOriginalLaunchClaim(work).ConfigureAwait(false);
            await ObserveOriginalHomeLaunchSources(work, source).ConfigureAwait(false);
            foreach (var cause in work.HomeCleanup.OriginalErrors) source.Retain(cause);
            foreach (var raw in new[] { work.DispatchWait, work.Release })
                if (raw is not null) try { await source.AwaitAsync(raw).ConfigureAwait(false); } catch (Exception cause) { source.Capture(raw, cause); }
        }
    }
}
