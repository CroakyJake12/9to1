using System.Runtime.Versioning;
using System.Text.Json;
using Haven.Application;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>Actual local model permission/personalisation preferences in the SAME
/// signed profile's production database. Keys outside this owned slice are refused.</summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserChatSettingsStore : IVersionedSettingsStore
{
    private readonly BrowserConversationProductionStore _store;
    private long _acknowledgedMutationGeneration;
    private long _mutationAdmissionGeneration;
    private int _unsettledMutations;
    internal long MutationAdmissionGeneration => Volatile.Read(ref _mutationAdmissionGeneration);
    internal bool HasUnsettledMutation => Volatile.Read(ref _unsettledMutations) != 0;
    internal long AcknowledgedMutationGeneration => Volatile.Read(ref _acknowledgedMutationGeneration);
    internal BrowserChatSettingsStore(BrowserConversationProductionStore store) => _store = store;
    private static void Key(string key)
    { if (key is not ("models.permissions.v1" or "models.personalisation.v1" or "privacy.preferences.v1")) throw new ArgumentException("This browser owner does not implement that settings key.", nameof(key)); }
    private SettingsExportManifest Decode(JsonElement value)
    {
        _store.DemandOriginalProfile(value);
        var version = value.GetProperty("version").GetInt32();
        if (version < 0) throw new InvalidDataException("The original settings version is invalid.");
        var result = new Dictionary<string, string>();
        foreach (var row in value.GetProperty("rows").EnumerateArray())
        {
            _store.DemandOriginalProfile(row);
            var key = row.GetProperty("key").GetString()!; Key(key);
            var json = row.GetProperty("json").GetString()!;
            if (row.GetProperty("sha256").GetString() != BrowserConversationProductionStore.Hash(json) || !result.TryAdd(key, json))
                throw new InvalidDataException("The original model settings payload or key differs.");
            using var parsed = JsonDocument.Parse(json);
        }
        return new() { Version = version, Settings = result };
    }
    public async Task<SettingsExportManifest> ExportAsync(CancellationToken token) =>
        Decode(await _store.CallAsync("ConversationProduction.SettingsExport", new { }, false, token));
    public async Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
    {
        Key(key);
        if (!(await ExportAsync(token)).Settings.TryGetValue(key, out var json)) return null;
        return JsonSerializer.Deserialize<T>(json)
            ?? throw new InvalidDataException($"The saved setting '{key}' contains null instead of a value.");
    }
    public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => Replace(key, JsonSerializer.Serialize(value), token);
    public Task RemoveAsync(string key, CancellationToken token) => Replace(key, null, token);
    private async Task Replace(string key, string? replacement, CancellationToken token)
    {
        Key(key); var saved = await ExportAsync(token);
        await Change(saved, new Dictionary<string, string?> { [key] = replacement }, token);
    }
    public async Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != 1 || manifest.Settings is null) throw new InvalidDataException("Unsupported original settings manifest.");
        var changes = new Dictionary<string, string?>();
        foreach (var (key, json) in manifest.Settings)
        { Key(key); using var parsed = JsonDocument.Parse(json); changes.Add(key, json); }
        var actual = await Change(await ExportAsync(token), changes, token);
        return new(true, actual.Settings, $"Imported {changes.Count} local model settings.");
    }
    private async Task<SettingsExportManifest> Change(SettingsExportManifest saved, Dictionary<string, string?> changes, CancellationToken token)
    {
        var request = changes.Select(item => new { key = item.Key, expected = saved.Settings.GetValueOrDefault(item.Key), replacement = item.Value,
            sha256 = item.Value is null ? null : BrowserConversationProductionStore.Hash(item.Value) }).ToArray();
        // Invalidate any policy observation before acquiring the original writer.
        // An unknown acknowledgement remains fenced for this private lifetime.
        Interlocked.Increment(ref _unsettledMutations);
        Interlocked.Increment(ref _mutationAdmissionGeneration);
        var result = await _store.CallAsync("ConversationProduction.SettingsCompareExchange", new { expectedVersion = saved.Version, changes = request }, true, token);
        var exchanged = result.GetProperty("exchanged");
        if (exchanged.ValueKind == JsonValueKind.False)
        {
            _store.DemandOriginalProfile(result);
            if (result.GetProperty("version").GetInt32() < 0) throw new InvalidDataException("Invalid original conflict observation.");
            Interlocked.Decrement(ref _unsettledMutations);
            throw new InvalidOperationException("The actual model preferences changed; refresh them before saving again.");
        }
        if (exchanged.ValueKind != JsonValueKind.True) throw new InvalidDataException("No valid original model preference acknowledgement.");
        var actual = Decode(result); var expected = new Dictionary<string, string>(saved.Settings);
        foreach (var (key, replacement) in changes) { if (replacement is null) expected.Remove(key); else expected[key] = replacement; }
        if (actual.Version != checked(saved.Version + 1) || actual.Settings.Count != expected.Count || expected.Any(item => actual.Settings.GetValueOrDefault(item.Key) != item.Value))
            throw new InvalidDataException("The acknowledged model preferences differ from the exact original mutation.");
        Interlocked.Increment(ref _acknowledgedMutationGeneration);
        Interlocked.Decrement(ref _unsettledMutations);
        return actual;
    }
}
