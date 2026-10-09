using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;

namespace HavenOS.Home.Core;

public interface IHomeGuardedModelRouteRepository : IVersionedModelRouteRepository
{
    Task<bool> TrySaveGuardedAsync(ConfiguredModelRoute route, long expectedRevision,
        AuthenticatedResourceActor expectedActor, IHomeStateCommitActorGuard guard, CancellationToken cancellationToken);
}

/// <summary>Home's durable CAS repository for the canonical Dulche ordered model-route contract.</summary>
public sealed class HomeVersionedModelRouteRepository(IHomeCoreStateStore store) : IHomeGuardedModelRouteRepository
{
    private const string RecordType = "home.model-route";
    public async Task<ConfiguredModelRoute?> GetAsync(string routeId, CancellationToken cancellationToken)
    {
        var state = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var record = state.Records.SingleOrDefault(r => r.RecordId == RecordId(routeId));
        return record is null ? null : Decode(record);
    }
    public async Task<IReadOnlyList<ConfiguredModelRoute>> ListAsync(CancellationToken cancellationToken) =>
        (await ReadAsync(cancellationToken).ConfigureAwait(false)).Records.Where(r => r.RecordType == RecordType).Select(Decode).ToArray();
    public async Task<bool> TrySaveAsync(ConfiguredModelRoute route, long expectedRevision, CancellationToken cancellationToken)
    {
        if (route.Revision != expectedRevision + 1) return false;
        var record = new HomeCoreStateRecord(RecordId(route.RouteId), RecordType, 1, HomeDataScope.DeviceLocal,
            HomeRecordAuthority.LocalCanonical, 0, JsonSerializer.SerializeToElement(route));
        return (await store.WriteAsync(record, expectedRevision, cancellationToken).ConfigureAwait(false)).IsSuccess;
    }
    public async Task<bool> TrySaveGuardedAsync(ConfiguredModelRoute route, long expectedRevision,
        AuthenticatedResourceActor expectedActor, IHomeStateCommitActorGuard guard, CancellationToken cancellationToken)
    {
        if (route.Revision != expectedRevision + 1 || route.Scope != ModelRouteScope.User || route.ScopeId != expectedActor.ProfileId ||
            route.RouteId != HomeModelPickerFeatureProvider.RouteId(expectedActor.ProfileId, route.Category))
            return false;
        var record = new HomeCoreStateRecord(RecordId(route.RouteId), RecordType, 1, HomeDataScope.DeviceLocal,
            HomeRecordAuthority.LocalCanonical, 0, JsonSerializer.SerializeToElement(route));
        return (await store.WriteGuardedAsync(record, expectedRevision, expectedActor, guard, cancellationToken).ConfigureAwait(false)).IsSuccess;
    }
    private async Task<HomeCoreStoredState> ReadAsync(CancellationToken cancellationToken)
    {
        var read = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        return read.IsSuccess ? read.State! : throw new InvalidOperationException(read.Failure?.Message ?? "Home route state unavailable.");
    }
    private static ConfiguredModelRoute Decode(HomeCoreStateRecord record)
    {
        if (record.SchemaVersion != 1) throw new InvalidDataException("Unsupported Home model-route schema; state preserved.");
        var route = record.Payload.Deserialize<ConfiguredModelRoute>() ?? throw new InvalidDataException("Model route payload is invalid.");
        if (record.RecordId != RecordId(route.RouteId) || record.Revision != route.Revision)
            throw new InvalidDataException("Model route identity/revision does not match its storage envelope.");
        return route;
    }
    private static string RecordId(string routeId) => $"home.model-route:{routeId}";
}
