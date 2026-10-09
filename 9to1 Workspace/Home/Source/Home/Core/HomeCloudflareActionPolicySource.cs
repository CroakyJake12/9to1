using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;

/// <summary>Closed owning-app risk catalogue. It grants neither resources, connections nor account permissions.</summary>
public sealed class HomeCloudflareActionPolicySource : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appId, string actionId)
    {
        if (appId != "cloudflare") return null;
        if (actionId is "cloudflare.connection.configure" or "cloudflare.kv.reconcileKnownCreate" or "cloudflare.staging.delegateTaskMarker") return new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true);
        var entry = CloudflareTypedToolCatalogue.Descriptors.SingleOrDefault(x => x.ActionId == actionId);
        if (entry is null) return null;
        return entry.IsReadOnly
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, false, true, true)
            : new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, entry.Kind is CloudflareOperationKind.KvCreate or CloudflareOperationKind.KvMarkerPut, true, true);
    }
}
