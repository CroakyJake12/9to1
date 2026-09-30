using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Images;

/// <summary>Compiled policy for publishing a separately approved local PNG snapshot.</summary>
public sealed class PictureNativeActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "picture" && actionId is "picture.file.export" or "picture.file.import" or "picture.file.save"
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true)
            : null;
}
