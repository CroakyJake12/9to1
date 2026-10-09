using Haven.Application;

namespace Haven.Desktop.Services;

// The process host captures this exact configured business owner. A page, window,
// or observation lease never receives this adapter or retires the global owner.
internal sealed class CanonicalProcessOriginalRetirementAdapter(TaskRunCanonicalProcessRetirementOwner original)
    : IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly TaskRunCanonicalProcessRetirementOwner _original = original
        ?? throw new ArgumentNullException(nameof(original));

    public void DemandExternalOriginalRetirementJoin() => _original.DemandExternalOriginalProcessJoin();
    public void RequestRetirement() => _original.RequestOriginalProcessRetirement();
    public Task CloseAndDrainAsync() => _original.CloseAndSuspendOriginalProducersAsync();
}
