namespace Dulche.Runtime.Agents;

/// <summary>Pure validation of saved upper bounds, shared by owning authoring/runtime consumers.
/// No registry resolution, identity, capability string or successful validation grants permission.</summary>
public static class AgentSavedPolicyValidation
{
    public static AgentFailure? Validate(AgentCapabilityPolicy policy, string target) =>
        ExactSets(policy.AllowedCapabilities, policy.DeniedCapabilities) &&
            UnambiguousPermissions(policy.AllowedCapabilities) && UnambiguousPermissions(policy.DeniedCapabilities) ? null :
            new(AgentFailureCode.InvalidInvocationContext, "Saved capability declarations require at most 128 exact IDs per set.", target);

    public static AgentFailure? Validate(AgentDelegationPolicy policy, string target)
    {
        if (!ExactSets(policy.AllowedAgentIds, policy.AllowedAgentClasses) || policy.MaximumConcurrency < 1 ||
            policy.MaximumDepth < 0 || policy.AllowSubagents && policy.MaximumDepth == 0 || policy.Budget is null)
            return new(AgentFailureCode.InvalidInvocationContext,
                "Saved delegation requires exact IDs, consistent nonnegative limits and a canonical budget.", target);
        return policy.Budget.Validate(target);
    }

    // Permission matching preserves the predecessor's OrdinalIgnoreCase semantics. A saved
    // set containing two spellings of the same permission is refused rather than expanded.
    private static bool UnambiguousPermissions(IReadOnlySet<string> values) =>
        values.Count == values.Distinct(StringComparer.OrdinalIgnoreCase).Count();

    private static bool ExactSets(params IReadOnlySet<string>[] sets) => sets.All(set => set is not null &&
        set.Count <= 128 && set.All(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 256 && id == id.Trim() && !id.Any(char.IsControl)));
}
