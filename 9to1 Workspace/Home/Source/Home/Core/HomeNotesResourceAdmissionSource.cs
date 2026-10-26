using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
using PermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;
using PermissionTrustLevel = HavenOS.Home.PermissionsTrustNotifications.HomeTrustLevel;

namespace HavenOS.Home.Core;

/// <summary>Actual Home ownership and policy admission for the original Notes store, never
/// a path/profile predicate promoted into content permission. An approved save is bound to the
/// original repository's immutable intent, not a mutable editor or raw repository fallback.</summary>
public sealed class HomeNotesResourceAdmissionSource : ICanonicalResourceAccessResolver, IOriginalScopedCanonicalResourceAccessResolver
{
    public const string TargetAppId = "boards";
    public const string ReadAction = "boards.library.read";
    public const string SaveAction = "boards.document.save";
    public const string WriteTargetAppId = "write";
    public const string WriteLibraryReadAction = "write.library.read";
    public const string WriteReadAction = "write.document.read";
    public const string WriteSaveAction = "write.document.save";
    public const string StoreKind = "notes";
    public const string ScopeKind = "notes.store";
    private readonly INotesOriginalStoreSource _notes;
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly HomeResourceOperationBroker _broker;
    private readonly HomePermissionTrustService _permissions;

    public HomeNotesResourceAdmissionSource(INotesOriginalStoreSource notes, FileHomeCoreStateStore store,
        HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership,
        ResourceAuthorizationService resources, HomePermissionTrustService permissions, HomeResourceOperationBroker broker)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store) ||
            !ownership.IsBoundTo(store, profiles) || !resources.IsBoundToActorSource(profiles) ||
            !broker.IsBoundToOriginalComposition(resources, permissions))
            throw new UnauthorizedAccessException("Retain the same original Home, actor, ownership and resource broker.");
        _notes = notes; _store = store; _profiles = profiles; _ownership = ownership; _broker = broker; _permissions = permissions;
    }

    public string ResourceKind => ScopeKind;

    /// <summary>Composition identity only. It grants no ownership, approval or Notes IO.</summary>
    public bool IsBoundToOriginalNotes(INotesOriginalStoreSource source) => ReferenceEquals(_notes, source);

    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope, CancellationToken cancellationToken) => new(EvaluateCoreAsync(null, actor, actionId, scope, cancellationToken));

    public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope,
        Action<Action> originalScope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        HomeNotesOriginalSource.RunAsync(originalScope, retain, source => EvaluateCoreAsync(source, actor, actionId, scope, cancellationToken));

    private async Task<ResourceAccessDecision> EvaluateCoreAsync(HomeNotesOriginalSource? source, AuthenticatedResourceActor actor,
        string actionId, ResourceScope scope, CancellationToken cancellationToken)
    {
        ResourceAccessDecision Deny() => new(false, "NotesOriginalStoreNotOwned", actor.ActorId,
            scope.Revision, actor.OrganisationId);
        var expectedAccess = actionId switch
        {
            ReadAction or WriteLibraryReadAction or WriteReadAction => ResourceAccess.Read,
            SaveAction or WriteSaveAction => ResourceAccess.Write,
            _ => (ResourceAccess?)null,
        };
        if (actor.AccountId is not null || actor.OrganisationId is not null || expectedAccess is null || scope.Kind != ScopeKind ||
            scope.Access != expectedAccess ||
            !Guid.TryParseExact(scope.Id, "D", out var id) || id == Guid.Empty || scope.Id != id.ToString("D") ||
            await ReadActorAsync(source, cancellationToken).ConfigureAwait(false) != actor) return Deny();
        var observed = await ObserveStoreAsync(source, cancellationToken).ConfigureAwait(false);
        if (!_notes.IsIssuedOriginalStoreObservation(observed) || observed.StoreIdentity.SchemaVersion != 1 ||
            observed.StoreIdentity.StoreId != id || observed.Revision != scope.Revision) return Deny();
        var binding = await ReadOwnershipAsync(source, scope.Id, cancellationToken).ConfigureAwait(false);
        if (binding is null || binding.ProfileId != actor.ProfileId || binding.Receipt is null ||
            !await IsOwnershipCurrentAsync(source, binding, actor, cancellationToken).ConfigureAwait(false) ||
            !await IsStoreCurrentAsync(source, observed, cancellationToken).ConfigureAwait(false) ||
            await ReadActorAsync(source, cancellationToken).ConfigureAwait(false) != actor) return Deny();
        return new(true, "NotesOriginalStoreOwned", actor.ActorId, scope.Revision, actor.OrganisationId);
    }

    /// <summary>Requests the genuine broker review after actual ownership resolution. It does
    /// not read documents, consume approval, issue an execution capability or authorise a save.</summary>
    public Task<HomePermissionAuthorization> RequestLibraryReadAsync(string sessionId,
        CancellationToken cancellationToken = default) => RequestLibraryReadCoreAsync(null, sessionId, cancellationToken);

    public Task<HomePermissionAuthorization> RequestLibraryReadWithinOriginalSourceAsync(string sessionId,
        Action<Action> originalScope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        HomeNotesOriginalSource.RunAsync(originalScope, retain, source => RequestLibraryReadCoreAsync(source, sessionId, cancellationToken));

    private async Task<HomePermissionAuthorization> RequestLibraryReadCoreAsync(HomeNotesOriginalSource? source, string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var actor = await ReadActorAsync(source, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original Home actor is unavailable.");
        var observed = await ObserveStoreAsync(source, cancellationToken).ConfigureAwait(false);
        if (!_notes.IsIssuedOriginalStoreObservation(observed))
            throw new UnauthorizedAccessException("The original Notes observation was not issued by this owner.");
        var scope = new ResourceScope(ScopeKind, observed.StoreIdentity.StoreId.ToString("D"),
            observed.Revision, ResourceAccess.Read);
        var decision = await EvaluateCoreAsync(source, actor, ReadAction, scope, cancellationToken).ConfigureAwait(false);
        if (!decision.Allowed) throw new UnauthorizedAccessException("Set up or explicitly import this original Notes store in Home first.");
        return await AuthorizeReviewAsync(source, actor, ReadAction, [scope],
            JsonSerializer.SerializeToElement(new { storeId = observed.StoreIdentity.StoreId, revision = observed.Revision }),
            "Read this owned local Boards library", null, sessionId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Review the private candidate already detached by this exact canonical owner.
    /// No document is read or published and no approval is inferred from ownership.</summary>
    public Task<HomeNotesSaveReview> RequestSaveAsync(INotesOriginalSaveIntent intent,
        string sessionId, CancellationToken cancellationToken = default) => RequestSaveCoreAsync(null, intent, sessionId,
            TargetAppId, SaveAction, "Boards", cancellationToken);

    public Task<HomeNotesSaveReview> RequestSaveWithinOriginalSourceAsync(INotesOriginalSaveIntent intent,
        string sessionId,
        Action<Action> originalScope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        HomeNotesOriginalSource.RunAsync(originalScope, retain, source => RequestSaveCoreAsync(source, intent, sessionId,
            TargetAppId, SaveAction, "Boards", cancellationToken));

    public Task<HomeNotesSaveReview> RequestWriteDocumentSaveAsync(INotesOriginalSaveIntent intent,
        string sessionId, CancellationToken cancellationToken = default) => RequestSaveCoreAsync(null, intent, sessionId,
            WriteTargetAppId, WriteSaveAction, "Write", cancellationToken);

    public Task<HomeNotesSaveReview> RequestWriteDocumentSaveWithinOriginalSourceAsync(INotesOriginalSaveIntent intent,
        string sessionId, Action<Action> originalScope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        HomeNotesOriginalSource.RunAsync(originalScope, retain, source => RequestSaveCoreAsync(source, intent, sessionId,
            WriteTargetAppId, WriteSaveAction, "Write", cancellationToken));

    private async Task<HomeNotesSaveReview> RequestSaveCoreAsync(HomeNotesOriginalSource? source, INotesOriginalSaveIntent intent,
        string sessionId, string targetAppId, string actionId, string artifactName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!_notes.IsIssuedOriginalSaveIntent(intent) ||
            !_notes.IsIssuedOriginalStoreObservation(intent.StoreObservation) ||
            !await IsStoreCurrentAsync(source, intent.StoreObservation, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Prepare this candidate with the original Notes owner first.");
        var actor = await ReadActorAsync(source, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original Home actor is unavailable.");
        var scope = new ResourceScope(ScopeKind, intent.StoreObservation.StoreIdentity.StoreId.ToString("D"),
            intent.StoreObservation.Revision, ResourceAccess.Write);
        if (!(await EvaluateCoreAsync(source, actor, actionId, scope, cancellationToken).ConfigureAwait(false)).Allowed)
            throw new UnauthorizedAccessException("Set up or explicitly import this original Notes store in Home first.");
        var preview = $"Save 1 {artifactName} document.\nDocument: {intent.DocumentId:D}\n" +
            $"Version: {intent.ExpectedVersion} → {checked(intent.ExpectedVersion + 1)}\nReason: {intent.Reason}";
        var authorization = await AuthorizeReviewAsync(source, actor, actionId, [scope],
            intent.Arguments, preview, null, sessionId,
            cancellationToken, targetAppId).ConfigureAwait(false);
        return new(this, intent, actor, scope, authorization, targetAppId, actionId);
    }

    public Task<HomePermissionAuthorization> GetSaveAuthorizationAsync(HomeNotesSaveReview review,
        CancellationToken cancellationToken = default)
    {
        DemandOriginalReview(review);
        return ReadAuthorizationAsync(null, review.Authorization.RequestId, cancellationToken);
    }

    /// <summary>Pending reviews remain pending. A real approved request is consumed once and
    /// claimed for its exact original tuple; the returned handle still grants no raw Notes IO.</summary>
    public Task<HomeNotesSaveAdmission?> BeginSaveAsync(HomeNotesSaveReview review,
        CancellationToken cancellationToken = default) => BeginSaveCoreAsync(null, review, cancellationToken);

    public Task<HomeNotesSaveAdmission?> BeginSaveWithinOriginalSourceAsync(HomeNotesSaveReview review,
        Action<Action> originalScope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        HomeNotesOriginalSource.RunAsync(originalScope, retain, source => BeginSaveCoreAsync(source, review, cancellationToken));

    private async Task<HomeNotesSaveAdmission?> BeginSaveCoreAsync(HomeNotesOriginalSource? source, HomeNotesSaveReview review,
        CancellationToken cancellationToken)
    {
        DemandOriginalReview(review);
        await WaitGateAsync(source, review.Gate, cancellationToken).ConfigureAwait(false);
        try
        {
            if (review.Admission is not null) return review.Admission;
            if (review.BeginAttempted) return null;
            if (await ReadActorAsync(source, cancellationToken).ConfigureAwait(false) != review.Actor ||
                !await IsStoreCurrentAsync(source, review.Intent.StoreObservation, cancellationToken).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The Home actor or original Notes store changed during review.");
            var authorization = await ReadAuthorizationAsync(source, review.Authorization.RequestId, cancellationToken).ConfigureAwait(false);
            if (!authorization.IsAllowed) return null;
            review.BeginAttempted = true;
            var capability = await BeginCapabilityAsync(source, review.Authorization.RequestId,
                review.Intent.Arguments, cancellationToken).ConfigureAwait(false);
            if (capability is null) return null;
            review.Capability = capability;
            var claimed = await ClaimAsync(source, capability, review.ActionId,
                [review.Scope], review.Intent.Arguments, cancellationToken, review.TargetAppId).ConfigureAwait(false);
            if (claimed.Disposition != HomeResourceClaimDisposition.Claimed || claimed.Actor != review.Actor) return null;
            return review.Admission = new(_notes, review.Intent, _store, _profiles, _ownership,
                _broker, review.Actor, capability, review.TargetAppId, review.ActionId,
                source?.OriginalScope, source?.OriginalRetain);
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
    public Task<HomeNotesLibraryReadReview> RequestLibrarySnapshotAsync(INotesOriginalLibraryReadRequest request,
        string sessionId, CancellationToken cancellationToken = default) => RequestLibrarySnapshotCoreAsync(null, request, sessionId,
            TargetAppId, ReadAction, null, null, cancellationToken);

    public Task<HomeNotesLibraryReadReview> RequestLibrarySnapshotWithinOriginalSourceAsync(INotesOriginalLibraryReadRequest request,
        string sessionId,
        Action<Action> originalScope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        HomeNotesOriginalSource.RunAsync(originalScope, retain, source => RequestLibrarySnapshotCoreAsync(source, request, sessionId,
            TargetAppId, ReadAction, null, null, cancellationToken));

    public Task<HomeNotesLibraryReadReview> RequestWriteDocumentReadAsync(INotesOriginalLibraryReadRequest request,
        long expectedVersion, string sessionId, CancellationToken cancellationToken = default) =>
        RequestWriteDocumentReadCoreAsync(null, request, expectedVersion, sessionId, cancellationToken);

    public Task<HomeNotesLibraryReadReview> RequestWriteDocumentReadWithinOriginalSourceAsync(
        INotesOriginalLibraryReadRequest request, long expectedVersion, string sessionId,
        Action<Action> originalScope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        HomeNotesOriginalSource.RunAsync(originalScope, retain, source =>
            RequestWriteDocumentReadCoreAsync(source, request, expectedVersion, sessionId, cancellationToken));

    private Task<HomeNotesLibraryReadReview> RequestWriteDocumentReadCoreAsync(HomeNotesOriginalSource? source,
        INotesOriginalLibraryReadRequest request, long expectedVersion, string sessionId, CancellationToken token)
    {
        if (expectedVersion < 0 || request.DocumentId is null || request.Kind != NotesOriginalReadKind.LibrarySnapshot ||
            request.VersionId is not null)
            throw new UnauthorizedAccessException("An embedded Write read requires one exact Write document and expected version.");
        var preview = $"Read Write document {request.DocumentId.Value:D} at version {expectedVersion}.";
        var arguments = JsonSerializer.SerializeToElement(new { request = request.Arguments, expectedVersion });
        return RequestLibrarySnapshotCoreAsync(source, request, sessionId, WriteTargetAppId, WriteReadAction,
            preview, arguments, token);
    }

    public Task<HomeNotesLibraryReadReview> RequestWriteLibrarySnapshotAsync(
        INotesOriginalLibraryReadRequest request, string sessionId, CancellationToken cancellationToken = default) =>
        RequestWriteLibrarySnapshotCoreAsync(null, request, sessionId, cancellationToken);

    public Task<HomeNotesLibraryReadReview> RequestWriteLibrarySnapshotWithinOriginalSourceAsync(
        INotesOriginalLibraryReadRequest request, string sessionId, Action<Action> originalScope,
        Action<Task> retain, CancellationToken cancellationToken = default) =>
        HomeNotesOriginalSource.RunAsync(originalScope, retain, source =>
            RequestWriteLibrarySnapshotCoreAsync(source, request, sessionId, cancellationToken));

    private Task<HomeNotesLibraryReadReview> RequestWriteLibrarySnapshotCoreAsync(HomeNotesOriginalSource? source,
        INotesOriginalLibraryReadRequest request, string sessionId, CancellationToken token)
    {
        if (request.DocumentId is not null || request.Kind != NotesOriginalReadKind.LibrarySnapshot || request.VersionId is not null)
            throw new UnauthorizedAccessException("A Write catalogue read may request metadata only, never a document body or saved version.");
        return RequestLibrarySnapshotCoreAsync(source, request, sessionId, WriteTargetAppId,
            WriteLibraryReadAction, "Read authorised Write document summaries without requesting any document body.",
            null, token);
    }

    private async Task<HomeNotesLibraryReadReview> RequestLibrarySnapshotCoreAsync(HomeNotesOriginalSource? source, INotesOriginalLibraryReadRequest request,
        string sessionId, string targetAppId, string actionId, string? previewOverride, JsonElement? argumentsOverride,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (!_notes.IsIssuedOriginalLibraryReadRequest(request) ||
            !_notes.IsIssuedOriginalStoreObservation(request.StoreObservation) ||
            !await IsStoreCurrentAsync(source, request.StoreObservation, cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Prepare this library request with the original Notes owner first.");
        var preview = previewOverride ?? DescribeOriginalLibraryRead(request);
        var actor = await ReadActorAsync(source, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original Home actor is unavailable.");
        var scope = new ResourceScope(ScopeKind, request.StoreObservation.StoreIdentity.StoreId.ToString("D"),
            request.StoreObservation.Revision, ResourceAccess.Read);
        if (!(await EvaluateCoreAsync(source, actor, actionId, scope, cancellationToken).ConfigureAwait(false)).Allowed)
            throw new UnauthorizedAccessException("Set up or explicitly import this original Notes store in Home first.");
        var arguments = argumentsOverride ?? request.Arguments;
        var authorization = await AuthorizeReviewAsync(source, actor, actionId, [scope],
            arguments, preview,
            null, sessionId, cancellationToken, targetAppId).ConfigureAwait(false);
        return new(this, request, actor, scope, authorization, targetAppId, actionId, arguments);
    }

    private static string DescribeOriginalLibraryRead(INotesOriginalLibraryReadRequest request)
    {
        if (request.Kind == NotesOriginalReadKind.LibrarySnapshot && request.VersionId is null)
            return request.DocumentId is { } selected
                ? $"Read this Boards library's summaries and document {selected:D}."
                : "Read this Boards library's summaries without requesting a document body.";
        if (request.DocumentId is not { } documentId || documentId == Guid.Empty)
            throw new UnauthorizedAccessException("An original saved-version read requires its exact document identity.");
        return request.Kind switch
        {
            NotesOriginalReadKind.VersionHistory when request.VersionId is null =>
                $"Read and verify saved-version metadata for Boards document {documentId:D}. No saved document body is requested.",
            NotesOriginalReadKind.Version when NotesOriginalVersionIdentity.IsVersionId(request.VersionId) =>
                $"Read saved version {request.VersionId} of Boards document {documentId:D}. This does not restore or modify the document.",
            _ => throw new UnauthorizedAccessException("The original Notes read kind and saved-version identity do not match.")
        };
    }

    public Task<HomePermissionAuthorization> GetLibrarySnapshotAuthorizationAsync(HomeNotesLibraryReadReview review,
        CancellationToken cancellationToken = default)
    {
        DemandOriginalReadReview(review);
        return ReadAuthorizationAsync(null, review.Authorization.RequestId, cancellationToken);
    }

    public Task<HomeNotesLibraryReadAdmission?> BeginLibrarySnapshotAsync(HomeNotesLibraryReadReview review,
        CancellationToken cancellationToken = default) => BeginLibrarySnapshotCoreAsync(null, review, cancellationToken);

    public Task<HomeNotesLibraryReadAdmission?> BeginLibrarySnapshotWithinOriginalSourceAsync(HomeNotesLibraryReadReview review,
        Action<Action> originalScope, Action<Task> retain, CancellationToken cancellationToken = default) =>
        HomeNotesOriginalSource.RunAsync(originalScope, retain, source => BeginLibrarySnapshotCoreAsync(source, review, cancellationToken));

    private async Task<HomeNotesLibraryReadAdmission?> BeginLibrarySnapshotCoreAsync(HomeNotesOriginalSource? source, HomeNotesLibraryReadReview review,
        CancellationToken cancellationToken)
    {
        DemandOriginalReadReview(review);
        await WaitGateAsync(source, review.Gate, cancellationToken).ConfigureAwait(false);
        try
        {
            if (review.Admission is not null) return review.Admission;
            if (review.BeginAttempted) return null;
            if (await ReadActorAsync(source, cancellationToken).ConfigureAwait(false) != review.Actor ||
                !await IsStoreCurrentAsync(source, review.Request.StoreObservation, cancellationToken).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The Home actor or original Notes store changed during review.");
            var authorization = await ReadAuthorizationAsync(source, review.Authorization.RequestId, cancellationToken).ConfigureAwait(false);
            if (!authorization.IsAllowed) return null;
            review.BeginAttempted = true;
            var capability = await BeginCapabilityAsync(source, review.Authorization.RequestId,
                review.Arguments, cancellationToken).ConfigureAwait(false);
            if (capability is null) return null;
            review.Capability = capability;
            var claimed = await ClaimAsync(source, capability, review.ActionId,
                [review.Scope], review.Arguments, cancellationToken, review.TargetAppId).ConfigureAwait(false);
            if (claimed.Disposition != HomeResourceClaimDisposition.Claimed || claimed.Actor != review.Actor) return null;
            return review.Admission = new(_notes, review.Request, _store, _profiles, _ownership,
                _broker, review.Actor, capability, review.TargetAppId, review.ActionId, review.Arguments,
                source?.OriginalScope, source?.OriginalRetain);
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

    public Task<HomePermissionOperationResult> RetrySaveDispatchAuditWithinOriginalSourceAsync(HomeNotesSaveReview review,
        Action<Action> scope, Action<Task> retain, CancellationToken token = default)
    {
        DemandOriginalReview(review);
        if (!review.BeginAttempted || review.Admission is not null)
            return Task.FromResult(new HomePermissionOperationResult(false, "NOTES_DISPATCH_AUDIT_NOT_OWNED", "No original save dispatch audit is retained."));
        return review.Capability is { } capability
            ? _broker.RetryNotesRejectedClaimWithinOriginalSourceAsync(capability, scope, retain, token)
            : _broker.RetryNotesRejectedBeginWithinOriginalSourceAsync(review.Authorization.RequestId, scope, retain, token);
    }
    public Task<HomePermissionOperationResult> RetryLibrarySnapshotDispatchAuditWithinOriginalSourceAsync(HomeNotesLibraryReadReview review,
        Action<Action> scope, Action<Task> retain, CancellationToken token = default)
    {
        DemandOriginalReadReview(review);
        if (!review.BeginAttempted || review.Admission is not null)
            return Task.FromResult(new HomePermissionOperationResult(false, "NOTES_READ_DISPATCH_AUDIT_NOT_OWNED", "No original read dispatch audit is retained."));
        return review.Capability is { } capability
            ? _broker.RetryNotesRejectedClaimWithinOriginalSourceAsync(capability, scope, retain, token)
            : _broker.RetryNotesRejectedBeginWithinOriginalSourceAsync(review.Authorization.RequestId, scope, retain, token);
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

    private Task<AuthenticatedResourceActor?> ReadActorAsync(HomeNotesOriginalSource? source, CancellationToken token) => source is null
        ? _profiles.GetCurrentAsync(token).AsTask()
        : source.Read(() => _profiles.GetCurrentAsync(source.Scope, source.Retain, token).AsTask());
    private Task<INotesOriginalStoreObservation> ObserveStoreAsync(HomeNotesOriginalSource? source, CancellationToken token) => source is null
        ? _notes.ObserveOriginalStoreAsync(token).AsTask()
        : source.Read(() => OriginalScopedNotes.ObserveOriginalStoreWithinOriginalSourceAsync(source.Scope, source.Retain, token).AsTask());
    private Task<bool> IsStoreCurrentAsync(HomeNotesOriginalSource? source, INotesOriginalStoreObservation observed, CancellationToken token) => source is null
        ? _notes.IsOriginalStoreObservationCurrentAsync(observed, token).AsTask()
        : source.Read(() => OriginalScopedNotes.IsOriginalStoreObservationCurrentWithinOriginalSourceAsync(observed, source.Scope, source.Retain, token).AsTask());
    private INotesOriginalScopedStoreSource OriginalScopedNotes => _notes as INotesOriginalScopedStoreSource
        ?? throw new InvalidOperationException("The SAME original Notes metadata owner lacks scoped source custody.");
    private Task<VerifiedResourceStoreOwnership?> ReadOwnershipAsync(HomeNotesOriginalSource? source, string id, CancellationToken token) => source is null
        ? _ownership.GetVerifiedAsync(StoreKind, id, token).AsTask()
        : source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync(StoreKind, id, source.Scope, source.Retain, token).AsTask());
    private Task<bool> IsOwnershipCurrentAsync(HomeNotesOriginalSource? source, VerifiedResourceStoreOwnership ownership,
        AuthenticatedResourceActor actor, CancellationToken token) => source is null
        ? _ownership.IsCurrentAsync(ownership, actor, token).AsTask()
        : source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(ownership, actor, source.Scope, source.Retain, token).AsTask());
    private Task<HomePermissionAuthorization> ReadAuthorizationAsync(HomeNotesOriginalSource? source, string requestId, CancellationToken token) => source is null
        ? _broker.GetAuthorizationAsync(requestId, token)
        : source.Read(() => _permissions.GetImportAuthorizationWithinOriginalSourceAsync(requestId, source.Scope, source.Retain, token));
    private async Task<HomePermissionAuthorization> AuthorizeReviewAsync(HomeNotesOriginalSource? source,
        AuthenticatedResourceActor actor, string action, IReadOnlyList<ResourceScope> scopes, JsonElement arguments,
        string preview, string? backupId, string sessionId, CancellationToken token, string targetAppId = TargetAppId)
    {
        if (source is null) return await _broker.AuthorizeForActorAsync(actor, targetAppId, action, scopes, arguments,
            preview, backupId, sessionId, token).ConfigureAwait(false);
        var prepared = source.Invoke(() => _broker.PrepareReviewForActor(actor, targetAppId, action, scopes,
            arguments, preview, backupId, sessionId));
        var observed = await source.Read(() => _broker.AuthorizePreparedReviewWithinOriginalSourceAsync(prepared,
            source.Scope, source.Retain, token, originalPermissionSources: true)).ConfigureAwait(false);
        if (observed.State != HomePreparedReviewState.RequestObserved || observed.Request is null)
            throw new UnauthorizedAccessException("The original Notes review was not admitted by its actual Home sources.");
        return await ReadAuthorizationAsync(source, prepared.RequestId, token).ConfigureAwait(false);
    }
    private Task<HomeResourceExecutionCapability?> BeginCapabilityAsync(HomeNotesOriginalSource? source, string requestId,
        JsonElement arguments, CancellationToken token) => source is null
        ? _broker.BeginExecutionCapabilityAsync(requestId, arguments, token)
        : source.Read(() => _broker.BeginExecutionCapabilityWithinOriginalSourceAsync(requestId, arguments,
            source.Scope, source.Retain, source.Scope, token, originalPermissionSources: true));
    private Task<HomeResourceClaimResult> ClaimAsync(HomeNotesOriginalSource? source, HomeResourceExecutionCapability capability,
        string action, IReadOnlyList<ResourceScope> scopes, JsonElement arguments, CancellationToken token,
        string targetAppId = TargetAppId) => source is null
        ? _broker.ClaimExecutionObservedAsync(capability, targetAppId, action, scopes, arguments, token)
        : source.Read(() => _broker.ClaimExecutionWithinOriginalSourceAsync(capability, targetAppId, action, scopes, arguments,
            source.Scope, source.Retain, source.Scope, token, originalPermissionSources: true));
    private static Task WaitGateAsync(HomeNotesOriginalSource? source, SemaphoreSlim gate, CancellationToken token) => source is null
        ? gate.WaitAsync(token) : HomeResourceOperationBroker.WaitColdProjectBrokerGateAsync(source.Callbacks, gate, token);

    public Task<HomePermissionAuthorization> GetLibrarySnapshotAuthorizationWithinOriginalSourceAsync(HomeNotesLibraryReadReview review,
        Action<Action> scope, Action<Task> retain, CancellationToken token = default)
    {
        DemandOriginalReadReview(review);
        return HomeNotesOriginalSource.RunAsync(scope, retain, source => ReadAuthorizationAsync(source, review.Authorization.RequestId, token));
    }
    public Task<HomePermissionAuthorization> GetSaveAuthorizationWithinOriginalSourceAsync(HomeNotesSaveReview review,
        Action<Action> scope, Action<Task> retain, CancellationToken token = default)
    {
        DemandOriginalReview(review);
        return HomeNotesOriginalSource.RunAsync(scope, retain, source => ReadAuthorizationAsync(source, review.Authorization.RequestId, token));
    }
}

/// <summary>Finite original borrower callback and raw task custody, without content authority.</summary>
internal sealed class HomeNotesOriginalSource
{
    internal HomeOwnershipOriginalSourceCallbacks Callbacks { get; }
    internal Action<Action> OriginalScope { get; }
    internal Action<Task> OriginalRetain { get; }
    internal Action<Action> Scope => Callbacks.Run;
    internal Action<Task> Retain => Callbacks.Retain;
    private HomeNotesOriginalSource(Action<Action> scope, Action<Task> retain)
    { OriginalScope = scope; OriginalRetain = retain; Callbacks = new(scope, retain); }
    internal T Invoke<T>(Func<T> body) => Callbacks.Invoke(body);
    internal async Task<T> Read<T>(Func<Task<T>> body)
    {
        Task<T>? actual = null; Exception? invocation = null, observed = null; T result = default!;
        try
        {
            _ = Invoke(() =>
            {
                actual = body() ?? throw new InvalidOperationException("No original Notes Home task was returned.");
                Retain(actual); return actual; // Capture precedes any external retainer/post-scope fault.
            });
        }
        catch (Exception error) { invocation = error; }
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); }
            catch (Exception error) { observed = error; }
        if (invocation is null && actual?.IsCanceled == true && observed is OperationCanceledException)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(observed).Throw();
        var failures = new List<Exception>();
        if (invocation is not null) failures.Add(invocation);
        if (actual?.Exception is { } group) failures.AddRange(group.InnerExceptions);
        else if (observed is not null) failures.Add(observed);
        if (failures.Count != 0)
            throw new AggregateException("The actual original Notes Home callback or source failed.", failures);
        return actual is null ? throw new InvalidOperationException("No original Notes Home task was captured.") : result;
    }
    internal static async Task<T> RunAsync<T>(Action<Action> scope, Action<Task> retain, Func<HomeNotesOriginalSource, Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var originals = new CloudflareOriginalTaskLedger();
        var source = new HomeNotesOriginalSource(scope, raw => { originals.Track(raw); retain(raw); });
        T result = default!; Task<T>? actualBody = null; Exception? primary = null;
        try
        {
            actualBody = body(source) ?? throw new InvalidOperationException("No original Notes Home body task was returned.");
            result = await actualBody.ConfigureAwait(false);
        }
        catch (Exception error) { primary = error; originals.Retain(error); }
        await originals.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        foreach (var error in source.Callbacks.Errors) originals.Retain(error);
        // Status of the exact retained body AND every captured child establishes genuine
        // cancellation. A faulted raw Task carrying OCE, a synchronous finite callback OCE,
        // or an independent post-scope/retainer fault can never take this branch.
        if (actualBody?.IsCanceled == true && primary is OperationCanceledException &&
            source.Callbacks.Errors.Length == 0 && originals.OriginalTasks.All(raw => !raw.IsFaulted) &&
            originals.OriginalErrors.All(error => error is OperationCanceledException))
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        if (originals.OriginalErrors.Count != 0)
            throw new AggregateException("The original Notes Home sources did not settle cleanly.", originals.OriginalErrors);
        return result;
    }
}

/// <summary>Non-serializable original Home review. Public authorization describes actual Home
/// state; copying it does not create this retained review or an execution admission.</summary>
public sealed class HomeNotesSaveReview
{
    internal HomeNotesSaveReview(HomeNotesResourceAdmissionSource issuer, INotesOriginalSaveIntent intent,
        AuthenticatedResourceActor actor, ResourceScope scope, HomePermissionAuthorization authorization,
        string targetAppId, string actionId)
    { Issuer = issuer; Intent = intent; Actor = actor; Scope = scope; Authorization = authorization;
        TargetAppId = targetAppId; ActionId = actionId; }
    internal HomeNotesResourceAdmissionSource Issuer { get; }
    internal INotesOriginalSaveIntent Intent { get; }
    internal AuthenticatedResourceActor Actor { get; }
    internal ResourceScope Scope { get; }
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal bool BeginAttempted { get; set; }
    internal HomeResourceExecutionCapability? Capability { get; set; }
    internal HomeNotesSaveAdmission? Admission { get; set; }
    internal string TargetAppId { get; }
    internal string ActionId { get; }
    public HomePermissionAuthorization Authorization { get; }
    /// <summary>Actual original dispatch observation only; not permission or a retry grant.</summary>
    public bool WasDispatchAttempted => BeginAttempted;
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
    private readonly string _targetAppId;
    private readonly string _actionId;
    private readonly Action<Action>? _originalScope;
    private readonly Action<Task>? _retain;
    private readonly SemaphoreSlim _completionGate = new(1, 1);
    private INotesOriginalSaveOutcome? _retainedOutcome;
    private int _captureAttempted;

    internal HomeNotesSaveAdmission(INotesOriginalStoreSource source, INotesOriginalSaveIntent intent,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        HomeResourceStoreOwnershipAuthority ownership, HomeResourceOperationBroker broker,
        AuthenticatedResourceActor actor, HomeResourceExecutionCapability capability, string targetAppId, string actionId,
        Action<Action>? originalScope = null, Action<Task>? retain = null)
    { _source = source; _intent = intent; _store = store; _profiles = profiles; _ownership = ownership;
        _broker = broker; _actor = actor; _capability = capability; _targetAppId = targetAppId; _actionId = actionId;
        _originalScope = originalScope; _retain = retain; }

    public ValueTask<bool> ValidateCommitFenceAsync(HomeClaimedResourceCommitFence fence, CancellationToken token = default) =>
        _originalScope is null ? fence.ValidateAsync(token) : fence.ValidateWithinOriginalSourceAsync(_originalScope, _retain!, token);
    public ValueTask DisposeCommitFenceAsync(HomeClaimedResourceCommitFence fence) =>
        _originalScope is null ? fence.DisposeAsync() : fence.DisposeWithinOriginalSourceAsync(_originalScope, _retain!);

    public bool IsBoundToOriginalNotes(INotesOriginalStoreSource source, INotesOriginalSaveIntent intent) =>
        ReferenceEquals(_source, source) && ReferenceEquals(_intent, intent) &&
        source.IsIssuedOriginalSaveIntent(intent) && _broker.CaptureClaimedAttestation(_capability) is not null;

    public ValueTask<HomeClaimedResourceCommitFence?> CaptureCommitFenceAsync(INotesOriginalStoreSource source,
        INotesOriginalSaveIntent intent, CancellationToken cancellationToken = default)
    {
        if (!IsBoundToOriginalNotes(source, intent) || Interlocked.CompareExchange(ref _captureAttempted, 1, 0) != 0)
            return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
        return _originalScope is null
            ? HomeClaimedResourceCommitFence.CaptureNotesSaveAsync(_broker, _store, _profiles, _ownership,
                source, intent, _capability, _actor, _targetAppId, _actionId, cancellationToken)
            : HomeClaimedResourceCommitFence.CaptureNotesSaveWithinOriginalSourceAsync(_broker, _store, _profiles, _ownership,
                source, intent, _capability, _actor, _originalScope, _retain!, _targetAppId, _actionId, cancellationToken);
    }

    public Task<HomePermissionOperationResult> CompleteObservedAsync(INotesOriginalStoreSource source,
        INotesOriginalSaveOutcome outcome, CancellationToken cancellationToken = default) => _originalScope is null
        ? CompleteObservedCoreAsync(null, source, outcome, cancellationToken)
        : HomeNotesOriginalSource.RunAsync(_originalScope, _retain!, callbacks => CompleteObservedCoreAsync(callbacks, source, outcome, cancellationToken));

    private async Task<HomePermissionOperationResult> CompleteObservedCoreAsync(HomeNotesOriginalSource? callbacks, INotesOriginalStoreSource source,
        INotesOriginalSaveOutcome outcome, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(outcome);
        if (!ReferenceEquals(source, _source) || !ReferenceEquals(outcome.Intent, _intent) ||
            !source.IsIssuedOriginalSaveOutcome(outcome, _intent))
            return new(false, "NOTES_OUTCOME_NOT_OWNED", "Only the original repository's observed save outcome can complete this claim.");
        await (callbacks is null ? _completionGate.WaitAsync(cancellationToken)
            : HomeResourceOperationBroker.WaitColdProjectBrokerGateAsync(callbacks.Callbacks, _completionGate, cancellationToken)).ConfigureAwait(false);
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
            return await (callbacks is null ? _broker.CompleteExecutionAsync(_capability, terminal, cancellationToken)
                : callbacks.Read(() => _broker.CompleteSetupExecutionWithinOriginalSourceAsync(_capability, terminal, callbacks.Scope, callbacks.Retain, cancellationToken))).ConfigureAwait(false);
        }
        finally { _completionGate.Release(); }
    }

    public Task<HomePermissionOperationResult> RetryCompletionAuditAsync(INotesOriginalStoreSource source,
        CancellationToken cancellationToken = default) => _originalScope is null
        ? RetryCompletionAuditCoreAsync(null, source, cancellationToken)
        : HomeNotesOriginalSource.RunAsync(_originalScope, _retain!, callbacks => RetryCompletionAuditCoreAsync(callbacks, source, cancellationToken));

    private async Task<HomePermissionOperationResult> RetryCompletionAuditCoreAsync(HomeNotesOriginalSource? callbacks, INotesOriginalStoreSource source,
        CancellationToken cancellationToken)
    {
        if (callbacks is not null)
            return _retainedOutcome is null
                ? new(false, "NOTES_OUTCOME_NOT_RETAINED", "No original observed save outcome is retained for audit retry.")
                : await CompleteObservedCoreAsync(callbacks, source, _retainedOutcome, cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(source, _source))
            return new(false, "NOTES_OUTCOME_NOT_OWNED", "The original Notes owner is required.");
        await (callbacks is null ? _completionGate.WaitAsync(cancellationToken)
            : HomeResourceOperationBroker.WaitColdProjectBrokerGateAsync(callbacks.Callbacks, _completionGate, cancellationToken)).ConfigureAwait(false);
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
public sealed class HomeNotesResourceResolver(Func<HomeNotesResourceAdmissionSource> original) : ICanonicalResourceAccessResolver, IOriginalScopedCanonicalResourceAccessResolver
{
    public string ResourceKind => HomeNotesResourceAdmissionSource.ScopeKind;
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken cancellationToken) => original().EvaluateAsync(actor, actionId, scope, cancellationToken);
    public Task<ResourceAccessDecision> EvaluateWithinOriginalSourceAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope resource, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        original().EvaluateWithinOriginalSourceAsync(actor, actionId, resource, scope, retain, token);
}

/// <summary>One private original Home read review. Authorization fields are observations,
/// never a serializable library-read capability.</summary>
public sealed class HomeNotesLibraryReadReview
{
    internal HomeNotesLibraryReadReview(HomeNotesResourceAdmissionSource issuer, INotesOriginalLibraryReadRequest request,
        AuthenticatedResourceActor actor, ResourceScope scope, HomePermissionAuthorization authorization,
        string targetAppId, string actionId, JsonElement arguments)
    { Issuer = issuer; Request = request; Actor = actor; Scope = scope; Authorization = authorization;
        TargetAppId = targetAppId; ActionId = actionId; Arguments = arguments.Clone(); }
    internal HomeNotesResourceAdmissionSource Issuer { get; }
    internal INotesOriginalLibraryReadRequest Request { get; }
    internal AuthenticatedResourceActor Actor { get; }
    internal ResourceScope Scope { get; }
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal bool BeginAttempted { get; set; }
    internal HomeResourceExecutionCapability? Capability { get; set; }
    internal HomeNotesLibraryReadAdmission? Admission { get; set; }
    internal string TargetAppId { get; }
    internal string ActionId { get; }
    internal JsonElement Arguments { get; }
    public HomePermissionAuthorization Authorization { get; }
    /// <summary>Actual original dispatch observation only; not permission or a retry grant.</summary>
    public bool WasDispatchAttempted => BeginAttempted;
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
    private readonly string _targetAppId;
    private readonly string _actionId;
    private readonly JsonElement _reviewedArguments;
    private readonly Action<Action>? _originalScope;
    private readonly Action<Task>? _retain;
    private readonly SemaphoreSlim _completionGate = new(1, 1);
    private INotesOriginalLibraryReadOutcome? _retainedOutcome;
    private int _captureAttempted;
    private VerifiedResourceStoreOwnership? _observedOwnership;
    private HomeClaimedResourceAttestation? _observedAdmission;
    private int _completionAudited;

    internal HomeNotesLibraryReadAdmission(INotesOriginalStoreSource source, INotesOriginalLibraryReadRequest request,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership,
        HomeResourceOperationBroker broker, AuthenticatedResourceActor actor, HomeResourceExecutionCapability capability,
        string targetAppId, string actionId, JsonElement reviewedArguments,
        Action<Action>? originalScope = null, Action<Task>? retain = null)
    { _source = source; _request = request; _store = store; _profiles = profiles; _ownership = ownership;
        _broker = broker; _actor = actor; _capability = capability; _targetAppId = targetAppId; _actionId = actionId;
        _reviewedArguments = reviewedArguments.Clone(); _originalScope = originalScope; _retain = retain; }

    public ValueTask<bool> ValidateCommitFenceAsync(HomeClaimedResourceCommitFence fence, CancellationToken token = default) =>
        _originalScope is null ? fence.ValidateAsync(token) : fence.ValidateWithinOriginalSourceAsync(_originalScope, _retain!, token);
    public ValueTask DisposeCommitFenceAsync(HomeClaimedResourceCommitFence fence) =>
        _originalScope is null ? fence.DisposeAsync() : fence.DisposeWithinOriginalSourceAsync(_originalScope, _retain!);

    public bool IsBoundToOriginalNotes(INotesOriginalStoreSource source, INotesOriginalLibraryReadRequest request) =>
        ReferenceEquals(_source, source) && ReferenceEquals(_request, request) &&
        source.IsIssuedOriginalLibraryReadRequest(request) && _broker.CaptureClaimedAttestation(_capability) is not null;

    public ValueTask<HomeClaimedResourceCommitFence?> CaptureCommitFenceAsync(INotesOriginalStoreSource source,
        INotesOriginalLibraryReadRequest request, CancellationToken cancellationToken = default) => _originalScope is null
        ? new(CaptureCommitFenceCoreAsync(null, source, request, cancellationToken))
        : new(HomeNotesOriginalSource.RunAsync(_originalScope, _retain!, callbacks => CaptureCommitFenceCoreAsync(callbacks, source, request, cancellationToken)));

    private async Task<HomeClaimedResourceCommitFence?> CaptureCommitFenceCoreAsync(HomeNotesOriginalSource? callbacks,
        INotesOriginalStoreSource source, INotesOriginalLibraryReadRequest request, CancellationToken cancellationToken)
    {
        if (!IsBoundToOriginalNotes(source, request) || Interlocked.CompareExchange(ref _captureAttempted, 1, 0) != 0)
            return null;
        _observedAdmission = _broker.CaptureClaimedAttestation(_capability);
        _observedOwnership = callbacks is null
            ? await _ownership.GetVerifiedAsync(HomeNotesResourceAdmissionSource.StoreKind,
                request.StoreObservation.StoreIdentity.StoreId.ToString("D"), cancellationToken).ConfigureAwait(false)
            : await callbacks.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync(HomeNotesResourceAdmissionSource.StoreKind,
                request.StoreObservation.StoreIdentity.StoreId.ToString("D"), callbacks.Scope, callbacks.Retain, cancellationToken).AsTask()).ConfigureAwait(false);
        if (_observedAdmission is null || _observedOwnership?.Receipt is null ||
            !await (callbacks is null ? _ownership.IsCurrentAsync(_observedOwnership, _actor, cancellationToken).AsTask()
                : callbacks.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(_observedOwnership, _actor,
                    callbacks.Scope, callbacks.Retain, cancellationToken).AsTask())).ConfigureAwait(false)) return null;
        return await (callbacks is null
            ? HomeClaimedResourceCommitFence.CaptureNotesLibraryReadAsync(_broker, _store, _profiles,
                _ownership, source, request, _capability, _actor, _targetAppId, _actionId, cancellationToken, _reviewedArguments)
            : HomeClaimedResourceCommitFence.CaptureNotesLibraryReadWithinOriginalSourceAsync(_broker, _store, _profiles,
                _ownership, source, request, _capability, _actor, callbacks.OriginalScope, callbacks.OriginalRetain,
                _targetAppId, _actionId, cancellationToken, _reviewedArguments)).ConfigureAwait(false);
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
        return _originalScope is null
            ? _store.AcquireLocalOperationLeaseCoreAsync(_profiles, _actor, new ObservedSnapshotGuard(this), cancellationToken)
            : new(CaptureScopedObservedLeaseAsync(cancellationToken));
    }

    private async Task<IHomeLocalOperationLease?> CaptureScopedObservedLeaseAsync(CancellationToken token)
    {
        var actual = await _store.AcquireLocalOperationLeaseCoreAsync(_profiles, _actor, new ObservedSnapshotGuard(this),
            _originalScope!, _retain!, token).ConfigureAwait(false);
        if (actual is null) return null;
        if (actual is IHomeOriginalScopedLocalOperationLeaseCleanup original)
            return new ObservedScopedLease(original, _originalScope!, _retain!);
        // The owning store is concrete and issues a scoped lease. A changed producer is
        // refused, and its acquired actual resource is still independently released.
        await actual.DisposeAsync().ConfigureAwait(false);
        throw new InvalidOperationException("The actual Home snapshot lease lacks original scoped check/cleanup custody.");
    }

    private sealed class ObservedScopedLease(IHomeOriginalScopedLocalOperationLeaseCleanup original,
        Action<Action> scope, Action<Task> retain) : IHomeLocalOperationLease
    {
        public ValueTask<bool> IsCurrentAsync(CancellationToken token = default) => original.IsCurrentAsync(scope, retain, token);
        public ValueTask DisposeAsync() => original.DisposeWithinOriginalSourceAsync(scope, retain);
    }

    private sealed class ObservedSnapshotGuard(HomeNotesLibraryReadAdmission original) : IHomeOriginalScopedStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor,
            HomeStateCommitPhase phase, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            CheckAsync(state, actor, phase, token); // Pure held-state/issuer comparison; invokes no external producer.
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor,
            HomeStateCommitPhase phase, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(actor == original._actor && original._observedOwnership is { } ownership &&
                HomeLocalStoreOwnership.IsReceiptCurrentInState(state, ownership) && original._observedAdmission is { } admission &&
                original._broker.IsObservedNotesReadCurrentInState(admission, actor, state));
        }
    }

    public Task<HomePermissionOperationResult> CompleteObservedAsync(INotesOriginalStoreSource source,
        INotesOriginalLibraryReadOutcome outcome, CancellationToken cancellationToken = default) => _originalScope is null
        ? CompleteObservedCoreAsync(null, source, outcome, cancellationToken)
        : HomeNotesOriginalSource.RunAsync(_originalScope, _retain!, callbacks => CompleteObservedCoreAsync(callbacks, source, outcome, cancellationToken));

    private async Task<HomePermissionOperationResult> CompleteObservedCoreAsync(HomeNotesOriginalSource? callbacks, INotesOriginalStoreSource source,
        INotesOriginalLibraryReadOutcome outcome, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(outcome);
        if (!ReferenceEquals(source, _source) || !ReferenceEquals(outcome.Request, _request) ||
            !source.IsIssuedOriginalLibraryReadOutcome(outcome, _request))
            return new(false, "NOTES_READ_OUTCOME_NOT_OWNED", "Only the original repository's observed library read outcome can complete this claim.");
        await (callbacks is null ? _completionGate.WaitAsync(cancellationToken)
            : HomeResourceOperationBroker.WaitColdProjectBrokerGateAsync(callbacks.Callbacks, _completionGate, cancellationToken)).ConfigureAwait(false);
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
            var completedMessage = _request.Kind switch
            {
                NotesOriginalReadKind.LibrarySnapshot => "The original Notes owner observed its reviewed library snapshot without recovery writes.",
                NotesOriginalReadKind.VersionHistory => "The original Notes owner verified its reviewed saved-version metadata without recovery writes.",
                NotesOriginalReadKind.Version => "The original Notes owner observed its reviewed saved document version without recovery writes.",
                _ => "The original Notes owner observed its reviewed read without recovery writes."
            };
            var refusedMessage = _request.Kind switch
            {
                NotesOriginalReadKind.LibrarySnapshot => "The original Notes owner did not return a library snapshot; any recovery needs separate authority.",
                NotesOriginalReadKind.VersionHistory => "The original Notes owner did not return saved-version metadata; any recovery needs separate authority.",
                NotesOriginalReadKind.Version => "The original Notes owner did not return the selected saved document version; any recovery needs separate authority.",
                _ => "The original Notes owner did not return the reviewed read; any recovery needs separate authority."
            };
            var terminal = outcome.Succeeded
                ? new HomeExecutionOutcome(HomePermissionRequestState.Succeeded, "NOTES_READ_COMPLETED",
                    completedMessage, affected)
                : new HomeExecutionOutcome(HomePermissionRequestState.Failed, "NOTES_READ_REFUSED",
                    refusedMessage, []);
            var completed = await (callbacks is null ? _broker.CompleteExecutionAsync(_capability, terminal, cancellationToken)
                : callbacks.Read(() => _broker.CompleteSetupExecutionWithinOriginalSourceAsync(_capability, terminal, callbacks.Scope, callbacks.Retain, cancellationToken))).ConfigureAwait(false);
            if (completed.Succeeded) Volatile.Write(ref _completionAudited, 1);
            return completed;
        }
        finally { _completionGate.Release(); }
    }

    public Task<HomePermissionOperationResult> RetryCompletionAuditAsync(INotesOriginalStoreSource source,
        CancellationToken cancellationToken = default) => _originalScope is null
        ? RetryCompletionAuditCoreAsync(null, source, cancellationToken)
        : HomeNotesOriginalSource.RunAsync(_originalScope, _retain!, callbacks => RetryCompletionAuditCoreAsync(callbacks, source, cancellationToken));

    private async Task<HomePermissionOperationResult> RetryCompletionAuditCoreAsync(HomeNotesOriginalSource? callbacks, INotesOriginalStoreSource source,
        CancellationToken cancellationToken)
    {
        if (callbacks is not null)
            return _retainedOutcome is null
                ? new(false, "NOTES_OUTCOME_NOT_RETAINED", "No original observed read outcome is retained for audit retry.")
                : await CompleteObservedCoreAsync(callbacks, source, _retainedOutcome, cancellationToken).ConfigureAwait(false);
        if (!ReferenceEquals(source, _source))
            return new(false, "NOTES_READ_OUTCOME_NOT_OWNED", "The original Notes owner is required.");
        await (callbacks is null ? _completionGate.WaitAsync(cancellationToken)
            : HomeResourceOperationBroker.WaitColdProjectBrokerGateAsync(callbacks.Callbacks, _completionGate, cancellationToken)).ConfigureAwait(false);
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
    internal Task<HomePermissionOperationResult> RetryNotesRejectedClaimWithinOriginalSourceAsync(HomeResourceExecutionCapability capability,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RunOriginalSetupCoreEnvelopeAsync(scope, retain, source => RetryRejectedClaimOriginalSetupCoreAsync(capability, token, source));
    internal Task<HomePermissionOperationResult> RetryNotesRejectedBeginWithinOriginalSourceAsync(string requestId,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RunOriginalSetupCoreEnvelopeAsync(scope, retain, source => RetryRejectedBeginOriginalSetupCoreAsync(requestId, token, source));

    internal bool IsObservedNotesReadCurrentInState(HomeClaimedResourceAttestation original,
        AuthenticatedResourceActor actor, HomeCoreStoredState actualState)
    {
        var appId = original.Submission.Scope.TargetAppId;
        var actionId = original.Submission.Scope.ActionName;
        var validReadAction = appId == HomeNotesResourceAdmissionSource.TargetAppId && actionId == HomeNotesResourceAdmissionSource.ReadAction ||
            appId == HomeNotesResourceAdmissionSource.WriteTargetAppId &&
                actionId is HomeNotesResourceAdmissionSource.WriteLibraryReadAction or HomeNotesResourceAdmissionSource.WriteReadAction;
        if (!ReferenceEquals(original.Issuer, this) || original.Actor != actor || !validReadAction) return false;
        var records = actualState.Records.Where(value => value.RecordId == "home.permissions-trust").ToArray();
        if (records.Length != 1 || records[0].RecordType != "home.permissions-trust" || records[0].SchemaVersion != 1 ||
            records[0].Scope != HomeDataScope.DeviceLocal || records[0].Authority != HomeRecordAuthority.LocalCanonical) return false;
        try
        {
            foreach (var id in records[0].Payload.GetProperty("BlockedCallerIds").EnumerateArray())
                if (id.GetString() == original.Submission.Caller.CallerId) return false;
            PermissionRequest? observed = null;
            foreach (var item in records[0].Payload.GetProperty("Requests").EnumerateArray())
            {
                if (!item.TryGetProperty(nameof(PermissionRequest.RequestId), out var id) ||
                    id.GetString() != original.Capability.RequestId) continue;
                if (observed is not null) return false;
                observed = item.Deserialize<PermissionRequest>();
                if (observed is null) return false;
            }
            return observed is not null && observed.State == HomePermissionRequestState.Succeeded &&
                observed.ResultCode == "NOTES_READ_COMPLETED" && observed.Policy == original.Policy &&
                observed.Policy.RequiresPerActionApproval && observed.AppliedGrantId is null &&
                observed.AppliedTrustLevel == PermissionTrustLevel.Session && MatchesPrepared(original.Submission, observed) &&
                original.Submission.Impact.ResourceBinding is { SchemaVersion: 1 } resource &&
                resource.OriginalActor == actor && resource.Scopes.SequenceEqual(original.Capability.Scopes);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return false; }
    }
}
