namespace Haven.Application;

/// <summary>Exact configured catalogue identity observation, not resource authority.</summary>
public interface ICanonicalMiniComputerCatalogLocation
{
    string OriginalCatalogPath { get; }
    Task<ICanonicalMiniComputerCatalogReservation> ReserveOriginalCatalogWithinSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}

public interface ICanonicalMiniComputerCatalogReservation : IAsyncDisposable
{
    bool IsOriginalCatalog(ICanonicalMiniComputerCatalogLocation sameCatalog);
    void DemandOriginalReservation();
}

/// <summary>Physical custody only. Product callers must independently verify the
/// current Home mini-computer.catalog receipt before requesting content bytes.</summary>
public interface ICanonicalMiniComputerProtectedCatalogRead : IAsyncDisposable
{
    ResourceStoreIdentity OriginalIdentity { get; }
    AuthenticatedResourceActor Actor { get; }
    bool IsOriginalCatalog(ICanonicalMiniComputerCatalogLocation sameCatalog);
    Task<ReadOnlyMemory<byte>> ReadOriginalBytesWithinSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token);
    Task RevalidateWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token);
    void DemandOriginalPinnedCatalog();
    Task? OriginalClose { get; }
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}
