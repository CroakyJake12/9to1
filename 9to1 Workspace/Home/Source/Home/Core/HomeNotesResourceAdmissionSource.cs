using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Actual Home ownership and policy admission for the original Notes store, never
/// a path/profile predicate promoted into content permission. An approved save is bound to the
/// original repository's immutable intent, not a mutable editor or raw repository fallback.</summary>
public sealed class HomeNotesResourceAdmissionSource : ICanonicalResourceAccessResolver
{
    public const string TargetAppId = "boards";
    public const string ReadAction = "boards.library.read";
    public const string SaveAction = "boards.document.save";
    public const string StoreKind = "notes";
    public const string ScopeKind = "notes.store";
    private readonly INotesOriginalStoreSource _notes;
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly HomeResourceOperationBroker _broker;

    public HomeNotesResourceAdmissionSource(INotesOriginalStoreSource notes, FileHomeCoreStateStore store,
        HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership,
        ResourceAuthorizationService resources, HomePermissionTrustService permissions, HomeResourceOperationBroker broker)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store) ||
            !ownership.IsBoundTo(store, profiles) || !resources.IsBoundToActorSource(profiles) ||
            !broker.IsBoundToOriginalComposition(resources, permissions))
            throw new UnauthorizedAccessException("Retain the same original Home, actor, ownership and resource broker.");
        _notes = notes; _store = store; _profiles = profiles; _ownership = ownership; _broker = broker;
    }

    public string ResourceKind => ScopeKind;

    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Deny() => new(false, "NotesOriginalStoreNotOwned", actor.ActorId,
            scope.Revision, actor.OrganisationId);
        if (actor.AccountId is not null || actor.OrganisationId is not null ||
            actionId is not (ReadAction or SaveAction) || scope.Kind != ScopeKind ||
            scope.Access != (actionId == ReadAction ? ResourceAccess.Read : ResourceAccess.Write) ||
            !Guid.TryParseExact(scope.Id, "D", out var id) || id == Guid.Empty || scope.Id != id.ToString("D") ||
            await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor) return Deny();
        var observed = await _notes.ObserveOriginalStoreAsync(cancellationToken).ConfigureAwait(false);
        if (!_notes.IsIssuedOriginalStoreObservation(observed) || observed.StoreIdentity.SchemaVersion != 1 ||
            observed.StoreIdentity.StoreId != id || observed.Revision != scope.Revision) return Deny();
        var binding = await _ownership.GetVerifiedAsync(StoreKind, scope.Id, cancellationToken).ConfigureAwait(false);
        if (binding is null || binding.ProfileId != actor.ProfileId || binding.Receipt is null ||
            !await _ownership.IsCurrentAsync(binding, actor, cancellationToken).ConfigureAwait(false) ||
            !await _notes.IsOriginalStoreObservationCurrentAsync(observed, cancellationToken).ConfigureAwait(false) ||
            await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != actor) return Deny();
        return new(true, "NotesOriginalStoreOwned", actor.ActorId, scope.Revision, actor.OrganisationId);
    }

    /// <summary>Requests the genuine broker review after actual ownership resolution. It does
    /// not read documents, consume approval, issue an execution capability or authorise a save.</summary>
    public async Task<HomePermissionAuthorization> RequestLibraryReadAsync(string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var actor = await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original Home actor is unavailable.");
        var observed = await _notes.ObserveOriginalStoreAsync(cancellationToken).ConfigureAwait(false);
        if (!_notes.IsIssuedOriginalStoreObservation(observed))
            throw new UnauthorizedAccessException("The original Notes observation was not issued by this owner.");
        var scope = new ResourceScope(ScopeKind, observed.StoreIdentity.StoreId.ToString("D"),
            observed.Revision, ResourceAccess.Read);
        var decision = await EvaluateAsync(actor, ReadAction, scope, cancellationToken).ConfigureAwait(false);
        if (!decision.Allowed) throw new UnauthorizedAccessException("Set up or explicitly import this original Notes store in Home first.");
        return await _broker.AuthorizeForActorAsync(actor, TargetAppId, ReadAction, [scope],
            JsonSerializer.SerializeToElement(new { storeId = observed.StoreIdentity.StoreId, revision = observed.Revision }),
            "Read this owned local Boards library", null, sessionId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Review the private candidate already detached by this exact canonical owner.
    /// No document is read or published and no approval is inferred from ownership.</summary>
    public async Task<HomeNotesSaveReview> RequestSaveAsync(INotesOriginalSaveIntent intent,
        string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!_notes.IsIssuedOriginalSaveIntent(intent) ||
            !_notes.IsIssuedOriginalStoreObservation(intent.StoreObservation) ||
            !await _notes.IsOriginalStoreObservationCurrentAsync(intent.StoreObservation, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Prepare this candidate with the original Notes owner first.");
        var actor = await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original Home actor is unavailable.");
        var scope = new ResourceScope(ScopeKind, intent.StoreObservation.StoreIdentity.StoreId.ToString("D"),
            intent.StoreObservation.Revision, ResourceAccess.Write);
        if (!(await EvaluateAsync(actor, SaveAction, scope, cancellationToken).ConfigureAwait(false)).Allowed)
            throw new UnauthorizedAccessException("Set up or explicitly import this original Notes store in Home first.");
        var preview = $"Save 1 Boards document.\nDocument: {intent.DocumentId:D}\n" +
            $"Version: {intent.ExpectedVersion} → {checked(intent.ExpectedVersion + 1)}\nReason: {intent.Reason}";
        var authorization = await _broker.AuthorizeForActorAsync(actor, TargetAppId, SaveAction, [scope],
            intent.Arguments, preview, null, sessionId,
            cancellationToken).ConfigureAwait(false);
        return new(this, intent, actor, scope, authorization);
    }

    public Task<HomePermissionAuthorization> GetSaveAuthorizationAsync(HomeNotesSaveReview review,
        CancellationToken cancellationToken = default)
    {
        DemandOriginalReview(review);
        return _broker.GetAuthorizationAsync(review.Authorization.RequestId, cancellationToken);
    }

    /// <summary>Pending reviews remain pending. A real approved request is consumed once and
    /// claimed for its exact original tuple; the returned handle still grants no raw Notes IO.</summary>
    public async Task<HomeNotesSaveAdmission?> BeginSaveAsync(HomeNotesSaveReview review,
        CancellationToken cancellationToken = default)
    {
        DemandOriginalReview(review);
        await review.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (review.Admission is not null) return review.Admission;
            if (review.BeginAttempted) return null;
            if (await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != review.Actor ||
                !await _notes.IsOriginalStoreObservationCurrentAsync(review.Intent.StoreObservation, cancellationToken).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The Home actor or original Notes store changed during review.");
            var authorization = await _broker.GetAuthorizationAsync(review.Authorization.RequestId, cancellationToken).ConfigureAwait(false);
            if (!authorization.IsAllowed) return null;
            review.BeginAttempted = true;
            var capability = await _broker.BeginExecutionCapabilityAsync(review.Authorization.RequestId,
                review.Intent.Arguments, cancellationToken).ConfigureAwait(false);
            if (capability is null) return null;
            review.Capability = capability;
            var claimed = await _broker.ClaimExecutionObservedAsync(capability, TargetAppId, SaveAction,
                [review.Scope], review.Intent.Arguments, cancellationToken).ConfigureAwait(false);
            if (claimed.Disposition != HomeResourceClaimDisposition.Claimed || claimed.Actor != review.Actor) return null;
            return review.Admission = new(_notes, review.Intent, _store, _profiles, _ownership,
                _broker, review.Actor, capability);
        }
        finally { review.Gate.Release(); }
    }

    /// <summary>Only the actual failed dispatch audit is retried. This cannot invoke Notes or
    /// create another review, capability, execution claim or publication attempt.</summary>
    public Task<HomePermissionOperationResult> RetrySaveDispatchAuditAsync(HomeNotesSaveReview review,
        CancellationToken cancellationToken = default)
    {
        DemandOriginalReview(review);
        if (!review.BeginAttempted || review.Admission is not null)
            return Task.FromResult(new HomePermissionOperationResult(false, "NOTES_DISPATCH_AUDIT_NOT_OWNED",
                "No failed original Notes dispatch audit is retained."));
        return review.Capability is { } capability
            ? _broker.RetryRejectedClaimAuditAsync(capability, cancellationToken)
            : _broker.RetryRejectedBeginAuditAsync(review.Authorization.RequestId, cancellationToken);
    }

    /// <summary>Reviews a private metadata-only library request. This never calls the raw
    /// Notes List/Load recovery paths or reads document content before genuine Home consent.</summary>
    public async Task<HomeNotesLibraryReadReview> RequestLibrarySnapshotAsync(INotesOriginalLibraryReadRequest request,
        string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!_notes.IsIssuedOriginalLibraryReadRequest(request) ||
            !_notes.IsIssuedOriginalStoreObservation(request.StoreObservation) ||
            !await _notes.IsOriginalStoreObservationCurrentAsync(request.StoreObservation, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Prepare this library request with the original Notes owner first.");
        var actor = await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original Home actor is unavailable.");
        var scope = new ResourceScope(ScopeKind, request.StoreObservation.StoreIdentity.StoreId.ToString("D"),
            request.StoreObservation.Revision, ResourceAccess.Read);
        if (!(await EvaluateAsync(actor, ReadAction, scope, cancellationToken).ConfigureAwait(false)).Allowed)
            throw new UnauthorizedAccessException("Set up or explicitly import this original Notes store in Home first.");
        var authorization = await _broker.AuthorizeForActorAsync(actor, TargetAppId, ReadAction, [scope],
            request.Arguments, request.DocumentId is { } selected
                ? $"Read this Boards library's summaries and document {selected:D}."
                : "Read this Boards library's summaries without requesting a document body.",
            null, sessionId, cancellationToken).ConfigureAwait(false);
        return new(this, request, actor, scope, authorization);
    }

    public Task<HomePermissionAuthorization> GetLibrarySnapshotAuthorizationAsync(HomeNotesLibraryReadReview review,
        CancellationToken cancellationToken = default)
    {
        DemandOriginalReadReview(review);
        return _broker.GetAuthorizationAsync(review.Authorization.RequestId, cancellationToken);
    }

    public async Task<HomeNotesLibraryReadAdmission?> BeginLibrarySnapshotAsync(HomeNotesLibraryReadReview review,
        CancellationToken cancellationToken = default)
    {
        DemandOriginalReadReview(review);
        await review.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (review.Admission is not null) return review.Admission;
            if (review.BeginAttempted) return null;
            if (await _profiles.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != review.Actor ||
                !await _notes.IsOriginalStoreObservationCurrentAsync(review.Request.StoreObservation, cancellationToken).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The Home actor or original Notes store changed during review.");
            var authorization = await _broker.GetAuthorizationAsync(review.Authorization.RequestId, cancellationToken).ConfigureAwait(false);
            if (!authorization.IsAllowed) return null;
            review.BeginAttempted = true;
            var capability = await _broker.BeginExecutionCapabilityAsync(review.Authorization.RequestId,
                review.Request.Arguments, cancellationToken).ConfigureAwait(false);
            if (capability is null) return null;
            review.Capability = capability;
            var claimed = await _broker.ClaimExecutionObservedAsync(capability, TargetAppId, ReadAction,
                [review.Scope], review.Request.Arguments, cancellationToken).ConfigureAwait(false);
            if (claimed.Disposition != HomeResourceClaimDisposition.Claimed || claimed.Actor != review.Actor) return null;
            return review.Admission = new(_notes, review.Request, _store, _profiles, _ownership,
                _broker, review.Actor, capability);
        }
        finally { review.Gate.Release(); }
    }

    public Task<HomePermissionOperationResult> RetryLibrarySnapshotDispatchAuditAsync(HomeNotesLibraryReadReview review,
        CancellationToken cancellationToken = default)
    {
        DemandOriginalReadReview(review);
        if (!review.BeginAttempted || review.Admission is not null)
            return Task.FromResult(new HomePermissionOperationResult(false, "NOTES_READ_DISPATCH_AUDIT_NOT_OWNED",
                "No failed original Notes read dispatch audit is retained."));
        return review.Capability is { } capability
            ? _broker.RetryRejectedClaimAuditAsync(capability, cancellationToken)
            : _broker.RetryRejectedBeginAuditAsync(review.Authorization.RequestId, cancellationToken);
    }

    private void DemandOriginalReadReview(HomeNotesLibraryReadReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!ReferenceEquals(review.Issuer, this) || !_notes.IsIssuedOriginalLibraryReadRequest(review.Request))
            throw new UnauthorizedAccessException("The Notes library review belongs to a different original owner.");
    }

    private void DemandOriginalReview(HomeNotesSaveReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (!ReferenceEquals(review.Issuer, this) || !_notes.IsIssuedOriginalSaveIntent(review.Intent))
            throw new UnauthorizedAccessException("The Notes review belongs to a different original owner.");
    }
}

/// <summary>Non-serializable original Home review. Public authorization describes actual Home
/// state; copying it does not create this retained review or an execution admission.</summary>
public sealed class HomeNotesSaveReview
{
    internal HomeNotesSaveReview(HomeNotesResourceAdmissionSource issuer, INotesOriginalSaveIntent intent,
        AuthenticatedResourceActor actor, ResourceScope scope, HomePermissionAuthorization authorization)
    { Issuer = issuer; Intent = intent; Actor = actor; Scope = scope; Authorization = authorization; }
    internal HomeNotesResourceAdmissionSource Issuer { get; }
    internal INotesOriginalSaveIntent Intent { get; }
    internal AuthenticatedResourceActor Actor { get; }
    internal ResourceScope Scope { get; }
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal bool BeginAttempted { get; set; }
    internal HomeResourceExecutionCapability? Capability { get; set; }
    internal HomeNotesSaveAdmission? Admission { get; set; }
    public HomePermissionAuthorization Authorization { get; }
}

/// <summary>One exact claimed Home save, usable only by the original repository and intent.
/// Capture precedes the owner transaction; validation happens inside it. The owner releases the
/// fence before submitting its private observed outcome. No caller supplies validation callbacks.</summary>
public sealed class HomeNotesSaveAdmission
{
    private readonly INotesOriginalStoreSource _source;
    private readonly INotesOriginalSaveIntent _intent;
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly HomeResourceOperationBroker _broker;
    private readonly AuthenticatedResourceActor _actor;
    private readonly HomeResourceExecutionCapability _capability;
    private readonly SemaphoreSlim _completionGate = new(1, 1);
    private INotesOriginalSaveOutcome? _retainedOutcome;
    private int _captureAttempted;

    internal HomeNotesSaveAdmission(INotesOriginalStoreSource source, INotesOriginalSaveIntent intent,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, HomeResourceOperationBroker broker,
        AuthenticatedResourceActor actor, HomeResourceExecutionCapability capability)
    { _source = source; _intent = intent; _store = store; _profiles = profiles; _ownership = ownership;
        _broker = broker; _actor = actor; _capability = capability; }

    public bool IsBoundToOriginalNotes(INotesOriginalStoreSource source, INotesOriginalSaveIntent intent) =>
        ReferenceEquals(_source, source) && ReferenceEquals(_intent, intent) &&
        source.IsIssuedOriginalSaveIntent(intent) && _broker.CaptureClaimedAttestation(_capability) is not null;

    public ValueTask<HomeClaimedResourceCommitFence?> CaptureCommitFenceAsync(INotesOriginalStoreSource source,
        INotesOriginalSaveIntent intent, CancellationToken cancellationToken = default)
    {
        if (!IsBoundToOriginalNotes(source, intent) || Interlocked.CompareExchange(ref _captureAttempted, 1, 0) != 0)
            return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
        return HomeClaimedResourceCommitFence.CaptureNotesSaveAsync(_broker, _store, _profiles,
            _ownership, source, intent, _capability, _actor, cancellationToken);
    }

    public async Task<HomePermissionOperationResult> CompleteObservedAsync(INotesOriginalStoreSource source,
        INotesOriginalSaveOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(outcome);
        if (!ReferenceEquals(source, _source) || !ReferenceEquals(outcome.Intent, _intent) ||
            !source.IsIssuedOriginalSaveOutcome(outcome, _intent))
            return new(false, "NOTES_OUTCOME_NOT_OWNED", "Only the original repository's observed save outcome can complete this claim.");
        await _completionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_retainedOutcome is not null && !ReferenceEquals(_retainedOutcome, outcome))
                return new(false, "NOTES_OUTCOME_ALREADY_RETAINED", "Retry only the first original observed outcome audit.");
            if (!ValidObservedOutcome(outcome))
                return new(false, "NOTES_OUTCOME_INVALID", "The original observed outcome has an invalid shape.");
            _retainedOutcome ??= outcome;
            var affected = new[] { new HomeObjectReference("notes.document", _intent.DocumentId.ToString("D")) };
            var terminal = outcome.Disposition switch
            {
                NotesOriginalSaveDisposition.Committed => new HomeExecutionOutcome(HomePermissionRequestState.Succeeded,
                    "NOTES_SAVE_COMMITTED", "The original Notes owner observed its reviewed candidate committed.", affected),
                NotesOriginalSaveDisposition.Refused => new HomeExecutionOutcome(HomePermissionRequestState.Failed,
                    "NOTES_SAVE_REFUSED", "The original Notes owner refused publication before a confirmed effect.", []),
                _ => new HomeExecutionOutcome(HomePermissionRequestState.PartiallyCompleted,
                    "NOTES_SAVE_UNCERTAIN", "The original Notes publication outcome is uncertain; preserve recovery and do not repeat the mutation.", affected)
            };
            return await _broker.CompleteExecutionAsync(_capability, terminal, cancellationToken).ConfigureAwait(false);
        }
        finally { _completionGate.Release(); }
    }

    public async Task<HomePermissionOperationResult> RetryCompletionAuditAsync(INotesOriginalStoreSource source,
        CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(source, _source))
            return new(false, "NOTES_OUTCOME_NOT_OWNED", "The original Notes owner is required.");
        await _completionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _retainedOutcome is null
                ? new(false, "NOTES_OUTCOME_NOT_RETAINED", "No original observed outcome is retained for audit retry.")
                : await _broker.RetryCompletionAuditAsync(_capability, cancellationToken).ConfigureAwait(false);
        }
        finally { _completionGate.Release(); }
    }

    private bool ValidObservedOutcome(INotesOriginalSaveOutcome outcome) =>
        outcome.Disposition switch
        {
            NotesOriginalSaveDisposition.Committed => _intent.ExpectedVersion < long.MaxValue &&
                outcome.CommittedVersion == _intent.ExpectedVersion + 1 && IsSha256(outcome.CommittedSha256),
            NotesOriginalSaveDisposition.Refused or NotesOriginalSaveDisposition.Uncertain =>
                outcome.CommittedVersion is null && outcome.CommittedSha256 is null,
            _ => false
        };

    private static bool IsSha256(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}

