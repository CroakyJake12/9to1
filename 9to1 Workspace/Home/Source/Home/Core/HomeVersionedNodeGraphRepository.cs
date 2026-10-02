using System.Text.Json;
using Haven.Application.NodeGraph;

namespace HavenOS.Home.Core;

/// <summary>Durable shared graph definition storage with one atomic Home envelope per graph. It never executes a graph
/// or authorizes its owner. The service calling this repository must authenticate its resource operation.</summary>
public sealed partial class HomeVersionedNodeGraphRepository : IVersionedNodeGraphRepository
{
    private readonly IHomeCoreStateStore store;
    private readonly NodeGraphSchemaRegistry schemas;
    public HomeVersionedNodeGraphRepository(IHomeCoreStateStore store, NodeGraphSchemaRegistry schemas)
    { this.store = store; this.schemas = schemas; }
    internal bool IsBoundToStore(IHomeCoreStateStore candidate) => ReferenceEquals(store, candidate);
    private const string RecordType = "home.node-graph";
    public async Task<OwnedGraphDefinition?> GetAsync(Guid graphId, CancellationToken cancellationToken = default)
    {
        if (graphId == Guid.Empty) throw new ArgumentException("A canonical graph ID is required.", nameof(graphId));
        var record = await ReadRecordAsync(graphId, cancellationToken).ConfigureAwait(false);
        return record is null ? null : Decode(record);
    }
    public async Task<bool> TrySaveDraftAsync(string ownerAppId, string ownerEntityId, GraphDocument draft, long expectedGraphRevision,
        CancellationToken cancellationToken = default)
    {
        if (draft is null) return false;
        // Snapshot the complete definition before validation and before asynchronous store access.
        draft = JsonSerializer.Deserialize<GraphDocument>(JsonSerializer.Serialize(draft))!;
        if (string.IsNullOrWhiteSpace(ownerAppId) || string.IsNullOrWhiteSpace(ownerEntityId) || expectedGraphRevision < 0 ||
            draft.State != GraphRevisionState.Draft || draft.Revision != expectedGraphRevision + 1 || schemas.Validate(draft).Count != 0) return false;
        var record = await ReadRecordAsync(draft.GraphId, cancellationToken).ConfigureAwait(false);
        var prior = record is null ? null : Decode(record);
        if (prior?.CanonicalOwner is not null) return false;
        if ((prior?.Draft.Revision ?? 0) != expectedGraphRevision || prior is not null &&
            (prior.OwnerAppId != ownerAppId || prior.OwnerEntityId != ownerEntityId || prior.Draft.CapabilityProfileId != draft.CapabilityProfileId)) return false;
        var next = new OwnedGraphDefinition(ownerAppId, ownerEntityId, draft, prior?.Active)
            { ActivatedRevisions = prior?.ActivatedRevisions ?? [], CanonicalOwner = prior?.CanonicalOwner, LastPublication = prior?.LastPublication };
        return await WriteAsync(next, record?.Revision ?? 0, cancellationToken).ConfigureAwait(false);
    }
    public async Task<bool> TryActivateAsync(Guid graphId, long expectedGraphRevision, CancellationToken cancellationToken = default)
    {
        var record = await ReadRecordAsync(graphId, cancellationToken).ConfigureAwait(false);
        if (record is null) return false;
        var definition = Decode(record);
        if (definition.CanonicalOwner is not null) return false;
        if (definition.Draft.Revision != expectedGraphRevision || schemas.Validate(definition.Draft).Count != 0) return false;
        if (definition.Active?.Revision == expectedGraphRevision) return true;
        var active = definition.Draft with { State = GraphRevisionState.Active };
        return await WriteAsync(definition with { Active = active, ActivatedRevisions = definition.ActivatedRevisions.Append(active).ToArray() },
            record.Revision, cancellationToken).ConfigureAwait(false);
    }
    private async Task<HomeCoreStateRecord?> ReadRecordAsync(Guid graphId, CancellationToken cancellationToken)
    {
        var read = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess) throw new InvalidOperationException("The Home graph store is unavailable; existing graphs were preserved.");
        return read.State!.Records.SingleOrDefault(record => record.RecordId == Id(graphId));
    }
    private async Task<bool> WriteAsync(OwnedGraphDefinition definition, long expectedStorageRevision, CancellationToken cancellationToken)
    {
        var record = new HomeCoreStateRecord(Id(definition.Draft.GraphId), RecordType, 1, HomeDataScope.DeviceLocal,
            HomeRecordAuthority.LocalCanonical, 0, JsonSerializer.SerializeToElement(definition));
        return (await store.WriteAsync(record, expectedStorageRevision, cancellationToken).ConfigureAwait(false)).IsSuccess;
    }
    private static OwnedGraphDefinition Decode(HomeCoreStateRecord record)
    {
        if (record.RecordType != RecordType || record.SchemaVersion != 1) throw new InvalidDataException("Shared graph migration is required; stored content was preserved.");
        var value = record.Payload.Deserialize<OwnedGraphDefinition>() ?? throw new InvalidDataException("Invalid shared graph record.");
        if (string.IsNullOrWhiteSpace(value.OwnerAppId) || string.IsNullOrWhiteSpace(value.OwnerEntityId) || value.Draft is null ||
            value.Draft.GraphId == Guid.Empty || value.Draft.Revision < 1 || value.Draft.State != GraphRevisionState.Draft || record.RecordId != Id(value.Draft.GraphId) ||
            value.Draft.SchemaVersion != GraphDocument.CurrentSchemaVersion || value.Active is { } active &&
                (active.GraphId != value.Draft.GraphId || active.Revision > value.Draft.Revision || active.Revision < 1 || active.State != GraphRevisionState.Active ||
                 active.SchemaVersion != GraphDocument.CurrentSchemaVersion || active.CapabilityProfileId != value.Draft.CapabilityProfileId))
            throw new InvalidDataException("Shared graph identity/revision is inconsistent; stored content was preserved.");
        if (value.ActivatedRevisions is null || value.ActivatedRevisions.Any(item => item is null || item.GraphId != value.Draft.GraphId ||
            item.Revision < 1 || item.Revision > value.Draft.Revision || item.State != GraphRevisionState.Active || item.SchemaVersion != GraphDocument.CurrentSchemaVersion ||
            item.CapabilityProfileId != value.Draft.CapabilityProfileId) ||
            value.ActivatedRevisions.Select(item => item.Revision).Distinct().Count() != value.ActivatedRevisions.Count ||
            value.Active is not null && !value.ActivatedRevisions.Any(item => item.Revision == value.Active.Revision))
            throw new InvalidDataException("Shared active graph history is inconsistent; stored content was preserved.");
        if (value.CanonicalOwner is { } owner && (owner.StoreId == Guid.Empty || owner.EntityId == Guid.Empty ||
            string.IsNullOrWhiteSpace(owner.EntityKind) || owner.AppId != value.OwnerAppId || owner.CanonicalEntityId != value.OwnerEntityId))
            throw new InvalidDataException("Canonical graph owner metadata is inconsistent; preserve for recovery.");
        if (value.LastPublication is { } receipt && (value.CanonicalOwner is null || receipt.Owner != value.CanonicalOwner ||
            receipt.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(receipt.ArgumentsDigest) || !Enum.IsDefined(receipt.Kind) ||
            receipt.GraphId != value.Draft.GraphId || receipt.ExpectedGraphRevision < 0 || receipt.CommittedGraphRevision != value.Draft.Revision ||
            receipt.Kind == GraphPublicationKind.SaveDraft && (receipt.ExpectedGraphRevision == long.MaxValue ||
                receipt.CommittedGraphRevision != receipt.ExpectedGraphRevision + 1) ||
            receipt.Kind == GraphPublicationKind.Activate && (receipt.CommittedGraphRevision != receipt.ExpectedGraphRevision ||
                value.Active?.Revision != receipt.CommittedGraphRevision)))
            throw new InvalidDataException("Graph publication receipt is inconsistent; preserve for recovery.");
        return value;
    }
    private static string Id(Guid graphId) => RecordType + ":" + graphId.ToString("D");
}
