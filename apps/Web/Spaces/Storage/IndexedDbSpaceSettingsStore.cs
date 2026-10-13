using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using NineToOne.Web.Services;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>Only the canonical Space registry/selection keys, held in the existing private
/// IndexedDB owner. Exact actor/session binding and guarded CAS prevent another tab or
/// account from silently replacing the configuration observed by this owner.</summary>
[SupportedOSPlatform("browser")]
public sealed class IndexedDbSpaceSettingsStore(BrowserTaskExecutionTransport transport, BrowserTaskActorSource actors)
    : IVersionedSettingsStore, IVersionedSettingsGuardedCompareExchange
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AuthenticatedResourceActor? _actor;
    private static void RequireKey(string key)
    {
        if (key is not ("spaces.registry" or "spaces.current"))
            throw new ArgumentException("This store owns only Space configuration and selection.", nameof(key));
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class
    {
        RequireKey(key);
        var exported = await ExportAsync(cancellationToken);
        return exported.Settings.TryGetValue(key, out var json) ? JsonSerializer.Deserialize<T>(json) : null;
    }

    public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class =>
        ReplaceAsync(key, JsonSerializer.Serialize(value), cancellationToken);
    public Task RemoveAsync(string key, CancellationToken cancellationToken) => ReplaceAsync(key, null, cancellationToken);
    private async Task ReplaceAsync(string key, string? replacement, CancellationToken token)
    {
        RequireKey(key);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var current = await ExportAsync(token);
            current.Settings.TryGetValue(key, out var expected);
            if ((await CompareExchangeAsync(key, expected, replacement, token)).Exchanged) return;
        }
        throw new InvalidOperationException("Space settings changed repeatedly. Refresh before trying again.");
    }

    public async Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var observed = await CallAsync("SpacesSettings.Export", new { }, cancellationToken);
            var value = observed.Reply.GetProperty("value");
            var version = ReadVersion(value);
            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in value.GetProperty("rows").EnumerateArray())
            {
                var key = row.GetProperty("key").GetString()!;
                RequireKey(key);
                if (row.GetProperty("profileId").GetString() != observed.Actor.ProfileId)
                    throw new InvalidDataException("The Space settings partition differs from its verified account.");
                var json = ReadJson(row);
                if (!settings.TryAdd(key, json)) throw new InvalidDataException("Duplicate Space settings key.");
            }
            return new() { Version = version, Settings = settings };
        }
        finally { _gate.Release(); }
    }

    public async Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson,
        string? replacementJson, CancellationToken cancellationToken)
    {
        var result = await CompareExchangeGuardedAsync(key, expectedJson, replacementJson,
            new Dictionary<string, string?>(), cancellationToken);
        return new(result.Exchanged, result.CurrentJson, result.StoreVersion);
    }

    public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expectedJson,
        string? replacementJson, IReadOnlyDictionary<string, string?> expectedGuards, CancellationToken cancellationToken)
    {
        RequireKey(key);
        foreach (var guard in expectedGuards.Keys) RequireKey(guard);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Establish this store's immutable session binding before its first write.
            // Normal registry use already reads first, but direct callers cannot skip it.
            if (_actor is null) await CallAsync("SpacesSettings.Export", new { }, cancellationToken);
            var hash = replacementJson is null ? null : Digest(replacementJson);
            var observed = await CallAsync("SpacesSettings.CompareExchange", new
            {
                key, expected = expectedJson, replacement = replacementJson, sha256 = hash, guards = expectedGuards
            }, cancellationToken);
            var value = observed.Reply.GetProperty("value");
            if (value.GetProperty("profileId").GetString() != observed.Actor.ProfileId || value.GetProperty("key").GetString() != key)
                throw new InvalidDataException("The Space settings acknowledgement identifies another key or account.");
            var exchanged = value.GetProperty("exchanged").GetBoolean();
            var current = value.GetProperty("current").ValueKind == JsonValueKind.Null ? null : ReadJson(value.GetProperty("current"));
            if (exchanged && (observed.Reply.GetProperty("committed").ValueKind != JsonValueKind.True || current != replacementJson))
                throw new InvalidDataException("The Space settings write has no exact committed acknowledgement.");
            var conflict = value.GetProperty("conflictingGuardKey").GetString();
            if (conflict is not null && !expectedGuards.ContainsKey(conflict))
                throw new InvalidDataException("An unknown Space settings guard was returned.");
            return new(exchanged, current, ReadVersion(value), conflict);
        }
        finally { _gate.Release(); }
    }

    private async Task<BrowserAuthenticatedStorageReply> CallAsync(string action, object args, CancellationToken token)
    {
        var actual = await transport.InvokeSpaceSettingsAsync(actors, action, JsonSerializer.SerializeToElement(args), _actor, token);
        if (actual.Reply.ValueKind != JsonValueKind.Object || actual.Reply.GetProperty("ok").ValueKind != JsonValueKind.True)
            throw new IOException("Space storage could not complete the request. Refresh to inspect the saved state before retrying.");
        _actor ??= actual.Actor;
        if (_actor != actual.Actor) throw new InvalidDataException("The Space settings account changed.");
        return actual;
    }

    private static int ReadVersion(JsonElement value)
    {
        var version = value.GetProperty("version").GetInt32();
        if (version < 0) throw new InvalidDataException("Invalid Space settings store version.");
        return version;
    }
    private static string ReadJson(JsonElement row)
    {
        var json = row.GetProperty("json").GetString() ?? throw new InvalidDataException("Missing Space settings JSON.");
        if (row.GetProperty("sha256").GetString() != Digest(json)) throw new InvalidDataException("Space settings integrity check failed.");
        using var valid = JsonDocument.Parse(json);
        return json;
    }
    private static string Digest(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) =>
        Task.FromException<SettingsImportResult>(new NotSupportedException("Space import requires its reviewed migration owner."));
}
