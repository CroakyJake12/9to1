using System.Text.Json;
using System.Collections.Frozen;

namespace Dulche.Runtime;

public enum CapabilitySourceType { App, Automation, Plugin, Mcp, Agent, Skill, Resource, Native, ComputerUse }
public enum CapabilityReadiness { Executable, ApprovalRequired, PermissionDenied, AccountConnectionRequired, MissingRequiredInput, AppUnavailable, ProviderUnavailable, CapabilityDisabledByPolicy, UnsupportedOnCurrentPlatform }
public enum ResolutionFailureKind { NoMatchingCapability, MatchingCapabilityUnavailable, MissingPrerequisite, PermissionBlocked, PolicyBlocked }

public sealed record RegistryCapability(
    string CapabilityId, CapabilitySourceType SourceType, string OwnerId, string Revision,
    string Name, string ActionId, IReadOnlySet<string> IntentTags,
    IReadOnlySet<string> InputTypes, IReadOnlySet<string> OutputTypes, string ParameterSchemaJson,
    IReadOnlySet<string> PermissionScopes, IReadOnlySet<string> Platforms,
    IReadOnlyList<string> Prerequisites, CapabilityReadiness Readiness,
    bool IsLocal, bool HasSideEffects, bool RequiresApproval,
    IReadOnlyList<string>? IntentAliases = null, string? CanonicalAppId = null,
    int ReliabilityRank = 0, int PreferenceRank = 0);

public sealed record CapabilityRequestContext(
    string CallerId, string Platform, IReadOnlySet<string> PermissionScopes,
    IReadOnlySet<string> AllowedOwnerIds, IReadOnlySet<string> DisabledCapabilityIds,
    IReadOnlySet<string> ExplicitOwnerInvocations, bool AllowRemote,
    string? ComputerUseInvocationId = null, IReadOnlySet<string>? AvailableInputTypes = null);

public sealed record EffectiveCapability(RegistryCapability Capability, CapabilityReadiness State, IReadOnlyList<string> Blockers);
public sealed record EffectiveCapabilitySet(long RegistryRevision, string CallerId, IReadOnlyList<EffectiveCapability> Entries);
public sealed record ResolutionIntent(string IntentTag, JsonElement Arguments, string? CanonicalAppId = null);
public sealed record ResolutionStep(string StepId, string CapabilityId, string ActionId, string OwnerId,
    JsonElement Arguments, IReadOnlySet<string> ExpectedOutputs, IReadOnlyList<string> Dependencies,
    CapabilityReadiness State, string Reason);
public sealed record ResolutionPlan(string ResolutionId, long RegistryRevision, IReadOnlyList<ResolutionStep> Steps,
    bool ImmediatelyExecutable, IReadOnlyList<string> MissingPrerequisites);
public sealed record CapabilityResolution(string ResolutionId, long RegistryRevision,
    IReadOnlyList<ResolutionPlan> Plans, ResolutionFailureKind? Failure,
    IReadOnlyList<EffectiveCapability> EvaluatedCandidates, string Explanation);

/// <summary>Home/package authentication owns registration authority; model text and tool output cannot register entries.</summary>
public interface IToolRegistryAuthority
{
    ValueTask<bool> MayRegisterAsync(string authenticatedRegistrar, string ownerId, CancellationToken cancellationToken);
}

