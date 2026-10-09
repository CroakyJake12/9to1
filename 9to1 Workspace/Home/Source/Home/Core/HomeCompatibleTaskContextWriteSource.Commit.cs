using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeCompatibleTaskContextWriteSource
{
    private sealed partial class Claim
    {
        internal Task AcquireEntry(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            Task actual; Context context; TaskCompletionSource begin;
            lock (owner._gate) lock (_gate)
            {
                if (_entry is not null) return _entry;
                if (owner._retiring || _retired || !IsReady || _settlement is not null || _operations.Any(raw => !raw.IsCompletedSuccessfully))
                    throw new UnauthorizedAccessException("The actual individually accepted WRITE must be current before held entry acquisition.");
                context = new(owner, this, scope, retain); begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual = Drive(begin.Task, context, async sources =>
                {
                    // The producer has acquired its actual store/project native pins. Do not
                    // call ordinary store/profile/READ APIs here or while Home is held.
                    sources.Invoke(() => { owner._creator.DemandOriginalCreationCommit(intent); return true; });
                    await sources.Capture(() => owner._store.AcquireLocalOperationLeaseCoreAsync(owner._profiles,
                        Actor!, new HeldGuard(this), sources.Scope, sources.Retain, token), value => _home = value).ConfigureAwait(false);
                    if (_home is not IHomeOriginalScopedLocalOperationLease held)
                        throw new UnauthorizedAccessException("The genuine held Home WRITE entry was unavailable.");
                    if (!await sources.Read(() => held.IsCurrentAsync(sources.Scope, sources.Retain, token).AsTask()).ConfigureAwait(false))
                        throw new UnauthorizedAccessException("The held actor/receipt/WRITE changed before SQL admission.");
                    sources.Invoke(() => { DemandCommitCore(requireTerminalEntry: false); return true; }); return true;
                });
                _entry = actual; _operations.Add(actual); _contexts.Add(context);
            }
            try { context.Publish(actual); } catch (Exception cause) { context.Errors.Retain(cause); }
            finally { begin.SetResult(); } return actual;
        }
        internal Task ValidateEntry(Action<Action> scope, Action<Task> retain, CancellationToken token) => Run(scope, retain, async sources =>
        {
            DemandCommit();
            if (_home is not IHomeOriginalScopedLocalOperationLease held ||
                !await sources.Read(() => held.IsCurrentAsync(sources.Scope, sources.Retain, token).AsTask()).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual held Home WRITE entry is no longer current.");
            sources.Invoke(() => { DemandCommit(); return true; }); return true;
        });
        internal void DemandCommit() => DemandCommitCore(requireTerminalEntry: true);
        private void DemandCommitCore(bool requireTerminalEntry)
        {
            DemandLive();
            lock (_gate)
            {
                if (!Acquisition.IsCompletedSuccessfully || (requireTerminalEntry && _entry?.IsCompletedSuccessfully != true) ||
                    _settlement is not null || _home is null || _completion is null || _attestation is null ||
                    _capability?.IsUncompletedClaim(owner._broker) != true)
                    throw new UnauthorizedAccessException("SAME individually accepted, uncompleted and held WRITE is required.");
            }
            if (!owner._reads.IsIssuedOriginalCommandRead(read) || !owner._creator.IsIssuedOriginalCreationIntent(intent))
                throw new UnauthorizedAccessException("The intent-bound LIVE project READ or SQL intent was revoked.");
            owner._creator.DemandOriginalCreationCommit(intent);
        }
        internal void RetainSql(Task<ICanonicalProjectTaskContextCreation> sql)
        {
            ArgumentNullException.ThrowIfNull(sql); DemandCommit();
            if (!owner._creator.IsOriginalCreationCommitTask(intent, sql))
                throw new UnauthorizedAccessException("Only the SAME actual producer's atomic SQL child may be retained.");
            lock (owner._gate) lock (_gate)
            {
                if (owner._retiring || _retired || _settlement is not null)
                    throw new ObjectDisposedException("Original context WRITE");
                if (_sql is not null) throw new UnauthorizedAccessException("This one-use WRITE already retained its atomic SQL child.");
                _sql = sql; // Custody before any child start/callback release.
            }
        }
        internal void RequireSameSql(Task<ICanonicalProjectTaskContextCreation> sql)
        {
            lock (_gate) if (!ReferenceEquals(sql, _sql) || !owner._creator.IsOriginalCreationCommitTask(intent, sql))
                throw new UnauthorizedAccessException("Completion must join the SAME retained raw atomic SQL child.");
        }
        private sealed class HeldGuard(Claim claim) : IHomeOriginalScopedStateCommitActorGuard
        {
            public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
                HomeStateCommitPhase phase, CancellationToken token) => CheckAsync(state, expected, phase, body => body(), _ => { }, token);
            public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
                HomeStateCommitPhase phase, Action<Action> scope, Action<Task> retain, CancellationToken token)
            {
                var sources = new Context(claim.Owner, claim, scope, retain);
                bool valid = false;
                try
                {
                    valid = sources.Invoke(() =>
                    {
                        token.ThrowIfCancellationRequested(); claim.DemandLive();
                        // Actual already-held Home state, actual issuer-bound receipt and
                        // attestation. No read/reacquisition of Home, SQLite or Files.
                        if (expected != claim.Actor || claim._ownership is null || claim._attestation is null ||
                            !HomeLocalStoreOwnership.IsReceiptCurrentInState(state, claim._ownership) ||
                            (claim._denOwnership is not null && !HomeLocalStoreOwnership.IsReceiptCurrentInState(state, claim._denOwnership)) ||
                            !claim.Owner._broker.IsClaimedAttestationCurrentInState(claim._attestation, expected, state) ||
                            !claim.Owner._reads.IsIssuedOriginalCommandRead(claim.Read)) return false;
                        if (claim.Owner._creator is ICanonicalProjectTaskContextOriginalDenRevisionOwner actualDenOwner &&
                            !ReferenceEquals(actualDenOwner.GetOriginalCreationDenOwnership(claim.Intent), claim._denOwnership)) return false;
                        claim.Owner._creator.DemandOriginalCreationCommit(claim.Intent); return true;
                    });
                    if (valid) valid = await sources.Read(() => claim.Owner._profiles.CheckAsync(state, expected, phase,
                        sources.Scope, sources.Retain, token).AsTask()).ConfigureAwait(false);
                }
                catch (Exception cause) { sources.Errors.Retain(cause); }
                await sources.Settle().ConfigureAwait(false); return valid;
            }
        }
    }
}
