using HavenOS.Home;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeCuiSurfaceTests
{
    [Fact]
    public void Briefing_follows_saved_order_and_redacts_hidden_removed_and_reconfigured_tiles()
    {
        var surface = HomeCuiSurface.LoadDefault();
        var a = Tile("a", 0);
        var b = Tile("b", 1);
        surface.ApplyLayout(new(1, 1, [a, b], false, false));
        surface.ApplyTileContent("b", Content("Second"));
        surface.ApplyTileContent("a", Content("First"));
        Assert.Equal("First  ·  Second", surface.BriefSummary);

        surface.ApplyLayout(new(1, 2, [a with { Order = 1 }, b with { Order = 0 }], false, false));
        Assert.Equal("Second  ·  First", surface.BriefSummary);
        surface.ApplyLayout(new(1, 3, [a with { Visibility = HomeTileVisibility.Hidden }, b], false, false));
        surface.ApplyTileContent("a", Content("Late hidden content"));
        Assert.Equal("Second", surface.BriefSummary);
        surface.ApplyLayout(new(1, 4, [a, b], false, false));
        Assert.Equal("Second", surface.BriefSummary);
        surface.ApplyLayout(new(1, 5, [a, b with { Configuration = new Dictionary<string, string> { ["source"] = "new" } }], false, false));
        Assert.Equal("No dashboard tiles have loaded.", surface.BriefSummary);
        surface.ApplyTileContent("b", Content("Updated source"));
        surface.ApplyLayout(new(1, 6, [], false, false));
        surface.ApplyTileContent("b", Content("Late removed content"));
        Assert.DoesNotContain("source", surface.BriefSummary);
        Assert.DoesNotContain("removed", surface.BriefSummary);
    }

    [Fact]
    public async Task Superseded_library_search_cannot_replace_the_current_results()
    {
        var index = new DeferredIndex();
        var controller = new HomeCuiController(new HomeDashboard(),
            libraryService: new HomeLibraryService(index, new AllowArtifacts(), new Router(), new Pins()));
        var older = controller.SearchLibraryAsync(new(Text: "older"));
        var current = controller.SearchLibraryAsync(new(Text: "current"));
        index.Current.SetResult([Artifact("current")]);
        await current;
        index.Older.SetResult([Artifact("older")]);
        await older;
        Assert.Equal("current", Assert.Single(controller.Surface.LibraryItems).ArtifactId);
    }

    [Fact]
    public async Task Cancelled_library_search_is_not_published_when_a_provider_ignores_cancellation()
    {
        var index = new DeferredIndex();
        var controller = new HomeCuiController(new HomeDashboard(),
            libraryService: new HomeLibraryService(index, new AllowArtifacts(), new Router(), new Pins()));
        using var cancel = new CancellationTokenSource();
        var refresh = controller.SearchLibraryAsync(new(Text: "current"), cancel.Token);
        cancel.Cancel();
        index.Current.SetResult([Artifact("late")]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Null(controller.Surface.LibraryPage);
    }

    [Fact]
    public async Task Events_distinguish_an_unconnected_source_from_an_empty_connected_source()
    {
        var service = new HomeEventsService([], new AllowEvents());
        var empty = await service.ListAsync(new());
        Assert.False(empty.Succeeded);
        Assert.Equal("Unavailable", empty.Code);
        var surface = HomeCuiSurface.LoadDefault();
        surface.ApplyEventsPage(empty);
        Assert.Contains("No activity sources", surface.EventsSummary);
        var invalid = await service.ListAsync(new(From: DateTimeOffset.UtcNow, To: DateTimeOffset.UtcNow.AddDays(-1)));
        Assert.Equal("InvalidQuery", invalid.Code);
        surface.ApplyEventsPage(invalid);
        Assert.Contains("invalid", surface.EventsSummary);
    }

    private static HomeTileInstance Tile(string id, int order) => new(id, "brief", "test.provider", order,
        HomeTileSize.Small, HomeTileVisibility.Visible, false, false, new Dictionary<string, string>(), null,
        ["source:" + id], HomeTileLifetime.Persistent);
    private static HomeTileContent Content(string summary) => new("available", summary, ["source"], new Dictionary<string, string>(), []);
    private static HomeArtifactReference Artifact(string id) => new("test", id, id, "document", null, null, null,
        DateTimeOffset.UtcNow, true, false, false, false, [], new(HomeRoute.Library, "test", id, TargetRouteId: "app.test"));
    private sealed class DeferredIndex : IHomeArtifactSearchIndex
    {
        internal TaskCompletionSource<IReadOnlyList<HomeArtifactReference>> Older { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<IReadOnlyList<HomeArtifactReference>> Current { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<HomeArtifactReference>> SearchAsync(HomeLibraryQuery query, CancellationToken cancellationToken) =>
            query.Text == "older" ? Older.Task : Current.Task;
        public Task<HomeArtifactReference?> GetByIdAsync(string ownerApp, string artifactId, CancellationToken cancellationToken) => Task.FromResult<HomeArtifactReference?>(null);
    }
    private sealed class AllowArtifacts : IHomeArtifactAuthorization
    {
        public Task<bool> CanReadAsync(string ownerApp, string artifactId, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<bool> CanOpenAsync(HomeDeepLink link, CancellationToken cancellationToken) => Task.FromResult(true);
    }
    private sealed class AllowEvents : IHomeActivityAuthorization
    { public Task<bool> CanReadAsync(HomeActivityEvent activity, CancellationToken cancellationToken) => Task.FromResult(true); }
    private sealed class Pins : IHomeLibraryPreferences
    {
        public Task<bool> IsPinnedAsync(string ownerApp, string artifactId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task SetPinnedAsync(string ownerApp, string artifactId, bool pinned, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Router : IHomeDeepLinkRouter
    {
        public Task<HavenOS.Home.Core.HomeFeatureNavigationResult> OpenAsync(HomeDeepLink link, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A search must not launch an artifact.");
    }
}
