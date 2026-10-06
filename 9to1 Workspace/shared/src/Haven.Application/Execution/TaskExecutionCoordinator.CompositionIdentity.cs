namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator
{
    /// <summary>Pure configured-instance identity only. This observes no actor, current
    /// permission, recovered ownership, model readiness or resource admission.</summary>
    public bool HasOriginalAdmissionAuthority(ITaskRunAdmissionAuthority sameAuthority) =>
        ReferenceEquals(_admissionAuthority, sameAuthority);
}
