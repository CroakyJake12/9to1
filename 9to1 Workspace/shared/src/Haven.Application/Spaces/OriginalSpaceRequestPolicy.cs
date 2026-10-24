using System.Collections.Frozen;
using System.Text.Json.Serialization;
using Haven.Core;

namespace Haven.Application;

public sealed record OriginalSpacePolicyRevision(Guid SpaceId, long Revision);

/// <summary>Durable restrictive lineage only; it contains no actor credential or resource grant.</summary>
public sealed class OriginalSpacePolicyLineage : IEquatable<OriginalSpacePolicyLineage>
{
    [JsonConstructor]
    public OriginalSpacePolicyLineage(Guid spaceId, IReadOnlyList<OriginalSpacePolicyRevision> ancestors)
    {
        ArgumentNullException.ThrowIfNull(ancestors);
        if (spaceId == Guid.Empty || ancestors.Count is < 1 or > 128 ||
            ancestors.Any(value => value is null || value.SpaceId == Guid.Empty || value.Revision < 1) ||
            ancestors.Select(value => value.SpaceId).Distinct().Count() != ancestors.Count || ancestors[^1].SpaceId != spaceId)
            throw new ArgumentException("A bounded complete existing Space ancestry is required.", nameof(ancestors));
        SpaceId = spaceId; Ancestors = Array.AsReadOnly(ancestors.ToArray());
    }
    public Guid SpaceId { get; }
    public IReadOnlyList<OriginalSpacePolicyRevision> Ancestors { get; }
    public bool Equals(OriginalSpacePolicyLineage? other) => other is not null && SpaceId == other.SpaceId && Ancestors.SequenceEqual(other.Ancestors);
    public override bool Equals(object? other) => other is OriginalSpacePolicyLineage value && Equals(value);
    public override int GetHashCode() { var hash = new HashCode(); hash.Add(SpaceId); foreach (var row in Ancestors) hash.Add(row); return hash.ToHashCode(); }
}

/// <summary>Canonical immutable policy projection; availability never grants execution.</summary>
public sealed class OriginalSpacePolicySnapshot
{
    public OriginalSpacePolicySnapshot(EffectiveSpacePolicy policy, OriginalSpacePolicyLineage lineage)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(lineage);
        if (policy.SpaceId != lineage.SpaceId || !policy.Ancestry.SequenceEqual(lineage.Ancestors.Select(row => row.SpaceId)))
            throw new ArgumentException("The policy and original ancestry differ.");
        Lineage = lineage;
        Policy = policy with
        {
            Model = policy.Model with { RequiredCapabilities = Array.AsReadOnly((policy.Model.RequiredCapabilities ?? []).ToArray()) },
            Memory = policy.Memory with { AllowedScopes = Array.AsReadOnly((policy.Memory.AllowedScopes ?? []).ToArray()) },
            Capabilities = Array.AsReadOnly(policy.Capabilities.ToArray()), Sources = Array.AsReadOnly(policy.Sources.ToArray()),
            Ancestry = Array.AsReadOnly(policy.Ancestry.ToArray()), DisabledCapabilityKinds = policy.DisabledCapabilityKinds.ToFrozenSet()
        };
    }
    public EffectiveSpacePolicy Policy { get; }
    public OriginalSpacePolicyLineage Lineage { get; }
}

public interface IOriginalSpaceRequestPolicyInput { }

/// <summary>The actual trusted Home/Spaces source issues its own live input. Callbacks are
/// original lifetime custody, not authority supplied by a model or serialized request.</summary>
public interface IOriginalSpaceRequestPolicySource
{
    Task<IOriginalSpaceRequestPolicyInput> CaptureAsync(Conversation conversation, Action<Action> acquireOriginal, CancellationToken token);
    Task<OriginalSpacePolicySnapshot> ReadAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation, Action<Action> acquireOriginal, CancellationToken token);
    Task ValidateAsync(IOriginalSpaceRequestPolicyInput input, Conversation conversation, Action<Action> acquireOriginal, CancellationToken token);
}

