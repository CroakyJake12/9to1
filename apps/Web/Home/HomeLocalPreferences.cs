using System.Text.Json;
using System.Text.Json.Serialization;

namespace NineToOne.Web;

/// <summary>Device-only presentation preferences. Contains no account, artifact or permission identity.</summary>
internal interface IHomeLocalPreferences
{
    string? Read();
    void Write(string json);
    Task WriteAsync(string json, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); Write(json); return Task.CompletedTask; }
}

internal sealed record HomeLocalTile(string Id, bool Pinned = false, bool Locked = false, bool Hidden = false);
internal sealed record HomeLocalDashboard(int Version, bool AiTiles, bool AiReorder, HomeLocalTile[] Tiles)
{
    internal static readonly string[] TileIds = ["continue", "apps", "activity", "services"];
    internal static HomeLocalDashboard Default => new(1, false, false, TileIds.Select(id => new HomeLocalTile(id)).ToArray());

    internal static HomeLocalDashboard Read(string? json)
    {
        if (string.IsNullOrEmpty(json)) return Default;
        if (json.Length > 8192) throw new InvalidDataException("Home preferences exceed their size limit.");
        var state = JsonSerializer.Deserialize(json,HomeLocalPreferencesJsonContext.Default.HomeLocalDashboard);
        if (state is null || state.Version != 1 || state.Tiles is null || state.Tiles.Length != TileIds.Length ||
            state.Tiles.Any(tile => tile is null) || !state.Tiles.Select(tile => tile.Id).Order().SequenceEqual(TileIds.Order()))
            throw new InvalidDataException("Home preferences have an unsupported schema.");
        return state;
    }
    internal string Serialize()=>JsonSerializer.Serialize(this,HomeLocalPreferencesJsonContext.Default.HomeLocalDashboard);
}

// Preserve the original PascalCase browser envelope while removing all trimmed
// reflection constructor/parameter assumptions from this owner boundary.
[JsonSerializable(typeof(HomeLocalDashboard))]
internal sealed partial class HomeLocalPreferencesJsonContext : JsonSerializerContext;
