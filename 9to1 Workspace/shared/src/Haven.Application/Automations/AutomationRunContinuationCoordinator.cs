using Haven.Core;

namespace Haven.Application.Automations;

/// <summary>
/// Coordinates durable Wait/Approval resume. It is the owning runtime seam that:
///  - atomically claims each resumable Run through <see cref="IAutomationRunClaimStore"/> BEFORE any
///    resumed side effect, so two candidate readers cannot both run the same action and only race at a
///    terminal CAS; a losing worker performs zero effects;
///  - delegates revalidated execution to the owning resumer, then persists the terminal or re-suspended
///    outcome under the claim with a single owner;
///  - applies authoritative approval decisions with strict Run/node/revision/current-actor fencing and a
///    fail-closed auth seam, recording the decision durably and never auto-approving.
/// It owns no timer/background loop; it is invoked on demand by the scheduler or a host entry point.
/// </summary>
public sealed class AutomationRunContinuationCoordinator(
    IAutomationRepository repository,
    IAutomationRunContinuationStore continuationStore,
    IAutomationRunClaimStore claimStore,
    IAutomationRunResumer? resumer = null,
    IAutomationApprovalAuth? approvalAuth = null)
{
    public IAutomationRunClaimStore ClaimStore => claimStore;

    /// <summary>Reads resumable candidates that are due and safe to claim (a candidate read, never an
    /// approval or an execution grant).</summary>
    public Task<IReadOnlyList<AutomationRun>> GetResumableCandidatesAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
        => claimStore.GetResumableClaimCandidatesAsync(now, limit, cancellationToken);

    /// <summary>Atomically claims one candidate. Exposed so tests and the scheduler can hold a claim
    /// across the exact resumed effect boundary.</summary>
    public Task<AutomationRunClaimAcquisition> TryAcquireForRunAsync(AutomationRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var request = new AutomationRunClaimRequest(
            run.Id, run.AutomationId, run.LeaseToken ?? string.Empty, run.Revision, run.Status,
            ComputeRunContinuationDigest(run.ContinuationDescriptorJson));
        return claimStore.TryAcquireRunClaimAsync(request, cancellationToken);
    }

    /// <summary>Sweeps due resumable Runs: claim, resume, persist, release. After a store reopen the
    /// same coordinator over the same store must be able to resume the SAME Run/pin.</summary>
    public async Task<AutomationRunDecisionOutcome> SweepResumeAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        if (resumer is null)
            return new(false, AutomationResumeErrorKind.NeedsAttention, "resumer.unavailable", "No resume strategy is wired; nothing was resumed.");
        var candidates = await claimStore.GetResumableClaimCandidatesAsync(now, limit, cancellationToken).ConfigureAwait(false);
        var last = new AutomationRunDecisionOutcome(false, AutomationResumeErrorKind.None, "claim.none", "No resumable Run was claimed.");
        foreach (var run in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claim = await TryAcquireForRunAsync(run, cancellationToken).ConfigureAwait(false);
            if (!claim.Acquired) continue;
            try
            {
                var result = await resumer.ResumeAsync(run, cancellationToken).ConfigureAwait(false);
                last = result.Outcome;
                if (!result.Outcome.Accepted) continue;
                if (result.ReSuspended)
                {
                    if (result.UpdatedRun is null)
                        throw new InvalidOperationException("A re-suspended resume result must carry the updated Run.");
                    await continuationStore.SuspendRunAsync(result.UpdatedRun, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    if (result.UpdatedRun is null)
                        throw new InvalidOperationException("A terminal resume result must carry the updated Run.");
                    await repository.CompleteRunAsync(result.UpdatedRun, result.NextRunAt, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                await claimStore.ReleaseRunClaimAsync(run.Id, claim.ClaimToken, cancellationToken).ConfigureAwait(false);
            }
        }
        return last;
    }

    /// <summary>Applies an authoritative approval decision to a due ApprovalRequired Run. The decision
    /// requires the current authorised original actor (via the fail-closed Home auth seam), the exact
    /// same Run/continuation revision and the exact pending Approval node. A due ApprovalRequired row is
    /// only a candidate, never an approval; this method never auto-approves and never sets Home's
    /// permissionGranted flag. On success the decision is recorded durably on the continuation so a
    /// later resume resolves the approval into its branch.</summary>
    public async Task<AutomationRunDecisionOutcome> ApplyApprovalDecisionAsync(AutomationRunApprovalDecision decision, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var normalized = (decision.Decision ?? string.Empty).Trim().ToLowerInvariant();
        if (!AutomationGraphContinuation.ApprovalDecisions.Contains(normalized))
            return new(false, AutomationResumeErrorKind.WaitResumeFailed, "approval.decision.invalid", $"Approval decision '{decision.Decision}' is not a known outcome.");
        if (string.IsNullOrWhiteSpace(decision.ActorId))
            return new(false, AutomationResumeErrorKind.NeedsAttention, "approval.actor.missing", "An authorised original actor is required.");

        var candidates = await claimStore.GetResumableClaimCandidatesAsync(now, 100, cancellationToken).ConfigureAwait(false);
        var run = candidates.FirstOrDefault(candidate => candidate.Id == decision.RunId && candidate.Status == AutomationRunStatus.ApprovalRequired);
        if (run is null)
            return new(false, AutomationResumeErrorKind.WaitResumeFailed, "approval.run.not.due", "The ApprovalRun is not a due approval candidate.");

        var claim = await TryAcquireForRunAsync(run, cancellationToken).ConfigureAwait(false);
        if (!claim.Acquired)
            return new(false, AutomationResumeErrorKind.WaitResumeFailed, "approval.claim.lost", "Another worker is already handling this approval.");

        try
        {
            if (decision.ExpectedRevision != run.Revision)
                return new(false, AutomationResumeErrorKind.WaitResumeFailed, "approval.revision.stale", "The decision revision is stale; refusing before any effect.");

            if (approvalAuth is null)
                return new(false, AutomationResumeErrorKind.NeedsAttention, "approval.authority.unavailable", "Home approval authority is not wired; the decision stays pending (fail-closed).");
            if (!await approvalAuth.IsAuthorisedAsync(run.AutomationId, decision.ActorId, cancellationToken).ConfigureAwait(false))
                return new(false, AutomationResumeErrorKind.NeedsAttention, "approval.actor.unauthorised", "The actor is not the authorised original caller.");

            if (!AutomationGraphContinuationCodec.TryDeserialize(run.ContinuationDescriptorJson, out var continuation))
                return new(false, AutomationResumeErrorKind.WaitResumeFailed, "approval.continuation.invalid", "The persisted Approval continuation is unreadable; refusing.");
            if (continuation.PendingYield is not { Kind: AutomationGraphYieldKind.Approval } approvalYield)
                return new(false, AutomationResumeErrorKind.WaitResumeFailed, "approval.state.invalid", "The Run is not awaiting the pending approval.");
            if (approvalYield.ApprovalNodeId != decision.NodeId)
                return new(false, AutomationResumeErrorKind.WaitResumeFailed, "approval.node.mismatch", "The decision targets a different node; refusing.");

            // Record the decision durably by re-suspending the SAME Run with the updated continuation. The
            // repository revalidates revision/lease/pin atomically, so duplicate/stale/conflicting decisions
            // cannot both commit.
            var updatedContinuation = continuation with { ApprovalDecision = normalized };
            var updatedRun = run with
            {
                Revision = run.Revision + 1,
                ContinuationDescriptorJson = AutomationGraphContinuationCodec.Serialize(updatedContinuation)
            };
            await continuationStore.SuspendRunAsync(updatedRun, cancellationToken).ConfigureAwait(false);
            return new(true, AutomationResumeErrorKind.None, $"approval.{normalized}", $"Approval decided: {normalized}.", run.Id);
        }
        finally
        {
            await claimStore.ReleaseRunClaimAsync(run.Id, claim.ClaimToken, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ComputeRunContinuationDigest(string? continuationJson)
    {
        var bytes = string.IsNullOrWhiteSpace(continuationJson)
            ? Array.Empty<byte>()
            : System.Text.Encoding.UTF8.GetBytes(continuationJson);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
