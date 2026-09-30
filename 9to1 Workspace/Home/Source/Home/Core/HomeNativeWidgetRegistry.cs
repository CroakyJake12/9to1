using System.Collections.Concurrent;
using System.Collections.Frozen;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed record HomeNativeWidgetSize(int Columns, int Rows);
public enum HomeNativeWidgetUpdateMode { Manual, Event, Interval }
/// <summary>Persistable locator only. A copied locator is never a live provider, permission or execution token.</summary>
public sealed record HomeNativeWidgetReference(string AppId, Guid InstalledApplicationId,
    string InstallationRevision, string WidgetId, string DefinitionRevision);
/// <summary>Owner-authored metadata. Action IDs resolve through the existing Home action broker; these
/// declarations do not supply policy, arguments, approval, executable paths or arbitrary CUI documents.</summary>
public sealed record HomeNativeWidgetDefinition(string WidgetId, string Revision, string Label,
    HomeNativeWidgetSize MinimumSize, HomeNativeWidgetSize DefaultSize, HomeNativeWidgetSize MaximumSize,
    string ConfigurationSchemaReference, string SurfaceReference, HomeNativeWidgetUpdateMode UpdateMode,
    int? IntervalSeconds, IReadOnlyList<string> ActionIds, IReadOnlyList<ResourceScope> DataScopes);
public sealed record HomeNativeWidgetResolution(HomeNativeWidgetReference Reference, HomeNativeWidgetDefinition Definition);

