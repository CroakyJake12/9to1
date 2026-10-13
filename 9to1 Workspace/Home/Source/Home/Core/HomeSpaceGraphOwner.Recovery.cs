using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Application.NodeGraph;

namespace HavenOS.Home.Core;

public sealed partial class HomeSpaceGraphOwner
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<PublicationEvidence, byte> _observations = new();

    /// <summary>Reopens only an exact durable pending effect under a fresh actual
    /// profile admission. It never restores/replays the old request or publication.</summary>
    public async Task<Operation> RecoverPublishedAsync(Guid spaceId, Guid operationId, string originalSessionId,
        CancellationToken token = default)
    {
        if (spaceId == Guid.Empty || operationId == Guid.Empty || string.IsNullOrWhiteSpace(originalSessionId))
            throw new ArgumentException("An exact pending Space operation and current session are required.");
        DemandHost();
        lock (_capacity)
        {
            if (_reserved == 128 || !_reservedSpaces.Add(spaceId))
                throw new InvalidOperationException("Retire the original in-memory Space operation before recovery.");
            _reserved++;
        }
        var transferred = false;
        PublicationEvidence? evidence = null;
        try
        {
            var actor = await RequireActorAsync(token).ConfigureAwait(false);
            _ = await _spaceAuthority.CaptureWriteAdmissionForActorAsync(actor, token).ConfigureAwait(false);
            var identity = await _settings.GetStoreIdentityAsync(token).ConfigureAwait(false);
            var current = await _spaces.ReadExistingAsync(spaceId, token).ConfigureAwait(false);
            var pending = current?.GraphConfiguration?.Pending;
            if (current is null || current.IsBuiltIn || current.IsArchived || pending is null || pending.OperationId != operationId ||
                pending.Owner.StoreId != identity.StoreId || current.Revision != pending.PendingSpaceRevision ||
                actor.ProfileId != pending.OriginalActor.ProfileId || actor.AuthenticationRevision == pending.OriginalActor.AuthenticationRevision)
                throw new UnauthorizedAccessException("Recovery requires the exact persisted Space intent and fresh same-profile Home authentication.");
            // Capture validates the stored proposal as DATA against the current
            // trusted schema. No selection, receipt or old capability is restored.
            var encoded = pending.PublicationArguments;
            var graph = encoded.GetProperty("Graph").Deserialize<GraphDocument>() ?? throw new InvalidDataException("The pending graph is invalid.");
            if (graph.CapabilityProfileId != CapabilityProfileForPage(pending.Page))
                throw new InvalidDataException("The pending page capability profile requires explicit migration.");
            var scopes = encoded.GetProperty("Scopes").Deserialize<ResourceScope[]>() ?? throw new InvalidDataException("The pending graph scopes are invalid.");
            var intent = GraphPublicationIntent.Capture(pending.Owner, pending.OriginalActor, pending.OperationId,
                pending.Kind, graph, pending.ExpectedGraphRevision, scopes, _schemas, new(4 * 1024 * 1024, 32));
            if (intent.Arguments.GetRawText() != pending.PublicationArguments.GetRawText())
                throw new InvalidDataException("The current schema cannot reproduce the exact pending publication; preserve it for explicit migration.");
            var record = await _graphs.ObservePublicationRecordAsync(intent, pending.ArgumentsDigest, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("SPACE_GRAPH_NEEDS_ATTENTION: the exact LastPublication receipt is absent or superseded; no write will be replayed.");
            var receipt = record.Payload.Deserialize<OwnedGraphDefinition>()?.LastPublication
                ?? throw new InvalidDataException("The observed graph receipt is unavailable.");
            if (!ReceiptMatches(pending, receipt)) throw new InvalidDataException("The exact pending graph receipt differs.");
            evidence = new(this, actor, receipt, record, null);
            var operation = new Operation(this, intent, pending, originalSessionId, actor, recovery: true);
            operation.SetRecoveredEvidence(evidence);
            if (!_observations.TryAdd(evidence, 0) || !_operations.TryAdd(operation, 0))
                throw new InvalidOperationException("Private recovery evidence issuance failed.");
            transferred = true;
            try { await RequireOriginalAsync(operation, intent, receipt, token).ConfigureAwait(false); }
            catch
            {
                _operations.TryRemove(operation, out _); _observations.TryRemove(evidence, out _); transferred = false; throw;
            }
            return operation;
        }
        finally
        {
            if (!transferred)
            {
                if (evidence is not null) _observations.TryRemove(evidence, out _);
                lock (_capacity) { _reserved--; _reservedSpaces.Remove(spaceId); }
            }
        }
    }

    private async Task<PublicationEvidence?> CaptureEvidenceAsync(Operation operation, CancellationToken token)
    {
        if (operation.CapturedEvidence is { } captured) return captured;
        if (operation.IsRecovery) return null;
        var original = await operation.PublicationForEvidence.CaptureObservationAsync(token).ConfigureAwait(false);
        if (original is null) return null;
        var record = await _graphs.ObservePublicationRecordAsync(operation.Intent, operation.Pending.ArgumentsDigest, token).ConfigureAwait(false);
        if (record is null) return null;
        var evidence = new PublicationEvidence(this, operation.CurrentActor, original.Receipt, record, original);
        if (!_observations.TryAdd(evidence, 0)) return null;
        return evidence;
    }
    private bool IsEvidenceCurrentInState(PublicationEvidence evidence, AuthenticatedResourceActor actor, HomeCoreStoredState actual)
    {
        if (!ReferenceEquals(evidence.Owner, this) || !_observations.ContainsKey(evidence) || evidence.Actor != actor ||
            evidence.Live is not null && !_publisher.IsObservationCurrentInState(evidence.Live, actor, actual)) return false;
        var records = actual.Records.Where(record => record.RecordId == evidence.RecordId).ToArray();
        return records.Length == 1 && records[0].RecordType == "home.node-graph" && records[0].SchemaVersion == 1 &&
            records[0].Scope == HomeDataScope.DeviceLocal && records[0].Authority == HomeRecordAuthority.LocalCanonical &&
            CryptographicOperations.FixedTimeEquals(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(records[0])), evidence.Fingerprint);
    }
    internal sealed class PublicationEvidence(HomeSpaceGraphOwner owner, AuthenticatedResourceActor actor,
        GraphPublicationReceipt receipt, HomeCoreStateRecord record, HomeGraphPublicationObservation? live)
    {
        internal HomeSpaceGraphOwner Owner { get; } = owner;
        internal AuthenticatedResourceActor Actor { get; } = actor;
        internal GraphPublicationReceipt Receipt { get; } = receipt;
        internal HomeGraphPublicationObservation? Live { get; } = live;
        internal string RecordId { get; } = record.RecordId;
        internal byte[] Fingerprint { get; } = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record));
    }
}
