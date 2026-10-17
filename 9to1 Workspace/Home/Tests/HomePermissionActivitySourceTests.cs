using Haven.Application;
using HavenOS.Home;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HomePermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomePermissionActivitySourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "home-activity-" + Guid.NewGuid().ToString("N"));
    private readonly ActorSource _actors = new();
    private readonly HomePermissionTrustService _permissions;

    public HomePermissionActivitySourceTests()
    {
        _permissions = new HomePermissionTrustService(new FileHomeCoreStateStore(Path.Combine(_root, "home.json")),
            (_, _) => new(HomePermissionRisk.Routine, true, false));
    }

    [Fact]
    public async Task Activity_projects_durable_request_events_with_exact_actor_and_canonical_deep_links()
    {
        var original = _actors.Current!;
        var admitted = await SubmitAsync(original, "one");
        await SubmitAsync(original with { ProfileId = "other-profile" }, "other");
        await SubmitAsync(null, "unbound");
        var source = new HomePermissionActivitySource(_permissions, _actors, original);
        var service = new HomeEventsService([source], source);
        var page = await service.ListAsync(new());
        var observed = Assert.Single(page.Items);
        Assert.Equal(admitted.RequestId, observed.RequestId);
        Assert.Equal("home.permissions", observed.SourceApp);
        Assert.Equal("RequestReceived", observed.EventType);
        Assert.Equal("Test caller", observed.ActorDisplayName);
        var audit = (await _permissions.GetSnapshotAsync()).RecentAuditEvents.Single(item => item.RequestId == admitted.RequestId);
        Assert.Equal(audit.AuditId, observed.EventId);
        Assert.Equal(audit.Timestamp, observed.Timestamp);
        Assert.Equal(HomeFeatureRouteIds.Permissions, Assert.Single(observed.Objects).TargetRouteId);

        await _permissions.DecideAsync(admitted.RequestId, HomeApprovalChoice.Decline);
        var refreshed = await service.ListAsync(new());
        Assert.Contains(refreshed.Items, item => item.EventType == "DecisionMade" && item.Severity == "warning");
        Assert.All(refreshed.Items, item => Assert.Equal(admitted.RequestId, item.RequestId));
    }

    [Fact]
    public async Task Profile_change_redacts_activity_and_prevents_opening_an_old_event()
    {
        var original = _actors.Current!;
        await SubmitAsync(original, "one");
        var source = new HomePermissionActivitySource(_permissions, _actors, original);
        var service = new HomeEventsService([source], source);
        var activity = Assert.Single((await service.ListAsync(new())).Items);
        _actors.Current = original with { AuthenticationRevision = "retired" };
        var page = await service.ListAsync(new());
        Assert.Equal("Unavailable", page.Code);
        Assert.Empty(page.Items);
        var router = new RejectUnexpectedNavigation();
        var open = await service.OpenObjectAsync(activity.EventId, 0, router);
        Assert.False(open.Succeeded);
        Assert.False(router.Invoked);
    }

    [Fact]
    public async Task Cancelled_activity_read_never_returns_a_successful_empty_history()
    {
        var source = new HomePermissionActivitySource(_permissions, _actors, _actors.Current!);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAsync(new(), cancelled.Token));
    }

    private Task<HomePermissionAuthorization> SubmitAsync(AuthenticatedResourceActor? actor, string id) =>
        _permissions.AuthorizeAsync(new(id, new("test.caller", "Test caller", "test", "v1", true), "session",
            new("test", "artifact.read", [new("artifact", id)]), new(["artifact"], 1, [new("artifact", id)], false,
                ResourceBinding: actor is null ? null : new(1, actor, [new("artifact", id, "1", ResourceAccess.Read)]))));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class ActorSource : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Current { get; set; } = new("actor", "profile", null, null, "1");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Current);
    }
    private sealed class RejectUnexpectedNavigation : IHomeDeepLinkRouter
    {
        public bool Invoked { get; private set; }
        public Task<HomeFeatureNavigationResult> OpenAsync(HomeDeepLink link, CancellationToken cancellationToken = default)
        {
            Invoked = true;
            throw new InvalidOperationException("A retired profile cannot navigate to an event.");
        }
    }
}
