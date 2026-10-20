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

    /// <summary>Sweeps due resumable Runs: claim, resume, persist, settle. After a store reopen the
    /// same coordinator over the same store must be able to resume the SAME Run/pin. Persistence faults,
    /// post-commit ACK loss, caller cancellation during held persistence, and malformed resumer results do
    /// NOT release the claim: they record an uncertain settlement that retains the claim so a later sweep
    /// cannot re-execute the resumed action B. The claim is released only after an authoritative durable
    /// settlement or a proven safe pre-effect refusal (outcome.Accepted == false).</summary>
    public async Task<AutomationRunDecisionOutcome> SweepResumeAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        if (resumer is null)
            return new(false, AutomationResumeErrorKind.NeedsAttention, "resumer.unavailable", "No resume strategy is wired; nothing was resumed.");
        // Reconcile any retained uncertain settlements first (exact persistence replay, no graph execution).
        await ReconcileSweepAsync(cancellationToken).ConfigureAwait(false);
        var candidates = await claimStore.GetResumableClaimCandidatesAsync(now, limit, cancellationToken).ConfigureAwait(false);
        var last = new AutomationRunDecisionOutcome(false, AutomationResumeErrorKind.None, "claim.none", "No resumable Run was claimed.");
        foreach (var run in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claim = await TryAcquireForRunAsync(run, cancellationToken).ConfigureAwait(false);
            if (!claim.Acquired) continue;

            AutomationRunResumeResult result;
            try
            {
                result = await resumer.ResumeAsync(run, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The resumer cancelled before a persistence operation was prepared. We cannot know whether
                // a side effect occurred; retain the claim and record an uncertain settlement so a later
                // sweep never re-executes B while unresolved.
                await RecordUncertainAsync(run, claim, AutomationRunClaimSettlementKind.ResumeCancelled, null, null,
                    "The resumer cancelled before persistence.", CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await RecordUncertainAsync(run, claim, AutomationRunClaimSettlementKind.ResumeFault, null, null,
                    ex.Message, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            last = result.Outcome;
            if (!result.Outcome.Accepted)
            {
                // Proven safe pre-effect refusal: release the claim; nothing was persisted/affected.
                await claimStore.ReleaseRunClaimAsync(run.Id, claim.ClaimToken, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (result.ReSuspended)
            {
                if (result.UpdatedRun is null)
                {
                    await RecordUncertainAsync(run, claim, AutomationRunClaimSettlementKind.MalformedResume, null, null,
                        "A re-suspended resume result must carry the updated Run.", cancellationToken).ConfigureAwait(false);
                    continue;
                }
                await PersistUnderClaimAsync(() => continuationStore.SuspendRunAsync(result.UpdatedRun, cancellationToken),
                    run, claim, AutomationRunClaimSettlementKind.Suspend, result.UpdatedRun, null, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (result.UpdatedRun is null)
                {
                    await RecordUncertainAsync(run, claim, AutomationRunClaimSettlementKind.MalformedResume, null, null,
                        "A terminal resume result must carry the updated Run.", cancellationToken).ConfigureAwait(false);
                    continue;
                }
                await PersistUnderClaimAsync(() => repository.CompleteRunAsync(result.UpdatedRun, result.NextRunAt, cancellationToken),
                    run, claim, AutomationRunClaimSettlementKind.Complete, result.UpdatedRun, result.NextRunAt, cancellationToken).ConfigureAwait(false);
            }
        }
        return last;
    }

    /// <summary>Reconciles retained uncertain settlements by replaying ONLY the exact persisted persistence
    /// operation (SuspendRunAsync or CompleteRunAsync with the exact prepared payload/next schedule) — never
    /// a fresh graph execution, fresh lease, or re-run of the action. Once the exact operation is
    /// acknowledged, the claim is released; otherwise it is retained with its settlement unchanged.</summary>
    public async Task<int> ReconcileSweepAsync(CancellationToken cancellationToken)
    {
        var settlements = await claimStore.GetUncertainSettlementsAsync(cancellationToken).ConfigureAwait(false);
        var reconciled = 0;
        foreach (var settlement in settlements)
        {
            // Only an exact, replayable persistence operation can be reconciled. A malformed/fault
            // settlement has no exact payload; its claim stays retained for inspection.
            if (string.IsNullOrWhiteSpace(settlement.RunJson)) continue;
            AutomationRun run;
            try
            {
                run = System.Text.Json.JsonSerializer.Deserialize<AutomationRun>(settlement.RunJson)!;
            }
            catch (System.Text.Json.JsonException)
            {
                continue;
            }
            try
            {
                if (settlement.Kind == AutomationRunClaimSettlementKind.Suspend)
                    await continuationStore.SuspendRunAsync(run, cancellationToken).ConfigureAwait(false);
                else if (settlement.Kind == AutomationRunClaimSettlementKind.Complete)
                    await repository.CompleteRunAsync(run, settlement.NextRunAt, cancellationToken).ConfigureAwait(false);
                else
                    continue; // not an exact persistence replay; retain
            }
            catch (Exception)
            {
                continue; // still unacknowledged; keep the claim and settlement
            }
            // Exact persistence operation acknowledged → clear the claim (which also clears the settlement).
            await claimStore.ReleaseRunClaimAsync(settlement.RunId, settlement.ClaimToken, cancellationToken).ConfigureAwait(false);
            reconciled++;
        }
        return reconciled;
    }

    private async Task PersistUnderClaimAsync(Func<Task> persist, AutomationRun run, AutomationRunClaimAcquisition claim,
        AutomationRunClaimSettlementKind kind, AutomationRun? updatedRun, DateTimeOffset? nextRunAt, CancellationToken cancellationToken)
    {
        try
        {
            await persist().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Keep the claim and record the EXACT persistence operation that could not be acknowledged.
            // This is the durable outcome/receipt; we never release on failure, ACK loss, or cancellation.
            var runJson = updatedRun is null ? null : System.Text.Json.JsonSerializer.Serialize(updatedRun);
            // Persist the retained uncertain outcome through an INDEPENDENT bounded lifetime, never the
            // caller's cancelled token: an in-flight/lost ACK must be recorded before any rethrow so a
            // later sweep cannot re-execute B. The recording is a best-effort durable receipt.
            await RecordUncertainAsync(run, claim, kind, runJson, nextRunAt, ex.Message, CancellationToken.None).ConfigureAwait(false);
            if (ex is OperationCanceledException oce && cancellationToken.IsCancellationRequested) throw oce;
            throw;
        }
        // Authoritative durable settlement acknowledged → release the claim.
        await claimStore.ReleaseRunClaimAsync(run.Id, claim.ClaimToken, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordUncertainAsync(AutomationRun run, AutomationRunClaimAcquisition claim,
        AutomationRunClaimSettlementKind kind, string? runJson, DateTimeOffset? nextRunAt, string error, CancellationToken cancellationToken)
    {
        var settlement = new AutomationRunClaimSettlement(run.Id, run.AutomationId, claim.ClaimToken, kind, runJson, nextRunAt, error);
        await claimStore.RecordUncertainSettlementAsync(settlement, cancellationToken).ConfigureAwait(false);
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

        // Bounded, authoritative by-ID read+claim: a large number of other waiting Runs must never hide
        // this exact approved target. The read is a candidate read — never an approval or execution grant.
        var run = await claimStore.GetResumableRunByIdAsync(decision.RunId, now, cancellationToken).ConfigureAwait(false);
        if (run is null || run.Status != AutomationRunStatus.ApprovalRequired)
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

            // A recorded decision is final for this continuation revision. Refuse a duplicate/conflicting
            // decision before any effect.
            if (continuation.ApprovalDecision is not null)
                return new(false, AutomationResumeErrorKind.WaitResumeFailed, "approval.decision.duplicate",
                    "An approval decision is already recorded for this Run; refusing a conflicting duplicate.");

            // Record the decision durably by re-suspending the SAME Run with the updated continuation. The
            // repository revalidates revision/lease/pin atomically, so duplicate/stale/conflicting decisions
            // cannot both commit; a concurrent transition conflict is surfaced as a refusal, not an effect.
            var updatedContinuation = continuation with { ApprovalDecision = normalized };
            var updatedRun = run with
            {
                Revision = run.Revision + 1,
                ContinuationDescriptorJson = AutomationGraphContinuationCodec.Serialize(updatedContinuation)
            };
            try
            {
                await continuationStore.SuspendRunAsync(updatedRun, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return new(false, AutomationResumeErrorKind.WaitResumeFailed, "approval.decision.conflict",
                    "A conflicting/stale decision was refused before effect.");
            }
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
