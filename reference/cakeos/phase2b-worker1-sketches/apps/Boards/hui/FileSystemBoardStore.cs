using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Platform;

namespace CakeOS.Apps.Boards.Hui;

public sealed class FileSystemBoardStore : IHavenBoardStore
{
    private readonly IVersionedSettingsStore _settingsStore;

    public FileSystemBoardStore(IVersionedSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public async Task<HavenBoardSnapshot?> LoadAsync(string boardId, CancellationToken ct = default)
    {
        var key = $"boards/{boardId}.json";
        var json = await _settingsStore.GetStringAsync(key, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(json))
            return null;

        return JsonSerializer.Deserialize<HavenBoardSnapshot>(json, JsonOptions.Default);
    }

    public async Task SaveAsync(HavenBoardSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var key = $"boards/{snapshot.Id}.json";
        var json = JsonSerializer.Serialize(snapshot, JsonOptions.Default);
        await _settingsStore.SetStringAsync(key, json, ct).ConfigureAwait(false);
    }

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
    }
}