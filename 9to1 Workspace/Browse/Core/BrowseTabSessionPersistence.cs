using Haven.Browser;

namespace HavenOS.Apps.Browse;

/// <summary>A projection into the existing canonical workspace session, not a new
/// rich persistence schema. Private tabs and private selection are excluded.</summary>
public sealed record BrowseTabSessionProjection(IReadOnlyList<BrowserTabState> Tabs, Guid? SelectedTabId);

/// <summary>The shared host adapts its existing WorkspaceSessionSnapshot owner.
/// Other browser data remains with BrowserDataService. Every returned task belongs
/// to the supplied owner and is retained by Browse before it is awaited.</summary>
public interface IBrowseTabSessionPersistence
{
    Task<BrowseTabSessionProjection?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(BrowseTabSessionProjection projection, CancellationToken cancellationToken);
}
