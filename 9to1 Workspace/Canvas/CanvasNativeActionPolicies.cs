using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Apps.Canvas;

/// <summary>Owning Canvas policy for creating a new document or publishing its exact captured revision.</summary>
public sealed class CanvasNativeActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "canvas" && actionId is "canvas.file.create" or "canvas.file.save"
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true)
            : null;
}
