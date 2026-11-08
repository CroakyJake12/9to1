using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Mesh resource synchronization across an established pairing. The first two checks drive two real
/// SecureLanMeshTransport instances over loopback TLS, so a mutation and its acknowledgement travel a genuine
/// socket between two distinct device identities. The rest drive the inbound route directly, because a refused,
/// stale, duplicated or conflicting mutation must stay distinguishable from a merged one. Nothing here asserts
/// native rendering, a second physical device, durable offline replay or independent acceptance.
/// </summary>
public sealed class MeshSyncTests
{
    private static readonly Guid LocalDevice = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PeerDevice = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ForeignDevice = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid BoardResource = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private const string BoardType = "board";

    [Fact]
    public async Task PairedLoopbackDevicesConvergeOneBoardRevisionAndAcknowledgeItPerDestination()
    {
        var mergeA = new RecordingMerge();
        var mergeB = new RecordingMerge();
        var transportA = new SecureLanMeshTransport(new EmptyCapabilitySource());
        var transportB = new SecureLanMeshTransport(new EmptyCapabilitySource());
        await using var deviceA = CreateDevice(BlankState(), transportA, mergeA);
        await using var deviceB = CreateDevice(BlankState(), transportB, mergeB);
        await deviceA.InitialiseAsync(CancellationToken.None);
        await deviceB.InitialiseAsync(CancellationToken.None);

        var offer = await deviceB.CreatePairingOfferAsync(CancellationToken.None);
        var port = offer.Endpoint[(offer.Endpoint.LastIndexOf(':') + 1)..];
        var paired = await deviceA.PairAsync(offer with { Endpoint = "127.0.0.1:" + port }, CancellationToken.None);
        Assert.True(paired.Succeeded, $"{paired.Message} Receiver: {transportB.LastInboundFailure ?? "no inbound diagnostic"}");
        await WaitForAsync(async () => (await deviceB.GetDashboardAsync(CancellationToken.None)).TrustedPeers.Count == 1);

        var peerOnA = (await deviceA.GetDashboardAsync(CancellationToken.None)).TrustedPeers.Single().Peer.DeviceId;
        var peerOnB = (await deviceB.GetDashboardAsync(CancellationToken.None)).TrustedPeers.Single().Peer.DeviceId;
        await deviceA.SetPeerCapabilityPermissionAsync(peerOnA, MeshCoordinator.SyncOutboundCapability, true, CancellationToken.None);
        await deviceB.SetPeerCapabilityPermissionAsync(peerOnB, MeshCoordinator.SyncInboundCapability, true, CancellationToken.None);

        var localIdentity = (await deviceA.GetDashboardAsync(CancellationToken.None)).LocalDevice;
        var mutation = Mutation(Guid.NewGuid(), baseRevision: 0, revision: 1, hash: "v1", origin: localIdentity.DeviceId, payload: "{\"title\":\"Vectors\"}");
        var result = await deviceA.PublishResourceChangeAsync(mutation, CancellationToken.None);

        Assert.Equal(MeshSyncDecisionKind.Apply, result.LocalDecision);
        var delivery = Assert.Single(result.Deliveries);
        Assert.Equal(MeshSyncDeliveryState.Acknowledged, delivery.State);
        Assert.Equal(mutation.Revision, delivery.Revision);
        Assert.Single(mergeA.Applied);
        Assert.Single(mergeB.Applied);
        Assert.Equal("v1", mergeB.Applied[0].ContentHash);
        var remote = await deviceB.GetDashboardAsync(CancellationToken.None);
        Assert.Empty(remote.Conflicts);
        Assert.Equal(1, mergeB.Current(BoardType, BoardResource)!.Revision);
    }

