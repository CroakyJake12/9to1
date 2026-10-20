using System.Threading;
using Haven.Application.Haven;

namespace HavenOS.Apps.Browse;

/// <summary>
/// Shared-control partition of the Browse/Haven workspace. The persistent Haven rail
/// exposes the genuine Home-owned model, notifications and profile controls. This
/// partial only wires those controls to the typed asynchronous gateway supplied by the
/// actual host. Shared opens are genuine retained operations: their real show/init/
/// account settlement is awaited and retained under the workspace's original-operation
/// ledger, so close joins actual faults/refusals rather than treating the open as an
/// instantaneous bool. It never fabricates model/account/badge/usage state and never
/// invokes inference.
/// </summary>
public sealed partial class BrowseNativeWorkspace
{
    private bool IsHavenSharedCommand(string command) => command is
        "9to1.Haven.OpenModel" or "9to1.Haven.OpenNotifications" or "9to1.Haven.OpenProfile";

    /// <summary>Runs one shared rail open as a retained original operation. The operation does
    /// not report success until the borrowed surface's real open has settled; a refused or
    /// failed open surfaces its structured message and never fabricates success.</summary>
    private async Task RunSharedOpenAsync(Task start, string command, CancellationToken token)
    {
        await start; token.ThrowIfCancellationRequested();
        var context = _hostContext;
        if (context?.SharedControls is not { } shared) return;
        try
        {
            var result = command switch
            {
                "9to1.Haven.OpenModel" => await shared.OpenModelPickerAsync(token).ConfigureAwait(false),
                "9to1.Haven.OpenNotifications" => await shared.OpenNotificationsAsync(token).ConfigureAwait(false),
                "9to1.Haven.OpenProfile" => await shared.OpenProfileAsync(token).ConfigureAwait(false),
                _ => HavenSharedOpenResult.Refused("UnknownCommand", "Unknown shared control command.")
            };
            if (!result.Opened && !string.IsNullOrWhiteSpace(result.Message))
                _status = result.Message;
        }
        catch (OperationCanceledException)
        {
            // Ignored cancellation stays safe; the open simply did not settle.
        }
        catch (Exception failure)
        {
            _status = "Browse retained the shared open failure: " + failure.Message;
        }
    }

    private bool? HavenSharedActionAvailable(string command) => command switch
    {
        "9to1.Haven.OpenModel" => _hostContext?.ModelSupported == true,
        "9to1.Haven.OpenNotifications" => _hostContext?.NotificationsSupported == true,
        "9to1.Haven.OpenProfile" => _hostContext?.ProfileSupported == true,
        _ => null
    };
}
