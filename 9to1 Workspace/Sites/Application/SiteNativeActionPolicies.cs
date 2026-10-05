using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Apps.Sites.Application;

/// <summary>Trusted compiled declarations for local canonical project writes; this grants no publishing authority.</summary>
public sealed class SiteNativeActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == SiteNativeWriteIntent.TargetAppId && actionId is "sites.project.create" or "sites.project.save"
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true) : null;
}
