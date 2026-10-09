namespace Haven.Application;

// Pure SAME issuer liveness at the actual native request factory; no new authorization.
public interface ITaskRunOriginalInferenceLeaseCurrentnessSource
{
    void DemandOriginalInferenceWithinSource(TaskRunAttemptAdmission sameAdmission);
}
