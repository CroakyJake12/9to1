namespace Haven.Application;

public sealed record EffectiveSpacePolicy(
    Guid SpaceId,
    SpaceModelPolicy Model,
    SpaceContextPolicy Context,
    SpaceMemoryPolicy Memory,
    IReadOnlyList<SpaceCapabilityBinding> Capabilities,
    IReadOnlyList<SpaceContextReference> Sources,
    IReadOnlyList<Guid> Ancestry,
    IReadOnlySet<SpaceCapabilityKind> DisabledCapabilityKinds = null!,
    bool MemoryCategoryDisabled = false,
    bool ModelsCategoryDisabled = false)
{
    public IReadOnlySet<SpaceCapabilityKind> DisabledCapabilityKinds { get; init; } =
        DisabledCapabilityKinds ?? (IReadOnlySet<SpaceCapabilityKind>)new HashSet<SpaceCapabilityKind>();
}

/// <summary>Resolves inherited Space policy without granting capabilities or opening app data.</summary>
public sealed class SpacePolicyResolver(SpaceRegistry registry)
{
    public async Task<EffectiveSpacePolicy> ResolveAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        if (spaceId == Guid.Empty) throw new ArgumentException("A Space ID is required.", nameof(spaceId));
        var spaces = await registry.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var byId = spaces.ToDictionary(space => space.Id);
        var selected = byId.GetValueOrDefault(spaceId)
            ?? throw new KeyNotFoundException($"Active Space '{spaceId}' was not found.");
        var chain = new List<SpaceDefinition>();
        var visited = new HashSet<Guid>();
        var current = selected;
        while (true)
        {
            if (!visited.Add(current.Id)) throw new InvalidDataException("Space hierarchy contains a cycle.");
            chain.Add(current);
            if (current.ParentSpaceId is not { } parentId) break;
            current = byId.GetValueOrDefault(parentId)
                ?? throw new InvalidDataException($"Parent Space '{parentId}' is unavailable.");
            if (current.IsArchived) throw new InvalidDataException("An active Space cannot inherit from an archived parent.");
        }

        chain.Reverse();
        var root = chain[0];
        var rootDisabled = DisabledKinds(root.CapabilityCategoryPolicy);
        var rootCapabilities = ApplyCategoryPolicy(root.CapabilityBindings?.ToArray() ?? [], root.CapabilityCategoryPolicy, rootDisabled);
        var rootMemory = ApplyCategoryMemory(root.MemoryPolicy ?? new SpaceMemoryPolicy([SpaceMemoryScope.Conversation]), root.CapabilityCategoryPolicy);
        var rootModelsDisabled = root.CapabilityCategoryPolicy?.Models == SpaceCapabilityCategoryState.Disabled;
        var rootModel = ApplyCategoryModel(root.ModelPolicy ?? new SpaceModelPolicy(root.ModelName), root.CapabilityCategoryPolicy);
        var effective = new EffectiveSpacePolicy(
            root.Id,
            rootModel,
            root.ContextPolicy ?? new SpaceContextPolicy(),
            rootMemory,
            rootCapabilities,
            ReadableSources(root.ContextReferences),
            [root.Id],
            rootDisabled,
            root.CapabilityCategoryPolicy?.Memory == SpaceCapabilityCategoryState.Disabled,
            rootModelsDisabled);

        foreach (var child in chain.Skip(1))
            effective = Merge(effective, child);

