using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Home-owned read policy. Installed service declaration is admission, never consent.</summary>
public sealed class HomeCoreServiceReadActionPolicies : IHomeActionPolicySource
{
    public const string TargetAppId = "home";
    public const string Scope = "home.services.read";
    public const string RequiredInstalledServiceId = "home.core";

    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == TargetAppId && IsReadTarget(actionId)
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Routine, true, false)
            : null;

    internal static bool IsReadTarget(string target) => target is
        "9to1.Home.GetState" or "9to1.Home.GetServices" or
        "9to1.Home.GetService" or "9to1.Home.GetCompatibility";
}
