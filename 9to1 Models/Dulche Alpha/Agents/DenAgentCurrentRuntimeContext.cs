using Haven.Core;

namespace Dulche.Runtime.Agents;

/// <summary>Observations from the registered Home host for one exact current Den revision.
/// These values never grant Execute, model access, resource access or tool authority.
/// Missing, stale or ambiguous original host selection remains an explicit refusal.</summary>
public sealed record DenAgentCurrentRuntimeContext(DenAgentReference Reference,
    CapabilityPlatform Platform, string Scope, string? OriginalInheritedModelKey);

/// <summary>The registered Home host checks its SAME current actor, original Den Store/session
/// and reference before publishing observations. Original invocation/step paths require SAME
/// privately admitted object references; public copies and capability strings confer nothing.</summary>
public interface IDenAgentCurrentRuntimeContextSource
{
    ValueTask<AgentResult<DenAgentCurrentRuntimeContext>> GetForObservationAsync(
        DenAgentReference reference, CancellationToken cancellationToken = default) => Refuse(reference);
    ValueTask<AgentResult<DenAgentCurrentRuntimeContext>> GetForInvocationAsync(
        DenAgentReference reference, AgentInvocationContext originalInvocation,
        CancellationToken cancellationToken = default) => Refuse(reference);
    ValueTask<AgentResult<DenAgentCurrentRuntimeContext>> GetForStepAsync(
        DenAgentReference reference, AgentExecutionStep originalStep,
        CancellationToken cancellationToken = default) => Refuse(reference);

    private static ValueTask<AgentResult<DenAgentCurrentRuntimeContext>> Refuse(DenAgentReference reference) =>
        ValueTask.FromResult(AgentResult<DenAgentCurrentRuntimeContext>.Failure(new(
            AgentFailureCode.PermissionDenied, "The original current Home Agent runtime context is unavailable.",
            reference.AgentId)));
}
