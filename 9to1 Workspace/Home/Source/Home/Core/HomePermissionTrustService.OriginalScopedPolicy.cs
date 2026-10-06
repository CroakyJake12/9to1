namespace HavenOS.Home.PermissionsTrustNotifications;
public sealed partial class HomePermissionTrustService
{
    // Scoped original-only caller owns the entire finite callback and conserves its exact
    // synchronous cause. The ordinary forgiving catalogue lookup remains unchanged.
    internal HomePermissionActionPolicy? ResolveOriginalTrustedActionPolicy(string appId, string actionId)
        => _resolvePolicy(appId, actionId);
}
