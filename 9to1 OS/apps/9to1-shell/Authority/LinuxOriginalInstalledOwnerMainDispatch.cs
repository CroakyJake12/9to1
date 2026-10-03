using System.Runtime.Versioning;
using System.Net.Sockets;
namespace NineToOne.Os.Shell.Authority;

// The maintained Main calls this BEFORE Home startup preparation, profile DI, or GUI.
// Ordinary arguments return null and preserve the existing local/free startup ABI.
[SupportedOSPlatform("linux")]
internal static class LinuxOriginalInstalledOwnerMainDispatch
{
    internal static async Task<int?> TryRunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0] != "--native-controlled-widget-owner") return null;
        try
        {
            var endpoints = LinuxControlledInstalledOwnerChildStartup.PrepareBeforeProfileIo(args);
            await LinuxOriginalInstalledOwnerServiceEntry.RunAsync(endpoints.OriginalAdministratorSocket,
                endpoints.RoutedWidgetSocket, LinuxAdministratorOriginalSessionClient.ConnectForOwnerAsync, ct).ConfigureAwait(false);
            return 0;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or
            System.ComponentModel.Win32Exception or EntryPointNotFoundException or DllNotFoundException or
            InvalidOperationException or SocketException or OperationCanceledException)
        {
            // No fallback into the Home lease or GUI path after owner-mode failure.
            Console.Error.WriteLine("The original supervised installed owner retired or could not start.");
            return 1;
        }
    }
}
