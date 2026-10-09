using Haven.Application;
namespace Haven.Desktop.Services;

/// <summary>The native host captures actual configured owners once. These adapters return
/// each SAME original close Task and confer no source, app, credential or permission authority.</summary>
internal sealed class DeveloperReadOriginalRetirementAdapter(IDeveloperProjectOriginalReadRetirementSource original)
    : IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    public void DemandExternalOriginalRetirementJoin() => original.DemandExternalOriginalReadAdmissionJoin();
    public void RequestRetirement() => original.RequestOriginalReadRetirement();
    public Task CloseAndDrainAsync() => original.CloseAndDrainOriginalReadsAsync();
}
internal sealed class DeveloperKernelOriginalRetirementAdapter(IDeveloperProjectOriginalPhysicalCaptureSource original)
    : IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    public void DemandExternalOriginalRetirementJoin() => original.DemandExternalOriginalJoin();
    public void RequestRetirement() => original.RequestOriginalCaptureRetirement();
    public Task CloseAndDrainAsync() => original.CloseAndDrainOriginalCapturesAsync();
}
internal sealed class CloudflareStagingOriginalRetirementAdapter(ICloudflareStagingDelegationSource original)
    : IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    public void DemandExternalOriginalRetirementJoin() => original.DemandExternalOriginalStagingJoin();
    public void RequestRetirement() => original.RequestOriginalStagingRetirement();
    public Task CloseAndDrainAsync() => original.CloseAndDrainOriginalStagingReviewsAsync();
}
