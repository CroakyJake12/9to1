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
