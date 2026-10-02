using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

/// <summary>Explicit graph publication declarations only. No SQL association, run or node effect grant.</summary>
public sealed class AutomationGraphPublicationActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "automations" && actionId is "automations.graph.save-draft" or "automations.activate"
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, IsReversible: true,
                HasExternalSideEffects: false, RequiresPerActionApproval: true) : null;
}