/// <summary>Lazy composition breaks the registered-resource/broker constructor cycle. The
/// original host must provide the same retained owner; no request supplies this resolver.</summary>
public sealed class HomeNotesResourceResolver(Func<HomeNotesResourceAdmissionSource> original) : ICanonicalResourceAccessResolver
{
    public string ResourceKind => HomeNotesResourceAdmissionSource.ScopeKind;
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken) => original().EvaluateAsync(actor, actionId, scope, cancellationToken);
}

/// <summary>One private original Home read review. Authorization fields are observations,
/// never a serializable library-read capability.</summary>
public sealed class HomeNotesLibraryReadReview
{
    internal HomeNotesLibraryReadReview(HomeNotesResourceAdmissionSource issuer, INotesOriginalLibraryReadRequest request,
        AuthenticatedResourceActor actor, ResourceScope scope, HomePermissionAuthorization authorization)
    { Issuer = issuer; Request = request; Actor = actor; Scope = scope; Authorization = authorization; }
    internal HomeNotesResourceAdmissionSource Issuer { get; }
    internal INotesOriginalLibraryReadRequest Request { get; }
    internal AuthenticatedResourceActor Actor { get; }
    internal ResourceScope Scope { get; }
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal bool BeginAttempted { get; set; }
    internal HomeResourceExecutionCapability? Capability { get; set; }
    internal HomeNotesLibraryReadAdmission? Admission { get; set; }
    public HomePermissionAuthorization Authorization { get; }
}

