using System.Globalization;
using System.Runtime.Versioning;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Administrator supervisor entry into the real maintained Home Main path.
/// Arguments choose process credentials only; they are not an actor, installed role,
/// session lease, or trusted launch receipt. No owner enrollment is granted here.</summary>
[SupportedOSPlatform("linux")]
internal static class LinuxControlledHomeChildStartup
{
    internal static string? OriginalAdministratorSocket { get; private set; }
    internal static string? OriginalCanonicalSocket { get; private set; }
    internal static string? OriginalWidgetObservationSocket { get; private set; }
    internal static bool ServiceOnly { get; private set; }
    public static string[] PrepareBeforeProfileIo(string[] args)
    {
        if (args.Length == 0 || args[0] != "--native-controlled-home-child") return args;
        if (args.Length is not (4 or 5 or 7 or 9) || !TryCanonicalId(args[1], out var uid) || !TryCanonicalId(args[2], out var gid))
            throw new UnauthorizedAccessException("The administrator Home child requires exact non-root UID and GID arguments.");
        if (!Path.IsPathFullyQualified(args[3]) || Path.GetFullPath(args[3]) != args[3] ||
            !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(args[3])!) ||
            (args.Length >= 5 && args[4] != "--service-only") ||
            (args.Length >= 7 && (args[5] != "--canonical-socket" || !Path.IsPathFullyQualified(args[6]) ||
                Path.GetFullPath(args[6]) != args[6] || !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(args[6])!))) ||
            (args.Length == 9 && (args[7] != "--widget-observation-socket" || !Path.IsPathFullyQualified(args[8]) ||
                Path.GetFullPath(args[8]) != args[8] || args[8] == args[6] ||
                !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(args[8])!))))
            throw new UnauthorizedAccessException("Protected original supervisor socket required.");
        LinuxControlledChildPrivilegeBoundary.ApplyBeforeProfileIo(uid, gid);
        OriginalAdministratorSocket = args[3]; ServiceOnly = args.Length >= 5;
        OriginalCanonicalSocket = args.Length >= 7 ? args[6] : null;
        OriginalWidgetObservationSocket = args.Length == 9 ? args[8] : null;
        return [];
    }
    private static bool TryCanonicalId(string value, out uint result) =>
        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result) &&
        result is not 0 and not uint.MaxValue && result.ToString(CultureInfo.InvariantCulture) == value;
}
