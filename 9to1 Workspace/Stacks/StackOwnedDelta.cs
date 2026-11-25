namespace HavenOS.Apps.Stacks;

/// <summary>The domain's OWNED delta against its effective parent. Inherited ancestor changes are excluded so they never inflate a child's totals.
/// Renamed counts known-identity renames once rather than as an unrelated delete plus add.</summary>
public sealed record StackOwnedDelta(int Added, int Deleted, int Modified, int Renamed)
{
    public static StackOwnedDelta Empty => new(0, 0, 0, 0);
}

/// <summary>
/// Pure owned-delta computation (ED-STACKS-001). A domain's effective tree combines inherited ancestor state with its own local/working changes.
/// The owned delta must reflect only what the domain itself changed relative to what it merely inherited — an ancestor framework change that
/// cascades into the effective tree without a local edit contributes zero. This model takes the parent's effective tree, the domain's effective
/// tree, and the domain's own mutations so it can attribute each difference correctly without re-deriving the engine's cascade.
/// </summary>
public static class StackOwnedDeltaCalculator
{
    /// <summary>
    /// Compute the owned delta. parentEffective is the inherited baseline (the parent's effective tree as of this domain's base);
    /// domainEffective is the domain's own effective tree; localMutations are the domain's OWN committed+working changes keyed by path.
    /// A path differing between parent and domain that is NOT covered by a local mutation is inherited, not owned, and is ignored.
    /// </summary>
    public static StackOwnedDelta Compute(
        IReadOnlyDictionary<string, StackResource?> parentEffective,
        IReadOnlyDictionary<string, StackResource?> domainEffective,
        IReadOnlyDictionary<string, StackMutation> localMutations)
    {
        ArgumentNullException.ThrowIfNull(parentEffective); ArgumentNullException.ThrowIfNull(domainEffective); ArgumentNullException.ThrowIfNull(localMutations);

        // Only paths the domain itself touched are candidates for owned change; inherited differences are ignored.
        int added = 0, deleted = 0, modified = 0, renamed = 0;

        foreach (var mutation in localMutations.Values)
        {
            switch (mutation.Kind)
            {
                case StackMutationKind.Rename:
                    // A known-identity rename is one owned rename, not a delete+add.
                    renamed++;
                    break;
                case StackMutationKind.Delete:
                    if (parentEffective.ContainsKey(mutation.Path) && parentEffective[mutation.Path] is not null) deleted++;
                    break;
                case StackMutationKind.Upsert:
                {
                    bool existed = parentEffective.TryGetValue(mutation.Path, out var before) && before is not null;
                    bool existsNow = domainEffective.TryGetValue(mutation.Path, out var after) && after is not null;
                    if (!existed && existsNow) added++;
                    else if (existed && existsNow && !Equivalent(before, after)) modified++;
                    break;
                }
            }
        }

        return new StackOwnedDelta(added, deleted, modified, renamed);
    }

    private static bool Equivalent(StackResource? left, StackResource? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return left.Content.AsSpan().SequenceEqual(right.Content.AsSpan());
    }
}
