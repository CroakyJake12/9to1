using Haven.Core;
namespace Haven.Application;

/// <summary>Fresh selected LOCAL route capture before any attempt exists. SAME existing
/// issuer reads actual actor/provider/configuration/catalogue/policy under original custody;
/// the candidate grants no model, effect, artifact or cloud permission.</summary>
public interface ITaskRunOriginalSelectedRouteCaptureSource
{
    Task<TaskRunRouteCandidate> CaptureSelectedRouteWithinOriginalSourceAsync(TaskExecutionSnapshot current,
        ProviderModelDescriptor actualSelection, IReadOnlyCollection<ToolCapability> requiredCapabilities,
        IReadOnlyCollection<RestrictedModelCapability> requiredRestrictedCapabilities,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken);
}

public interface ITaskRunOriginalSelectedRouteObservationSource : ITaskRunRouteObservationSource
{
    Task<TaskRunRouteCandidate?> ObserveOriginalWithinSourceAsync(TaskExecutionSnapshot current,
        ProviderModelDescriptor actualModel, IReadOnlyList<string> requiredCapabilities,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken);
}
