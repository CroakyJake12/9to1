namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    internal void DemandOriginalCanonicalProcessComposition(TaskRunPermissionAuthority authority,
        TaskRunOriginalFrameOwner frames)
    {
        if (!ReferenceEquals(_admissionAuthority, authority) || !ReferenceEquals(_runtimeSettlement, frames))
            throw new InvalidOperationException("The process cohort must capture the canonical owner's same authority and frame settlement instances.");
    }

    internal void SealOriginalCanonicalProcessAdmission()
    {
        lock (_processProducerGate) _processProducerAdmissionSealed = true;
    }

    internal Task ReadOriginalCanonicalProcessRequest()
    {
        lock (_processProducerGate) return _originalProcessRequest
            ?? throw new InvalidOperationException("No actual coordinator process request was published.");
    }
}
