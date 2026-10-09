using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
using PermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace HavenOS.Home.Core;

public enum HomePreparedReviewState { NotAttempted, RequestObserved, AdmissionDenied, OutcomeUnconfirmed }
/// <summary>Readonly observation, never an execution capability. The exact opaque prepared handle remains required.</summary>
public sealed record HomePreparedReviewObservation(HomePreparedReviewState State, PermissionRequest? Request,
    bool BindingRecovered, string Code);

/// <summary>Same-host issuer-bound prepared intent. Public RequestId is metadata, not a grant; never serialize/recreate the handle.</summary>
public sealed class HomeResourcePreparedReview
{
    private readonly HomeResourceOperationBroker _issuer;
    private int _reserved;
    internal readonly SemaphoreSlim Gate = new(1, 1);
    internal readonly AuthenticatedResourceActor Actor;
    internal readonly ResourceScope[] Scopes;
    internal readonly HomePermissionRequestSubmission Submission;
    internal bool CanonicalAdmission;
    internal bool BoundOnce;
    internal bool KnownDenied;
    internal bool Conflict;
    internal HomeResourcePreparedReview(HomeResourceOperationBroker issuer, AuthenticatedResourceActor actor,
        ResourceScope[] scopes, HomePermissionRequestSubmission submission)
    { _issuer = issuer; Actor = actor; Scopes = scopes; Submission = submission; RequestId = submission.RequestId!; }
    public string RequestId { get; }
    internal bool IssuedBy(HomeResourceOperationBroker issuer) => ReferenceEquals(_issuer, issuer);
    internal bool Reserve() => Interlocked.CompareExchange(ref _reserved, 1, 0) == 0;
    internal bool AttemptReserved => Volatile.Read(ref _reserved) != 0;
}

public sealed partial class HomeResourceOperationBroker
{
    private readonly ConcurrentDictionary<HomeResourcePreparedReview, byte> _preparedReviews = new();
    private readonly object _preparedCapacity = new();

    public HomeResourcePreparedReview PrepareReviewForActor(AuthenticatedResourceActor originalActor,
        string targetAppId, string actionId, IReadOnlyList<ResourceScope> scopes, JsonElement arguments,
        string preview, string? backupId, string sessionId) =>
        PrepareReviewCore(originalActor, targetAppId, actionId, scopes, arguments, preview, backupId, sessionId,
            () => new HomePermissionCallerIdentity(originalActor.ActorId, originalActor.ActorId,
                originalActor.ProfileId, originalActor.AuthenticationRevision, true).Validate());

    /// <summary>Internal original-session composition only. Detached caller fields cannot issue this proof.</summary>
    internal HomeResourcePreparedReview PrepareReviewForOriginalInstalledCaller(
        HomeNativeCoreApiSessions.Session.OriginalPackageCaller original,
        string targetAppId, string actionId, IReadOnlyList<ResourceScope> scopes, JsonElement arguments,
        string preview, string? backupId)
    {
        ArgumentNullException.ThrowIfNull(original);
        original.RequireRetained();
        return PrepareReviewCore(original.Actor, targetAppId, actionId, scopes, arguments, preview, backupId,
            original.SessionId, () => { original.RequireRetained(); return original.PermissionCaller; });
    }

    private HomeResourcePreparedReview PrepareReviewCore(AuthenticatedResourceActor originalActor,
        string targetAppId, string actionId, IReadOnlyList<ResourceScope> scopes, JsonElement arguments,
        string preview, string? backupId, string sessionId, Func<HomePermissionCallerIdentity> originalCaller)
    {
        ArgumentNullException.ThrowIfNull(originalActor); ArgumentNullException.ThrowIfNull(scopes);
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Original session required.", nameof(sessionId));
        // Reuses actual ResourceAuthorization's existing 1000-scope admission ceiling, checking enumeration itself.
        var detached = new List<ResourceScope>();
        foreach (var scope in scopes)
        {
            if (detached.Count == 1000 || scope is null) throw new ArgumentException("Resource scope admission capacity/shape invalid.", nameof(scopes));
            detached.Add(scope);
        }
        if (detached.Count == 0) throw new ArgumentException("Explicit owner scope required.", nameof(scopes));
        var snapshot = detached.ToArray(); var captured = arguments.Clone();
        var objects = snapshot.Select(scope => new HomeObjectReference(scope.Kind, scope.Id)).ToArray();
        var caller = originalCaller();
        var submission = new HomePermissionRequestSubmission(Guid.NewGuid().ToString("N"), caller, sessionId.Trim(),
            new HomePermissionScope(targetAppId, actionId, objects).Validate(),
            new HomePermissionImpactPreview(objects.Select(item => item.ObjectType).Distinct().ToArray(), objects.Length,
                objects, false, preview, backupId, Digest(captured),
                new HomeCanonicalResourceBinding(1, originalActor, Array.AsReadOnly(snapshot.ToArray()))));
        var prepared = new HomeResourcePreparedReview(this, originalActor, snapshot, submission);
        lock (_preparedCapacity)
        {
            if (_preparedReviews.Count >= 1024) throw new InvalidOperationException("Too many retained prepared reviews.");
            if (!_preparedReviews.TryAdd(prepared, 0)) throw new InvalidOperationException("Prepared review issuance failed.");
        }
        return prepared;
    }