public interface IOriginalSpacePolicyDispatchGuard
{
    Task ValidateAsync(IModelProvider? actualProvider, CancellationToken token);
}

public sealed class SpaceRequestPolicyBlockedException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>Separate from UCML's local-only guard. Missing live policy after serialization
/// refuses; the actual maintained provider routers call this before physical dispatch.</summary>
public static class OriginalSpacePolicyDispatch
{
    public static bool RequiresGuard(GenerationOptions? options) => options?.RequireOriginalSpacePolicy == true || options?.OriginalSpacePolicy is not null;
    public static void ValidateRoute(GenerationOptions? options)
    {
        if (RequiresGuard(options) && (options?.OriginalSpacePolicy is null || options.RequestedSpacePolicyLineage is null))
            throw new SpaceRequestPolicyBlockedException("SPACE_POLICY_LIVE_CAPTURE_REQUIRED", "This Space request needs a fresh original policy capture before it can continue.");
    }
    public static Task ValidateAsync(GenerationOptions? options, IModelProvider? actualProvider, CancellationToken token)
    {
        ValidateRoute(options);
        return RequiresGuard(options) ? options!.OriginalSpacePolicy!.ValidateAsync(actualProvider, token) : Task.CompletedTask;
    }
}

public static class SpaceRequestPolicyConstraints
{
    public static GenerationOptions BindContext(GenerationOptions? options, OriginalSpacePolicySnapshot snapshot)
    {
        options ??= new();
        if (options.RequestedSpacePolicyLineage is { } old && !old.Equals(snapshot.Lineage))
            throw Block("SPACE_POLICY_CHANGED", "This Space or one of its parent policies changed; start a new request after reviewing it.");
        var policy = snapshot.Policy;
        if (policy.ModelsCategoryDisabled) throw Block("SPACE_MODELS_DISABLED", "Models are disabled by this Space or a parent policy.");
        // The live page currently supplies some opaque context strings. Do not pretend to
        // filter origin-tagged selection, a closed corpus or a retrieval count that it lacks.
        if (policy.Context.SourceOnly || !policy.Context.IncludeCurrentSelection || policy.Context.MaximumRetrievedItems is not null)
            throw Block("SPACE_TYPED_CONTEXT_CAPTURE_UNAVAILABLE", "This Space's source-only, selection or retrieval-limit policy requires typed context capture that is not available yet. No model request was sent.");
        var context = options.RequestedContextConstraints ?? new();
        var allowPersonal = !policy.MemoryCategoryDisabled && (policy.Memory.AllowedScopes ?? []).Contains(SpaceMemoryScope.Personal);
        // Ordinary conversation history remains available. Never substitute the legacy
        // unscoped personal Learn Me source for a Space-scoped Den broker.
        context = context with
        {
            AllowPersistentMemoryRead = allowPersonal && context.AllowPersistentMemoryRead == true,
            RequireOriginalPersistentMemoryInput = allowPersonal && context.AllowPersistentMemoryRead == true
        };
        var routing = options.RequestedRoutingConstraints;
        if ((policy.Model.RequiredCapabilities?.Count ?? 0) != 0 || !string.IsNullOrWhiteSpace(policy.Model.ProviderConstraint))
            routing = (routing ?? new()) with { AllowFallback = false };
        return options with { RequestedContextConstraints = context, RequestedRoutingConstraints = routing,
            RequestedSpacePolicyLineage = snapshot.Lineage, RequireOriginalSpacePolicy = true };
    }

    public static void ValidateModel(OriginalSpacePolicySnapshot snapshot, ModelDescriptor model)
    {
        foreach (var name in snapshot.Policy.Model.RequiredCapabilities ?? [])
            if (!Enum.TryParse<ToolCapability>(name, true, out var capability) || !Enum.IsDefined(capability) || !model.Supports(capability))
                throw Block("SPACE_MODEL_CAPABILITY_REQUIRED", "The selected model does not advertise a capability required by the effective Space policy.");
    }

