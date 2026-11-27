using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using Haven.Desktop.Views.Pages.Home;

namespace Haven.Desktop.Services;

internal sealed record HomeAppHubLayout(long Revision, bool Customise, IReadOnlyList<HomeAppHub> CustomHubs,
    HomeAppsFilter Filter = HomeAppsFilter.Installed)
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    public bool CustomLayoutInitialised { get; init; }
    public HomeHubArrangementUndo? Undo { get; init; }
    internal static HomeAppHubLayout Default { get; } = new(0, false, [], HomeAppsFilter.Installed);

    internal HomeAppHubLayout Validate()
    {
        if (SchemaVersion != 1 || Revision < 0 || !Enum.IsDefined(Filter) || !ValidHubs(CustomHubs) ||
            Undo is { } undo && (string.IsNullOrWhiteSpace(undo.Description) || undo.Description.Length > 160 ||
                undo.Description.Any(char.IsControl) || !ValidHubs(undo.Hubs)))
            throw new InvalidDataException("The saved app arrangement has an unsupported version or invalid placement. Its saved data was preserved.");
        // Older saved arrangements predate the explicit initialisation marker. A saved enabled or
        // nonempty arrangement already has an identity; an intentionally empty enabled one does too.
        return this with { CustomLayoutInitialised = CustomLayoutInitialised || Customise || CustomHubs.Count > 0 };
    }

    private static bool ValidHubs(IReadOnlyList<HomeAppHub>? hubs) => hubs is { Count: <= 32 } &&
            !hubs.Any(hub => hub is null || !ValidKey(hub.Key) || string.IsNullOrWhiteSpace(hub.Name) ||
                hub.Name.Length > 80 || hub.Name.Any(char.IsControl) || hub.Description is null || hub.Description.Length > 512 ||
                !ValidKey(hub.IconKey) || hub.AppIdentities is null || hub.AppIdentities.Count > 256 ||
                hub.AppIdentities.Any(identity => !ValidIdentity(identity)) ||
                hub.AppIdentities.Distinct(StringComparer.Ordinal).Count() != hub.AppIdentities.Count) &&
            hubs.Select(hub => hub.Key).Distinct(StringComparer.Ordinal).Count() == hubs.Count;

    private static bool ValidKey(string? value) => value is { Length: > 0 and <= 80 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool ValidIdentity(string? value) => value is { Length: > 0 and <= 160 } &&
        (value.StartsWith("mode:", StringComparison.Ordinal) && Guid.TryParseExact(value[5..], "D", out var id) && id != Guid.Empty ||
         value.StartsWith("reference:", StringComparison.Ordinal) && ValidKey(value[10..]) ||
         value.StartsWith("catalogue:", StringComparison.Ordinal) && ValidKey(value[10..]));
}

internal sealed record HomeHubArrangementUndo(string Description, IReadOnlyList<HomeAppHub> Hubs);

/// <summary>Presentation-only edits. Callers still require the profile admission and exact settings
/// compare/exchange; these transformations never install, delete or change an app or its documents.</summary>
internal static class HomeHubEdits
{
    internal static HomeAppHubLayout Toggle(HomeAppHubLayout layout, IReadOnlyList<HomeAppHub> standard)
    {
        layout = layout.Validate();
        return (layout with
        {
            Customise = !layout.Customise,
            CustomLayoutInitialised = true,
            CustomHubs = layout.CustomLayoutInitialised ? layout.CustomHubs : Copy(standard)
        }).Validate();
    }

    internal static HomeAppHubLayout Rename(HomeAppHubLayout layout, HomeHubActionTarget target, string name)
    {
        var index = Index(layout, target);
        name = name.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Use a hub name between 1 and 80 characters, without control characters.", nameof(name));
        if (layout.CustomHubs[index].Name == name) return layout;
        var hubs = Copy(layout.CustomHubs);
        hubs[index] = hubs[index] with { Name = name };
        return Changed(layout, hubs, "Hub renamed");
    }

