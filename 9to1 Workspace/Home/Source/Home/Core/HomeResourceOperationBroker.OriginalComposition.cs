namespace HavenOS.Home.Core;

public sealed partial class HomeResourceOperationBroker
{
    // Primary-constructor dependencies are the exact original captured objects. No actor read.
    internal bool IsBoundToLocalCommitComposition(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles) =>
        permissions.IsBoundToStore(store) && resources.IsBoundToActorSource(profiles);
}
