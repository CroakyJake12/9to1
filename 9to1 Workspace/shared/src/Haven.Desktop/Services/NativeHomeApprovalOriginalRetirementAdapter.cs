#if !ANDROID
using HavenOS.Home.NativeUI;
namespace Haven.Desktop.Services;

internal sealed class NativeHomeApprovalOriginalRetirementAdapter(HomeNativeApprovalWindowOwner original)
    : IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    public void DemandExternalOriginalRetirementJoin() => original.DemandExternalOriginalRetirementJoin();
    public void RequestRetirement() => original.RequestRetirement();
    public Task CloseAndDrainAsync() => original.CloseAndDrainAsync();
}
#endif
