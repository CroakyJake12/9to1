using System.Collections.ObjectModel;
using System.Text.Json;

namespace Haven.Application;

public enum SpaceSwitcherLayout { Rail, Dropdown }

/// <summary>A local Home-owned settings observation. This is not an account sync receipt or a navigation grant.</summary>
public sealed record SpaceNavigationPreferenceSnapshot(long Revision, SpaceSwitcherLayout Layout,
    IReadOnlyList<Guid> PinnedSpaceIds, IReadOnlyDictionary<string, bool> CollapsedSidebars)
{
    public bool IsCollapsed(Guid? spaceId) => CollapsedSidebars.GetValueOrDefault(SpaceNavigationPreferences.ScopeKey(spaceId));
}

/// <summary>Native local-profile preferences on the SAME settings and Home ownership authority.
/// Every mutation uses original exact settings JSON and an admission checked under the shared store lease.
/// Nothing here creates a Space, changes the active conversation, or represents account-wide synchronisation.</summary>
public sealed class SpaceNavigationPreferences(VersionedAtomicSettingsStore settings, SpaceLocalStoreAuthority authority)
{
    private const string Key = "spaces.navigation.local.v1";
    private const string RegistryKey = "spaces.registry";
    private const int MaximumEntries = 4096;
    private sealed record State(int SchemaVersion, long Revision, SpaceSwitcherLayout Layout,
        Guid[] Pins, Dictionary<string, bool> Collapsed);

    public async Task<SpaceNavigationPreferenceSnapshot> ReadAsync(CancellationToken token = default)
    {
        var admission = await authority.CaptureWriteAdmissionAsync(token).ConfigureAwait(false);
        var snapshot = await settings.ExportAsync(token).ConfigureAwait(false);
        await DemandCurrentAsync(admission, snapshot, token).ConfigureAwait(false);
        snapshot.Settings.TryGetValue(Key, out var raw);
        return Project(Parse(raw));
    }

    public Task<SpaceNavigationPreferenceSnapshot> SetLayoutAsync(SpaceSwitcherLayout layout, long expectedRevision, CancellationToken token = default)
    {
        if (!Enum.IsDefined(layout)) throw new ArgumentOutOfRangeException(nameof(layout));
        return MutateAsync(expectedRevision, (state, _) => state with { Layout = layout }, token);
    }

    public Task<SpaceNavigationPreferenceSnapshot> SetCollapsedAsync(Guid? spaceId, bool collapsed, long expectedRevision, CancellationToken token = default) =>
        MutateAsync(expectedRevision, (state, registry) =>
        {
            if (spaceId is { } id) DemandExisting(id, registry);
            var next = new Dictionary<string, bool>(state.Collapsed, StringComparer.Ordinal) { [ScopeKey(spaceId)] = collapsed };
            return state with { Collapsed = next };
        }, token);

    public Task<SpaceNavigationPreferenceSnapshot> SetPinnedAsync(Guid spaceId, bool pinned, long expectedRevision, CancellationToken token = default)
    {
        if (spaceId == Guid.Empty) throw new ArgumentException("A pin requires a canonical Space identity.", nameof(spaceId));
        return MutateAsync(expectedRevision, (state, registry) =>
        {
            if (pinned) DemandExisting(spaceId, registry);
            var next = state.Pins.Where(id => id != spaceId).ToList();
            if (pinned) next.Add(spaceId);
            // Pinning an already pinned item preserves its existing position.
            return state with { Pins = pinned && state.Pins.Contains(spaceId) ? state.Pins : next.ToArray() };
        }, token);
    }

    public Task<SpaceNavigationPreferenceSnapshot> MovePinAsync(Guid spaceId, int destinationIndex, long expectedRevision, CancellationToken token = default) =>
        MutateAsync(expectedRevision, (state, _) =>
        {
            if (!state.Pins.Contains(spaceId)) throw new InvalidOperationException("The selected Space is no longer pinned.");
            if (destinationIndex < 0 || destinationIndex >= state.Pins.Length) throw new ArgumentOutOfRangeException(nameof(destinationIndex));
            var next = state.Pins.Where(id => id != spaceId).ToList(); next.Insert(destinationIndex, spaceId);
            return state with { Pins = next.ToArray() };
        }, token);

