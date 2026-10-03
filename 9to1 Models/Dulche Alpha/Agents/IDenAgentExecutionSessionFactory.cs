namespace Dulche.Runtime.Agents;

/// <summary>A trusted host binds the implementation to the SAME admitted Den/session/context.
/// References and cancellation tokens are observations, never a substitute for private Home admission.
/// The unavailable default does not create a coordinator, run or model/provider activity.</summary>
public interface IDenAgentExecutionSessionFactory
{
    ValueTask<AgentResult<IDenAgentExecutionSession>> OpenCurrentAsync(DenAgentReference reference,
        AgentInvocationContext originalContext, CancellationToken originalLifetime,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(
            AgentResult<IDenAgentExecutionSession>.Failure(new(AgentFailureCode.PermissionDenied,
                "The original Home execution session is not composed.", reference.AgentId)));
}

/// <summary>A scoped consumer of the existing canonical coordinator, with fresh original
/// host/context/lifetime checks. It exposes no catalog, Den Store, raw coordinator or tool executor.</summary>
public interface IDenAgentExecutionSession
{
    DenAgentReference Reference { get; }
    ValueTask<AgentResult<AgentExecutionSnapshot>> StartAsync(AgentRunRequest originalRequest, CancellationToken cancellationToken = default);
    ValueTask<AgentResult<AgentExecutionSnapshot>> GetAsync(string originalRunId, CancellationToken cancellationToken = default);
    ValueTask<AgentResult<AgentExecutionSnapshot>> ExecuteNextStepAsync(string originalRunId, CancellationToken cancellationToken = default);
    ValueTask<AgentResult<AgentExecutionSnapshot>> PauseAsync(string originalRunId, CancellationToken cancellationToken = default);
    ValueTask<AgentResult<AgentExecutionSnapshot>> StopAsync(string originalRunId, CancellationToken cancellationToken = default);
    ValueTask<AgentResult<AgentExecutionSnapshot>> CancelAsync(string originalRunId, CancellationToken cancellationToken = default);
    ValueTask<AgentResult<AgentExecutionSnapshot>> ResumeAsync(string originalRunId, CancellationToken cancellationToken = default);
    ValueTask<AgentResult<AgentExecutionSnapshot>> RecoverAsync(string originalRunId, CancellationToken cancellationToken = default);
}
