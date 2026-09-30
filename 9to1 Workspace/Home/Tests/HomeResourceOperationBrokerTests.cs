using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeResourceOperationBrokerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-resource-" + Guid.NewGuid().ToString("N"));
    private readonly ActorSource _actors = new();
    private readonly OwnerResolver _owner = new();
    private readonly HomePermissionTrustService _permissions;
    private readonly HomeResourceOperationBroker _broker;
    private static readonly ResourceScope[] Scopes = [new("planner-event", "event-1", "revision-1", ResourceAccess.Write)];
    private static readonly JsonElement Arguments = JsonSerializer.SerializeToElement(new { journeyId = "journey-1" });

    public HomeResourceOperationBrokerTests()
    {
        Directory.CreateDirectory(_root);
        _permissions = new(new FileHomeCoreStateStore(Path.Combine(_root, "home.json")),
            (app, action) => app == "planner" && action == "planner.attachJourney"
                ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true) : null);
        _broker = new(new ResourceAuthorizationService(_actors, [_owner]), _permissions);
    }

    private Task<HomePermissionAuthorization> Request() => _broker.AuthorizeAsync("planner", "planner.attachJourney", Scopes,
        Arguments, "Attach the selected Journey to appointment event-1", null, "authenticated-session");

    [Fact]
    public async Task Canonical_resource_access_and_explicit_approval_are_both_required_and_one_use()
    {
        var pending = await Request();
        Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
        Assert.False(await _broker.BeginExecutionAsync(pending.RequestId, Arguments));
        Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.True(await _broker.BeginExecutionAsync(pending.RequestId, Arguments));
        Assert.False(await _broker.BeginExecutionAsync(pending.RequestId, Arguments));
    }

    [Fact]
    public async Task Approval_cannot_preserve_changed_actor_revision_acl_or_arguments()
    {
        foreach (var change in new[] { "actor", "revision", "acl", "arguments", "organisation" })
        {
            _actors.Current = ActorSource.Initial;
            _owner.Allowed = true; _owner.Revision = "revision-1"; _owner.Organisation = null;
            var pending = await Request();
            Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            var arguments = Arguments;
            switch (change)
            {
                case "actor": _actors.Current = ActorSource.Initial with { AuthenticationRevision = "new-session" }; break;
                case "revision": _owner.Revision = "revision-2"; break;
                case "acl": _owner.Allowed = false; break;
                case "arguments": arguments = JsonSerializer.SerializeToElement(new { journeyId = "other-journey" }); break;
                case "organisation": _owner.Organisation = Guid.NewGuid(); break;
            }
            Assert.False(await _broker.BeginExecutionAsync(pending.RequestId, arguments));
        }
    }

    [Fact]
    public async Task Missing_actor_unknown_resource_and_duplicate_authorities_fail_closed()
    {
        _actors.Current = null;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(Request);
        _actors.Current = ActorSource.Initial;
        Assert.Null(await new ResourceAuthorizationService(_actors, []).AuthorizeAsync("planner.attachJourney", Scopes));
        Assert.Null(await new ResourceAuthorizationService(_actors, [_owner, _owner]).AuthorizeAsync("planner.attachJourney", Scopes));
        Assert.Null(await new ResourceAuthorizationService(_actors, [_owner]).AuthorizeAsync("planner.attachJourney", []));
    }

    private sealed class ActorSource : IAuthenticatedResourceActorSource
    {
        public static AuthenticatedResourceActor Initial { get; } = new("verified-user", "profile-1", Guid.NewGuid(), null, "session-1");
        public AuthenticatedResourceActor? Current { get; set; } = Initial;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Current);
    }
    private sealed class OwnerResolver : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "planner-event";
        public bool Allowed { get; set; } = true;
        public string Revision { get; set; } = "revision-1";
        public Guid? Organisation { get; set; }
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope,
            CancellationToken cancellationToken) => ValueTask.FromResult(new ResourceAccessDecision(
                Allowed && scope.Id == "event-1" && actionId == "planner.attachJourney", "owner-policy", actor.ActorId, Revision, Organisation));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
