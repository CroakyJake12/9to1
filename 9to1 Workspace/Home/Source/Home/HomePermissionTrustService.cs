using System.Text.Json;
using HavenOS.Home.Core;

namespace HavenOS.Home.PermissionsTrustNotifications;

/// <summary>
/// Home-owned permission decision and trust state for app API calls. The caller cannot select an
/// action risk: the trusted target-action resolver supplies it for every request.
/// </summary>
public sealed class HomePermissionTrustService
{
    private static readonly TimeSpan AcceptAndTrustLifetime = TimeSpan.FromDays(30);
    private const int AuditPageSize = 100;
    private const string StateRecordId = "home.permissions-trust";
    private const string StateRecordType = "home.permissions-trust";
    private const int StateSchemaVersion = 1;
    private readonly IHomeCoreStateStore _stateStore;
    private readonly Func<string, string, HomePermissionActionPolicy?> _resolvePolicy;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<SessionApproval> _sessionApprovals = [];

    public HomePermissionTrustService(
        IHomeCoreStateStore stateStore,
        Func<string, string, HomePermissionActionPolicy?> resolveTargetActionPolicy,
        TimeProvider? timeProvider = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _resolvePolicy = resolveTargetActionPolicy ?? throw new ArgumentNullException(nameof(resolveTargetActionPolicy));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<HomePermissionAuthorization> AuthorizeAsync(
        HomePermissionRequestSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        HomePermissionCallerIdentity caller;
        HomePermissionScope scope;
        try
        {
            caller = submission.Caller.Validate();
            scope = submission.Scope.Validate();
            if (string.IsNullOrWhiteSpace(submission.SessionId))
                throw new ArgumentException("A broker-issued session identity is required.", nameof(submission.SessionId));
        }
        catch (ArgumentException exception)
        {
            return new HomePermissionAuthorization(
                HomePermissionRequestState.Denied, "HOME_PERMISSION_REQUEST_INVALID", exception.Message,
                submission.RequestId ?? NewId(), null);
        }

        HomePermissionActionPolicy? policy;
        var policyResolutionFailed = false;
        try { policy = _resolvePolicy(scope.TargetAppId, scope.ActionName); }
        catch { policy = null; policyResolutionFailed = true; }
        var now = _timeProvider.GetUtcNow();
        var requestId = string.IsNullOrWhiteSpace(submission.RequestId) ? NewId() : submission.RequestId.Trim();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (state.Requests.Any(item => item.RequestId == requestId))
                return new HomePermissionAuthorization(HomePermissionRequestState.Denied, "HOME_REQUEST_ID_CONFLICT",
                    "That request identity has already been used.", requestId, null);
            var request = new HomePermissionRequest(
                requestId, caller, submission.SessionId.Trim(), scope,
                policy ?? new HomePermissionActionPolicy(HomePermissionRisk.High, false, false, true),
                submission.Impact ?? HomePermissionImpactPreview.Unknown, now,
                HomePermissionRequestState.PendingApproval, false, null, null, null);
            state.Requests.Add(request);
            AddAudit(state, request, HomePermissionAuditKind.RequestReceived, null, null, null, now);

            if (policy is null)
                return await DenyAndSaveAsync(state, request,
                    policyResolutionFailed ? "HOME_ACTION_POLICY_UNAVAILABLE" : "HOME_ACTION_NOT_REGISTERED",
                    policyResolutionFailed
                        ? "Home could not verify this action's trusted risk policy. The action remains blocked."
                        : "The target app has not registered this action with Home's trusted action catalogue.",
                    now, cancellationToken).ConfigureAwait(false);

            if (state.BlockedCallerIds.Contains(caller.CallerId, StringComparer.Ordinal))
            {
                request = request with
                {
                    State = HomePermissionRequestState.Blocked,
                    ResultCode = "HOME_CALLER_BLOCKED",
                    ResultMessage = "This caller is blocked in Home Settings.",
                };
                ReplaceRequest(state, request);
                AddAudit(state, request, HomePermissionAuditKind.DecisionMade, HomePermissionRequestState.Blocked,
                    request.ResultCode, request.ResultMessage, now);
                await SaveAsync(state, cancellationToken).ConfigureAwait(false);
                return Authorization(request);
            }

            await ExpireGrantsAsync(state, now, cancellationToken).ConfigureAwait(false);
            var grant = FindApplicableGrant(state, request, now);
            if (grant is not null)
            {
                grant = grant with { LastUsedAt = now };
                if (grant.TrustLevel == HomeTrustLevel.TemporaryAlwaysTrust && grant.RemainingActions is { } actions)
                    grant = grant with { RemainingActions = Math.Max(0, actions - 1) };
                ReplaceGrant(state, grant);
                request = request with
                {
                    State = HomePermissionRequestState.Approved,
                    AppliedTrustLevel = grant.TrustLevel,
                    AppliedGrantId = grant.GrantId,
                    ResultCode = "HOME_PERMISSION_GRANTED_BY_TRUST",
                    ResultMessage = "An active trust grant covers this exact caller, action and scope.",
                };
                ReplaceRequest(state, request);
                await SaveAsync(state, cancellationToken).ConfigureAwait(false);
                return Authorization(request);
            }

            if (policy.RequiresPerActionApproval || policy.Risk != HomePermissionRisk.Routine)
            {
                _sessionApprovals.RemoveAll(item => item.CallerId == caller.CallerId &&
                    ScopeEquals(item.Scope, scope) && item.SessionId == submission.SessionId);
            }
            else if (_sessionApprovals.Any(item => item.CallerId == caller.CallerId &&
                item.SessionId == submission.SessionId.Trim() && ScopeEquals(item.Scope, scope)))
            {
                request = request with
                {
                    State = HomePermissionRequestState.Approved,
                    AppliedTrustLevel = HomeTrustLevel.Session,
                    ResultCode = "HOME_PERMISSION_GRANTED_FOR_SESSION",
                    ResultMessage = "This exact action and scope was approved for the current session.",
                };
                ReplaceRequest(state, request);
                await SaveAsync(state, cancellationToken).ConfigureAwait(false);
                return Authorization(request);
            }

            request = request with
            {
                ResultCode = "HOME_PERMISSION_REQUIRED",
                ResultMessage = "Home approval is required before the target app action can execute.",
            };
            ReplaceRequest(state, request);
            AddAudit(state, request, HomePermissionAuditKind.ApprovalPromptShown, request.State,
                request.ResultCode, request.ResultMessage, now);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return Authorization(request);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HomePermissionOperationResult> MarkAlwaysTrustWarningShownAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId)) return Failure("HOME_REQUEST_ID_INVALID", "A request ID is required.");
        var now = _timeProvider.GetUtcNow();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var request = FindPending(state, requestId);
            if (request is null) return Failure("HOME_REQUEST_NOT_PENDING", "The permission request is no longer awaiting approval.");
            request = request with { AlwaysTrustWarningShown = true };
            ReplaceRequest(state, request);
            AddAudit(state, request, HomePermissionAuditKind.ApprovalPromptShown, request.State,
                "HOME_ALWAYS_TRUST_WARNING_SHOWN", "The prominent Always Trust warning was displayed.", now);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return Success("HOME_ALWAYS_TRUST_WARNING_SHOWN", "The Always Trust warning is recorded.");
        }
        finally { _gate.Release(); }
    }

    public async Task<HomePermissionOperationResult> DecideAsync(
        string requestId,
        HomeApprovalChoice choice,
        bool userConfirmedAlwaysTrustWarning = false,
        HomeTrustGrantOptions? temporaryOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId)) return Failure("HOME_REQUEST_ID_INVALID", "A request ID is required.");
        var now = _timeProvider.GetUtcNow();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var request = FindPending(state, requestId);
            if (request is null) return Failure("HOME_REQUEST_NOT_PENDING", "The permission request is no longer awaiting approval.");

            if (choice == HomeApprovalChoice.AcceptAndAlwaysTrust &&
                (!request.AlwaysTrustWarningShown || !userConfirmedAlwaysTrustWarning))
                return Failure("HOME_ALWAYS_TRUST_WARNING_REQUIRED", "Always Trust requires the warning step and explicit confirmation.");

            if (choice == HomeApprovalChoice.GrantTemporaryTrustedAccess)
            {
                if (!request.AlwaysTrustWarningShown || !userConfirmedAlwaysTrustWarning)
                    return Failure("HOME_ALWAYS_TRUST_WARNING_REQUIRED", "Temporary trusted access requires the warning step and explicit confirmation.");
                try { temporaryOptions = (temporaryOptions ?? throw new ArgumentException("Temporary access limits are required.")).Validate(); }
                catch (ArgumentException exception) { return Failure("HOME_TEMPORARY_TRUST_OPTIONS_INVALID", exception.Message); }
            }

            switch (choice)
            {
                case HomeApprovalChoice.Decline:
                    request = FinishDecision(request, HomePermissionRequestState.Denied,
                        "HOME_PERMISSION_DENIED", "The user declined this permission request.", null);
                    break;
                case HomeApprovalChoice.DeclineAndBlock:
                    if (!state.BlockedCallerIds.Contains(request.Caller.CallerId, StringComparer.Ordinal))
                        state.BlockedCallerIds.Add(request.Caller.CallerId);
                    AddAudit(state, request, HomePermissionAuditKind.CallerBlocked, HomePermissionRequestState.Blocked,
                        "HOME_CALLER_BLOCKED", "The user blocked this caller.", now);
                    request = FinishDecision(request, HomePermissionRequestState.Blocked,
                        "HOME_CALLER_BLOCKED", "The request was declined and the caller was blocked.", null);
                    break;
                case HomeApprovalChoice.Accept:
                    _sessionApprovals.Add(new SessionApproval(request.Caller.CallerId, request.SessionId, request.Scope));
                    request = FinishDecision(request, HomePermissionRequestState.Approved,
                        "HOME_PERMISSION_ACCEPTED", "This exact action and scope was approved for the current session.", HomeTrustLevel.Session);
                    break;
                case HomeApprovalChoice.AcceptAndTrust:
                    if (request.Policy.RequiresPerActionApproval || request.Policy.Risk != HomePermissionRisk.Routine)
                        return Failure("HOME_TRUST_SCOPE_REQUIRES_EXPLICIT_APPROVAL", "This elevated action requires explicit approval each time unless Always Trust explicitly covers it.");
                    if (string.IsNullOrWhiteSpace(request.Caller.IdentityVersion))
                        return Failure("HOME_CALLER_IDENTITY_VERSION_REQUIRED", "A tamper-evident caller identity version is required before persistent trust can be granted.");
                    var trustedGrant = CreateGrant(request, HomeTrustLevel.AcceptAndTrust, now,
                        now + AcceptAndTrustLifetime, null, null);
                    state.Grants.Add(trustedGrant);
                    AddAudit(state, request, HomePermissionAuditKind.TrustGranted, HomePermissionRequestState.Approved,
                        "HOME_ACCEPT_AND_TRUST_GRANTED", "A 30-day trust grant was created for the requested scope.", now,
                        HomeTrustLevel.AcceptAndTrust);
                    request = FinishDecision(request, HomePermissionRequestState.Approved,
                        "HOME_ACCEPT_AND_TRUST_GRANTED", "The requested ordinary scope was approved and trusted for 30 days.", HomeTrustLevel.AcceptAndTrust);
                    break;
                case HomeApprovalChoice.AcceptAndAlwaysTrust:
                    if (string.IsNullOrWhiteSpace(request.Caller.IdentityVersion))
                        return Failure("HOME_CALLER_IDENTITY_VERSION_REQUIRED", "A tamper-evident caller identity version is required before persistent trust can be granted.");
                    state.Grants.Add(CreateGrant(request, HomeTrustLevel.AlwaysTrust, now, null, null, null));
                    AddAudit(state, request, HomePermissionAuditKind.TrustGranted, HomePermissionRequestState.Approved,
                        "HOME_ALWAYS_TRUST_GRANTED", "The user explicitly granted persistent trust for the requested scope.", now,
                        HomeTrustLevel.AlwaysTrust);
                    request = FinishDecision(request, HomePermissionRequestState.Approved,
                        "HOME_ALWAYS_TRUST_GRANTED", "The requested scope was approved with persistent trust.", HomeTrustLevel.AlwaysTrust);
                    break;
                case HomeApprovalChoice.GrantTemporaryTrustedAccess:
                    if (string.IsNullOrWhiteSpace(request.Caller.IdentityVersion))
                        return Failure("HOME_CALLER_IDENTITY_VERSION_REQUIRED", "A tamper-evident caller identity version is required before temporary trust can be granted.");
                    state.Grants.Add(CreateGrant(request, HomeTrustLevel.TemporaryAlwaysTrust, now,
                        temporaryOptions!.Duration is { } duration ? now + duration : null,
                        temporaryOptions.ActionCount, temporaryOptions.Fallback));
                    AddAudit(state, request, HomePermissionAuditKind.TemporaryTrustGranted, HomePermissionRequestState.Approved,
                        "HOME_TEMPORARY_TRUST_GRANTED", "The user granted limited temporary trusted access.", now,
                        HomeTrustLevel.TemporaryAlwaysTrust);
                    request = FinishDecision(request, HomePermissionRequestState.Approved,
                        "HOME_TEMPORARY_TRUST_GRANTED", "The requested scope was approved within the selected temporary limit.", HomeTrustLevel.TemporaryAlwaysTrust);
                    break;
                default:
                    return Failure("HOME_APPROVAL_CHOICE_INVALID", "The selected approval choice is not supported.");
            }

            ReplaceRequest(state, request);
            AddAudit(state, request, HomePermissionAuditKind.DecisionMade, request.State,
                request.ResultCode, request.ResultMessage, now, request.AppliedTrustLevel);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return Success(request.ResultCode!, request.ResultMessage!);
        }
        finally { _gate.Release(); }
    }

    public async Task<HomePermissionOperationResult> RecordExecutionAsync(
        string requestId,
        HomeExecutionOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        try { outcome.Validate(); }
        catch (ArgumentException exception) { return Failure("HOME_EXECUTION_OUTCOME_INVALID", exception.Message); }
        var now = _timeProvider.GetUtcNow();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var index = state.Requests.FindIndex(request => request.RequestId == requestId);
            if (index < 0) return Failure("HOME_PERMISSION_REQUEST_NOT_FOUND", "The audited permission request was not found.");
            var request = state.Requests[index];
            if (request.State is not (HomePermissionRequestState.Approved or HomePermissionRequestState.Executing))
                return Failure("HOME_PERMISSION_NOT_AUTHORIZED", "The target action cannot execute without an approved request.");
            if (request.Policy.RequiresPerActionApproval && request.AppliedTrustLevel == HomeTrustLevel.AlwaysTrust)
                return Failure("HOME_TARGET_POLICY_REQUIRES_CONFIRMATION", "The target action policy requires an explicit approval for this execution.");

            if (outcome.State == HomePermissionRequestState.Executing)
            {
                request = request with { State = HomePermissionRequestState.Executing };
                ReplaceRequest(state, request);
                AddAudit(state, request, HomePermissionAuditKind.ExecutionStarted, request.State,
                    outcome.Code, outcome.Message, now, request.AppliedTrustLevel, outcome.AffectedObjects);
            }
            else
            {
                request = request with
                {
                    State = outcome.State,
                    ResultCode = outcome.Code,
                    ResultMessage = outcome.Message,
                };
                ReplaceRequest(state, request);
                AddAudit(state, request, HomePermissionAuditKind.ExecutionCompleted, outcome.State,
                    outcome.Code, outcome.Message, now, request.AppliedTrustLevel, outcome.AffectedObjects);
                if (request.AppliedGrantId is not null && request.AppliedTrustLevel == HomeTrustLevel.TemporaryAlwaysTrust &&
                    state.Grants.FirstOrDefault(grant => grant.GrantId == request.AppliedGrantId) is { IsRevoked: false, RemainingActions: 0 } exhaustedGrant)
                    await ExpireTemporaryGrantAsync(state, exhaustedGrant, now, cancellationToken).ConfigureAwait(false);
            }
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return Success("HOME_EXECUTION_AUDITED", "The target execution state was added to the Home audit trail.");
        }
        finally { _gate.Release(); }
    }

    /// <summary>Rechecks trust revocation and expiry at the dispatch boundary before the target runs.</summary>
    public async Task<HomePermissionAuthorization> BeginExecutionAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return new HomePermissionAuthorization(HomePermissionRequestState.Denied, "HOME_REQUEST_ID_INVALID",
                "A permission request ID is required.", string.Empty, null);
        var now = _timeProvider.GetUtcNow();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            await ExpireGrantsAsync(state, now, cancellationToken).ConfigureAwait(false);
            var requestIndex = state.Requests.FindLastIndex(item => item.RequestId == requestId);
            if (requestIndex < 0)
                return new HomePermissionAuthorization(HomePermissionRequestState.Denied,
                    "HOME_PERMISSION_REQUEST_NOT_FOUND", "The permission request was not found.", requestId, null);
            var request = state.Requests[requestIndex];
            if (request.State != HomePermissionRequestState.Approved)
                return new HomePermissionAuthorization(request.State, "HOME_PERMISSION_NOT_AUTHORIZED",
                    "Only an approved request may be dispatched.", requestId, request.AppliedTrustLevel);
            if (state.BlockedCallerIds.Contains(request.Caller.CallerId, StringComparer.Ordinal))
                return new HomePermissionAuthorization(HomePermissionRequestState.Blocked, "HOME_CALLER_BLOCKED",
                    "The caller was blocked before target dispatch.", requestId, null);
            if (request.AppliedTrustLevel is HomeTrustLevel.AcceptAndTrust or HomeTrustLevel.AlwaysTrust or HomeTrustLevel.TemporaryAlwaysTrust)
            {
                var activeGrant = state.Grants.Any(grant => !grant.IsRevoked &&
                    grant.Caller.CallerId == request.Caller.CallerId &&
                    grant.Caller.IdentityVersion == request.Caller.IdentityVersion && grant.GrantId == request.AppliedGrantId &&
                    ScopeEquals(grant.Scope, request.Scope) && grant.TrustLevel == request.AppliedTrustLevel &&
                    (grant.ExpiresAt is null || grant.ExpiresAt > now) &&
                    (grant.RemainingActions is null || grant.RemainingActions > 0 ||
                        grant.TrustLevel == HomeTrustLevel.TemporaryAlwaysTrust && grant.RemainingActions == 0));
                if (!activeGrant)
                {
                    var pending = request with
                    {
                        State = HomePermissionRequestState.PendingApproval,
                        AppliedTrustLevel = null,
                        ResultCode = "HOME_PERMISSION_REQUIRED",
                        ResultMessage = "The trust grant changed before dispatch; request approval again.",
                    };
                    ReplaceRequest(state, pending);
                    AddAudit(state, pending, HomePermissionAuditKind.DecisionMade, pending.State,
                        pending.ResultCode, pending.ResultMessage, now);
                    await SaveAsync(state, cancellationToken).ConfigureAwait(false);
                    return Authorization(pending);
                }
            }

            var executing = request with
            {
                State = HomePermissionRequestState.Executing,
                ResultCode = "HOME_EXECUTION_STARTED",
                ResultMessage = "The target app action has begun.",
            };
            ReplaceRequest(state, executing);
            AddAudit(state, executing, HomePermissionAuditKind.ExecutionStarted, executing.State,
                executing.ResultCode, executing.ResultMessage, now, executing.AppliedTrustLevel);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return Authorization(executing);
        }
        finally { _gate.Release(); }
    }

    public async Task<HomePermissionOperationResult> NarrowGrantAsync(
        string grantId,
        HomePermissionScope narrowedScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(narrowedScope);
        HomePermissionScope scope;
        try { scope = narrowedScope.Validate(); }
        catch (ArgumentException exception) { return Failure("HOME_PERMISSION_SCOPE_INVALID", exception.Message); }
        var now = _timeProvider.GetUtcNow();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var index = state.Grants.FindIndex(grant => grant.GrantId == grantId && !grant.IsRevoked);
            if (index < 0) return Failure("HOME_GRANT_NOT_FOUND", "An active grant with that identity was not found.");
            var grant = state.Grants[index];
            if (!IsNarrowerOrEqual(grant.Scope, scope))
                return Failure("HOME_GRANT_SCOPE_WIDENING_DENIED", "Grant editing can remove access but cannot add actions, apps or objects.");
            grant = grant with { Scope = scope };
            state.Grants[index] = grant;
            AddGrantAudit(state, grant, HomePermissionAuditKind.TrustNarrowed,
                "HOME_GRANT_NARROWED", "The user narrowed this grant's target scope.", now);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return Success("HOME_GRANT_NARROWED", "The grant was narrowed; future requests are checked against the updated scope.");
        }
        finally { _gate.Release(); }
    }

    public async Task<HomePermissionOperationResult> RevokeGrantAsync(
        string grantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(grantId)) return Failure("HOME_GRANT_ID_INVALID", "A grant ID is required.");
        var now = _timeProvider.GetUtcNow();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var index = state.Grants.FindIndex(grant => grant.GrantId == grantId && !grant.IsRevoked);
            if (index < 0) return Failure("HOME_GRANT_NOT_FOUND", "An active grant with that identity was not found.");
            var grant = state.Grants[index] with { IsRevoked = true };
            state.Grants[index] = grant;
            AddGrantAudit(state, grant, HomePermissionAuditKind.TrustRevoked,
                "HOME_GRANT_REVOKED", "The user revoked this trust grant.", now);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return Success("HOME_GRANT_REVOKED", "The grant was revoked before future authorization checks.");
        }
        finally { _gate.Release(); }
    }

    public async Task<HomePermissionOperationResult> BlockCallerAsync(
        string callerId,
        CancellationToken cancellationToken = default) => await SetCallerBlockedAsync(callerId, true, cancellationToken).ConfigureAwait(false);

    public async Task<HomePermissionOperationResult> UnblockCallerAsync(
        string callerId,
        CancellationToken cancellationToken = default) => await SetCallerBlockedAsync(callerId, false, cancellationToken).ConfigureAwait(false);

    public async Task<HomePermissionManagementSnapshot> GetSnapshotAsync(
        int auditOffset = 0,
        int auditPageSize = AuditPageSize,
        CancellationToken cancellationToken = default)
    {
        if (auditOffset < 0) throw new ArgumentOutOfRangeException(nameof(auditOffset));
        if (auditPageSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(auditPageSize));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var now = _timeProvider.GetUtcNow();
            await ExpireGrantsAsync(state, now, cancellationToken).ConfigureAwait(false);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            var audit = state.Audit.OrderByDescending(entry => entry.Timestamp).ThenByDescending(entry => entry.AuditId)
                .Skip(auditOffset).Take(auditPageSize + 1).ToArray();
            return new HomePermissionManagementSnapshot(
                state.Requests.Where(request => request.State == HomePermissionRequestState.PendingApproval)
                    .OrderBy(request => request.RequestedAt).ToArray(),
                state.Grants.OrderBy(grant => grant.Caller.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(grant => grant.CreatedAt).ToArray(),
                state.BlockedCallerIds.Order(StringComparer.Ordinal).ToArray(),
                audit.Take(auditPageSize).ToArray(),
                audit.Length > auditPageSize ? (auditOffset + auditPageSize).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
        }
        finally { _gate.Release(); }
    }

    private async Task<HomePermissionOperationResult> SetCallerBlockedAsync(
        string callerId,
        bool blocked,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(callerId)) return Failure("HOME_CALLER_ID_INVALID", "A stable caller ID is required.");
        callerId = callerId.Trim();
        var now = _timeProvider.GetUtcNow();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var wasBlocked = state.BlockedCallerIds.Contains(callerId, StringComparer.Ordinal);
            if (blocked == wasBlocked)
                return Success(blocked ? "HOME_CALLER_ALREADY_BLOCKED" : "HOME_CALLER_ALREADY_UNBLOCKED",
                    blocked ? "The caller is already blocked." : "The caller is already unblocked.");
            if (blocked) state.BlockedCallerIds.Add(callerId);
            else state.BlockedCallerIds.RemoveAll(item => item == callerId);
            var caller = state.Requests.LastOrDefault(item => item.Caller.CallerId == callerId)?.Caller ??
                new HomePermissionCallerIdentity(callerId, callerId, null, null, false);
            AddCallerAudit(state, caller, blocked ? HomePermissionAuditKind.CallerBlocked : HomePermissionAuditKind.CallerUnblocked,
                blocked ? "HOME_CALLER_BLOCKED" : "HOME_CALLER_UNBLOCKED",
                blocked ? "The user blocked this caller." : "The user unblocked this caller.", now);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return Success(blocked ? "HOME_CALLER_BLOCKED" : "HOME_CALLER_UNBLOCKED",
                blocked ? "The caller is blocked from future API permission requests." : "The caller may request permissions again.");
        }
        finally { _gate.Release(); }
    }

    private async Task ExpireGrantsAsync(PersistedState state, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var grant in state.Grants.Where(item => !item.IsRevoked &&
                     item.ExpiresAt <= now).ToArray())
            await ExpireTemporaryGrantAsync(state, grant, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExpireTemporaryGrantAsync(
        PersistedState state,
        HomePermissionGrant grant,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (grant.IsRevoked) return;
        var expired = grant with { IsRevoked = true, RemainingActions = 0 };
        ReplaceGrant(state, expired);
        AddGrantAudit(state, expired, HomePermissionAuditKind.TrustExpired,
            "HOME_TRUST_EXPIRED", "The temporary trust limit ended.", now);
        if (grant.TrustLevel == HomeTrustLevel.TemporaryAlwaysTrust &&
            grant.TemporaryFallback == HomeTemporaryGrantFallback.AcceptAndTrust)
        {
            var fallback = new HomePermissionGrant(
                NewId(), grant.Caller, grant.Scope, HomeTrustLevel.AcceptAndTrust,
                now, now + AcceptAndTrustLifetime, null, null, grant.LastUsedAt, false);
            state.Grants.Add(fallback);
            AddGrantAudit(state, fallback, HomePermissionAuditKind.TrustGranted,
                "HOME_TEMPORARY_TRUST_ROLLED_DOWN", "Temporary trust ended and rolled down to a 30-day ordinary trust grant.", now);
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static HomePermissionGrant? FindApplicableGrant(PersistedState state, HomePermissionRequest request, DateTimeOffset now)
    {
        if (request.Policy.RequiresPerActionApproval) return null;
        return state.Grants
            .Where(grant => !grant.IsRevoked && grant.Caller.CallerId == request.Caller.CallerId &&
                grant.Caller.IdentityVersion == request.Caller.IdentityVersion &&
                ScopeEquals(grant.Scope, request.Scope) && (grant.ExpiresAt is null || grant.ExpiresAt > now) &&
                (grant.RemainingActions is null || grant.RemainingActions > 0))
            .Where(grant => request.Policy.Risk == HomePermissionRisk.Routine || grant.TrustLevel is HomeTrustLevel.AlwaysTrust or HomeTrustLevel.TemporaryAlwaysTrust)
            .OrderByDescending(grant => grant.TrustLevel)
            .ThenByDescending(grant => grant.CreatedAt)
            .FirstOrDefault();
    }

    private async Task<HomePermissionAuthorization> DenyAndSaveAsync(
        PersistedState state,
        HomePermissionRequest request,
        string code,
        string message,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        request = request with { State = HomePermissionRequestState.Denied, ResultCode = code, ResultMessage = message };
        ReplaceRequest(state, request);
        AddAudit(state, request, HomePermissionAuditKind.DecisionMade, request.State, code, message, now);
        await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        return Authorization(request);
    }

    private static HomePermissionRequest? FindPending(PersistedState state, string requestId) =>
        state.Requests.LastOrDefault(request => request.RequestId == requestId && request.State == HomePermissionRequestState.PendingApproval);

    private static HomePermissionRequest FinishDecision(
        HomePermissionRequest request,
        HomePermissionRequestState state,
        string code,
        string message,
        HomeTrustLevel? trust) => request with
        {
            State = state,
            AppliedTrustLevel = trust,
            ResultCode = code,
            ResultMessage = message,
        };

    private static HomePermissionGrant CreateGrant(
        HomePermissionRequest request,
        HomeTrustLevel trustLevel,
        DateTimeOffset now,
        DateTimeOffset? expiresAt,
        int? remainingActions,
        HomeTemporaryGrantFallback? fallback) => new(
            NewId(), request.Caller, request.Scope, trustLevel, now, expiresAt, remainingActions, fallback, null, false);

    private static bool IsNarrowerOrEqual(HomePermissionScope oldScope, HomePermissionScope newScope)
    {
        if (oldScope.TargetAppId != newScope.TargetAppId || oldScope.ActionName != newScope.ActionName)
            return false;
        if (ScopeEquals(oldScope, newScope)) return true;
        if (!oldScope.IncludesAllObjects && newScope.IncludesAllObjects) return false;
        if (oldScope.IncludesAllObjects) return true;
        var oldObjects = oldScope.Objects.ToHashSet();
        return newScope.Objects.All(oldObjects.Contains);
    }

    private static void AddAudit(
        PersistedState state,
        HomePermissionRequest request,
        HomePermissionAuditKind kind,
        HomePermissionRequestState? requestState,
        string? code,
        string? message,
        DateTimeOffset now,
        HomeTrustLevel? trust = null,
        IReadOnlyList<HomeObjectReference>? affectedObjects = null) => state.Audit.Add(new HomePermissionAuditEvent(
            NewId(), request.RequestId, request.Caller.CallerId, request.Scope.TargetAppId, request.Scope.ActionName,
            request.Scope, request.Policy.Risk, trust ?? request.AppliedTrustLevel, kind, requestState, now,
            affectedObjects ?? request.Impact.KnownObjects, code, message));

    private static void AddGrantAudit(
        PersistedState state,
        HomePermissionGrant grant,
        HomePermissionAuditKind kind,
        string code,
        string message,
        DateTimeOffset now) => state.Audit.Add(new HomePermissionAuditEvent(
            NewId(), null, grant.Caller.CallerId, grant.Scope.TargetAppId, grant.Scope.ActionName, grant.Scope,
            null, grant.TrustLevel, kind, null, now, grant.Scope.Objects, code, message));

    private static void AddCallerAudit(
        PersistedState state,
        HomePermissionCallerIdentity caller,
        HomePermissionAuditKind kind,
        string code,
        string message,
        DateTimeOffset now) => state.Audit.Add(new HomePermissionAuditEvent(
            NewId(), null, caller.CallerId, null, null, null, null, null, kind, null, now, [], code, message));

    private async Task<PersistedState> LoadAsync(CancellationToken cancellationToken)
    {
        var read = await _stateStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess)
            throw new HomeFeatureStoreException(read.Failure!.Code.ToString(), read.Failure.Message);
        var record = read.State!.Records.SingleOrDefault(item => item.RecordId == StateRecordId);
        if (record is null) return new PersistedState();
        if (record.RecordType != StateRecordType || record.SchemaVersion != StateSchemaVersion)
            throw new HomeFeatureStoreException("HOME_STATE_VERSION_UNSUPPORTED",
                "Saved permission/trust state has an incompatible record type or schema version.");
        try
        {
            var state = record.Payload.Deserialize<PersistedState>()
                ?? throw new HomeFeatureStoreException("HOME_STATE_INVALID", "Saved permission/trust state has no payload.");
            state.RecordRevision = record.Revision;
            if (state.Requests is null || state.Grants is null || state.BlockedCallerIds is null || state.Audit is null)
                throw new HomeFeatureStoreException("HOME_STATE_INVALID", "Saved permission/trust state is missing required collections.");
            return state;
        }
        catch (JsonException exception)
        {
            throw new HomeFeatureStoreException("HOME_STATE_CORRUPT",
                "Saved permission/trust state could not be decoded and was preserved.", exception);
        }
    }

    private async Task SaveAsync(PersistedState state, CancellationToken cancellationToken)
    {
        var record = new HomeCoreStateRecord(
            StateRecordId, StateRecordType, StateSchemaVersion, HomeDataScope.DeviceLocal,
            HomeRecordAuthority.LocalCanonical, state.RecordRevision,
            JsonSerializer.SerializeToElement(state));
        var result = await _stateStore.WriteAsync(record, state.RecordRevision, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
            throw new HomeFeatureStoreException(result.Failure!.Code.ToString(), result.Failure.Message);
        state.RecordRevision = result.State!.Records.Single(item => item.RecordId == StateRecordId).Revision;
    }

    private static void ReplaceRequest(PersistedState state, HomePermissionRequest request)
    {
        var index = state.Requests.FindLastIndex(item => item.RequestId == request.RequestId);
        if (index >= 0) state.Requests[index] = request;
    }

    private static void ReplaceGrant(PersistedState state, HomePermissionGrant grant)
    {
        var index = state.Grants.FindIndex(item => item.GrantId == grant.GrantId);
        if (index >= 0) state.Grants[index] = grant;
    }

    private static HomePermissionAuthorization Authorization(HomePermissionRequest request) => new(
        request.State, request.ResultCode ?? "HOME_PERMISSION_REQUIRED",
        request.ResultMessage ?? "Home approval is required before the target app action can execute.",
        request.RequestId, request.AppliedTrustLevel);

    private static string NewId() => Guid.NewGuid().ToString("N");
    private static HomePermissionOperationResult Success(string code, string message) => new(true, code, message);
    private static HomePermissionOperationResult Failure(string code, string message) => new(false, code, message);

    private static bool ScopeEquals(HomePermissionScope left, HomePermissionScope right) =>
        left.TargetAppId == right.TargetAppId && left.ActionName == right.ActionName &&
        left.IncludesAllObjects == right.IncludesAllObjects && left.Objects.Count == right.Objects.Count &&
        left.Objects.All(right.Objects.Contains);

    private sealed record SessionApproval(string CallerId, string SessionId, HomePermissionScope Scope);

    private sealed class PersistedState
    {
        public PersistedState() { }
        public List<HomePermissionRequest> Requests { get; init; } = [];
        public List<HomePermissionGrant> Grants { get; init; } = [];
        public List<string> BlockedCallerIds { get; init; } = [];
        public List<HomePermissionAuditEvent> Audit { get; init; } = [];
        [System.Text.Json.Serialization.JsonIgnore]
        public long RecordRevision { get; set; }
    }
}
