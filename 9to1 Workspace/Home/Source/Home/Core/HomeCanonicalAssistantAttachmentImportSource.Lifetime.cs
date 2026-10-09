using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed partial class HomeCanonicalAssistantAttachmentImportSource
{
    private sealed partial class Claim
    {
        private bool HasHealthyDeclinedPreEffectSources =>
            ((_declined is { State: HomePreparedReviewState.RequestObserved, Request: { } request } && _review is not null &&
                request.RequestId == _review.RequestId && request.State is HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked) ||
                HasHealthyWithdrawnPreEffectSources) &&
            _capability is null && _attestation is null && _completion is null && _home is null && _entry is null && _operation is null &&
            _acknowledgedRefusal is not null && _acquisitionContext.SuccessfullySettled;
        internal bool IsAcknowledgedAcquisitionRefusal(Task actual) => ReferenceEquals(actual, Acquisition) && actual.IsFaulted &&
            HasHealthyDeclinedPreEffectSources && actual.Exception is { InnerExceptions.Count: 1 } group &&
            ReferenceEquals(group.InnerExceptions[0], _acknowledgedRefusal);
        internal sealed class SettlementReleasePhase(Claim claim, Task<ICanonicalAttachmentImportAcknowledgment>? operation, Task[] releases, int expected)
            : ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalAttachmentImportIntent, ICanonicalAttachmentImportAcknowledgment>
        {
            internal readonly Claim OriginalClaim = claim;
            internal readonly Task[] OriginalHomeReleases = releases;
            internal readonly int ExpectedReleaseCount = expected;
            public ICanonicalAttachmentImportIntent OriginalIntent => OriginalClaim.Intent;
            public Task<ICanonicalAttachmentImportAcknowledgment>? OriginalAtomicSqlTask => operation;
        }
        private SettlementReleasePhase? _originalSettlementReleasePhase;
        private Task? _originalDispatchWait; private bool _originalDispatchAcknowledged;
        internal bool IsIssuedSettlementReleasePhase(SettlementReleasePhase same)
        {
            lock (_gate) return ReferenceEquals(_originalSettlementReleasePhase, same) && ReferenceEquals(same.OriginalClaim, this) &&
                ReferenceEquals(same.OriginalAtomicSqlTask, _operation) && (_operation is null || _operation.IsCompleted) &&
                _settlement is not null && _originalDispatchWait?.IsCompletedSuccessfully == true && _originalDispatchAcknowledged &&
                _operations.All(raw => raw.IsCompleted) &&
                same.OriginalHomeReleases.Length == same.ExpectedReleaseCount && same.OriginalHomeReleases.All(raw => raw.IsCompletedSuccessfully) &&
                _contexts.All(context => context.AllOriginalSourcesTerminal);
        }
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
            Task[] operations; Context[] contexts; Task<ICanonicalAttachmentImportAcknowledgment>? operation;
            lock (_gate) { operations = _operations.ToArray(); contexts = _contexts.ToArray(); operation = _operation; }
            ICanonicalAttachmentImportAcknowledgment? acknowledgment = null;
            if (operation is not null)
                try { acknowledgment = await sources.Await(operation).ConfigureAwait(false); }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            foreach (var originalTask in operations)
            {
                if (IsAcknowledgedAcquisitionRefusal(originalTask))
                {
                    // Independently join and preserve the exact failed source occurrence in
                    // Acquisition/_operations. It is never enrolled in a generic error ledger
                    // which would recapture an already source-acknowledged refusal as failure.
                    try { await originalTask.ConfigureAwait(false); }
                    catch (Exception cause) { if (!IsAcknowledgedAcquisitionRefusal(originalTask)) sources.Errors.Capture(originalTask, cause); }
                }
                else try { await sources.Await(originalTask).ConfigureAwait(false); }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            }
            foreach (var context in contexts)
                try { await context.Settle().ConfigureAwait(false); }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            bool acknowledged = false;
            try { acknowledged = operation?.IsCompletedSuccessfully == true && acknowledgment is not null && sources.Physical(() =>
                owner._creator.IsOriginalAtomicImportTask(intent, operation) && owner._creator.IsOwnedOriginalImportAcknowledgment(intent, acknowledgment, operation)); }
            catch (Exception cause) { sources.Errors.Retain(cause); }
            (string Code, bool Applied)? acknowledgedDecision = null;
            if (acknowledged)
                try { acknowledgedDecision = sources.Physical(() => CaptureAcknowledgedDecisionAudit(acknowledgment!)); }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            if (operation is not null && !acknowledged)
                sources.Errors.Retain(new InvalidOperationException("The retained actual attachment atomic SQL child did not issue a healthy SAME acknowledgment; preserve its operation/payload for recovery."));
            // Independent original cleanup. Both raw releases are acquired before either
            // await; no failure/hold skips the sibling. Home audit is never under these locks.
            var releases = new List<Task>(); var expectedReleases = (_home is null ? 0 : 1) + (_completion is null ? 0 : 1);
            if (_home is { } home) sources.AcquireClose(home.DisposeAsync, releases);
            if (_completion is { } completion) sources.AcquireClose(completion.DisposeAsync, releases);
            foreach (var actual in releases)
                try { await sources.Await(actual).ConfigureAwait(false); }
                catch (Exception cause) { sources.Errors.Retain(cause); }
            bool releaseAcknowledged = releases.Count == expectedReleases && releases.All(raw => raw.IsCompletedSuccessfully);
            bool pinReleaseAcknowledged = true;
            if (owner._creator is ICanonicalOriginalWriteSettlementPinOwner<ICanonicalAttachmentImportIntent, ICanonicalAttachmentImportAcknowledgment> pinOwner)
            {
                pinReleaseAcknowledged = false;
                if (releaseAcknowledged)
                {
                    // An external close can arrive during an actual SQL/Den pin acquisition.
                    // Join its source-owned dispatch barrier, not the encompassing creator
                    // which may itself be waiting for this settlement to finish.
                    Task? dispatch = null; bool dispatchAcknowledged = false;
                    try
                    {
                        sources.Physical(() =>
                        {
                            dispatch = pinOwner.WaitOriginalSettlementDispatchWithinSourceAsync(intent, sources.Scope, sources.Retain, CancellationToken.None);
                            lock (_gate) _originalDispatchWait = dispatch;
                            _ = sources.Errors.Track(dispatch); return true;
                        });
                    }
                    catch (Exception cause) { sources.Errors.Retain(cause); }
                    if (dispatch is not null)
                        try { await sources.Await(dispatch).ConfigureAwait(false); } catch (Exception cause) { sources.Errors.Capture(dispatch, cause); }
                    lock (_gate) operation = _operation;
                    try { dispatchAcknowledged = dispatch?.IsCompletedSuccessfully == true && sources.Physical(() =>
                        pinOwner.IsOwnedOriginalSettlementDispatch(intent, dispatch, operation)); }
                    catch (Exception cause) { sources.Errors.Retain(cause); }
                    lock (_gate) _originalDispatchAcknowledged = dispatchAcknowledged;
                    if (!dispatchAcknowledged) sources.Errors.Retain(new InvalidOperationException("The actual source-owned dispatch/no-SQL-effect boundary is not acknowledged; Home audit remains unstarted."));
                    SettlementReleasePhase phase;
                    lock (_gate) phase = _originalSettlementReleasePhase ??= new(this, operation, releases.ToArray(), expectedReleases);
                    Task? release = null;
                    try
                    {
                        if (!dispatchAcknowledged || !IsIssuedSettlementReleasePhase(phase)) throw new UnauthorizedAccessException("The actual source dispatch/SQL/Home release cohort is not terminal.");
                        sources.Physical(() =>
                        {
                            release = pinOwner.ReleaseOriginalSettlementPinsWithinSourceAsync(phase, sources.Scope, sources.Retain, CancellationToken.None);
                            _ = sources.Errors.Track(release); return true; // Retain before a post-callback scope can fail.
                        });
                    }
                    catch (Exception cause) { sources.Errors.Retain(cause); }
                    if (release is not null)
                        try { await sources.Await(release).ConfigureAwait(false); } catch (Exception cause) { sources.Errors.Capture(release, cause); }
                    try { pinReleaseAcknowledged = release?.IsCompletedSuccessfully == true && sources.Physical(() =>
                        pinOwner.IsOwnedOriginalSettlementPinRelease(phase, release)); }
                    catch (Exception cause) { sources.Errors.Retain(cause); }
                }
                if (!pinReleaseAcknowledged) sources.Errors.Retain(new InvalidOperationException("Original settlement-pin release has no SAME healthy issuer acknowledgment; Home audit remains unstarted."));
            }
            bool auditMayReenterHome = releaseAcknowledged && pinReleaseAcknowledged;
            bool released = auditMayReenterHome && sources.Errors.OriginalErrors.Count == 0;
            if (_capability is { } capability)
            {
                // A genuinely acknowledged atomic SQL outcome can have a failed Home audit. Such
                // failure remains visible; it never retries the attachment draft mutation or mints a receipt.
                try
                {
                    if (!auditMayReenterHome)
                        throw new InvalidOperationException("Unknown held-entry release refuses Home audit reentry.");
                    var audit = await sources.Read(() => capability.IsUncompletedClaim(owner._broker)
                        ? owner._broker.CompleteSetupExecutionWithinOriginalSourceAsync(capability,
                            new(acknowledged && released && acknowledgedDecision?.Applied == true ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                                acknowledged && released ? acknowledgedDecision!.Value.Code : operation is null ? "HOME_ASSISTANT_ATTACHMENT_NOT_DISPATCHED" : "HOME_ASSISTANT_ATTACHMENT_OUTCOME_UNCONFIRMED",
                                acknowledged ? "The exact source-issued attachment draft decision was observed. A known no-effect decision does not report the requested draft change as succeeded; cleanup and Home audit remain independently required."
                                    : "The attachment draft mutation was not acknowledged as applied. The original file and attachment rows, plus every uncertain SQL outcome, remain retained for recovery.", []), sources.Scope, sources.Retain, CancellationToken.None)
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
                    _retired = true; begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _close = ClosePublished(begin.Task);
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
            Task settlement = SettleWrite(body => body(), _ => { });
            try { await settlement.ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(settlement.Exception ?? cause); }
            if (errors.Count != 0) throw new AggregateException("Actual individually approved Home WRITE and source/SQL/cleanup originals failed.", errors);
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    }

    private sealed class Context
    {
        private readonly HomeCanonicalAssistantAttachmentImportSource _owner; private readonly Claim _claim;
        private readonly Action<Task> _retain; private readonly bool _productive;
        private readonly HomeOwnershipOriginalSourceCallbacks _protocol;
        internal readonly CloudflareOriginalTaskLedger Errors = new();
        internal Context(HomeCanonicalAssistantAttachmentImportSource owner, Claim claim, Action<Action> caller,
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
