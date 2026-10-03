using System.Globalization;
using System.Runtime.Versioning;
namespace NineToOne.Os.Shell.Authority;
[SupportedOSPlatform("linux")]
internal static class LinuxRootPairedInstalledRuntimeCommand
{
    internal static async Task<int?> TryRunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0] != "--native-supervise-installed-widget-pair") return null;
        if (args.Length != 5 || !Id(args[1], out var uid) || !Id(args[2], out var gid))
            throw new UnauthorizedAccessException("Exact actual target credentials and protected runtime required.");
        await using var runtime = await LinuxRootPairedInstalledRuntime.StartAsync(uid, gid, args[3], args[4], ct);
        while (!runtime.OwnerChannel.IsCompleted && !runtime.OriginalHome.OriginalExitObserved.IsCompleted)
        {
            if (!await runtime.OriginalHome.IsOriginalCurrentAsync(ct) ||
                !await runtime.OriginalOwner.IsOriginalCurrentAsync(ct)) return 1;
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        if (runtime.OwnerChannel.IsCompleted) await runtime.OwnerChannel;
        else await runtime.OriginalHome.OriginalExitObserved;
        return 1;
    }
    private static bool Id(string value, out uint id) =>
        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) &&
        id is not 0 and not uint.MaxValue && id.ToString(CultureInfo.InvariantCulture) == value;
}
