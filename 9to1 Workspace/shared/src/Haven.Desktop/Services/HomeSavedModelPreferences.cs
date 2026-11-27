using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using HavenOS.Home.Discover;

namespace Haven.Desktop.Services;

internal sealed record HomeSavedModel(HomeDiscoverIdentity Identity, string Name, DateTimeOffset SavedAt);

internal sealed record HomeSavedModelList(long Revision, IReadOnlyList<HomeSavedModel> Models)
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    internal static HomeSavedModelList Empty { get; } = new(0, []);

    internal HomeSavedModelList Validate()
    {
        if (SchemaVersion != 1 || Revision < 0 || Models is null || Models.Count > 512 ||
            Models.Any(model => model is null || model.Identity is null ||
                !ValidText(model.Identity.ProviderId, 160) || !ValidText(model.Identity.ModelId, 1024) ||
                model.Identity.ArtifactRevision is { } revision && !ValidText(revision, 1024) ||
                !ValidText(model.Name, 512) || model.SavedAt == default) ||
            Models.Select(model => model.Identity).Distinct().Count() != Models.Count)
            throw new InvalidDataException("The saved model list has an unsupported format or invalid entries. Its stored data was preserved.");
        return this;
    }

    private static bool ValidText(string? text, int limit) => text is { Length: > 0 } &&
        text.Length <= limit && !string.IsNullOrWhiteSpace(text) && !text.Any(char.IsControl);
}

/// <summary>Profile-scoped bookmarks only. This owner never installs, loads or routes a model.
/// Changes use the current profile's final admission and exact serialized settings CAS.</summary>
internal sealed class HomeSavedModelPreferences
{
    internal sealed record Snapshot(HomeSavedModelList List, string? Serialized);
    internal sealed record SaveResult(bool Saved, Snapshot Current);
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly IReadOnlyDictionary<string, string?> NoAdditionalGuards = new Dictionary<string, string?>();
    private readonly IVersionedSettingsStore _settings;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly string _key;

    internal HomeSavedModelPreferences(IVersionedSettingsStore settings, string profileId, Func<CancellationToken, Task> admit)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        _key = "haven.models.saved.v1." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileId)));
    }

    internal bool CanEdit => _settings is IVersionedSettingsGuardedCompareExchange;

    internal async Task<Snapshot> ReadAsync(CancellationToken token)
    {
        await _admit(token).ConfigureAwait(false);
        var exported = await _settings.ExportAsync(token).ConfigureAwait(false);
        await _admit(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return Read(exported.Settings.GetValueOrDefault(_key));
    }

    internal async Task<SaveResult> SaveAsync(Snapshot expected, HomeSavedModelList candidate, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var store = _settings as IVersionedSettingsGuardedCompareExchange
            ?? throw new NotSupportedException("Saving models requires the original guarded settings store.");
        var next = (candidate with { Revision = checked(expected.List.Revision + 1) }).Validate();
        var replacement = JsonSerializer.Serialize(next, Json);
        var result = await store.CompareExchangeGuardedAsync(_key, expected.Serialized, replacement,
            NoAdditionalGuards, new Admission(_admit), token).ConfigureAwait(false);
        if (result.AdmissionRejected)
            throw new UnauthorizedAccessException("The profile changed before the model bookmark could be saved.");
        return result.Exchanged ? new(true, new(next, replacement)) : new(false, Read(result.CurrentJson));
    }

    private static Snapshot Read(string? raw)
    {
        if (raw is null) return new(HomeSavedModelList.Empty, null);
        try
        {
            var value = JsonSerializer.Deserialize<HomeSavedModelList>(raw, Json)
                ?? throw new InvalidDataException("The saved model list is empty. Its stored data was preserved.");
            return new(value.Validate(), raw);
        }
        catch (JsonException error)
        { throw new InvalidDataException("The saved model list could not be read. Its stored data was preserved.", error); }
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
