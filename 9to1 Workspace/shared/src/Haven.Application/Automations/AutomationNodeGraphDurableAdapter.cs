using System.Collections.Concurrent;
using System.Text.Json;
using Haven.Application.NodeGraph;

namespace Haven.Application.Automations;

/// <summary>
/// Real Automations adapter behind <see cref="INodeGraphOwnerBackedContinuationAdapter"/>. It is the
/// owning resume seam the shared <see cref="NodeGraphRuntimeRegistry"/> consumes:
///   - <see cref="AdmitAsync"/> loads the canonical graph from <see cref="IVersionedNodeGraphRepository"/>,
///     requires the Active revision and compiles it through <see cref="NodeGraphAutomationAdapter"/> so the
///     schema, node types/ports and owning bindings are revalidated before any effect;
///   - <see cref="AdmitResumeAsync"/> reloads the exact persisted Run, verifies the envelope↔continuation
///     pin, atomically acquires the real claim (the winning claim required/current at every resumed effect
///     boundary) and returns/refuses the detached authoritative binding. Returning null denies/revokes
///     (fail closed); a public DTO/string/envelope is never a grant;
///   - <see cref="ResumeAsync"/> executes the pinned snapshot through the durable runner, persists the
///     terminal/re-suspended outcome under the claim, and releases the claim only after durable settlement
///     (recording an uncertain settlement that retains the claim on persistence failure/ACK loss).
/// Fresh start (ExecuteAsync) remains on the scheduler's legacy durable start path; the registry path
/// covers resume, so ExecuteAsync is fail-closed here. This adapter is additive and keeps the legacy
/// fallback separate; it does not silently activate unreviewed graph definitions.
/// </summary>
public sealed class AutomationNodeGraphDurableAdapter : INodeGraphOwnerBackedContinuationAdapter
{
    public const string GraphTypeValue = "automation";

    private readonly NodeGraphAutomationAdapter _compile;
    private readonly IVersionedNodeGraphRepository _graphs;
    private readonly IAutomationRepository _repository;
    private readonly IAutomationRunContinuationStore _continuationStore;
    private readonly IAutomationRunClaimStore _claimStore;
    private readonly ScheduledTaskRunner _runner;
    private readonly IAutomationApprovalAuth? _approvalAuth;
    private readonly ConcurrentDictionary<Guid, string> _heldClaims = new();

    public AutomationNodeGraphDurableAdapter(
        NodeGraphAutomationAdapter compile,
        IVersionedNodeGraphRepository graphs,
        IAutomationRepository repository,
        IAutomationRunContinuationStore continuationStore,
        IAutomationRunClaimStore claimStore,
        ScheduledTaskRunner runner,
        IAutomationApprovalAuth? approvalAuth = null)
    {
        _compile = compile;
        _graphs = graphs;
        _repository = repository;
        _continuationStore = continuationStore;
        _claimStore = claimStore;
        _runner = runner;
        _approvalAuth = approvalAuth;
    }

    public string GraphType => GraphTypeValue;

    public IReadOnlySet<string> SupportedEvents { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "schedule", "trigger" };

    /// <summary>Admits the canonical Active graph at the expected revision. Refuses (null) when the
    /// graph is missing/inactive/not at the expected revision, cannot be compiled against the owning
    /// bindings, or the schema is invalid. The capability projection is derived from the authoritative
    /// graph's own nodes — never from caller-supplied JSON.</summary>
    public async ValueTask<GraphRuntimeAdmission?> AdmitAsync(GraphActivationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.GraphId == Guid.Empty || request.ExpectedRevision < 1) return null;
        OwnedGraphDefinition? owned;
        try
        {
            owned = await _graphs.GetAsync(request.GraphId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null; // fail closed on repository fault
        }
        var active = owned?.Active;
        if (active is null || active.Revision != request.ExpectedRevision || active.State != GraphRevisionState.Active) return null;
        try
        {
            _ = _compile.Compile(active); // throws on schema/binding/port violations
        }
        catch (Exception)
        {
            return null;
        }
        var profile = new GraphCapabilityProfile(
            active.CapabilityProfileId,
            active.Nodes.Select(node => node.TypeId).ToHashSet(StringComparer.Ordinal),
            active.Nodes.Select(node => node.CapabilityId).ToHashSet(StringComparer.Ordinal));
        return new GraphRuntimeAdmission(active, profile);
    }