    internal static HomeAppHubLayout MoveHub(HomeAppHubLayout layout, HomeHubActionTarget target, int delta)
    {
        var index = Index(layout, target);
        var destination = index + Math.Sign(delta);
        if (destination < 0 || destination >= layout.CustomHubs.Count || destination == index) return layout;
        var hubs = layout.CustomHubs.ToList();
        var hub = hubs[index]; hubs.RemoveAt(index); hubs.Insert(destination, hub);
        return Changed(layout, hubs, "Hub order changed");
    }

    internal static HomeAppHubLayout AddHub(HomeAppHubLayout layout, string name, string key)
    {
        DemandEditing(layout);
        name = name.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Use a hub name between 1 and 80 characters, without control characters.", nameof(name));
        if (layout.CustomHubs.Count >= 32) throw new InvalidOperationException("This arrangement already contains 32 hubs.");
        return Changed(layout, layout.CustomHubs.Append(new HomeAppHub(key, name,
            "Your app arrangement", "grid", [])).ToArray(), "Hub added");
    }

    internal static HomeAppHubLayout RemoveHub(HomeAppHubLayout layout, HomeHubActionTarget target)
    {
        var index = Index(layout, target);
        return Changed(layout, layout.CustomHubs.Where((_, at) => at != index).ToArray(), "Hub removed");
    }

    internal static HomeAppHubLayout Reset(HomeAppHubLayout layout, IReadOnlyList<HomeAppHub> standard)
    {
        DemandEditing(layout);
        return Changed(layout, standard, "Arrangement reset");
    }

    internal static HomeAppHubLayout AddApp(HomeAppHubLayout layout, HomeHubActionTarget target)
    {
        var index = Index(layout, target);
        var identity = target.AppIdentity ?? throw new ArgumentException("Choose an app to add.", nameof(target));
        var hub = layout.CustomHubs[index];
        if (hub.AppIdentities.Contains(identity, StringComparer.Ordinal)) return layout;
        if (hub.AppIdentities.Count >= 256) throw new InvalidOperationException("This hub already contains 256 app placements.");
        var hubs = Copy(layout.CustomHubs);
        hubs[index] = hub with { AppIdentities = hub.AppIdentities.Append(identity).ToArray() };
        return Changed(layout, hubs, "App placement added");
    }

    internal static HomeAppHubLayout RemoveApp(HomeAppHubLayout layout, HomeHubActionTarget target)
    {
        var index = Index(layout, target);
        var hub = layout.CustomHubs[index];
        if (target.AppIdentity is null || !hub.AppIdentities.Contains(target.AppIdentity, StringComparer.Ordinal))
            throw new InvalidOperationException("That app placement is no longer in this hub.");
        var hubs = Copy(layout.CustomHubs);
        hubs[index] = hub with { AppIdentities = hub.AppIdentities.Where(identity => identity != target.AppIdentity).ToArray() };
        return Changed(layout, hubs, "App placement removed");
    }

    internal static HomeAppHubLayout MoveApp(HomeAppHubLayout layout, HomeHubActionTarget target, int delta)
    {
        var index = Index(layout, target);
        var identities = layout.CustomHubs[index].AppIdentities.ToList();
        var position = identities.FindIndex(identity => identity == target.AppIdentity);
        if (position < 0) throw new InvalidOperationException("That app placement is no longer in this hub.");
        var destination = position + Math.Sign(delta);
        if (destination < 0 || destination >= identities.Count || position == destination) return layout;
        var identity = identities[position]; identities.RemoveAt(position); identities.Insert(destination, identity);
        var hubs = Copy(layout.CustomHubs);
        hubs[index] = hubs[index] with { AppIdentities = identities.ToArray() };
        return Changed(layout, hubs, "App order changed");
    }

    internal static HomeAppHubLayout Undo(HomeAppHubLayout layout)
    {
        DemandEditing(layout);
        return layout.Undo is { } undo
            ? (layout with { CustomHubs = Copy(undo.Hubs), Undo = null, CustomLayoutInitialised = true }).Validate()
            : layout;
    }

