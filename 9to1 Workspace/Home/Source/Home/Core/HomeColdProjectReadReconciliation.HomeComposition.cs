using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;

public sealed partial class HomeColdProjectReadReconciliation
{
    // Pure actual constructor tuple. No source operation, grant or producer qualification.
    public bool HasOriginalHomeComposition(FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles,
        ResourceAuthorizationService resources, HomeResourceOperationBroker broker, HomePermissionTrustService permissions) =>
        ReferenceEquals(_store, store) && ReferenceEquals(_profiles, profiles) && ReferenceEquals(_resources, resources) &&
        ReferenceEquals(_broker, broker) && ReferenceEquals(_permissions, permissions);
}
