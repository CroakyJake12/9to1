using System.Text.Json.Nodes;
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
    public async Task Revoked_grant_activity_joins_exact_grant_and_request_and_stays_profile_scoped()
    {
        var original = _actors.Current!;
        var ownerRequest = await SubmitAsync(original, "owner-grant-origin");
        Assert.True((await _permissions.DecideAsync(ownerRequest.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var ownerGrant = Assert.Single((await _permissions.GetSnapshotAsync()).Grants);

        var otherProfile = original with { ProfileId = "other-profile" };
        var otherRequest = await SubmitAsync(otherProfile, "other-profile-grant-origin");
        Assert.True((await _permissions.DecideAsync(otherRequest.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var grants = (await _permissions.GetSnapshotAsync()).Grants;
        var otherGrant = Assert.Single(grants.Where(item => item.GrantId != ownerGrant.GrantId));

        Assert.True((await _permissions.RevokeGrantAsync(ownerGrant.GrantId)).Succeeded);
        Assert.True((await _permissions.RevokeGrantAsync(otherGrant.GrantId)).Succeeded);

        var source = new HomePermissionActivitySource(_permissions, _actors, original);
        var service = new HomeEventsService([source], source);
        var events = (await service.ListAsync(new())).Items;
        var revoked = Assert.Single(events.Where(item => item.EventType == "TrustRevoked"));
        Assert.Equal(ownerRequest.RequestId, revoked.RequestId);
        Assert.Equal("test.caller", revoked.ActorIdentity);
        Assert.Equal("Test caller", revoked.ActorDisplayName);
        Assert.DoesNotContain(events, item => item.RequestId == otherRequest.RequestId);

        var audit = (await _permissions.GetSnapshotAsync()).RecentAuditEvents.Single(item =>
            item.AuditId == revoked.EventId);
        Assert.Null(audit.RequestId);
        Assert.Equal(ownerRequest.RequestId, audit.RelatedRequestId);
        Assert.Equal(ownerGrant.GrantId, audit.RelatedGrantId);
        Assert.Equal(audit.Timestamp, revoked.Timestamp);
        Assert.True(await source.CanReadAsync(revoked, CancellationToken.None));
        Assert.False(await source.CanReadAsync(revoked with { RequestId = otherRequest.RequestId }, CancellationToken.None));

        // Older payloads have no typed grant origin; do not infer it from caller names or timestamps.
        var legacy = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "home.json")))!;
        var payload = legacy["Records"]!.AsArray()
            .Single(item => item!["RecordId"]!.GetValue<string>() == "home.permissions-trust")!["Payload"]!;
        var persistedOwnerGrant = payload["Grants"]!.AsArray().Single(item =>
            item!["GrantId"]!.GetValue<string>() == ownerGrant.GrantId)!.AsObject();
        Assert.True(persistedOwnerGrant.Remove("OriginatingRequestId"));
        var persistedRevocation = payload["Audit"]!.AsArray().Single(item =>
            item!["AuditId"]!.GetValue<string>() == revoked.EventId)!.AsObject();
        Assert.True(persistedRevocation.Remove("RelatedRequestId"));
        Assert.True(persistedRevocation.Remove("RelatedGrantId"));
        await File.WriteAllTextAsync(Path.Combine(_root, "home.json"), legacy.ToJsonString());
        Assert.DoesNotContain(await source.ReadAsync(new(), CancellationToken.None),
            item => item.EventType == "TrustRevoked");
    }

    [Fact]
    public async Task Revoked_narrowed_grant_activity_keeps_origin_and_rejects_widened_scope_provenance()
    {
        var original = _actors.Current!;
        var ownerRequest = await SubmitAsync(original, "narrow-owner", "narrow-extra");
        Assert.True((await _permissions.DecideAsync(ownerRequest.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var ownerGrant = Assert.Single((await _permissions.GetSnapshotAsync()).Grants);
        var narrowedScope = new HomePermissionScope("test", "artifact.read",
            [new HomeObjectReference("artifact", "narrow-owner")]);
        Assert.True((await _permissions.NarrowGrantAsync(ownerGrant.GrantId, narrowedScope)).Succeeded);

        var otherProfile = original with { ProfileId = "other-profile" };
        var otherRequest = await SubmitAsync(otherProfile, "narrow-other-profile");
        Assert.True((await _permissions.DecideAsync(otherRequest.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var otherGrant = (await _permissions.GetSnapshotAsync()).Grants.Single(item => item.GrantId != ownerGrant.GrantId);

        Assert.True((await _permissions.RevokeGrantAsync(ownerGrant.GrantId)).Succeeded);
        Assert.True((await _permissions.RevokeGrantAsync(otherGrant.GrantId)).Succeeded);
        var source = new HomePermissionActivitySource(_permissions, _actors, original);
        var eventsService = new HomeEventsService([source], source);
        var events = (await eventsService.ListAsync(new())).Items;
        var revoked = Assert.Single(events.Where(item => item.EventType == "TrustRevoked"));
        Assert.Equal(ownerRequest.RequestId, revoked.RequestId);
        Assert.True(await source.CanReadAsync(revoked, CancellationToken.None));
        var router = new RecordingRouter();
        Assert.True((await eventsService.OpenObjectAsync(revoked.EventId, 0, router)).Succeeded);
        Assert.Equal(ownerRequest.RequestId, router.Opened?.EntityId);
        Assert.DoesNotContain(events, item => item.RequestId == otherRequest.RequestId);

        // Even mutually changed grant/audit scope claims cannot attach a widened grant to a narrower origin.
        var legacy = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "home.json")))!;
        var payload = legacy["Records"]!.AsArray()
            .Single(item => item!["RecordId"]!.GetValue<string>() == "home.permissions-trust")!["Payload"]!;
        var persistedOwnerGrant = payload["Grants"]!.AsArray().Single(item =>
            item!["GrantId"]!.GetValue<string>() == ownerGrant.GrantId)!;
        var persistedRevocation = payload["Audit"]!.AsArray().Single(item =>
            item!["AuditId"]!.GetValue<string>() == revoked.EventId)!;
        foreach (var scope in new[] { persistedOwnerGrant["Scope"]!, persistedRevocation["Scope"]! })
        {
            scope["IncludesAllObjects"] = true;
            scope["Objects"] = new JsonArray();
        }
        await File.WriteAllTextAsync(Path.Combine(_root, "home.json"), legacy.ToJsonString());
        Assert.False(await source.CanReadAsync(revoked, CancellationToken.None));
        Assert.DoesNotContain(await source.ReadAsync(new(), CancellationToken.None),
            item => item.EventType == "TrustRevoked");
        Assert.False((await eventsService.OpenObjectAsync(revoked.EventId, 0, router)).Succeeded);
    }

    [Fact]
    public async Task Cancelled_activity_read_never_returns_a_successful_empty_history()
    {
        var source = new HomePermissionActivitySource(_permissions, _actors, _actors.Current!);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ReadAsync(new(), cancelled.Token));
    }

    private Task<HomePermissionAuthorization> SubmitAsync(AuthenticatedResourceActor? actor, string id,
        string? secondObjectId = null)
    {
        string[] objectIds = secondObjectId is null ? [id] : [id, secondObjectId];
        var objects = objectIds.Select(objectId => new HomeObjectReference("artifact", objectId)).ToArray();
        var resourceScopes = objectIds.Select(objectId =>
            new Haven.Application.ResourceScope("artifact", objectId, "1", ResourceAccess.Read)).ToArray();
        return _permissions.AuthorizeAsync(new(id, new("test.caller", "Test caller", "test", "v1", true), "session",
            new("test", "artifact.read", objects), new(["artifact"], objects.Length, objects, false,
                ResourceBinding: actor is null ? null : new(1, actor, resourceScopes))));
    }

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
    private sealed class RecordingRouter : IHomeDeepLinkRouter
    {
        public HomeFeatureNavigationRequest? Opened { get; private set; }
        public Task<HomeFeatureNavigationResult> OpenAsync(HomeDeepLink link, CancellationToken cancellationToken = default)
        {
            Opened = new HomeFeatureNavigationRequest(HomeFeatureRouteIds.Permissions, link.OwnerApp, link.ObjectId, link.Action);
            return Task.FromResult(new HomeFeatureNavigationResult(true, "Succeeded", "Opened.", Opened));
        }
    }
}
