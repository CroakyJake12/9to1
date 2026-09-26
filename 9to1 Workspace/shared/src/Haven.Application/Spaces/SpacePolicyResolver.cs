namespace Haven.Application;

public sealed record EffectiveSpacePolicy(
    Guid SpaceId,
    SpaceModelPolicy Model,
    SpaceContextPolicy Context,
    SpaceMemoryPolicy Memory,
    IReadOnlyList<SpaceCapabilityBinding> Capabilities,
    IReadOnlyList<SpaceContextReference> Sources,
    IReadOnlyList<Guid> Ancestry);

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
        var effective = new EffectiveSpacePolicy(
            chain[0].Id,
            chain[0].ModelPolicy ?? new SpaceModelPolicy(chain[0].ModelName),
            chain[0].ContextPolicy ?? new SpaceContextPolicy(),
            chain[0].MemoryPolicy ?? new SpaceMemoryPolicy([SpaceMemoryScope.Conversation]),
            chain[0].CapabilityBindings?.ToArray() ?? [],
            ReadableSources(chain[0].ContextReferences),
            [chain[0].Id]);
        foreach (var child in chain.Skip(1))
            effective = Merge(effective, child);

        return effective with { SpaceId = selected.Id };
    }

    private static EffectiveSpacePolicy Merge(EffectiveSpacePolicy parent, SpaceDefinition child)
    {
        var inheritance = child.Inheritance ?? new SpaceInheritancePolicy();
        var ownModel = child.ModelPolicy ?? new SpaceModelPolicy(child.ModelName);
        var parentModel = inheritance.ModelPolicy ? parent.Model : new SpaceModelPolicy(null);
        var model = new SpaceModelPolicy(
            ownModel.DefaultModelId ?? parentModel.DefaultModelId,
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
        if (inheritance.Plugins || inheritance.Skills || inheritance.Agents)
        {
            foreach (var binding in parent.Capabilities)
            {
                if (!IsInherited(binding.Kind, inheritance)) continue;
                capabilities[(binding.Kind, binding.CapabilityId)] = binding;
            }
        }
        foreach (var binding in child.CapabilityBindings ?? [])
            capabilities[(binding.Kind, binding.CapabilityId)] = binding;

        var sources = new Dictionary<Guid, SpaceContextReference>();
        if (inheritance.Sources)
            foreach (var source in parent.Sources) sources[source.ContextId] = source;
        foreach (var source in ReadableSources(child.ContextReferences)) sources[source.ContextId] = source;

        return new EffectiveSpacePolicy(
            child.Id, model, context, memory,
            capabilities.Values.OrderBy(item => item.Kind).ThenBy(item => item.CapabilityId, StringComparer.Ordinal).ToArray(),
            sources.Values.OrderBy(item => item.AddedAt).ThenBy(item => item.ContextId).ToArray(),
            [.. parent.Ancestry, child.Id]);
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
