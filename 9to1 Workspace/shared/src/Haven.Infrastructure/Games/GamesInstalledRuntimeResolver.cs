using System.Security.Cryptography;
using Haven.Core.Games;

namespace Haven.Infrastructure.Games;

/// <summary>Supplied by trusted installer/host composition, never by project content or environment discovery.</summary>
public sealed record GamesInstalledRuntimePackage(string ExecutablePath, string ExecutableSha256);

public sealed record GamesInstalledRuntimeCapability(IGamesSceneRuntime? Runtime, string? UnavailableReason)
{
    public bool Available => Runtime is not null;
}

/// <summary>Resolves only an explicitly installed and pinned package. Availability is refreshed on each call;
/// the returned runtime independently rechecks the pin on every observation.</summary>
public sealed class GamesInstalledRuntimeResolver(GamesInstalledRuntimePackage? package)
{
    public async Task<GamesInstalledRuntimeCapability> ResolveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (package is null) return new(null, "The Games runtime package is not installed.");
        if (string.IsNullOrWhiteSpace(package.ExecutablePath) || !Path.IsPathFullyQualified(package.ExecutablePath)
            || package.ExecutableSha256 is not { Length: 64 } || !package.ExecutableSha256.All(Uri.IsHexDigit))
            return new(null, "The Games runtime package declaration is invalid.");
        try
        {
            var path = Path.GetFullPath(package.ExecutablePath);
            if (!File.Exists(path)) return new(null, "The installed Games runtime executable is missing.");
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
                return new(null, "The installed Games runtime executable cannot be executed.");
            await using var input = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(hash, package.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                return new(null, "The installed Games runtime does not match its package declaration.");
            return new(new GodotSceneRuntime(path, package.ExecutableSha256), null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(null, "The installed Games runtime package is inaccessible.");
        }
    }
}
