using Haven.Application.Automations;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

/// <summary>Compiled declarations for recoverable canonical definition writes only.
/// Actual canonical resource access and private owner admission remain mandatory; no graph/run grant.</summary>
public sealed class AutomationDefinitionActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == AutomationDefinitionChange.TargetAppID && actionId is
            "automations.create" or "automations.update" or "automations.disable" or
            "automations.delete" or "automations.restore" or "automations.recover"
            ? new(HomePermissionRisk.High, IsReversible: true, HasExternalSideEffects: false,
                RequiresPerActionApproval: true) : null;
}
