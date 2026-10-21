using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Application.Automations;

/// <summary>Typed classification of a durable resume/decision refusal. These map to the specification's
/// structured failure vocabulary (NeedsAttention, WaitResumeFailed, MigrationRequired). A bad
/// continuation/version/schema/pin/revoked capability returns one of these BEFORE any side effect;
/// instruction fallback is never invented.</summary>
public enum AutomationResumeErrorKind
{
    None = 0,
    NeedsAttention = 1,
    WaitResumeFailed = 2,
    MigrationRequired = 3
}

/// <summary>Result of a durable resume/approval/cancel attempt.</summary>
public sealed record AutomationRunDecisionOutcome(
    bool Accepted,
    AutomationResumeErrorKind ErrorKind,
    string Code,
    string Message,
    Guid? ResultRunId = null);

/// <summary>A request to atomically claim a resumable Run before any resumed side effect.</summary>
public sealed record AutomationRunClaimRequest(
    Guid RunId,
    Guid AutomationId,
    string LeaseToken,
    long ExpectedRevision,
    AutomationRunStatus ExpectedStatus,
    string ContinuationDigest);

/// <summary>Outcome of the atomic claim acquisition. A losing worker receives Acquired=false and must
/// perform zero effects; no SQL token forging and no late terminal-CAS race is ever needed.</summary>
public sealed record AutomationRunClaimAcquisition(
    bool Acquired,
    string ClaimToken,
    AutomationResumeErrorKind ErrorKind,
    string Code,
    string Message);

/// <summary>Typed classification of a held claim's settlement state. A settlement is recorded when the
/// exact persistence operation that should make the resume durable (re-suspension or terminal completion)
/// cannot be acknowledged — for example a SQL rollback, a post-commit ACK loss, caller cancellation during
/// held persistence, or a malformed resumer result. Recording a settlement RETAINS the claim so a later
/// sweep cannot re-execute the resumed action B; the claim is released only after the exact persistence
/// operation is acknowledged or a proven safe pre-effect refusal occurs.</summary>
public enum AutomationRunClaimSettlementKind
{
    /// <summary>Re-suspension (SuspendRunAsync) outcome is unknown/unacknowledged.</summary>
    Suspend = 0,
    /// <summary>Terminal completion (CompleteRunAsync) outcome is unknown/unacknowledged.</summary>
    Complete = 1,
    /// <summary>The resumer cancelled before a persistence operation was prepared.</summary>
    ResumeCancelled = 2,
    /// <summary>The resumer faulted (invalid/malformed result, unexpected error).</summary>
    ResumeFault = 3,
    /// <summary>The resumer returned an accepted outcome but omitted the required updated Run.</summary>
    MalformedResume = 4
}

/// <summary>Durable record of an uncertain claim settlement. It retains the exact prepared persistence
/// operation (the run JSON and next schedule) plus the original error, so reconciliation replays ONLY
/// that exact operation without fresh graph execution. The claim is retained while unsettled.</summary>
public sealed record AutomationRunClaimSettlement(
    Guid RunId,
    Guid AutomationId,
    string ClaimToken,
    AutomationRunClaimSettlementKind Kind,
    string? RunJson,
    DateTimeOffset? NextRunAt,
    string? Error);

/// <summary>A typed approval decision for an Approval continuation. Decisions require the current
/// authorised original actor and the exact same Run/node/continuation revision; duplicate conflicting,
/// stale or forged decisions are rejected before effects.</summary>
public sealed record AutomationRunApprovalDecision(
    Guid RunId,
    Guid NodeId,
    string Decision,
    string ActorId,
    long ExpectedRevision,
    string? Reason = null);