/// <summary>Process-lifetime catalogue of verified live owners. Trusted transport composition must obtain
/// observed peers from actual OS credentials, never from request JSON. No installer catalogue or dynamic
/// loader is fabricated here. Owners register metadata through their admitted live transport and dispose
/// its registration on disconnect. Every lookup rechecks that exact peer and current resource authority.</summary>
public sealed class HomeNativeWidgetRegistry(IHomeNativeInstalledPeerVerifier verifier,
    IAuthenticatedResourceActorSource actors, ResourceAuthorizationService resources)
{
    public const string ServiceId = "home.widgets";
    public const string RenderActionId = "home.widget.render";
    private sealed record Entry(HomeNativeObservedPeer Observed, HomeNativeInstalledPeer Owner,
        AuthenticatedResourceActor Actor, IReadOnlyList<HomeNativeWidgetDefinition> Definitions);
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly object _registrationGate = new();

    public async ValueTask<IDisposable?> RegisterAsync(HomeNativeObservedPeer observed,
        IReadOnlyList<HomeNativeWidgetDefinition> definitions, CancellationToken ct = default)
    {
        if (observed is null || observed.ProcessId <= 0 || !Text(observed.OperatingSystemPrincipalId) || definitions is null)
            return null;
        var snapshot = Snapshot(definitions);
        var actor = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        if (actor is null || !Text(actor.ActorId) || !Text(actor.ProfileId) || !Text(actor.AuthenticationRevision)) return null;
        var owner = Copy(await verifier.VerifyAsync(observed, ct).ConfigureAwait(false));
        if (owner is null) return null;
        var entry = new Entry(observed, owner, actor, snapshot);
        if (!await CurrentAsync(entry, ct).ConfigureAwait(false)) return null;
        var id = Guid.NewGuid();
        lock (_registrationGate)
        {
            if (_entries.Count >= 128 || !_entries.TryAdd(id, entry)) return null;
        }
        // The registration is nonserializable and only removes this admitted lifetime.
        return new Registration(() => _entries.TryRemove(id, out _));
    }

    public async ValueTask<IReadOnlyList<HomeNativeWidgetResolution>> ListAsync(CancellationToken ct = default)
    {
        var actor = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        if (actor is null) return [];
        var result = new List<HomeNativeWidgetResolution>();
        foreach (var (id, entry) in _entries.ToArray())
        {
            if (!await CurrentAsync(entry, ct).ConfigureAwait(false)) continue;
            foreach (var definition in entry.Definitions)
            {
                if (!await CanReadAsync(entry, definition, ct).ConfigureAwait(false)) continue;
                if (!_entries.TryGetValue(id, out var current) || !ReferenceEquals(current, entry)) break;
                result.Add(new(Reference(entry.Owner, definition), definition));
            }
        }
        // Duplicate live declarations are ambiguous, not a reason to select the first process.
        var duplicate = result.GroupBy(item => item.Reference).Where(group => group.Count() != 1)
            .Select(group => group.Key).ToHashSet();
        var currentResults = new List<HomeNativeWidgetResolution>();
        foreach (var item in result.Where(item => !duplicate.Contains(item.Reference)))
        {
            var current = await ResolveAsync(item.Reference, ct).ConfigureAwait(false);
            if (current is not null) currentResults.Add(current);
        }
        if (actor != await actors.GetCurrentAsync(ct).ConfigureAwait(false)) return [];
        return Array.AsReadOnly(currentResults.ToArray());
    }

    public async ValueTask<HomeNativeWidgetResolution?> ResolveAsync(HomeNativeWidgetReference reference,
        CancellationToken ct = default)
    {
        if (reference is null) return null;
        var matches = _entries.ToArray().SelectMany(pair => pair.Value.Definitions
            .Where(definition => Reference(pair.Value.Owner, definition) == reference)
            .Select(definition => (pair.Key, Entry: pair.Value, Definition: definition))).Take(2).ToArray();
        if (matches.Length != 1) return null;
        var match = matches[0];
        if (!await CanReadAsync(match.Entry, match.Definition, ct).ConfigureAwait(false) ||
            !_entries.TryGetValue(match.Key, out var current) || !ReferenceEquals(current, match.Entry)) return null;
        return new(reference, match.Definition);
    }

    private async ValueTask<bool> CanReadAsync(Entry entry, HomeNativeWidgetDefinition definition, CancellationToken ct)
    {
        if (!await CurrentAsync(entry, ct).ConfigureAwait(false)) return false;
        if (definition.DataScopes.Count > 0 &&
            await resources.AuthorizeAsync(RenderActionId, definition.DataScopes, ct).ConfigureAwait(false) != entry.Actor) return false;
        return await CurrentAsync(entry, ct).ConfigureAwait(false);
    }
    private async ValueTask<bool> CurrentAsync(Entry entry, CancellationToken ct)
    {
        var peer = Copy(await verifier.VerifyAsync(entry.Observed, ct).ConfigureAwait(false));
        return peer is not null && peer.AppId == entry.Owner.AppId &&
            peer.InstalledApplicationId == entry.Owner.InstalledApplicationId &&
            peer.InstallationRevision == entry.Owner.InstallationRevision && peer.ExecutableIdentity == entry.Owner.ExecutableIdentity &&
            await actors.GetCurrentAsync(ct).ConfigureAwait(false) == entry.Actor;
    }
    private static HomeNativeInstalledPeer? Copy(HomeNativeInstalledPeer? peer) => peer is null ||
        !Text(peer.AppId) || peer.InstalledApplicationId == Guid.Empty || !Text(peer.InstallationRevision) ||
        !Text(peer.ExecutableIdentity) || peer.AllowedServiceIds is null || !peer.AllowedServiceIds.Contains(ServiceId)
        ? null : peer with { AllowedServiceIds = peer.AllowedServiceIds.ToFrozenSet(StringComparer.Ordinal),
            Roles = (peer.Roles ?? FrozenSet<string>.Empty).ToFrozenSet(StringComparer.Ordinal) };
    private static HomeNativeWidgetReference Reference(HomeNativeInstalledPeer owner, HomeNativeWidgetDefinition definition) =>
        new(owner.AppId, owner.InstalledApplicationId, owner.InstallationRevision, definition.WidgetId, definition.Revision);
    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096;
    private static bool Size(HomeNativeWidgetSize? size) => size is { Columns: >= 1 and <= 64, Rows: >= 1 and <= 64 };
    private static IReadOnlyList<HomeNativeWidgetDefinition> Snapshot(IReadOnlyList<HomeNativeWidgetDefinition> definitions)
    {
        var input = definitions.Take(257).ToArray();
        if (input.Any(value => value is null || value.ActionIds is null || value.DataScopes is null))
            throw new InvalidDataException("Widget action and data declarations must be explicit.");
        var values = input.Select(value => value is null ? null : value with {
            ActionIds = Array.AsReadOnly((value.ActionIds ?? []).Take(101).ToArray()),
            DataScopes = Array.AsReadOnly((value.DataScopes ?? []).Take(101).ToArray()) }).ToArray();
        if (values.Length is 0 or > 256 || values.Any(value => value is null || !Text(value.WidgetId) || !Text(value.Revision) ||
            !Text(value.Label) || !Text(value.ConfigurationSchemaReference) || !Text(value.SurfaceReference) ||
            !Size(value.MinimumSize) || !Size(value.DefaultSize) || !Size(value.MaximumSize) ||
            value.MinimumSize.Columns > value.DefaultSize.Columns || value.DefaultSize.Columns > value.MaximumSize.Columns ||
            value.MinimumSize.Rows > value.DefaultSize.Rows || value.DefaultSize.Rows > value.MaximumSize.Rows ||
            !Enum.IsDefined(value.UpdateMode) || (value.UpdateMode == HomeNativeWidgetUpdateMode.Interval
                ? value.IntervalSeconds is null or < 60 or > 86400 : value.IntervalSeconds is not null) ||
            value.ActionIds.Count > 100 || value.ActionIds.Any(action => !Text(action)) ||
            value.ActionIds.Distinct(StringComparer.Ordinal).Count() != value.ActionIds.Count ||
            value.DataScopes.Count > 100 || value.DataScopes.Any(scope => scope is null || scope.Access != ResourceAccess.Read ||
                !Text(scope.Kind) || !Text(scope.Id) || !Text(scope.Revision))) ||
            values.GroupBy(value => value!.WidgetId, StringComparer.Ordinal).Any(group => group.Count() != 1))
            throw new InvalidDataException("Widget definitions must be bounded, typed and unambiguous.");
        long textUnits = 0;
        foreach (var value in values)
        {
            textUnits += value!.WidgetId.Length + value.Revision.Length + value.Label.Length +
                value.ConfigurationSchemaReference.Length + value.SurfaceReference.Length;
            foreach (var action in value.ActionIds) textUnits += action.Length;
            foreach (var scope in value.DataScopes) textUnits += scope.Kind.Length + scope.Id.Length + scope.Revision.Length;
        }
        if (textUnits > 262144) throw new InvalidDataException("Widget definition metadata exceeds the session bound.");
        return Array.AsReadOnly(values.Select(value => value!).ToArray());
    }
    private sealed class Registration(Action remove) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) remove(); }
    }
}
