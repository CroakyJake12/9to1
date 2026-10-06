using System.Runtime.CompilerServices;
namespace Haven.Application;

public sealed partial class TaskRunPermissionAuthority : ITaskRunOriginalIssuedRouteConfigurationSource
{
    private readonly ConditionalWeakTable<TaskRunOriginalIssuedRouteConfiguration, object> _issuedRouteConfigurations = new();
    public TaskRunOriginalIssuedRouteConfiguration? TryObserveOriginalIssuedRouteConfiguration(TaskRunAttemptAdmission sameAdmission)
    {
        ArgumentNullException.ThrowIfNull(sameAdmission);
        lock (_sync)
        {
            Owner owner;
            try { owner = RequireOwner(sameAdmission.Snapshot); }
            catch (UnauthorizedAccessException) { return null; }
            catch (ObjectDisposedException) { return null; }
            if (sameAdmission.Lease is not Lease lease || lease.AttemptId != sameAdmission.AttemptId ||
                !ReferenceEquals(owner.Attempts.GetValueOrDefault(sameAdmission.AttemptId), lease) ||
                lease.Owner != sameAdmission.Snapshot.OwnerBinding) return null;
            try { lease.DemandOriginalToolAdmission(); }
            catch (UnauthorizedAccessException) { return null; }
            var observation = new TaskRunOriginalIssuedRouteConfiguration(sameAdmission, lease.OriginalCapturedRouteConfiguration);
            _issuedRouteConfigurations.Add(observation, lease); return observation;
        }
    }
    public bool IsIssuedOriginalRouteConfiguration(TaskRunOriginalIssuedRouteConfiguration sameObservation, TaskRunAttemptAdmission sameAdmission)
    {
        ArgumentNullException.ThrowIfNull(sameObservation); ArgumentNullException.ThrowIfNull(sameAdmission);
        lock (_sync)
            return _issuedRouteConfigurations.TryGetValue(sameObservation, out var original) && original is Lease lease &&
                ReferenceEquals(sameObservation.OriginalAdmission, sameAdmission) && ReferenceEquals(sameAdmission.Lease, lease) &&
                sameAdmission.AttemptId == lease.AttemptId && ReferenceEquals(sameObservation.OriginalCapturedConfiguration, lease.OriginalCapturedRouteConfiguration) &&
                _owners.Values.Any(owner => ReferenceEquals(owner.Attempts.GetValueOrDefault(lease.AttemptId), lease));
        // Pure retained historical membership remains valid after settlement/close or actor renewal.
        // It does not reopen that lease, revalidate policy, identify later wire bytes or certify server effects.
    }
}
