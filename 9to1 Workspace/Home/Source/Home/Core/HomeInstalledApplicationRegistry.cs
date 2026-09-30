using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Home.Core;

/// <summary>Profile-bound durable platform index. Inaccessible profiles retain identity but never expose launch authority.</summary>
public sealed class HomeInstalledApplicationRegistry(IHomeCoreStateStore store, IAuthenticatedResourceActorSource actors,
    IEnumerable<IInstalledApplicationObservationProvider> providers) : IInstalledApplicationRegistry
{
    private readonly IInstalledApplicationObservationProvider[] _providers = providers.ToArray();
    private sealed record State(string ProfileId, IReadOnlyList<InstalledApplicationReference> Applications);
    private static readonly AppOperability Unknown = new(AppOperabilityClassification.Unknown, AppOperabilityPath.TypedApi);

    public async ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct)
    {
        var actor = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        if (actor is null || !Text(actor.ActorId) || !Text(actor.ProfileId) || !Text(actor.AuthenticationRevision) || actor.AccountId == Guid.Empty || actor.OrganisationId is not null) throw new UnauthorizedAccessException("A verified personal Home profile is required.");
        if (actors is not IHomeStateCommitActorGuard commitGuard)
            throw new UnauthorizedAccessException("The current actor source cannot validate commit authority.");
        if (_providers.Any(p => !Text(p.ProviderId)) || _providers.GroupBy(p => p.ProviderId, StringComparer.Ordinal).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Installed application providers must have unique canonical identities.");
        var observed = new List<(string Provider, InstalledApplicationProfileObservation Profile)>();
        foreach (var provider in _providers)
        {
            var profiles = (await provider.ObserveAsync(ct).ConfigureAwait(false))?.Take(257).ToArray();
            if (profiles is null || profiles.Length > 256 || profiles.Any(p => p is null || !Text(p.PlatformProfileId) || !Text(p.Label) || p.Applications is null) ||
                profiles.GroupBy(p => p.PlatformProfileId, StringComparer.Ordinal).Any(g => g.Count() != 1))
                throw new InvalidDataException("Invalid platform profile observations.");
            foreach (var profile in profiles.ToArray())
            {
                var applications = profile.Applications.Take(10001).ToArray();
                if (applications.Length > 10000 || applications.Any(a => a is null || !Text(a.OsApplicationId) || !Text(a.Entrypoint) || !Text(a.Label) || a.Version?.Length > 1024 || (a.StableLaunchIdentity is not null && !Text(a.StableLaunchIdentity))) ||
                    applications.GroupBy(a => (a.OsApplicationId, a.Entrypoint)).Any(g => g.Count() != 1) ||
                    applications.Where(a => a.StableLaunchIdentity is not null).GroupBy(a => (a.OsApplicationId, a.StableLaunchIdentity)).Any(g => g.Count() != 1))
                    throw new InvalidDataException("Invalid platform application observations.");
                observed.Add((provider.ProviderId, profile with { Applications = applications }));
                if (observed.Sum(item => item.Profile.Applications.Count) > 100000) throw new InvalidDataException("Installed application observation exceeds the registry bound.");
            }
        }
        if (actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) throw new UnauthorizedAccessException("Home profile changed during platform discovery.");
        var id = "home.installed-apps." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.ProfileId)));
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var read = await store.ReadAsync(ct).ConfigureAwait(false);
            if (!read.IsSuccess) throw new InvalidDataException("Installed application state requires recovery.");
            var record = read.State!.Records.SingleOrDefault(r => r.RecordId == id);
            var previous = Read(record, actor.ProfileId);
            var next = previous.Applications.ToList();
            foreach (var (provider, profile) in observed)
            {
                var existing = next.Where(a => a.ProviderId == provider && a.PlatformProfileId == profile.PlatformProfileId).ToArray();
                if (!profile.Accessible)
                {
                    foreach (var item in existing) Replace(next, item, item with { ProfileAccessible = false, Revision = item.Revision + (item.ProfileAccessible ? 1 : 0) });
                    continue; // Quiet/work profiles are not authoritative empty inventories.
                }
                var matched = new HashSet<Guid>();
                foreach (var app in profile.Applications)
                {
                    // Exact component identity takes precedence over a provider's update locator.
                    var old = existing.SingleOrDefault(a => a.OsApplicationId == app.OsApplicationId && a.Entrypoint == app.Entrypoint);
                    if (old is null && app.StableLaunchIdentity is not null)
                    {
                        old = existing.SingleOrDefault(a => a.OsApplicationId == app.OsApplicationId &&
                            a.StableLaunchIdentity == app.StableLaunchIdentity);
                        // Migrate only a genuinely sole historical entry, including tombstones.
                        // Never choose one of several old activities after a package update.
                        var historical = existing.Where(a => a.OsApplicationId == app.OsApplicationId).ToArray();
                        if (old is null && historical.Length == 1 && historical[0].StableLaunchIdentity is null &&
                            profile.Applications.Count(a => a.OsApplicationId == app.OsApplicationId) == 1)
                            old = historical[0];
                    }
                    if (old is not null && old.Entrypoint != app.Entrypoint && profile.Applications.Any(a =>
                        a.OsApplicationId == old.OsApplicationId && a.Entrypoint == old.Entrypoint))
                        throw new InvalidDataException("Stable launch identity conflicts with an existing component.");
                    if (old is not null && !matched.Add(old.ApplicationId))
                        throw new InvalidDataException("Ambiguous stable launch identity; preserve the registry for recovery.");
                    var operability = app.Operability is { } declared && Enum.IsDefined(declared.Classification) && Enum.IsDefined(declared.Path) ? declared : Unknown;
                    var item = new InstalledApplicationReference(old?.ApplicationId ?? Guid.NewGuid(), actor.ProfileId, provider,
                        profile.PlatformProfileId, app.OsApplicationId, app.Entrypoint, app.Label, app.Version, app.Enabled,
                        true, profile.IsManaged, old?.Revision ?? 1, operability) { StableLaunchIdentity = app.StableLaunchIdentity };
                    if (old is not null && item != old) item = item with { Revision = old.Revision + 1 };
                    if (old is null) next.Add(item); else Replace(next, old, item);
                }
                foreach (var removed in existing.Where(a => !matched.Contains(a.ApplicationId)))
                    Replace(next, removed, removed with { Enabled = false, ProfileAccessible = true, Revision = removed.Revision + (removed.Enabled || !removed.ProfileAccessible ? 1 : 0) });
            }
            // Providers/profile scopes missing from this observation are unavailable, never deleted or granted.
            foreach (var missing in next.Where(a => !observed.Any(o => o.Provider == a.ProviderId && o.Profile.PlatformProfileId == a.PlatformProfileId)).ToArray())
                Replace(next, missing, missing with { ProfileAccessible = false, Revision = missing.Revision + (missing.ProfileAccessible ? 1 : 0) });
            if (next.Where(a => a.StableLaunchIdentity is not null).GroupBy(a =>
                (a.ProviderId, a.PlatformProfileId, a.OsApplicationId, a.StableLaunchIdentity)).Any(g => g.Count() != 1))
                throw new InvalidDataException("Conflicting stable launch identities; preserve the registry for recovery.");
            if (actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) throw new UnauthorizedAccessException("Home profile changed during registry reconciliation.");
            var write = await store.WriteGuardedAsync(new(id, "home.installed-apps", 2, HomeDataScope.DeviceLocal,
                HomeRecordAuthority.LocalCanonical, (record?.Revision ?? 0) + 1, JsonSerializer.SerializeToElement(new State(actor.ProfileId, next))), record?.Revision ?? 0, actor, commitGuard, ct).ConfigureAwait(false);
            if (write.IsSuccess)
            {
                if (actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) throw new UnauthorizedAccessException("Home profile changed during registry persistence.");
                return next.OrderBy(a => a.ProviderId, StringComparer.Ordinal).ThenBy(a => a.PlatformProfileId, StringComparer.Ordinal).ThenBy(a => a.ApplicationId).ToArray();
            }
            if (write.Failure?.Code == HomeCoreErrorCode.PermissionDenied)
                throw new UnauthorizedAccessException("Home profile changed before registry publication.");
            if (write.Failure?.Code != HomeCoreErrorCode.HomeStateConflict) throw new InvalidDataException("Installed application state could not be saved safely.");
        }
        throw new IOException("Installed application registry changed concurrently; retry discovery.");
    }

    public async ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid applicationId, long expectedRevision, CancellationToken ct)
    {
        if (applicationId == Guid.Empty || expectedRevision < 1) return null;
        return (await RefreshAsync(ct).ConfigureAwait(false)).SingleOrDefault(a => a.ApplicationId == applicationId &&
            a.Revision == expectedRevision && a.Enabled && a.ProfileAccessible);
    }
    private static State Read(HomeCoreStateRecord? record, string profile)
    {
        if (record is null) return new(profile, []);
        if (record.SchemaVersion is not (1 or 2) || record.RecordType != "home.installed-apps" || record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical)
            throw new InvalidDataException("Unsupported installed application registry; preserve it for recovery.");
        State state;
        try { state = record.Payload.Deserialize<State>() ?? throw new JsonException(); }
        catch (JsonException ex) { throw new InvalidDataException("Corrupt installed application registry; preserve it for recovery.", ex); }
        if (state.ProfileId != profile || state.Applications is null || state.Applications.Count > 100000 ||
            state.Applications.Any(a => a is null || a.ApplicationId == Guid.Empty || a.HomeProfileId != profile || a.Revision < 1 || a.Revision == long.MaxValue || !Text(a.Label) || a.Version?.Length > 1024 || !Text(a.ProviderId) || !Text(a.PlatformProfileId) || !Text(a.OsApplicationId) || !Text(a.Entrypoint) || (a.StableLaunchIdentity is not null && !Text(a.StableLaunchIdentity)) || a.Operability is null || !Enum.IsDefined(a.Operability.Classification) || !Enum.IsDefined(a.Operability.Path)) ||
            state.Applications.Select(a => a.ApplicationId).Distinct().Count() != state.Applications.Count ||
            state.Applications.GroupBy(a => (a.ProviderId, a.PlatformProfileId, a.OsApplicationId, a.Entrypoint)).Any(g => g.Count() != 1) ||
            state.Applications.Where(a => a.StableLaunchIdentity is not null).GroupBy(a =>
                (a.ProviderId, a.PlatformProfileId, a.OsApplicationId, a.StableLaunchIdentity)).Any(g => g.Count() != 1))
            throw new InvalidDataException("Invalid installed application ownership; preserve it for recovery.");
        return state;
    }
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096;
    private static void Replace(List<InstalledApplicationReference> values, InstalledApplicationReference old, InstalledApplicationReference next) => values[values.IndexOf(old)] = next;
}
