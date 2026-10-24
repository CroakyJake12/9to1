using System.Text.Json.Nodes;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using HomePermissionRisk = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk;
using HomeTrustLevel = HavenOS.Home.PermissionsTrustNotifications.HomeTrustLevel;
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
    public async Task Strict_per_action_policy_refuses_reusable_trust_but_accepts_one_current_action()
    {
        var strictPolicy = new HomePermissionActionPolicy(HomePermissionRisk.High, false, true,
            RequiresPerActionApproval: true);
        var service = new HomePermissionTrustService(_stateStore,
            (target, action) => target == "app.boards" && action == "boards.delete" ? strictPolicy : null, _clock);
        var request = await service.AuthorizeAsync(Request("boards.delete") with { RequestId = "strict-per-action" });
        Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
        Assert.True((await service.MarkAlwaysTrustWarningShownAsync(request.RequestId)).Succeeded);
        var baselineAudit = (await service.GetSnapshotAsync()).RecentAuditEvents.Select(item => item.AuditId).ToArray();

        var always = await service.DecideAsync(request.RequestId, HomeApprovalChoice.AcceptAndAlwaysTrust,
            userConfirmedAlwaysTrustWarning: true);
        Assert.False(always.Succeeded);
        Assert.Equal("HOME_TRUST_SCOPE_REQUIRES_EXPLICIT_APPROVAL", always.Code);
        var temporary = await service.DecideAsync(request.RequestId, HomeApprovalChoice.GrantTemporaryTrustedAccess,
            userConfirmedAlwaysTrustWarning: true,
            temporaryOptions: new HomeTrustGrantOptions(null, 1, HomeTemporaryGrantFallback.ManualApproval));
        Assert.False(temporary.Succeeded);
        Assert.Equal("HOME_TRUST_SCOPE_REQUIRES_EXPLICIT_APPROVAL", temporary.Code);
        var afterRefusals = await service.GetSnapshotAsync();
        Assert.Single(afterRefusals.PendingRequests);
        Assert.Empty(afterRefusals.Grants);
        Assert.Equal(baselineAudit, afterRefusals.RecentAuditEvents.Select(item => item.AuditId));
        Assert.Equal(HomePermissionRequestState.PendingApproval,
            (await service.ReadRequestObservationAsync(request.RequestId))!.State);

        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(request.RequestId)).State);
        var repeatedDispatch = await service.BeginExecutionAsync(request.RequestId);
        Assert.Equal(HomePermissionRequestState.Executing, repeatedDispatch.State);
        Assert.Equal("HOME_PERMISSION_NOT_AUTHORIZED", repeatedDispatch.Code);
        Assert.False(repeatedDispatch.IsAllowed);
        Assert.Single((await service.GetSnapshotAsync()).RecentAuditEvents.Where(item =>
            item.RequestId == request.RequestId && item.Kind == HomePermissionAuditKind.ExecutionStarted));

        var later = await service.AuthorizeAsync(Request("boards.delete", sessionId: "session-2"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, later.State);
    }

    [Fact]
    public async Task Strict_one_shot_approval_is_isolated_from_pending_sibling_and_block_revokes_both()
    {
        var strictPolicy = new HomePermissionActionPolicy(HomePermissionRisk.High, false, true,
            RequiresPerActionApproval: true);
        var service = new HomePermissionTrustService(_stateStore,
            (target, action) => target == "app.boards" && action == "boards.delete" ? strictPolicy : null, _clock);

        var first = await service.AuthorizeAsync(Request("boards.delete") with { RequestId = "strict-one-shot-first" });
        Assert.True((await service.DecideAsync(first.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Executing, (await service.BeginExecutionAsync(first.RequestId)).State);
        Assert.True(await service.IsExecutionCurrentAsync(first.RequestId));

        var sibling = await service.AuthorizeAsync(Request("boards.delete") with
            { RequestId = "strict-one-shot-pending-sibling" });
        Assert.Equal(HomePermissionRequestState.PendingApproval, sibling.State);
        Assert.Equal("HOME_PERMISSION_REQUIRED", sibling.Code);
        Assert.Null(sibling.TrustLevel);
        var siblingBeforeApproval = await service.BeginExecutionAsync(sibling.RequestId);
        Assert.Equal(HomePermissionRequestState.PendingApproval, siblingBeforeApproval.State);
        Assert.False(siblingBeforeApproval.IsAllowed);
        Assert.True(await service.IsExecutionCurrentAsync(first.RequestId));

        Assert.True((await service.DecideAsync(sibling.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(sibling.RequestId)).State);
        Assert.True(await service.IsExecutionCurrentAsync(first.RequestId));
        Assert.True(await service.IsExecutionCurrentAsync(sibling.RequestId));

        var restarted = new HomePermissionTrustService(new FileHomeCoreStateStore(Path.Combine(_root, "home-state.json")),
            (target, action) => target == "app.boards" && action == "boards.delete" ? strictPolicy : null, _clock);
        Assert.False(await restarted.IsExecutionCurrentAsync(first.RequestId));
        var restartedConsumed = await restarted.BeginExecutionAsync(first.RequestId);
        Assert.Equal(HomePermissionRequestState.Executing, restartedConsumed.State);
        Assert.Equal("HOME_PERMISSION_NOT_AUTHORIZED", restartedConsumed.Code);
        Assert.False(restartedConsumed.IsAllowed);

        Assert.True((await service.BlockCallerAsync("script:sha256:test")).Succeeded);
        Assert.False(await service.IsExecutionCurrentAsync(first.RequestId));
        Assert.False(await service.IsExecutionCurrentAsync(sibling.RequestId));
        Assert.True((await service.UnblockCallerAsync("script:sha256:test")).Succeeded);
        Assert.False(await service.IsExecutionCurrentAsync(first.RequestId));
        Assert.False(await service.IsExecutionCurrentAsync(sibling.RequestId));
    }

    [Fact]
    public async Task Dispatch_revalidates_current_trusted_action_policy_and_fails_closed()
    {
        HomePermissionActionPolicy? currentPolicy = new(HomePermissionRisk.Routine, true, false);
        var throwPolicyResolution = false;
        var service = new HomePermissionTrustService(_stateStore,
            (target, action) => throwPolicyResolution
                ? throw new IOException("trusted policy catalogue unavailable")
                : target == "app.boards" && action == "boards.read" ? currentPolicy : null, _clock);
        var request = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);

        currentPolicy = new HomePermissionActionPolicy(HomePermissionRisk.High, false, true, RequiresPerActionApproval: true);
        var staleApproval = await service.BeginExecutionAsync(request.RequestId);
        Assert.Equal(HomePermissionRequestState.PendingApproval, staleApproval.State);
        Assert.Equal("HOME_ACTION_POLICY_CHANGED", staleApproval.Code);
        Assert.Equal(HomePermissionRisk.High, (await service.ReadRequestObservationAsync(request.RequestId))!.Policy.Risk);
        var changedPolicyAudit = (await service.GetSnapshotAsync()).RecentAuditEvents;
        Assert.Contains(changedPolicyAudit, item => item.RequestId == request.RequestId && item.ResultCode == "HOME_ACTION_POLICY_CHANGED");
        Assert.DoesNotContain(changedPolicyAudit, item => item.RequestId == request.RequestId && item.Kind == HomePermissionAuditKind.ExecutionStarted);

        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var dispatch = await service.BeginExecutionAsync(request.RequestId);
        Assert.Equal(HomePermissionRequestState.Executing, dispatch.State);
        currentPolicy = new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false);
        Assert.False(await service.IsExecutionCurrentAsync(request.RequestId));

        currentPolicy = new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false);
        var unavailableRequest = await service.AuthorizeAsync(Request("boards.read") with { RequestId = "policy-unavailable" });
        Assert.True((await service.DecideAsync(unavailableRequest.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        currentPolicy = null;
        var unavailable = await service.BeginExecutionAsync(unavailableRequest.RequestId);
        Assert.Equal(HomePermissionRequestState.Denied, unavailable.State);
        Assert.Equal("HOME_ACTION_POLICY_UNAVAILABLE", unavailable.Code);
        Assert.False(await service.IsExecutionCurrentAsync(unavailableRequest.RequestId));
        Assert.Contains((await service.GetSnapshotAsync()).RecentAuditEvents,
            item => item.RequestId == unavailableRequest.RequestId && item.ResultCode == "HOME_ACTION_POLICY_UNAVAILABLE");

        currentPolicy = new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false);
        var failedResolutionRequest = await service.AuthorizeAsync(Request("boards.read") with { RequestId = "policy-resolution-fault" });
        Assert.True((await service.DecideAsync(failedResolutionRequest.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        throwPolicyResolution = true;
        var failedResolution = await service.BeginExecutionAsync(failedResolutionRequest.RequestId);
        Assert.Equal(HomePermissionRequestState.Denied, failedResolution.State);
        Assert.Equal("HOME_ACTION_POLICY_UNAVAILABLE", failedResolution.Code);
        Assert.Contains((await service.GetSnapshotAsync()).RecentAuditEvents,
            item => item.RequestId == failedResolutionRequest.RequestId && item.ResultCode == "HOME_ACTION_POLICY_UNAVAILABLE");
    }

    [Theory]
    [InlineData(HomePermissionRisk.Elevated, true, false, false)]
    [InlineData(HomePermissionRisk.Routine, false, false, false)]
    [InlineData(HomePermissionRisk.Routine, true, true, false)]
    [InlineData(HomePermissionRisk.Routine, true, false, true)]
    public async Task Dispatch_requires_fresh_approval_when_any_trusted_policy_field_changes(
        HomePermissionRisk changedRisk, bool changedReversibility, bool changedExternalEffects, bool changedPerActionApproval)
    {
        var currentPolicy = new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false);
        var service = new HomePermissionTrustService(_stateStore,
            (target, action) => target == "app.boards" && action == "boards.read" ? currentPolicy : null, _clock);
        var request = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);

        currentPolicy = new HomePermissionActionPolicy(changedRisk, changedReversibility,
            changedExternalEffects, changedPerActionApproval);
        var result = await service.BeginExecutionAsync(request.RequestId);

        Assert.Equal(HomePermissionRequestState.PendingApproval, result.State);
        Assert.Equal("HOME_ACTION_POLICY_CHANGED", result.Code);
        var observed = await service.ReadRequestObservationAsync(request.RequestId);
        Assert.Equal(currentPolicy, observed!.Policy);
        Assert.Null(observed.AppliedGrantId);
        Assert.Null(observed.AppliedTrustLevel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Policy_change_or_loss_does_not_resurrect_old_session_approval(bool policyUnavailable)
    {
        HomePermissionActionPolicy? currentPolicy = new(HomePermissionRisk.Routine, true, false);
        var service = new HomePermissionTrustService(_stateStore,
            (target, action) => target == "app.boards" && action == "boards.read" ? currentPolicy : null, _clock);
        var approved = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(approved.RequestId, HomeApprovalChoice.Accept)).Succeeded);

        currentPolicy = policyUnavailable
            ? null
            : new HomePermissionActionPolicy(HomePermissionRisk.Elevated, true, false);
        var stale = await service.BeginExecutionAsync(approved.RequestId);
        Assert.Equal(policyUnavailable ? HomePermissionRequestState.Denied : HomePermissionRequestState.PendingApproval,
            stale.State);

        currentPolicy = new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false);
        var later = await service.AuthorizeAsync(Request("boards.read"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, later.State);
        Assert.Equal("HOME_PERMISSION_REQUIRED", later.Code);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Policy_change_or_failure_detected_by_execution_fence_invalidates_session_approval(
        bool policyUnavailable, bool resolverThrows)
    {
        HomePermissionActionPolicy? currentPolicy = new(HomePermissionRisk.Routine, true, false);
        var throwPolicyResolution = false;
        var service = new HomePermissionTrustService(_stateStore,
            (target, action) => throwPolicyResolution
                ? throw new IOException("trusted policy catalogue unavailable")
                : target == "app.boards" && action == "boards.read" ? currentPolicy : null, _clock);
        var approved = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(approved.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(approved.RequestId)).State);

        if (policyUnavailable) currentPolicy = null;
        else if (resolverThrows) throwPolicyResolution = true;
        else currentPolicy = new HomePermissionActionPolicy(HomePermissionRisk.Elevated, true, false);
        Assert.False(await service.IsExecutionCurrentAsync(approved.RequestId));
        throwPolicyResolution = false;
        currentPolicy = new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false);
        Assert.False(await service.IsExecutionCurrentAsync(approved.RequestId));

        var later = await service.AuthorizeAsync(Request("boards.read"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, later.State);
    }

    [Fact]
    public async Task Stale_trust_grant_is_not_carried_forward_after_action_policy_change()
    {
        HomePermissionActionPolicy? currentPolicy = new(HomePermissionRisk.Routine, true, false);
        var service = new HomePermissionTrustService(_stateStore,
            (target, action) => target == "app.boards" && action == "boards.read" ? currentPolicy : null, _clock);
        var consent = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(consent.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);

        var trustedRequest = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-2"));
        Assert.Equal(HomePermissionRequestState.Approved, trustedRequest.State);
        Assert.NotNull(trustedRequest.AppliedGrantId);
        currentPolicy = new HomePermissionActionPolicy(HomePermissionRisk.Elevated, true, false);

        var result = await service.BeginExecutionAsync(trustedRequest.RequestId);

        Assert.Equal(HomePermissionRequestState.PendingApproval, result.State);
        Assert.Equal("HOME_ACTION_POLICY_CHANGED", result.Code);
        var observed = await service.ReadRequestObservationAsync(trustedRequest.RequestId);
        Assert.Null(observed!.AppliedGrantId);
        Assert.Null(observed.AppliedTrustLevel);
        Assert.Contains((await service.GetSnapshotAsync()).Grants, grant => !grant.IsRevoked);
    }

    [Fact]
    public async Task Thirty_day_grant_requires_explicit_reapproval_after_policy_risk_expansion()
    {
        HomePermissionActionPolicy currentPolicy = new(HomePermissionRisk.Routine, true, false);
        var service = new HomePermissionTrustService(_stateStore,
            (target, action) => target == "app.boards" && action == "boards.read" ? currentPolicy : null, _clock);
        var consent = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(consent.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var grant = Assert.Single((await service.GetSnapshotAsync()).Grants);
        Assert.Equal(HomePermissionRisk.Routine, grant.MaximumRisk);

        var previousPreview = new HomePermissionImpactPreview(["board"], 1,
            [new HomeObjectReference("board", "board-42")], false, ChangePreview: "low-risk preview");
        var coveredBeforeChange = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-2") with
        {
            RequestId = "routine-trust-before-risk-change",
            Impact = previousPreview,
        });
        Assert.Equal(HomePermissionRequestState.Approved, coveredBeforeChange.State);
        Assert.Equal(grant.GrantId, coveredBeforeChange.AppliedGrantId);

        currentPolicy = new HomePermissionActionPolicy(HomePermissionRisk.High, false, true);
        var staleDispatch = await service.BeginExecutionAsync(coveredBeforeChange.RequestId);
        Assert.Equal(HomePermissionRequestState.PendingApproval, staleDispatch.State);
        Assert.Equal("HOME_ACTION_POLICY_CHANGED", staleDispatch.Code);
        var refreshed = await service.ReadRequestObservationAsync(coveredBeforeChange.RequestId);
        Assert.Equal(currentPolicy, refreshed!.Policy);
        Assert.True(refreshed.Impact.IsUnknown);
        Assert.Null(refreshed.Impact.ChangePreview);
        Assert.NotEqual("low-risk preview", refreshed.Impact.ChangePreview);
        Assert.Equal(grant.GrantId, Assert.Single((await service.GetSnapshotAsync()).Grants).GrantId);

        Assert.False((await service.DecideAsync(coveredBeforeChange.RequestId,
            HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        Assert.True((await service.DecideAsync(coveredBeforeChange.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var reapproved = await service.ReadRequestObservationAsync(coveredBeforeChange.RequestId);
        Assert.Equal(currentPolicy, reapproved!.Policy);
        Assert.True(reapproved.Impact.IsUnknown);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(coveredBeforeChange.RequestId)).State);

        var expandedRiskNewRequest = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-3"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, expandedRiskNewRequest.State);
        Assert.Null(expandedRiskNewRequest.AppliedGrantId);
    }

    [Fact]
    public async Task AlwaysTrust_risk_ceiling_is_explicit_and_scope_exact_after_policy_expansion()
    {
        HomePermissionActionPolicy readPolicy = new(HomePermissionRisk.Routine, true, false);
        var service = new HomePermissionTrustService(_stateStore, (target, action) =>
            target != "app.boards" ? null : action switch
            {
                "boards.read" => readPolicy,
                "boards.delete" => new HomePermissionActionPolicy(HomePermissionRisk.High, false, false),
                _ => null,
            }, _clock);
        var routine = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.MarkAlwaysTrustWarningShownAsync(routine.RequestId)).Succeeded);
        Assert.True((await service.DecideAsync(routine.RequestId, HomeApprovalChoice.AcceptAndAlwaysTrust,
            userConfirmedAlwaysTrustWarning: true)).Succeeded);
        var routineTrust = (await service.GetSnapshotAsync()).Grants.Single(grant => grant.Scope.ActionName == "boards.read");
        Assert.Equal(HomePermissionRisk.Routine, routineTrust.MaximumRisk);

        var alreadyApproved = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-2") with
        {
            RequestId = "always-trust-routine-before-escalation",
            Impact = new HomePermissionImpactPreview(["board"], 1,
                [new HomeObjectReference("board", "board-42")], false, ChangePreview: "routine impact"),
        });
        Assert.Equal(HomePermissionRequestState.Approved, alreadyApproved.State);
        Assert.Equal(routineTrust.GrantId, alreadyApproved.AppliedGrantId);
        readPolicy = new HomePermissionActionPolicy(HomePermissionRisk.High, false, true);

        var stale = await service.BeginExecutionAsync(alreadyApproved.RequestId);
        Assert.Equal(HomePermissionRequestState.PendingApproval, stale.State);
        var refreshed = await service.ReadRequestObservationAsync(alreadyApproved.RequestId);
        Assert.Equal(readPolicy, refreshed!.Policy);
        Assert.True(refreshed.Impact.IsUnknown);
        Assert.Null(refreshed.Impact.ChangePreview);
        Assert.True((await service.DecideAsync(alreadyApproved.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.Equal(readPolicy, (await service.ReadRequestObservationAsync(alreadyApproved.RequestId))!.Policy);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(alreadyApproved.RequestId)).State);

        var expandedRiskRequest = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-3"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, expandedRiskRequest.State);
        Assert.Null(expandedRiskRequest.AppliedGrantId);

        var explicitlyHigh = await service.AuthorizeAsync(Request("boards.delete"));
        Assert.True((await service.MarkAlwaysTrustWarningShownAsync(explicitlyHigh.RequestId)).Succeeded);
        Assert.True((await service.DecideAsync(explicitlyHigh.RequestId, HomeApprovalChoice.AcceptAndAlwaysTrust,
            userConfirmedAlwaysTrustWarning: true)).Succeeded);
        var highTrust = (await service.GetSnapshotAsync()).Grants.Single(grant => grant.Scope.ActionName == "boards.delete");
        Assert.Equal(HomePermissionRisk.High, highTrust.MaximumRisk);
        var explicitlyCoveredHigh = await service.AuthorizeAsync(Request("boards.delete", sessionId: "session-4"));
        Assert.Equal(HomePermissionRequestState.Approved, explicitlyCoveredHigh.State);
        Assert.Equal(highTrust.GrantId, explicitlyCoveredHigh.AppliedGrantId);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(explicitlyCoveredHigh.RequestId)).State);
        Assert.True(await service.IsExecutionCurrentAsync(explicitlyCoveredHigh.RequestId));
        Assert.Single((await service.GetSnapshotAsync()).RecentAuditEvents.Where(item =>
            item.RequestId == explicitlyCoveredHigh.RequestId && item.Kind == HomePermissionAuditKind.ExecutionStarted));
        Assert.Equal(HomePermissionRequestState.PendingApproval,
            (await service.AuthorizeAsync(Request("boards.delete", sessionId: "session-5", objectId: "board-99"))).State);

        var restarted = new HomePermissionTrustService(_stateStore, (target, action) =>
            target == "app.boards" && action == "boards.delete"
                ? new HomePermissionActionPolicy(HomePermissionRisk.High, false, false)
                : null, _clock);
        Assert.Equal(HomePermissionRisk.High,
            Assert.Single((await restarted.GetSnapshotAsync()).Grants.Where(grant => grant.Scope.ActionName == "boards.delete")).MaximumRisk);
        Assert.Equal(HomePermissionRequestState.Approved,
            (await restarted.AuthorizeAsync(Request("boards.delete", sessionId: "session-6"))).State);

        var legacyState = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "home-state.json")))!;
        var permissionRecord = legacyState["Records"]!.AsArray().Single(record =>
            record!["RecordId"]!.GetValue<string>() == "home.permissions-trust")!;
        var legacyHighGrant = permissionRecord["Payload"]!["Grants"]!.AsArray().Single(grant =>
            grant!["Scope"]!["ActionName"]!.GetValue<string>() == "boards.delete")!.AsObject();
        Assert.True(legacyHighGrant.Remove("MaximumRisk"));
        await File.WriteAllTextAsync(Path.Combine(_root, "home-state.json"), legacyState.ToJsonString());

        var legacyRestart = new HomePermissionTrustService(_stateStore, (target, action) =>
            target == "app.boards" && action == "boards.delete"
                ? new HomePermissionActionPolicy(HomePermissionRisk.High, false, false)
                : null, _clock);
        Assert.Equal(HomePermissionRisk.High,
            Assert.Single((await legacyRestart.GetSnapshotAsync()).Grants.Where(grant => grant.Scope.ActionName == "boards.delete")).MaximumRisk);
        Assert.Equal(HomePermissionRequestState.Approved,
            (await legacyRestart.AuthorizeAsync(Request("boards.delete", sessionId: "session-7"))).State);
    }

    [Theory]
    [InlineData("duplicate-high")]
    [InlineData("null-plus-high")]
    [InlineData("different-risk")]
    [InlineData("changed-caller-identity")]
    [InlineData("request-risk-mismatch")]
    public async Task Legacy_elevated_risk_recovery_fails_closed_without_unique_exact_evidence(string tamper)
    {
        var highPolicy = new HomePermissionActionPolicy(HomePermissionRisk.High, false, false);
        var service = new HomePermissionTrustService(_stateStore,
            (target, action) => target == "app.boards" && action == "boards.delete" ? highPolicy : null, _clock);
        var request = await service.AuthorizeAsync(Request("boards.delete") with { RequestId = "legacy-risk-source" });
        Assert.True((await service.MarkAlwaysTrustWarningShownAsync(request.RequestId)).Succeeded);
        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.AcceptAndAlwaysTrust,
            userConfirmedAlwaysTrustWarning: true)).Succeeded);
        var grant = Assert.Single((await service.GetSnapshotAsync()).Grants);

        var document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "home-state.json")))!;
        var payload = document["Records"]!.AsArray()
            .Single(record => record!["RecordId"]!.GetValue<string>() == "home.permissions-trust")!["Payload"]!;
        var grantNode = payload["Grants"]!.AsArray().Single(item =>
            item!["GrantId"]!.GetValue<string>() == grant.GrantId)!.AsObject();
        Assert.True(grantNode.Remove("MaximumRisk"));
        if (tamper is "changed-caller-identity" or "request-risk-mismatch")
        {
            var sourceRequest = payload["Requests"]!.AsArray().Single(item =>
                item!["RequestId"]!.GetValue<string>() == request.RequestId)!;
            if (tamper == "changed-caller-identity")
                sourceRequest["Caller"]!["IdentityVersion"] = "hash-v2-changed";
            else
                sourceRequest["Policy"]!["Risk"] = (int)HomePermissionRisk.Routine;
        }
        else
        {
            var eventNode = payload["Audit"]!.AsArray().Single(item =>
                item!["RequestId"]!.GetValue<string>() == request.RequestId &&
                item["Kind"]!.GetValue<int>() == (int)HomePermissionAuditKind.TrustGranted)!;
            var duplicate = JsonNode.Parse(eventNode.ToJsonString())!;
            if (tamper == "null-plus-high") duplicate["Risk"] = null;
            if (tamper == "different-risk") duplicate["Risk"] = (int)HomePermissionRisk.Routine;
            if (tamper is "duplicate-high" or "null-plus-high" or "different-risk")
                payload["Audit"]!.AsArray().Add(duplicate);
        }
        await File.WriteAllTextAsync(Path.Combine(_root, "home-state.json"), document.ToJsonString());

        var reopened = new HomePermissionTrustService(new FileHomeCoreStateStore(Path.Combine(_root, "home-state.json")),
            (target, action) => target == "app.boards" && action == "boards.delete" ? highPolicy : null, _clock);
        var retainedGrant = Assert.Single((await reopened.GetSnapshotAsync()).Grants);
        Assert.Equal(grant.GrantId, retainedGrant.GrantId);
        Assert.Equal(HomePermissionRisk.Routine, retainedGrant.MaximumRisk);
        var newHighRiskRequest = await reopened.AuthorizeAsync(Request("boards.delete", sessionId: "legacy-check"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, newHighRiskRequest.State);
        Assert.Null(newHighRiskRequest.AppliedGrantId);
    }

    [Fact]
    public async Task Legacy_grants_with_same_time_recover_risk_only_through_their_exact_request_links()
    {
        HomePermissionActionPolicy policy = new(HomePermissionRisk.Routine, true, false);
        var service = new HomePermissionTrustService(_stateStore,
            (target, action) => target == "app.boards" && action == "boards.read" ? policy : null, _clock);
        var routineRequest = await service.AuthorizeAsync(Request("boards.read") with { RequestId = "same-time-routine" });
        Assert.True((await service.MarkAlwaysTrustWarningShownAsync(routineRequest.RequestId)).Succeeded);
        Assert.True((await service.DecideAsync(routineRequest.RequestId, HomeApprovalChoice.AcceptAndAlwaysTrust,
            userConfirmedAlwaysTrustWarning: true)).Succeeded);
        var grantsAfterRoutine = (await service.GetSnapshotAsync()).Grants;
        var routineGrant = Assert.Single(grantsAfterRoutine);

        policy = new HomePermissionActionPolicy(HomePermissionRisk.High, false, false);
        var highRequest = await service.AuthorizeAsync(Request("boards.read", sessionId: "high-session") with
            { RequestId = "same-time-high" });
        Assert.Equal(HomePermissionRequestState.PendingApproval, highRequest.State);
        Assert.True((await service.MarkAlwaysTrustWarningShownAsync(highRequest.RequestId)).Succeeded);
        Assert.True((await service.DecideAsync(highRequest.RequestId, HomeApprovalChoice.AcceptAndAlwaysTrust,
            userConfirmedAlwaysTrustWarning: true)).Succeeded);
        var grantsAfterHigh = (await service.GetSnapshotAsync()).Grants;
        Assert.Equal(2, grantsAfterHigh.Count);
        var highGrant = Assert.Single(grantsAfterHigh.Where(item => item.GrantId != routineGrant.GrantId));
        Assert.Equal(routineGrant.CreatedAt, highGrant.CreatedAt);

        var document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "home-state.json")))!;
        var payload = document["Records"]!.AsArray()
            .Single(record => record!["RecordId"]!.GetValue<string>() == "home.permissions-trust")!["Payload"]!;
        foreach (var id in new[] { routineGrant.GrantId, highGrant.GrantId })
        {
            var grantNode = payload["Grants"]!.AsArray().Single(item =>
                item!["GrantId"]!.GetValue<string>() == id)!.AsObject();
            Assert.True(grantNode.Remove("MaximumRisk"));
        }
        await File.WriteAllTextAsync(Path.Combine(_root, "home-state.json"), document.ToJsonString());

        var reopened = new HomePermissionTrustService(new FileHomeCoreStateStore(Path.Combine(_root, "home-state.json")),
            (target, action) => target == "app.boards" && action == "boards.read"
                ? new HomePermissionActionPolicy(HomePermissionRisk.High, false, false)
                : null, _clock);
        var recovered = (await reopened.GetSnapshotAsync()).Grants;
        Assert.Equal(HomePermissionRisk.Routine, Assert.Single(recovered.Where(item => item.GrantId == routineGrant.GrantId)).MaximumRisk);
        Assert.Equal(HomePermissionRisk.High, Assert.Single(recovered.Where(item => item.GrantId == highGrant.GrantId)).MaximumRisk);
        var next = await reopened.AuthorizeAsync(Request("boards.read", sessionId: "after-legacy-recovery"));
        Assert.Equal(HomePermissionRequestState.Approved, next.State);
        Assert.Equal(highGrant.GrantId, next.AppliedGrantId);
    }

    [Fact]
    public async Task Value_equal_current_policy_objects_remain_valid_at_dispatch_and_execution_fence()
    {
        var service = new HomePermissionTrustService(_stateStore,
            (target, action) => target == "app.boards" && action == "boards.read"
                ? new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false)
                : null, _clock);
        var request = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);

        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(request.RequestId)).State);
        Assert.True(await service.IsExecutionCurrentAsync(request.RequestId));
        var restarted = new HomePermissionTrustService(_stateStore,
            (target, action) => target == "app.boards" && action == "boards.read"
                ? new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false)
                : null, _clock);
        Assert.False(await restarted.IsExecutionCurrentAsync(request.RequestId));

        var consumedRetry = await restarted.BeginExecutionAsync(request.RequestId);
        Assert.Equal(HomePermissionRequestState.Executing, consumedRetry.State);
        Assert.Equal("HOME_PERMISSION_NOT_AUTHORIZED", consumedRetry.Code);
        Assert.False(consumedRetry.IsAllowed);
        var repeatedDecision = await restarted.DecideAsync(request.RequestId, HomeApprovalChoice.Accept);
        Assert.False(repeatedDecision.Succeeded);
        Assert.Equal("HOME_REQUEST_NOT_PENDING", repeatedDecision.Code);

        var oldStartAuditCount = (await restarted.GetSnapshotAsync()).RecentAuditEvents.Count(item =>
            item.RequestId == request.RequestId && item.Kind == HomePermissionAuditKind.ExecutionStarted);
        Assert.Equal(1, oldStartAuditCount);

        var approvedButUnbegun = await restarted.AuthorizeAsync(Request("boards.read") with
            { RequestId = "session-approved-but-not-begun" });
        Assert.Equal(HomePermissionRequestState.PendingApproval, approvedButUnbegun.State);
        Assert.True((await restarted.DecideAsync(approvedButUnbegun.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await restarted.BeginExecutionAsync(approvedButUnbegun.RequestId)).State);
        Assert.True(await restarted.IsExecutionCurrentAsync(approvedButUnbegun.RequestId));
        Assert.False(await restarted.IsExecutionCurrentAsync(request.RequestId));
        Assert.Equal(1, (await restarted.GetSnapshotAsync()).RecentAuditEvents.Count(item =>
            item.RequestId == request.RequestId && item.Kind == HomePermissionAuditKind.ExecutionStarted));
    }

    [Fact]
    public async Task Invalidating_session_consent_revokes_all_matching_request_ids_without_affecting_other_scopes()
    {
        var readPolicy = new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false);
        var service = new HomePermissionTrustService(_stateStore, (target, action) =>
            target == "app.boards" && action == "boards.read" ? readPolicy :
            target == "app.boards" && action == "boards.write"
                ? new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false)
                : null, _clock);

        var first = await service.AuthorizeAsync(Request("boards.read") with { RequestId = "consent-first" });
        Assert.True((await service.DecideAsync(first.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var second = await service.AuthorizeAsync(Request("boards.read") with { RequestId = "consent-second" });
        Assert.Equal(HomePermissionRequestState.Approved, second.State);
        Assert.Equal("HOME_PERMISSION_GRANTED_FOR_SESSION", second.ResultCode);

        var otherScope = await service.AuthorizeAsync(Request("boards.write") with { RequestId = "other-scope" });
        Assert.True((await service.DecideAsync(otherScope.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Executing, (await service.BeginExecutionAsync(first.RequestId)).State);
        Assert.Equal(HomePermissionRequestState.Executing, (await service.BeginExecutionAsync(second.RequestId)).State);
        Assert.Equal(HomePermissionRequestState.Executing, (await service.BeginExecutionAsync(otherScope.RequestId)).State);

        readPolicy = new HomePermissionActionPolicy(HomePermissionRisk.High, false, true);
        Assert.False(await service.IsExecutionCurrentAsync(first.RequestId));
        Assert.False(await service.IsExecutionCurrentAsync(second.RequestId));
        Assert.True(await service.IsExecutionCurrentAsync(otherScope.RequestId));

        readPolicy = new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false);
        var fresh = await service.AuthorizeAsync(Request("boards.read") with { RequestId = "consent-fresh" });
        Assert.Equal(HomePermissionRequestState.PendingApproval, fresh.State);
        Assert.True((await service.DecideAsync(fresh.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Executing, (await service.BeginExecutionAsync(fresh.RequestId)).State);
        Assert.True(await service.IsExecutionCurrentAsync(fresh.RequestId));

        // A new consent for this key must not restore an older already-consumed request.
        Assert.False(await service.IsExecutionCurrentAsync(first.RequestId));
        Assert.False(await service.IsExecutionCurrentAsync(second.RequestId));
        Assert.True(await service.IsExecutionCurrentAsync(otherScope.RequestId));
        Assert.True(await service.IsExecutionCurrentAsync(fresh.RequestId));
        var audit = (await service.GetSnapshotAsync()).RecentAuditEvents;
        Assert.Equal(1, audit.Count(item => item.RequestId == first.RequestId &&
            item.Kind == HomePermissionAuditKind.ExecutionStarted));
        Assert.Equal(1, audit.Count(item => item.RequestId == second.RequestId &&
            item.Kind == HomePermissionAuditKind.ExecutionStarted));
    }

    [Fact]
    public async Task Revoking_applied_trust_invalidates_an_in_flight_execution_fence()
    {
        var service = CreateService();
        var consent = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(consent.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var trusted = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-2"));
        Assert.Equal(HomePermissionRequestState.Approved, trusted.State);

        var executing = await service.BeginExecutionAsync(trusted.RequestId);
        Assert.Equal(HomePermissionRequestState.Executing, executing.State);
        Assert.True((await service.IsExecutionCurrentAsync(trusted.RequestId)));
        Assert.True((await service.RevokeGrantAsync(trusted.AppliedGrantId!)).Succeeded);

        Assert.False(await service.IsExecutionCurrentAsync(trusted.RequestId));
    }

    [Fact]
    public async Task Narrowing_applied_trust_invalidates_an_in_flight_out_of_scope_execution()
    {
        var service = CreateService();
        var consent = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(consent.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var trusted = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-2"));
        Assert.NotNull(trusted.AppliedGrantId);
        var executing = await service.BeginExecutionAsync(trusted.RequestId);
        Assert.Equal(HomePermissionRequestState.Executing, executing.State);
        Assert.True(await service.IsExecutionCurrentAsync(trusted.RequestId));

        Assert.True((await service.NarrowGrantAsync(trusted.AppliedGrantId!,
            new HomePermissionScope("app.boards", "boards.read", []))).Succeeded);
        Assert.False(await service.IsExecutionCurrentAsync(trusted.RequestId));
    }

    [Fact]
    public async Task Expiring_applied_trust_invalidates_an_in_flight_execution_fence()
    {
        var service = CreateService();
        var consent = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(consent.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var trusted = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-2"));
        Assert.Equal(HomePermissionRequestState.Approved, trusted.State);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(trusted.RequestId)).State);
        Assert.True(await service.IsExecutionCurrentAsync(trusted.RequestId));

        _clock.Advance(TimeSpan.FromDays(30).Add(TimeSpan.FromTicks(1)));

        Assert.False(await service.IsExecutionCurrentAsync(trusted.RequestId));
    }

    [Fact]
    public async Task Unblocking_caller_does_not_restore_an_in_flight_approval_invalidated_by_block()
    {
        var service = CreateService();
        var request = await service.AuthorizeAsync(Request("boards.read"));
        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var separatelyApproved = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-2"));
        Assert.True((await service.DecideAsync(separatelyApproved.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(request.RequestId)).State);

        Assert.True((await service.BlockCallerAsync("script:sha256:test")).Succeeded);
        Assert.False(await service.IsExecutionCurrentAsync(request.RequestId));
        Assert.True((await service.UnblockCallerAsync("script:sha256:test")).Succeeded);

        Assert.False(await service.IsExecutionCurrentAsync(request.RequestId));
        var blockedApproval = await service.BeginExecutionAsync(separatelyApproved.RequestId);
        Assert.Equal(HomePermissionRequestState.Blocked, blockedApproval.State);
        Assert.Equal("HOME_CALLER_BLOCKED", blockedApproval.Code);
        var newRequest = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-3"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, newRequest.State);
    }

    [Fact]
    public async Task Pending_request_created_before_block_cannot_grant_trust_after_unblock()
    {
        var service = CreateService();
        var stale = await service.AuthorizeAsync(Request("boards.read") with { RequestId = "pending-before-block" });
        Assert.Equal(HomePermissionRequestState.PendingApproval, stale.State);
        Assert.True((await service.MarkAlwaysTrustWarningShownAsync(stale.RequestId)).Succeeded);

        Assert.True((await service.BlockCallerAsync("script:sha256:test")).Succeeded);
        Assert.True((await service.UnblockCallerAsync("script:sha256:test")).Succeeded);
        var refused = await service.DecideAsync(stale.RequestId, HomeApprovalChoice.AcceptAndAlwaysTrust,
            userConfirmedAlwaysTrustWarning: true);

        Assert.False(refused.Succeeded);
        Assert.Equal("HOME_CALLER_BLOCKED", refused.Code);
        Assert.Equal(HomePermissionRequestState.Blocked,
            (await service.ReadRequestObservationAsync(stale.RequestId))!.State);
        Assert.Empty((await service.GetSnapshotAsync()).Grants);

        var fresh = await service.AuthorizeAsync(Request("boards.read") with { RequestId = "fresh-after-unblock" });
        Assert.Equal(HomePermissionRequestState.PendingApproval, fresh.State);
        Assert.True((await service.DecideAsync(fresh.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(fresh.RequestId)).State);
    }

    [Fact]
    public async Task Blocking_caller_withdraws_pending_and_session_approved_requests()
    {
        var service = CreateService();
        var approved = await service.AuthorizeAsync(Request("boards.read", sessionId: "same-session") with
            { RequestId = "approved-before-block" });
        Assert.True((await service.DecideAsync(approved.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var pending = await service.AuthorizeAsync(Request("boards.edit", sessionId: "different-session") with
            { RequestId = "pending-before-block" });
        Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);

        Assert.True((await service.BlockCallerAsync("script:sha256:test")).Succeeded);
        Assert.Equal(HomePermissionRequestState.Blocked,
            (await service.ReadRequestObservationAsync(approved.RequestId))!.State);
        Assert.Equal(HomePermissionRequestState.Blocked,
            (await service.ReadRequestObservationAsync(pending.RequestId))!.State);
        Assert.Empty((await service.GetSnapshotAsync()).PendingRequests);

        Assert.True((await service.UnblockCallerAsync("script:sha256:test")).Succeeded);
        var fresh = await service.AuthorizeAsync(Request("boards.read", sessionId: "same-session") with
            { RequestId = "fresh-after-block" });
        Assert.Equal(HomePermissionRequestState.PendingApproval, fresh.State);
        Assert.True((await service.DecideAsync(fresh.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(fresh.RequestId)).State);
    }

    [Fact]
    public async Task Recording_terminal_outcome_cannot_replace_dispatch_admission()
    {
        var service = CreateService();
        var request = await service.AuthorizeAsync(Request("boards.read") with { RequestId = "outcome-before-dispatch" });
        Assert.True((await service.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);

        var premature = await service.RecordExecutionAsync(request.RequestId,
            new HomeExecutionOutcome(HomePermissionRequestState.Succeeded, "READ_DONE", "Read completed.",
                [new HomeObjectReference("board", "board-42")]));
        Assert.False(premature.Succeeded);
        Assert.Equal("HOME_PERMISSION_NOT_AUTHORIZED", premature.Code);
        Assert.Equal(HomePermissionRequestState.Approved,
            (await service.ReadRequestObservationAsync(request.RequestId))!.State);
        Assert.DoesNotContain((await service.GetSnapshotAsync()).RecentAuditEvents,
            item => item.RequestId == request.RequestId && item.Kind == HomePermissionAuditKind.ExecutionCompleted);

        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(request.RequestId)).State);
        Assert.True((await service.RecordExecutionAsync(request.RequestId,
            new HomeExecutionOutcome(HomePermissionRequestState.Succeeded, "READ_DONE", "Read completed.",
                [new HomeObjectReference("board", "board-42")]))).Succeeded);
    }

    [Fact]
    public async Task Failed_persistence_does_not_leave_a_session_approval_in_memory()
    {
        var path = Path.Combine(_root, "home-state.json");
        var store = new FailNextWriteStateStore(new FileHomeCoreStateStore(path));
        var service = new HomePermissionTrustService(store, (target, action) =>
            target == "app.boards" && action == "boards.read"
                ? new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false)
                : null, _clock);
        var request = await service.AuthorizeAsync(Request("boards.read"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);

        store.FailNextPermissionWrite = true;
        await Assert.ThrowsAsync<HomeFeatureStoreException>(() =>
            service.DecideAsync(request.RequestId, HomeApprovalChoice.Accept));

        Assert.Equal(HomePermissionRequestState.PendingApproval,
            (await service.GetAuthorizationAsync(request.RequestId)).State);
        var afterFailedWrite = await service.AuthorizeAsync(Request("boards.read"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, afterFailedWrite.State);
        Assert.Equal("HOME_PERMISSION_REQUIRED", afterFailedWrite.Code);
    }

    [Fact]
    public async Task Session_approval_requires_same_stable_identity_version_origin_and_verification_state()
    {
        var service = CreateService();
        HomePermissionRequestSubmission ForCaller(string callerId, string requestId, string? identityVersion,
            string? origin, bool verified, string displayName) =>
            Request("boards.read", "identity-bound-session") with
            {
                RequestId = requestId,
                Caller = new HomePermissionCallerIdentity(callerId, displayName, origin, identityVersion, verified),
            };

        var initial = await service.AuthorizeAsync(ForCaller("script:sha256:stable", "identity-initial",
            "content-hash-v1", "https://origin-a.example", true, "Original display"));
        Assert.True((await service.DecideAsync(initial.RequestId, HomeApprovalChoice.Accept)).Succeeded);

        var displayOnlyChange = await service.AuthorizeAsync(ForCaller("script:sha256:stable", "identity-display-only",
            "content-hash-v1", "https://origin-a.example", true, "Renamed display"));
        Assert.Equal(HomePermissionRequestState.Approved, displayOnlyChange.State);

        var changedVersion = await service.AuthorizeAsync(ForCaller("script:sha256:stable", "identity-version-changed",
            "content-hash-v2", "https://origin-a.example", true, "Original display"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, changedVersion.State);

        var changedVerifiedOrigin = await service.AuthorizeAsync(ForCaller("script:sha256:stable", "identity-origin-changed",
            "content-hash-v1", "https://origin-b.example", true, "Original display"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, changedVerifiedOrigin.State);

        var changedVerificationState = await service.AuthorizeAsync(ForCaller("script:sha256:stable", "identity-verification-changed",
            "content-hash-v1", "https://origin-a.example", false, "Original display"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, changedVerificationState.State);

        var noVersion = await service.AuthorizeAsync(ForCaller("script:sha256:no-version", "identity-null-version",
            null, null, false, "No version"));
        Assert.True((await service.DecideAsync(noVersion.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var stillNoVersion = await service.AuthorizeAsync(ForCaller("script:sha256:no-version", "identity-null-stays-null",
            null, null, false, "Renamed no-version"));
        Assert.Equal(HomePermissionRequestState.Approved, stillNoVersion.State);
        var versionNowPresent = await service.AuthorizeAsync(ForCaller("script:sha256:no-version", "identity-null-to-version",
            "content-hash-added", null, false, "No version"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, versionNowPresent.State);
    }

    [Fact]
    public async Task Persistent_grant_requires_same_stable_caller_identity_but_not_display_name()
    {
        var service = CreateService();
        HomePermissionRequestSubmission ForCaller(string requestId, string identityVersion, string origin,
            bool verified, string displayName) => Request("boards.read", "persistent-identity-session") with
        {
            RequestId = requestId,
            Caller = new HomePermissionCallerIdentity("script:sha256:persistent", displayName, origin,
                identityVersion, verified),
        };
        var consent = await service.AuthorizeAsync(ForCaller("persistent-consent", "code-hash-v1",
            "https://origin-a.example", true, "Original name"));
        Assert.True((await service.DecideAsync(consent.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var grant = Assert.Single((await service.GetSnapshotAsync()).Grants);

        var displayOnlyChange = await service.AuthorizeAsync(ForCaller("persistent-display-change", "code-hash-v1",
            "https://origin-a.example", true, "Renamed display"));
        Assert.Equal(HomePermissionRequestState.Approved, displayOnlyChange.State);
        Assert.Equal(grant.GrantId, displayOnlyChange.AppliedGrantId);

        var changedVersion = await service.AuthorizeAsync(ForCaller("persistent-version-change", "code-hash-v2",
            "https://origin-a.example", true, "Original name"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, changedVersion.State);
        var changedOrigin = await service.AuthorizeAsync(ForCaller("persistent-origin-change", "code-hash-v1",
            "https://origin-b.example", true, "Original name"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, changedOrigin.State);
        var changedVerification = await service.AuthorizeAsync(ForCaller("persistent-verification-change", "code-hash-v1",
            "https://origin-a.example", false, "Original name"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, changedVerification.State);
    }

    [Fact]
    public async Task Block_unblock_history_survives_store_reopen_and_is_bound_to_stable_caller_identity()
    {
        var path = Path.Combine(_root, "home-state.json");
        var store = new FileHomeCoreStateStore(path);
        Func<string, string, HomePermissionActionPolicy?> policy = (target, action) =>
            target == "app.boards" && action.StartsWith("boards.", StringComparison.Ordinal)
                ? new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false)
                : null;
        var service = new HomePermissionTrustService(store, policy, _clock);
        HomePermissionRequestSubmission CallerRequest(string callerId, string sessionId, string requestId) =>
            Request("boards.read", sessionId) with
            {
                RequestId = requestId,
                Caller = new HomePermissionCallerIdentity(callerId, "Same display name", "local project", callerId + ":v1", false),
            };

        var firstCallerGrant = await service.AuthorizeAsync(CallerRequest("script:sha256:caller-a", "session-a0", "caller-a-grant"));
        Assert.True((await service.DecideAsync(firstCallerGrant.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var approvedBeforeBlock = await service.AuthorizeAsync(CallerRequest("script:sha256:caller-a", "session-a1", "caller-a-approved"));
        Assert.Equal(HomePermissionRequestState.Approved, approvedBeforeBlock.State);
        var executingBeforeBlock = await service.AuthorizeAsync(CallerRequest("script:sha256:caller-a", "session-a2", "caller-a-executing"));
        Assert.Equal(HomePermissionRequestState.Approved, executingBeforeBlock.State);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(executingBeforeBlock.RequestId)).State);

        var originalAudit = (await store.ReadAsync()).State!.Records.Single(item => item.RecordId == "home.permissions-trust")
            .Payload.GetProperty("Audit").EnumerateArray().Select(item => item.GetRawText()).ToArray();
        Assert.True((await service.BlockCallerAsync("script:sha256:caller-a")).Succeeded);

        var otherCaller = await service.AuthorizeAsync(CallerRequest("script:sha256:caller-b", "session-b", "caller-b-independent"));
        Assert.Equal(HomePermissionRequestState.PendingApproval, otherCaller.State);
        Assert.True((await service.DecideAsync(otherCaller.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(otherCaller.RequestId)).State);
        Assert.True((await service.UnblockCallerAsync("script:sha256:caller-a")).Succeeded);

        var reopened = new HomePermissionTrustService(new FileHomeCoreStateStore(path), policy, _clock);
        Assert.False(await reopened.IsExecutionCurrentAsync(executingBeforeBlock.RequestId));
        var oldApprovedDispatch = await reopened.BeginExecutionAsync(approvedBeforeBlock.RequestId);
        Assert.Equal(HomePermissionRequestState.Blocked, oldApprovedDispatch.State);
        Assert.Equal("HOME_CALLER_BLOCKED", oldApprovedDispatch.Code);
        Assert.True(await reopened.IsExecutionCurrentAsync(otherCaller.RequestId));
        Assert.Contains((await reopened.GetSnapshotAsync()).Grants,
            grant => grant.Caller.CallerId == "script:sha256:caller-a" && !grant.IsRevoked);

        var newAfterUnblock = await reopened.AuthorizeAsync(CallerRequest("script:sha256:caller-a", "session-a3", "caller-a-new-request") with
        {
            Scope = new HomePermissionScope("app.boards", "boards.edit", [new HomeObjectReference("board", "board-42")]),
        });
        Assert.Equal(HomePermissionRequestState.PendingApproval, newAfterUnblock.State);
        Assert.True((await reopened.DecideAsync(newAfterUnblock.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await reopened.BeginExecutionAsync(newAfterUnblock.RequestId)).State);
        Assert.True(await reopened.IsExecutionCurrentAsync(newAfterUnblock.RequestId));

        var finalState = (await new FileHomeCoreStateStore(path).ReadAsync()).State!;
        var auditEvents = finalState.Records.Single(item => item.RecordId == "home.permissions-trust")
            .Payload.GetProperty("Audit").EnumerateArray().ToArray();
        var finalAudit = auditEvents.Select(item => item.GetRawText()).ToArray();
        Assert.Equal(originalAudit, finalAudit.Take(originalAudit.Length));
        var requestReceived = auditEvents.Single(item => item.TryGetProperty("RequestId", out var id) &&
            id.GetString() == executingBeforeBlock.RequestId &&
            item.GetProperty("Kind").GetInt32() == (int)HomePermissionAuditKind.RequestReceived);
        var callerBlock = auditEvents.Single(item => item.GetProperty("CallerId").GetString() == "script:sha256:caller-a" &&
            item.GetProperty("Kind").GetInt32() == (int)HomePermissionAuditKind.CallerBlocked);
        var callerUnblock = auditEvents.Single(item => item.GetProperty("CallerId").GetString() == "script:sha256:caller-a" &&
            item.GetProperty("Kind").GetInt32() == (int)HomePermissionAuditKind.CallerUnblocked);
        Assert.Equal(requestReceived.GetProperty("Timestamp").GetDateTimeOffset(),
            callerBlock.GetProperty("Timestamp").GetDateTimeOffset());
        Assert.Equal(callerBlock.GetProperty("Timestamp").GetDateTimeOffset(),
            callerUnblock.GetProperty("Timestamp").GetDateTimeOffset());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-caller")]
    [InlineData("wrong-scope")]
    [InlineData("wrong-time")]
    [InlineData("duplicate")]
    public async Task Untrustworthy_request_received_provenance_fails_closed_without_rewriting_audit(string tamper)
    {
        var path = Path.Combine(_root, "home-state.json");
        var service = CreateService();
        var approved = await service.AuthorizeAsync(Request("boards.read") with { RequestId = "provenance-approved" });
        Assert.True((await service.DecideAsync(approved.RequestId, HomeApprovalChoice.AcceptAndTrust)).Succeeded);
        var executing = await service.AuthorizeAsync(Request("boards.read", sessionId: "session-2") with
            { RequestId = "provenance-executing" });
        Assert.Equal(HomePermissionRequestState.Approved, executing.State);
        Assert.Equal(HomePermissionRequestState.Executing,
            (await service.BeginExecutionAsync(executing.RequestId)).State);
        var pendingDecision = await service.AuthorizeAsync(Request("boards.edit") with
            { RequestId = "provenance-pending-decision" });
        Assert.Equal(HomePermissionRequestState.PendingApproval, pendingDecision.State);
        Assert.True((await service.MarkAlwaysTrustWarningShownAsync(pendingDecision.RequestId)).Succeeded);

        var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        var payload = document["Records"]!.AsArray()
            .Single(item => item!["RecordId"]!.GetValue<string>() == "home.permissions-trust")!["Payload"]!;
        var audit = payload["Audit"]!.AsArray();
        foreach (var requestId in new[] { approved.RequestId, executing.RequestId, pendingDecision.RequestId })
        {
            var received = audit.Single(item => item?["RequestId"]?.GetValue<string>() == requestId &&
                item?["Kind"]?.GetValue<int>() == (int)HomePermissionAuditKind.RequestReceived)!;
            switch (tamper)
            {
                case "missing": audit.Remove(received); break;
                case "wrong-caller": received["CallerId"] = "script:sha256:different-caller"; break;
                case "wrong-scope": received["Scope"]!["Objects"]!.AsArray()[0]!["ObjectId"] = "board-99"; break;
                case "wrong-time": received["Timestamp"] = "2026-09-26T12:00:01+00:00"; break;
                case "duplicate": audit.Add(JsonNode.Parse(received.ToJsonString())!); break;
                default: throw new ArgumentOutOfRangeException(nameof(tamper));
            }
        }
        var originalTamperedAudit = audit.Select(item => item!.ToJsonString()).ToArray();
        await File.WriteAllTextAsync(path, document.ToJsonString());

        var reopened = new HomePermissionTrustService(new FileHomeCoreStateStore(path),
            (target, action) => target == "app.boards" && action == "boards.read"
                ? new HomePermissionActionPolicy(HomePermissionRisk.Routine, true, false)
                : null, _clock);
        Assert.False(await reopened.IsExecutionCurrentAsync(executing.RequestId));
        var refused = await reopened.BeginExecutionAsync(approved.RequestId);
        Assert.Equal(HomePermissionRequestState.Denied, refused.State);
        Assert.Equal("HOME_PERMISSION_NOT_AUTHORIZED", refused.Code);
        var untrustedDecision = await reopened.DecideAsync(pendingDecision.RequestId,
            HomeApprovalChoice.AcceptAndAlwaysTrust, userConfirmedAlwaysTrustWarning: true);
        Assert.False(untrustedDecision.Succeeded);
        Assert.Equal("HOME_PERMISSION_NOT_AUTHORIZED", untrustedDecision.Code);
        Assert.Single((await reopened.GetSnapshotAsync()).Grants);

        var retained = (await new FileHomeCoreStateStore(path).ReadAsync()).State!.Records
            .Single(item => item.RecordId == "home.permissions-trust").Payload.GetProperty("Audit")
            .EnumerateArray().Select(item => item.GetRawText()).ToArray();
        Assert.Equal(originalTamperedAudit, retained.Take(originalTamperedAudit.Length));
        Assert.False(await reopened.IsExecutionCurrentAsync(approved.RequestId));
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

    [Fact]
    public async Task Original_source_policy_exception_is_denied_and_audited_within_the_same_custody()
    {
        var service = new HomePermissionTrustService(_stateStore,
            (_, _) => throw new IOException("trusted action catalogue unavailable"), _clock);
        var retained = new List<Task>();
        var scopeCalls = 0;
        var result = await service.AuthorizeImportWithinOriginalSourceAsync(
            Request("boards.read") with { RequestId = "original-policy-fault" },
            action => { scopeCalls++; action(); }, retained.Add, CancellationToken.None);

        Assert.Equal(HomePermissionRequestState.Denied, result.State);
        Assert.Equal("HOME_ACTION_POLICY_UNAVAILABLE", result.Code);
        Assert.True(scopeCalls > 0);
        Assert.NotEmpty(retained);
        Assert.All(retained, task => Assert.True(task.IsCompletedSuccessfully));
        var snapshot = await service.GetSnapshotAsync();
        Assert.Contains(snapshot.RecentAuditEvents, item => item.RequestId == result.RequestId &&
            item.Kind == HomePermissionAuditKind.RequestReceived);
        Assert.Contains(snapshot.RecentAuditEvents, item => item.RequestId == result.RequestId &&
            item.ResultCode == "HOME_ACTION_POLICY_UNAVAILABLE");
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

    private sealed class FailNextWriteStateStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public bool FailNextPermissionWrite { get; set; }

        public Task<HomeStateReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            inner.ReadAsync(cancellationToken);

        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRecordRevision,
            CancellationToken cancellationToken = default)
        {
            if (FailNextPermissionWrite && record.RecordId == "home.permissions-trust")
            {
                FailNextPermissionWrite = false;
                return Task.FromResult(HomeStateWriteResult.Failed(new HomeCoreFailure(
                    HomeCoreErrorCode.HomeStateConflict, "Injected one-shot permission state write refusal.",
                    record.RecordId, true)));
            }
            return inner.WriteAsync(record, expectedRecordRevision, cancellationToken);
        }
    }
}
