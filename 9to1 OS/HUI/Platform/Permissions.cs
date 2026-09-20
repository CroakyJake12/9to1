namespace CakeOS.Platform;

public enum PermissionRisk
{
    Low = 0,
    Consequential = 1,
    Critical = 2
}

public enum PermissionPolicy
{
    AlwaysAsk = 0,
    AskForConsequentialRisk = 1,
    AllowUnlessCritical = 2
}

public enum PermissionDecisionKind
{
    Allowed = 0,
    Ask = 1
}

public enum GrantSource
{
    User = 0,
    Policy = 1,
    System = 2,
    Migration = 3
}

public sealed record PermissionRequest(
    string SubjectId,
    string Resource,
    string Action,
    PermissionRisk Risk,
    bool RequiresGrant = true);

public sealed record PermissionGrant(
    string SubjectId,
    string Resource,
    string Action,
    DateTimeOffset GrantedAtUtc,
    GrantSource Source = GrantSource.User,
    DateTimeOffset? ExpiresAtUtc = null);

public sealed record PermissionDecision(
    PermissionDecisionKind Kind,
    PermissionRequest Request,
    string Reason);

public sealed record PermissionAuditEvent(
    Guid EventId,
    DateTimeOffset TimestampUtc,
    string SubjectId,
    string Resource,
    string Action,
    PermissionDecisionKind Decision,
    string Reason,
    GrantSource? GrantSource = null);

