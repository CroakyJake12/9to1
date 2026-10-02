using System.Text.Json;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Application.NodeGraph;

namespace HavenOS.Home.Core;

public sealed partial class HomeVersionedNodeGraphRepository
{
    // Internal only: the public persistence interface cannot supply arbitrary actor guards as authority.
    // The owning Home issuer must claim its distinct graph capability before calling this body.
    internal async Task<GraphPublicationReceipt?> TryPublishGuardedAsync(GraphPublicationIntent intent,
        string argumentsDigest, IHomeStateCommitActorGuard issuedGuard, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(intent); ArgumentNullException.ThrowIfNull(issuedGuard);
        var record = await ReadRecordAsync(intent.Graph.GraphId, ct).ConfigureAwait(false);
        var prior = record is null ? null : Decode(record);
        if (record is not null && (record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical))
            throw new InvalidDataException("Noncanonical graph preserved for explicit recovery.");
        // Existing legacy definitions need an explicit migration/adoption operation, never implicit ownership reassignment.
        if (prior is not null && (prior.CanonicalOwner != intent.Owner || prior.OwnerAppId != intent.Owner.AppId ||
            prior.OwnerEntityId != intent.Owner.CanonicalEntityId || prior.Draft.CapabilityProfileId != intent.Graph.CapabilityProfileId)) return null;
        if ((prior?.Draft.Revision ?? 0) != intent.ExpectedGraphRevision || schemas.Validate(intent.Graph).Count != 0) return null;
        OwnedGraphDefinition next;
        if (intent.Kind == GraphPublicationKind.SaveDraft)
        {
            if (intent.ExpectedGraphRevision == long.MaxValue || intent.Graph.Revision != intent.ExpectedGraphRevision + 1 ||
                intent.Graph.State != GraphRevisionState.Draft) return null;
            next = new(intent.Owner.AppId, intent.Owner.CanonicalEntityId, intent.Graph, prior?.Active)
                { CanonicalOwner = intent.Owner, ActivatedRevisions = prior?.ActivatedRevisions ?? [] };
        }
        else
        {
            if (prior is null || intent.Graph.Revision != intent.ExpectedGraphRevision ||
                JsonSerializer.Serialize(prior.Draft) != JsonSerializer.Serialize(intent.Graph) ||
                prior.Active?.Revision == intent.ExpectedGraphRevision) return null;
            var active = prior.Draft with { State = GraphRevisionState.Active };
            next = prior with { Active = active, ActivatedRevisions = prior.ActivatedRevisions.Append(active).ToArray() };
        }
        var receipt = new GraphPublicationReceipt(intent.OperationId, argumentsDigest, intent.Kind, intent.Owner,
            intent.Graph.GraphId, intent.ExpectedGraphRevision, intent.Graph.Revision);
        next = next with { LastPublication = receipt };
        var candidate = new HomeCoreStateRecord(Id(intent.Graph.GraphId), RecordType, 1, HomeDataScope.DeviceLocal,
            HomeRecordAuthority.LocalCanonical, 0, JsonSerializer.SerializeToElement(next));
        var result = await store.WriteGuardedAsync(candidate, record?.Revision ?? 0, intent.OriginalActor, new GraphBodyGuard(intent.Graph.GraphId, record, issuedGuard), ct).ConfigureAwait(false);
        return result.IsSuccess ? receipt : null;
    }

    private sealed class GraphBodyGuard(Guid graphId, HomeCoreStateRecord? expected, IHomeStateCommitActorGuard issued)
        : IHomeStateCommitActorGuard
    {
        private readonly byte[]? _expected = expected is null ? null : SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(expected));
        public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor actor,
            HomeStateCommitPhase phase, CancellationToken ct)
        {
            var matches = state.Records.Where(record => record.RecordId == Id(graphId)).ToArray();
            if (_expected is null ? matches.Length != 0 : matches.Length != 1 ||
                !SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(matches[0])).AsSpan().SequenceEqual(_expected)) return false;
            return await issued.CheckAsync(state, actor, phase, ct).ConfigureAwait(false);
        }
    }

    internal async Task<HomeCoreStateRecord?> ObservePublicationRecordAsync(GraphPublicationIntent intent,
        string argumentsDigest, CancellationToken ct)
    {
        var record = await ReadRecordAsync(intent.Graph.GraphId, ct).ConfigureAwait(false);
        if (record is null || record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical) return null;
        var value = Decode(record);
        var receipt = value.LastPublication;
        return value.CanonicalOwner == intent.Owner && receipt is not null && receipt.Owner == intent.Owner &&
            receipt.OperationId == intent.OperationId && receipt.ArgumentsDigest == argumentsDigest && receipt.Kind == intent.Kind &&
            receipt.GraphId == intent.Graph.GraphId && receipt.ExpectedGraphRevision == intent.ExpectedGraphRevision &&
            receipt.CommittedGraphRevision == intent.Graph.Revision &&
            JsonSerializer.Serialize(value.Draft) == JsonSerializer.Serialize(intent.Graph) &&
            (intent.Kind != GraphPublicationKind.Activate || value.Active is not null &&
                JsonSerializer.Serialize(value.Active) == JsonSerializer.Serialize(intent.Graph with { State = GraphRevisionState.Active })) ? record : null;
    }

    internal async Task<GraphPublicationReceipt?> ObservePublicationAsync(GraphPublicationIntent intent,
        string argumentsDigest, CancellationToken ct)
    {
        var record = await ReadRecordAsync(intent.Graph.GraphId, ct).ConfigureAwait(false);
        if (record is null || record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical) return null;
        var value = Decode(record);
        var receipt = value.LastPublication;
        return value.CanonicalOwner == intent.Owner && receipt is not null && receipt.Owner == intent.Owner &&
            receipt.OperationId == intent.OperationId && receipt.ArgumentsDigest == argumentsDigest && receipt.Kind == intent.Kind &&
            receipt.GraphId == intent.Graph.GraphId && receipt.ExpectedGraphRevision == intent.ExpectedGraphRevision &&
            receipt.CommittedGraphRevision == intent.Graph.Revision &&
            JsonSerializer.Serialize(value.Draft) == JsonSerializer.Serialize(intent.Graph) &&
            (intent.Kind != GraphPublicationKind.Activate || value.Active is not null &&
                JsonSerializer.Serialize(value.Active) == JsonSerializer.Serialize(intent.Graph with { State = GraphRevisionState.Active })) ? receipt : null;
        // Missing or superseded receipt means unconfirmed, never permission to write again.
    }
}
