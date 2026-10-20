using System.Text.Json;
using Haven.Application.NodeGraph;
using Haven.Core;

namespace Haven.Application.Automations;

/// <summary>
/// Real Automations adapter behind <see cref="INodeGraphOwnerBackedContinuationAdapter"/> and the shared
/// <see cref="NodeGraphRuntimeRegistry"/>. It is the owning seam the registry consumes for automation graph
/// activation/resume:
///   - <see cref="AdmitAsync"/> loads the canonical graph from <see cref="IVersionedNodeGraphRepository"/>,
///     requires the request's expected revision (the current Active for a fresh start, or the owning
///     historical pinned revision for a resume — an old V4 run is still resumable after an Active V5 is
///     published) and compiles it through <see cref="NodeGraphAutomationAdapter"/> so the schema, node
///     types/ports and owning bindings are revalidated before any effect;
///   - <see cref="AdmitResumeAsync"/> reloads the exact persisted Run and derives the authoritative binding
///     from the STORED continuation (pending yield node, resume-time, condition/owner-state, consumed
///     budget) and the current caller authority — it never echoes caller-supplied envelope fields. It
///     acquires NO claim here; claim acquisition happens at the real effect boundary in
///     <see cref="ResumeAsync"/> so a forged/rejected admission never strands a claim;
///   - <see cref="ResumeAsync"/> atomically acquires the real claim AT the effect boundary, executes the
///     pinned snapshot through the durable runner, persists the terminal/re-suspended outcome under the
///     claim, and releases the claim only after durable settlement (recording an uncertain settlement that
///     retains the claim on persistence failure/ACK loss/cancellation).
/// Fresh start (<see cref="ExecuteAsync"/>) runs the admitted graph through the scheduler's durable start
/// path (a persisted automation definition bound to the graph), so a fresh activation is a real durable
/// run, never a silent no-op.
/// </summary>
public sealed class AutomationNodeGraphDurableAdapter : INodeGraphOwnerBackedContinuationAdapter
{
    public const string GraphTypeValue = "automation";

    private static readonly TimeSpan UncertainRecordingBound = TimeSpan.FromSeconds(15);

    private readonly NodeGraphAutomationAdapter _compile;
    private readonly IVersionedNodeGraphRepository _graphs;
    private readonly IAutomationRepository _repository;
    private readonly IAutomationRunContinuationStore _continuationStore;
    private readonly IAutomationRunClaimStore _claimStore;
    private readonly ScheduledTaskRunner _runner;
    private readonly IAutomationApprovalAuth? _approvalAuth;

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

    /// <summary>Registration/factory seam for the shared <see cref="NodeGraphRuntimeRegistry"/>. The
    /// registry consumes <see cref="INodeGraphRuntimeAdapter"/> instances; a host registers this adapter
    /// (via this factory or the DI extension) so automation graph activations/resumes route through the
    /// shared registry. It is the production caller the adapter previously lacked.</summary>
    public static AutomationNodeGraphDurableAdapter Create(
        NodeGraphAutomationAdapter compile,
        IVersionedNodeGraphRepository graphs,
        IAutomationRepository repository,
        IAutomationRunContinuationStore continuationStore,
        IAutomationRunClaimStore claimStore,
        ScheduledTaskRunner runner,
        IAutomationApprovalAuth? approvalAuth = null) =>
        new(compile, graphs, repository, continuationStore, claimStore, runner, approvalAuth);

    public string GraphType => GraphTypeValue;

    public IReadOnlySet<string> SupportedEvents { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "schedule", "trigger" };

