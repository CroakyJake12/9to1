using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomePermissionTrustServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-home-permissions-" + Guid.NewGuid().ToString("N"));
    private readonly FileHomeCoreStateStore _stateStore;
    private readonly MutableTimeProvider _clock = new(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));

    public HomePermissionTrustServiceTests()
    {
        Directory.CreateDirectory(_root);
        _stateStore = new FileHomeCoreStateStore(Path.Combine(_root, "home-state.json"));
    }

    [Fact]
    public async Task Accept_is_session_scoped_and_exact_scope_is_required_after_restart()
    {
        var service = CreateService();
        var first = await service.AuthorizeAsync(Request("boards.read"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, first.State);
        Assert.Equal("HOME_PERMISSION_REQUIRED", first.Code);
        Assert.True((await service.DecideAsync(first.RequestId, HomeApprovalChoice.Accept)).Succeeded);

        var sameSession = await service.AuthorizeAsync(Request("boards.read"));
        Assert.Equal(HomePermissionRequestState.Approved, sameSession.State);
        Assert.Equal(HomeTrustLevel.Session, sameSession.TrustLevel);

        var otherSession = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-2"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, otherSession.State);

        var differentObject = await service.AuthorizeAsync(Request("boards.read", objectId: "board-99"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, differentObject.State);

        var restarted = CreateService();
        var afterRestart = await restarted.AuthorizeAsync(Request("boards.read"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, afterRestart.State);
    }

    [Fact]
    public async Task Ordinary_trust_lasts_thirty_days_but_does_not_cover_elevated_actions()
    {
        var service = CreateService();
        var request = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);

        var ordinaryAgain = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-2"));
        Assert.Equal(HomePermissionRequestState.Approved, ordinaryAgain.State);
        Assert.Equal(HomeTrustLevel.AcceptAndTrust, ordinaryAgain.TrustLevel);

        var elevated = await service.AuthorizeAsync(Request("boards.delete"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, elevated.State);
        var prohibited = await service.DecideAsync(elevated.RequestId, HomeApprovalChoice.AcceptAndTrust);
        Assert.False(prohibited.Succeeded);
        Assert.Equal("HOME_TRUST_SCOPE_REQUIRES_EXPLICIT_APPROVAL", prohibited.Code);

        _clock.Advance(TimeSpan.FromDays(30).Add(TimeSpan.FromSeconds(1)));
        var expired = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-3"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, expired.State);
        var snapshot = await service.GetSnapshotAsync();
        Assert.Contains(snapshot.RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.TrustExpired);
    }

    [Fact]
    public async Task Always_trust_requires_warning_and_trust_is_revoked_before_dispatch()
    {
        var service = CreateService();
        var request = await service.AuthorizeAsync(Request("boards.delete"));
        var skippedWarning = await service.DecideAsync(request.RequestId, HomeApprovalChoice.AcceptAndAlwaysTrust,
            userConfirmedAlwaysTrustWarning: true);
        Assert.False(skippedWarning.Succeeded);
        Assert.Equal("HOME_ALWAYS_TRUST_WARNING_REQUIRED", skippedWarning.Code);

        Assert.True((await service.MarkAlwaysTrustWarningShownAsync(request.RequestId)).Succeeded);
        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.AcceptAndAlwaysTrust,
            userConfirmedAlwaysTrustWarning: true)).Succeeded);
        var grant = Assert.Single((await service.GetSnapshotAsync()).Grants);

        var newRequest = await service.AuthorizeAsync(Request("boards.delete", sessionId: "session-2"));
        Assert.Equal(HomePermissionRequestState.Approved, newRequest.State);
        Assert.True((await service.RevokeGrantAsync(grant.GrantId)).Succeeded);

        var dispatch = await service.BeginExecutionAsync(newRequest.RequestId);
        Assert.Equal(HomePermissionRequestState.PendingApproval, dispatch.State);
        Assert.Equal("HOME_PERMISSION_REQUIRED", dispatch.Code);
    }

    [Fact]
    public async Task Temporary_action_limit_rolls_down_only_when_user_selected_it()
    {
        var service = CreateService();
        var request = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.MarkAlwaysTrustWarningShownAsync(request.RequestId)).Succeeded);
        var granted = await service.DecideAsync(request.RequestId, HomeApprovalChoice.GrantTemporaryTrustedAccess,
            userConfirmedAlwaysTrustWarning: true,
            temporaryOptions: new HomeTrustGrantOptions(null, 1, HomeTemporaryGrantFallback.AcceptAndTrust));
        Assert.True(granted.Succeeded);

        var firstUse = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-2"));
        Assert.Equal(HomePermissionRequestState.Approved, firstUse.State);
        Assert.Equal(HomeTrustLevel.TemporaryAlwaysTrust, firstUse.TrustLevel);
        var secondUse = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-3"));
        Assert.Equal(HomePermissionRequestState.Approved, secondUse.State);
        Assert.Equal(HomeTrustLevel.AcceptAndTrust, secondUse.TrustLevel);

        var snapshot = await service.GetSnapshotAsync();
        Assert.Contains(snapshot.Grants, grant => grant.TrustLevel == HomeTrustLevel.TemporaryAlwaysTrust && grant.IsRevoked);
        Assert.Contains(snapshot.Grants, grant => grant.TrustLevel == HomeTrustLevel.AcceptAndTrust && !grant.IsRevoked);
    }

    [Fact]
    public async Task Block_denies_caller_and_execution_attempts_are_separately_audited()
    {
        var service = CreateService();
        var request = await service.AuthorizeAsync(Request("boards.edit"));
        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var executing = await service.BeginExecutionAsync(request.RequestId);
        Assert.Equal(HomePermissionRequestState.Executing, executing.State);
        Assert.True((await service.RecordExecutionAsync(request.RequestId, new HomeExecutionOutcome(
            HomePermissionRequestState.PartiallyCompleted, "PARTIAL", "One item could not be updated.",
            [new HomeObjectReference("board", "board-42")]))).Succeeded);

        Assert.True((await service.BlockCallerAsync("script:sha256:test")).Succeeded);
        var blocked = await service.AuthorizeAsync(Request("boards.edit", sessionId: "session-2"));
        Assert.Equal(HomePermissionRequestState.Blocked, blocked.State);

        var snapshot = await service.GetSnapshotAsync();
        Assert.Contains(snapshot.BlockedCallerIds, caller => caller == "script:sha256:test");
        Assert.Contains(snapshot.RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.RequestReceived);
        Assert.Contains(snapshot.RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.DecisionMade);
        Assert.Contains(snapshot.RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.ExecutionStarted);
        Assert.Contains(snapshot.RecentAuditEvents, item => item.Kind == HomePermissionAuditKind.ExecutionCompleted &&
            item.RequestState == HomePermissionRequestState.PartiallyCompleted);
        Assert.True((await service.UnblockCallerAsync("script:sha256:test")).Succeeded);
    }

    [Fact]
    public async Task Unknown_action_policy_fails_closed_and_is_audited()
    {
        var service = CreateService(knownActions: []);

        var result = await service.AuthorizeAsync(Request("boards.unknown"));

        Assert.Equal(HomePermissionRequestState.Denied, result.State);
        Assert.Equal("HOME_ACTION_NOT_REGISTERED", result.Code);
        var snapshot = await service.GetSnapshotAsync();
        Assert.Contains(snapshot.RecentAuditEvents, item => item.ResultCode == "HOME_ACTION_NOT_REGISTERED");
    }

    private HomePermissionTrustService CreateService(string[]? knownActions = null)
    {
        var actions = (knownActions ?? ["boards.read", "boards.edit", "boards.delete"])
            .ToHashSet(StringComparer.Ordinal);
        return new HomePermissionTrustService(_stateStore, (target, action) =>
            target == "app.boards" && actions.Contains(action)
                ? new HomePermissionActionPolicy(
                    action == "boards.delete" ? HomePermissionRisk.High : HomePermissionRisk.Routine,
                    IsReversible: action != "boards.delete",
                    HasExternalSideEffects: false)
                : null, _clock);
    }

    private static HomePermissionRequestSubmission Request(
        string action,
        string sessionId = "session-1",
        string objectId = "board-42") => new(
        null,
        new HomePermissionCallerIdentity("script:sha256:test", "Board assistant", "local project", "hash-v1", false),
        sessionId,
        new HomePermissionScope("app.boards", action, [new HomeObjectReference("board", objectId)]),
        new HomePermissionImpactPreview(["board"], 1, [new HomeObjectReference("board", objectId)], false));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan amount) => Now += amount;
    }
}
