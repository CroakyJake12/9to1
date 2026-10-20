using Haven.Application.Haven;

namespace HavenOS.Apps.Browse;

/// <summary>
/// Shared-control partition of the Browse/Haven workspace. The persistent Haven rail
/// exposes the genuine Home-owned model, notifications and profile controls; this
/// partial only wires those controls to the typed gateway supplied by the actual host.
/// It never fabricates model/account/badge/usage state and never invokes inference.
/// </summary>
public sealed partial class BrowseNativeWorkspace
{
    private bool TryDispatchHavenShared(string command)
    {
        var context = _hostContext;
        if (context is null || context.SharedControls is not { } shared) return false;
        return command switch
        {
            "9to1.Haven.OpenModel" => shared.OpenModelPicker(),
            "9to1.Haven.OpenNotifications" => shared.OpenNotifications(),
            "9to1.Haven.OpenProfile" => shared.OpenProfile(),
            _ => false
        };
    }

    private bool? HavenSharedActionAvailable(string command) => command switch
    {
        "9to1.Haven.OpenModel" => _hostContext?.ModelSupported == true,
        "9to1.Haven.OpenNotifications" => _hostContext?.NotificationsSupported == true,
        "9to1.Haven.OpenProfile" => _hostContext?.ProfileSupported == true,
        _ => null
    };
}