    [Fact]
    public async Task TrustedPeerWithoutSyncPermissionCannotPushAndTheReceiverAppliesNothing()
    {
        var mergeA = new RecordingMerge();
        var mergeB = new RecordingMerge();
        var transportA = new SecureLanMeshTransport(new EmptyCapabilitySource());
        var transportB = new SecureLanMeshTransport(new EmptyCapabilitySource());
        await using var deviceA = CreateDevice(BlankState(), transportA, mergeA);
        await using var deviceB = CreateDevice(BlankState(), transportB, mergeB);
        await deviceA.InitialiseAsync(CancellationToken.None);
        await deviceB.InitialiseAsync(CancellationToken.None);

        var offer = await deviceB.CreatePairingOfferAsync(CancellationToken.None);
        var port = offer.Endpoint[(offer.Endpoint.LastIndexOf(':') + 1)..];
        var paired = await deviceA.PairAsync(offer with { Endpoint = "127.0.0.1:" + port }, CancellationToken.None);
        Assert.True(paired.Succeeded, $"{paired.Message} Receiver: {transportB.LastInboundFailure ?? "no inbound diagnostic"}");
        await WaitForAsync(async () => (await deviceB.GetDashboardAsync(CancellationToken.None)).TrustedPeers.Count == 1);

        // Only the sender opts in. Pairing alone never starts sharing, which is the requirement under test.
        var peerOnA = (await deviceA.GetDashboardAsync(CancellationToken.None)).TrustedPeers.Single().Peer.DeviceId;
        await deviceA.SetPeerCapabilityPermissionAsync(peerOnA, MeshCoordinator.SyncOutboundCapability, true, CancellationToken.None);

        var localIdentity = (await deviceA.GetDashboardAsync(CancellationToken.None)).LocalDevice;
        var result = await deviceA.PublishResourceChangeAsync(
            Mutation(Guid.NewGuid(), 0, 1, "private-v1", localIdentity.DeviceId, "{\"secret\":\"not shared\"}"), CancellationToken.None);

        var delivery = Assert.Single(result.Deliveries);
        Assert.Equal(MeshSyncDeliveryState.Rejected, delivery.State);
        Assert.Contains("only when the user allows it", delivery.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(mergeB.Applied);
        var remote = await deviceB.GetDashboardAsync(CancellationToken.None);
        Assert.Empty(remote.Conflicts);
        Assert.Null(remote.TrustedPeers.Single().Peer.AllowedRemoteCapabilities);
    }

    [Fact]
    public async Task MutationClaimingAForeignOriginIsRefusedOnTheSessionThatDeliveredIt()
    {
        var merge = new RecordingMerge();
        var transport = new ControllableTransport();
        await using var device = CreateDevice(StateWithPeer(), transport, merge);
        await device.InitialiseAsync(CancellationToken.None);
        transport.ObservePeer(PeerRecord());

        transport.Emit(PeerDevice, "sync.mutation", Mutation(Guid.NewGuid(), 0, 1, "spoofed", ForeignDevice, "{}"));
        await WaitForAsync(async () => (await SentAcksAsync(transport)).Count == 1);

        var ack = Assert.Single(await SentAcksAsync(transport));
        Assert.Equal(MeshSyncDecisionKind.Rejected, ack.Decision);
        Assert.Contains("different originating device", ack.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(merge.Applied);
    }

    [Fact]
    public async Task PermissionedInboundMutationIsAppliedOnceAndReplayIsAcknowledgedAsDuplicate()
    {
        var merge = new RecordingMerge();
        var transport = new ControllableTransport();
        await using var device = CreateDevice(StateWithPeer(MeshCoordinator.SyncInboundCapability), transport, merge);
        await device.InitialiseAsync(CancellationToken.None);
        transport.ObservePeer(PeerRecord());

        var operation = Guid.NewGuid();
        transport.Emit(PeerDevice, "sync.mutation", Mutation(operation, 0, 1, "v1", PeerDevice, "{\"blocks\":1}"));
        await WaitForAsync(async () => (await SentAcksAsync(transport)).Count == 1);
        transport.Emit(PeerDevice, "sync.mutation", Mutation(operation, 0, 1, "v1", PeerDevice, "{\"blocks\":1}"));
        await WaitForAsync(async () => (await SentAcksAsync(transport)).Count == 2);

        var acks = await SentAcksAsync(transport);
        Assert.Equal(MeshSyncDecisionKind.Apply, acks[0].Decision);
        Assert.Equal(1, acks[0].LocalRevision);
        Assert.Equal(MeshSyncDecisionKind.Duplicate, acks[1].Decision);
        Assert.Single(merge.Applied);
    }

    [Fact]
    public async Task OlderRevisionIsIgnoredWithoutOverwritingTheNewerLocalResource()
    {
        var merge = new RecordingMerge();
        merge.Seed(BoardType, BoardResource, revision: 5, hash: "local-newer");
        var transport = new ControllableTransport();
        await using var device = CreateDevice(StateWithPeer(MeshCoordinator.SyncInboundCapability), transport, merge);
        await device.InitialiseAsync(CancellationToken.None);
        transport.ObservePeer(PeerRecord());

        transport.Emit(PeerDevice, "sync.mutation", Mutation(Guid.NewGuid(), baseRevision: 2, revision: 3, hash: "remote-older", PeerDevice, "{}"));
        await WaitForAsync(async () => (await SentAcksAsync(transport)).Count == 1);

        var ack = Assert.Single(await SentAcksAsync(transport));
        Assert.Equal(MeshSyncDecisionKind.IgnoreStale, ack.Decision);
        Assert.Equal(5, ack.LocalRevision);
        Assert.Empty(merge.Applied);
        Assert.Equal("local-newer", merge.Current(BoardType, BoardResource)!.ContentHash);
    }

    [Fact]
    public async Task DivergentEditsBecomeAVisibleConflictHoldingBothRevisionsOnTheReceiver()
    {
        var merge = new RecordingMerge();
        merge.Seed(BoardType, BoardResource, revision: 5, hash: "edited-locally");
        var transport = new ControllableTransport();
        await using var device = CreateDevice(StateWithPeer(MeshCoordinator.SyncInboundCapability), transport, merge);
        await device.InitialiseAsync(CancellationToken.None);
        transport.ObservePeer(PeerRecord());

        transport.Emit(PeerDevice, "sync.mutation", Mutation(Guid.NewGuid(), baseRevision: 3, revision: 6, hash: "edited-remotely", PeerDevice, "{}"));
        await WaitForAsync(async () => (await SentAcksAsync(transport)).Count == 1);

        var ack = Assert.Single(await SentAcksAsync(transport));
        Assert.Equal(MeshSyncDecisionKind.Conflict, ack.Decision);
        Assert.Equal(5, ack.LocalRevision);
        Assert.Empty(merge.Applied);
        var conflict = Assert.Single((await device.GetDashboardAsync(CancellationToken.None)).Conflicts);
        Assert.Equal(BoardResource, conflict.ResourceId);
        Assert.Equal("edited-locally", conflict.Current.ContentHash);
        Assert.Equal("edited-remotely", conflict.Incoming.ContentHash);
        Assert.NotEqual(conflict.Current.Revision, conflict.Incoming.Revision);
    }

    [Fact]
    public async Task OfflinePeerStaysVisiblyQueuedInsteadOfBeingCountedAsSynced()
    {
        var merge = new RecordingMerge();
        var transport = new ControllableTransport(connected: false);
        await using var device = CreateDevice(StateWithPeer(MeshCoordinator.SyncOutboundCapability), transport, merge);
        await device.InitialiseAsync(CancellationToken.None);

        var result = await device.PublishResourceChangeAsync(
            Mutation(Guid.NewGuid(), 0, 1, "v1", LocalDevice, "{\"title\":\"offline draft\"}"), CancellationToken.None);

        Assert.Equal(MeshSyncDecisionKind.Apply, result.LocalDecision);
        var delivery = Assert.Single(result.Deliveries);
        Assert.Equal(MeshSyncDeliveryState.QueuedOffline, delivery.State);
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task OversizedResourcePayloadIsRefusedLocallyAndNeverEntersTheMeshSession()
    {
        var merge = new RecordingMerge();
        var transport = new ControllableTransport();
        await using var device = CreateDevice(StateWithPeer(MeshCoordinator.SyncOutboundCapability), transport, merge);
        await device.InitialiseAsync(CancellationToken.None);
        transport.ObservePeer(PeerRecord());

        var result = await device.PublishResourceChangeAsync(
            Mutation(Guid.NewGuid(), 0, 1, "big", LocalDevice, new string('x', (int)MeshCoordinator.MaximumSyncPayloadBytes + 16)), CancellationToken.None);

        Assert.Equal(MeshSyncDecisionKind.Rejected, result.LocalDecision);
        Assert.Contains("file transfer", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Deliveries);
        Assert.Empty(transport.Sent);
        Assert.Empty(merge.Applied);
    }

    [Fact]
    public async Task RevokedDeviceReceivesNoFurtherMutationsAndItsShareGrantIsIgnored()
    {
        var merge = new RecordingMerge();
        var transport = new ControllableTransport();
        await using var device = CreateDevice(StateWithPeer(MeshCoordinator.SyncOutboundCapability), transport, merge);
        await device.InitialiseAsync(CancellationToken.None);
        transport.ObservePeer(PeerRecord());

        await device.RevokeAsync(PeerDevice, CancellationToken.None);
        var result = await device.PublishResourceChangeAsync(
            Mutation(Guid.NewGuid(), 0, 1, "after-revoke", LocalDevice, "{}"), CancellationToken.None);

        Assert.Equal(MeshSyncDecisionKind.Apply, result.LocalDecision);
        Assert.Empty(result.Deliveries);
        Assert.Empty(transport.Sent);
    }

    private static MeshCoordinator CreateDevice(MeshPersistentState state, IMeshTransport transport, IMeshResourceMergeService merge) =>
        new(new MemoryStateStore(state), new MemoryIdentitySecrets(state.LocalIdentity), transport, new EmptyCapabilitySource(), merge, new EmptyInboundDeviceActions(), new NoDiscovery());

    private static MeshPersistentState BlankState() =>
        new(MeshPersistentState.CurrentVersion, null, [], [], [], [], [], [], []);

    private static MeshPersistentState StateWithPeer(params string[] grants) =>
        new(
            MeshPersistentState.CurrentVersion,
            new MeshLocalIdentity(LocalDevice, "Local PC", MeshDeviceClass.Desktop, CapabilityPlatform.Linux, new string('d', 64), DateTimeOffset.UtcNow.AddDays(-3)),
            [PeerRecord(grants)],
            [],
            [],
            [],
            [],
            [],
            []);

    private static MeshPeerRecord PeerRecord(params string[] grants) =>
        new(PeerDevice, "Remote PC", MeshDeviceClass.Desktop, CapabilityPlatform.Linux, new string('c', 64), MeshPeerTrustState.Trusted,
            DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), null, "127.0.0.1:41001", grants);

    private static MeshSyncMutation Mutation(Guid operationId, long baseRevision, long revision, string hash, Guid origin, string payload) =>
        new(operationId, BoardResource, BoardType, MeshSyncOperationKind.Upsert, baseRevision, revision, hash, origin, DateTimeOffset.UtcNow, payload);

    private static List<MeshSyncAcknowledgement> SentAcks(ControllableTransport transport) =>
        transport.Sent.Where(item => item.Kind == "sync.ack")
            .Select(item => JsonSerializer.Deserialize<MeshSyncAcknowledgement>(item.Payload)!)
            .ToList();

    private static async Task<List<MeshSyncAcknowledgement>> SentAcksAsync(ControllableTransport transport)
    {
        await Task.CompletedTask;
        return SentAcks(transport);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition().ConfigureAwait(false))
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(25, timeout.Token).ConfigureAwait(false);
        }
    }

