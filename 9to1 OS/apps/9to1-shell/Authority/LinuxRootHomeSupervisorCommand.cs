using System.Globalization;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace NineToOne.Os.Shell.Authority;

[SupportedOSPlatform("linux")]
internal static class LinuxRootHomeSupervisorCommand
{
    internal static async Task<int?> TryRunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0] != "--native-supervise-home") return null;
        if (args.Length != 5 || !CanonicalId(args[1], out var uid) || !CanonicalId(args[2], out var gid) ||
            !Path.IsPathFullyQualified(args[3]) || Path.GetFullPath(args[3]) != args[3] ||
            !Path.IsPathFullyQualified(args[4]) || Path.GetFullPath(args[4]) != args[4] ||
            !LinuxRootOwnedFiles.DirectoryImmutable(args[4]))
            throw new UnauthorizedAccessException("Actual root-owned supervisor runtime and target credentials required.");
        var prepared = await LinuxRootHomeStartPreparation.ReadForAdministratorAsync(ct)
            ?? throw new UnauthorizedAccessException("One existing signed protected Home installation required.");
        var directory = Path.Combine(args[4], Guid.NewGuid().ToString("N"));
        // Root-created immutable parent makes socket substitution by the target user impossible.
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        if (!LinuxRootOwnedFiles.DirectoryImmutable(directory)) throw new UnauthorizedAccessException("Protected bootstrap parent unavailable.");
        var socketPath = Path.Combine(directory, "home.sock");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        LinuxRootSupervisedHome? home = null;
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
            listener.Listen(8);
            home = await LinuxRootSupervisedHome.StartPreparedAsync(prepared, listener, socketPath,
                uid, gid, args[3], ct) ?? throw new UnauthorizedAccessException("Actual original Home bootstrap was not admitted.");
            // Real original process/lease checks continue; this is not a fixture acceptance marker.
            while (!home.OriginalExitObserved.IsCompleted)
            {
                if (!await home.IsOriginalCurrentAsync(ct)) return 1;
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            await home.OriginalExitObserved; return 1;
        }
        finally
        {
            if (home is not null)
            {
                try
                {
                    if (!await home.ShutdownOriginalAndDrainForAdministratorAsync())
                        Console.Error.WriteLine("The original admitted Home did not acknowledge bounded shutdown.");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { Console.Error.WriteLine("The original admitted Home shutdown could not be observed."); }
                finally { home.Dispose(); }
            }
            listener.Dispose();
            // This command removes only its freshly created private route, never another epoch.
            if (File.Exists(socketPath)) File.Delete(socketPath);
            Directory.Delete(directory);
        }
    }
    private static bool CanonicalId(string input, out uint id) =>
        uint.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out id) &&
        id is not 0 and not uint.MaxValue && id.ToString(CultureInfo.InvariantCulture) == input;
}
