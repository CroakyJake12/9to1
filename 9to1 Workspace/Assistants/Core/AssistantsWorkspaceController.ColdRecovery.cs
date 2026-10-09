using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Core;

public sealed partial class AssistantsWorkspaceController
{
    public Task<AssistantColdTaskRecoveryPreview> ReadOriginalColdTaskRecoveryAsync(CancellationToken token = default)
    {
        var binding = DemandBinding();
        return CommandAsync(() => ObserveSourceAsync(() => DemandOriginalColdTaskOwner()
            .ReadOriginalColdTaskRecoveryAsync(binding, token)), false, token);
    }
    public Task<TaskRunOriginalResumeObservationLease> StartObservedOriginalColdTaskResumeAsync(
        AssistantColdTaskRecoveryPreview samePreview, CancellationToken token = default)
    {
        var binding = DemandBinding();
        return CommandAsync(async () =>
        {
            DemandObservationCapacity();
            var actual = await ObserveSourceAsync(() => DemandOriginalColdTaskOwner()
                .StartObservedOriginalColdTaskResumeAsync(binding, samePreview, token)).ConfigureAwait(false);
            RetainObservation(new(actual, actual.RequestOriginalObservationRetirement,
                _bridge.DemandExternalOriginalRetirementJoin, actual.DetachAndDrainAsync));
            return actual;
        }, false, token);
    }
    private IAssistantOriginalColdTaskRecoveryOwner DemandOriginalColdTaskOwner() =>
        _bridge as IAssistantOriginalColdTaskRecoveryOwner ?? throw IssueLocalRefusal("Recovery is not configured for this host.");
}