/// <summary>Separate, additive, typed atomic continuation-claim store capability. It is deliberately a
/// distinct interface from <c>IAutomationRunContinuationStore</c> so that a store that cannot fence a
/// claim does not appear to support safe concurrent resume. Two readers of resumable candidates must
/// not both run the same action B and only race at a terminal CAS: the claim is acquired atomically
/// BEFORE the first resumed effect, binds the exact RunID/AutomationID/original token/pins/expected
/// revision/status/continuation digest to one running claim, and a losing worker returns without
/// effects. Public caller-supplied JSON is never a claim.</summary>
public interface IAutomationRunClaimStore
{
    /// <summary>Reads Runs whose persisted wait/approval is due and are safe to claim. This is a
    /// candidate read, never an approval or an execution grant.</summary>
    Task<IReadOnlyList<AutomationRun>> GetResumableClaimCandidatesAsync(DateTimeOffset now, int limit,
        CancellationToken cancellationToken);

    /// <summary>Bounded, authoritative by-ID read of exactly one due resumable Run (no pagination).
    /// Returns the Run only when it is a genuine due candidate; otherwise null. Used by the approval
    /// path so a large number of other waiting Runs never hides an exact approved target.</summary>
    Task<AutomationRun?> GetResumableRunByIdAsync(Guid runId, DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Atomically binds one running claim to exactly one resumable Run. Returns the winning
    /// acquisition for at most one concurrent caller; every loser is refused with zero effects.</summary>
    Task<AutomationRunClaimAcquisition> TryAcquireRunClaimAsync(AutomationRunClaimRequest request,
        CancellationToken cancellationToken);

    /// <summary>Records an uncertain settlement on the held claim row, retaining the exact prepared
    /// persistence operation and the original error. The claim is NOT released; a later sweep must not
    /// re-execute the resumed action while this settlement is outstanding.</summary>
    Task RecordUncertainSettlementAsync(AutomationRunClaimSettlement settlement, CancellationToken cancellationToken);

    /// <summary>Validates that a prepared persistence operation (a Run transition to be replayed) is
    /// bound EXACTLY to the held claim named by the token: the Run's identity/automation must match, its
    /// revision must advance the claim's expected revision by exactly one, and its pinned definition
    /// revision must equal the claim's fence. Reconciliation must never persist a payload for another Run
    /// while releasing this claim.</summary>
    Task<bool> ValidatePreparedOperationAgainstClaimAsync(Guid runId, string claimToken, AutomationRun prepared,
        CancellationToken cancellationToken);

    /// <summary>Reads retained claims whose settlement is still uncertain. Reconciliation replays only
    /// the exact persisted operation and releases the claim once it is durably acknowledged.</summary>
    Task<IReadOnlyList<AutomationRunClaimSettlement>> GetUncertainSettlementsAsync(CancellationToken cancellationToken);

    /// <summary>Releases a held claim (e.g. after a terminal write or re-suspension). Only the owning
    /// claim token may release it.</summary>
    Task ReleaseRunClaimAsync(Guid runId, string claimToken, CancellationToken cancellationToken);
}

/// <summary>Result of resuming a claimed Run: either a terminal Run ready for <c>CompleteRunAsync</c>
/// or a re-suspended Run ready for <c>SuspendRunAsync</c>, plus the typed outcome and the next schedule
/// when terminal. Persistence is performed by the coordinator, never inside the resumer, so the
/// suspension/terminal transaction stays under the claim with a single owner.</summary>
public sealed record AutomationRunResumeResult(
    AutomationRun? UpdatedRun,
    DateTimeOffset? NextRunAt,
    bool ReSuspended,
    AutomationRunDecisionOutcome Outcome);

/// <summary>Strategy that actually resumes a claimed Run from its persisted continuation. The
/// coordinator handles claim acquisition and persistence transitions; this resumer performs the
/// revalidated graph execution/decision work. It must route every resumed side effect through the
/// owning admission/action paths and revalidate the current original caller/scope and required
/// capabilities before them. Workflow approval is distinct from Home's permissionGranted flag: this
/// resumer never sets that flag merely because human review was approved.</summary>
public interface IAutomationRunResumer
{
    Task<AutomationRunResumeResult> ResumeAsync(AutomationRun run, CancellationToken cancellationToken);
}

/// <summary>Small, fail-closed typed seam for authoritative workflow-approval authority. It is the
/// Home-owned decision/actor-ownership boundary: decisions require the current authorised original
/// actor, not a stored actor string. When a host has not wired this seam, approval decisions are
/// refused and the affected Approval stays pending/fail-closed — a fake auto-approval service/UI is
/// never invented.</summary>
public interface IAutomationApprovalAuth
{
    Task<bool> IsAuthorisedAsync(Guid automationId, string actorId, CancellationToken cancellationToken);
}

/// <summary>Typed, fail-closed caller/actor authority context for a real automation Run/effect. The
/// originating Home actor, session and entity come from the actual Home principal/profile — never from a
/// caller-supplied participant string. The resource scope (action + owner identity + revision) is the
/// canonical Run/effect being authorized; the arguments digest pins the exact prepared operation so a
/// forged/tampered effect cannot re-use a grant.</summary>
public sealed record AutomationRunAuthorityContext(
    Guid AutomationId,
    long ExpectedRevision,
    string ActionId,
    string SessionId,
    string EntityId,
    string ParticipantId,
    IReadOnlyList<ResourceScope> Scopes,
    JsonElement Arguments);

/// <summary>Outcome of one Home authority decision. Denied (Allowed=false) is fail-closed. RequestId is
/// the Home permission request binding created at admission and consumed once at the effect boundary; it is
/// never a grant by itself.</summary>
public sealed record AutomationRunAuthorityDecision(bool Allowed, string Code, string ActorId, string RequestId);

/// <summary>Real Home caller/session/profile/entity and action authority for automation Run/effect
/// execution. When a host wires this seam, the adapter refuses fresh and resume admission AND every
/// protected effect unless the current Home actor holds genuine authority for the exact automation Run/
/// effect. When unwired (null) the adapter preserves its existing behavior so older hosts/tests remain
/// green. Implementations must derive the current actor from the actual Home principal/profile and must
/// consume Home's exact per-invocation authorization/capability once; they must never accept a caller-
/// supplied DTO/string/bool as authority.</summary>
public interface IAutomationRunAuthority
{
    /// <summary>Admits the caller for a fresh start or resume. The current Home actor must match the
    /// request participant and hold current action authority for the canonical Run/effect. Returns a
    /// denied decision (or throws) before any effect when Home/permission/actor is absent.</summary>
    ValueTask<AutomationRunAuthorityDecision> AuthorizeAsync(AutomationRunAuthorityContext context,
        CancellationToken cancellationToken);

