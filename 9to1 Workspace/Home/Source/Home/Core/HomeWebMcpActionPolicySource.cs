using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;
/// <summary>Trusted opt-in catalogue declaration. Page/request metadata cannot supply this policy.</summary>
public sealed class HomeWebMcpActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "browse" && actionId == "webmcp.invoke"
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, true, RequiresPerActionApproval: true) : null;
}