/// <summary>Exact original Home read claim. The repository must acquire its canonical
/// transaction, validate the returned fence, perform only protected nonrecovering reads and
/// release the fence before submitting the actual private owner outcome.</summary>
public sealed class HomeNotesLibraryReadAdmission
{
    private readonly INotesOriginalStoreSource _source;
    private readonly INotesOriginalLibraryReadRequest _request;
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly HomeResourceOperationBroker _broker;
    private readonly AuthenticatedResourceActor _actor;
    private readonly HomeResourceExecutionCapability _capability;
    private readonly SemaphoreSlim _completionGate = new(1, 1);
    private INotesOriginalLibraryReadOutcome? _retainedOutcome;
    private int _captureAttempted;
    private VerifiedResourceStoreOwnership? _observedOwnership;
    private HomeClaimedResourceAttestation? _observedAdmission;
    private int _completionAudited;

    internal HomeNotesLibraryReadAdmission(INotesOriginalStoreSource source, INotesOriginalLibraryReadRequest request,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership,
        HomeResourceOperationBroker broker, AuthenticatedResourceActor actor, HomeResourceExecutionCapability capability)
    { _source = source; _request = request; _store = store; _profiles = profiles; _ownership = ownership;
        _broker = broker; _actor = actor; _capability = capability; }

