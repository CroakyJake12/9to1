using Haven.Application;
using Haven.Browser;
using Haven.Core;

namespace HavenOS.Apps.Browse;

/// <summary>A projection into the existing canonical workspace session, not a new
/// rich persistence schema. Private tabs and private selection are excluded.</summary>
public sealed record BrowseClosedTabProjection(BrowserTabState Tab, int Index, BrowseEngineKind? EngineOverride);
/// <summary>Projection of an existing canonical group. The GUID is its identity; ordered member IDs remain the workspace owner's data.</summary>
public sealed record BrowseTabGroupProjection(Guid Id, string Name, bool IsCollapsed, IReadOnlyList<Guid> OrderedTabIds);

/// <summary>A stable presentation object over two existing BrowserTabs, including while inactive.
/// It owns no profile, navigation history, renderer or duplicate tab state.</summary>
public sealed record BrowseSplitViewProjection(Guid Id, IReadOnlyList<Guid> PaneTabIds,
    Guid FocusedTabId, SplitOrientation Orientation, double PrimaryPaneRatio);

public sealed record BrowseTabSessionProjection(IReadOnlyList<BrowserTabState> Tabs, Guid? SelectedTabId)
{
    public IReadOnlyList<BrowseClosedTabProjection> ClosedTabs { get; init; } = [];
    /// <summary>Tab identities currently occupying the two durable split panes.</summary>
    /// <summary>Null means a legacy producer omitted pane state; empty explicitly collapses the split.</summary>
    public IReadOnlyList<Guid>? PaneTabIds { get; init; }
    public SplitOrientation? SplitOrientation { get; init; }
    public double? PrimaryPaneRatio { get; init; }
    /// <summary>Null means an older producer omitted group metadata; an empty list explicitly removes this window's groups.</summary>
    public IReadOnlyList<BrowseTabGroupProjection>? Groups { get; init; }
    /// <summary>Null means a legacy producer supplied only the active pane layout.
    /// Empty explicitly removes saved split objects; selection alone never removes them.</summary>
    public IReadOnlyList<BrowseSplitViewProjection>? SplitViews { get; init; }
}

/// <summary>The shared host adapts its existing WorkspaceSessionSnapshot owner.
/// Other browser data remains with BrowserDataService. Every returned task belongs
/// to the supplied owner and is retained by Browse before it is awaited.</summary>
public interface IBrowseTabSessionPersistence
{
    Task<BrowseTabSessionProjection?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(BrowseTabSessionProjection projection, CancellationToken cancellationToken);
}
