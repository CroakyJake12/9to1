namespace HavenOS.Home;

public enum HomePermissionRisk
{
    Routine,
    Elevated,
    High,
}

public enum HomeTrustLevel
{
    Session,
    AcceptAndTrust,
    TemporaryAlwaysTrust,
    AlwaysTrust,
}

public enum HomePermissionRequestState
{
    PendingApproval,
    Approved,
    Denied,
    Blocked,
    Executing,
    Succeeded,
    Failed,
    Cancelled,
    PartiallyCompleted,
}

public enum HomeApprovalChoice
{
    Decline,
    DeclineAndBlock,
    Accept,
    AcceptAndTrust,
    AcceptAndAlwaysTrust,
    GrantTemporaryTrustedAccess,
}

public enum HomeTemporaryGrantFallback
{
    ManualApproval,
    AcceptAndTrust,
}

public enum HomePermissionAuditKind
{
    RequestReceived,
    ApprovalPromptShown,
    DecisionMade,
    TrustGranted,
    TemporaryTrustGranted,
    TrustExpired,
    TrustRevoked,
    TrustNarrowed,
    CallerBlocked,
    CallerUnblocked,
    ExecutionStarted,
    ExecutionCompleted,
}

public sealed record HomeCallerIdentity(
    string CallerId,
    string DisplayName,
    string? Origin,
    string? IdentityVersion,
    bool IsVerified)
{
    public HomeCallerIdentity Validate()
    {
        if (string.IsNullOrWhiteSpace(CallerId)) throw new ArgumentException("Caller identity is required.", nameof(CallerId));
        if (string.IsNullOrWhiteSpace(DisplayName)) throw new ArgumentException("Caller display name is required.", nameof(DisplayName));
        return this with
        {
            CallerId = CallerId.Trim(),
            DisplayName = DisplayName.Trim(),
            Origin = string.IsNullOrWhiteSpace(Origin) ? null : Origin.Trim(),
            IdentityVersion = string.IsNullOrWhiteSpace(IdentityVersion) ? null : IdentityVersion.Trim(),
        };
    }
}

public sealed record HomeObjectReference(string ObjectType, string ObjectId);

/// <summary>An exact action and object scope; broad access must be explicitly represented.</summary>
public sealed record HomePermissionScope(
    string TargetAppId,
    string ActionName,
    IReadOnlyList<HomeObjectReference> Objects,
    bool IncludesAllObjects = false)
{
    public HomePermissionScope Validate()
    {
        if (string.IsNullOrWhiteSpace(TargetAppId)) throw new ArgumentException("Target app identity is required.", nameof(TargetAppId));
        if (string.IsNullOrWhiteSpace(ActionName)) throw new ArgumentException("Canonical action name is required.", nameof(ActionName));
        ArgumentNullException.ThrowIfNull(Objects);
        if (Objects.Any(item => item is null || string.IsNullOrWhiteSpace(item.ObjectType) || string.IsNullOrWhiteSpace(item.ObjectId)))
            throw new ArgumentException("Object references require stable object type and identifier values.", nameof(Objects));
        var normalized = Objects
            .Select(item => item with { ObjectType = item.ObjectType.Trim(), ObjectId = item.ObjectId.Trim() })
            .Distinct()
            .OrderBy(item => item.ObjectType, StringComparer.Ordinal)
            .ThenBy(item => item.ObjectId, StringComparer.Ordinal)
            .ToArray();
        if (IncludesAllObjects && normalized.Length > 0)
            throw new ArgumentException("An all-objects scope must not also list specific object identifiers.", nameof(Objects));
        return this with { TargetAppId = TargetAppId.Trim(), ActionName = ActionName.Trim(), Objects = normalized };
    }
}

public sealed record HomePermissionImpactPreview(
    IReadOnlyList<string> AffectedObjectTypes,
    int? AffectedObjectCount,
    IReadOnlyList<HomeObjectReference> KnownObjects,
    bool IsUnknown)
{
    public static HomePermissionImpactPreview Unknown { get; } = new([], null, [], true);
}

