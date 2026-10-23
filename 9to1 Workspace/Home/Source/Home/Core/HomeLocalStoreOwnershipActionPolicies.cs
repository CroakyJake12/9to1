using HavenOS.Home.PermissionsTrustNotifications;
using PermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;

namespace HavenOS.Home.Core;

/// <summary>Home owns the manual import of an existing local store into its current
/// local profile. Registering this action supplies no approval, ownership or content access.</summary>
public sealed class HomeLocalStoreOwnershipActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "9to1.home.local-profile" && actionId == "home.profile.importStore"
            ? new(PermissionRisk.High, false, false, true)
            : null;
}
