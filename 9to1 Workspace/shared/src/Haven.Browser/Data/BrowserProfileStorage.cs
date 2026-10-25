using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;

namespace Haven.Browser;

/// <summary>Resolves the verified owner of pre-isolation browser storage without reassigning it at login.</summary>
public static class BrowserProfileStorage
{
    private const int MarkerSchemaVersion = 1;
    private static readonly object Gate = new();

    /// <summary>Returns the legacy root only to its durable owner; all other identities get deterministic roots.</summary>
    public static string ResolveProfileDirectory(IAppPaths paths, string profileId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(profileId);
        if (string.IsNullOrWhiteSpace(profileId) && profileId.Length > 0)
            throw new InvalidDataException("A browser profile identity cannot be whitespace.");
        DemandNotReparsePoint(paths.BrowserProfileDirectory);
        DemandNotReparsePoint(paths.DataDirectory);
        DemandNotReparsePoint(Path.Combine(paths.BrowserProfileDirectory, "profiles"));
        DemandNotReparsePoint(Path.Combine(paths.BrowserProfileDirectory, "engines"));

        lock (Gate)
        {
            var owner = ReadOwner(paths);
            if (owner is null && !HasLegacyBrowserData(paths)) owner = TryClaimEmptyLegacyRoot(paths, profileId);
            if (owner is not null && StringComparer.Ordinal.Equals(owner.ProfileId, profileId))
                return paths.BrowserProfileDirectory;

            var key = profileId.Length == 0 ? "default" : profileId;
            var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
            return Path.Combine(paths.BrowserProfileDirectory, "profiles", identity);
        }
    }

    /// <summary>True only when a valid durable marker binds the legacy root to this identity.</summary>
    public static bool OwnsLegacyRoot(IAppPaths paths, string profileId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(profileId);
        lock (Gate) return ReadOwner(paths) is { } owner && StringComparer.Ordinal.Equals(owner.ProfileId, profileId);
    }

    /// <summary>Reports legacy browser bytes that have no verified profile owner and remain untouched.</summary>
    public static bool HasUnassignedLegacyData(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        try { lock (Gate) return ReadOwner(paths) is null && HasLegacyBrowserData(paths); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return true; }
    }

    private static OwnerMarker? ReadOwner(IAppPaths paths)
    {
        var path = MarkerPath(paths);
        if (!File.Exists(path)) return null;
        if (IsReparsePoint(path)) return null;
        try
        {
            var marker = JsonSerializer.Deserialize<OwnerMarker>(File.ReadAllText(path));
            return marker is { SchemaVersion: MarkerSchemaVersion, ProfileId: not null } &&
                (marker.ProfileId.Length == 0 || !string.IsNullOrWhiteSpace(marker.ProfileId)) ? marker : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return null; }
    }

    private static OwnerMarker? TryClaimEmptyLegacyRoot(IAppPaths paths, string profileId)
    {
        var marker = new OwnerMarker(MarkerSchemaVersion, profileId);
        try
        {
            Directory.CreateDirectory(paths.DataDirectory);
            using (var stream = new FileStream(MarkerPath(paths), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, marker);
                stream.Flush(flushToDisk: true);
            }
            return marker;
        }
        catch (IOException) { return ReadOwner(paths); }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool HasLegacyBrowserData(IAppPaths paths)
    {
        var permissionPath = Path.Combine(paths.DataDirectory, "browser-site-permissions.json");
        if (File.Exists(permissionPath) || File.Exists(permissionPath + ".bak")) return true;
        var engineRoot = Path.Combine(paths.BrowserProfileDirectory, "engines");
        if (Directory.Exists(engineRoot) && Directory.EnumerateFileSystemEntries(engineRoot).Any()) return true;
        if (!Directory.Exists(paths.BrowserProfileDirectory)) return false;
        return Directory.EnumerateFileSystemEntries(paths.BrowserProfileDirectory)
            .Any(entry => !StringComparer.Ordinal.Equals(Path.GetFileName(entry), "profiles"));
    }

    private static string MarkerPath(IAppPaths paths) => Path.Combine(paths.DataDirectory, ".browser-legacy-profile-owner.json");
    internal static void DemandNotReparsePoint(string path)
    {
        if (IsReparsePoint(path))
            throw new InvalidDataException("Browser profile storage cannot use a symbolic-link or reparse-point root.");
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private sealed record OwnerMarker(int SchemaVersion, string ProfileId);
}