public interface IPermissionService
{
    event EventHandler<PermissionAuditEvent>? Audited;
    Task<PermissionPolicy> GetPolicyAsync(CancellationToken cancellationToken = default);
    Task SetPolicyAsync(PermissionPolicy policy, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(CancellationToken cancellationToken = default);
    Task GrantAsync(string subjectId, string resource, string action, GrantSource source = GrantSource.User, DateTimeOffset? expiresAtUtc = null, CancellationToken cancellationToken = default);
    Task RevokeAsync(string subjectId, string resource, string action, CancellationToken cancellationToken = default);
    Task<PermissionDecision> EvaluateAsync(PermissionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Single persisted grant authority for platform capabilities. It never invokes an OS authorization backend.</summary>
public sealed class PermissionService(IVersionedSettingsStore settings, Func<DateTimeOffset>? clock = null) : IPermissionService
{
    public const string SettingsKey = "platform.permissions.v1";

    private readonly IVersionedSettingsStore _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public event EventHandler<PermissionAuditEvent>? Audited;

    public async Task<PermissionPolicy> GetPolicyAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken).ConfigureAwait(false)).Policy;

    public async Task SetPolicyAsync(PermissionPolicy policy, CancellationToken cancellationToken = default)
    {
        EnsureDefined(policy, nameof(policy));
        var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
        await SaveAsync(state with { Policy = policy }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(CancellationToken cancellationToken = default) =>
        (await LoadAsync(cancellationToken).ConfigureAwait(false)).Grants.ToArray();

    public async Task GrantAsync(string subjectId, string resource, string action, GrantSource source = GrantSource.User, DateTimeOffset? expiresAtUtc = null, CancellationToken cancellationToken = default)
    {
        ValidateScope(subjectId, resource, action);
        EnsureDefined(source, nameof(source));
        var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var now = _clock();
        if (expiresAtUtc.HasValue && expiresAtUtc.Value <= now)
            throw new ArgumentException("Expiration must be in the future.", nameof(expiresAtUtc));

        var grants = state.Grants
            .Where(grant => !Matches(grant, subjectId, resource, action))
            .Append(new PermissionGrant(subjectId, resource, action, now, source, expiresAtUtc))
            .ToArray();
        await SaveAsync(state with { Grants = grants }, cancellationToken).ConfigureAwait(false);
        
        EmitAudit(new PermissionAuditEvent(
            Guid.NewGuid(), now, subjectId, resource, action,
            PermissionDecisionKind.Allowed, "Granted via API.", source));
    }

    public async Task RevokeAsync(string subjectId, string resource, string action, CancellationToken cancellationToken = default)
    {
        ValidateScope(subjectId, resource, action);
        var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var grants = state.Grants.Where(grant => !Matches(grant, subjectId, resource, action)).ToArray();
        if (grants.Length != state.Grants.Length)
        {
            await SaveAsync(state with { Grants = grants }, cancellationToken).ConfigureAwait(false);
            EmitAudit(new PermissionAuditEvent(
                Guid.NewGuid(), _clock(), subjectId, resource, action,
                PermissionDecisionKind.Ask, "Revoked via API.", null));
        }
    }

    public async Task<PermissionDecision> EvaluateAsync(PermissionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScope(request.SubjectId, request.Resource, request.Action);
        EnsureDefined(request.Risk, nameof(request.Risk));
        var state = await LoadAsync(cancellationToken).ConfigureAwait(false);
        
        var hasGrant = state.Grants.Any(grant => Matches(grant, request.SubjectId, request.Resource, request.Action) && !IsExpired(grant));
        
        if (!request.RequiresGrant || hasGrant)
        {
            var allowedDecision = new PermissionDecision(PermissionDecisionKind.Allowed, request, "Allowed by capability scope.");
            EmitAudit(new PermissionAuditEvent(Guid.NewGuid(), _clock(), request.SubjectId, request.Resource, request.Action, allowedDecision.Kind, allowedDecision.Reason, hasGrant ? GrantSource.User : null));
            return allowedDecision;
        }

        var asks = state.Policy switch
        {
            PermissionPolicy.AlwaysAsk => true,
            PermissionPolicy.AskForConsequentialRisk => request.Risk >= PermissionRisk.Consequential,
            PermissionPolicy.AllowUnlessCritical => request.Risk >= PermissionRisk.Critical,
            _ => throw new InvalidDataException("Persisted permission policy is unsupported.")
        };

        var evaluatedDecision = asks
            ? new PermissionDecision(PermissionDecisionKind.Ask, request, "A scoped grant is required before this capability can run.")
            : new PermissionDecision(PermissionDecisionKind.Allowed, request, "Allowed by the shared permission policy.");

        EmitAudit(new PermissionAuditEvent(Guid.NewGuid(), _clock(), request.SubjectId, request.Resource, request.Action, evaluatedDecision.Kind, evaluatedDecision.Reason, null));
        return evaluatedDecision;
    }

    private async Task<PermissionState> LoadAsync(CancellationToken cancellationToken)
    {
        var stored = await _settings.GetAsync<PermissionState>(SettingsKey, cancellationToken).ConfigureAwait(false);
        var state = stored ?? new PermissionState(PermissionPolicy.AlwaysAsk, []);
        EnsureDefined(state.Policy, nameof(state.Policy));
        foreach (var grant in state.Grants)
            ValidateScope(grant.SubjectId, grant.Resource, grant.Action);
        return state;
    }

    private Task SaveAsync(PermissionState state, CancellationToken cancellationToken) =>
        _settings.SetAsync(SettingsKey, state, cancellationToken);

    private static bool Matches(PermissionGrant grant, string subjectId, string resource, string action) =>
        string.Equals(grant.SubjectId, subjectId, StringComparison.Ordinal) &&
        string.Equals(grant.Resource, resource, StringComparison.Ordinal) &&
        string.Equals(grant.Action, action, StringComparison.Ordinal);

    private static bool IsExpired(PermissionGrant grant) =>
        grant.ExpiresAtUtc.HasValue && grant.ExpiresAtUtc.Value <= DateTimeOffset.UtcNow;

    private static void ValidateScope(string subjectId, string resource, string action)
    {
        PlatformContractValidation.RequireIdentifier(subjectId, nameof(subjectId));
        PlatformContractValidation.RequireIdentifier(resource, nameof(resource));
        PlatformContractValidation.RequireIdentifier(action, nameof(action));
    }

    private static void EnsureDefined<T>(T value, string parameterName) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName, $"{typeof(T).Name} value is unsupported.");
    }

    private void EmitAudit(PermissionAuditEvent auditEvent)
    {
        Audited?.Invoke(this, auditEvent);
    }

    private sealed record PermissionState(PermissionPolicy Policy, PermissionGrant[] Grants);
}