using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Controls;

/// <summary>Desktop compatibility surface over the canonical Home-owned native approvals UI.</summary>
public sealed class HomeApprovalCuiSurface(HomeCoreRuntime runtime, HomeLocalProfileIdentity profiles,
    HomePermissionTrustService permissions) : HavenOS.Home.NativeUI.HomeApprovalCuiSurface(runtime, profiles, permissions);
