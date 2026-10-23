using System.Text.Json.Serialization;

namespace Haven.Application.NodeGraph;

/// <summary>Immutable inception evidence, never a capability or a SQL creation receipt.</summary>
public sealed record GraphOwnerCreationOrigin(int SchemaVersion, Guid StoreId, Guid AutomationId, Guid DefinitionOperationId,
    Guid GraphOperationId, string Name, DateTimeOffset CapturedAt, string OriginalAbsenceSha256, AuthenticatedResourceActor OriginalActor);

/// <summary>One shared canonical definition, retaining the last activated revision while a newer draft is edited.
/// Ownership metadata is immutable. This persistence port confers no caller access; owning apps authorize references.</summary>
public sealed record OwnedGraphDefinition(string OwnerAppId, string OwnerEntityId, GraphDocument Draft, GraphDocument? Active)
{
    public IReadOnlyList<GraphDocument> ActivatedRevisions { get; init; } = [];
    public GraphOwnerDescriptor? CanonicalOwner { get; init; }
    public GraphPublicationReceipt? LastPublication { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GraphOwnerCreationOrigin? CreationOrigin { get; init; }
}
public interface IVersionedNodeGraphRepository
{
    Task<OwnedGraphDefinition?> GetAsync(Guid graphId, CancellationToken cancellationToken = default);
    Task<bool> TrySaveDraftAsync(string ownerAppId, string ownerEntityId, GraphDocument draft, long expectedGraphRevision,
        CancellationToken cancellationToken = default);
    Task<bool> TryActivateAsync(Guid graphId, long expectedGraphRevision, CancellationToken cancellationToken = default);
}
