using Haven.Core;

namespace Haven.Application;

public enum CapabilityOriginalCatalogueState { Available, SetupRequired }

/// <summary>Actual repository observation, privately issued only after scoped protected
/// SQL/current Home READ. Setup has no definitions and grants no store/tool access.</summary>
public interface ICapabilityOriginalRepositoryObservation
{
    AuthenticatedResourceActor Actor { get; }
    CapabilityOriginalCatalogueState State { get; }
    string Detail { get; }
    IReadOnlyList<CapabilityDefinition> Definitions { get; }
}
public interface ICapabilityOriginalRepositoryReadSource
{
    bool HasOriginalCapabilityRepository(ICapabilityRepository sameActualRepository);
    Task<ICapabilityOriginalRepositoryObservation> ReadOriginalCapabilitiesWithinSourceAsync(
        AuthenticatedResourceActor sameActualActor, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalRepositoryObservation(ICapabilityOriginalRepositoryObservation sameActual);
    Task RevalidateOriginalRepositoryObservationWithinSourceAsync(
        ICapabilityOriginalRepositoryObservation sameActual, AuthenticatedResourceActor sameActualActor,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}
/// <summary>SAME maintained registry discovery over its owning protected READ source.
/// Unscoped dynamic providers remain unavailable on this protected READ path.
/// Returned records are metadata; runtime/model/Home policy still decide each effect.</summary>
public sealed class CapabilityOriginalCatalogueObservation
{
    internal CapabilityOriginalCatalogueObservation(object issuer, ICapabilityOriginalRepositoryReadSource source,
        ICapabilityOriginalRepositoryObservation repository, CapabilityPlatform platform,
        IReadOnlyList<CapabilityDefinition> definitions, bool unscopedDynamicProviders)
    { Issuer = issuer; OriginalSource = source; OriginalRepository = repository; Platform = platform; Definitions = definitions;
        HasUnscopedDynamicProviders = unscopedDynamicProviders; }
    internal object Issuer { get; }
    internal ICapabilityOriginalRepositoryReadSource OriginalSource { get; }
    internal ICapabilityOriginalRepositoryObservation OriginalRepository { get; }
    public AuthenticatedResourceActor Actor => OriginalRepository.Actor;
    public CapabilityOriginalCatalogueState State => OriginalRepository.State;
    public string Detail => OriginalRepository.Detail;
    public bool HasUnscopedDynamicProviders { get; }
    public CapabilityPlatform Platform { get; }
    public IReadOnlyList<CapabilityDefinition> Definitions { get; }
}