    public bool IsBoundToOriginalNotes(INotesOriginalStoreSource source, INotesOriginalLibraryReadRequest request) =>
        ReferenceEquals(_source, source) && ReferenceEquals(_request, request) &&
        source.IsIssuedOriginalLibraryReadRequest(request) && _broker.CaptureClaimedAttestation(_capability) is not null;

    public async ValueTask<HomeClaimedResourceCommitFence?> CaptureCommitFenceAsync(INotesOriginalStoreSource source,
        INotesOriginalLibraryReadRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsBoundToOriginalNotes(source, request) || Interlocked.CompareExchange(ref _captureAttempted, 1, 0) != 0)
            return null;
        _observedAdmission = _broker.CaptureClaimedAttestation(_capability);
        _observedOwnership = await _ownership.GetVerifiedAsync(HomeNotesResourceAdmissionSource.StoreKind,
            request.StoreObservation.StoreIdentity.StoreId.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (_observedAdmission is null || _observedOwnership?.Receipt is null ||
            !await _ownership.IsCurrentAsync(_observedOwnership, _actor, cancellationToken).ConfigureAwait(false)) return null;
        return await HomeClaimedResourceCommitFence.CaptureNotesLibraryReadAsync(_broker, _store, _profiles,
            _ownership, source, request, _capability, _actor, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Narrows presentation of this completed read's already-returned metadata. This
    /// is not a read execution capability and cannot refresh content or authorise mutation.
    /// The canonical Notes owner acquires its own lock first and disposes this actual Home
    /// lease before diagnostics. Original actor, ownership receipt and completed review remain required.</summary>
    public ValueTask<IHomeLocalOperationLease?> CaptureObservedSnapshotLeaseAsync(INotesOriginalStoreSource source,
        INotesOriginalLibraryReadRequest request, INotesOriginalLibraryReadOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(source, _source) || !ReferenceEquals(request, _request) ||
            !ReferenceEquals(outcome, _retainedOutcome) || !outcome.Succeeded ||
            !source.IsIssuedOriginalLibraryReadOutcome(outcome, request) || Volatile.Read(ref _completionAudited) != 1 ||
            _observedAdmission is null || _observedOwnership?.Receipt is null)
            return ValueTask.FromResult<IHomeLocalOperationLease?>(null);
        return _store.AcquireLocalOperationLeaseCoreAsync(_profiles, _actor, new ObservedSnapshotGuard(this), cancellationToken);
    }

    private sealed class ObservedSnapshotGuard(HomeNotesLibraryReadAdmission original) : IHomeStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor,
            HomeStateCommitPhase phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(actor == original._actor && original._observedOwnership is { } ownership &&
                HomeLocalStoreOwnership.IsReceiptCurrentInState(state, ownership) && original._observedAdmission is { } admission &&
                original._broker.IsObservedNotesReadCurrentInState(admission, actor, state));
        }
    }

    public async Task<HomePermissionOperationResult> CompleteObservedAsync(INotesOriginalStoreSource source,
        INotesOriginalLibraryReadOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(outcome);
        if (!ReferenceEquals(source, _source) || !ReferenceEquals(outcome.Request, _request) ||
            !source.IsIssuedOriginalLibraryReadOutcome(outcome, _request))
            return new(false, "NOTES_READ_OUTCOME_NOT_OWNED", "Only the original repository's observed library read outcome can complete this claim.");
        await _completionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_retainedOutcome is not null && !ReferenceEquals(_retainedOutcome, outcome))
                return new(false, "NOTES_READ_OUTCOME_ALREADY_RETAINED", "Retry only the first original observed read outcome audit.");
            if (outcome.DocumentCount < 0 || (outcome.DocumentWasRead && _request.DocumentId is null) ||
                (outcome.Succeeded ? !IsSha256(outcome.SnapshotSha256) :
                    outcome.DocumentCount != 0 || outcome.DocumentWasRead || outcome.SnapshotSha256 is not null))
                return new(false, "NOTES_READ_OUTCOME_INVALID", "The original observed library read outcome has an invalid shape.");
            _retainedOutcome ??= outcome;
            HomeObjectReference[] affected = outcome.Succeeded ? new[] { new HomeObjectReference(HomeNotesResourceAdmissionSource.ScopeKind,
                _request.StoreObservation.StoreIdentity.StoreId.ToString("D")) } : [];
            var terminal = outcome.Succeeded
                ? new HomeExecutionOutcome(HomePermissionRequestState.Succeeded, "NOTES_READ_COMPLETED",
                    "The original Notes owner observed its reviewed library snapshot without recovery writes.", affected)
                : new HomeExecutionOutcome(HomePermissionRequestState.Failed, "NOTES_READ_REFUSED",
                    "The original Notes owner did not return a library snapshot; any recovery needs separate authority.", []);
            var completed = await _broker.CompleteExecutionAsync(_capability, terminal, cancellationToken).ConfigureAwait(false);
            if (completed.Succeeded) Volatile.Write(ref _completionAudited, 1);
            return completed;
        }
        finally { _completionGate.Release(); }
    }

    public async Task<HomePermissionOperationResult> RetryCompletionAuditAsync(INotesOriginalStoreSource source,
        CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(source, _source))
            return new(false, "NOTES_READ_OUTCOME_NOT_OWNED", "The original Notes owner is required.");
        await _completionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_retainedOutcome is null)
                return new(false, "NOTES_READ_OUTCOME_NOT_RETAINED", "No original observed library read outcome is retained for audit retry.");
            var completed = await _broker.RetryCompletionAuditAsync(_capability, cancellationToken).ConfigureAwait(false);
            if (completed.Succeeded) Volatile.Write(ref _completionAudited, 1);
            return completed;
        }
        finally { _completionGate.Release(); }
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}

