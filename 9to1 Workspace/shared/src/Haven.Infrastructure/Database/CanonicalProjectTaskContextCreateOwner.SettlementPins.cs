using System.Runtime.CompilerServices;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class CanonicalProjectTaskContextCreateOwner
{
    private readonly ConditionalWeakTable<
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalProjectTaskContextCreationIntent, ICanonicalProjectTaskContextCreation>,
        SettlementRelease> _settlementReleases = new();
    private sealed class SettlementRelease
    {
        internal Task Driver = null!;
        internal Task? Inner, OriginalPinClose;
        internal Commit? Invocation;
        internal CanonicalSqliteOriginalSourceScope Source = null!;
    }
    public Task ReleaseOriginalSettlementPinsWithinSourceAsync(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalProjectTaskContextCreationIntent, ICanonicalProjectTaskContextCreation> phase,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(phase);
        lock (_gate) if (_settlementReleases.TryGetValue(phase, out var cached)) return cached.Driver;
        // Pure configured-issuer provenance only, under the physical source guard and
        // outside bookkeeping locks. No supplied callbacks or resource acquisitions.
        CommitAttempt? cleanup = null;
        WithinOriginalPhysical(() =>
        {
            var writes = OriginalHomeWriteSource;
            if (writes is null || !writes.IsIssuedOriginalSettlementReleasePhase(phase))
                throw new UnauthorizedAccessException("The actual Home issuer did not issue this original cleanup phase.");
            var intent = RequireIntent(phase.OriginalIntent);
            if (!_commits.TryGetValue(intent, out cleanup))
                throw new UnauthorizedAccessException("No actual original cleanup parent exists.");
        });
        SettlementRelease release; TaskCompletionSource begin;
        lock (_gate)
        {
            if (_settlementReleases.TryGetValue(phase, out var existing)) return existing.Driver;
            var original = ReserveOriginalOwnedCleanup(cleanup!);
            release = new() { Source = new(_store.OriginalSourceOwner, WrapOriginalScope(scope), retain) };
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            release.Driver = Start(begin.Task, phase, release);
            _settlementReleases.Add(phase, release); PublishOriginal(original, release.Driver);
        }
        begin.SetResult(); return release.Driver;

        async Task Start(Task admitted,
            ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalProjectTaskContextCreationIntent, ICanonicalProjectTaskContextCreation> actualPhase,
            SettlementRelease retained)
        {
            await admitted.ConfigureAwait(false);
            var previous = _logical.Value; _logical.Value = previous + 1;
            try
            {
                retained.Inner = _store.RetainOriginalReader(retained.Source, async () =>
                {
                    var source = retained.Source;
                    var invocation = source.Invoke(() =>
                    {
                        var writes = OriginalHomeWriteSource;
                        if (writes is null || !writes.IsIssuedOriginalSettlementReleasePhase(actualPhase))
                            throw new UnauthorizedAccessException("The SAME Home WRITE issuer did not acknowledge this actual release boundary.");
                        var intent = RequireIntent(actualPhase.OriginalIntent);
                        if (!_commits.TryGetValue(intent, out var commit) || !ReferenceEquals(commit.Invocation.Intent, intent) ||
                            !ReferenceEquals(commit.Invocation.Atomic, actualPhase.OriginalAtomicSqlTask) ||
                            (commit.Invocation.Atomic is { } atomic && !atomic.IsCompleted))
                            throw new UnauthorizedAccessException("No SAME privately retained terminal child or actual no-SQL invocation exists.");
                        return commit.Invocation;
                    });
                    retained.Invocation = invocation;
                    // A null Atomic/pin snapshot alone cannot exclude a pending accepted
                    // acquisition. Independently await the original setup/dispatch barrier.
                    await source.Read(() => invocation.DispatchSettled.Task).ConfigureAwait(false);
                    if (!invocation.DispatchSettled.Task.IsCompletedSuccessfully ||
                        !ReferenceEquals(invocation.Atomic, actualPhase.OriginalAtomicSqlTask))
                        throw new UnauthorizedAccessException("The actual producer dispatch acquisition cohort is not settled.");
                    // Only cached original Den exclusion is released. SQLite/project pins
                    // stay owned by the encompassing creation driver. No Home, SQL, Den
                    // definition/session read, approval or mutation takes place here.
                    if (invocation.ResumePin is { } pin)
                    {
                        source.CaptureOriginalResource(pin);
                        await source.CloseAsync(pin).ConfigureAwait(false);
                        retained.OriginalPinClose = source.Invoke(() => pin.OriginalClose);
                        if (retained.OriginalPinClose?.IsCompletedSuccessfully != true)
                            throw new InvalidOperationException("The actual original Den revision pin release is unfinished; Home audit remains blocked.");
                    }
                    await source.JoinAllAsync().ConfigureAwait(false); return 0;
                });
                await retained.Inner.ConfigureAwait(false);
            }
            finally { _logical.Value = previous; }
        }
    }
    public bool IsOwnedOriginalSettlementPinRelease(
        ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalProjectTaskContextCreationIntent, ICanonicalProjectTaskContextCreation> phase,
        Task actualRelease)
    {
        if (!_settlementReleases.TryGetValue(phase, out var retained) ||
            !ReferenceEquals(retained.Driver, actualRelease) || !actualRelease.IsCompletedSuccessfully ||
            retained.Inner?.IsCompletedSuccessfully != true || !retained.Source.IsHealthySettled ||
            retained.Invocation is not { } invocation) return false;
        // No callback/foreign property access in this pure query. The actual source
        // guard and original pin receipt were privately captured in the driver.
        return invocation.ResumePin is null || retained.OriginalPinClose?.IsCompletedSuccessfully == true;
    }
}
