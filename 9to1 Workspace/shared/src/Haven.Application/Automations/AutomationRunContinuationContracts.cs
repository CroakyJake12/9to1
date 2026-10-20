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

    /// <summary>Atomically binds one running claim to exactly one resumable Run. Returns the winning
    /// acquisition for at most one concurrent caller; every loser is refused with zero effects.</summary>
    Task<AutomationRunClaimAcquisition> TryAcquireRunClaimAsync(AutomationRunClaimRequest request,
        CancellationToken cancellationToken);

    /// <summary>Releases a held claim (e.g. after a terminal write or re-suspension). Only the owning
    /// claim token may release it.</summary>
    Task ReleaseRunClaimAsync(Guid runId, string claimToken, CancellationToken cancellationToken);
}

/// <summary>Strategy that actually resumes a claimed Run from its persisted continuation. The
/// coordinator handles claim acquisition and persistence transitions; this resumer performs the
/// revalidated graph execution/decision work. It must revalidate the current original caller/scope and
/// required capabilities before every resumed side effect through the owning admission/action paths.</summary>
public interface IAutomationRunResumer
{
    Task<AutomationRunDecisionOutcome> ResumeAsync(AutomationRun run, CancellationToken cancellationToken);
}
