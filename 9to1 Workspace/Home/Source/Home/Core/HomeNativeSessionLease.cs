using System.Security.Cryptography;
using System.Text;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Cross-process OS-local session lease for the designated Home host. Child apps never acquire or replace it.</summary>
public sealed class HomeNativeSessionLease : IDisposable
{
    private readonly FileStream _handle;
    public string ProfileId { get; }
    private HomeNativeSessionLease(FileStream handle, string profileId) { _handle = handle; ProfileId = profileId; }

    /// <summary>Uses trusted host paths and actual ambient profile authority, never requested usernames or application arguments.</summary>
    public static async ValueTask<HomeNativeSessionLease?> TryAcquireAsync(IAuthenticatedResourceActorSource actors,
        IAppPaths paths, CancellationToken cancellationToken = default)
    {
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || actor.OrganisationId is not null || string.IsNullOrWhiteSpace(actor.ProfileId)) return null;
        var root = Path.Combine(paths.DataDirectory, "Home", "Runtime");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(root);
        else Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(actor.ProfileId)));
        var path = Path.Combine(root, key + ".lease");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Home session lease cannot follow a link.");
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        FileStream handle;
        try { handle = new FileStream(path, options); }
        catch (IOException) when (File.Exists(path)) { return null; }
        try
        {
            if (actor != await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false)) { handle.Dispose(); return null; }
            return new(handle, actor.ProfileId);
        }
        catch { handle.Dispose(); throw; }
    }
    public void Dispose() => _handle.Dispose();
}