    private sealed class MemoryStateStore(MeshPersistentState initial) : IMeshStateStore
    {
        public MeshPersistentState State { get; private set; } = initial;
        public Task<MeshPersistentState> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(State);
        public Task SaveAsync(MeshPersistentState state, CancellationToken cancellationToken)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Mirrors the sibling Mesh fixtures: a pre-seeded local identity needs its private secret present, because
    /// InitialiseAsync deliberately refuses to run an installation whose identity metadata has no key. A blank
    /// state leaves the store empty so the coordinator mints and stores its own real key instead.
    /// </summary>
    private sealed class MemoryIdentitySecrets(MeshLocalIdentity? existingIdentity) : IMeshIdentitySecretStore
    {
        private readonly Dictionary<Guid, string> _values = existingIdentity is null
            ? new()
            : new() { [existingIdentity.DeviceId] = "test-secret" };
        public Task<string?> GetPrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken) =>
            Task.FromResult(_values.TryGetValue(deviceId, out var value) ? value : null);
        public Task SetPrivateKeyAsync(Guid deviceId, string privateKey, CancellationToken cancellationToken)
        {
            _values[deviceId] = privateKey;
            return Task.CompletedTask;
        }
        public Task DeletePrivateKeyAsync(Guid deviceId, CancellationToken cancellationToken)
        {
            _values.Remove(deviceId);
            return Task.CompletedTask;
        }
    }

