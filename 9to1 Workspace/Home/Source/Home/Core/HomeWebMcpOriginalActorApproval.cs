using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
using PermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace HavenOS.Home.Core;

/// <summary>Trusted local composition only. No default registration, remote Home or unsupported native fallback.</summary>
public sealed class HomeWebMcpOriginalActorApproval : IWebMcpOriginalActorApproval
{
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomePermissionTrustService _permissions;
    public HomeWebMcpOriginalActorApproval(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceOperationBroker broker, HomePermissionTrustService permissions)
    {
        ArgumentNullException.ThrowIfNull(store); ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(permissions);
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store) || !broker.IsBoundToPermissions(permissions))
            throw new InvalidOperationException("The exact local Home/profile/permission/broker graph is required.");
        _store = store; _profiles = profiles; _broker = broker; _permissions = permissions;
    }
    public IWebMcpOriginalActorReview PrepareReview(AuthenticatedResourceActor originalActor,
        WebMcpInvocationRequest request, IReadOnlyList<ResourceScope> originalOwnerScopes)
    {
        ArgumentNullException.ThrowIfNull(originalActor); ArgumentNullException.ThrowIfNull(request);
        if (!request.IsValid()) throw new ArgumentException("Invalid observed WebMcp request.", nameof(request));
        var captured = request with { InputSchema = request.InputSchema.Clone(), Arguments = request.Arguments.Clone() };
        var arguments = JsonSerializer.SerializeToElement(captured);
        var prepared = _broker.PrepareReviewForActor(originalActor, "browse", "webmcp.invoke", originalOwnerScopes,
            arguments, "Invoke the observed page tool " + captured.ToolName, null, "webmcp:" + captured.DocumentId);
        return new Review(this, originalActor, captured, arguments, prepared);
    }
    public async ValueTask<IWebMcpOriginalActorReview> ReviewAsync(AuthenticatedResourceActor actor,
        WebMcpInvocationRequest request, IReadOnlyList<ResourceScope> scopes, CancellationToken ct)
    {
        var review = PrepareReview(actor, request, scopes);
        await review.SubmitPreparedAsync(ct).ConfigureAwait(false); return review;
    }
    private static bool Matches(WebMcpInvocationRequest first, WebMcpInvocationRequest second) =>
        second.IsValid() && JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);

    private sealed class Review(HomeWebMcpOriginalActorApproval owner, AuthenticatedResourceActor actor,
        WebMcpInvocationRequest request, JsonElement arguments, HomeResourcePreparedReview prepared) : IWebMcpOriginalActorReview
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private WebMcpDispatchBeginResult? _begin;
        private HomeResourceExecutionCapability? _capability;
        private bool _reserved;
        public string RequestId => prepared.RequestId;
        public async ValueTask<WebMcpPreparedReviewSubmission> SubmitPreparedAsync(CancellationToken ct)
        {
            var observation = await owner._broker.AuthorizePreparedReviewAsync(prepared, ct).ConfigureAwait(false);
            if (observation.State == HomePreparedReviewState.AdmissionDenied)
                return new(WebMcpPreparedReviewState.Rejected, observation.Code);
            if (observation.State != HomePreparedReviewState.RequestObserved)
                return new(WebMcpPreparedReviewState.OutcomeUnconfirmed, observation.Code);
            return observation.Request!.State switch
            {
                HomePermissionRequestState.PendingApproval => new(WebMcpPreparedReviewState.PendingApproval, observation.Code),
                HomePermissionRequestState.Approved => new(WebMcpPreparedReviewState.Approved, observation.Code),
                HomePermissionRequestState.Executing => new(WebMcpPreparedReviewState.OutcomeUnconfirmed, "WEBMCP_ADMISSION_RESERVED"),
                _ => new(WebMcpPreparedReviewState.Rejected, observation.Code)
            };
        }
        public async ValueTask<WebMcpDispatchBeginResult> BeginDispatchAsync(WebMcpInvocationRequest fresh, CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_begin is not null) return _begin;
                if (_reserved) return new(WebMcpDispatchBeginState.OutcomeUnconfirmed, null, "WEBMCP_ADMISSION_RESERVED");
                if (!Matches(request, fresh)) return new(WebMcpDispatchBeginState.Rejected, null, "WEBMCP_BINDING_CHANGED");
                var observation = await owner._broker.ObservePreparedReviewAsync(prepared, ct).ConfigureAwait(false);
                if (observation.Request?.State == HomePermissionRequestState.PendingApproval)
                    return new(WebMcpDispatchBeginState.PendingApproval, null, "WEBMCP_APPROVAL_PENDING");
                if (observation.State == HomePreparedReviewState.AdmissionDenied ||
                    (observation.Request is not null && observation.Request.State is not (HomePermissionRequestState.PendingApproval or HomePermissionRequestState.Approved or HomePermissionRequestState.Executing)))
                    return new(WebMcpDispatchBeginState.Rejected, null, observation.Code);
                if (observation.State != HomePreparedReviewState.RequestObserved || observation.Request?.State != HomePermissionRequestState.Approved)
                    return new(WebMcpDispatchBeginState.OutcomeUnconfirmed, null, "WEBMCP_APPROVAL_UNCONFIRMED");
                _reserved = true; // Before Begin/Claim waits. Exceptions never restore this attempt.
                _capability = await owner._broker.BeginExecutionCapabilityAsync(RequestId, arguments, ct).ConfigureAwait(false);
                if (_capability is null) return _begin = new(WebMcpDispatchBeginState.OutcomeUnconfirmed, null, "WEBMCP_BEGIN_UNCONFIRMED");
                var claimed = await owner._broker.ClaimExecutionAsync(_capability, "browse", "webmcp.invoke", prepared.Scopes, arguments, ct).ConfigureAwait(false);
                if (claimed != actor) return _begin = new(WebMcpDispatchBeginState.Rejected, null, "WEBMCP_CLAIM_REJECTED");
                return _begin = new(WebMcpDispatchBeginState.Ready,
                    new Dispatch(owner, actor, request, prepared, _capability, observation.Request.Policy), "WEBMCP_DISPATCH_READY");
            }
            finally { _gate.Release(); }
        }
        public async ValueTask<WebMcpOwnerRecoveryStatus> FinishAdmissionAuditAsync(CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!_reserved || _begin?.State == WebMcpDispatchBeginState.Ready)
                    return new(false, false, "WEBMCP_NO_REJECTED_ADMISSION");
                var result = _capability is null
                    ? await owner._broker.RetryRejectedBeginAuditAsync(RequestId, ct).ConfigureAwait(false)
                    : await owner._broker.RetryRejectedClaimAuditAsync(_capability, ct).ConfigureAwait(false);
                return new(result.Succeeded, false, result.Code); // Audit is never effect completion evidence.
            }
            finally { _gate.Release(); }
        }
    }
    private sealed class Dispatch(HomeWebMcpOriginalActorApproval owner, AuthenticatedResourceActor actor,
        WebMcpInvocationRequest request, HomeResourcePreparedReview prepared, HomeResourceExecutionCapability capability,
        HomePermissionActionPolicy originalPolicy)
        : IWebMcpOriginalDispatch, IHomeStateCommitActorGuard
    {
        private readonly SemaphoreSlim _outcomeGate = new(1, 1);
        private WebMcpObservedOutcome? _outcome;
        private readonly object _knownOutcome = new();
        private int _entryReserved;
        private int _leaseActive;
        public string InvocationId { get; } = Guid.NewGuid().ToString("N");
        public ValueTask<bool> CheckFinalDispatchAsync(WebMcpInvocationRequest binding, CancellationToken ct) =>
            ValueTask.FromResult(false); // Boolean checks never substitute for the actual held local entry.
        public async ValueTask<IWebMcpFinalDispatchLease?> EnterFinalDispatchAsync(WebMcpInvocationRequest binding, CancellationToken ct)
        {
            await _outcomeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_outcome is not null || !Matches(request, binding) || Interlocked.CompareExchange(ref _entryReserved, 1, 0) != 0) return null;
                var lease = await owner._store.AcquireLocalOperationLeaseCoreAsync(owner._profiles, actor, this, ct).ConfigureAwait(false);
                if (lease is null) return null;
                bool accepted;
                lock (_knownOutcome)
                {
                    accepted = _outcome is null;
                    if (accepted) Interlocked.Exchange(ref _leaseActive, 1);
                }
                if (!accepted) { await lease.DisposeAsync().ConfigureAwait(false); return null; }
                return new EntryLease(lease, () => { lock (_knownOutcome) Interlocked.Exchange(ref _leaseActive, 0); });
            }
            finally { _outcomeGate.Release(); }
        }
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
            HomeStateCommitPhase phase, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (expected != actor || _outcome is not null) return ValueTask.FromResult(false);
            var records = state.Records.Where(item => item.RecordId == "home.permissions-trust" && item.RecordType == "home.permissions-trust").ToArray();
            if (records.Length != 1 || records[0].SchemaVersion != 1 || records[0].Scope != HomeDataScope.DeviceLocal ||
                records[0].Authority != HomeRecordAuthority.LocalCanonical) return ValueTask.FromResult(false);
            try
            {
                var requests = records[0].Payload.GetProperty("Requests").Deserialize<PermissionRequest[]>();
                var matches = requests?.Where(item => item.RequestId == prepared.RequestId).ToArray();
                return ValueTask.FromResult(matches?.Length == 1 && matches[0].State == HomePermissionRequestState.Executing &&
                    matches[0].Policy == originalPolicy && matches[0].Caller == prepared.Submission.Caller && matches[0].SessionId == prepared.Submission.SessionId &&
                    JsonSerializer.Serialize(matches[0].Scope) == JsonSerializer.Serialize(prepared.Submission.Scope) &&
                    JsonSerializer.Serialize(matches[0].Impact) == JsonSerializer.Serialize(prepared.Submission.Impact));
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
            { return ValueTask.FromResult(false); }
        }
        public async ValueTask<WebMcpOwnerRecoveryStatus> CompleteObservedAsync(WebMcpObservedOutcome outcome, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            if (outcome.InvocationId != InvocationId || !Enum.IsDefined(outcome.Kind)) throw new UnauthorizedAccessException("Original correlated outcome required.");
            if (outcome.Kind == WebMcpObservedOutcomeKind.OutcomeUnconfirmed) return new(false, false, "WEBMCP_OUTCOME_UNCONFIRMED");
            lock (_knownOutcome)
            {
                if (Volatile.Read(ref _leaseActive) != 0) throw new InvalidOperationException("Release native entry lease before Home completion.");
                if (_outcome is not null && _outcome != outcome) throw new InvalidOperationException("Original observed outcome cannot change.");
                _outcome ??= outcome; // BEFORE even a cancelled audit-gate wait; never lose the first actual observed outcome.
            }
            await _outcomeGate.WaitAsync(ct).ConfigureAwait(false);
            try { return await RecordAsync(ct).ConfigureAwait(false); }
            finally { _outcomeGate.Release(); }
        }
        public async ValueTask<WebMcpOwnerRecoveryStatus> FinishAuditAsync(CancellationToken ct)
        {
            await _outcomeGate.WaitAsync(ct).ConfigureAwait(false);
            try { return _outcome is null ? new(false, false, "WEBMCP_OUTCOME_UNCONFIRMED") : await RecordAsync(ct).ConfigureAwait(false); }
            finally { _outcomeGate.Release(); }
        }
        private async ValueTask<WebMcpOwnerRecoveryStatus> RecordAsync(CancellationToken ct)
        {
            var outcome = _outcome!;
            var state = outcome.Kind switch { WebMcpObservedOutcomeKind.Succeeded => HomePermissionRequestState.Succeeded,
                WebMcpObservedOutcomeKind.Cancelled => HomePermissionRequestState.Cancelled, _ => HomePermissionRequestState.Failed };
            var result = await owner._broker.CompleteExecutionAsync(capability, new(state, outcome.Code, outcome.Detail, []), ct).ConfigureAwait(false);
            return new(result.Succeeded, true, result.Code);
        }
        private sealed class EntryLease(IHomeLocalOperationLease lease, Action released) : IWebMcpFinalDispatchLease
        {
            public ValueTask<bool> CheckAsync(CancellationToken ct) => lease.IsCurrentAsync(ct);
            public async ValueTask DisposeAsync() { try { await lease.DisposeAsync().ConfigureAwait(false); } finally { released(); } }
        }
    }
}
