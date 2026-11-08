using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Resource synchronization over an already-paired Mesh session. Pairing alone never shares anything: both
/// directions need their own explicit permission, the receiver re-checks that the mutation actually came
/// from the session it arrived on, and every destination gets its own acknowledged state instead of one
/// aggregate "synced" flag. Mesh transports and records; the owning feature decides what a merge means.
/// </summary>
public sealed partial class MeshCoordinator
{
    /// <summary>Inbound gate on this device: that peer may push resource changes here. Off until the user allows it.</summary>
    public const string SyncInboundCapability = "mesh-resource-sync-in";

    /// <summary>Outbound gate on this device: this installation may push its own changes to that peer. Off until the user allows it.</summary>
    public const string SyncOutboundCapability = "mesh-resource-share-out";

    /// <summary>Transport frames carry 1 MiB, so a resource payload is refused locally with a reason rather than failing in the socket layer.</summary>
    public const long MaximumSyncPayloadBytes = 768L * 1024;

    private const string SyncMutationMessage = "sync.mutation";
    private const string SyncAcknowledgementMessage = "sync.ack";
    private static readonly TimeSpan SyncAcknowledgementWindow = TimeSpan.FromSeconds(15);

    private readonly ConcurrentDictionary<(Guid OperationId, Guid PeerDeviceId), TaskCompletionSource<MeshSyncAcknowledgement>> _pendingSyncAcknowledgements = new();
    private readonly ConcurrentDictionary<(Guid OperationId, Guid PeerDeviceId), MeshSyncDelivery> _syncDeliveries = new();

    public IReadOnlyList<MeshSyncDelivery> GetSyncDeliveries() =>
        _syncDeliveries.Values.OrderByDescending(item => item.UpdatedAt).Take(200).ToArray();

