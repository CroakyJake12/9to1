using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using PermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;

namespace HavenOS.Files.NativeHost;

/// <summary>Compiled Files policy for the two implemented native structural actions.
/// A declaration does not approve, claim or commit an operation.</summary>
public sealed class FilesNativeBrowserActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId)
    {
        if (appId != "files" || actionId is not ("9to1.Files.CreateFolder" or "9to1.Files.Rename")) return null;
        var name = actionId["9to1.Files.".Length..];
        var definition = FilesServiceActionCatalog.GetPage().Items.Single(item => item.Name == name);
        return new(definition.Risk == FilesRiskLevel.Ordinary ? PermissionRisk.Routine : PermissionRisk.Elevated,
            definition.IsReversible, definition.HasExternalSideEffects, RequiresPerActionApproval: true);
    }
}
