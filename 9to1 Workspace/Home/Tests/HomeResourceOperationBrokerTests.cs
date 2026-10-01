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

    [Fact]
    public async Task Owner_capability_is_bound_to_issuer_exact_operation_and_claimed_once()
    {
        var pending = await Request();
        Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = Assert.IsType<HomeResourceExecutionCapability>(await _broker.BeginExecutionCapabilityAsync(pending.RequestId, Arguments));
        var otherBroker = new HomeResourceOperationBroker(new ResourceAuthorizationService(_actors, [_owner]), _permissions);
        Assert.Null(await otherBroker.ClaimExecutionAsync(capability, "planner", "planner.attachJourney", Scopes, Arguments));
        Assert.Null(await _broker.ClaimExecutionAsync(capability, "other-app", "planner.attachJourney", Scopes, Arguments));
        Assert.Null(await _broker.ClaimExecutionAsync(capability, "planner", "other-action", Scopes, Arguments));
        Assert.Null(await _broker.ClaimExecutionAsync(capability, "planner", "planner.attachJourney", [], Arguments));
        Assert.Null(await _broker.ClaimExecutionAsync(capability, "planner", "planner.attachJourney", Scopes,
            JsonSerializer.SerializeToElement(new { journeyId = "other" })));
        Assert.Equal(_actors.Current, await _broker.ClaimExecutionAsync(capability, "planner", "planner.attachJourney", Scopes, Arguments));
        Assert.Null(await _broker.ClaimExecutionAsync(capability, "planner", "planner.attachJourney", Scopes, Arguments));
        Assert.Null(await _broker.BeginExecutionCapabilityAsync(pending.RequestId, Arguments));
    }

    [Fact]
    public async Task Owner_claim_rechecks_current_actor_acl_and_revision_after_approval_consumption()
    {
        foreach (var change in new[] { "actor", "acl", "revision", "blocked" })
        {
            _actors.Current = ActorSource.Initial; _owner.Allowed = true; _owner.Revision = "revision-1";
            await _permissions.UnblockCallerAsync(ActorSource.Initial.ActorId);
            var pending = await Request();
            Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            var capability = Assert.IsType<HomeResourceExecutionCapability>(await _broker.BeginExecutionCapabilityAsync(pending.RequestId, Arguments));
            if (change == "actor") _actors.Current = ActorSource.Initial with { AuthenticationRevision = "switched" };
            if (change == "acl") _owner.Allowed = false;
            if (change == "revision") _owner.Revision = "revision-2";
            if (change == "blocked") await _permissions.BlockCallerAsync(ActorSource.Initial.ActorId);
            Assert.Null(await _broker.ClaimExecutionAsync(capability, "planner", "planner.attachJourney", Scopes, Arguments));
        }
    }

    [Fact]
    public async Task Completion_requires_original_issuer_successful_claim_and_reports_actual_outcome_once()
    {
        var pending = await Request();
        Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = (await _broker.BeginExecutionCapabilityAsync(pending.RequestId, Arguments))!;
        var outcome = new HomeExecutionOutcome(HomePermissionRequestState.Succeeded, "OWNER_COMMITTED", "The owner committed revision2.", [new("planner-event", "event-1")]);
        Assert.False((await _broker.CompleteExecutionAsync(capability, outcome)).Succeeded);
        Assert.NotNull(await _broker.ClaimExecutionAsync(capability, "planner", "planner.attachJourney", Scopes, Arguments));
        var foreign = new HomeResourceOperationBroker(new ResourceAuthorizationService(_actors, [_owner]), _permissions);
        Assert.False((await foreign.CompleteExecutionAsync(capability, outcome)).Succeeded);
        // The actual committed outcome belongs to the original request even after the UI session changes.
        _actors.Current = ActorSource.Initial with { AuthenticationRevision = "later-session" };
        Assert.True((await _broker.CompleteExecutionAsync(capability, outcome)).Succeeded);
        Assert.True((await _broker.CompleteExecutionAsync(capability, outcome)).Succeeded); // same receipt, no repeated audit/effect
        Assert.False((await _broker.CompleteExecutionAsync(capability, outcome with { Code = "DIFFERENT" })).Succeeded);
        Assert.True((await _permissions.RecordExecutionAsync(pending.RequestId, outcome)).Succeeded); // durable idempotency after a lost acknowledgement
        Assert.False((await _permissions.RecordExecutionAsync(pending.RequestId, outcome with { AffectedObjects = [] })).Succeeded);
        Assert.Equal(HomePermissionRequestState.Succeeded, (await _permissions.GetAuthorizationAsync(pending.RequestId)).State);
        Assert.Single((await _permissions.GetSnapshotAsync()).RecentAuditEvents,
            item => item.RequestId == pending.RequestId && item.Kind == HomePermissionAuditKind.ExecutionCompleted);
    }

    [Fact]
    public async Task Rejected_owner_claim_cannot_manufacture_completion_and_records_no_execution_success()
    {
        var pending = await Request();
        Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = (await _broker.BeginExecutionCapabilityAsync(pending.RequestId, Arguments))!;
        _owner.Allowed = false;
        Assert.Null(await _broker.ClaimExecutionAsync(capability, "planner", "planner.attachJourney", Scopes, Arguments));
        Assert.False((await _broker.CompleteExecutionAsync(capability,
            new(HomePermissionRequestState.Succeeded, "FORGED", "No owner committed.", []))).Succeeded);
        Assert.Equal(HomePermissionRequestState.Failed, (await _permissions.GetAuthorizationAsync(pending.RequestId)).State);
        Assert.Equal(HomePermissionRequestState.Failed, (await _broker.GetExecutionDecisionAsync(capability))!.State);
    }

    [Theory]
    [InlineData(HomePermissionRequestState.Failed)]
    [InlineData(HomePermissionRequestState.PartiallyCompleted)]
    [InlineData(HomePermissionRequestState.Cancelled)]
    public async Task Owner_terminal_failures_are_not_relabelled_as_success(HomePermissionRequestState state)
    {
        var pending = await Request();
        Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = (await _broker.BeginExecutionCapabilityAsync(pending.RequestId, Arguments))!;
        Assert.NotNull(await _broker.ClaimExecutionAsync(capability, "planner", "planner.attachJourney", Scopes, Arguments));
        Assert.False((await _broker.CompleteExecutionAsync(capability,
            new(HomePermissionRequestState.Executing, "INVALID", "Not terminal.", []))).Succeeded);
        Assert.True((await _broker.CompleteExecutionAsync(capability, new(state, "OWNER_RESULT", "Observed owner outcome.", []))).Succeeded);
        Assert.Equal(state, (await _permissions.GetAuthorizationAsync(pending.RequestId)).State);
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
