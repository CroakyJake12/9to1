using Haven.Core;

namespace Haven.Application;

public enum TaskRunOriginalResumeObservationDisposition { ProducerTerminal = 0, ObservationDetached = 1 }

/// <summary>Presentation outcome only. Detached never means the producer completed or was cancelled.</summary>
public sealed record TaskRunOriginalResumeObservationResult(
    TaskRunOriginalResumeObservationDisposition Disposition, ProviderExecutionContext? CanonicalTaskContext,
    TaskExecutionLifecycle? State, bool RequiresInspection);

/// <summary>Issued only by the same coordinator retaining the actual resumed business producer.
/// No raw producer Task, permission, private input or effect authority is exposed.</summary>
public sealed class TaskRunOriginalResumeObservationLease
{
    internal readonly TaskExecutionCoordinator Issuer;
    internal readonly object Original;
    private readonly Func<Task<TaskRunOriginalResumeObservationResult>> _wait;
    private readonly Action _request;
    private readonly Func<Task> _detach;
    internal TaskRunOriginalResumeObservationLease(TaskExecutionCoordinator issuer, object original,
        ProviderExecutionContext context, Func<Task<TaskRunOriginalResumeObservationResult>> wait,
        Action request, Func<Task> detach)
    { Issuer = issuer; Original = original; CanonicalTaskContext = context; _wait = wait; _request = request; _detach = detach; }
    public ProviderExecutionContext CanonicalTaskContext { get; }
    public Task<TaskRunOriginalResumeObservationResult> WaitAsync() => _wait();
    public void RequestOriginalObservationRetirement() => _request();
    public Task DetachAndDrainAsync() => _detach();
}
