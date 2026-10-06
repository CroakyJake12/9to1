namespace Haven.Application;

public sealed partial class TaskRunPermissionAuthority
{
    private sealed partial class Lease : ITaskRunOriginalInferenceLeaseSource, ITaskRunOriginalInferenceLeaseCurrentnessSource
    {
        public Task RevalidateOriginalInferenceWithinSourceAsync(TaskRunAttemptAdmission sameAdmission,
            Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sameAdmission);
            if (!ReferenceEquals(sameAdmission.Lease, this))
                throw new UnauthorizedAccessException("Scoped native model use requires this SAME actual issued lease.");
            // Return the SAME genuine authority driver; no ordinary Revalidate proxy.
            return issuer.ValidateOriginalInferenceAdmissionAsync(sameAdmission,
                originalSynchronousScope, retainOriginalTask, cancellationToken);
        }
        public void DemandOriginalInferenceWithinSource(TaskRunAttemptAdmission sameAdmission)
        {
            ArgumentNullException.ThrowIfNull(sameAdmission);
            if (!ReferenceEquals(sameAdmission.Lease, this))
                throw new UnauthorizedAccessException("Native request currentness requires this SAME actual issued lease.");
            issuer.DemandOriginalInferenceAdmission(sameAdmission);
        }
    }
}
