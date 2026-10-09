using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HomePermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;

namespace HavenOS.Files.NativeHost;

// Browser native transfer approval does not authorize the Files write. This
// implemented Files action always receives its own individual Home review.
public sealed class FilesOriginalBrowserDownloadActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == "files" && actionId == "9to1.Files.RegisterBrowserDownload"
            ? new(HomePermissionRisk.Elevated, IsReversible: false, HasExternalSideEffects: false,
                RequiresPerActionApproval: true) : null;
}
