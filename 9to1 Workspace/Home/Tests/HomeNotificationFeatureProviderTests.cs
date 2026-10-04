using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeNotificationFeatureProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-home-notifications-" + Guid.NewGuid().ToString("N"));
    private readonly FileHomeCoreStateStore _stateStore;

    public HomeNotificationFeatureProviderTests()
    {
        Directory.CreateDirectory(_root);
        _stateStore = new FileHomeCoreStateStore(Path.Combine(_root, "home-state.json"));
    }

    [Fact]
    public async Task Publish_history_read_and_deep_link_use_persistent_typed_records()
    {
        var navigation = new HomeFeatureNavigationHost([new RouteHandler("app.boards")]);
        var provider = CreateProvider(navigation);

        var published = await provider.PublishAsync(Notification("activity-1"));

        Assert.True(published.Succeeded);
        Assert.Equal("Succeeded", published.Code);
        Assert.NotNull(published.Value);
        Assert.False(published.Value.IsRead);
        Assert.False(published.Value.IsDismissed);
        Assert.True(published.Value.CreatedAtUtc > DateTimeOffset.UnixEpoch);

        var reopened = CreateProvider(navigation);
        var history = await reopened.GetSnapshotAsync(new HomeNotificationQuery(SourceId: "app.boards"));
        Assert.True(history.Succeeded);
        Assert.Single(history.Value!.Items);
        Assert.Equal("activity-1", history.Value.Items[0].NotificationId);

        var resolved = await reopened.ResolveDeepLinkAsync(new HomeNotificationDeepLinkRequest("activity-1", "open-board"));
        Assert.True(resolved.Succeeded);
        Assert.Equal("app.boards", resolved.Value!.RouteId);
        Assert.Equal("board", resolved.Value.EntityType);
        Assert.Equal("board-42", resolved.Value.EntityId);

        var read = await reopened.MarkReadAsync("activity-1", history.Value.Items[0].Revision);
        Assert.True(read.Succeeded);
        Assert.True(read.Value!.IsRead);
        var dismissed = await reopened.DismissAsync("activity-1", read.Value.Revision);
        Assert.True(dismissed.Succeeded);
        Assert.True(dismissed.Value!.IsDismissed);
    }

    [Fact]
    public async Task Publication_fails_closed_when_source_is_not_authorized()
    {
        var provider = new HomeNotificationFeatureProvider(
            _stateStore,
            new HomeFeatureNavigationHost(),
            (_, _) => Task.FromResult(false));

        var result = await provider.PublishAsync(Notification("blocked"));

        Assert.False(result.Succeeded);
        Assert.Equal("PermissionDenied", result.Code);
        var history = await provider.GetSnapshotAsync(new HomeNotificationQuery());
        Assert.Empty(history.Value!.Items);
    }

    [Fact]
    public async Task Resolve_rejects_consequential_action_names_and_unregistered_routes()
    {
        var provider = CreateProvider(new HomeFeatureNavigationHost());
        var publish = await provider.PublishAsync(Notification("danger", actionName: "delete"));
        Assert.True(publish.Succeeded);

        var result = await provider.ResolveDeepLinkAsync(new HomeNotificationDeepLinkRequest("danger", "open-board"));

        Assert.False(result.Succeeded);
        Assert.Equal("NotificationActionUnsupported", result.Code);
    }

    [Fact]
    public async Task Clear_dismisses_filtered_items_and_retains_history()
    {
        var provider = CreateProvider(new HomeFeatureNavigationHost([new RouteHandler("app.boards")]));
        Assert.True((await provider.PublishAsync(Notification("one"))).Succeeded);
        Assert.True((await provider.PublishAsync(Notification("two", category: "updates"))).Succeeded);
        var snapshot = await provider.GetSnapshotAsync(new HomeNotificationQuery(IsDismissed: false));

        var cleared = await provider.ClearAsync(new HomeNotificationQuery(Category: "activity"), snapshot.Value!.Revision);

        Assert.True(cleared.Succeeded);
        Assert.Equal(1, cleared.Value);
        var all = await provider.GetSnapshotAsync(new HomeNotificationQuery());
        Assert.Equal(2, all.Value!.Items.Count);
        Assert.Single(all.Value.Items, item => item.IsDismissed);
        Assert.Single(all.Value.Items, item => !item.IsDismissed);
    }

    private HomeNotificationFeatureProvider CreateProvider(IHomeFeatureNavigationHost navigation) =>
        new(_stateStore, navigation, (source, _) => Task.FromResult(source == "app.boards"));

    private static HomeNotificationRecord Notification(string id, string category = "activity", string actionName = "open") =>
        new(id, "app.boards", category, "Info", "Board updated", "A board changed.", DateTimeOffset.UnixEpoch,
            false, false, "board", "board-42",
            [new HomeNotificationAction("open-board", "app.boards", actionName,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["entityType"] = "board", ["entityId"] = "board-42" })], 0);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class RouteHandler(string routeId) : IHomeFeatureRouteHandler
    {
        public string RouteId { get; } = routeId;

        public Task<HomeFeatureNavigationResult> OpenAsync(
            HomeFeatureNavigationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HomeFeatureNavigationResult(true, "Succeeded", "Opened.", request));
        }
    }
}
