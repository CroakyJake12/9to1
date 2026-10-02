namespace Haven.Application.NodeGraph;

/// <summary>One shared canonical definition, retaining the last activated revision while a newer draft is edited.
/// Ownership metadata is immutable. This persistence port confers no caller access; owning apps authorize references.</summary>
public sealed record OwnedGraphDefinition(string OwnerAppId, string OwnerEntityId, GraphDocument Draft, GraphDocument? Active)
{
    public IReadOnlyList<GraphDocument> ActivatedRevisions { get; init; } = [];
    public GraphOwnerDescriptor? CanonicalOwner { get; init; }
    public GraphPublicationReceipt? LastPublication { get; init; }
}
public interface IVersionedNodeGraphRepository
{
    Task<OwnedGraphDefinition?> GetAsync(Guid graphId, CancellationToken cancellationToken = default);
    Task<bool> TrySaveDraftAsync(string ownerAppId, string ownerEntityId, GraphDocument draft, long expectedGraphRevision,
        CancellationToken cancellationToken = default);
    Task<bool> TryActivateAsync(Guid graphId, long expectedGraphRevision, CancellationToken cancellationToken = default);
}
