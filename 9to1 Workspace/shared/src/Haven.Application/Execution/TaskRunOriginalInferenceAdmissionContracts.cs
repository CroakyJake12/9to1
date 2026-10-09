using Haven.Core;
namespace Haven.Application;

/// <summary>Scoped revalidation by the SAME existing permission issuer. No new model grant,
/// context grant, artifact revision or taskless startup permission is issued.</summary>
public interface ITaskRunOriginalInferenceAdmissionSource
{
    Task ValidateOriginalInferenceAdmissionAsync(TaskRunAttemptAdmission sameAdmission,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    void DemandOriginalInferenceAdmission(TaskRunAttemptAdmission sameAdmission);
}

/// <summary>The coordinator retains the actual row read and checks SAME private issuance;
/// IDs/public admission copies cannot replace the original object.</summary>
public interface ITaskRunOriginalInferenceAttemptSource
{
    Task<TaskRunAttemptAdmission?> GetIssuedAttemptWithinOriginalSourceAsync(TaskRunAttemptAdmission sameAdmission,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}

/// <summary>Optional original configured route source. Missing support refuses this new native
/// path; ordinary route revalidation remains unchanged.</summary>
public interface ITaskRunOriginalInferenceRouteSource
{
    Task DemandOriginalRouteWithinSourceAsync(TaskExecutionSnapshot sameOriginalSnapshot,
        TaskRunRouteCandidate sameOriginalObservation, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
}
