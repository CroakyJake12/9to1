using HavenOS.Home;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeEcosystemTests
{
    [Fact]
    public async Task Layout_mutations_are_revisioned_and_AI_controls_are_independent()
    {
        var fixture = new LayoutFixture();
        var service = fixture.Service;
        var initial = await service.GetAsync();
        var added = await service.AddAsync(initial.Revision, "provider", "tile-1");
        Assert.True(added.Succeeded);
        var controls = await service.SetAiControlsAsync(added.Layout.Revision, allowGeneratedTiles: false, allowReorder: true);
        Assert.True(controls.Succeeded);
        Assert.False(controls.Layout.AllowAiGeneratedTiles);
        Assert.True(controls.Layout.AllowAiReorder);
        var stale = await service.RemoveAsync(added.Layout.Revision, "tile-1");
        Assert.Equal("RevisionConflict", stale.Code);
        Assert.Single(stale.Layout.Tiles);
    }

    [Fact]
    public async Task AI_layout_requires_opt_in_provenance_and_sources_and_respects_protected_tiles()
    {
        var fixture = new LayoutFixture();
        var service = fixture.Service;
        var added = await service.AddAsync(0, "provider", "tile-1");
        var blocked = await service.ApplyAiLayoutAsync(added.Layout.Revision, [fixture.Tile("ai-1")]);
        Assert.Equal("AiTileCreationDisabled", blocked.Code);
        var enabled = await service.SetAiControlsAsync(added.Layout.Revision, true, true);
        var aiTile = fixture.Tile("ai-1");
        var created = await service.ApplyAiLayoutAsync(enabled.Layout.Revision, [aiTile]);
        Assert.True(created.Succeeded);
        Assert.Equal(HomeTileChangeKind.Ai, created.Layout.ChangeKind);
        Assert.Single(fixture.Audit.Changes);
        var pinned = await service.SetPinnedAsync(created.Layout.Revision, "ai-1", true);
        var attempt = await service.ApplyAiLayoutAsync(pinned.Layout.Revision,
            [aiTile with { Order = 5 }]);
        Assert.Equal("TileProtected", attempt.Code);
    }

    [Fact]
    public async Task Library_filters_permissions_paginates_and_rechecks_before_open()
    {
        var a = Artifact("app.one", "1", "Alpha", pinned: true);
        var b = Artifact("app.two", "2", "Beta", pinned: false);
        var index = new FakeArtifactIndex([a, b]);
        var authorization = new FakeArtifactAuthorization { DeniedIds = ["2"] };
        var router = new FakeRouter();
        var service = new HomeLibraryService(index, authorization, router, new FakePreferences([("app.one", "1")]));
        var page = await service.SearchAsync(new HomeLibraryQuery(Pinned: true, Limit: 1));
        Assert.Single(page.Items);
        Assert.Equal("1", page.Items[0].ArtifactId);
        var denied = await service.OpenAsync("app.two", "2");
        Assert.Equal("ArtifactUnavailable", denied.Code);
        authorization.DeniedIds.Clear();
        var opened = await service.OpenAsync("app.one", "1");
        Assert.True(opened.Succeeded);
        Assert.Equal("1", router.Opened!.EntityId);
    }

    [Fact]
    public async Task Events_keep_authoritative_source_provenance_and_never_invent_records()
    {
        var sourceEvent = new HomeActivityEvent("event-1", DateTimeOffset.UtcNow, "app.one", "ArtifactChanged",
            "caller-id", "Caller", [new HomeDeepLink(HomeRoute.Library, "app.one", "artifact-1", TargetRouteId: "app.one.document")], "info", "artifact", "Artifact updated.");
        var mismatched = sourceEvent with { EventId = "spoof", SourceApp = "app.other" };
        var service = new HomeEventsService([new FakeActivitySource("app.one", [sourceEvent, sourceEvent, mismatched])], new AllowActivity());
        var page = await service.ListAsync(new HomeEventsQuery(Limit: 1));
        Assert.Single(page.Items);
        Assert.Equal("event-1", page.Items[0].EventId);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Navigation_retains_destination_state_and_uses_the_canonical_feature_route_id()
    {
        var state = new HomeNavigationState();
        state.Navigate(HomeRoute.Library);
        state.State(HomeRoute.Library).Query = "report";
        var router = new FakeRouter();
        Assert.True(await state.OpenDeepLinkAsync(new HomeDeepLink(HomeRoute.Events, "app.one", "event-1", TargetRouteId: HomeFeatureRouteIds.Events), router));
        Assert.Equal(HomeRoute.Events, state.Current);
        Assert.Equal(HomeFeatureRouteIds.Events, router.Opened!.RouteId);
        state.Navigate(HomeRoute.Library);
        Assert.Equal("report", state.State(HomeRoute.Library).Query);
    }

    [Fact]
    public async Task Json_layout_and_library_pin_stores_survive_service_recreation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "home-ecosystem-" + Guid.NewGuid().ToString("N"));
        try
        {
            var layoutStore = new HomeJsonDashboardLayoutStore(Path.Combine(directory, "layout.json"));
            var dashboard = new HomeDashboardLayoutService(layoutStore, new FakeProviders());
            var add = await dashboard.AddAsync(0, "provider", "stable-tile-id");
            Assert.True(add.Succeeded);
            var reloaded = await new HomeDashboardLayoutService(layoutStore, new FakeProviders()).GetAsync();
            Assert.Equal("stable-tile-id", Assert.Single(reloaded.Tiles).TileInstanceId);

            var audit = new FakeAudit();
            var withHistory = new HomeDashboardLayoutService(layoutStore, new FakeProviders(), audit);
            var enabled = await withHistory.SetAiControlsAsync(reloaded.Revision, true, true);
            var ai = await withHistory.ApplyAiLayoutAsync(enabled.Layout.Revision, [new HomeTileInstance(
                "generated", "brief", "provider", 2, HomeTileSize.Small, HomeTileVisibility.Visible, false, false,
                new Dictionary<string, string>(), "AI briefing", ["event-1"], HomeTileLifetime.Persistent)]);
            Assert.True(ai.Succeeded);
            var undo = await withHistory.UndoLastAiChangeAsync(ai.Layout.Revision);
            Assert.True(undo.Succeeded);
            Assert.DoesNotContain(undo.Layout.Tiles, tile => tile.TileInstanceId == "generated");
            Assert.Single(audit.Changes);

            var pins = new HomeJsonLibraryPreferences(Path.Combine(directory, "library.json"));
            await pins.SetPinnedAsync("app.one", "artifact-1", true, CancellationToken.None);
            Assert.True(await new HomeJsonLibraryPreferences(Path.Combine(directory, "library.json"))
                .IsPinnedAsync("app.one", "artifact-1", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static HomeArtifactReference Artifact(string app, string id, string name, bool pinned) => new(
        app, id, name, "document", "/docs/" + id, "owner", null, DateTimeOffset.UtcNow, true, pinned,
        false, false, [], new HomeDeepLink(HomeRoute.Library, app, id, TargetRouteId: app + ".document"));

    private sealed class LayoutFixture
    {
        private readonly FakeStore _store = new();
        public FakeAudit Audit { get; } = new();
        public HomeDashboardLayoutService Service => new(_store, new FakeProviders(), Audit);
        public HomeTileInstance Tile(string id) => new(id, "brief", "provider", 1, HomeTileSize.Small,
            HomeTileVisibility.Visible, false, false, new Dictionary<string, string>(), "generated from activity",
            ["event-1"], HomeTileLifetime.Persistent);
    }
    private sealed class FakeStore : IHomeDashboardLayoutStore
    {
        private HomeDashboardLayout? _layout;
        public Task<HomeDashboardLayout?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_layout);
        public Task<bool> TrySaveAsync(long expectedRevision, HomeDashboardLayout layout, CancellationToken cancellationToken)
        {
            if ((_layout?.Revision ?? 0) != expectedRevision) return Task.FromResult(false);
            _layout = layout;
            return Task.FromResult(true);
        }
    }
    private sealed class FakeProviders : IHomeTileProviderRegistry
    {
        private readonly IHomeTileProvider _provider = new FakeProvider();
        public IReadOnlyList<IHomeTileProvider> GetProviders() => [_provider];
        public IHomeTileProvider? Find(string providerId) => providerId == "provider" ? _provider : null;
    }
    private sealed class FakeProvider : IHomeTileProvider
    {
        public HomeTileProviderDescriptor Descriptor { get; } = new(1, "provider", "brief", "app.one",
            new HashSet<HomeTileSize> { HomeTileSize.Small, HomeTileSize.Medium }, [], [], TimeSpan.FromMinutes(5), [], true, true);
        public Task<HomeTileContent> RefreshAsync(HomeTileInstance tile, CancellationToken cancellationToken) =>
            Task.FromResult(new HomeTileContent("available", "Observed source summary.", tile.SourceEntityIds, new Dictionary<string, string>(), []));
    }
    private sealed class FakeAudit : IHomeDashboardAuditSink
    {
        public List<(HomeDashboardLayout Before, HomeDashboardLayout After)> Changes { get; } = [];
        public Task RecordLayoutChangeAsync(HomeDashboardLayout before, HomeDashboardLayout after, string actor, CancellationToken cancellationToken)
        { Changes.Add((before, after)); return Task.CompletedTask; }
    }
    private sealed class FakeArtifactIndex(IReadOnlyList<HomeArtifactReference> items) : IHomeArtifactSearchIndex
    {
        public Task<IReadOnlyList<HomeArtifactReference>> SearchAsync(HomeLibraryQuery query, CancellationToken cancellationToken) => Task.FromResult(items);
        public Task<HomeArtifactReference?> GetByIdAsync(string ownerApp, string artifactId, CancellationToken cancellationToken) =>
            Task.FromResult(items.FirstOrDefault(item => item.OwnerApp == ownerApp && item.ArtifactId == artifactId));
    }
    private sealed class FakeArtifactAuthorization : IHomeArtifactAuthorization
    {
        public HashSet<string> DeniedIds { get; set; } = [];
        public Task<bool> CanReadAsync(string ownerApp, string artifactId, CancellationToken cancellationToken) => Task.FromResult(!DeniedIds.Contains(artifactId));
        public Task<bool> CanOpenAsync(HomeDeepLink link, CancellationToken cancellationToken) => Task.FromResult(!DeniedIds.Contains(link.ObjectId));
    }
    private sealed class FakePreferences(HashSet<(string App, string Id)>? pinned = null) : IHomeLibraryPreferences
    {
        private readonly HashSet<(string App, string Id)> _pinned = pinned ?? [];
        public Task<bool> IsPinnedAsync(string ownerApp, string artifactId, CancellationToken cancellationToken) => Task.FromResult(_pinned.Contains((ownerApp, artifactId)));
        public Task SetPinnedAsync(string ownerApp, string artifactId, bool pinned, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class FakeRouter : IHomeDeepLinkRouter
    {
        public HomeFeatureNavigationRequest? Opened { get; private set; }
        public Task<HomeFeatureNavigationResult> OpenAsync(HomeDeepLink link, CancellationToken cancellationToken = default)
        {
            Opened = new HomeFeatureNavigationRequest(HomeRouteIds.For(link.Destination), link.OwnerApp, link.ObjectId, link.Action);
            return Task.FromResult(new HomeFeatureNavigationResult(true, "Succeeded", "Opened.", Opened));
        }
    }
    private sealed class FakeActivitySource(string id, IReadOnlyList<HomeActivityEvent> events) : IHomeActivitySource
    {
        public string SourceId => id;
        public Task<IReadOnlyList<HomeActivityEvent>> ReadAsync(HomeEventsQuery query, CancellationToken cancellationToken) => Task.FromResult(events);
        public Task<HomeActivityEvent?> GetByIdAsync(string eventId, CancellationToken cancellationToken) =>
            Task.FromResult(events.FirstOrDefault(item => item.EventId == eventId));
    }
    private sealed class AllowActivity : IHomeActivityAuthorization
    { public Task<bool> CanReadAsync(HomeActivityEvent activity, CancellationToken cancellationToken) => Task.FromResult(true); }
}