    private void RequirePrepared(HomeResourcePreparedReview prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (!prepared.IssuedBy(this) || !_preparedReviews.ContainsKey(prepared))
            throw new UnauthorizedAccessException("Exact retained issuer prepared review required.");
    }

    public async Task<HomePreparedReviewObservation> AuthorizePreparedReviewAsync(HomeResourcePreparedReview prepared,
        CancellationToken cancellationToken = default)
    {
        RequirePrepared(prepared);
        var first = prepared.Reserve(); // BEFORE any cancellable wait; never publish a second authorization.
        await prepared.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequirePrepared(prepared); // A concurrent terminal retirement cannot revive queued authorization.
            if (!first) return await ObservePreparedCoreAsync(prepared, cancellationToken).ConfigureAwait(false);
            var actor = await resources.AuthorizeForActorAsync(prepared.Actor, prepared.Submission.Scope.ActionName,
                prepared.Scopes, cancellationToken).ConfigureAwait(false);
            if (actor != prepared.Actor)
            {
                prepared.KnownDenied = true;
                return new(HomePreparedReviewState.AdmissionDenied, null, false, "HOME_PREPARED_RESOURCE_DENIED");
            }
            prepared.CanonicalAdmission = true;
            var result = await permissions.AuthorizeAsync(prepared.Submission, cancellationToken).ConfigureAwait(false);
            prepared.Conflict = result.Code == "HOME_REQUEST_ID_CONFLICT";
            return await ObservePreparedCoreAsync(prepared, cancellationToken).ConfigureAwait(false);
        }
        finally { prepared.Gate.Release(); }
    }

    public async Task<HomePreparedReviewObservation> ObservePreparedReviewAsync(HomeResourcePreparedReview prepared,
        CancellationToken cancellationToken = default)
    {
        RequirePrepared(prepared);
        await prepared.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { RequirePrepared(prepared); return await ObservePreparedCoreAsync(prepared, cancellationToken).ConfigureAwait(false); }
        finally { prepared.Gate.Release(); }
    }

    /// <summary>Retires only unsubmitted/known prepublication denial or exact observed terminal metadata.
    /// This acknowledges no audit and never cancels an unknown/pending/approved/executing request.</summary>
    public async Task<bool> RetirePreparedReviewAsync(HomeResourcePreparedReview prepared,
        CancellationToken cancellationToken = default)
    {
        RequirePrepared(prepared);
        await prepared.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RequirePrepared(prepared);
            var observed = await ObservePreparedCoreAsync(prepared, cancellationToken).ConfigureAwait(false);
            var terminal = observed.Request is { } request && Enum.IsDefined(request.State) &&
                request.State is not (HomePermissionRequestState.PendingApproval or HomePermissionRequestState.Approved
                    or HomePermissionRequestState.Executing);
            if (observed.State != HomePreparedReviewState.NotAttempted && !prepared.KnownDenied && !prepared.Conflict && !terminal)
                return false;
            if (!_preparedReviews.TryRemove(prepared, out _)) return false;
            if (prepared.BoundOnce) _bindings.TryRemove(prepared.RequestId, out _);
            return true;
        }
        finally { prepared.Gate.Release(); }
    }

    private async Task<HomePreparedReviewObservation> ObservePreparedCoreAsync(HomeResourcePreparedReview prepared, CancellationToken ct)
    {
        if (!prepared.AttemptReserved) return new(HomePreparedReviewState.NotAttempted, null, false, "HOME_PREPARED_NOT_ATTEMPTED");
        if (prepared.KnownDenied || prepared.Conflict)
            return new(HomePreparedReviewState.AdmissionDenied, null, false, "HOME_PREPARED_ADMISSION_DENIED");
        var request = await permissions.ReadRequestObservationAsync(prepared.RequestId, ct).ConfigureAwait(false);
        if (request is null || !prepared.CanonicalAdmission || !MatchesPrepared(prepared.Submission, request))
            return new(HomePreparedReviewState.OutcomeUnconfirmed, null, false, "HOME_PREPARED_REQUEST_UNCONFIRMED");
        if (!prepared.BoundOnce && request.State is HomePermissionRequestState.PendingApproval or HomePermissionRequestState.Approved)
        {
            var binding = new Binding(prepared.Actor, prepared.Submission.Scope.TargetAppId,
                prepared.Submission.Scope.ActionName, prepared.Scopes.ToArray(), prepared.Submission.Impact.ArgumentsDigest!,
                prepared.Submission, permissions.ResolveTrustedActionPolicy(prepared.Submission.Scope.TargetAppId, prepared.Submission.Scope.ActionName));
            if (!_bindings.TryAdd(prepared.RequestId, binding))
                return new(HomePreparedReviewState.OutcomeUnconfirmed, request, false, "HOME_PREPARED_BINDING_CONFLICT");
            prepared.BoundOnce = true; // Never reinstall after Begin consumed this binding.
        }
        return new(HomePreparedReviewState.RequestObserved, request, prepared.BoundOnce, "HOME_PREPARED_REQUEST_OBSERVED");
    }
    private static bool MatchesPrepared(HomePermissionRequestSubmission expected, PermissionRequest actual) =>
        actual.RequestId == expected.RequestId && actual.Caller == expected.Caller && actual.SessionId == expected.SessionId &&
        JsonSerializer.Serialize(actual.Scope) == JsonSerializer.Serialize(expected.Scope) &&
        JsonSerializer.Serialize(actual.Impact) == JsonSerializer.Serialize(expected.Impact);
}
