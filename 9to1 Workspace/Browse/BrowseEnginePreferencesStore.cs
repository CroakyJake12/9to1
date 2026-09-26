using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Browse;

/// <summary>Versioned, app-local persistence for Browse's default and per-site engine choices.</summary>
public sealed class BrowseEnginePreferencesStore
{
    private const int CurrentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true
    };
    private readonly string _path;

    public BrowseEnginePreferencesStore(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _path = Path.Combine(paths.DataDirectory, "browse-engine-preferences.json");
    }

    public async Task<BrowseEnginePreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return BrowseEnginePreferences.Default;
        try
        {
            await using var stream = File.OpenRead(_path);
            var saved = await JsonSerializer.DeserializeAsync<StoredPreferences>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (saved is null || saved.Version != CurrentVersion || !Enum.IsDefined(saved.DefaultEngine))
                return BrowseEnginePreferences.Default;
            var sites = (saved.SiteOverrides ?? new Dictionary<string, BrowseEngineKind>())
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && Enum.IsDefined(entry.Value))
                .GroupBy(entry => entry.Key.Trim().TrimEnd('.'), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToDictionary(entry => entry.Key.Trim().TrimEnd('.'), entry => entry.Value, StringComparer.OrdinalIgnoreCase);
            var tabs = (saved.TabOverrides ?? new Dictionary<Guid, BrowseEngineKind>())
                .Where(entry => entry.Key != Guid.Empty && Enum.IsDefined(entry.Value))
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            return new BrowseEnginePreferences(saved.DefaultEngine, sites, tabs);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            // Keep a malformed or inaccessible file intact; callers can continue with safe defaults.
            return BrowseEnginePreferences.Default;
        }
    }

    public async Task SaveAsync(BrowseEnginePreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!Enum.IsDefined(preferences.DefaultEngine)) throw new ArgumentOutOfRangeException(nameof(preferences));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream,
                    new StoredPreferences(CurrentVersion, preferences.DefaultEngine,
                        preferences.SiteOverrides.Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && Enum.IsDefined(entry.Value))
                            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
                        (preferences.TabOverrides ?? new Dictionary<Guid, BrowseEngineKind>())
                            .Where(entry => entry.Key != Guid.Empty && Enum.IsDefined(entry.Value))
                            .ToDictionary(entry => entry.Key, entry => entry.Value)),
                    JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
            }
        }
    }

    private sealed record StoredPreferences(int Version, BrowseEngineKind DefaultEngine,
        Dictionary<string, BrowseEngineKind>? SiteOverrides, Dictionary<Guid, BrowseEngineKind>? TabOverrides);
}

public sealed record BrowseEnginePreferences(
    BrowseEngineKind DefaultEngine,
    IReadOnlyDictionary<string, BrowseEngineKind> SiteOverrides,
    IReadOnlyDictionary<Guid, BrowseEngineKind>? TabOverrides = null)
{
    public static BrowseEnginePreferences Default { get; } = new(BrowseEngineKind.Gecko,
        new Dictionary<string, BrowseEngineKind>(StringComparer.OrdinalIgnoreCase));
}
