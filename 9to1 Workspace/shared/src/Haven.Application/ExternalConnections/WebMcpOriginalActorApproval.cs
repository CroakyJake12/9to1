namespace Haven.Application;

/// <summary>Trusted host-composed optional owner. Expected actor/scopes are requests, never grants.</summary>
public interface IWebMcpOriginalActorApproval
{
    // Metadata preparation is synchronous, privately issued, and grants no resource or execution access.
    // Actual supported owning flows must retain this exact review before ANY durable Home submission await.
    IWebMcpOriginalActorReview PrepareReview(AuthenticatedResourceActor originalActor,
        WebMcpInvocationRequest request, IReadOnlyList<ResourceScope> originalOwnerScopes) =>
        throw new NotSupportedException("The original owner has no prepared review capability.");
    ValueTask<IWebMcpOriginalActorReview> ReviewAsync(AuthenticatedResourceActor originalActor,
        WebMcpInvocationRequest request, IReadOnlyList<ResourceScope> originalOwnerScopes,
        CancellationToken cancellationToken);
}

/// <summary>Private issuer implementations retain exact original intent and Home request.
/// Missing approval/unknown admission cannot trigger a second Begin or dispatch.</summary>
public interface IWebMcpOriginalActorReview
{
    string RequestId { get; }
    // The actual issuer reserves one authorization attempt; repeated submission only observes
    // the exact full durable intent after a lost return, never creates another authorization.
    ValueTask<WebMcpPreparedReviewSubmission> SubmitPreparedAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new WebMcpPreparedReviewSubmission(WebMcpPreparedReviewState.OutcomeUnconfirmed,
            "WEBMCP_PREPARED_REVIEW_UNSUPPORTED"));
    // Idempotent per actual issued review: pending never consumes an attempt; after reservation
    // repeated calls only return the retained dispatch/rejection/unknown, never Begin or Claim again.
    ValueTask<WebMcpDispatchBeginResult> BeginDispatchAsync(WebMcpInvocationRequest freshObservedBinding,
        CancellationToken cancellationToken);
    ValueTask<WebMcpOwnerRecoveryStatus> FinishAdmissionAuditAsync(CancellationToken cancellationToken);
}

/// <summary>Opaque same-issuer one-use owner dispatch capability. Never serialize/recreate it.
/// The owning Browse adapter calls CheckFinalDispatchAsync inside the actual host emission pipeline.
/// That method revalidates actual original actor, exact binding and owning resource admission.</summary>
public interface IWebMcpOriginalDispatch
{
    string InvocationId { get; }
    // Optional actual local held-operation entry. Missing support denies effect admission.
    // Acquire AFTER actual resource claim, retain through synchronous native emission only,
    // dispose BEFORE awaiting external completion or writing any Home audit.
    ValueTask<IWebMcpFinalDispatchLease?> EnterFinalDispatchAsync(WebMcpInvocationRequest actualObservedBinding,
        CancellationToken cancellationToken) => ValueTask.FromResult<IWebMcpFinalDispatchLease?>(null);
    ValueTask<bool> CheckFinalDispatchAsync(WebMcpInvocationRequest actualObservedBinding,
        CancellationToken cancellationToken);
    ValueTask<WebMcpOwnerRecoveryStatus> CompleteObservedAsync(WebMcpObservedOutcome outcome,
        CancellationToken cancellationToken);
    ValueTask<WebMcpOwnerRecoveryStatus> FinishAuditAsync(CancellationToken cancellationToken);
}

/// <summary>Actual originating owner correlation only; external JSON alone is not an outcome proof.
/// OutcomeUnconfirmed never records a terminal failure/success or permits another dispatch.</summary>
public enum WebMcpObservedOutcomeKind { Succeeded, Failed, Cancelled, OutcomeUnconfirmed }
public sealed record WebMcpObservedOutcome(string InvocationId, WebMcpObservedOutcomeKind Kind,
    string Code, string Detail);
public sealed record WebMcpOwnerRecoveryStatus(bool AuditRecorded, bool OutcomeKnown, string Code);

/// <summary>PendingApproval guarantees the issuer has not reserved/consumed an admission attempt.
/// Ready carries the same retained private dispatch. Rejected/OutcomeUnconfirmed never authorize a new attempt.</summary>
public enum WebMcpDispatchBeginState { PendingApproval, Ready, Rejected, OutcomeUnconfirmed }
public sealed record WebMcpDispatchBeginResult(WebMcpDispatchBeginState State,
    IWebMcpOriginalDispatch? Dispatch, string Code);

/// <summary>Opaque actual issuer-held local admission lease, never a public metadata grant.
/// The supported native entry checks it immediately before synchronous emission.
/// No recursive Home/resource/permission read may occur while retained; the actual
/// producer may directly reread raw state under its already-held physical lock.</summary>
public interface IWebMcpFinalDispatchLease : IAsyncDisposable
{
    ValueTask<bool> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>Actual original request observation only; Approved never grants execution/dispatch.</summary>
public enum WebMcpPreparedReviewState { PendingApproval, Approved, Rejected, OutcomeUnconfirmed }
public sealed record WebMcpPreparedReviewSubmission(WebMcpPreparedReviewState State, string Code);
