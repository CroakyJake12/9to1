using Haven.Application;
using Haven.Application.Automations;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed partial class HomeCanonicalAutomationDefinitionWriteSource
{
    private sealed partial class Claim
    {
        private bool HasHealthyDeclinedPreEffectSources =>
            ((_declined is { State: HomePreparedReviewState.RequestObserved, Request: { } request } && _review is not null &&
                request.RequestId == _review.RequestId && request.State is HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked) ||
                HasHealthyWithdrawnPreEffectSources) &&
            _capability is null && _attestation is null && _completion is null && _home is null && _entry is null && _sql is null &&
            _acknowledgedRefusal is not null && _acquisitionContext.SuccessfullySettled;
        internal bool IsAcknowledgedAcquisitionRefusal(Task actual) => ReferenceEquals(actual, Acquisition) && actual.IsFaulted &&
            HasHealthyDeclinedPreEffectSources && actual.Exception is { InnerExceptions.Count: 1 } group &&
            ReferenceEquals(group.InnerExceptions[0], _acknowledgedRefusal);
        internal Task SettleWrite(Action<Action> scope, Action<Task> retain)
        {
            Task actual; TaskCompletionSource? begin = null;
            lock (_gate)
            {
                if (_settlement is not null) return _settlement;
                {
                    begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _settlement = SettlePublished(begin.Task);
                }
                actual = _settlement;
            }
            // The caller owns this driver; its own raw-child ledger never tracks itself.
            var publishing = new Context(owner, this, scope, retain, productive: false);
            try { publishing.Publish(actual); }
            catch (Exception cause) { lock (_gate) _publicationErrors.Add(cause); }
            finally { begin?.SetResult(); }
            return actual;
        }
        private readonly List<Exception> _publicationErrors = [];
        private Context? _originalSettlementContext; // Retain actual audit/release raw tasks after failed completion.
        private async Task SettlePublished(Task begin)
        {
            await begin.ConfigureAwait(false);
            using var parent = CloudflareOriginalExecutionGuard.EnterOriginal(owner);
            using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var sources = new Context(owner, this, body => body(), _ => { }, productive: false);
            lock (_gate) _originalSettlementContext = sources;
            Task[] operations; Context[] contexts; Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>? sql;
            lock (_gate) { operations = _operations.ToArray(); contexts = _contexts.ToArray(); sql = _sql; }
            ICanonicalAutomationDefinitionOriginalChangeAcknowledgment? acknowledgment = null;
            if (sql is not null)
                try { acknowledgment = await sources.Await(sql).ConfigureAwait(false); }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            foreach (var operation in operations)
            {
                if (IsAcknowledgedAcquisitionRefusal(operation))
                {
                    // Independently join and preserve the exact failed source occurrence in
                    // Acquisition/_operations. It is never enrolled in a generic error ledger
                    // which would recapture an already source-acknowledged refusal as failure.
                    try { await operation.ConfigureAwait(false); }
                    catch (Exception cause) { if (!IsAcknowledgedAcquisitionRefusal(operation)) sources.Errors.Capture(operation, cause); }
                }
                else try { await sources.Await(operation).ConfigureAwait(false); }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            }
            foreach (var context in contexts)
                try { await context.Settle().ConfigureAwait(false); }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            bool acknowledged = false;
            try { acknowledged = sql?.IsCompletedSuccessfully == true && acknowledgment is not null && sources.Physical(() =>
                owner._creator.IsOriginalAtomicChangeTask(intent, sql) && owner._creator.IsOwnedOriginalChangeAcknowledgment(intent, acknowledgment, sql)); }
            catch (Exception cause) { sources.Errors.Retain(cause); }
            if (sql is not null && !acknowledged)
                sources.Errors.Retain(new InvalidOperationException("The retained actual SQL child did not issue a healthy SAME acknowledgment; preserve its operation/payload for recovery."));
            // Independent original cleanup. Both raw releases are acquired before either
            // await; no failure/hold skips the sibling. Home audit is never under these locks.
            var releases = new List<Task>(); var expectedReleases = (_home is null ? 0 : 1) + (_completion is null ? 0 : 1);
            if (_home is { } home) sources.AcquireClose(home.DisposeAsync, releases);
            if (_completion is { } completion) sources.AcquireClose(completion.DisposeAsync, releases);
            foreach (var actual in releases)
                try { await sources.Await(actual).ConfigureAwait(false); }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            bool releaseAcknowledged = releases.Count == expectedReleases && releases.All(raw => raw.IsCompletedSuccessfully);
            bool nativeReleased = false;
            try { nativeReleased = sources.Physical(() => owner._creator.IsOwnedOriginalChangeNativeRelease(intent, sql)); }
            catch (Exception cause) { sources.Errors.Retain(cause); }
            bool auditMayReenterHome = releaseAcknowledged && (nativeReleased || HasHealthyDeclinedPreEffectSources);
            bool released = auditMayReenterHome && sources.Errors.OriginalErrors.Count == 0;
            if (_capability is { } capability)
            {
                // A genuinely acknowledged SQL effect can have a failed Home audit. Such
                // failure remains visible; it never retries the SQL operation or mints a receipt.
                try
                {
                    if (!auditMayReenterHome)
                        throw new InvalidOperationException("Unknown held-entry release refuses Home audit reentry.");
                    var audit = await sources.Read(() => capability.IsUncompletedClaim(owner._broker)
                        ? owner._broker.CompleteSetupExecutionWithinOriginalSourceAsync(capability,
                            new(acknowledged && released ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                                acknowledged && released
                                    ? _reviewChangeKind == CanonicalAutomationOriginalChangeKind.RecoverLegacy
                                        ? "HOME_AUTOMATION_DEFINITION_RECOVERED_NEEDS_REVIEW" : "HOME_AUTOMATION_DEFINITION_DISABLED"
                                    : sql is null ? "HOME_AUTOMATION_WRITE_NOT_DISPATCHED" : "HOME_AUTOMATION_WRITE_OUTCOME_UNCONFIRMED",
                                acknowledged ? "The exact atomic maintained-definition acknowledgment was observed; original cleanup and audit are retained independently."
                                    : "The requested automation transition was not acknowledged as successful. Existing definition values, history and any uncertain receipt remain available for recovery.", []), sources.Scope, sources.Retain, CancellationToken.None)
                        : owner._broker.AbortUnclaimedSetupExecutionWithinOriginalSourceAsync(capability, sources.Scope, sources.Retain, CancellationToken.None)).ConfigureAwait(false);
                    if (!audit.Succeeded) throw new InvalidOperationException("The actual Home WRITE audit is unfinished: " + audit.Code);
                }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            }
            if (_review is { } review && !HasHealthyWithdrawnPreEffectSources)
                try
                {
                    if (!auditMayReenterHome) throw new InvalidOperationException("Unknown Home/pin release refuses review-retirement reentry; original review is retained.");
                    if (!await sources.Read(() => owner._broker.RetireSetupPreparedReviewWithinOriginalSourceAsync(review, sources.Scope, sources.Retain, CancellationToken.None)).ConfigureAwait(false))
                        throw new InvalidOperationException("The actual Home WRITE review is not confirmed terminal and remains retained for recovery.");
                }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            lock (_gate) foreach (var cause in _publicationErrors) sources.Errors.Retain(cause);
            await sources.Settle().ConfigureAwait(false);
        }
        private Task? _closeObservation;
        private bool _healthyCloseObserved;
        internal bool HasIndependentlyJoinedHealthyClose { get { lock (_gate) return _healthyCloseObserved && _close?.IsCompletedSuccessfully == true; } }
        private async Task ObserveOriginalClose(Task sameClose)
        {
            try { await sameClose.ConfigureAwait(false); lock (_gate) _healthyCloseObserved = true; }
            catch { /* The same close/raw causes remain retained by this Claim. */ }
        }
        public void DemandExternalOriginalJoin()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(owner);
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        }
        public Task CloseAndDrainOriginalAsync() { DemandExternalOriginalJoin(); return CloseOwned(); }
        internal Task CloseOwned()
        {
            Task actual; TaskCompletionSource? begin = null;
            lock (_gate)
            {
                if (_close is null)
                {
                    begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _close = ClosePublished(begin.Task); _closeObservation = ObserveOriginalClose(_close);
                }
                actual = _close;
            }
            begin?.SetResult(); return actual;
        }
        private async Task ClosePublished(Task begin)
        {
            await begin.ConfigureAwait(false);
            // Every admitted acquisition/entry/source driver precedes cleanup. A pending
            // acquisition can return a late real lease; custody remains in its same fields.
            var errors = new List<Exception>(); Task[] operations;
            lock (_gate) operations = _operations.ToArray();
            foreach (var task in operations)
                try { await task.ConfigureAwait(false); }
                catch (Exception cause) { if (!IsAcknowledgedAcquisitionRefusal(task)) errors.Add(task.Exception ?? cause); }
            lock (_gate) _retired = true;
            Task settlement = SettleWrite(body => body(), _ => { });
            try { await settlement.ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(settlement.Exception ?? cause); }
            if (errors.Count != 0) throw new AggregateException("Actual individually approved Home WRITE and source/SQL/cleanup originals failed.", errors);
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    }

    private sealed class Context
    {
        private readonly HomeCanonicalAutomationDefinitionWriteSource _owner; private readonly Claim _claim;
        private readonly Action<Task> _retain; private readonly bool _productive;
        private readonly HomeOwnershipOriginalSourceCallbacks _protocol;
        internal readonly CloudflareOriginalTaskLedger Errors = new();
        internal Context(HomeCanonicalAutomationDefinitionWriteSource owner, Claim claim, Action<Action> caller,
            Action<Task> retain, bool productive = true)
        {
            ArgumentNullException.ThrowIfNull(caller); ArgumentNullException.ThrowIfNull(retain);
            _owner = owner; _claim = claim; _retain = retain; _productive = productive;
            _protocol = new(caller, Retain); Errors.BindOriginalOwner(claim); Errors.BindOriginalCallerCallback(Scope);
        }
        internal void Scope(Action body) => Physical(() => { _protocol.Run(body); return true; });
        internal void CleanupScope(Action body) => Physical(() => { body(); return true; });
        internal T Physical<T>(Func<T> body) => CloudflareOriginalExecutionGuard.InvokeOriginal(_owner,
            () => CloudflareOriginalExecutionGuard.InvokeOriginal(_claim, body));
        internal void DemandNoPriorFailure()
        {
            if (Errors.OriginalErrors.Count != 0 || _protocol.Errors.Length != 0)
                throw new AggregateException("Prior actual source/callback failure refuses another productive factory.", Errors.OriginalErrors.Concat(_protocol.Errors));
        }
        internal T Invoke<T>(Func<T> body)
        {
            if (_productive) { _claim.DemandLive(); DemandNoPriorFailure(); }
            var result = Errors.Invoke(body);
            if (_productive) _claim.DemandLive(); return result;
        }
        internal void Publish(Task driver) => Scope(() => _retain(driver));
        internal void Retain(Task raw) { ArgumentNullException.ThrowIfNull(raw); _ = Errors.Track(raw); Scope(() => _retain(raw)); }
        internal Task<T> Await<T>(Task<T> raw) => Errors.AwaitAsync(raw);
        internal Task Await(Task raw) => Errors.AwaitAsync(raw);
        internal Task<T> Read<T>(Func<Task<T>> factory) => Errors.RunToOriginalSettlementAsync(() => Invoke(factory));
        internal Task Read(Func<Task> factory) => Errors.RunToOriginalSettlementAsync(() => Invoke(factory));
        internal Task<T> Capture<T>(Func<Task<T>> factory, Action<T> capture) =>
            Errors.CaptureOriginalAcquisitionAsync(() => Invoke(factory), value => Physical(() => { capture(value); return true; }));
        internal Task<T> Capture<T>(Func<ValueTask<T>> factory, Action<T> capture) => Capture(() => factory().AsTask(), capture);
        internal void AcquireClose(Func<ValueTask> close, List<Task> closes)
        {
            try { _ = Errors.Invoke(() => { var raw = close().AsTask(); closes.Add(raw); _ = Errors.Track(raw); return raw; }); }
            catch (Exception cause) { Errors.Retain(cause); }
        }
        internal bool AllOriginalSourcesTerminal => Errors.OriginalTasks.All(raw => raw.IsCompleted);
        internal bool SuccessfullySettled => Errors.OriginalErrors.Count == 0 && _protocol.Errors.Length == 0 && Errors.OriginalTasks.All(raw => raw.IsCompletedSuccessfully);
        internal async Task Settle()
        {
            await Errors.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
            foreach (var cause in _protocol.Errors) Errors.Retain(cause);
            if (Errors.OriginalErrors.Count != 0) throw new AggregateException("Actual Home WRITE callback/raw source custody failed.", Errors.OriginalErrors);
        }
    }
}
