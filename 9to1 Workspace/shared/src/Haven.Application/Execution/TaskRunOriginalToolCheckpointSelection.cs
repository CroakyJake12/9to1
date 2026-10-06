using Haven.Core;

namespace Haven.Application;

/// <summary>Opaque router-issued selection observation. It is neither an admission nor a
/// permission, no-effect, completion or retirement grant. Resume remains the coordinator's work.</summary>
public interface TaskRunOriginalToolCheckpointSelection
{
    TaskRunOriginalFinalRequestFailure OriginalFailure { get; }
    Task OriginalSelectionTask { get; }
    IReadOnlyList<Task> OriginalSelectionSources { get; }
    TaskExecutionSnapshot ActualCurrentSnapshot { get; }
    ProviderModelDescriptor ActualSelectedModel { get; }
    TaskRunRouteCandidate ActualSelectedCandidate { get; }
    LocalModelEndpointObservation OriginalLocalEndpointObservation { get; }
}

/// <summary>The SAME configured router performs a fresh actual local catalogue/actor/model
/// policy capture for a retained failed tool-response call. A missing observation is unavailable.</summary>
public interface ITaskRunOriginalToolCheckpointSelectionSource
{
    Task<TaskRunOriginalToolCheckpointSelection?> SelectLocalForOriginalToolCheckpointAsync(
        TaskRunOriginalFinalRequestFailure sameFailure, TaskExecutionSnapshot actualCurrentSnapshot,
        CancellationToken cancellationToken);
    bool IsIssuedOriginalToolCheckpointSelection(TaskRunOriginalToolCheckpointSelection selection,
        TaskRunOriginalFinalRequestFailure sameFailure, TaskExecutionSnapshot actualCurrentSnapshot);
}