    /// <summary>
    /// Asks the owning feature for the complete current state of one of its resources and offers it to the peers
    /// this device is allowed to share with. The owner's answer is stamped with this device's identity here, so
    /// an owner can never assert an origin, and a feature with no capture contract is refused rather than
    /// guessed at. Returns null when the owner has no such resource.
    /// </summary>
    public async Task<MeshSyncPublishResult?> PublishWholeResourceAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        if (resourceId == Guid.Empty) throw new ArgumentException("A resource identity is required.", nameof(resourceId));
        if (_merge is not IMeshResourceStateCapture capture)
            throw new InvalidOperationException($"No Mesh resource owner for '{resourceType}' can produce a complete state, so nothing was published.");
        var captured = await capture.CaptureAsync(resourceId, cancellationToken).ConfigureAwait(false);
        if (captured is null) return null;
        if (!string.Equals(captured.ResourceType, resourceType, StringComparison.Ordinal) || captured.ResourceId != resourceId || captured.OriginDeviceId != Guid.Empty)
            throw new InvalidOperationException("The captured resource state does not describe the requested resource, so it was not published.");
        return await PublishResourceChangeAsync(captured with { OriginDeviceId = _state.LocalIdentity!.DeviceId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies one locally authored resource change through the owning merge service, then offers it to each
    /// peer this device has been told to share with. Offline and refused destinations stay visible per peer.
    /// </summary>
    public async Task<MeshSyncPublishResult> PublishResourceChangeAsync(MeshSyncMutation mutation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        await EnsureInitialisedAsync(cancellationToken).ConfigureAwait(false);
        if (mutation.OriginDeviceId != _state.LocalIdentity!.DeviceId)
            throw new ArgumentException("Only this device may originate its own resource mutations; another device's identity is not a local authoring credential.", nameof(mutation));
        if (mutation.Payload is not null && Encoding.UTF8.GetByteCount(mutation.Payload) > MaximumSyncPayloadBytes)
            return new(MeshSyncDecisionKind.Rejected,
                $"This resource payload is larger than the {MaximumSyncPayloadBytes / 1024} KiB Mesh synchronization frame. Send it as a Mesh file transfer and synchronize only its reference.",
                Array.Empty<MeshSyncDelivery>());

        var local = await ApplyEvaluatedAsync(mutation, cancellationToken).ConfigureAwait(false);
        var deliveries = new List<MeshSyncDelivery>();
        foreach (var peer in _state.TrustedPeers.Where(item => item.TrustState == MeshPeerTrustState.Trusted))
        {
            var grants = (peer.AllowedRemoteCapabilities ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!grants.Contains(SyncOutboundCapability)) continue;
            if (peer.DeviceId == mutation.OriginDeviceId) continue;
            if (GetPresence(peer).Connection != MeshConnectionState.Connected)
            {
                deliveries.Add(RecordDelivery(mutation, peer.DeviceId, MeshSyncDeliveryState.QueuedOffline,
                    "The device is offline. The operation stays queued and is confirmed only after an acknowledged attempt.", cancellationToken));
                continue;
            }
            deliveries.Add(await DeliverMutationAsync(mutation, peer, cancellationToken).ConfigureAwait(false));
        }
        StateChanged?.Invoke();
        return new(local.Kind, local.Reason, deliveries);
    }

    private async Task<MeshSyncDelivery> DeliverMutationAsync(MeshSyncMutation mutation, MeshPeerRecord peer, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<MeshSyncAcknowledgement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = (mutation.OperationId, peer.DeviceId);
        _pendingSyncAcknowledgements[key] = completion;
        try
        {
            await _transport.SendAsync(peer.DeviceId, SyncMutationMessage, JsonSerializer.Serialize(mutation), cancellationToken).ConfigureAwait(false);
            using var window = new CancellationTokenSource(SyncAcknowledgementWindow);
            var acknowledgement = await completion.Task.WaitAsync(window.Token).ConfigureAwait(false);
            return RecordDelivery(mutation, peer.DeviceId, acknowledgement.Decision switch
            {
                MeshSyncDecisionKind.Apply => MeshSyncDeliveryState.Acknowledged,
                MeshSyncDecisionKind.Conflict => MeshSyncDeliveryState.ConflictReported,
                MeshSyncDecisionKind.Rejected => MeshSyncDeliveryState.Rejected,
                _ => MeshSyncDeliveryState.Sent
            }, acknowledgement.Reason, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or JsonException)
        {
            return ex switch
            {
                OperationCanceledException => RecordDelivery(mutation, peer.DeviceId, MeshSyncDeliveryState.Unconfirmed,
                    "The peer did not answer inside the acknowledgement window, so the change was sent but not confirmed.", cancellationToken),
                _ => RecordDelivery(mutation, peer.DeviceId, MeshSyncDeliveryState.QueuedOffline,
                    "The Mesh session failed before the change could be delivered. The change remains queued on this device.", cancellationToken)
            };
        }
        finally
        {
            _pendingSyncAcknowledgements.TryRemove(key, out _);
        }
    }

    private MeshSyncDelivery RecordDelivery(MeshSyncMutation mutation, Guid peerDeviceId, MeshSyncDeliveryState state, string detail, CancellationToken cancellationToken)
    {
        var delivery = new MeshSyncDelivery(mutation.OperationId, mutation.ResourceId, mutation.ResourceType, mutation.Revision, peerDeviceId, state, detail, DateTimeOffset.UtcNow);
        _syncDeliveries[(mutation.OperationId, peerDeviceId)] = delivery;
        StateChanged?.Invoke();
        return delivery;
    }

    private bool TryHandleSyncMessage(MeshTransportMessage message)
    {
        if (message.Kind == SyncMutationMessage)
        {
            _ = HandleInboundMutationAsync(message);
            return true;
        }
        if (message.Kind == SyncAcknowledgementMessage)
        {
            try
            {
                var acknowledgement = JsonSerializer.Deserialize<MeshSyncAcknowledgement>(message.Payload);
                if (acknowledgement is not null
                        && _pendingSyncAcknowledgements.TryGetValue((acknowledgement.OperationId, acknowledgement.AcknowledgingDeviceId), out var completion))
                    completion.TrySetResult(acknowledgement);
            }
            catch (JsonException) { }
            return true;
        }
        return false;
    }

    private async Task HandleInboundMutationAsync(MeshTransportMessage message)
    {
        MeshSyncMutation? mutation;
        try { mutation = JsonSerializer.Deserialize<MeshSyncMutation>(message.Payload); }
        catch (JsonException) { return; }
        if (mutation is null) return;

        var decision = MeshSyncDecisionKind.Rejected;
        var reason = "The synchronization mutation was refused.";
        long localRevision = 0;
        try
        {
            var peer = RequireTrustedPeer(message.SourceDeviceId);
            if (mutation.OriginDeviceId != message.SourceDeviceId)
                throw new UnauthorizedAccessException("The mutation names a different originating device than the session that delivered it, so it cannot be treated as that device's own change.");
            var grants = (peer.AllowedRemoteCapabilities ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!grants.Contains(SyncInboundCapability))
                throw new UnauthorizedAccessException($"This 9to1 installation accepts resource synchronization from {peer.DisplayName} only when the user allows it. Nothing was applied.");
            if (mutation.Payload is not null && Encoding.UTF8.GetByteCount(mutation.Payload) > MaximumSyncPayloadBytes)
                throw new InvalidDataException($"The incoming payload exceeds the {MaximumSyncPayloadBytes / 1024} KiB Mesh synchronization frame.");

            var applied = await ApplyEvaluatedAsync(mutation, CancellationToken.None).ConfigureAwait(false);
            decision = applied.Kind;
            reason = applied.Reason;
            var current = await _merge.GetCurrentAsync(mutation.ResourceType, mutation.ResourceId, CancellationToken.None).ConfigureAwait(false);
            localRevision = current?.Revision ?? 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            reason = ex.Message;
        }

        try
        {
            await _transport.SendAsync(message.SourceDeviceId, SyncAcknowledgementMessage,
                JsonSerializer.Serialize(new MeshSyncAcknowledgement(
                    mutation.OperationId, mutation.ResourceId, mutation.ResourceType, decision, reason,
                    localRevision, _state.LocalIdentity!.DeviceId, DateTimeOffset.UtcNow)),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // The mutation outcome is already journalled locally. A lost acknowledgement is reported by the
            // sender as Unconfirmed rather than pretending the peer never received the change.
        }
        StateChanged?.Invoke();
    }
}
