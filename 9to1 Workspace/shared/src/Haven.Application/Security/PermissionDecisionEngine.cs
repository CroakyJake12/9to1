using Haven.Core;

namespace Haven.Application;

public enum HavenPermissionPolicy
{
    AlwaysAsk = 0,
    AskWithHighRisk = 1,
    AlwaysAllow = 2
}

public enum PermissionDecisionKind
{
    Allowed = 0,
    Ask = 1,
    Denied = 2
}

public sealed record PermissionDecision(
    PermissionDecisionKind Kind,
    string Scope,
    string Reason);

public interface IPermissionDecisionEngine
{
    HavenPermissionPolicy Policy { get; }
    PermissionDecision Evaluate(string scope, CapabilityRiskClass risk, bool requiresPermission, string reason);
    void SetPolicy(HavenPermissionPolicy policy);
    void Grant(string scope);
    void Revoke(string scope);
    IReadOnlySet<string> Grants { get; }
}

/// <summary>
/// Central risk-based Haven approval policy. A grant is scoped to one
/// capability/action key and never becomes authority for unrelated actions.
/// </summary>
/// <summary>The SAME policy writer gate surrounds a finite native effect. It is not an async lease.</summary>
public interface IPermissionOriginalEffectFence
{
    T RunOriginalEffect<T>(string scope, CapabilityRiskClass risk, bool requiresPermission,
        string reason, Func<T> originalNativeEffect);
}

public sealed class PermissionDecisionEngine : IPermissionDecisionEngine, IPermissionOriginalEffectFence
{
    private readonly object _gate = new();
    private readonly HashSet<string> _grants = new(StringComparer.OrdinalIgnoreCase);
    private HavenPermissionPolicy _policy = HavenPermissionPolicy.AlwaysAsk;
    private bool _originalEffectInProgress;

    public HavenPermissionPolicy Policy
    {
        get { lock (_gate) return _policy; }
    }

    public IReadOnlySet<string> Grants
    {
        get { lock (_gate) return new HashSet<string>(_grants, StringComparer.OrdinalIgnoreCase); }
    }

    public PermissionDecision Evaluate(string scope, CapabilityRiskClass risk, bool requiresPermission, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        lock (_gate)
        {
            if (!requiresPermission || _grants.Contains(scope))
                return new(PermissionDecisionKind.Allowed, scope, reason);
            var ask = _policy switch
            {
                HavenPermissionPolicy.AlwaysAsk => true,
                HavenPermissionPolicy.AskWithHighRisk => risk >= CapabilityRiskClass.Consequential,
                HavenPermissionPolicy.AlwaysAllow => risk >= CapabilityRiskClass.Consequential,
                _ => true
            };
            return ask
                ? new(PermissionDecisionKind.Ask, scope, reason)
                : new(PermissionDecisionKind.Allowed, scope, reason);
        }
    }

    public void SetPolicy(HavenPermissionPolicy policy)
    {
        lock (_gate) { DemandNoOriginalEffectReentry(); _policy = policy; }
    }

    public void Grant(string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        lock (_gate) { DemandNoOriginalEffectReentry(); _grants.Add(scope); }
    }

    public void Revoke(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return;
        lock (_gate) { DemandNoOriginalEffectReentry(); _grants.Remove(scope); }
    }

    public T RunOriginalEffect<T>(string scope, CapabilityRiskClass risk, bool requiresPermission,
        string reason, Func<T> originalNativeEffect)
    {
        ArgumentNullException.ThrowIfNull(originalNativeEffect);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        if (!Enum.IsDefined(risk)) throw new ArgumentOutOfRangeException(nameof(risk));
        lock (_gate)
        {
            DemandNoOriginalEffectReentry();
            // Reevaluate exact scope/risk under the existing Grant/Revoke/SetPolicy writer gate.
            // Only native Move/Delete/Start is permitted inside; never await or call an observer.
            if (Evaluate(scope, risk, requiresPermission, reason).Kind != PermissionDecisionKind.Allowed)
                throw new UnauthorizedAccessException("Current central permission policy refuses the original effect.");
            _originalEffectInProgress = true;
            try { return originalNativeEffect(); }
            finally { _originalEffectInProgress = false; }
        }
    }

    private void DemandNoOriginalEffectReentry()
    {
        if (_originalEffectInProgress)
            throw new InvalidOperationException("Permission mutation/effect reentry is forbidden inside an original native effect.");
    }
}