    /// <summary>Re-validates the SAME current Home actor and consumes the exact granted Home execution
    /// capability immediately before a protected effect. Denied → zero effects and no stranded claim.</summary>
    ValueTask<AutomationRunAuthorityDecision> AuthorizeEffectAsync(AutomationRunAuthorityContext context,
        CancellationToken cancellationToken);

    /// <summary>Records the owning effect outcome against the consumed Home execution capability. Returns
    /// false when the Home audit could not be recorded; the durable run outcome is not rolled back.</summary>
    Task<bool> CompleteEffectAsync(AutomationRunAuthorityContext context, AutomationRunAuthorityDecision authorization,
        bool succeeded, string code, string message, CancellationToken cancellationToken);
}

/// <summary>Thrown when an uncertain settlement (a durable outcome/receipt for an exact persistence
/// operation that could not be acknowledged) cannot itself be recorded durably. It preserves BOTH the
/// original persistence/execution cause and the independent recording failure — never a single message
/// that claims to preserve every original cause.</summary>
public sealed class AutomationUncertainRecordingException : Exception
{
    public AutomationUncertainRecordingException(AutomationRunClaimSettlementKind kind, Exception originalCause, Exception recordingFailure)
        : base($"The uncertain settlement for kind '{kind}' could not be recorded durably.", recordingFailure)
    {
        Kind = kind;
        OriginalCause = originalCause ?? throw new ArgumentNullException(nameof(originalCause));
        RecordingFailure = recordingFailure ?? throw new ArgumentNullException(nameof(recordingFailure));
    }

    public AutomationRunClaimSettlementKind Kind { get; }
    public Exception OriginalCause { get; }
    public Exception RecordingFailure { get; }
}
