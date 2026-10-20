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
/// instantaneous bool. A real fault is never turned into success: it is published as a
/// structured status and the original open task retains the fault so close joins it.
/// It never fabricates model/account/badge/usage state and never invokes inference.
/// </summary>
public sealed partial class BrowseNativeWorkspace
{
    private bool IsHavenSharedCommand(string command) => command is
        "9to1.Haven.OpenModel" or "9to1.Haven.OpenNotifications" or "9to1.Haven.OpenProfile";

    /// <summary>Runs one shared rail open as a retained original operation inside the same
    /// logical-driver invocation guard as ordinary commands, so a reentrant provider calling
    /// close cannot join its own in-flight action. The operation does not report success until
    /// the borrowed surface's real open has settled; a refused or failed open publishes its
    /// structured message and retains the original fault (never fabricates success).</summary>
    private async Task RunSharedOpenAsync(Task start, string command, CancellationToken token,
        CommandInvocation invocation)
    {
        var prior = LogicalDriver.Value; LogicalDriver.Value = invocation;
        try
        {
            await start; token.ThrowIfCancellationRequested();
            var context = _hostContext;
            if (context?.SharedControls is not { } shared) return;
            HavenSharedOpenResult result;
            try
            {
                result = command switch
                {
                    "9to1.Haven.OpenModel" => await shared.OpenModelPickerAsync(token),
                    "9to1.Haven.OpenNotifications" => await shared.OpenNotificationsAsync(token),
                    "9to1.Haven.OpenProfile" => await shared.OpenProfileAsync(token),
                    _ => HavenSharedOpenResult.Refused("UnknownCommand", "Unknown shared control command.")
                };
            }
            catch (OperationCanceledException)
            {
                // Cancellation stays safe and is surfaced as a structured non-success status; it
                // is never reclassified as success and the open task completes without fault so
                // an accepted close can join an ignored-cancellation original.
                PublishSharedStatus("Cancelled", "The shared control open was cancelled.");
                return;
            }
            catch (Exception failure)
            {
                PublishSharedStatus("OpenFailed", failure.Message);
                throw; // retain the original fault so close joins it; never fabricate success
            }
            if (!result.Opened && !string.IsNullOrWhiteSpace(result.Message))
                PublishSharedStatus(result.Code ?? "Refused", result.Message);
        }
        finally
        {
            invocation.Active = false;
            LogicalDriver.Value = prior;
        }
    }

    private void PublishSharedStatus(string code, string message)
    {
        _status = code + ": " + message;
        Changed();
    }

    /// <summary>Surfaces a structured shared-panel command refusal (stale/malformed) from a
    /// borrowed shared surface to the rail status area. Genuine refusals are published, never
    /// silently swallowed as if the command succeeded.</summary>
    public void PublishSharedRefusal(string code, string message)
        => PublishSharedStatus(code, message);

    private bool? HavenSharedActionAvailable(string command) => command switch
    {
        "9to1.Haven.OpenModel" => _hostContext?.ModelSupported == true,
        "9to1.Haven.OpenNotifications" => _hostContext?.NotificationsSupported == true,
        "9to1.Haven.OpenProfile" => _hostContext?.ProfileSupported == true,
        _ => null
    };
}
