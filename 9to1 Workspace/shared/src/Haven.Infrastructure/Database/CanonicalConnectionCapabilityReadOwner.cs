using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Actual capability metadata READ for the SAME maintained connection
/// provider/repositories over the SAME protected canonical store. No service content,
/// credentials, remote discovery, schema lifecycle or invocation permission is read.</summary>
public sealed class CanonicalConnectionCapabilityReadOwner : ICapabilityOriginalDynamicReadSource
{
    private readonly CanonicalCapabilityCatalogueReadOwner _catalogue;
    private readonly ExternalConnectionRepository _connections;
    private readonly PlannerRepository _planner;
    private readonly ConnectionCapabilityProvider _provider;
    private readonly ConditionalWeakTable<ICapabilityOriginalRepositoryObservation, Observation> _issued = new();
    private sealed class Observation(CanonicalConnectionCapabilityReadOwner owner,
        CanonicalCapabilityCatalogueReadOwner.DynamicCatalogueRows actual) : ICapabilityOriginalRepositoryObservation
    {
        internal readonly CanonicalConnectionCapabilityReadOwner Owner = owner;
        internal readonly CanonicalCapabilityCatalogueReadOwner.DynamicCatalogueRows Rows = actual;
        public AuthenticatedResourceActor Actor => Rows.Actor;
        public CapabilityOriginalCatalogueState State => Rows.State;
        public string Detail => Rows.Detail;
        public IReadOnlyList<CapabilityDefinition> Definitions => Rows.Definitions;
    }
    public CanonicalConnectionCapabilityReadOwner(CanonicalCapabilityCatalogueReadOwner sameCatalogue,
        SqliteDatabase sameDatabase, ExternalConnectionRepository sameConnections,
        PlannerRepository samePlanner, ConnectionCapabilityProvider sameProvider)
    {
        ArgumentNullException.ThrowIfNull(sameCatalogue); ArgumentNullException.ThrowIfNull(sameDatabase);
        ArgumentNullException.ThrowIfNull(sameConnections); ArgumentNullException.ThrowIfNull(samePlanner);
        ArgumentNullException.ThrowIfNull(sameProvider);
        if (!sameCatalogue.OriginalStore.HasOriginalDatabase(sameDatabase) ||
            !sameConnections.HasOriginalSqliteFactory(sameDatabase) || !samePlanner.HasOriginalSqliteFactory(sameDatabase) ||
            !sameProvider.HasOriginalRepositories(sameConnections, samePlanner))
            throw new InvalidOperationException("The SAME actual canonical store/repositories/maintained provider are required.");
        _catalogue = sameCatalogue; _connections = sameConnections; _planner = samePlanner; _provider = sameProvider;
    }
    public CanonicalCapabilityCatalogueReadOwner OriginalCatalogue => _catalogue;
    public ExternalConnectionRepository OriginalConnections => _connections;
    public PlannerRepository OriginalPlanner => _planner;
    public ConnectionCapabilityProvider OriginalProvider => _provider;
    public bool HasOriginalDynamicProvider(IDynamicCapabilityProvider sameActual) => ReferenceEquals(_provider, sameActual);
    public bool IsIssuedOriginalDynamicObservation(ICapabilityOriginalRepositoryObservation sameActual) =>
        sameActual is Observation issued && ReferenceEquals(issued.Owner, this) &&
        _issued.TryGetValue(sameActual, out var original) && ReferenceEquals(issued, original);

    public async Task<ICapabilityOriginalRepositoryObservation> ReadOriginalDynamicCapabilitiesWithinSourceAsync(
        AuthenticatedResourceActor actor, CapabilityPlatform platform, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_catalogue.OriginalStore.OriginalSourceOwner, scope, retain);
        return await _catalogue.OriginalStore.RetainOriginalReader<ICapabilityOriginalRepositoryObservation>(source, async () =>
        {
            var actual = await source.Read(() => _catalogue.ReadOriginalConnectionMetadataWithinSourceAsync(this,
                actor, platform, source.Run, source.Retain, token)).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            var observation = new Observation(this, actual); source.Run(() => _issued.Add(observation, observation));
            return observation;
        }).ConfigureAwait(false);
    }
    public Task RevalidateOriginalDynamicObservationWithinSourceAsync(ICapabilityOriginalRepositoryObservation sameActual,
        AuthenticatedResourceActor actor, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_catalogue.OriginalStore.OriginalSourceOwner, scope, retain);
        return _catalogue.OriginalStore.RetainOriginalReader(source, async () =>
        {
            var original = source.Invoke(() => IsIssuedOriginalDynamicObservation(sameActual) && sameActual.Actor == actor
                ? ((Observation)sameActual).Rows : throw new UnauthorizedAccessException("The SAME dynamic source/current actor did not issue this metadata observation."));
            var fresh = await source.Read(() => _catalogue.ReadOriginalConnectionMetadataWithinSourceAsync(this,
                actor, original.Platform, source.Run, source.Retain, token)).ConfigureAwait(false);
            source.Run(() =>
            {
                if (original.Identity != fresh.Identity || original.Receipt != fresh.Receipt || original.State != fresh.State ||
                    JsonSerializer.Serialize(original.Definitions) != JsonSerializer.Serialize(fresh.Definitions))
                    throw new UnauthorizedAccessException("The actual canonical connection metadata or Home READ ownership changed. Refresh before use.");
            });
            await source.JoinAllAsync().ConfigureAwait(false); return 0;
        });
    }
}