    /// <summary>Admits the canonical graph at the expected revision. A fresh start uses the current
    /// Active revision; a resume may use the owning historical pinned revision (an old run stays bound to
    /// its starting revision even after a newer Active is published). Refuses (null) when the graph is
    /// missing/not at the expected revision/cannot be compiled against the owning bindings, or the schema
    /// is invalid. The capability projection is derived from the authoritative graph's own nodes — never
    /// from caller-supplied JSON.</summary>
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
        GraphDocument? target = null;
        if (owned?.Active is { } active && active.State == GraphRevisionState.Active)
        {
            if (active.Revision == request.ExpectedRevision)
                target = active;
            else if (owned.ActivatedRevisions is { } history)
                target = history.FirstOrDefault(graph => graph.Revision == request.ExpectedRevision && graph.State == GraphRevisionState.Active);
        }
        if (target is null) return null;
        try
        {
            _ = _compile.Compile(target); // throws on schema/binding/port violations
        }
        catch (Exception)
        {
            return null;
        }
        var profile = new GraphCapabilityProfile(
            target.CapabilityProfileId,
            target.Nodes.Select(node => node.TypeId).ToHashSet(StringComparer.Ordinal),
            target.Nodes.Select(node => node.CapabilityId).ToHashSet(StringComparer.Ordinal));
        return new GraphRuntimeAdmission(target, profile);
    }

    /// <summary>Fresh durable start through the scheduler's durable path. The admitted canonical graph is
    /// compiled into the owning Automation projection, persisted as an automation definition bound to the
    /// graph, and executed via <see cref="ScheduledTaskRunner.RunOneAsync"/>. A durable Wait/Approval
    /// yield returns a yielded outcome carrying the detached continuation capture; a terminal outcome
    /// returns completed/failed truthfully. Refusals fail closed before any effect.</summary>
    public async ValueTask<GraphActivationResult> ExecuteAsync(GraphActivationRequest request, GraphRuntimeAdmission admission,
        GraphExecutionBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(admission);

        AutomationGraphProjection projection;
        try
        {
            projection = _compile.Compile(admission.Graph);
        }
        catch (Exception)
        {
            return Reject("graph.automation.compile.failed", budget);
        }

        string graphJson;
        try
        {
            graphJson = AutomationGraphCodec.Serialize(projection.Definition);
        }
        catch (Exception)
        {
            return Reject("graph.automation.compile.failed", budget);
        }

        var graphId = admission.Graph.GraphId;
        var revision = admission.Graph.Revision;
        var instruction = ScheduledGraphAutomationPayloadCodec.Serialize(
            graphId, request.StartNodeId, $"Registry activation {graphId:N}", graphJson, null,
            graphId: graphId, sourceRevision: revision, schemaVersion: admission.Graph.SchemaVersion,
            sourceDigest: projection.SourceDigest, capabilityProfileId: admission.Graph.CapabilityProfileId);

        var now = DateTimeOffset.UtcNow;
        var definition = new AutomationDefinition(
            Guid.NewGuid(), $"Registry activation {graphId:N}", HavenMode.Tasks, instruction,
            AutomationScheduleKind.Once, """{"at":"2000-01-01T00:00:00+00:00"}""", now, null, true, now, now)
        {
            Revision = 1,
            GraphBinding = new AutomationDefinitionGraphBinding(graphId, revision, revision)
        };

        try
        {
            await _repository.UpsertAsync(definition, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return Reject("graph.automation.persist.failed", budget);
        }

        AutomationRun run;
        try
        {
            run = await _runner.RunOneAsync(definition, now, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new GraphActivationResult(false, "graph.automation.start.cancelled", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed, true)
            { Outcome = GraphRunOutcome.Uncertain };
        }
        catch (Exception)
        {
            return new GraphActivationResult(false, "graph.automation.start.failed", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed, true)
            { Outcome = GraphRunOutcome.Uncertain };
        }

        return MapRunToResult(run, budget);
    }

    /// <summary>Owner-backed resume authority. Reloads the exact persisted Run, derives the authoritative
    /// binding from the STORED continuation and the current caller authority (approval auth where
    /// applicable), and returns/refuses the detached binding. No claim is acquired here; claim
    /// acquisition happens at the real effect boundary in <see cref="ResumeAsync"/>. Returning null
    /// denies/revokes (fail closed); a public DTO/string/envelope is never a grant.</summary>
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
        // when the approval auth seam is unwired. The typed seam is the authoritative Home boundary.
        if (continuation.Kind == GraphContinuationKind.Approval)
        {
            if (_approvalAuth is null || !await _approvalAuth.IsAuthorisedAsync(run.AutomationId, request.ParticipantId, cancellationToken).ConfigureAwait(false))
                return null;
        }

        return BuildStoredAdmission(run, ownerCont, request);
    }

    /// <summary>Executes the pinned snapshot through the durable runner under a real claim acquired AT
    /// the effect boundary, persists the terminal or re-suspended outcome, and releases the claim only
    /// after durable settlement. Persistence failure / ACK loss / cancellation retains the claim and
    /// records an uncertain settlement so a later sweep never re-executes the resumed action B.</summary>
    public async ValueTask<GraphActivationResult> ResumeAsync(GraphRuntimeContinuation continuation,
        GraphActivationRequest request, GraphRuntimeAdmission admission, GraphExecutionBudget budget,
        CancellationToken cancellationToken)
    {
        _ = request; _ = admission;
        if (!Guid.TryParse(continuation.OwnerRunId, out var runId) || runId == Guid.Empty)
            return Reject("graph.automation.run.missing", budget);

        var run = await _claimStore.GetResumableRunByIdAsync(runId, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        if (run is null)
            return Reject("graph.automation.run.missing", budget);

        // Re-validate the pinned continuation at the effect boundary (authoritative, never an echo).
        if (!AutomationGraphContinuationCodec.TryDeserialize(run.ContinuationDescriptorJson, out var ownerCont))
            return Reject("graph.automation.continuation.invalid", budget);
        if (ownerCont.GraphId != continuation.GraphId || ownerCont.PinnedDefinitionRevision != continuation.Revision)
            return Reject("graph.automation.continuation.pin.mismatch", budget);

        // Real claim acquisition AT the effect boundary — after the registry validated binding/digest, so
        // a forged/rejected admission never strands a claim. A losing worker performs zero effects.
        var claimRequest = new AutomationRunClaimRequest(run.Id, run.AutomationId, run.LeaseToken ?? string.Empty,
            run.Revision, run.Status, ownerCont.ComputeDigest());
        var claim = await _claimStore.TryAcquireRunClaimAsync(claimRequest, cancellationToken).ConfigureAwait(false);
        if (!claim.Acquired)
            return Reject("graph.automation.claim.lost", budget);
        var claimToken = claim.ClaimToken;

        AutomationRunResumeResult result;
        try
        {
            result = await _runner.ResumeScheduledGraphAsync(run, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            await RecordUncertainAsync(run, claimToken, AutomationRunClaimSettlementKind.ResumeCancelled, null, null, oce).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await RecordUncertainAsync(run, claimToken, AutomationRunClaimSettlementKind.ResumeFault, null, null, ex).ConfigureAwait(false);
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
            await RecordUncertainAsync(run, claimToken, kind, runJson, result.NextRunAt, ex).ConfigureAwait(false);
            return new GraphActivationResult(false, "graph.automation.persist.uncertain", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed, true)
            { Outcome = GraphRunOutcome.Uncertain };
        }

        // Authoritative durable settlement acknowledged → release the claim.
        await _claimStore.ReleaseRunClaimAsync(run.Id, claimToken, cancellationToken).ConfigureAwait(false);

        // Honor the SAME GraphExecutionBudget: report the steps actually consumed this segment (no
        // unchanged report after work).
        var segmentSteps = ExecutedStepsInSegment(run, ownerCont, result);
        ConsumeBestEffort(budget, segmentSteps);

        // A re-suspended run yielded again: report a yield so the registry can produce a fresh envelope
        // for the next resume. The exact next suspended node/kind/timing come from the persisted owner
        // continuation — never from caller-supplied fields.
        if (result.ReSuspended && result.UpdatedRun?.ContinuationDescriptorJson is { } nextJson &&
            AutomationGraphContinuationCodec.TryDeserialize(nextJson, out var nextCont) && nextCont.PendingYield is { } nextPending)
        {
            var nextKind = nextPending.Kind == AutomationGraphYieldKind.Approval
                ? GraphContinuationKind.Approval : GraphContinuationKind.Wait;
            var nextNodeId = nextPending.Kind == AutomationGraphYieldKind.Approval
                ? (nextPending.ApprovalNodeId ?? Guid.Empty) : (nextPending.YieldNodeId ?? Guid.Empty);
            return new GraphActivationResult(true, "graph.automation.re-suspended", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
            {
                Outcome = GraphRunOutcome.Yielded,
                Continuation = new GraphContinuationCapture(
                    run.Id.ToString(), nextKind, nextNodeId, nextCont.ResumeAt,
                    DeriveCondition(nextCont), DeriveOwnerState(nextCont))
            };
        }

        return new GraphActivationResult(true, "graph.automation.resumed", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
        { Outcome = GraphRunOutcome.Completed };
    }

    // ---- mapping / derivation helpers ----

    private GraphActivationResult MapRunToResult(AutomationRun run, GraphExecutionBudget budget)
    {
        if (run.Status is AutomationRunStatus.Waiting or AutomationRunStatus.ApprovalRequired)
        {
            if (!AutomationGraphContinuationCodec.TryDeserialize(run.ContinuationDescriptorJson, out var cont) ||
                cont.PendingYield is not { } pending)
                return new GraphActivationResult(false, "graph.automation.yield.invalid", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed, true)
                { Outcome = GraphRunOutcome.Uncertain };
            var kind = pending.Kind == AutomationGraphYieldKind.Approval
                ? GraphContinuationKind.Approval : GraphContinuationKind.Wait;
            var nodeId = pending.Kind == AutomationGraphYieldKind.Approval
                ? (pending.ApprovalNodeId ?? Guid.Empty) : (pending.YieldNodeId ?? Guid.Empty);
            if (nodeId == Guid.Empty)
                return new GraphActivationResult(false, "graph.automation.yield.invalid", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed, true)
                { Outcome = GraphRunOutcome.Uncertain };
            ConsumeBestEffort(budget, Math.Max(1, cont.ConsumedBudget));
            return new GraphActivationResult(true, "graph.automation.yielded", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
            {
                Outcome = GraphRunOutcome.Yielded,
                Continuation = new GraphContinuationCapture(run.Id.ToString(), kind, nodeId, cont.ResumeAt,
                    DeriveCondition(cont), DeriveOwnerState(cont))
            };
        }
        if (run.Status == AutomationRunStatus.Succeeded)
        {
            ConsumeBestEffort(budget, 1);
            return new GraphActivationResult(true, "graph.automation.completed", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
            { Outcome = GraphRunOutcome.Completed };
        }
        if (run.Status == AutomationRunStatus.Failed)
            return new GraphActivationResult(false, "graph.automation.failed", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
            { Outcome = GraphRunOutcome.Blocked };
        return new GraphActivationResult(false, "graph.automation.blocked", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
        { Outcome = GraphRunOutcome.Blocked };
    }

    /// <summary>Derives the authoritative binding from the STORED continuation and the current caller
    /// authority — never from caller-supplied envelope fields. The registry consequently rejects any
    /// forged/tampered envelope whose field disagrees with persisted state.</summary>
    private static GraphContinuationAdmission? BuildStoredAdmission(AutomationRun run, AutomationGraphContinuation c, GraphActivationRequest request)
    {
        var pending = c.PendingYield;
        if (pending is null) return null;
        GraphContinuationKind kind;
        Guid nodeId;
        if (pending.Kind == AutomationGraphYieldKind.Approval)
        {
            kind = GraphContinuationKind.Approval;
            nodeId = pending.ApprovalNodeId ?? Guid.Empty;
        }
        else
        {
            kind = GraphContinuationKind.Wait;
            nodeId = pending.YieldNodeId ?? Guid.Empty;
        }
        if (nodeId == Guid.Empty) return null;

        var consumed = c.ConsumedBudget;
        // The authorized shared GraphExecutionBudget must be internally consistent with the STORED
        // consumed budget; a forged request that contradicts persisted consumption is refused before any
        // effect. (The registry further requires this binding to equal the envelope's StepLimit, so an
        // echo cannot smuggle a tampered limit through.)
        if (request.StepLimit < consumed || request.StepLimit < 1 || request.StepLimit > 10000) return null;

        return new GraphContinuationAdmission(run.Id.ToString(), kind, nodeId, c.ResumeAt,
            DeriveCondition(c), DeriveOwnerState(c), request.StepLimit, consumed);
    }

    /// <summary>Deterministic condition projection from the stored pending yield. Shared by capture and
    /// admission so the envelope and the authoritative binding agree byte-for-byte.</summary>
    private static JsonElement DeriveCondition(AutomationGraphContinuation c)
    {
        var condition = c.PendingYield?.Condition;
        if (string.IsNullOrWhiteSpace(condition)) return JsonSerializer.SerializeToElement(new { });
        try
        {
            using var doc = JsonDocument.Parse(condition);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(new { value = condition });
        }
    }

    /// <summary>Deterministic owner-state projection from the stored continuation (audit trace +
    /// frontier). Shared by capture and admission so they agree.</summary>
    private static JsonElement DeriveOwnerState(AutomationGraphContinuation c)
    {
        var trace = c.Trace.Select(item => string.Format("{0}:{1}", item.NodeId.ToString("D"), (int)item.Status)).ToArray();
        var frontier = c.Frontier.Select(id => id.ToString("D")).ToArray();
        return JsonSerializer.SerializeToElement(new { trace, frontier });
    }

    private static int ExecutedStepsInSegment(AutomationRun run, AutomationGraphContinuation prior, AutomationRunResumeResult result)
    {
        if (result.UpdatedRun is not { } updated) return 0;
        if (AutomationGraphContinuationCodec.TryDeserialize(updated.ContinuationDescriptorJson, out var next))
            return Math.Max(0, next.ConsumedBudget - (prior?.ConsumedBudget ?? 0));
        return 1; // terminal segment performed at least the resolved action(s)
    }

    private static void ConsumeBestEffort(GraphExecutionBudget budget, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (!budget.TryConsume()) break;
        }
    }

    private async Task RecordUncertainAsync(AutomationRun run, string claimToken, AutomationRunClaimSettlementKind kind,
        string? runJson, DateTimeOffset? nextRunAt, Exception cause)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            cts.CancelAfter(UncertainRecordingBound);
            var settlement = new AutomationRunClaimSettlement(run.Id, run.AutomationId, claimToken, kind, runJson,
                nextRunAt, cause?.Message ?? "unknown cause");
            await _claimStore.RecordUncertainSettlementAsync(settlement, cts.Token).ConfigureAwait(false);
        }
        catch (Exception recordingFailure)
        {
            throw new AutomationUncertainRecordingException(kind, cause, recordingFailure);
        }
    }

    private static GraphActivationResult Reject(string code, GraphExecutionBudget budget) =>
        new(false, code, Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
        { Outcome = GraphRunOutcome.Blocked };
}