    private sealed class NoDiscovery : IMeshDiscoveryService
    {
        public event Action<MeshDiscoveryCandidate>? CandidateObserved { add { } remove { } }
        public bool IsRunning => false;
        public Task StartAsync(MeshLocalIdentity localIdentity, string localEndpoint, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyCapabilitySource : IMeshCapabilitySource
    {
        public Task<IReadOnlyList<MeshCapabilityDescriptor>> GetLocalCapabilitiesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MeshCapabilityDescriptor>>([]);
    }

    private sealed class EmptyInboundDeviceActions : IMeshInboundDeviceActionExecutor
    {
        public Task<DeviceCapabilitySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceCapabilitySnapshot(
                new DeviceTargetDescriptor("current", "Local PC", CapabilityPlatform.Linux, DeviceTargetKind.CurrentDevice, "test"),
                true, DateTimeOffset.UtcNow, []));
        public Task<DeviceActionResult> ExecuteAsync(string actionKey, IReadOnlyDictionary<string, string>? parameters, bool permissionGranted, CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceActionResult(DeviceActionResultStatus.Unsupported, actionKey, "current", "not used"));
    }

    /// <summary>Records every apply, so a refused, stale or duplicated mutation stays distinguishable from a merged one.</summary>
    private sealed class RecordingMerge : IMeshResourceMergeService
    {
        private readonly Dictionary<(string, Guid), MeshResourceSnapshot> _resources = new();
        public List<MeshSyncMutation> Applied { get; } = [];

