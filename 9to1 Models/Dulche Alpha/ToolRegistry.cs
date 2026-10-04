using System.Text.Json;
using System.Collections.Frozen;

namespace Dulche.Runtime;

public enum CapabilitySourceType { App, Automation, Plugin, Mcp, Agent, Skill, Resource, Native, ComputerUse }
public enum CapabilityReadiness { Executable, ApprovalRequired, PermissionDenied, AccountConnectionRequired, MissingRequiredInput, AppUnavailable, ProviderUnavailable, CapabilityDisabledByPolicy, UnsupportedOnCurrentPlatform }
public enum ResolutionFailureKind { NoMatchingCapability, MatchingCapabilityUnavailable, MissingPrerequisite, PermissionBlocked, PolicyBlocked, PlanningLimitExceeded }

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
public sealed record ResolutionIntent(string IntentTag, JsonElement Arguments, string? CanonicalAppId = null,
    IReadOnlyDictionary<string, JsonElement>? PrerequisiteArguments = null);
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
        if (intents.Count > 32) throw new ArgumentException("Resolution is bounded to 32 semantic intents.", nameof(intents));
        var evaluated = new List<EffectiveCapability>();
        var paths = new List<List<ResolutionStep>> { new() };
        var budget = new PlanningBudget();
        foreach (var intent in intents)
        {
            var matches = Rank(set.Entries.Where(e => Matches(e.Capability, intent.IntentTag)), context, intent.CanonicalAppId).ToArray();
            if (matches.Any(e => context.ExplicitOwnerInvocations.Contains(e.Capability.OwnerId)))
                matches = matches.Where(e => context.ExplicitOwnerInvocations.Contains(e.Capability.OwnerId)).ToArray();
            evaluated.AddRange(matches);
            var expanded = new List<List<ResolutionStep>>();
            foreach (var path in paths)
            foreach (var candidate in matches)
            {
                var trial = path.ToList();
                if (Plan(candidate, intent.Arguments, intent, trial, new HashSet<string>(StringComparer.Ordinal), set, context, evaluated, budget))
                    expanded.Add(trial);
                if (expanded.Count >= 8) break;
            }
            if (expanded.Count == 0)
            {
                var reason = matches.Length == 0 ? ResolutionFailureKind.NoMatchingCapability
                    : budget.Exhausted ? ResolutionFailureKind.PlanningLimitExceeded
                    : matches.Any(e => e.State == CapabilityReadiness.PermissionDenied) ? ResolutionFailureKind.PermissionBlocked
                    : matches.Any(e => e.State == CapabilityReadiness.CapabilityDisabledByPolicy) ? ResolutionFailureKind.PolicyBlocked
                    : matches.Any(e => e.State is CapabilityReadiness.Executable or CapabilityReadiness.ApprovalRequired or CapabilityReadiness.AccountConnectionRequired or CapabilityReadiness.MissingRequiredInput)
                        ? ResolutionFailureKind.MissingPrerequisite : ResolutionFailureKind.MatchingCapabilityUnavailable;
                return Remember(new(id, set.RegistryRevision, [], reason, Array.AsReadOnly(evaluated.Distinct().ToArray()),
                    $"No registered dependency path was selected for '{intent.IntentTag}': {reason}. Required arguments, prerequisite capabilities and typed inputs are checked; none are fabricated."), context.CallerId);
            }
            paths = expanded.Take(8).ToList();
        }
        var plans = paths.Select((steps, index) =>
        {
            var approvals = steps.Where(step => step.State == CapabilityReadiness.ApprovalRequired).Select(step => $"{step.StepId}: approval required").ToArray();
            return new ResolutionPlan(id + ":" + (index + 1), set.RegistryRevision, Array.AsReadOnly(steps.ToArray()),
                approvals.Length == 0, Array.AsReadOnly(approvals));
        }).ToArray();
        return Remember(new(id, set.RegistryRevision, Array.AsReadOnly(plans), null, Array.AsReadOnly(evaluated.Distinct().ToArray()),
            "Ranked registered dependency paths exist; each execution requires fresh authority, schema validation and canonical output binding."), context.CallerId);
    }

    private static IOrderedEnumerable<EffectiveCapability> Rank(IEnumerable<EffectiveCapability> entries,
        CapabilityRequestContext context, string? canonicalAppId = null) => entries
        .OrderByDescending(e => context.ExplicitOwnerInvocations.Contains(e.Capability.OwnerId))
        .ThenByDescending(e => canonicalAppId is not null && e.Capability.CanonicalAppId == canonicalAppId)
        .ThenByDescending(e => e.Capability.IsLocal).ThenBy(e => e.State == CapabilityReadiness.Executable ? 0 : 1)
        .ThenByDescending(e => e.Capability.ReliabilityRank).ThenByDescending(e => e.Capability.PreferenceRank)
        .ThenBy(e => e.Capability.CapabilityId, StringComparer.Ordinal);

    private static bool Plan(EffectiveCapability candidate, JsonElement arguments, ResolutionIntent intent,
        List<ResolutionStep> steps, HashSet<string> visiting, EffectiveCapabilitySet set, CapabilityRequestContext context,
        List<EffectiveCapability> evaluated, PlanningBudget budget)
    {
        if (!budget.Consume()) return false;
        if (steps.Count >= 64 || visiting.Count >= 16 || !visiting.Add(candidate.Capability.CapabilityId)) return false;
        try
        {
            evaluated.Add(candidate);
            if (candidate.State is not (CapabilityReadiness.Executable or CapabilityReadiness.ApprovalRequired) ||
                !Haven.Application.ExtensionJsonSchemaValidator.Validate(candidate.Capability.ParameterSchemaJson, arguments, out _)) return false;
            var capability = candidate.Capability;
            foreach (var prerequisiteId in capability.Prerequisites)
            {
                if (steps.Any(step => step.CapabilityId == prerequisiteId)) continue;
                var prerequisite = set.Entries.SingleOrDefault(entry => entry.Capability.CapabilityId == prerequisiteId);
                if (prerequisite is null || !Plan(prerequisite, Arguments(prerequisiteId, intent), intent, steps, visiting, set, context, evaluated, budget)) return false;
            }
            foreach (var type in capability.InputTypes)
            {
                if (context.AvailableInputTypes?.Contains(type) == true || steps.Any(step => step.ExpectedOutputs.Contains(type))) continue;
                var supplied = false;
                foreach (var producer in Rank(set.Entries.Where(entry => entry.Capability.OutputTypes.Contains(type)), context))
                {
                    var trial = steps.ToList();
                    if (!Plan(producer, Arguments(producer.Capability.CapabilityId, intent), intent, trial, visiting, set, context, evaluated, budget)) continue;
                    steps.Clear(); steps.AddRange(trial); supplied = true; break;
                }
                if (!supplied) return false;
            }
            var dependencies = steps.Where(step => capability.Prerequisites.Contains(step.CapabilityId) || step.ExpectedOutputs.Any(capability.InputTypes.Contains))
                .Select(step => step.StepId).Distinct(StringComparer.Ordinal).ToArray();
            steps.Add(new($"step-{steps.Count + 1}", capability.CapabilityId, capability.ActionId, capability.OwnerId, arguments.Clone(),
                capability.OutputTypes, Array.AsReadOnly(dependencies), candidate.State,
                $"Registered {capability.SourceType} capability {capability.CapabilityId}; prerequisite identities and typed inputs resolved."));
            return true;
        }
        finally { visiting.Remove(candidate.Capability.CapabilityId); }
    }
    private sealed class PlanningBudget
    {
        private int _remaining = 4096;
        public bool Exhausted => _remaining < 0;
        public bool Consume() => _remaining-- > 0;
    }
    private static JsonElement Arguments(string capabilityId, ResolutionIntent intent) =>
        intent.PrerequisiteArguments?.TryGetValue(capabilityId, out var arguments) == true
            ? arguments.Clone() : JsonSerializer.SerializeToElement(new { });

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
