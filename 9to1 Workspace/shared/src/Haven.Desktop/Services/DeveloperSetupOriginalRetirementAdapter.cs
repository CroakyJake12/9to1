using Haven.Application;
namespace Haven.Desktop.Services;

/// <summary>The host captures this SAME configured setup owner before any join. The
/// adapter returns the actual original close Task, never an IDisposable success proxy.</summary>
internal sealed class DeveloperSetupOriginalRetirementAdapter(IDeveloperProjectOriginalSetupPermissionSource original)
    : IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    public void DemandExternalOriginalRetirementJoin() => original.DemandExternalOriginalSetupJoin();
    public void RequestRetirement() => original.RequestOriginalSetupRetirement();
    public Task CloseAndDrainAsync() => original.CloseAndDrainOriginalSetupsAsync();
}
