namespace Haven.Application;

public sealed partial class AgentTaskRuntimeService
{
    internal void DemandOriginalCanonicalProcessCoordinator(TaskExecutionCoordinator coordinator) =>
        chat.DemandOriginalCanonicalProcessCoordinator(coordinator);

    internal void SealOriginalCanonicalProcessAdmission()
    {
        lock (_agentProcessGate) _agentProcessSealed = true;
    }

    internal Task ReadOriginalCanonicalProcessRequest()
    {
        lock (_agentProcessGate) return _agentProcessRequest
            ?? throw new InvalidOperationException("No actual Agent process request was published.");
    }
}
