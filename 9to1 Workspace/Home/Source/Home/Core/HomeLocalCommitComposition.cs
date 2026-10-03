namespace HavenOS.Home.Core;

/// <summary>Pure reference composition; no IO, actor observation, request consumption or grant.
/// Final actual claimed-operation and raw Home commit fences remain mandatory.</summary>
public static class HomeLocalCommitComposition
{
    public static bool IsBound(HomeResourceOperationBroker? broker, FileHomeCoreStateStore? store,
        HomeLocalProfileIdentity? profiles, HomeResourceStoreOwnershipAuthority? ownership) =>
        broker is not null && store is not null && profiles is not null && ownership is not null &&
        profiles.IsBoundToStore(store) && broker.IsBoundToLocalCommitComposition(store, profiles) &&
        ownership.IsBoundTo(store, profiles);
}