    private static int Index(HomeAppHubLayout layout, HomeHubActionTarget target)
    {
        DemandEditing(layout);
        if (target.Revision != layout.Revision) throw new InvalidOperationException("The arrangement changed. Review its latest state and try again.");
        var index = layout.CustomHubs.ToList().FindIndex(hub => hub.Key == target.HubKey);
        return index >= 0 ? index : throw new InvalidOperationException("That hub is no longer in this arrangement.");
    }

    private static void DemandEditing(HomeAppHubLayout layout)
    {
        layout.Validate();
        if (!layout.Customise) throw new InvalidOperationException("Turn on Customise hubs before changing the arrangement.");
    }

    private static HomeAppHubLayout Changed(HomeAppHubLayout layout, IReadOnlyList<HomeAppHub> hubs, string description) =>
        (layout with { CustomHubs = Copy(hubs), CustomLayoutInitialised = true,
            Undo = new(description, Copy(layout.CustomHubs)) }).Validate();

    private static HomeAppHub[] Copy(IReadOnlyList<HomeAppHub> hubs) =>
        hubs.Select(hub => hub with { AppIdentities = hub.AppIdentities.ToArray() }).ToArray();
}

/// <summary>Profile-scoped presentation only. App pins stay in IPinRepository; package installation,
/// documents, Dashboard and New Tab keep their existing owners and settings.</summary>
internal sealed class HomeAppHubPreferences
{
    internal sealed record Snapshot(HomeAppHubLayout Layout, string? Serialized);
    internal sealed record SaveResult(bool Saved, Snapshot Current);
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly IReadOnlyDictionary<string, string?> NoAdditionalGuards = new Dictionary<string, string?>();
    private readonly IVersionedSettingsStore _settings;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly string _key;

    internal HomeAppHubPreferences(IVersionedSettingsStore settings, string profileId, Func<CancellationToken, Task> admit)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        _key = "haven.apps.hubs.v1." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileId)));
    }

    internal bool CanEdit => _settings is IVersionedSettingsGuardedCompareExchange;

    internal async Task<Snapshot> ReadAsync(CancellationToken token)
    {
        await _admit(token).ConfigureAwait(false);
        var exported = await _settings.ExportAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return Read(exported.Settings.GetValueOrDefault(_key));
    }

    internal async Task<SaveResult> SaveAsync(Snapshot expected, HomeAppHubLayout candidate, CancellationToken token)
    {
        var store = _settings as IVersionedSettingsGuardedCompareExchange
            ?? throw new NotSupportedException("App arrangements require a guarded settings store.");
        var next = (candidate with { Revision = checked(expected.Layout.Revision + 1) }).Validate();
        var replacement = JsonSerializer.Serialize(next, Json);
        var result = await store.CompareExchangeGuardedAsync(_key, expected.Serialized, replacement,
            NoAdditionalGuards, new Admission(_admit), token).ConfigureAwait(false);
        if (result.AdmissionRejected)
            throw new UnauthorizedAccessException("The profile changed before the app arrangement could be saved.");
        return result.Exchanged ? new(true, new(next, replacement)) : new(false, Read(result.CurrentJson));
    }

    private static Snapshot Read(string? raw)
    {
        if (raw is null) return new(HomeAppHubLayout.Default, null);
        try
        {
            var layout = JsonSerializer.Deserialize<HomeAppHubLayout>(raw, Json)
                ?? throw new InvalidDataException("The saved app arrangement is empty. Its saved data was preserved.");
            return new(layout.Validate(), raw);
        }
        catch (JsonException cause)
        {
            throw new InvalidDataException("The saved app arrangement could not be read. Its saved data was preserved.", cause);
        }
    }

    private sealed class Admission(Func<CancellationToken, Task> admit) : ISettingsCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
        {
            try { await admit(token).ConfigureAwait(false); return true; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
