namespace Haven.Application;

/// <summary>Only the fields exposed by the Basic editor. This is a patch to the
/// existing Space, not another saved definition or permission grant.</summary>
public sealed record SpaceBuilderBasicPatch(string Name, string Description, string IconKey, string Instructions);

/// <summary>Existing declared Space policy. Availability and effective permissions
/// must still be resolved by their original owners before any runtime operation.</summary>
public sealed record SpaceBuilderAdvancedPatch(
    SpaceBuilderBasicPatch Details,
    SpaceModelPolicy ModelPolicy,
    SpaceContextPolicy ContextPolicy,
    SpaceMemoryPolicy MemoryPolicy,
    IReadOnlyList<SpaceCapabilityBinding> CapabilityBindings,
    SpaceInheritancePolicy Inheritance);

public sealed partial class SpaceRegistry
{
    public Task<SpaceDefinition> ApplyBuilderBasicAsync(Guid spaceId, long expectedRevision,
        SpaceBuilderBasicPatch patch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ValidateBuilderDetails(patch);
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        return MutateSpaceAsync(spaceId, (space, peers) =>
            PatchBuilderDetails(RequireBuilderRevision(space, expectedRevision), peers, patch), cancellationToken);
    }

    public Task<SpaceDefinition> ApplyBuilderAdvancedAsync(Guid spaceId, long expectedRevision,
        SpaceBuilderAdvancedPatch patch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(patch.Details);
        ArgumentNullException.ThrowIfNull(patch.ModelPolicy);
        ArgumentNullException.ThrowIfNull(patch.ContextPolicy);
        ArgumentNullException.ThrowIfNull(patch.MemoryPolicy);
        ArgumentNullException.ThrowIfNull(patch.CapabilityBindings);
        ArgumentNullException.ThrowIfNull(patch.Inheritance);
        ValidateBuilderDetails(patch.Details);
        if (expectedRevision < 1) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        // Detach caller-owned collections before the first asynchronous owner read.
        patch = patch with
        {
            ModelPolicy = patch.ModelPolicy with { RequiredCapabilities = patch.ModelPolicy.RequiredCapabilities?.ToArray() },
            MemoryPolicy = patch.MemoryPolicy with { AllowedScopes = patch.MemoryPolicy.AllowedScopes?.ToArray() },
            CapabilityBindings = patch.CapabilityBindings.ToArray()
        };
        return MutateSpaceAsync(spaceId, (space, peers) =>
        {
            var changed = PatchBuilderDetails(RequireBuilderRevision(space, expectedRevision), peers, patch.Details);
            return changed with
            {
                ModelPolicy = patch.ModelPolicy,
                ContextPolicy = patch.ContextPolicy,
                MemoryPolicy = patch.MemoryPolicy,
                CapabilityBindings = patch.CapabilityBindings,
                Inheritance = patch.Inheritance
            };
        }, cancellationToken);
    }

    private static SpaceDefinition RequireBuilderRevision(SpaceDefinition space, long expectedRevision)
    {
        if (space.Revision != expectedRevision)
            throw new SpaceRevisionConflictException(space.Id, expectedRevision, space.Revision);
        if (space.IsBuiltIn) throw new InvalidOperationException("Built-in Space definitions are protected. Make a copy to configure your own Space.");
        if (space.IsArchived) throw new InvalidOperationException("Restore this Space before editing its configuration.");
        return space;
    }

    private SpaceDefinition PatchBuilderDetails(SpaceDefinition space, IReadOnlyList<SpaceDefinition> peers,
        SpaceBuilderBasicPatch patch)
    {
        var name = NormalizeName(patch.Name);
        if (!string.Equals(name, space.Name, StringComparison.OrdinalIgnoreCase))
            EnsureUniqueName(peers, name, space.Id);
        // Deliberately patch the current owner snapshot. Layouts, examples, sources,
        // shares, parent, stable IDs and every unexposed property remain unchanged.
        return space with
        {
            Name = name, Description = patch.Description, IconKey = patch.IconKey.Trim(),
            Instructions = patch.Instructions, UpdatedAt = _clock()
        };
    }

    private static void ValidateBuilderDetails(SpaceBuilderBasicPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch.Description);
        ArgumentNullException.ThrowIfNull(patch.Instructions);
        _ = NormalizeName(patch.Name);
        if (string.IsNullOrWhiteSpace(patch.IconKey))
            throw new ArgumentException("Choose an icon key for the Space.", nameof(patch));
    }
}
