namespace Haven.Application;

/// <summary>Fresh scoped validation of the SAME already issued response attempt. This port
/// issues no attempt, route, context, action ACK, Home grant or native model-use permission.</summary>
public interface ITaskRunOriginalResponseAdmissionSource
{
    Task ValidateOriginalResponseAdmissionAsync(TaskRunAttemptAdmission sameAdmission,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken);
    void DemandOriginalResponseAdmission(TaskRunAttemptAdmission sameAdmission);
}

/// <summary>Optional callback-bearing validation on the SAME private configured cloud lease.
/// Returned Tasks are captured once before scope exit and independently joined; old validation
/// APIs and configured credential/context/use authorization retain their original semantics.</summary>
public interface ITaskRunOriginalScopedCloudAdmissionLease
{
    ValueTask RevalidateWithinOriginalSourceAsync(Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
}

/// <summary>Optional scoped actor and central cloud-use policy validation on the SAME actual
/// private permission lease. Interface presence and callback enrollment never grant cloud use.</summary>
public interface ITaskRunOriginalScopedCloudUsePermissionLease
{
    ValueTask RevalidateWithinOriginalSourceAsync(Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
}
