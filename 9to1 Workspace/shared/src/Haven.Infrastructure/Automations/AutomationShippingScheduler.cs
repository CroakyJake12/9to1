using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using System.Text.Json;

namespace Haven.Infrastructure.Automations;

/// <summary>
/// Production scheduled-Task executor that routes every automation graph through the SAME shared NodeGraph
/// runtime registry + durable adapter + real Home authority via
/// <see cref="AutomationShippingComposition.IAutomationRunGateway"/>, so fresh due runs, user "run now" and
/// resume entry points all receive genuine current-Home admission/effect authority. Non-graph instruction
/// payloads retain the legacy <see cref="ScheduledTaskRunner"/> terminal semantics (there is no separate
/// engine and no recursion: the durable adapter already owns the runner for graph execution).
///
/// This is the real shipping consumer the composition previously lacked: it owns the original invocation of
/// the registered gateway, and it owns the retained Home audit drain for clean close (see
/// <see cref="DrainPendingAuditsAsync"/>).
/// </summary>
public sealed class AutomationShippingScheduler(
    AutomationShippingComposition.IAutomationRunGateway gateway,
    IAutomationRepository repository,
    ScheduledTaskRunner runner,
    IAutomationRunAuditDrain? auditDrain = null,
    IAuthenticatedResourceActorSource? actors = null)
{
    /// <summary>The registered shipping gateway this scheduler drives. Exposed so the resume entry point and
    /// any host-composed callers use the SAME live gateway (never a parallel registry).</summary>
    public AutomationShippingComposition.IAutomationRunGateway Gateway => gateway;

    private const int FreshStepLimit = 1000;
    private const string SessionPrefix = "session-automations-shipping";
    private const string EntityId = "entity";

    /// <summary>Runs every due automation through the real shipping path. Graph payloads are fresh-activated
    /// through the gateway (shared registry + durable adapter + real Home authority); legacy instruction
    /// payloads retain the runner's terminal semantics. Suspended (durable Wait/Approval) activations are
    /// counted as started, not terminal.</summary>
    public async Task<ScheduledTaskBatchResult> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await repository.GetDueAsync(now, cancellationToken).ConfigureAwait(false);
        var started = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        var uncertainties = new List<ScheduledTaskCompletionUncertaintyException>();
        foreach (var task in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (ScheduledGraphAutomationPayloadCodec.IsPayload(task.Instruction))
                {
                    var request = await BuildFreshActivationRequestAsync(task, cancellationToken).ConfigureAwait(false);
                    if (request is null)
                    {
                        skipped++;
                        continue;
                    }
                    var result = await gateway.ActivateAsync(request, cancellationToken).AsTask().ConfigureAwait(false);
                    started++;
                    if (result.Succeeded && result.EffectiveOutcome == GraphRunOutcome.Completed)
                        succeeded++;
                    else if (!result.Succeeded || result.EffectiveOutcome == GraphRunOutcome.Blocked)
                        failed++;
                    // Durable Wait/Approval yields (EffEffectiveOutcome == Yielded/Uncertain) are started but
                    // suspended — neither terminal success nor failure.
                }
                else
                {
                    var run = await runner.RunOneAsync(task, now, cancellationToken).ConfigureAwait(false);
                    started++;
                    if (run.Status == AutomationRunStatus.Succeeded) succeeded++;
                    else if (run.Status == AutomationRunStatus.Failed) failed++;
                }
            }
            catch (ScheduledTaskCompletionUncertaintyException uncertainty)
            {
                // Preserve the exact draft/cause for inspectable batch reporting.
                uncertainties.Add(uncertainty);
            }
        }
        return new ScheduledTaskBatchResult(due.Count, started, succeeded, failed, skipped)
        {
            Uncertainties = uncertainties
        };
    }

    /// <summary>Runs one automation now through the real shipping path. Graph payloads activate through the
    /// gateway under real Home authority; legacy instructions keep the runner's terminal semantics. Returns a
    /// faithful run projection for the caller surface.</summary>
    public async Task<AutomationRun> RunNowAsync(AutomationDefinition definition, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!ScheduledGraphAutomationPayloadCodec.IsPayload(definition.Instruction))
            return await runner.RunOneAsync(definition, now, cancellationToken).ConfigureAwait(false);

        var request = await BuildFreshActivationRequestAsync(definition, cancellationToken).ConfigureAwait(false);
        if (request is null)
            return FailedRun(definition.Id, now, "graph.automation.request.invalid");

        var result = await gateway.ActivateAsync(request, cancellationToken).AsTask().ConfigureAwait(false);
        if (result.Succeeded && result.EffectiveOutcome == GraphRunOutcome.Completed)
            return new AutomationRun(Guid.NewGuid(), definition.Id, AutomationRunStatus.Succeeded, now, now,
                DateTimeOffset.UtcNow, "{\"graphSucceeded\":true}", null, null);
        if (result.Succeeded && result.EffectiveOutcome == GraphRunOutcome.Yielded)
            return new AutomationRun(Guid.NewGuid(), definition.Id, AutomationRunStatus.Waiting, now, now, null,
                null, null, null);
        if (!result.Succeeded)
            return FailedRun(definition.Id, now, result.Code);
        return FailedRun(definition.Id, now, "graph.automation.outcome.unknown");
    }

    /// <summary>Production resume entry point: invokes the SAME registered gateway's resume path for a stored
    /// resumable Run under real Home authority. The caller supplies the well-formed continuation envelope
    /// (derived from the stored continuation) and the matching activation request; the registry/adapter
    /// re-derive the authoritative binding from the persisted Run, so a forged/stale envelope is refused
    /// before any effect.</summary>
    public async ValueTask<GraphActivationResult> ResumeAsync(GraphRuntimeContinuation continuation,
        GraphActivationRequest request, CancellationToken cancellationToken)
        => await gateway.ResumeAsync(continuation, request, cancellationToken).ConfigureAwait(false);

    /// <summary>Drains every retained Home audit (exact detached outcome replay, never a graph effect) on
    /// clean close so a cancel-before-record / claim-loser audit is settled honestly.</summary>
    public async Task<int> DrainPendingAuditsAsync(CancellationToken cancellationToken)
    {
        if (auditDrain is null) return 0;
        return await auditDrain.DrainPendingAuditsAsync(cancellationToken).ConfigureAwait(false);
    }

    private static AutomationRun FailedRun(Guid automationId, DateTimeOffset now, string code) =>
        new(Guid.NewGuid(), automationId, AutomationRunStatus.Failed, now, now, DateTimeOffset.UtcNow, null, code, null);

    private async Task<GraphActivationRequest?> BuildFreshActivationRequestAsync(AutomationDefinition definition, CancellationToken cancellationToken)
    {
        if (!ScheduledGraphAutomationPayloadCodec.TryDeserialize(definition.Instruction, out var payload)) return null;
        var graphId = payload.GraphId ?? definition.GraphBinding?.GraphId;
        if (graphId is null || graphId == Guid.Empty || payload.TriggerNodeId == Guid.Empty) return null;
        var revision = payload.SourceRevision ?? definition.GraphBinding?.ActiveRevision;
        if (revision is null || revision < 1) return null;

        var actorId = "automations-shipping";
        var sessionId = SessionPrefix;
        if (actors is not null)
        {
            var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            // Fail closed until a genuine current Home actor exists: a fabrication would be denied by the
            // real authority anyway, and without an actor there is no valid owner to run for.
            if (actor is null || string.IsNullOrWhiteSpace(actor.ActorId)) return null;
            actorId = actor.ActorId;
            sessionId = SessionPrefix + "-" + actor.ActorId;
        }

        return new GraphActivationRequest(
            AutomationNodeGraphDurableAdapter.GraphTypeValue,
            graphId.Value,
            revision.Value,
            payload.TriggerNodeId,
            "schedule",
            sessionId,
            EntityId,
            actorId,
            JsonSerializer.SerializeToElement(new { }),
            FreshStepLimit);
    }
}
