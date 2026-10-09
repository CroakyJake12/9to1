using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Separate manual WRITE for an identity-only change to one existing
/// physically protected catalogue. The actual catalogue producer owns native file
/// authority, byte comparison and mutation. No prior import or VM grant is borrowed.</summary>
public sealed partial class HomeCanonicalMiniComputerCatalogIdentitySource : ICanonicalMiniComputerCatalogIdentityHomeSource,
    IOriginalScopedCanonicalResourceAccessResolver, IAsyncDisposable
{
    public const string WriteAction = "mini-computer.catalog.initialize-identity";
    public string ResourceKind => "mini-computer.catalog.identity";
    public ICanonicalMiniComputerCatalogIdentitySource OriginalProducer => _creator;
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly ResourceAuthorizationService _resources;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomePermissionTrustService _permissions;
    private readonly ICanonicalMiniComputerCatalogIdentitySource _creator;
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<ICanonicalMiniComputerCatalogIdentityIntent, Claim> _issued = new();
    private readonly List<Claim> _active = [];
    private readonly ConditionalWeakTable<Task, Claim> _acquisitions = new();
    private bool _retiring; private Task? _close;

    public HomeCanonicalMiniComputerCatalogIdentitySource(FileHomeCoreStateStore actualStore, HomeLocalProfileIdentity actualProfiles,
        ResourceAuthorizationService actualResources, HomeResourceOperationBroker actualBroker,
        HomePermissionTrustService actualPermissions,
        ICanonicalMiniComputerCatalogIdentitySource actualCreator)
    {
        ArgumentNullException.ThrowIfNull(actualCreator);
        if (!actualProfiles.IsBoundToStore(actualStore) || !actualPermissions.IsBoundToStore(actualStore) ||
            !actualResources.IsBoundToActorSource(actualProfiles) || !actualBroker.IsBoundToOriginalComposition(actualResources, actualPermissions))
            throw new UnauthorizedAccessException("The SAME actual Home store/actor/resource/broker/policy tuple is required.");
        _store = actualStore; _profiles = actualProfiles; _resources = actualResources; _broker = actualBroker; _permissions = actualPermissions;
        _creator = actualCreator;
    }
    public bool HasOriginalComposition(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        ICanonicalMiniComputerCatalogIdentitySource creator) =>
        ReferenceEquals(_store, store) && ReferenceEquals(_profiles, profiles) && ReferenceEquals(_creator, creator);
    private void DemandLive() { lock (_gate) if (_retiring) throw new ObjectDisposedException(nameof(HomeCanonicalMiniComputerCatalogIdentitySource)); }
    private Claim Require(ICanonicalMiniComputerCatalogIdentityHomeClaim value)
    { lock (_gate) return value is Claim actual && ReferenceEquals(actual.Owner, this) && _issued.TryGetValue(actual.Intent, out var known) && ReferenceEquals(actual, known)
        ? actual : throw new UnauthorizedAccessException("The SAME private Home WRITE claim is required."); }
    public Task<ICanonicalMiniComputerCatalogIdentityHomeClaim> AcquireOriginalIdentityWriteWithinSourceAsync(
        ICanonicalMiniComputerCatalogIdentityIntent intent,
        Action<Action> scope, Action<Task> retain, Action<ICanonicalMiniComputerCatalogIdentityHomeClaim> capture, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(capture);
        DemandExternalOriginalJoin(); Claim actual; TaskCompletionSource? start = null;
        lock (_gate)
        {
            if (_retiring || _originalWithdrawalRequested) throw new ObjectDisposedException(nameof(HomeCanonicalMiniComputerCatalogIdentitySource));
            if (_issued.TryGetValue(intent, out actual!))
            {
                return actual.Acquisition;
            }
            _active.RemoveAll(value => value.OriginalClose?.IsCompletedSuccessfully == true);
            if (_active.Count >= 128) throw new InvalidOperationException("Original Home WRITE custody is full; unresolved evidence is retained.");
            actual = new(this, intent); _issued.Add(intent, actual); _active.Add(actual);
            start = actual.PublishAcquisition(scope, retain, token); _acquisitions.Add(actual.Acquisition, actual);
        }
        try { actual.PublishToCaller(capture); } finally { start.SetResult(); } return actual.Acquisition;
    }
    public bool IsAcknowledgedOriginalIdentityWriteRefusal(Task sameOriginalAcquisition) =>
        _acquisitions.TryGetValue(sameOriginalAcquisition, out var actual) && actual.IsAcknowledgedAcquisitionRefusal(sameOriginalAcquisition);
    public bool IsIssuedOriginalSettlementReleasePhase(ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalMiniComputerCatalogIdentityIntent, ICanonicalMiniComputerCatalogIdentityAcknowledgment> phase)
    {
        if (phase is not Claim.SettlementReleasePhase actual || !ReferenceEquals(actual.OriginalClaim.Owner, this)) return false;
        lock (_gate) if (!_issued.TryGetValue(actual.OriginalIntent, out var known) || !ReferenceEquals(known, actual.OriginalClaim)) return false;
        return actual.OriginalClaim.IsIssuedSettlementReleasePhase(actual);
    }
    public bool IsIssuedOriginalIdentityWriteClaim(ICanonicalMiniComputerCatalogIdentityHomeClaim value, ICanonicalMiniComputerCatalogIdentityIntent intent)
    {
        Claim actual; try { actual = Require(value); } catch (UnauthorizedAccessException) { return false; }
        lock (_gate) if (_retiring || !ReferenceEquals(actual.Intent, intent)) return false;
        return actual.IsReady;
    }
    public Task AcquireOriginalIdentityWriteEntryWithinSourceAsync(ICanonicalMiniComputerCatalogIdentityHomeClaim value,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Require(value).AcquireEntry(scope, retain, token);
    public Task ValidateOriginalCommitWithinSourceAsync(ICanonicalMiniComputerCatalogIdentityHomeClaim value,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Require(value).ValidateEntry(scope, retain, token);
    public void DemandOriginalIdentityWrite(ICanonicalMiniComputerCatalogIdentityHomeClaim value, ICanonicalMiniComputerCatalogIdentityIntent intent)
    {
        var actual = Require(value);
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => CloudflareOriginalExecutionGuard.InvokeOriginal(actual, () =>
        { if (!ReferenceEquals(actual.Intent, intent)) throw new UnauthorizedAccessException("Another intent cannot use this WRITE."); actual.DemandCommit(); return true; }));
    }
    public void RetainOriginalIdentityWrite(ICanonicalMiniComputerCatalogIdentityHomeClaim value, Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> operation)
    {
        var actual = Require(value);
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => CloudflareOriginalExecutionGuard.InvokeOriginal(actual, () =>
        { actual.RetainOperation(operation); return true; }));
    }
    public Task CompleteOriginalIdentityWriteWithinSourceAsync(ICanonicalMiniComputerCatalogIdentityHomeClaim value,
        Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment> operation, Action<Action> scope, Action<Task> retain, CancellationToken token)
    { var actual = Require(value); actual.RequireSameOperation(operation); return actual.SettleWrite(scope, retain); }
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope resource, CancellationToken token)
        => new(EvaluateWithinOriginalSourceAsync(actor, action, resource, body => body(), _ => { }, token));
    public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string action, ResourceScope resource,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        Claim? actual; lock (_gate) actual = !_retiring && SupportsOriginalAction(action)
            ? _active.SingleOrDefault(value => value.OriginalAction == action && value.Actor == actor && value.Scope == resource && value.IsReviewEligible) : null;
        if (actual is null) return Task.FromResult(new ResourceAccessDecision(false, "HOME_MINI_COMPUTER_IDENTITY_PRIVATE_WRITE_REQUIRED", actor.ActorId, resource.Revision, actor.OrganisationId));
        return actual.Run(scope, retain, async current =>
        {
            await actual.ValidateIntent(current, token).ConfigureAwait(false);
            return new ResourceAccessDecision(true, "HOME_MINI_COMPUTER_IDENTITY_MANUAL_WRITE", actor.ActorId, resource.Revision, actor.OrganisationId);
        });
    }
    public void DemandExternalOriginalJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Claim[] all; lock (_gate) all = _active.ToArray();
        foreach (var actual in all) actual.DemandExternalOriginalJoin();
    }
    // Seal new claims and request only this issuer's pending reviews. Already
    // accepted business retains its current authority until its owner joins it.
    public void RequestOriginalRetirement() => RequestOriginalPendingReviewWithdrawals();
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin();
        RequestOriginalPendingReviewWithdrawals();
        TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = ClosePublished(start.Task); }
            actual = _close;
        }
        start?.SetResult(); return actual;
    }
    private async Task ClosePublished(Task start)
    {
        await start.ConfigureAwait(false);
        Claim[] all; Task? withdrawals;
        lock (_gate) { all = _active.ToArray(); withdrawals = _originalPendingReviewWithdrawalTask; }
        var errors = new List<Exception>();
        // The review withdrawal owns an independent prepared-review child. Joining
        // it before acquisition avoids invalidating an accepted pending decision.
        if (withdrawals is not null)
            try { await withdrawals.ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(withdrawals.Exception ?? cause); }
        foreach (var claim in all)
            try { await claim.Acquisition.ConfigureAwait(false); }
            catch (Exception cause) { if (!claim.IsAcknowledgedAcquisitionRefusal(claim.Acquisition)) errors.Add(claim.Acquisition.Exception ?? cause); }
        lock (_gate) _retiring = true;
        var closes = new List<Task>();
        foreach (var claim in all) try { closes.Add(claim.CloseOwned()); } catch (Exception cause) { errors.Add(cause); }
        foreach (var close in closes) try { await close.ConfigureAwait(false); } catch (Exception cause) { errors.Add(close.Exception ?? cause); }
        if (errors.Count != 0) throw new AggregateException("Actual Home Mini Computer catalogue identity WRITE originals did not all close successfully.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());

    private sealed partial class Claim(HomeCanonicalMiniComputerCatalogIdentitySource owner,
        ICanonicalMiniComputerCatalogIdentityIntent intent)
        : ICanonicalMiniComputerCatalogIdentityHomeClaim
    {
        internal HomeCanonicalMiniComputerCatalogIdentitySource Owner => owner;
        internal ICanonicalMiniComputerCatalogIdentityIntent Intent => intent;
        public ICanonicalMiniComputerCatalogIdentityIntent OriginalIntent => intent;
        private readonly object _gate = new(); private bool _retired;
        private readonly List<Task> _operations = []; private readonly List<Context> _contexts = [];
        private Context _acquisitionContext = null!;
        internal Task<ICanonicalMiniComputerCatalogIdentityHomeClaim> Acquisition = null!;
        public Task<ICanonicalMiniComputerCatalogIdentityHomeClaim> OriginalAcquisition => Acquisition;
        private Exception? _acknowledgedRefusal; private HomePreparedReviewObservation? _declined;
        private HomeResourcePreparedReview? _review; private HomeResourceExecutionCapability? _capability;
        private HomeClaimedResourceAttestation? _attestation; private IAsyncDisposable? _completion;
        private IHomeLocalOperationLease? _home; private Task? _entry, _settlement, _close;
        private Task<ICanonicalMiniComputerCatalogIdentityAcknowledgment>? _operation;
        private JsonElement _arguments; internal ResourceScope? Scope;
        internal AuthenticatedResourceActor? Actor;
        public string OriginalApprovalRequestId { get { lock (_gate) return _review?.RequestId ?? string.Empty; } }
        public Task? OriginalClose { get { lock (_gate) return _close; } }
        internal bool IsLive { get { lock (_gate) return !_retired && _close is null; } }
        internal bool IsReviewEligible { get { lock (_gate) return !_retired && _close is null && _entry is null && _settlement is null; } }
        internal bool IsReady { get { lock (_gate) return !_retired && _close is null && Acquisition.IsCompletedSuccessfully &&
            _capability?.IsUncompletedClaim(owner._broker) == true && _completion is not null && _attestation is not null; } }
        internal void DemandLive() { owner.DemandLive(); if (!IsLive) throw new ObjectDisposedException("Original Mini Computer catalogue identity WRITE"); }
        internal TaskCompletionSource PublishAcquisition(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            _acquisitionContext = new(owner, this, scope, retain);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Acquisition = Drive(start.Task, _acquisitionContext, async current =>
            { try { await AcquireBody(current, token).ConfigureAwait(false); return (ICanonicalMiniComputerCatalogIdentityHomeClaim)this; } finally { _originalReviewPrepared.TrySetResult(null); } });
            _operations.Add(Acquisition); _contexts.Add(_acquisitionContext); return start;
        }
        internal void PublishToCaller(Action<ICanonicalMiniComputerCatalogIdentityHomeClaim>? capture)
        {
            try { _acquisitionContext.Scope(() => capture?.Invoke(this)); _acquisitionContext.Publish(Acquisition); }
            catch (Exception cause) { _acquisitionContext.Errors.Retain(cause); }
        }
        internal Task<T> Run<T>(Action<Action> scope, Action<Task> retain, Func<Context, Task<T>> body)
        {
            var current = new Context(owner, this, scope, retain); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<T> task;
            lock (owner._gate) lock (_gate)
            {
                if (owner._retiring || _retired || _close is not null || _settlement is not null) throw new ObjectDisposedException("Original Mini Computer catalogue identity WRITE");
                if (_operations.Count >= 4096) throw new InvalidOperationException("Original WRITE source custody is full.");
                task = Drive(start.Task, current, body); _operations.Add(task); _contexts.Add(current);
            }
            try { current.Publish(task); } catch (Exception cause) { current.Errors.Retain(cause); }
            finally { start.SetResult(); } return task;
        }
        private async Task<T> Drive<T>(Task start, Context current, Func<Context, Task<T>> body)
        {
            await start.ConfigureAwait(false); using var parent = CloudflareOriginalExecutionGuard.EnterOriginal(owner); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            T result = default!; try { current.DemandNoPriorFailure(); result = await body(current).ConfigureAwait(false); }
            catch (Exception cause)
            {
                if (ReferenceEquals(current, _acquisitionContext) && ReferenceEquals(cause, _acknowledgedRefusal))
                {
                    await current.Settle().ConfigureAwait(false);
                    if (HasHealthyDeclinedPreEffectSources) ExceptionDispatchInfo.Capture(cause).Throw();
                }
                current.Errors.Retain(cause);
            }
            await current.Settle().ConfigureAwait(false); return result;
        }
        private async Task AcquireBody(Context sources, CancellationToken token)
        {
            DemandLive();
            sources.Invoke(() =>
            {
                if (!owner._creator.IsIssuedOriginalIdentityIntent(intent) || intent.OperationId == Guid.Empty)
                    throw new UnauthorizedAccessException("The SAME actual catalogue producer must issue this identity-only WRITE intent.");
                _originalMutation = CaptureOriginalMutation(owner._creator, intent);
                Actor = _originalMutation.Actor;
                _arguments = JsonSerializer.SerializeToElement(new
                {
                    _originalMutation.OperationId, _originalMutation.Actor,
                    _originalMutation.CatalogueName, _originalMutation.OriginalCatalogSha256,
                    _originalMutation.OriginalFileEvidenceSha256, _originalMutation.OriginalByteLength,
                    _originalMutation.OriginalSchemaVersion, OriginalIntentDigest = _originalMutation.IntentDigest,
                    Action = WriteAction
                });
                var bytes = JsonSerializer.SerializeToUtf8Bytes(_arguments);
                if (bytes.Length > 256 * 1024) throw new InvalidDataException("The immutable catalogue identity WRITE intent exceeds its bound.");
                Scope = new(owner.ResourceKind, _originalMutation.OriginalFileEvidenceSha256 + ":" + _originalMutation.OperationId.ToString("N"),
                    Convert.ToHexString(SHA256.HashData(bytes)), ResourceAccess.Write); return true;
            });
            await ValidateIntent(sources, token).ConfigureAwait(false);
            _review = sources.Invoke(() => owner._broker.PrepareReviewForActor(Actor!, "mini-computer", _originalMutation.Action, [Scope!], _arguments,
                _originalMutation.ReviewMessage, null, "mini-computer-catalogue-identity:" + intent.OperationId.ToString("N")));
            HomePreparedReviewObservation observed;
            try
            {
                observed = await sources.Read(() =>
                {
                    var actual = owner._broker.AuthorizePreparedReviewWithinOriginalSourceAsync(_review, sources.Scope, sources.Retain, token, originalPermissionSources: true);
                    lock (_gate) _originalPreparedReviewTask = actual;
                    _originalReviewPrepared.TrySetResult(actual);
                    return actual;
                }).ConfigureAwait(false);
            }
            finally { _originalReviewPrepared.TrySetResult(null); }
            await DemandReviewNotWithdrawn(sources).ConfigureAwait(false);
            for (var poll = 0; observed.Request?.State == HomePermissionRequestState.PendingApproval && poll < 300; poll++)
            {
                DemandLive(); await sources.Read(() => Task.Delay(TimeSpan.FromSeconds(1), token)).ConfigureAwait(false);
                await DemandReviewNotWithdrawn(sources).ConfigureAwait(false);
                observed = await sources.Read(() => owner._broker.ObservePreparedReviewWithinOriginalSourceAsync(_review, sources.Scope, sources.Retain, token, originalPermissionSources: true)).ConfigureAwait(false);
            }
            if (observed.State == HomePreparedReviewState.RequestObserved && observed.Request?.RequestId == _review.RequestId &&
                observed.Request.State is HomePermissionRequestState.Denied or HomePermissionRequestState.Blocked)
            {
                _declined = observed;
                _acknowledgedRefusal = new UnauthorizedAccessException("The separate actual Home WRITE was individually declined before capability/file mutation admission.");
                throw _acknowledgedRefusal;
            }
            if (observed.Request?.State != HomePermissionRequestState.Approved) throw new UnauthorizedAccessException("The separate actual Home WRITE was not individually accepted.");
            await ValidateIntent(sources, token).ConfigureAwait(false);
            await DemandReviewNotWithdrawn(sources).ConfigureAwait(false);
            bool withdrawing;
            lock (_gate)
            {
                withdrawing = _originalReviewWithdrawal is not null;
                if (!withdrawing) _originalCapabilityAcquisitionStarted = true;
            }
            if (withdrawing)
            {
                await DemandReviewNotWithdrawn(sources).ConfigureAwait(false);
                // A healthy false withdrawal means approval/execution won. The SAME
                // cached withdrawal prevents a new review-cancel driver being admitted.
                lock (_gate) _originalCapabilityAcquisitionStarted = true;
            }
            await sources.Capture(() => owner._broker.BeginExecutionCapabilityWithinOriginalSourceAsync(_review.RequestId, _arguments,
                sources.Scope, sources.Retain, sources.CleanupScope, token, originalPermissionSources: true), value => _capability = value).ConfigureAwait(false);
            if (_capability is null) throw new UnauthorizedAccessException("No genuine WRITE capability was issued.");
            var claim = await sources.Read(() => owner._broker.ClaimExecutionWithinOriginalSourceAsync(_capability, "mini-computer", _originalMutation.Action,
                [Scope!], _arguments, sources.Scope, sources.Retain, sources.CleanupScope, token, originalPermissionSources: true)).ConfigureAwait(false);
            if (claim.Disposition != HomeResourceClaimDisposition.Claimed || claim.Actor != Actor) throw new UnauthorizedAccessException("The actual Home WRITE claim was refused.");
            _attestation = sources.Invoke(() => owner._broker.CaptureClaimedAttestation(_capability));
            if (_attestation is null) throw new UnauthorizedAccessException("An individual actual WRITE Accept is required.");
            await sources.Capture(() => _capability.AcquireCommitCompletionLeaseAsync(owner._broker, token, new(sources.Scope, sources.Retain)), value => _completion = value).ConfigureAwait(false);
            if (_completion is null) throw new UnauthorizedAccessException("The actual WRITE completion lease is unavailable.");
        }
        internal async Task ValidateIntent(Context sources, CancellationToken token)
        {
            DemandLive();
            sources.Invoke(() => { DemandSameOriginalMutation(); return true; });
            if (!sources.Invoke(() => owner._creator.IsIssuedOriginalIdentityIntent(intent)))
                throw new UnauthorizedAccessException("The actual existing-file identity intent was revoked.");
            if (await sources.Read(() => owner._profiles.GetCurrentWithinOriginalSourceAsync(sources.Scope, sources.Retain, token)).ConfigureAwait(false) != Actor)
                throw new UnauthorizedAccessException("The actual Home actor changed before WRITE.");
            await sources.Read(() => owner._creator.ValidateOriginalIdentityIntentWithinSourceAsync(intent, sources.Scope, sources.Retain, token)).ConfigureAwait(false);

        }
    }
}

public sealed class HomeMiniComputerCatalogIdentityActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "mini-computer" && HomeCanonicalMiniComputerCatalogIdentitySource.SupportsOriginalAction(actionId)
        ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, false, false, RequiresPerActionApproval: true) : null;
}

// Deferred original composition breaks the Home resolver/creator construction cycle.
// It has no identity, policy or grant of its own and refuses an absent actual owner.
public sealed class HomeMiniComputerCatalogIdentityResourceResolver(Func<HomeCanonicalMiniComputerCatalogIdentitySource> original)
    : IOriginalScopedCanonicalResourceAccessResolver
{
    public string ResourceKind => "mini-computer.catalog.identity";
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action,
        ResourceScope resource, CancellationToken token) => original().EvaluateAsync(actor, action, resource, token);
    public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string action,
        ResourceScope resource, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        original().EvaluateWithinOriginalSourceAsync(actor, action, resource, scope, retain, token);
    public bool IsBoundToOriginalOwner(HomeCanonicalMiniComputerCatalogIdentitySource sameOwner) => ReferenceEquals(original(), sameOwner);
}