        public Task<MeshResourceSnapshot?> GetCurrentAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken) =>
            Task.FromResult(_resources.TryGetValue((resourceType, resourceId), out var value) ? value : null);

        public Task<bool> TryApplyAsync(MeshSyncMutation mutation, CancellationToken cancellationToken)
        {
            Applied.Add(mutation);
            _resources[(mutation.ResourceType, mutation.ResourceId)] = new MeshResourceSnapshot(
                mutation.ResourceId, mutation.ResourceType, mutation.Revision, mutation.ContentHash, mutation.OriginDeviceId, mutation.CreatedAt, mutation.Payload);
            return Task.FromResult(true);
        }

        public void Seed(string resourceType, Guid resourceId, long revision, string hash) =>
            _resources[(resourceType, resourceId)] = new MeshResourceSnapshot(resourceId, resourceType, revision, hash, PeerDevice, DateTimeOffset.UtcNow, null);

        public MeshResourceSnapshot? Current(string resourceType, Guid resourceId) =>
            _resources.TryGetValue((resourceType, resourceId), out var value) ? value : null;
    }

    private sealed class ControllableTransport(bool connected = true) : IMeshTransport
    {
        public event Action<MeshTransportPeer>? PeerObserved;
        public event Action<MeshTransportPeer>? PairingCompleted { add { } remove { } }
        public event Action<Guid, MeshConnectionState>? ConnectionChanged;
        public event Action<MeshTransportMessage>? MessageReceived;

        public List<(Guid PeerDeviceId, string Kind, string Payload)> Sent { get; } = [];
        public bool IsRunning { get; private set; }
        public string? LocalEndpoint { get; private set; } = "127.0.0.1:40000";

        public Task StartAsync(MeshLocalIdentity localIdentity, string privateKey, CancellationToken cancellationToken)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public Task SetPairingChallengeAsync(MeshPairingChallenge? challenge, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetTrustedPeersAsync(IReadOnlyList<MeshPeerRecord> peers, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<MeshTransportPeer> PairAsync(string endpoint, Guid challengeId, string verificationCode, string expectedRemoteFingerprint, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task ConnectAsync(MeshPeerRecord peer, CancellationToken cancellationToken)
        {
            ObservePeer(peer);
            return Task.CompletedTask;
        }

        public void ObservePeer(MeshPeerRecord peer)
        {
            PeerObserved?.Invoke(new MeshTransportPeer(peer.DeviceId, peer.DisplayName, peer.DeviceClass, peer.Platform, peer.PublicKeyFingerprint, peer.LastKnownEndpoint!, []));
            if (connected) ConnectionChanged?.Invoke(peer.DeviceId, MeshConnectionState.Connected);
        }

        public Task DisconnectAsync(Guid peerDeviceId, CancellationToken cancellationToken)
        {
            ConnectionChanged?.Invoke(peerDeviceId, MeshConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public Task SendAsync(Guid peerDeviceId, string kind, string payload, CancellationToken cancellationToken)
        {
            Sent.Add((peerDeviceId, kind, payload));
            return Task.CompletedTask;
        }

        public void Emit(Guid sourceDeviceId, string kind, MeshSyncMutation payload) =>
            MessageReceived?.Invoke(new MeshTransportMessage(Guid.NewGuid(), sourceDeviceId, kind, JsonSerializer.Serialize(payload), DateTimeOffset.UtcNow));

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }
}