/// <summary>Declared by the target app's trusted action catalogue, never by a permission caller.</summary>
public sealed record HomePermissionActionPolicy(
    HomePermissionRisk Risk,
    bool IsReversible,
    bool HasExternalSideEffects,
    bool RequiresPerActionApproval = false);

public sealed record HomePermissionRequestSubmission(
    string? RequestId,
    HomeCallerIdentity Caller,
    string SessionId,
    HomePermissionScope Scope,
    HomePermissionImpactPreview Impact);

public sealed record HomePermissionRequest(
    string RequestId,
    HomeCallerIdentity Caller,
    string SessionId,
    HomePermissionScope Scope,
    HomePermissionActionPolicy Policy,
    HomePermissionImpactPreview Impact,
    DateTimeOffset RequestedAt,
    HomePermissionRequestState State,
    bool AlwaysTrustWarningShown,
    HomeTrustLevel? AppliedTrustLevel,
    string? ResultCode,
    string? ResultMessage);

public sealed record HomePermissionGrant(
    string GrantId,
    HomeCallerIdentity Caller,
    HomePermissionScope Scope,
    HomeTrustLevel TrustLevel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    int? RemainingActions,
    HomeTemporaryGrantFallback? TemporaryFallback,
    DateTimeOffset? LastUsedAt,
    bool IsRevoked);

public sealed record HomePermissionAuditEvent(
    string AuditId,
    string? RequestId,
    string CallerId,
    string? TargetAppId,
    string? ActionName,
    HomePermissionScope? Scope,
    HomePermissionRisk? Risk,
    HomeTrustLevel? TrustLevel,
    HomePermissionAuditKind Kind,
    HomePermissionRequestState? RequestState,
    DateTimeOffset Timestamp,
    IReadOnlyList<HomeObjectReference> AffectedObjects,
    string? ResultCode,
    string? ResultMessage);

public sealed record HomePermissionAuthorization(
    HomePermissionRequestState State,
    string Code,
    string Message,
    string RequestId,
    HomeTrustLevel? TrustLevel)
{
    public bool IsAllowed => State is HomePermissionRequestState.Approved or HomePermissionRequestState.Executing;
}

public sealed record HomeTrustGrantOptions(
    TimeSpan? Duration,
    int? ActionCount,
    HomeTemporaryGrantFallback Fallback)
{
    public HomeTrustGrantOptions Validate()
    {
        var hasDuration = Duration is { } duration && duration > TimeSpan.Zero;
        var hasCount = ActionCount is > 0;
        if (hasDuration == hasCount)
            throw new ArgumentException("Choose exactly one positive duration or action count.");
        return this;
    }
}

public sealed record HomePermissionManagementSnapshot(
    IReadOnlyList<HomePermissionRequest> PendingRequests,
    IReadOnlyList<HomePermissionGrant> Grants,
    IReadOnlyList<string> BlockedCallerIds,
    IReadOnlyList<HomePermissionAuditEvent> RecentAuditEvents,
    string? ContinuationToken);

public sealed record HomeExecutionOutcome(
    HomePermissionRequestState State,
    string Code,
    string Message,
    IReadOnlyList<HomeObjectReference> AffectedObjects)
{
    public HomeExecutionOutcome Validate()
    {
        if (State is not (HomePermissionRequestState.Succeeded or HomePermissionRequestState.Failed or
            HomePermissionRequestState.Cancelled or HomePermissionRequestState.PartiallyCompleted))
            throw new ArgumentOutOfRangeException(nameof(State), "Execution outcome must be a terminal execution state.");
        if (string.IsNullOrWhiteSpace(Code)) throw new ArgumentException("A stable result code is required.", nameof(Code));
        ArgumentNullException.ThrowIfNull(AffectedObjects);
        return this;
    }
}

public sealed record HomePermissionOperationResult(bool Succeeded, string Code, string Message);
