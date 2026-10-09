using Haven.Core;

namespace Haven.Application;

/// <summary>Observation-only result. Detached presentation never labels the canonical producer complete or canceled.</summary>
public enum AgentRunObservationDisposition { ProducerTerminal = 0, ObservationDetached = 1 }

/// <summary>TerminalRun is present only for the actual successfully returned producer result.
/// This public value is neither an invocation, permission, recovery nor completion grant.</summary>
public sealed record AgentRunObservationResult(AgentRunObservationDisposition Disposition, AgentRun? TerminalRun);

/// <summary>Actual service-issued presentation lease. UI code cannot construct it or acquire the producer Task.
/// Exact issuance provenance survives observation retirement; current admission is a separate owner check.</summary>
public sealed class AgentRunOriginalObservationLease
{
    internal readonly AgentTaskRuntimeService Issuer;
    internal readonly object Original;
    private readonly Func<Task<AgentRunObservationResult>> _wait;
    private readonly Action _request;
    private readonly Func<Task> _drain;
    internal AgentRunOriginalObservationLease(AgentTaskRuntimeService issuer, object original,
        Func<Task<AgentRunObservationResult>> wait, Action request, Func<Task> drain)
    { Issuer = issuer; Original = original; _wait = wait; _request = request; _drain = drain; }
    public Task<AgentRunObservationResult> WaitOriginalObservationAsync() => _wait();
    public void RequestOriginalObservationRetirement() => _request();
    public Task DetachAndDrainOriginalObservationAsync() => _drain();
}

/// <summary>Optional genuine issuer port implemented only by the existing Agent service.
/// Start retains the SAME existing producer before any callback; producer cancellation remains the original explicit token.
/// Presentation withdrawal seals only this observation and joins its real wait/retirement originals.</summary>
public interface IAgentRunOriginalObservationSource
{
    AgentRunOriginalObservationLease StartObservedOriginalRun(Guid agentId, string task,
        CancellationToken cancellationToken, Guid? retryOfRunId = null, string? resourceReference = null);
    AgentRunOriginalObservationLease StartObservedOriginalRetry(Guid runId, CancellationToken cancellationToken);
    bool IsIssuedOriginalObservation(AgentRunOriginalObservationLease lease);
}