        return effective with { SpaceId = selected.Id };
    }

    private static EffectiveSpacePolicy Merge(EffectiveSpacePolicy parent, SpaceDefinition child)
    {
        var inheritance = child.Inheritance ?? new SpaceInheritancePolicy();
        var ownModel = child.ModelPolicy ?? new SpaceModelPolicy(child.ModelName);
        var parentModel = inheritance.ModelPolicy ? parent.Model : new SpaceModelPolicy(null);
        var modelsDisabled = parent.ModelsCategoryDisabled ||
            child.CapabilityCategoryPolicy?.Models == SpaceCapabilityCategoryState.Disabled;
        var rawDefault = ownModel.DefaultModelId ?? parentModel.DefaultModelId;
        var model = new SpaceModelPolicy(
            modelsDisabled ? null : rawDefault,
            (inheritance.ModelPolicy ? parentModel.RequiredCapabilities ?? [] : [])
                .Concat(ownModel.RequiredCapabilities ?? []).Distinct(StringComparer.Ordinal).ToArray(),
            ownModel.ProviderConstraint ?? parentModel.ProviderConstraint);

        var ownContext = child.ContextPolicy ?? new SpaceContextPolicy();
        var inheritedContext = inheritance.Sources ? parent.Context : new SpaceContextPolicy();
        var context = new SpaceContextPolicy(
            MoreRestrictive(inheritedContext.WebAccess, ownContext.WebAccess),
            inheritedContext.SourceOnly || ownContext.SourceOnly,
            inheritedContext.IncludeCurrentSelection && ownContext.IncludeCurrentSelection,
            MinBudget(inheritedContext.MaximumRetrievedItems, ownContext.MaximumRetrievedItems));

        var ownMemory = child.MemoryPolicy ?? new SpaceMemoryPolicy([SpaceMemoryScope.Conversation]);
        var inheritedMemory = inheritance.MemoryPolicy ? parent.Memory : new SpaceMemoryPolicy(ownMemory.AllowedScopes, ownMemory.DefaultScope);
        var allowedMemory = (inheritedMemory.AllowedScopes ?? []).Intersect(ownMemory.AllowedScopes ?? []).Distinct().ToArray();
        var defaultMemory = allowedMemory.Contains(ownMemory.DefaultScope)
            ? ownMemory.DefaultScope
            : allowedMemory.FirstOrDefault();
        var memory = new SpaceMemoryPolicy(allowedMemory, defaultMemory);

        var capabilities = new Dictionary<(SpaceCapabilityKind Kind, string Id), SpaceCapabilityBinding>();
        // Carry ancestor disabled categories forward (restrictive, even with inheritance off).
        var disabled = new HashSet<SpaceCapabilityKind>(parent.DisabledCapabilityKinds);
        var childDisabled = DisabledKinds(child.CapabilityCategoryPolicy);
        foreach (var k in childDisabled) disabled.Add(k);
        var memoryDisabled = parent.MemoryCategoryDisabled ||
            child.CapabilityCategoryPolicy?.Memory == SpaceCapabilityCategoryState.Disabled;

        foreach (var binding in parent.Capabilities)
        {
            // Ancestor exclusion remains a restriction even when this category is not inherited.
            if (binding.Override == SpaceCapabilityOverride.Disabled)
            {
                capabilities[(binding.Kind, binding.CapabilityId)] = binding;
                continue;
            }
            if (disabled.Contains(binding.Kind)) continue; // category disabled by ancestor or self
            if (!IsInherited(binding.Kind, inheritance)) continue;
            capabilities[(binding.Kind, binding.CapabilityId)] = binding;
        }
        foreach (var binding in child.CapabilityBindings ?? [])
        {
            var key = (binding.Kind, binding.CapabilityId);
            // Preserve explicit ancestor exclusion (tombstone) — never overwrite with enabled.
            if (capabilities.TryGetValue(key, out var inherited) && inherited.Override == SpaceCapabilityOverride.Disabled) continue;
            // Category disabled: reject new resources of that kind.
            if (disabled.Contains(binding.Kind)) continue;
            capabilities[key] = binding;
        }

        // Apply child category policy: Disabled removes all of that kind (including inherited).
        var filtered = capabilities.Values.Where(b => !disabled.Contains(b.Kind)).ToArray();

        var sources = new Dictionary<Guid, SpaceContextReference>();
        if (inheritance.Sources)
            foreach (var source in parent.Sources) sources[source.ContextId] = source;
        foreach (var source in ReadableSources(child.ContextReferences)) sources[source.ContextId] = source;

        var mergedModel = modelsDisabled
            ? model with { DefaultModelId = null }
            : ApplyCategoryModel(model, child.CapabilityCategoryPolicy);
        var mergedMemory = memoryDisabled
            ? new SpaceMemoryPolicy([], memory.DefaultScope)
            : ApplyCategoryMemory(memory, child.CapabilityCategoryPolicy);

        return new EffectiveSpacePolicy(
            child.Id, mergedModel, context, mergedMemory,
            filtered.OrderBy(item => item.Kind).ThenBy(item => item.CapabilityId, StringComparer.Ordinal).ToArray(),
            sources.Values.OrderBy(item => item.AddedAt).ThenBy(item => item.ContextId).ToArray(),
            [.. parent.Ancestry, child.Id],
            disabled,
            memoryDisabled,
            modelsDisabled);
    }

    private static IReadOnlyList<SpaceCapabilityBinding> ApplyCategoryPolicy(
        IReadOnlyList<SpaceCapabilityBinding> capabilities,
        SpaceCapabilityCategoryPolicy? policy,
        HashSet<SpaceCapabilityKind> disabled)
    {
        if (disabled.Count == 0) return capabilities;
        return capabilities.Where(b => !disabled.Contains(b.Kind)).ToArray();
    }

    private static SpaceModelPolicy ApplyCategoryModel(SpaceModelPolicy model, SpaceCapabilityCategoryPolicy? policy)
    {
        if (policy is null || policy.Models != SpaceCapabilityCategoryState.Disabled) return model;
        // Disabled models category: restrict to no default model.
        return model with { DefaultModelId = null };
    }

    private static SpaceMemoryPolicy ApplyCategoryMemory(SpaceMemoryPolicy memory, SpaceCapabilityCategoryPolicy? policy)
    {
        if (policy is null || policy.Memory != SpaceCapabilityCategoryState.Disabled) return memory;
        return new SpaceMemoryPolicy([], memory.DefaultScope);
    }

    private static HashSet<SpaceCapabilityKind> DisabledKinds(SpaceCapabilityCategoryPolicy? policy)
    {
        var set = new HashSet<SpaceCapabilityKind>();
        if (policy is null) return set;
        if (policy.Skills == SpaceCapabilityCategoryState.Disabled) set.Add(SpaceCapabilityKind.Skill);
        if (policy.Plugins == SpaceCapabilityCategoryState.Disabled) set.Add(SpaceCapabilityKind.Plugin);
        if (policy.Apps == SpaceCapabilityCategoryState.Disabled)
        {
            set.Add(SpaceCapabilityKind.ConnectedApp);
            set.Add(SpaceCapabilityKind.Mcp);
            set.Add(SpaceCapabilityKind.Agent);
        }
        return set;
    }

    private static IReadOnlyList<SpaceContextReference> ReadableSources(IReadOnlyList<SpaceContextReference>? sources) =>
        (sources ?? []).Where(source => source.Permission is SpaceContextPermission.Read or SpaceContextPermission.ReadWrite)
            .OrderBy(source => source.AddedAt).ThenBy(source => source.ContextId).ToArray();

    private static bool IsInherited(SpaceCapabilityKind kind, SpaceInheritancePolicy inheritance) => kind switch
    {
        SpaceCapabilityKind.Plugin => inheritance.Plugins,
        SpaceCapabilityKind.Skill => inheritance.Skills,
        SpaceCapabilityKind.Agent => inheritance.Agents,
        _ => inheritance.ConnectedApps
    };

    private static SpaceWebAccessPolicy MoreRestrictive(SpaceWebAccessPolicy left, SpaceWebAccessPolicy right) =>
        (SpaceWebAccessPolicy)Math.Max((int)left, (int)right);

    private static int? MinBudget(int? left, int? right) => (left, right) switch
    {
        (null, null) => null,
        (null, { } value) => value,
        ({ } value, null) => value,
        ({ } first, { } second) => Math.Min(first, second)
    };
}
