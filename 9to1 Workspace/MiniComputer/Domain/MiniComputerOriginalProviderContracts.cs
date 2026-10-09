using Haven.Application;

namespace HavenOS.Apps.MiniComputer;

/// <summary>Issued only by the actual canonical engine after its configured source
/// has pinned the selected catalogue row and an exact Home operation claim.</summary>
public sealed class MiniComputerOriginalProviderTarget
{
    private readonly MiniComputerEngine _engine;
    private readonly IVirtualisationProvider _provider;
    private readonly ICanonicalMiniComputerOperationIntent _intent;
    internal MiniComputerOriginalProviderTarget(MiniComputerEngine engine, IVirtualisationProvider provider,
        VirtualMachine original, ICanonicalMiniComputerOperationIntent intent)
    { _engine = engine; _provider = provider; Original = original; _intent = intent; }
    public VirtualMachine Original { get; }
    public CanonicalMiniComputerAction Action => _intent.Action;
    internal void Demand(IVirtualisationProvider sameProvider)
    {
        if (!ReferenceEquals(_provider, sameProvider))
            throw new UnauthorizedAccessException("The original VM belongs to another configured provider.");
        _engine.DemandOriginalProviderTarget(this, _provider, _intent);
    }
}

public sealed record MiniComputerOriginalProviderObservation(ProviderStateObservation State,
    bool WasDispatched, bool IsPending, string Reason);

/// <summary>The actual maintained provider's original callback/child-task path.
/// The target cannot be constructed from saved VM IDs or host Computer Use tokens.</summary>
public interface IOriginalScopedVirtualisationProvider
{
    Task<MiniComputerOriginalProviderObservation> InvokeOriginalTargetWithinSourceAsync(
        MiniComputerOriginalProviderTarget sameTarget, Action<Action> scope,
        Action<Task> retain, CancellationToken token);
    Task? OriginalClose { get; }
    void DemandExternalOriginalRetirementJoin();
    void RequestOriginalRetirement();
    Task CloseAndDrainOriginalAsync();
}
