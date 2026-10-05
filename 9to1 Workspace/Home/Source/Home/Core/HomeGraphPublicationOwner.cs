using System.Text.Json;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Application.NodeGraph;
using HavenOS.Home.PermissionsTrustNotifications;
using PermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace HavenOS.Home.Core;

/// <summary>Opt-in trusted local composition only. No definition/pair capability is accepted and no run permission is issued.</summary>
public sealed class HomeGraphPublicationOwner
{
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly HomeVersionedNodeGraphRepository _graphs;
    private readonly HomeResourceOperationBroker _broker;
    private readonly IGraphPublicationOriginAuthority[] _origins;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Review, byte> _reviews = new();
    private readonly HomeGraphPublicationResourceRegistry _resources;
    public HomeGraphPublicationOwner(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, HomeVersionedNodeGraphRepository graphs,
        HomeResourceOperationBroker broker, HomePermissionTrustService permissions,
        IEnumerable<IGraphPublicationOriginAuthority> origins, HomeGraphPublicationResourceRegistry resources)
    {
        if (!profiles.IsBoundToStore(store) || !ownership.IsBoundTo(store, profiles) || !graphs.IsBoundToStore(store) ||
            !permissions.IsBoundToStore(store) || !broker.IsBoundToPermissions(permissions))
            throw new UnauthorizedAccessException("Same actual local Home graph required.");
        _store = store; _profiles = profiles; _ownership = ownership; _graphs = graphs; _broker = broker;
        _origins = origins.ToArray(); _resources = resources;
    }
    internal bool IsBoundToAssociationComposition(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, HomeResourceOperationBroker broker) =>
        ReferenceEquals(_store, store) && ReferenceEquals(_profiles, profiles) &&
        ReferenceEquals(_ownership, ownership) && ReferenceEquals(_broker, broker);
    public Review Prepare(GraphPublicationIntent intent, IOriginalGraphOwnerSelection originalSelection,
        string owningResourceKind, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(originalSelection);
        var authorities = _origins.Where(owner => owner.OwnerAppId == intent.Owner.AppId).ToArray();
        if (authorities.Length != 1 || !authorities[0].IsBoundToGraphRepository(_graphs))
            throw new NotSupportedException("One actual original owner bound to this canonical graph repository required.");
        if (owningResourceKind != "automations") throw new ArgumentException("Actual registered Automation store kind required.");
        var entityScopeId = intent.Owner.StoreId.ToString("D") + "/" + intent.Owner.EntityId.ToString("D");
        var graphScopeId = intent.OriginalActor.ProfileId + "/" + intent.Graph.GraphId.ToString("D");
        if (intent.Scopes.Count(scope => scope.Kind == intent.Owner.EntityKind && scope.Id == entityScopeId &&
            scope.Access == ResourceAccess.Write) != 1 ||
            intent.Scopes.Count(scope => scope.Kind == "nodegraph.definition" && scope.Id == graphScopeId &&
                scope.Revision == intent.ExpectedGraphRevision.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                scope.Access == ResourceAccess.Write) != 1)
            throw new ArgumentException("Exact owning entity and profile-qualified graph scopes required.");
        // Profile qualification is an actor domain, not a physical Home StoreUUID. The actual issuer/store
        // references and locked-state guards provide the local origin check. Missing genuine resolver denies.
        var action = intent.Kind == GraphPublicationKind.SaveDraft ? "automations.graph.save-draft" : "automations.activate";
        // Restrict this first owning adapter; no generic arbitrary target/action mapping or legacy ownership adoption.
        if (intent.Owner.AppId != "automations" || intent.Owner.EntityKind != "automation.reusable-task")
            throw new NotSupportedException("Registered owning graph publication adapter unavailable.");
        if (_reviews.Count >= 1024) throw new InvalidOperationException("Retained graph review capacity exceeded.");
        var prepared = _broker.PrepareReviewForActor(intent.OriginalActor, intent.Owner.AppId, action, intent.Scopes,
            intent.Arguments, "Publish the exact approved graph revision", null, sessionId);
        var review = new Review(this, intent, owningResourceKind, action, prepared, originalSelection, authorities[0]);
        if (!_reviews.TryAdd(review, 0)) throw new InvalidOperationException("Graph review issuance failed.");
        _resources.Register(review);
        return review;
    }
    // The SQL owner must supply state from its genuine raw Home reader inside its own final guard.
    // This method performs no reads and cannot substitute for original SQL actor/capability/receipt checks.
    public bool IsObservationCurrentInState(HomeGraphPublicationObservation observation,
        AuthenticatedResourceActor originalActor, HomeCoreStoredState actualState)
    {
        ArgumentNullException.ThrowIfNull(observation); ArgumentNullException.ThrowIfNull(actualState);
        if (!ReferenceEquals(observation.Issuer, this) || !_reviews.ContainsKey(observation.Review) ||
            observation.Actor != originalActor || !observation.Review.MatchesObservation(observation, actualState)) return false;
        var matches = actualState.Records.Where(record => record.RecordId == observation.RecordId).ToArray();
        return matches.Length == 1 && matches[0].RecordType == "home.node-graph" && matches[0].SchemaVersion == 1 &&
            matches[0].Scope == HomeDataScope.DeviceLocal && matches[0].Authority == HomeRecordAuthority.LocalCanonical &&
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(matches[0])).AsSpan().SequenceEqual(observation.RecordFingerprint);
    }
    public sealed class Review
    {
        internal bool IsPublicationCapability(HomeResourceExecutionCapability capability) => ReferenceEquals(_capability, capability);
        private readonly HomeGraphPublicationOwner _issuer;
        private readonly GraphPublicationIntent _intent;
        private readonly string _kind, _action;
        private readonly HomeResourcePreparedReview _prepared;
        private readonly IOriginalGraphOwnerSelection _originalSelection;
        private readonly IGraphPublicationOriginAuthority _originAuthority;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private VerifiedResourceStoreOwnership? _binding;
        private HomeResourceExecutionCapability? _capability;
        private HomePermissionActionPolicy? _policy;
        private int _executionReserved;
        private bool _claimed;
        private bool _retired;
        private GraphPublicationReceipt? _known;
        private bool _publicationAttempted;
        private bool _noPublicationKnown;
        private bool _auditRecorded;
        internal Review(HomeGraphPublicationOwner issuer, GraphPublicationIntent intent, string kind, string action,
            HomeResourcePreparedReview prepared, IOriginalGraphOwnerSelection originalSelection, IGraphPublicationOriginAuthority originAuthority)
        { _issuer = issuer; _intent = intent; _kind = kind; _action = action; _prepared = prepared; _originalSelection = originalSelection; _originAuthority = originAuthority; }
        private Task RequireOriginAsync(CancellationToken ct) =>
            _originAuthority.RequireCurrentAsync(_originalSelection, _intent, ct).AsTask();
        internal bool MatchesResource(AuthenticatedResourceActor actor, string action, ResourceScope scope) =>
            !Volatile.Read(ref _retired) && actor == _intent.OriginalActor && action == _action && _intent.Scopes.Contains(scope) &&
            scope.Kind == "nodegraph.definition" && scope.Access == ResourceAccess.Write;
        internal async ValueTask<bool> VerifyResourceAsync(CancellationToken ct)
        {
            await RequireOriginAsync(ct).ConfigureAwait(false);
            if (await _issuer._profiles.GetCurrentAsync(ct).ConfigureAwait(false) != _intent.OriginalActor) return false;
            var graph = await _issuer._graphs.GetAsync(_intent.Graph.GraphId, ct).ConfigureAwait(false);
            var valid = graph is null ? _intent.ExpectedGraphRevision == 0 && _intent.Kind == GraphPublicationKind.SaveDraft :
                graph.CanonicalOwner == _intent.Owner && graph.Draft.Revision == _intent.ExpectedGraphRevision &&
                graph.OwnerAppId == _intent.Owner.AppId && graph.OwnerEntityId == _intent.Owner.CanonicalEntityId;
            await RequireOriginAsync(ct).ConfigureAwait(false);
            return valid && await _issuer._profiles.GetCurrentAsync(ct).ConfigureAwait(false) == _intent.OriginalActor;
        }
        public string RequestId => _prepared.RequestId;
        public async Task<HomePreparedReviewObservation> SubmitAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_retired) throw new UnauthorizedAccessException("Graph review retired.");
                await RequireOriginAsync(ct).ConfigureAwait(false);
                if (_binding is null)
                {
                    if (await _issuer._profiles.GetCurrentAsync(ct).ConfigureAwait(false) != _intent.OriginalActor)
                        throw new UnauthorizedAccessException("Original graph owner actor changed.");
                    var binding = await _issuer._ownership.GetVerifiedAsync(_kind, _intent.Owner.StoreId.ToString("D"), ct).ConfigureAwait(false);
                    if (binding is null || !await _issuer._ownership.IsCurrentAsync(binding, _intent.OriginalActor, ct).ConfigureAwait(false))
                        throw new UnauthorizedAccessException("Actual original owning store receipt required.");
                    _binding = binding;
                }
                return await _issuer._broker.AuthorizePreparedReviewAsync(_prepared, ct).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }
        public async Task<GraphPublicationReceipt?> PublishAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_known is not null) return _known;
                if (_noPublicationKnown) return null;
                if (_retired) throw new UnauthorizedAccessException("Graph review retired.");
                // Once reserved, retry is observation only; missing/superseded never authorizes another write.
                if (Volatile.Read(ref _executionReserved) != 0)
                {
                    if (!_claimed || _capability is null || _binding is null ||
                        !await _issuer._ownership.IsCurrentAsync(_binding, _intent.OriginalActor, ct).ConfigureAwait(false)) return null;
                    return _known = await _issuer._graphs.ObservePublicationAsync(_intent, _prepared.Submission.Impact.ArgumentsDigest!, ct).ConfigureAwait(false);
                }
                await RequireOriginAsync(ct).ConfigureAwait(false);
                var observation = await _issuer._broker.ObservePreparedReviewAsync(_prepared, ct).ConfigureAwait(false);
                if (observation.Request?.State != HomePermissionRequestState.Approved || _binding is null ||
                    !await _issuer._ownership.IsCurrentAsync(_binding, _intent.OriginalActor, ct).ConfigureAwait(false)) return null;
                _policy = observation.Request.Policy;
                Interlocked.Exchange(ref _executionReserved, 1); // BEFORE any Begin/Claim/publication await.
                try
                {
                    _capability = await _issuer._broker.BeginExecutionCapabilityAsync(RequestId, _intent.Arguments, ct).ConfigureAwait(false);
                    if (_capability is null) { _noPublicationKnown = true; return null; }
                    var actor = await _issuer._broker.ClaimExecutionAsync(_capability, _intent.Owner.AppId, _action,
                        _intent.Scopes, _intent.Arguments, ct).ConfigureAwait(false);
                    if (actor != _intent.OriginalActor) { _noPublicationKnown = true; return null; }
                    _claimed = true;
                    _publicationAttempted = true; // BEFORE entering the writer; a lost return is unknown.
                    _known = await _issuer._graphs.TryPublishGuardedAsync(_intent, _prepared.Submission.Impact.ArgumentsDigest!, new PublicationGuard(this), ct).ConfigureAwait(false);
                    _noPublicationKnown = _known is null; // Actual writer returned refusal, not an inferred transport failure.
                    return _known;
                }
                catch
                {
                    if (!_publicationAttempted) _noPublicationKnown = true; // No graph writer was invoked.
                    throw; // Original transport/cancellation is preserved; retained outcome remains separately observable.
                }
            }
            finally { _gate.Release(); }
        }
        // SQL is already held by the association owner. Waiting here could invert a concurrent
        // graph operation's review -> origin SQL ordering, so contention MUST deny immediately.
        internal IAsyncDisposable? TryAcquireAssociationLifetime(HomeGraphPublicationObservation observation)
        {
            if (!_gate.Wait(0)) return null;
            if (!ReferenceEquals(observation.Issuer, _issuer) || !ReferenceEquals(observation.Review, this) ||
                !_issuer._reviews.ContainsKey(this) || _retired || !_claimed || _known is null ||
                _known != observation.Receipt || observation.Actor != _intent.OriginalActor)
            { _gate.Release(); return null; }
            return new AssociationLifetime(_gate);
        }
        private sealed class AssociationLifetime(SemaphoreSlim gate) : IAsyncDisposable
        {
            private int _disposed;
            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) gate.Release();
                return ValueTask.CompletedTask;
            }
        }
        internal bool MatchesObservation(HomeGraphPublicationObservation observation, HomeCoreStoredState state) =>
            !Volatile.Read(ref _retired) && _claimed && _known is not null && _known == observation.Receipt &&
            _binding is not null && HomeLocalStoreOwnership.IsReceiptCurrentInState(state, _binding);
        public async Task<HomeGraphPublicationObservation?> CaptureObservationAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var known = _known;
                if (_retired || !_claimed || known is null || _binding is null) return null;
                await _originAuthority.RequirePublishedAsync(_originalSelection, _intent, known, ct).ConfigureAwait(false);
                if (!await _issuer._ownership.IsCurrentAsync(_binding, _intent.OriginalActor, ct).ConfigureAwait(false)) return null;
                var record = await _issuer._graphs.ObservePublicationRecordAsync(_intent,
                    _prepared.Submission.Impact.ArgumentsDigest!, ct).ConfigureAwait(false);
                await _originAuthority.RequirePublishedAsync(_originalSelection, _intent, known, ct).ConfigureAwait(false);
                if (record is null || !await _issuer._ownership.IsCurrentAsync(_binding, _intent.OriginalActor, ct).ConfigureAwait(false)) return null;
                return new(_issuer, this, _intent.OriginalActor, known, record);
            }
            finally { _gate.Release(); }
        }
        // Read-only same-host outcome metadata. No admission, actor adoption, mutation, or audit IO.
        public async Task<GraphPublicationOutcome> GetRetainedOutcomeAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return new(_known is not null ? true : _noPublicationKnown ? false : null, _known, _auditRecorded,
                    _known is not null ? _auditRecorded ? "GraphPublished" : "GraphPublishedAuditPending" :
                    _noPublicationKnown ? _auditRecorded ? "GraphNotPublished" : "GraphNotPublishedAuditPending" :
                    Volatile.Read(ref _executionReserved) != 0 ? "GraphPublicationOutcomeUnconfirmed" : "GraphPublicationNotStarted");
            }
            finally { _gate.Release(); }
        }
        public async Task<bool> RetireAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_retired) return true;
                if (!await _issuer._broker.RetirePreparedReviewAsync(_prepared, ct).ConfigureAwait(false)) return false;
                Volatile.Write(ref _retired, true);
                _issuer._resources.Retire(this);
                _issuer._reviews.TryRemove(this, out _);
                return true; // Metadata retirement only; never cancellation or audit acknowledgement.
            }
            finally { _gate.Release(); }
        }
        // Audit-only after a known canonical publication, including a later profile/store change.
        // Null/missing publication remains unknown and cannot be turned into a failure outcome.
        public async Task<HomePermissionOperationResult> FinishAuditAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_auditRecorded) return new(true, "GRAPH_PUBLICATION_AUDIT_RECORDED", "The original fixed outcome audit was acknowledged.");
                if (_capability is null || _known is null && !_noPublicationKnown)
                    return new(false, "GRAPH_PUBLICATION_OUTCOME_UNCONFIRMED", "No auditable original capability and known effect are retained; do not replay.");
                HomePermissionOperationResult result;
                if (_claimed)
                {
                    result = await _issuer._broker.CompleteExecutionAsync(_capability,
                        new(_known is not null ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                            _known is not null ? "HOME_GRAPH_PUBLISHED" : "HOME_GRAPH_NOT_PUBLISHED",
                            _known is not null ? "The exact canonical graph revision was published." : "This reserved attempt did not publish a graph revision.",
                            _known is not null ? [new("nodegraph.definition", _intent.Graph.GraphId.ToString("D"))] : []), ct).ConfigureAwait(false);
                }
                else
                {
                    // A known no-writer attempt may only finish its original rejection/abort. Unknown/NOT_OWNED
                    // never becomes durable acknowledgement, and no Claim or graph writer is replayed.
                    result = await _issuer._broker.RetryRejectedClaimAuditAsync(_capability, ct).ConfigureAwait(false);
                    if (!result.Succeeded)
                        result = await _issuer._broker.AbortUnclaimedExecutionAsync(_capability, ct).ConfigureAwait(false);
                }
                if (result.Succeeded) _auditRecorded = true;
                return result;
            }
            finally { _gate.Release(); }
        }
        private sealed class PublicationGuard(Review review) : IHomeStateCommitActorGuard
        {
            public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor,
                HomeStateCommitPhase phase, CancellationToken ct) => review.CheckAsync(state, actor, phase, ct);
        }
        private async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor,
            HomeStateCommitPhase phase, CancellationToken ct)
        {
            if (_retired || !_claimed || actor != _intent.OriginalActor || _binding is null || _capability is null || _policy is null ||
                !HomeLocalStoreOwnership.IsReceiptCurrentInState(state, _binding) ||
                !await _issuer._store.IsRawGraphPublicationStateCurrentAsync(state, ct).ConfigureAwait(false) ||
                !await _issuer._profiles.CheckAsync(state, actor, phase, ct).ConfigureAwait(false) ||
                !await _issuer._store.IsRawGraphPublicationStateCurrentAsync(state, ct).ConfigureAwait(false)) return false;
            var records = state.Records.Where(item => item.RecordId == "home.permissions-trust").ToArray();
            if (records.Length != 1 || records[0].RecordType != "home.permissions-trust" || records[0].SchemaVersion != 1 ||
                records[0].Scope != HomeDataScope.DeviceLocal || records[0].Authority != HomeRecordAuthority.LocalCanonical) return false;
            try
            {
                var requests = records[0].Payload.GetProperty("Requests").Deserialize<PermissionRequest[]>();
                var matches = requests?.Where(item => item.RequestId == RequestId).ToArray();
                return matches?.Length == 1 && matches[0].State == HomePermissionRequestState.Executing &&
                    matches[0].Policy == _policy && matches[0].Caller == _prepared.Submission.Caller &&
                    matches[0].SessionId == _prepared.Submission.SessionId &&
                    JsonSerializer.Serialize(matches[0].Scope) == JsonSerializer.Serialize(_prepared.Submission.Scope) &&
                    JsonSerializer.Serialize(matches[0].Impact) == JsonSerializer.Serialize(_prepared.Submission.Impact);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException) { return false; }
        }
    }
}