    private async Task<SpaceNavigationPreferenceSnapshot> MutateAsync(long expectedRevision,
        Func<State, string?, State> mutation, CancellationToken token)
    {
        if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        var admission = await authority.CaptureWriteAdmissionAsync(token).ConfigureAwait(false);
        var snapshot = await settings.ExportAsync(token).ConfigureAwait(false);
        await DemandCurrentAsync(admission, snapshot, token).ConfigureAwait(false);
        snapshot.Settings.TryGetValue(Key, out var raw);
        snapshot.Settings.TryGetValue(RegistryKey, out var registryRaw);
        var original = Parse(raw);
        if (original.Revision != expectedRevision) throw new InvalidOperationException("Space navigation preferences changed. Refresh before applying this edit.");
        var next = mutation(original, registryRaw) with { Revision = checked(original.Revision + 1) };
        Validate(next);
        var committed = await settings.CompareExchangeGuardedAsync(Key, raw, JsonSerializer.Serialize(next),
            new Dictionary<string, string?> { [RegistryKey] = registryRaw }, admission, token).ConfigureAwait(false);
        if (committed.AdmissionRejected) throw new UnauthorizedAccessException("Home ownership no longer permits changing Space navigation.");
        if (!committed.Exchanged) throw new InvalidOperationException("Space navigation or its catalog changed. Refresh before applying this edit.");
        return Project(next);
    }

    internal static string ScopeKey(Guid? spaceId)
    {
        if (spaceId == Guid.Empty) throw new ArgumentException("An empty identity is not a Space.", nameof(spaceId));
        return spaceId is { } id ? id.ToString("D") : "general";
    }
    private static async Task DemandCurrentAsync(ISettingsCommitAdmission admission, SettingsExportManifest snapshot, CancellationToken token)
    {
        if (snapshot.StoreIdentity is not { } identity || !await admission.CheckAsync(new(identity, snapshot.Version, SettingsCommitPhase.Admission), token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Home ownership of navigation preferences changed.");
    }
    private static State Parse(string? raw)
    {
        if (raw is null) return new(1, 0, SpaceSwitcherLayout.Rail, [], new(StringComparer.Ordinal));
        State? state;
        try { state = JsonSerializer.Deserialize<State>(raw); }
        catch (JsonException error) { throw new InvalidDataException("Space navigation preferences are corrupt; the saved value was preserved.", error); }
        if (state is null) throw new InvalidDataException("Space navigation preferences contain no state; the saved value was preserved.");
        Validate(state); return state;
    }
    private static void Validate(State state)
    {
        if (state.SchemaVersion != 1 || state.Revision < 0 || !Enum.IsDefined(state.Layout) || state.Pins is null || state.Collapsed is null ||
            state.Pins.Length > MaximumEntries || state.Collapsed.Count > MaximumEntries || state.Pins.Any(id => id == Guid.Empty) ||
            state.Pins.Distinct().Count() != state.Pins.Length || state.Collapsed.Keys.Any(key => key != "general" &&
                (!Guid.TryParseExact(key, "D", out var id) || id == Guid.Empty || id.ToString("D") != key)))
            throw new InvalidDataException("Space navigation preferences are invalid or unsupported; saved state was preserved.");
    }
    private static void DemandExisting(Guid id, string? registryRaw)
    {
        if (id == Guid.Empty) throw new ArgumentException("An empty identity is not a Space.", nameof(id));
        SpaceRegistryState? registry;
        try { registry = registryRaw is null ? null : JsonSerializer.Deserialize<SpaceRegistryState>(registryRaw); }
        catch (JsonException error) { throw new InvalidDataException("The existing Space registry is corrupt.", error); }
        if (registry is null || registry.Version is < 2 or > 4 || registry.Spaces is null ||
            registry.Spaces.Count(space => space.Id == id && !space.IsArchived) != 1 || registry.Spaces.Count(space => space.Id == id) != 1)
            throw new InvalidOperationException("The selected Space is not an active canonical stored Space.");
    }
    private static SpaceNavigationPreferenceSnapshot Project(State state) => new(state.Revision, state.Layout,
        Array.AsReadOnly(state.Pins.ToArray()), new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>(state.Collapsed, StringComparer.Ordinal)));
}