/// <summary>Canonical versioned discovery and planning. This service never executes or grants a capability.</summary>
public sealed class DulcheToolRegistry(IToolRegistryAuthority authority)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RegistryCapability> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string CallerId, CapabilityResolution Resolution)> _resolutions = new(StringComparer.Ordinal);
    private long _revision;
    public long Revision { get { lock (_gate) return _revision; } }

    public async ValueTask<OperationResult<long>> RegisterAsync(string authenticatedRegistrar, RegistryCapability capability, CancellationToken cancellationToken = default)
    {
        if (!await authority.MayRegisterAsync(authenticatedRegistrar, capability.OwnerId, cancellationToken).ConfigureAwait(false))
            return Fail<long>(DulcheErrorCode.PermissionDenied, "The registrar is not authorised for this capability owner.", capability.CapabilityId);
        if (string.IsNullOrWhiteSpace(capability.CapabilityId) || string.IsNullOrWhiteSpace(capability.OwnerId) ||
            string.IsNullOrWhiteSpace(capability.Revision) || string.IsNullOrWhiteSpace(capability.ActionId) || capability.IntentTags.Count == 0)
            return Fail<long>(DulcheErrorCode.InvalidArgument, "Stable identity, owner, revision, action and semantic intent are required.", capability.CapabilityId);
        try { using var schema = JsonDocument.Parse(capability.ParameterSchemaJson); if (schema.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(); }
        catch (JsonException) { return Fail<long>(DulcheErrorCode.InvalidArgument, "Parameter schema must be a JSON object.", capability.CapabilityId); }
        // Copy mutable provider collections so later mutation cannot evade revision invalidation.
        var frozen = capability with {
            IntentTags = capability.IntentTags.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            InputTypes = capability.InputTypes.ToFrozenSet(StringComparer.Ordinal), OutputTypes = capability.OutputTypes.ToFrozenSet(StringComparer.Ordinal),
            PermissionScopes = capability.PermissionScopes.ToFrozenSet(StringComparer.Ordinal), Platforms = capability.Platforms.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            Prerequisites = Array.AsReadOnly(capability.Prerequisites.ToArray()), IntentAliases = capability.IntentAliases is null ? null : Array.AsReadOnly(capability.IntentAliases.ToArray())
        };
        lock (_gate)
        {
            if (_entries.TryGetValue(frozen.CapabilityId, out var existing) && existing.OwnerId != frozen.OwnerId)
                return Fail<long>(DulcheErrorCode.Conflict, "A capability identity cannot change ownership.", frozen.CapabilityId);
            _entries[frozen.CapabilityId] = frozen; _resolutions.Clear(); return OperationResult<long>.Success(++_revision);
        }
    }

    public async ValueTask<OperationResult<long>> RemoveAsync(string authenticatedRegistrar, string capabilityId, CancellationToken cancellationToken = default)
    {
        RegistryCapability? entry; lock (_gate) _entries.TryGetValue(capabilityId, out entry);
        if (entry is null) return Fail<long>(DulcheErrorCode.InvalidArgument, "Capability not found.", capabilityId);
        if (!await authority.MayRegisterAsync(authenticatedRegistrar, entry.OwnerId, cancellationToken).ConfigureAwait(false))
            return Fail<long>(DulcheErrorCode.PermissionDenied, "The registrar is not authorised for this capability owner.", capabilityId);
        lock (_gate) { _entries.Remove(capabilityId); _resolutions.Clear(); return OperationResult<long>.Success(++_revision); }
    }

    public EffectiveCapabilitySet List(CapabilityRequestContext context)
    {
        lock (_gate)
        {
            var entries = _entries.Values.Where(c => context.AllowedOwnerIds.Contains(c.OwnerId)).Where(c => c.SourceType != CapabilitySourceType.ComputerUse || !string.IsNullOrWhiteSpace(context.ComputerUseInvocationId))
                .Select(c => Effective(c, context)).OrderBy(c => c.Capability.CapabilityId, StringComparer.Ordinal).ToArray();
            return new(_revision, context.CallerId, entries);
        }
    }

    public EffectiveCapability? Get(string capabilityId, CapabilityRequestContext context) =>
        List(context).Entries.SingleOrDefault(e => e.Capability.CapabilityId == capabilityId);

    public IReadOnlyList<EffectiveCapability> Search(string intent, CapabilityRequestContext context, int offset = 0, int limit = 50)
    {
        if (offset < 0 || limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        return List(context).Entries.Where(e => Matches(e.Capability, intent)).Skip(offset).Take(limit).ToArray();
    }

    public CapabilityResolution Resolve(IReadOnlyList<ResolutionIntent> intents, CapabilityRequestContext context)
    {
        if (intents.Count == 0) throw new ArgumentException("At least one semantic intent is required.", nameof(intents));
        var set = List(context); var id = Guid.NewGuid().ToString("N");
        var selected = new List<ResolutionStep>(); var evaluated = new List<EffectiveCapability>(); var missing = new List<string>();
        var availableInputs = (context.AvailableInputTypes ?? new HashSet<string>()).ToHashSet(StringComparer.Ordinal);
        foreach (var intent in intents)
        {
            var matches = set.Entries.Where(e => Matches(e.Capability, intent.IntentTag)).ToArray(); evaluated.AddRange(matches);
            var eligible = matches.Where(e => e.State is CapabilityReadiness.Executable or CapabilityReadiness.ApprovalRequired)
                .Where(e => e.Capability.InputTypes.All(availableInputs.Contains))
                .OrderByDescending(e => context.ExplicitOwnerInvocations.Contains(e.Capability.OwnerId))
                .ThenByDescending(e => intent.CanonicalAppId is not null && e.Capability.CanonicalAppId == intent.CanonicalAppId)
                .ThenByDescending(e => e.Capability.IsLocal).ThenBy(e => e.State == CapabilityReadiness.Executable ? 0 : 1)
                .ThenByDescending(e => e.Capability.ReliabilityRank).ThenByDescending(e => e.Capability.PreferenceRank)
                .ThenBy(e => e.Capability.CapabilityId, StringComparer.Ordinal).FirstOrDefault();
            if (eligible is null)
            {
                var reason = matches.Length == 0 ? ResolutionFailureKind.NoMatchingCapability
                    : matches.Any(e => e.State == CapabilityReadiness.PermissionDenied) ? ResolutionFailureKind.PermissionBlocked
                    : matches.Any(e => e.State == CapabilityReadiness.CapabilityDisabledByPolicy) ? ResolutionFailureKind.PolicyBlocked
                    : matches.Any(e => e.State is CapabilityReadiness.AccountConnectionRequired or CapabilityReadiness.MissingRequiredInput ||
                        !e.Capability.InputTypes.All(availableInputs.Contains)) ? ResolutionFailureKind.MissingPrerequisite
                    : ResolutionFailureKind.MatchingCapabilityUnavailable;
                return Remember(new(id, set.RegistryRevision, [], reason, Array.AsReadOnly(evaluated.ToArray()), $"No feasible path for semantic intent '{intent.IntentTag}': {reason}."), context.CallerId);
            }
            var capability = eligible.Capability;
            var dependencies = selected.Where(s => s.ExpectedOutputs.Any(capability.InputTypes.Contains)).Select(s => s.StepId).ToArray();
            var stepId = $"step-{selected.Count + 1}";
            selected.Add(new(stepId, capability.CapabilityId, capability.ActionId, capability.OwnerId, intent.Arguments.Clone(),
                capability.OutputTypes, dependencies, eligible.State, $"Satisfies {intent.IntentTag} via registered semantic capability {capability.CapabilityId}."));
            availableInputs.UnionWith(capability.OutputTypes);
            if (eligible.State == CapabilityReadiness.ApprovalRequired) missing.Add($"{stepId}: approval required");
        }
        var plan = new ResolutionPlan(id, set.RegistryRevision, Array.AsReadOnly(selected.ToArray()), missing.Count == 0, Array.AsReadOnly(missing.ToArray()));
        return Remember(new(id, set.RegistryRevision, Array.AsReadOnly(new[] { plan }), null, Array.AsReadOnly(evaluated.ToArray()), "A typed capability path exists; execution still requires current authorisation and validation."), context.CallerId);
    }

    public CapabilityResolution? ExplainResolution(string resolutionId, CapabilityRequestContext context)
    {
        lock (_gate) return _resolutions.TryGetValue(resolutionId, out var resolution) &&
            resolution.CallerId == context.CallerId &&
            resolution.Resolution.EvaluatedCandidates.All(c => context.AllowedOwnerIds.Contains(c.Capability.OwnerId) &&
                Effective(c.Capability, context).State == c.State) ? resolution.Resolution : null;
    }

    private CapabilityResolution Remember(CapabilityResolution value, string callerId)
    {
        lock (_gate) { if (_revision == value.RegistryRevision) { if (_resolutions.Count >= 256) _resolutions.Remove(_resolutions.Keys.First()); _resolutions[value.ResolutionId] = (callerId, value); } }
        return value;
    }

    private static bool Matches(RegistryCapability capability, string intent) =>
        capability.IntentTags.Contains(intent, StringComparer.OrdinalIgnoreCase) ||
        (capability.IntentAliases?.Any(alias => intent.Contains(alias, StringComparison.OrdinalIgnoreCase)) ?? false);

    private static EffectiveCapability Effective(RegistryCapability capability, CapabilityRequestContext context)
    {
        var state = capability.Readiness; var blockers = new List<string>();
        if (!context.AllowedOwnerIds.Contains(capability.OwnerId) || capability.PermissionScopes.Any(s => !context.PermissionScopes.Contains(s)))
        { state = CapabilityReadiness.PermissionDenied; blockers.Add("Required caller scope is not granted."); }
        else if (context.DisabledCapabilityIds.Contains(capability.CapabilityId) || (!capability.IsLocal && !context.AllowRemote))
        { state = CapabilityReadiness.CapabilityDisabledByPolicy; blockers.Add("Current policy blocks this capability."); }
        else if (!capability.Platforms.Contains(context.Platform, StringComparer.OrdinalIgnoreCase))
        { state = CapabilityReadiness.UnsupportedOnCurrentPlatform; blockers.Add("Unsupported on the current platform."); }
        else if (state == CapabilityReadiness.Executable && capability.RequiresApproval)
        { state = CapabilityReadiness.ApprovalRequired; blockers.Add("Approval required before execution."); }
        if (state != CapabilityReadiness.Executable) blockers.AddRange(capability.Prerequisites);
        return new(capability, state, blockers);
    }

    private static OperationResult<T> Fail<T>(DulcheErrorCode code, string message, string target) => OperationResult<T>.Failure(new(code, message, target, false));
}
