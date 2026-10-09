namespace Haven.Core;

public enum ExtensionSidebarTargetKind { Page, AppSurface, CuiSurface, Automation, GenerativePage }

/// <summary>Stable declared sidebar item. The enabling Space and host authority are resolved at runtime.</summary>
public sealed record ExtensionSidebarContribution(
    string SidebarItemId, string Label, string IconKey, ExtensionSidebarTargetKind TargetKind, string TargetId);