    /// <summary>Only subtract from the SAME fresh plan and the existing exact selection.
    /// Unknown network/resource effects are withheld under Ask/Disabled, never labelled approved.</summary>
    public static ModelRequestToolSelectionConstraints IntersectTools(OriginalSpacePolicySnapshot snapshot,
        ToolAvailabilityPlan plan, IReadOnlyList<OllamaToolDefinition> selected, IReadOnlyCollection<ActiveCapability> configured,
        IReadOnlyList<PluginToolBinding> plugins, ModelRequestToolSelectionConstraints? existing)
    {
        var policy = snapshot.Policy;
        var localWorkspace = ToolAvailabilityPlanner.Default.GetOriginalConfiguredDefinitions(plan, configured)
            .Where(value => ModelToolPermissionMap.Map(value.Name) != RestrictedModelCapability.RunCommands)
            .Select(value => value.Name).ToHashSet(StringComparer.Ordinal);
        var deniedPlugins = policy.Capabilities.Where(value => value.Kind == SpaceCapabilityKind.Plugin && value.Override == SpaceCapabilityOverride.Disabled)
            .Select(value => value.CapabilityId).ToHashSet(StringComparer.Ordinal);
        var deniedMcp = policy.DisabledCapabilityKinds.Contains(SpaceCapabilityKind.Mcp) || policy.Capabilities.Any(value => value.Kind == SpaceCapabilityKind.Mcp && value.Override == SpaceCapabilityOverride.Disabled);
        var deniedConnections = policy.DisabledCapabilityKinds.Contains(SpaceCapabilityKind.ConnectedApp) || policy.Capabilities.Any(value => value.Kind == SpaceCapabilityKind.ConnectedApp && value.Override == SpaceCapabilityOverride.Disabled);
        bool Allows(OllamaToolDefinition definition)
        {
            if (existing is not null && !existing.Allows(definition.Name) || !plan.TryGetRuntime(definition.Name, out var runtime)) return false;
            if (policy.DisabledCapabilityKinds.Contains(SpaceCapabilityKind.ConnectedApp) && runtime != ToolRuntimeKind.Plugin) return false;
            if (runtime == ToolRuntimeKind.Mcp && deniedMcp || runtime == ToolRuntimeKind.Calendar && deniedConnections) return false;
            if (runtime == ToolRuntimeKind.Plugin)
            {
                var binding = plugins.SingleOrDefault(value => value.Definition.Name == definition.Name);
                if (binding is null || policy.DisabledCapabilityKinds.Contains(SpaceCapabilityKind.Plugin) || deniedPlugins.Contains(binding.PackageId)) return false;
                if (policy.DisabledCapabilityKinds.Contains(SpaceCapabilityKind.ConnectedApp) && binding.RequiredPermissions != ExtensionPermission.None) return false;
                if (policy.Context.WebAccess != SpaceWebAccessPolicy.Allowed &&
                    (binding.RequiredPermissions & (ExtensionPermission.NetworkAccess | ExtensionPermission.ProcessExecution | ExtensionPermission.ConnectorAccess)) != 0) return false;
                return true;
            }
            if (policy.Context.WebAccess == SpaceWebAccessPolicy.Allowed) return true;
            // Browser/Computer/MCP/Calendar/Automation may retrieve outside the Space;
            // these plans do not yet carry a source-bound Ask grant. Do not invent one.
            return runtime == ToolRuntimeKind.Workspace && localWorkspace.Contains(definition.Name);
        }
        return new(selected.Where(Allows).Select(value => value.Name).ToArray());
    }
    private static SpaceRequestPolicyBlockedException Block(string code, string text) => new(code, text);
}
