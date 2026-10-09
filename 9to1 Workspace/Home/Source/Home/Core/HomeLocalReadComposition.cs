namespace HavenOS.Home.Core;

/// <summary>Pure exact local read composition, never actor/source/resource authority.</summary>
public static class HomeLocalReadComposition
{
    public static bool IsBound(FileHomeCoreStateStore? store, HomeLocalProfileIdentity? profiles,
        HomeResourceStoreOwnershipAuthority? ownership) =>
        store is not null && profiles is not null && ownership is not null &&
        profiles.IsBoundToStore(store) && ownership.IsBoundTo(store, profiles);
}
