namespace Haven.Core;

public enum WorkspaceWindowKind { Main = 0, Normal = 1, PopUp = 2 }
public enum WorkspaceLayoutKind { Single = 0, Split = 1 }
public enum SplitOrientation { Horizontal = 0, Vertical = 1 }

public sealed record TabSessionSnapshot(
    Guid Id,
    string AppKey,
    string Title,
    string StateJson,
    string? NavigationJson,
    double? ScrollOffset,
    Guid? GroupId,
    bool IsPinned,
    bool IsProtected,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TabGroupSnapshot(
    Guid Id,
    string Name,
    bool IsCollapsed,
    IReadOnlyList<Guid> OrderedTabIds)
{
    /// <summary>Optional sRGB #RRGGBB group accent, independent of tab profile and theme surfaces.</summary>
    public string? Color { get; init; }
}

public sealed record WorkspacePaneSnapshot(Guid Id, Guid TabId, int Order);

/// <summary>Durable presentation over existing tab identities, retained even when another tab is active.</summary>
public sealed record WorkspaceSplitViewSnapshot(Guid Id, IReadOnlyList<WorkspacePaneSnapshot> Panes,
    Guid FocusedTabId, SplitOrientation Orientation, double PrimaryRatio);

public sealed record WorkspaceLayoutSnapshot(
    Guid Id,
    WorkspaceLayoutKind Kind,
    SplitOrientation Orientation,
    double PrimaryRatio,
    IReadOnlyList<WorkspacePaneSnapshot> Panes)
{
    public const double MinimumPaneRatio = 0.2;
    public const double MaximumPaneRatio = 0.8;
}

public sealed record WorkspaceWindowSnapshot(
    Guid Id,
    WorkspaceWindowKind Kind,
    WorkspaceLayoutSnapshot Layout,
    IReadOnlyList<Guid> OrderedTabIds,
    Guid? SelectedTabId,
    string? BoundsJson,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Bounded, private-free recovery metadata for recently closed tabs in this window.</summary>
    public IReadOnlyList<WorkspaceClosedTabSnapshot> RecentlyClosedTabs { get; init; } = [];
    /// <summary>Additive v1 session metadata. Older active-only layouts migrate on the owning bridge's next save.</summary>
    public IReadOnlyList<WorkspaceSplitViewSnapshot> SplitViews { get; init; } = [];
}

/// <summary>Recoverable tab identity and safe reopen metadata, not a live/open tab row.</summary>
public sealed record WorkspaceClosedTabSnapshot(
    Guid Id,
    string Title,
    string Address,
    string ProfileId,
    Guid? GroupId,
    int Index,
    string? EngineOverride,
    bool IsPinned,
    DateTimeOffset ClosedAt);

public sealed record WorkspaceSessionSnapshot(
    int SchemaVersion,
    IReadOnlyList<TabSessionSnapshot> Tabs,
    IReadOnlyList<TabGroupSnapshot> Groups,
    IReadOnlyList<WorkspaceWindowSnapshot> Windows,
    DateTimeOffset SavedAt)
{
    public const int CurrentSchemaVersion = 1;
}
