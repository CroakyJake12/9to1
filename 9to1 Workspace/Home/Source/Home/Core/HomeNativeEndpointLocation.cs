using System.Security.Cryptography;
using System.Text;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Routing descriptor only. Its contents never authenticate the server or grant service/resource access.</summary>
public sealed record HomeNativeEndpointLocation(int SchemaVersion, string ProfileId, Guid Epoch,
    int HostProcessId, string SocketPath, string LocatorPath);

public static class HomeNativeEndpointLocations
{
    public static async ValueTask<HomeNativeEndpointLocation> CreateForLeasedHostAsync(HomeNativeSessionLease lease,
        IAppPaths trustedHomePaths, IAuthenticatedResourceActorSource actors, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease); ArgumentNullException.ThrowIfNull(trustedHomePaths);
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This endpoint is the Linux Unix-socket transport.");
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!lease.IsHeld || actor is null || actor.ProfileId != lease.ProfileId)
            throw new UnauthorizedAccessException("The designated Home session lease and current profile are required.");
        var root = RuntimeDirectory(trustedHomePaths);
        if (Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Home runtime endpoints cannot follow a link.");
        Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var epoch = Guid.NewGuid();
        var socket = Path.Combine(root, epoch.ToString("N") + ".sock");
        if (Encoding.UTF8.GetByteCount(socket) > 100) throw new PathTooLongException("The installed Home runtime path exceeds Unix socket transport limits.");
        var locator = await GetLocatorPathAsync(trustedHomePaths, cancellationToken).ConfigureAwait(false);
        if (!lease.IsHeld || actor != await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The Home session changed during endpoint creation.");
        return new(1, actor.ProfileId, epoch, Environment.ProcessId, socket, locator);
    }

    /// <summary>Children may locate the installed host without composing another Home graph. Always attest the connected OS peer afterwards.</summary>
    public static async ValueTask<string> GetLocatorPathAsync(IAppPaths trustedInstalledHomePaths,
        CancellationToken cancellationToken = default)
    {
        var principal = await new OperatingSystemPrincipalSource().GetPrincipalAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new PlatformNotSupportedException("No supported operating-system principal authority is available.");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(principal)));
        return Path.Combine(RuntimeDirectory(trustedInstalledHomePaths), key + ".endpoint.json");
    }
    private static string RuntimeDirectory(IAppPaths paths) => Path.Combine(paths.DataDirectory, "Home", "Runtime");
}
