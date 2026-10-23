using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using PermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;

namespace HavenOS.Files.NativeHost;

/// <summary>Compiled Files policy for implemented native structural and access-revocation actions.
/// A declaration does not approve, claim or commit an operation.</summary>
public sealed class FilesNativeBrowserActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId)
    {
        if (appId != "files" || actionId is not ("9to1.Files.CreateFolder" or "9to1.Files.Rename" or "9to1.Files.Move" or "9to1.Files.Delete" or "9to1.Files.Restore" or "9to1.Files.Share.Revoke")) return null;
        var name = actionId["9to1.Files.".Length..];
        var definition = FilesServiceActionCatalog.GetPage().Items.Single(item => item.Name == name);
        return new(definition.Risk == FilesRiskLevel.Ordinary ? PermissionRisk.Routine : PermissionRisk.Elevated,
            definition.IsReversible, definition.HasExternalSideEffects, RequiresPerActionApproval: true);
    }
}