// SAME actual broker, original attestation and raw Home state. A completed read is never
// reclassified as an uncompleted execution capability or used to admit new content IO.
public sealed partial class HomeResourceOperationBroker
{
    internal bool IsObservedNotesReadCurrentInState(HomeClaimedResourceAttestation original,
        AuthenticatedResourceActor actor, HomeCoreStoredState actualState)
    {
        if (!ReferenceEquals(original.Issuer, this) || original.Actor != actor ||
            original.Submission.Scope.TargetAppId != HomeNotesResourceAdmissionSource.TargetAppId ||
            original.Submission.Scope.ActionName != HomeNotesResourceAdmissionSource.ReadAction) return false;
        var records = actualState.Records.Where(value => value.RecordId == "home.permissions-trust").ToArray();
        if (records.Length != 1 || records[0].RecordType != "home.permissions-trust" || records[0].SchemaVersion != 1 ||
            records[0].Scope != HomeDataScope.DeviceLocal || records[0].Authority != HomeRecordAuthority.LocalCanonical) return false;
        try
        {
            foreach (var id in records[0].Payload.GetProperty("BlockedCallerIds").EnumerateArray())
                if (id.GetString() == original.Submission.Caller.CallerId) return false;
            HomePermissionRequest? observed = null;
            foreach (var item in records[0].Payload.GetProperty("Requests").EnumerateArray())
            {
                if (!item.TryGetProperty(nameof(HomePermissionRequest.RequestId), out var id) ||
                    id.GetString() != original.Capability.RequestId) continue;
                if (observed is not null) return false;
                observed = item.Deserialize<HomePermissionRequest>();
                if (observed is null) return false;
            }
            return observed is not null && observed.State == HomePermissionRequestState.Succeeded &&
                observed.ResultCode == "NOTES_READ_COMPLETED" && observed.Policy == original.Policy &&
                observed.Policy.RequiresPerActionApproval && observed.AppliedGrantId is null &&
                observed.AppliedTrustLevel == HomeTrustLevel.Session && MatchesPrepared(original.Submission, observed) &&
                original.Submission.Impact.ResourceBinding is { SchemaVersion: 1 } resource &&
                resource.OriginalActor == actor && resource.Scopes.SequenceEqual(original.Capability.Scopes);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return false; }
    }
}
