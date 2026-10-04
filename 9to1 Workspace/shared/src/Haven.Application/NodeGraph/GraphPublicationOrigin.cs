namespace Haven.Application.NodeGraph;

/// <summary>Opaque same-host original owner selection. Interface implementation and public metadata are not permission.</summary>
public interface IOriginalGraphOwnerSelection { }

/// <summary>Trusted composition registers exactly one actual owner per app. Implementations must verify
/// their private issued selection identity, actual original store/factory, actor, entity and revisions.
/// Runs BEFORE Home's writer lease; must never be called recursively from that lease.</summary>
public interface IGraphPublicationOriginAuthority
{
    string OwnerAppId { get; }
    // Trusted same-provider identity check, not a public descriptor or an execution grant.
    // A copied Home graph store with colliding IDs cannot substitute for the actual publisher.
    bool IsBoundToGraphRepository(IVersionedNodeGraphRepository actualRepository) => false;
    ValueTask RequireCurrentAsync(IOriginalGraphOwnerSelection originalSelection, GraphPublicationIntent intent,
        CancellationToken cancellationToken);
    // Distinct post-publication observation. Prior graph-revision admission cannot be
    // reused after the Home graph changed. Missing actual owner implementation denies.
    ValueTask RequirePublishedAsync(IOriginalGraphOwnerSelection originalSelection, GraphPublicationIntent intent,
        GraphPublicationReceipt knownPublication, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Owning post-publication observation is unavailable.");
}
