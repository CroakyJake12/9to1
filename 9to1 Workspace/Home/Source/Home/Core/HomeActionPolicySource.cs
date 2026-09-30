using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

/// <summary>Compiled trusted owning-app catalogue registered by the native host, never by a model or operation payload.</summary>
public interface IHomeActionPolicySource
{
    HomePermissionActionPolicy? TryGet(string appId, string actionId);
}
