using System.Collections.Frozen;
using System.Diagnostics;
using Haven.Application;
using Haven.Core;

namespace Dulche.Runtime.Agents;

/// <summary>Consumes the existing canonical Den catalog/state/permission ports and original
/// shared Chat loop. Host observations never replace Home's reference-bound execution admission.
/// No Agent definition, run registry, approval broker or tool executor is copied or created.</summary>
public sealed class DenAgentChatExecutionAdapter(DenAgentReference boundAgent,
    DenPersistentAgentCatalog catalog, DenAgentRuntimeValidator validator,
    IDenAgentCurrentRuntimeContextSource host, IAgentExecutionStateStore state,
    IAgentPermissionBroker permissions, AgentTaskRuntimeService runtime) : IAgentExecutionAdapter
{
    public async ValueTask<AgentResult<ResolvedExecutionTarget>> ResolveTargetAsync(
        PersistentAgentSnapshot agent, AgentInvocationContext context, AgentModelPolicy requestPolicy,
        CancellationToken cancellationToken = default)
    {
        if (agent.AgentId != boundAgent.AgentId || agent.DefinitionRevision != boundAgent.DefinitionRevision)
            return Refuse<ResolvedExecutionTarget>(AgentFailureCode.DefinitionRevisionUnavailable,
                "Target resolution requires the exact bound canonical Agent revision.", agent.AgentId);
        var projection = await catalog.GetInvocationProjectionAsync(boundAgent, context, cancellationToken).ConfigureAwait(false);
        if (projection.Error is { } error) return AgentResult<ResolvedExecutionTarget>.Failure(error);
        if (!projection.Value!.Execution.Enabled)
            return Refuse<ResolvedExecutionTarget>(AgentFailureCode.AgentDisabled, "The current Agent is disabled.", agent.AgentId);
        if (!SamePolicy(requestPolicy, projection.Value.Execution.ModelPolicy))
            return Refuse<ResolvedExecutionTarget>(AgentFailureCode.ModelUnavailable,
                "The requested model policy differs from the same saved canonical revision.", agent.AgentId);
        var currentHost = await host.GetForInvocationAsync(boundAgent, context, cancellationToken).ConfigureAwait(false);
        if (currentHost.Error is { } hostError) return AgentResult<ResolvedExecutionTarget>.Failure(hostError);
        if (!ValidHost(currentHost.Value))
            return Refuse<ResolvedExecutionTarget>(AgentFailureCode.InvalidInvocationContext,
                "The original current host did not bind this exact Agent execution scope.", agent.AgentId);
        var current = currentHost.Value!;
        var result = await validator.ValidateCurrentAsync(boundAgent, current.Platform, current.Scope,
            current.OriginalInheritedModelKey, cancellationToken).ConfigureAwait(false);
        if (result.Error is { } validationError) return AgentResult<ResolvedExecutionTarget>.Failure(validationError);
        if (ValidateSupported(result.Value!) is { } unsupported)
            return AgentResult<ResolvedExecutionTarget>.Failure(unsupported);
        // Re-read the original host after provider/registry I/O. DTO equality only narrows this
        // observation; the registered Home source still checks the SAME original context.
        var final = await host.GetForInvocationAsync(boundAgent, context, cancellationToken).ConfigureAwait(false);
        if (final.Error is { } finalError) return AgentResult<ResolvedExecutionTarget>.Failure(finalError);
        if (final.Value != current)
            return Refuse<ResolvedExecutionTarget>(AgentFailureCode.RevisionConflict,
                "The original host/model selection changed during target resolution.", agent.AgentId);
        var revisionFence = await catalog.GetInvocationProjectionAsync(boundAgent, context, cancellationToken).ConfigureAwait(false);
        if (revisionFence.Error is { } revisionError) return AgentResult<ResolvedExecutionTarget>.Failure(revisionError);
        cancellationToken.ThrowIfCancellationRequested();
        var selected = result.Value!.Model!;
        return AgentResult<ResolvedExecutionTarget>.Success(new(selected.ProviderId,
            Guid.NewGuid().ToString("N"), selected.Name, selected.ProviderId,
            result.Value.Dependencies.Capabilities.Select(value => value.Key).ToFrozenSet(StringComparer.Ordinal),
            selected.IsLocal, boundAgent.DefinitionRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    public async ValueTask<AgentExecutionStepResult> ExecuteStepAsync(AgentExecutionStep step,
        Func<AgentExecutionEventEnvelope, CancellationToken, ValueTask> publish,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step); ArgumentNullException.ThrowIfNull(publish);
        var elapsed = Stopwatch.StartNew();
        var issued = await permissions.GetOriginalStepAdmissionAsync(step, cancellationToken).ConfigureAwait(false);
        if (issued.Error is { } admissionError)
            throw new UnauthorizedAccessException("The original Home step was not admitted: " + admissionError.Code);
        var admission = issued.Value!;
        if (admission.OriginalExecutionAuthority is null || !admission.OriginalLifetime.CanBeCanceled ||
            admission.OriginalLifetime.IsCancellationRequested)
            throw new UnauthorizedAccessException("The original Home step lifetime is unavailable or retired.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(admission.OriginalLifetime, cancellationToken);
        var token = lifetime.Token;
        var original = await state.ReadAsync(step.AgentRunId, token).ConfigureAwait(false);
        if (!Matches(original, step))
            throw new UnauthorizedAccessException("The current durable root run/attempt/session differs from the original step.");
        var originalScope = new AgentInvocationContext(original!.Run.CallerId, original.Run.SurfaceId,
            original.Run.SpaceId, original.Run.ProjectOrEntityId);
        AgentExecutionStepResult Failed(AgentFailure error, AgentBudgetUsage? usage = null) =>
            Failure(error, usage ?? original!.Run.BudgetUsage, original);
        var currentHost = await host.GetForStepAsync(boundAgent, step, token).ConfigureAwait(false);
        if (currentHost.Error is { } hostError) return Failed(hostError);
        if (!ValidHost(currentHost.Value))
            return Failed(new(AgentFailureCode.InvalidInvocationContext, "The original Home step scope is unavailable.", step.AgentRunId));
        var projection = await catalog.GetInvocationProjectionAsync(boundAgent, originalScope, token).ConfigureAwait(false);
        if (projection.Error is { } projectionError) return Failed(projectionError);
        if (!projection.Value!.Execution.Enabled)
            return Failed(new(AgentFailureCode.AgentDisabled, "The current canonical Agent is disabled.", boundAgent.AgentId));
        var current = currentHost.Value!;
        var resolved = await validator.ValidateCurrentAsync(boundAgent, current.Platform, current.Scope,
            current.OriginalInheritedModelKey, token).ConfigureAwait(false);
        if (resolved.Error is { } resolutionError) return Failed(resolutionError);
        if (ValidateSupported(resolved.Value!) is { } unsupported) return Failed(unsupported);
        var validated = resolved.Value!;
        var selected = validated.Model!;
        if (selected.ProviderId != step.Target.ProviderId || selected.Name != step.Target.ModelId)
            return Failed(new(AgentFailureCode.ModelUnavailable, "The current routed model differs from the committed root attempt.", step.AgentRunId));
        if (step.ContextSnapshot.Count != 0 || step.ResumeCheckpointId is not null)
            return Failed(new(AgentFailureCode.MemoryScopeDenied,
                "Context/continuation owner binding is not integrated into this original Chat adapter yet.", step.AgentRunId));
        if (step.RemainingBudget.Validate(step.AgentRunId) is { } invalid) return Failed(invalid);
        if (step.RemainingBudget.MaxTokens is not null || step.RemainingBudget.MaxCost is not null)
            return Failed(new(AgentFailureCode.BudgetUsageUnavailable,
                "The original Chat provider does not measure an enforceable cumulative token/cost budget.", step.AgentRunId));
        var previous = original!.Run.BudgetUsage;
        var oldSteps = MeasuredOrEmpty(previous.Steps); var oldTime = MeasuredOrEmpty(previous.Time);
        var oldCalls = MeasuredOrEmpty(previous.ToolCalls);
        if (oldSteps is null || oldTime is null || step.RemainingBudget.MaxToolCalls is not null && oldCalls is null)
            return Failed(new(AgentFailureCode.BudgetUsageUnavailable, "Prior cumulative budget observations are unavailable.", step.AgentRunId));
        if (step.RemainingBudget.MaxSteps is { } maxSteps && oldSteps >= maxSteps ||
            step.RemainingBudget.MaxTime is { } maxTime && oldTime >= maxTime)
            return Failed(new(AgentFailureCode.BudgetExceeded, "The cumulative step/time limit is already exhausted.", step.AgentRunId));
        var remainingTime = step.RemainingBudget.MaxTime is { } limit ? limit - oldTime!.Value : (TimeSpan?)null;
        if (remainingTime?.TotalMilliseconds > uint.MaxValue - 1L)
            return Failed(new(AgentFailureCode.BudgetUsageUnavailable, "This timer cannot enforce the saved duration; no service limit is invented.", step.AgentRunId));
        if (remainingTime is { } totalTime)
        {
            var left = totalTime - elapsed.Elapsed;
            if (left <= TimeSpan.Zero) return Failed(new(AgentFailureCode.BudgetExceeded,
                "The original total step duration was exhausted during preparation.", step.AgentRunId));
            lifetime.CancelAfter(left);
        }
        var remainingCalls = step.RemainingBudget.MaxToolCalls is { } callLimit ? Math.Max(0, callLimit - oldCalls!.Value) : (long?)null;
        var permitted = validated.Dependencies.Capabilities.Where(value =>
            admission.EffectiveCapabilities.Contains(value.Key) && step.EffectiveCapabilities.Contains(value.Key)).ToArray();
        if (permitted.Length != validated.Dependencies.Capabilities.Count)
            return Failed(new(AgentFailureCode.PermissionDenied, "Current original Home admission does not allow every requested registered capability.", step.AgentRunId));
        if (!Guid.TryParse(step.AgentRunId, out var runId) || runId == Guid.Empty ||
            !Guid.TryParse(step.AttemptId, out var attemptId) || attemptId == Guid.Empty ||
            !Guid.TryParse(step.SessionId, out var sessionId) || sessionId == Guid.Empty)
            return Failed(new(AgentFailureCode.InvalidInvocationContext, "Canonical run, attempt and conversation IDs must be actual UUIDs.", step.AgentRunId));
        var descriptor = await runtime.ResolveCanonicalDenModelAsync(selected, sessionId,
            admission.OriginalExecutionAuthority, admission.OriginalLifetime, token).ConfigureAwait(false);
        // Every await above is followed by current original reference, host, Den and durable-run
        // fences before entering the central Chat loop; the Chat issuer repeats them at dispatch.
        var refreshed = await permissions.GetOriginalStepAdmissionAsync(step, token).ConfigureAwait(false);
        if (refreshed.Error is { } refreshedError) return Failed(refreshedError);
        if (!ReferenceEquals(refreshed.Value!.OriginalExecutionAuthority, admission.OriginalExecutionAuthority) ||
            refreshed.Value.OriginalLifetime != admission.OriginalLifetime ||
            !refreshed.Value.EffectiveCapabilities.SetEquals(admission.EffectiveCapabilities))
            return Failed(new(AgentFailureCode.PermissionDenied, "The original step admission changed during preparation.", step.AgentRunId));
        var hostFence = await host.GetForStepAsync(boundAgent, step, token).ConfigureAwait(false);
        if (hostFence.Error is { } fenceError) return Failed(fenceError);
        if (hostFence.Value != current || !Matches(await state.ReadAsync(step.AgentRunId, token).ConfigureAwait(false), step))
            return Failed(new(AgentFailureCode.RevisionConflict, "The original host or committed attempt changed during preparation.", step.AgentRunId));
        var revisionFence = await catalog.GetInvocationProjectionAsync(boundAgent, originalScope, token).ConfigureAwait(false);
        if (revisionFence.Error is { } revisionError) return Failed(revisionError);
        var currentTimeLeft = remainingTime is { } total ? total - elapsed.Elapsed : (TimeSpan?)null;
        if (currentTimeLeft is { } leftNow && leftNow <= TimeSpan.Zero)
            return Failed(new(AgentFailureCode.BudgetExceeded, "The original duration is exhausted before model work.", step.AgentRunId));
        var request = new CanonicalAgentChatStepRequest(runId, attemptId, sessionId, step.Prompt,
            revisionFence.Value!.Execution.Name, revisionFence.Value.SharedDefinition.Instructions, descriptor,
            Array.AsReadOnly(permitted), remainingCalls, currentTimeLeft);
        CanonicalAgentChatStepResult response;
        try
        {
            response = await runtime.RunCanonicalDenStepAsync(request, admission.OriginalExecutionAuthority,
                admission.OriginalLifetime, token).ConfigureAwait(false);
        }
        catch (Exception originalFailure)
        {
            try
            {
                // The exception itself is observational. Only the registered Home broker can
                // match the SAME original step/error to its actual still-pending request.
                var pending = await permissions.GetOriginalApprovalAsync(step, originalFailure, token).ConfigureAwait(false);
                if (pending.Error is not null || pending.Value is not { } approval ||
                    approval.AgentRunId != step.AgentRunId || approval.CallerId != step.CallerId ||
                    string.IsNullOrWhiteSpace(approval.ApprovalId) ||
                    !approval.State.Equals("pending", StringComparison.OrdinalIgnoreCase) ||
                    approval.DecidedAtUtc is not null || approval.DecisionId is not null)
                    throw;
                var currentAdmission = await permissions.GetOriginalStepAdmissionAsync(step, token).ConfigureAwait(false);
                if (currentAdmission.Error is not null ||
                    !ReferenceEquals(currentAdmission.Value!.OriginalExecutionAuthority, admission.OriginalExecutionAuthority) ||
                    !Matches(await state.ReadAsync(step.AgentRunId, token).ConfigureAwait(false), step))
                    throw;
                token.ThrowIfCancellationRequested(); elapsed.Stop();
                var pausedUsage = previous with
                {
                    Time = UsageValue<TimeSpan>.Measured(oldTime!.Value + elapsed.Elapsed,
                        "Actual elapsed original work before its current approval pause."),
                    Steps = UsageValue<long>.Unavailable("The original Chat step paused before completion."),
                    ToolCalls = UsageValue<long>.Unavailable("Partial original dispatch is not a complete callback/retry inventory."),
                    Tokens = UsageValue<long>.Unavailable("No actual measured token usage was supplied before the pause."),
                    Cost = UsageValue<decimal>.Unavailable("No actual measured provider charge was supplied before the pause.")
                };
                return new(AgentRunState.AwaitingApproval, null, null, null, [], [approval], [], [],
                    original.CompletedConsequentialActionIds.ToArray(), original.UncertainConsequentialActionIds.ToArray(),
                    pausedUsage, null, new(AgentFailureCode.ApprovalRequired,
                        "The actual original Home request is awaiting its user decision; no checkpoint continuation is available.",
                        step.AgentRunId, Retryable: false));
            }
            catch (Exception handlingFailure)
            {
                if (ReferenceEquals(handlingFailure, originalFailure)) throw;
                throw new AggregateException("Original Chat refusal and independent current approval handling failure.",
                    originalFailure, handlingFailure);
            }
        }
        elapsed.Stop();
        var finalAdmission = await permissions.GetOriginalStepAdmissionAsync(step, token).ConfigureAwait(false);
        if (finalAdmission.Error is { } finalAdmissionError) return Failed(finalAdmissionError);
        if (!ReferenceEquals(finalAdmission.Value!.OriginalExecutionAuthority, admission.OriginalExecutionAuthority) ||
            !Matches(await state.ReadAsync(step.AgentRunId, token).ConfigureAwait(false), step))
            return Failed(new(AgentFailureCode.RevisionConflict, "The original attempt changed before result publication.", step.AgentRunId));
        var finalProjection = await catalog.GetInvocationProjectionAsync(boundAgent, originalScope, token).ConfigureAwait(false);
        if (finalProjection.Error is { } finalProjectionError) return Failed(finalProjectionError);
        var returnedCalls = response.Observation.ObservationComplete
            ? response.Observation.Invocations.LongCount(value => value.Status == ToolInvocationObservationStatus.RuntimeReturned)
            : (long?)null;
        var usage = previous with
        {
            Tokens = UsageValue<long>.Unavailable("The original provider supplied no measured token usage."),
            Cost = UsageValue<decimal>.Unavailable("No measured provider charge is supplied by the original loop."),
            Time = UsageValue<TimeSpan>.Measured(oldTime!.Value + elapsed.Elapsed, "Actual elapsed original Chat step plus prior measured time."),
            Steps = UsageValue<long>.Measured(checked(oldSteps!.Value + 1), "One actual original Chat step completed."),
            ToolCalls = oldCalls is { } priorCalls && returnedCalls is { } calls
                ? UsageValue<long>.Measured(checked(priorCalls + calls), "Complete original dispatch RuntimeReturned facts, including retries.")
                : UsageValue<long>.Unavailable("Original dispatch observations are incomplete/deferred or prior usage unavailable.")
        };
        if (response.Preflight?.IsCompatible == false || response.Assistant is null)
            return Failed(new(AgentFailureCode.ModelUnavailable, "The actual original Chat pass did not complete an assistant response.", step.AgentRunId), usage);
        if (!response.Observation.ObservationComplete)
            return Failed(new(AgentFailureCode.BudgetUsageUnavailable,
                "Deferred/incomplete original dispatch cannot prove a completed canonical step or safe continuation.", step.AgentRunId), usage);
        // The response is persisted inside the SAME encrypted Den run's event collection by the
        // owning coordinator. The reference addresses that actual root record, not an invented file.
        var resultEvent = new AgentExecutionEventEnvelope(step.AgentRunId, null, null, step.AgentRunId,
            step.AttemptId, checked(step.EventCursor + 1), DateTimeOffset.UtcNow,
            "chat.response.completed", response.Assistant.Content);
        return new(AgentRunState.Completed, response.Assistant.Content, null, null,
            [new("response:" + step.AgentRunId + ":" + step.AttemptId, "Den", "AgentRun", step.AgentRunId)],
            [], [], [resultEvent], original.CompletedConsequentialActionIds.ToArray(),
            original.UncertainConsequentialActionIds.ToArray(), usage, 100);
    }

    public ValueTask<AgentResult<AgentCheckpoint>> CheckpointAsync(AgentRunSnapshot run,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(Refuse<AgentCheckpoint>(
            AgentFailureCode.CheckpointUnavailable, "The original Chat loop exposes no restorable model checkpoint.", run.AgentRunId));
    public ValueTask<AgentResult<IReadOnlyList<string>>> ReconcileSideEffectsAsync(AgentRunSnapshot run,
        IReadOnlyList<string> uncertainActionIds, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Refuse<IReadOnlyList<string>>(AgentFailureCode.RunRecoveryFailed,
            "Original dispatch returns are not canonical owner mutation receipts; reconciliation requires the owning service.", run.AgentRunId));

    private bool ValidHost(DenAgentCurrentRuntimeContext? value) => value is not null && value.Reference == boundAgent &&
        Guid.TryParseExact(boundAgent.AgentId, "D", out var id) && value.Scope == "agent:" + id.ToString("N");
    private bool Matches(AgentExecutionSnapshot? value, AgentExecutionStep step) => value is not null &&
        value.Run.AgentRunId == step.AgentRunId && value.Run.AgentId == boundAgent.AgentId &&
        value.Run.DefinitionRevision == boundAgent.DefinitionRevision && value.Run.CurrentAttemptId == step.AttemptId &&
        value.Run.SessionId == step.SessionId && value.Run.CallerId == step.CallerId && value.Run.Objective == step.Prompt &&
        value.Run.ModelId == step.Target.ModelId && value.Run.ProviderId == step.Target.ProviderId &&
        value.Run.State == AgentRunState.Running;
    private static bool SamePolicy(AgentModelPolicy left, AgentModelPolicy right) => left.Inherit == right.Inherit &&
        left.ModelId == right.ModelId && left.ProviderId == right.ProviderId && left.AllowCloud == right.AllowCloud &&
        left.AllowFallback == right.AllowFallback && (left.RequiredCapabilities ?? new HashSet<string>()).SetEquals(
            right.RequiredCapabilities ?? new HashSet<string>());
    private static AgentFailure? ValidateSupported(DenAgentRuntimeValidation value) => !value.ConfigurationResolved
        ? new(AgentFailureCode.CapabilityUnavailable, "Current registry/model validation has unresolved dependencies.", value.Reference.AgentId)
        : value.Dependencies.Skills.Count != 0 || value.Dependencies.Packages.Count != 0
            ? new(AgentFailureCode.CapabilityUnavailable,
                "Exact Skill/package execution scope is not integrated into this original Chat adapter yet.", value.Reference.AgentId)
            : null;
    private static T? MeasuredOrEmpty<T>(UsageValue<T> value) where T : struct =>
        value.Availability == UsageAvailability.Measured ? value.Value :
        value.Availability == UsageAvailability.Empty ? default(T) : null;
    private static AgentResult<T> Refuse<T>(AgentFailureCode code, string message, string target) =>
        AgentResult<T>.Failure(new(code, message, target));
    private static AgentExecutionStepResult Failure(AgentFailure error, AgentBudgetUsage? usage = null,
        AgentExecutionSnapshot? original = null) =>
        new(AgentRunState.Failed, null, null, null, [], [], [], [],
            original?.CompletedConsequentialActionIds.ToArray() ?? [],
            original?.UncertainConsequentialActionIds.ToArray() ?? [],
            usage ?? AgentBudgetUsage.Empty, null, error with { Retryable = false });
}
