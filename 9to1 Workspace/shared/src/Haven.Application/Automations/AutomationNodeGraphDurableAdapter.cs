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

    /// <summary>Canonical action name for automation Run/effect execution in the real Home composition.
    /// This is a Run/effect grant, distinct from the definition library's write actions (automations.recover
    /// / automations.disable on automation.definition). Never broaden a definition write into execution.</summary>
    public const string AutomationRunAuthorityAction = "automations.run.effect";

    private static readonly TimeSpan UncertainRecordingBound = TimeSpan.FromSeconds(15);

    private readonly NodeGraphAutomationAdapter _compile;
    private readonly IVersionedNodeGraphRepository _graphs;
    private readonly IAutomationRepository _repository;
    private readonly IAutomationRunContinuationStore _continuationStore;
    private readonly IAutomationRunClaimStore _claimStore;
    private readonly ScheduledTaskRunner _runner;
    private readonly IAutomationApprovalAuth? _approvalAuth;
    private readonly IAutomationRunAuthority? _authority;

    public AutomationNodeGraphDurableAdapter(
        NodeGraphAutomationAdapter compile,
        IVersionedNodeGraphRepository graphs,
        IAutomationRepository repository,
        IAutomationRunContinuationStore continuationStore,
        IAutomationRunClaimStore claimStore,
        ScheduledTaskRunner runner,
        IAutomationApprovalAuth? approvalAuth = null,
        IAutomationRunAuthority? authority = null)
    {
        _compile = compile;
        _graphs = graphs;
        _repository = repository;
        _continuationStore = continuationStore;
        _claimStore = claimStore;
        _runner = runner;
        _approvalAuth = approvalAuth;
        _authority = authority;
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
        IAutomationApprovalAuth? approvalAuth = null,
        IAutomationRunAuthority? authority = null) =>
        new(compile, graphs, repository, continuationStore, claimStore, runner, approvalAuth, authority);

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
        // Real caller/action admission BEFORE admission: the current Home actor must hold genuine authority
        // for the exact automation Run/effect. Denial/fault fails closed (null) before any effect.
        var authorityContext = BuildAuthorityContext(request, request.GraphId, request.ExpectedRevision, AutomationRunAuthorityAction);
        if (!await AuthorityAdmitsAsync(authorityContext, cancellationToken).ConfigureAwait(false)) return null;
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
        var now = DateTimeOffset.UtcNow;

        // Fresh activation must target the CURRENT Active revision. A stale/historical pinned revision is
        // only reachable through a persisted resume, never a fresh start — so a fresh obsolete V4 is
        // refused once V5 is Active, while a genuine old V4 Run still resumes its own stored graph.
        OwnedGraphDefinition? owned;
        try
        {
            owned = await _graphs.GetAsync(request.GraphId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return Reject("graph.automation.repository.fault", budget);
        }
        if (owned?.Active is not { } active || active.State != GraphRevisionState.Active || active.Revision != admission.Graph.Revision)
            return Reject("graph.automation.stale-or-inactive", budget);

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

        // Resolve the canonical definition for this graph+trigger and deduplicate, so repeated
        // activations never mint a fresh AutomationDefinition each time. The Active revision is pinned.
        var definition = await ResolveOrCreateDefinitionAsync(graphId, request.StartNodeId, graphJson, revision,
            admission.Graph.SchemaVersion, projection.SourceDigest, admission.Graph.CapabilityProfileId, cancellationToken).ConfigureAwait(false);
        if (definition is null)
            return Reject("graph.automation.persist.failed", budget);

        try
        {
            await _repository.UpsertAsync(definition, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return Reject("graph.automation.persist.failed", budget);
        }

        // Real current-Home effect authority, re-validated immediately before the protected fresh start.
        var effectContext = BuildAuthorityContext(request, request.GraphId, admission.Graph.Revision, AutomationRunAuthorityAction);
        var effectDecision = await AuthorityEffectDecisionAsync(effectContext, cancellationToken).ConfigureAwait(false);
        if (_authority is not null && effectDecision is null)
            return Reject("graph.automation.authority.denied", budget);

        AutomationRun run;
        try
        {
            run = await _runner.RunOneAsync(definition, now, cancellationToken, budget).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Settle the claimed Home handle with the real cancelled outcome so it is neither lost nor
            // fabricated complete.
            if (effectDecision is not null)
                await CompleteAuthorityEffectAsync(effectContext, effectDecision, false, "graph.automation.start.cancelled",
                    "fresh automation cancelled before settlement", cancellationToken).ConfigureAwait(false);
            return new GraphActivationResult(false, "graph.automation.start.cancelled", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed, true)
            { Outcome = GraphRunOutcome.Uncertain };
        }
        catch (Exception)
        {
            if (effectDecision is not null)
                await CompleteAuthorityEffectAsync(effectContext, effectDecision, false, "graph.automation.start.failed",
                    "fresh automation faulted before settlement", cancellationToken).ConfigureAwait(false);
            return new GraphActivationResult(false, "graph.automation.start.failed", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed, true)
            { Outcome = GraphRunOutcome.Uncertain };
        }

        var freshResult = MapRunToResult(run, budget);
        if (effectDecision is not null)
        {
            var auditSettled = await CompleteAuthorityEffectAsync(effectContext, effectDecision, freshResult.Succeeded, freshResult.Code,
                "fresh automation activation", cancellationToken).ConfigureAwait(false);
            freshResult = WithUnsettledAudit(freshResult, auditSettled);
        }
        return freshResult;
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

        // Real current-Home caller/action admission for the resume, against the persisted Run's automation
        // identity and the pinned continuation revision. Denial/fault fails closed (null) before any effect.
        var resumeAuthority = BuildAuthorityContext(request, run.AutomationId, continuation.Revision, AutomationRunAuthorityAction);
        if (!await AuthorityAdmitsAsync(resumeAuthority, cancellationToken).ConfigureAwait(false)) return null;

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

        // Real current-Home effect authority, re-validated immediately before the resumed protected effects,
        // and BEFORE any claim is acquired so a denied/faulted resume never strands a claim.
        var effectContext = BuildAuthorityContext(request, run.AutomationId, continuation.Revision, AutomationRunAuthorityAction);
        var effectDecision = await AuthorityEffectDecisionAsync(effectContext, cancellationToken).ConfigureAwait(false);
        if (_authority is not null && effectDecision is null)
            return Reject("graph.automation.authority.denied", budget);

        // Real claim acquisition AT the effect boundary — after the registry validated binding/digest, so
        // a forged/rejected admission never strands a claim. A losing worker performs zero effects.
        var claimRequest = new AutomationRunClaimRequest(run.Id, run.AutomationId, run.LeaseToken ?? string.Empty,
            run.Revision, run.Status, ownerCont.ComputeDigest());
        var claim = await _claimStore.TryAcquireRunClaimAsync(claimRequest, cancellationToken).ConfigureAwait(false);
        if (!claim.Acquired)
        {
            // Claim-loser: zero effects ran. Settle the already-claimed Home handle honestly so it is neither
            // stranded nor fabricated complete — this operation did not proceed because another worker won the
            // exact SQLite claim. The retained audit (if any) surfaces via NeedsRecovery / the authority drain.
            var claimAuditSettled = true;
            if (effectDecision is not null)
                claimAuditSettled = await CompleteAuthorityEffectAsync(effectContext, effectDecision, false,
                    "graph.automation.claim.lost",
                    "resume did not proceed; the SQLite claim was acquired by another worker", cancellationToken).ConfigureAwait(false);
            return WithUnsettledAudit(
                new GraphActivationResult(false, "graph.automation.claim.lost", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed),
                claimAuditSettled);
        }
        var claimToken = claim.ClaimToken;

        AutomationRunResumeResult result;
        try
        {
            result = await _runner.ResumeScheduledGraphAsync(run, cancellationToken, budget).ConfigureAwait(false);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested)
        {
            await RecordUncertainAsync(run, claimToken, AutomationRunClaimSettlementKind.ResumeCancelled, null, null, oce).ConfigureAwait(false);
            if (effectDecision is not null)
                await CompleteAuthorityEffectAsync(effectContext, effectDecision, false, "graph.automation.resume.cancelled",
                    "resumed action cancelled before settlement", cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await RecordUncertainAsync(run, claimToken, AutomationRunClaimSettlementKind.ResumeFault, null, null, ex).ConfigureAwait(false);
            if (effectDecision is not null)
                await CompleteAuthorityEffectAsync(effectContext, effectDecision, false, "graph.automation.resume.fault",
                    "resumed action faulted before settlement", cancellationToken).ConfigureAwait(false);
            return new GraphActivationResult(false, "graph.automation.resume.fault", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed, true)
            { Outcome = GraphRunOutcome.Uncertain };
        }

        if (!result.Outcome.Accepted)
        {
            var auditSettled = true;
            if (effectDecision is not null)
                auditSettled = await CompleteAuthorityEffectAsync(effectContext, effectDecision, false, result.Outcome.Code,
                    "resumed action outcome not accepted", cancellationToken).ConfigureAwait(false);
            await _claimStore.ReleaseRunClaimAsync(run.Id, claimToken, cancellationToken).ConfigureAwait(false);
            return WithUnsettledAudit(
                new GraphActivationResult(false, result.Outcome.Code, Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
                { Outcome = GraphRunOutcome.Blocked }, auditSettled);
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
            if (effectDecision is not null)
                await CompleteAuthorityEffectAsync(effectContext, effectDecision, false, "graph.automation.persist.uncertain",
                    "resumed action persisted uncertainly", cancellationToken).ConfigureAwait(false);
            return new GraphActivationResult(false, "graph.automation.persist.uncertain", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed, true)
            { Outcome = GraphRunOutcome.Uncertain };
        }

        // Authoritative durable settlement acknowledged → release the claim.
        await _claimStore.ReleaseRunClaimAsync(run.Id, claimToken, cancellationToken).ConfigureAwait(false);

        var auditSettled = true;
        if (effectDecision is not null)
            auditSettled = await CompleteAuthorityEffectAsync(effectContext, effectDecision, true,
                result.ReSuspended ? "graph.automation.re-suspended" : "graph.automation.resumed",
                "resumed action acknowledged", cancellationToken).ConfigureAwait(false);

        // The SAME GraphExecutionBudget was already consumed per evaluated node inside the durable graph
        // runner; this result reports budget.StepsConsumed (the truthful cumulative count) with no post-work
        // truncation or fabricated accounting.

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
            return WithUnsettledAudit(
                new GraphActivationResult(true, "graph.automation.re-suspended", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
                {
                    Outcome = GraphRunOutcome.Yielded,
                    Continuation = new GraphContinuationCapture(
                        run.Id.ToString(), nextKind, nextNodeId, nextCont.ResumeAt,
                        DeriveCondition(nextCont), DeriveOwnerState(nextCont))
                }, auditSettled);
        }

        return WithUnsettledAudit(
            new GraphActivationResult(true, "graph.automation.resumed", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
            { Outcome = GraphRunOutcome.Completed }, auditSettled);
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
            return new GraphActivationResult(true, "graph.automation.yielded", Array.Empty<GraphRuntimeTransition>(), budget.StepsConsumed)
            {
                Outcome = GraphRunOutcome.Yielded,
                Continuation = new GraphContinuationCapture(run.Id.ToString(), kind, nodeId, cont.ResumeAt,
                    DeriveCondition(cont), DeriveOwnerState(cont))
            };
        }
        if (run.Status == AutomationRunStatus.Succeeded)
        {
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
        var authorized = c.AuthorizedStepLimit;
        // The returned binding must equal the envelope's StepLimit (the registry enforces that equality via
        // ValidateBinding), so the binding carries request.StepLimit. The persisted authorized limit (when
        // present on a new-format continuation) is the run's original ceiling: it must cover the stored
        // consumption and may never be inflated by a resume request — lowering the per-segment cap stays
        // allowed (that is how budget exhaustion is honestly exercised before any effect).
        if (request.StepLimit < consumed || request.StepLimit < 1 || request.StepLimit > 10000) return null;
        if (authorized > 0 && (authorized < consumed || request.StepLimit > authorized)) return null;

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

    /// <summary>Resolves the canonical Automation definition for this graph+trigger and deduplicates, so
    /// repeated activations never mint a fresh AutomationDefinition each time. The canonical identity is the
    /// SAME maintained Automation bound to the SAME canonical graph and the SAME deduplicated trigger — NOT
    /// the Active revision. When a newer Active revision activates, the SAME maintained Automation is reused
    /// (stable id), its trigger stays deduplicated, and only its pinned Active revision advances; a fresh run
    /// thus never creates a random new AutomationID per revision. Returns null only on a repository fault
    /// (fail closed).</summary>
    private async Task<AutomationDefinition?> ResolveOrCreateDefinitionAsync(
        Guid graphId, Guid triggerNodeId, string graphJson, long revision, int schemaVersion,
        string sourceDigest, string capabilityProfileId, CancellationToken cancellationToken)
    {
        IReadOnlyList<AutomationDefinition> all;
        try
        {
            all = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
        // Reuse the SAME maintained Automation for this graph+trigger regardless of the current Active
        // revision. A newer Active revision updates the existing definition's pin rather than minting a new id.
        var existing = all.FirstOrDefault(definition => definition is not null &&
            definition.GraphBinding is { } gb && gb.GraphId == graphId &&
            ScheduledGraphAutomationPayloadCodec.TryDeserialize(definition.Instruction, out var payload) &&
            payload.TriggerNodeId == triggerNodeId);
        if (existing is not null)
        {
            if (existing.GraphBinding!.ActiveRevision == revision && string.Equals(existing.Instruction,
                    ScheduledGraphAutomationPayloadCodec.Serialize(graphId, triggerNodeId, existing.Name, graphJson, null,
                        graphId: graphId, sourceRevision: revision, schemaVersion: schemaVersion,
                        sourceDigest: sourceDigest, capabilityProfileId: capabilityProfileId), StringComparison.Ordinal))
                return existing;
            return existing with
            {
                Instruction = ScheduledGraphAutomationPayloadCodec.Serialize(graphId, triggerNodeId, existing.Name, graphJson, null,
                    graphId: graphId, sourceRevision: revision, schemaVersion: schemaVersion,
                    sourceDigest: sourceDigest, capabilityProfileId: capabilityProfileId),
                GraphBinding = new AutomationDefinitionGraphBinding(graphId, existing.GraphBinding.DraftRevision, revision),
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        var now = DateTimeOffset.UtcNow;
        var newInstruction = ScheduledGraphAutomationPayloadCodec.Serialize(
            graphId, triggerNodeId, $"Registry activation {graphId:N}", graphJson, null,
            graphId: graphId, sourceRevision: revision, schemaVersion: schemaVersion,
            sourceDigest: sourceDigest, capabilityProfileId: capabilityProfileId);
        return new AutomationDefinition(
            Guid.NewGuid(), $"Registry activation {graphId:N}", HavenMode.Tasks, newInstruction,
            AutomationScheduleKind.Once, """{"at":"2000-01-01T00:00:00+00:00"}""", now, null, true, now, now)
        {
            Revision = 1,
            GraphBinding = new AutomationDefinitionGraphBinding(graphId, revision, revision)
        };
    }

    /// <summary>Builds the typed authority context for the exact canonical Automation/effect. The owner
    /// identity is the automation (graph) id, the expected revision is pinned, and the arguments digest is
    /// the request variables — so a forged/tampered effect cannot re-use a grant.</summary>
    private static AutomationRunAuthorityContext BuildAuthorityContext(GraphActivationRequest request,
        Guid automationId, long expectedRevision, string actionId)
    {
        ResourceScope[] scopes = [new ResourceScope("automation.run", automationId.ToString("D"),
            expectedRevision.ToString(), ResourceAccess.Execute)];
        return new AutomationRunAuthorityContext(automationId, expectedRevision, actionId,
            request.SessionId, request.EntityId, request.ParticipantId, scopes, request.Variables.Clone());
    }

    /// <summary>Admission-time Home authority gate. Unwired (null) preserves existing behavior; wired,
    /// any denial/fault fails closed before admission/effect.</summary>
    private async Task<bool> AuthorityAdmitsAsync(AutomationRunAuthorityContext context, CancellationToken cancellationToken)
    {
        if (_authority is null) return true;
        try
        {
            return (await _authority.AuthorizeAsync(context, cancellationToken).ConfigureAwait(false)).Allowed;
        }
        catch (Exception)
        {
            return false; // fail closed on Home/permission/provenance faults
        }
    }

    /// <summary>Effect-boundary Home authority gate, re-validated immediately before every protected effect
    /// (fresh RunOneAsync and resume execution under the claim). Returns the consumed decision when wired and
    /// allowed, null when unwired, denied or faulted (fail closed before any effect).</summary>
    private async Task<AutomationRunAuthorityDecision?> AuthorityEffectDecisionAsync(AutomationRunAuthorityContext context, CancellationToken cancellationToken)
    {
        if (_authority is null) return null;
        try
        {
            var decision = await _authority.AuthorizeEffectAsync(context, cancellationToken).ConfigureAwait(false);
            return decision.Allowed ? decision : null;
        }
        catch (Exception)
        {
            return null; // fail closed before any effect
        }
    }

    /// <summary>Settles the owning effect outcome against the consumed Home capability. Returns true when
    /// the audit was durably settled (or no Home authority is wired); returns false when the audit is not
    /// acknowledged/faulted, so the caller can surface the retained pending audit (recoverable via the
    /// authority's drain) without fabricating completion or rolling back the durable run outcome.</summary>
    private async Task<bool> CompleteAuthorityEffectAsync(AutomationRunAuthorityContext context,
        AutomationRunAuthorityDecision decision, bool succeeded, string code, string message,
        CancellationToken cancellationToken)
    {
        if (_authority is null || !decision.Allowed) return true;
        try
        {
            return await _authority.CompleteEffectAsync(context, decision, succeeded, code, message, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The authority retains the exact capability + detached outcome for audit recovery on a fault;
            // we surface it as unsettled rather than discarding it.
            return false;
        }
    }

    private static GraphActivationResult WithUnsettledAudit(GraphActivationResult result, bool auditSettled) =>
        auditSettled ? result : result with { NeedsRecovery = true, Code = result.Code + ".home-audit-pending" };
}
