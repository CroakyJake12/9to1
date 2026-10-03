using System.Globalization;
using System.Runtime.Versioning;
namespace NineToOne.Os.Shell.Authority;

internal sealed record LinuxControlledInstalledOwnerEndpoints(string OriginalAdministratorSocket, string RoutedWidgetSocket);

// Explicit future Main owner-mode dispatch, before any profile/service/GUI input.
// Credentials and endpoint strings choose startup only; they issue no actor or installed role.
[SupportedOSPlatform("linux")]
internal static class LinuxControlledInstalledOwnerChildStartup
{
    internal static LinuxControlledInstalledOwnerEndpoints PrepareBeforeProfileIo(string[] args)
    {
        if (args.Length != 5 || args[0] != "--native-controlled-widget-owner" ||
            !TryId(args[1], out var uid) || !TryId(args[2], out var gid) ||
            !Path.IsPathFullyQualified(args[3]) || Path.GetFullPath(args[3]) != args[3] ||
            !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(args[3])!) ||
            !Path.IsPathFullyQualified(args[4]) || Path.GetFullPath(args[4]) != args[4])
            throw new UnauthorizedAccessException("Exact protected controlled-owner startup required.");
        // Actual existing boundary requires root, inherited NNP and safe credential controls,
        // performs the real drop and observes the actual managed task cohort. Never execute locally.
        LinuxControlledChildPrivilegeBoundary.ApplyBeforeProfileIo(uid, gid);
        return new(args[3], args[4]);
    }
    private static bool TryId(string value, out uint id) =>
        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) &&
        id is not 0 and not uint.MaxValue && id.ToString(CultureInfo.InvariantCulture) == value;
}