    /// <summary>Fresh start remains on the scheduler's legacy durable start path. This adapter covers
    /// resume; a fresh activation here is fail-closed and never silently activates an unreviewed graph.</summary>
    public ValueTask<GraphActivationResult> ExecuteAsync(GraphActivationRequest request, GraphRuntimeAdmission admission,
        GraphExecutionBudget budget, CancellationToken cancellationToken)
    {
        _ = request; _ = admission; _ = budget; _ = cancellationToken;
        return ValueTask.FromResult(new GraphActivationResult(false, "graph.automation.fresh-unsupported",
            Array.Empty<GraphRuntimeTransition>(), 0) { Outcome = GraphRunOutcome.Blocked });
    }

    /// <summary>Owner-backed resume authority. Reloads the exact persisted Run, verifies the envelope
    /// ↔ continuation pin, checks current authority (approval auth where applicable), and atomically
    /// acquires the real claim BEFORE any resumed effect. Returns the detached binding on success or
    /// null to deny/revoke (fail closed). The claim is held across <see cref="ResumeAsync"/>.</summary>
    public async ValueTask<GraphContinuationAdmission?> AdmitResumeAsync(GraphRuntimeContinuation continuation,
        GraphActivationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        ArgumentNullException.ThrowIfNull(request);
        if (!Guid.TryParse(continuation.OwnerRunId, out var runId) || runId == Guid.Empty) return null;

        var run = await _claimStore.GetResumableRunByIdAsync(runId, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        if (run is null) return null;
        if (!AutomationGraphContinuationCodec.TryDeserialize(run.ContinuationDescriptorJson, out var ownerCont))
            return null;
        if (ownerCont.GraphId != continuation.GraphId || ownerCont.PinnedDefinitionRevision != continuation.Revision)
            return null;
        // The envelope kind must match the Run's ACTUAL suspension kind (Wait vs Approval); a Wait Run is
        // never resumed as an Approval and vice versa. Any mismatch is denied before the authorization seam.
        if (ownerCont.PendingYield is not { } pendingYield ||
            pendingYield.Kind != (continuation.Kind == GraphContinuationKind.Approval
                ? AutomationGraphYieldKind.Approval : AutomationGraphYieldKind.Wait))
            return null;

        // Authority gate: approval resumes must not advance without the current authorised actor decision;
        // that decision is applied through the coordinator, so the resume admission stays fail-closed
        // when the approval auth seam is unwired.
        if (continuation.Kind == GraphContinuationKind.Approval)
        {
            if (_approvalAuth is null || !await _approvalAuth.IsAuthorisedAsync(run.AutomationId, request.ParticipantId, cancellationToken).ConfigureAwait(false))
                return null;
        }

        // Real claim acquisition: winning claim required/current at every resumed effect boundary.
        var claimRequest = new AutomationRunClaimRequest(run.Id, run.AutomationId, run.LeaseToken ?? string.Empty,
            run.Revision, run.Status, ownerCont.ComputeDigest());
        var claim = await _claimStore.TryAcquireRunClaimAsync(claimRequest, cancellationToken).ConfigureAwait(false);
        if (!claim.Acquired) return null;
        _heldClaims[runId] = claim.ClaimToken;

        // The authoritative binding echoes the validated envelope fields; the registry revalidates every
        // field against this binding before calling ResumeAsync.
        return new GraphContinuationAdmission(
            continuation.OwnerRunId,
            continuation.Kind,
            continuation.NodeId,
            continuation.ResumeAt,
            continuation.Condition,
            continuation.OwnerState,
            continuation.StepLimit,
            continuation.StepsConsumed);
    }

    /// <summary>Executes the pinned snapshot through the durable runner, persists the terminal or
    /// re-suspended outcome under the held claim, and releases the claim only after durable settlement.
    /// Persistence failure / ACK loss / cancellation retains the claim and records an uncertain settlement
    /// so a later sweep never re-executes the resumed action B.</summary>
    public async ValueTask<GraphActivationResult> ResumeAsync(GraphRuntimeContinuation continuation,
        GraphActivationRequest request, GraphRuntimeAdmission admission, GraphExecutionBudget budget,
        CancellationToken cancellationToken)
    {
        _ = request; _ = admission;
        if (!Guid.TryParse(continuation.OwnerRunId, out var runId) || runId == Guid.Empty)
            return Reject("graph.automation.run.missing", budget);
        if (!_heldClaims.TryRemove(runId, out var claimToken))
            return Reject("graph.automation.claim.missing", budget);

        var run = await _claimStore.GetResumableRunByIdAsync(runId, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        if (run is null)
            return Reject("graph.automation.run.missing", budget);

        AutomationRunResumeResult result;
        try
        {
            result = await _runner.ResumeScheduledGraphAsync(run, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RecordUncertainAsync(run, claimToken, AutomationRunClaimSettlementKind.ResumeFault, null, null, ex.Message, cancellationToken).ConfigureAwait(false);
            return new GraphActivationResult(false, "graph.automation.resume.fault", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed, true)
            { Outcome = GraphRunOutcome.Uncertain };
        }

        if (!result.Outcome.Accepted)
        {
            await _claimStore.ReleaseRunClaimAsync(run.Id, claimToken, cancellationToken).ConfigureAwait(false);
            return new GraphActivationResult(false, result.Outcome.Code, Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
            { Outcome = GraphRunOutcome.Blocked };
        }

        try
        {
            if (result.ReSuspended)
            {
                if (result.UpdatedRun is null) throw new InvalidOperationException("A re-suspended result must carry the updated Run.");
                await _continuationStore.SuspendRunAsync(result.UpdatedRun, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (result.UpdatedRun is null) throw new InvalidOperationException("A terminal result must carry the updated Run.");
                await _repository.CompleteRunAsync(result.UpdatedRun, result.NextRunAt, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            var kind = result.ReSuspended ? AutomationRunClaimSettlementKind.Suspend : AutomationRunClaimSettlementKind.Complete;
            var runJson = result.UpdatedRun is null ? null : JsonSerializer.Serialize(result.UpdatedRun);
            await RecordUncertainAsync(run, claimToken, kind, runJson, result.NextRunAt, ex.Message, cancellationToken).ConfigureAwait(false);
            return new GraphActivationResult(false, "graph.automation.persist.uncertain", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed, true)
            { Outcome = GraphRunOutcome.Uncertain };
        }

        // Authoritative durable settlement acknowledged → release the claim.
        await _claimStore.ReleaseRunClaimAsync(run.Id, claimToken, cancellationToken).ConfigureAwait(false);

        // A re-suspended run yielded again: report a yield so the registry can produce a fresh envelope
        // for the next resume. The exact next suspended node/kind/timing come from the persisted owner
        // continuation — never from caller-supplied fields.
        if (result.ReSuspended && result.UpdatedRun?.ContinuationDescriptorJson is { } nextJson &&
            AutomationGraphContinuationCodec.TryDeserialize(nextJson, out var nextCont) && nextCont.PendingYield is { } nextPending)
        {
            var nextKind = nextPending.Kind == AutomationGraphYieldKind.Approval
                ? GraphContinuationKind.Approval : GraphContinuationKind.Wait;
            var empty = JsonSerializer.SerializeToElement(new { });
            return new GraphActivationResult(true, "graph.automation.re-suspended", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
            {
                Outcome = GraphRunOutcome.Yielded,
                Continuation = new GraphContinuationCapture(
                    run.Id.ToString(), nextKind, nextPending.YieldNodeId ?? continuation.NodeId,
                    nextCont.ResumeAt, empty, empty)
            };
        }

        return new GraphActivationResult(true, "graph.automation.resumed", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
        { Outcome = GraphRunOutcome.Completed };
    }

    private async Task RecordUncertainAsync(AutomationRun run, string claimToken, AutomationRunClaimSettlementKind kind,
        string? runJson, DateTimeOffset? nextRunAt, string error, CancellationToken cancellationToken)
    {
        var settlement = new AutomationRunClaimSettlement(run.Id, run.AutomationId, claimToken, kind, runJson, nextRunAt, error);
        await _claimStore.RecordUncertainSettlementAsync(settlement, cancellationToken).ConfigureAwait(false);
    }

    private static GraphActivationResult Reject(string code, GraphExecutionBudget budget) =>
        new(false, code, Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
        { Outcome = GraphRunOutcome.Blocked };
}
