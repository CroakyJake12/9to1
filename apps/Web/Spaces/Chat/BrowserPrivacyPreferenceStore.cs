using System.Text.Json;
using Haven.Application;
using NineToOne.Web.Spaces.Storage;

namespace NineToOne.Web.Spaces.Chat;

/// <summary>Browser implementation of the shared privacy preference contract over
/// the same authenticated profile store. It does not claim remote synchronization.</summary>
internal sealed class BrowserPrivacyPreferenceStore : IPrivacyPreferenceStore
{
    private const string Key = "privacy.preferences.v1";
    private readonly IVersionedSettingsStore _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private sealed record Snapshot(PrivacyPreferences Value, (long Admission, long Acknowledged) Generation);
    private Snapshot? _current;
    private (long Admission, long Acknowledged) Generation
    {
        get
        {
            if (_settings is not BrowserChatSettingsStore actual) return (0, 0);
            if (actual.HasUnsettledMutation)
                throw new InvalidOperationException("An original settings mutation has not settled; hosted work is paused.");
            return (actual.MutationAdmissionGeneration, actual.AcknowledgedMutationGeneration);
        }
    }
    internal BrowserPrivacyPreferenceStore(IVersionedSettingsStore settings)
    {
        _settings = settings;
    }
    public PrivacyPreferences Current
    {
        get
        {
            return TryGetCurrent(out var value) ? value!
                : throw new InvalidOperationException("The current profile privacy policy must be read before hosted work can continue.");
        }
    }
    internal bool TryGetCurrent(out PrivacyPreferences? value)
    {
        value = null;
        var snapshot = Volatile.Read(ref _current);
        if (snapshot is null) return false;
        if (_settings is BrowserChatSettingsStore actual)
        {
            if (actual.HasUnsettledMutation) return false;
            var revision = (actual.MutationAdmissionGeneration, actual.AcknowledgedMutationGeneration);
            if (actual.HasUnsettledMutation || snapshot.Generation != revision) return false;
        }
        value = snapshot.Value; return true;
    }
    public async Task<PrivacyPreferences> RefreshAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            return await ReadOriginalAsync(token);
        }
        catch { Volatile.Write(ref _current, null); throw; }
        finally { _gate.Release(); }
    }
    private async Task<PrivacyPreferences> ReadOriginalAsync(CancellationToken token)
    {
        // Generic nullable Get cannot distinguish a missing key from stored JSON null.
        // The original export provides actual presence and preserves corrupt content.
        var generation = Generation;
        var observed = await _settings.ExportAsync(token);
        var preferences = observed.Settings.TryGetValue(Key, out var json) ? Decode(json) : PrivacyPreferences.Default;
        if (generation != Generation)
            throw new InvalidOperationException("The original privacy policy changed during its read; refresh before continuing.");
        Volatile.Write(ref _current, new Snapshot(preferences, generation)); return preferences;
    }
    private static PrivacyPreferences Decode(string json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The saved privacy policy must be an object.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!keys.Add(property.Name)) throw new InvalidDataException("The saved privacy policy contains duplicate fields.");
            foreach (var key in new[] { nameof(PrivacyPreferences.LocalOnlyMode), nameof(PrivacyPreferences.BackgroundLearningEnabled), nameof(PrivacyPreferences.ModelImprovementSharingEnabled) })
                if (!root.TryGetProperty(key, out var flag) || flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new InvalidDataException("The saved privacy policy is missing a required Boolean field: " + key);
            if (!root.TryGetProperty(nameof(PrivacyPreferences.UpdatedAt), out var updated) || updated.ValueKind != JsonValueKind.String || !updated.TryGetDateTimeOffset(out _))
                throw new InvalidDataException("The saved privacy policy has no valid original update time.");
            if (root.TryGetProperty(nameof(PrivacyPreferences.BackgroundLearningPolicy), out var learning) && learning.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The saved background learning policy is invalid.");
            return JsonSerializer.Deserialize<PrivacyPreferences>(json) ?? throw new InvalidDataException("The saved privacy policy is null.");
        }
        catch (JsonException cause) { throw new InvalidDataException("The saved privacy policy is invalid; its original content was preserved.", cause); }
    }
    public async Task UpdateAsync(PrivacyPreferences preferences, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        await _gate.WaitAsync(token);
        try
        {
            var saved = preferences with { UpdatedAt = DateTimeOffset.UtcNow };
            _ = Decode(JsonSerializer.Serialize(saved));
            Volatile.Write(ref _current, null);
            await _settings.SetAsync(Key, saved, token);
            await ReadOriginalAsync(token);
        }
        catch { Volatile.Write(ref _current, null); throw; }
        finally { _gate.Release(); }
    }
}
