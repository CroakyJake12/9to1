using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Trusted Notes operations. Save requires individual Home review and the original
/// repository publication fence; this catalogue is not a document mutation capability.</summary>
public sealed class HomeNotesActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == HomeNotesResourceAdmissionSource.TargetAppId &&
        actionId is HomeNotesResourceAdmissionSource.ReadAction or HomeNotesResourceAdmissionSource.SaveAction
            ? new(HomePermissionRisk.Routine, true, false, true) : null;
}
