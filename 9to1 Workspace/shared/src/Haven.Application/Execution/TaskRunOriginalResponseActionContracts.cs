using Haven.Core;

namespace Haven.Application;

/// <summary>The SAME source-owned response operation. Reservation is identity only, never action or provider authority.</summary>
public interface ITaskRunOriginalResponseActionSource
{
    Task<TaskRunOriginalResponseActionAcknowledgment> BindOriginalResponseAsync(
        OllamaChatRequest sameOriginalRequest, TaskRunAttemptAdmission sameOriginalAdmission,
        TaskExecutionSnapshot actualRunningSnapshot, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
    Task<TaskRunOriginalResponseActionAcknowledgment> BindOriginalResponseAsync(
        OllamaToolRequest sameOriginalRequest, TaskRunAttemptAdmission sameOriginalAdmission,
        TaskExecutionSnapshot actualRunningSnapshot, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
    bool IsIssuedOriginalResponseActionAcknowledgment(TaskRunOriginalResponseActionAcknowledgment sameAcknowledgment,
        OllamaChatRequest sameOriginalRequest, TaskRunAttemptAdmission sameOriginalAdmission);
    bool IsIssuedOriginalResponseActionAcknowledgment(TaskRunOriginalResponseActionAcknowledgment sameAcknowledgment,
        OllamaToolRequest sameOriginalRequest, TaskRunAttemptAdmission sameOriginalAdmission);
}

/// <summary>Observation of the exact response-action CAS and original custody. Public fields issue no grant, acceptance or effect-absence proof.</summary>
public sealed class TaskRunOriginalResponseActionAcknowledgment
{
    internal TaskRunOriginalResponseActionAcknowledgment(ITaskRunOriginalResponseActionSource issuer,
        object sameRequest, TaskRunAttemptAdmission sameAdmission, Guid actionId,
        TaskExecutionSnapshot acknowledged, Task originalRegistration, IReadOnlyList<Task> sources)
    {
        Issuer = issuer; OriginalSelf = this; OriginalRequest = sameRequest; OriginalAdmission = sameAdmission;
        ActualActionId = actionId; AcknowledgedSnapshot = acknowledged; OriginalRegistration = originalRegistration;
        OriginalSources = Array.AsReadOnly(sources.ToArray());
    }
    internal ITaskRunOriginalResponseActionSource Issuer { get; }
    internal object OriginalSelf { get; }
    public Guid ActualActionId { get; }
    public TaskExecutionSnapshot AcknowledgedSnapshot { get; }
    public object OriginalRequest { get; }
    public TaskRunAttemptAdmission OriginalAdmission { get; }
    public Task OriginalRegistration { get; }
    public IReadOnlyList<Task> OriginalSources { get; }
}
