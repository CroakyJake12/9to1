using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Durability of Mesh synchronization: a destination that was unreachable stays pending across an application
/// restart, and a replay delivers whatever the owning feature holds at replay time rather than a copy Mesh kept.
/// Mesh state holds intents, never content. Not native rendering, not two physical devices, not acceptance.
/// </summary>
public sealed class MeshSyncQueueTests
{
    private static readonly Guid LocalDevice = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PeerDevice = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task OfflineDestinationStaysPendingAcrossARestartAndReplaysTheCurrentBoardNotASavedCopy()
    {
        var (store, root) = NewBoards();
        var board = HavenBoardDocumentCodec.CreateNew("Original title");
        await store.SaveDocumentAtPathAsync(board, Path.Combine(root, board.DocumentId.ToString("N") + ".9to1board"), CancellationToken.None);

        var state = new MemoryStateStore(StateWithPeer());
        var offlineTransport = new PeerTransport(autoAcknowledge: false, connected: false);
        await using var firstRun = CreateDevice(state, offlineTransport, root);
        await firstRun.InitialiseAsync(CancellationToken.None);
        await firstRun.SetPeerCapabilityPermissionAsync(PeerDevice, MeshCoordinator.SyncOutboundCapability, true, CancellationToken.None);

        var result = await firstRun.PublishWholeResourceAsync(HavenBoardDocument.FormatIdentity, board.DocumentId, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Equal(MeshSyncDeliveryState.QueuedOffline, Assert.Single(result!.Deliveries).State);
        Assert.Empty(offlineTransport.Sent);
        var intent = Assert.Single(state.State.SyncIntents!);
        Assert.Equal(board.DocumentId, intent.ResourceId);
        Assert.Equal(HavenBoardDocument.FormatIdentity, intent.ResourceType);

        // The board is edited while the peer is still away, and the app is restarted.
        board.RichNotes!.Title = "Revised while offline";
        board.RichNotes.Version = 2;
        await store.SaveDocumentAtPathAsync(board, Path.Combine(root, board.DocumentId.ToString("N") + ".9to1board"), CancellationToken.None);

        var restartTransport = new PeerTransport(autoAcknowledge: true, connected: false);
        await using var secondRun = CreateDevice(state, restartTransport, root);
        await secondRun.InitialiseAsync(CancellationToken.None);

        var afterRestart = Assert.Single(secondRun.GetPendingSyncIntents());
        Assert.Equal(MeshSyncDeliveryState.QueuedOffline, afterRestart.State);

        restartTransport.ObservePeer(PeerRecord());
        await WaitForAsync(async () => (await SentMutationsAsync(restartTransport)).Count >= 1);

        var replayed = (await SentMutationsAsync(restartTransport)).First();
        Assert.Equal(board.DocumentId, replayed.ResourceId);
        Assert.Equal(MeshSyncOperationKind.Replace, replayed.Kind);
        Assert.Contains("Revised while offline", replayed.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("Original title", replayed.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcknowledgedDeliveryLeavesNothingPending()
    {
        var (store, root) = NewBoards();
        var board = HavenBoardDocumentCodec.CreateNew("Vector fields");
        var boardPath = Path.Combine(root, board.DocumentId.ToString("N") + ".9to1board");
        await store.SaveDocumentAtPathAsync(board, boardPath, CancellationToken.None);

        var memory = new MemoryStateStore(StateWithPeer(MeshCoordinator.SyncOutboundCapability));
        var transport = new PeerTransport(autoAcknowledge: true, connected: true);
        await using var device = CreateDevice(memory, transport, root);
        await device.InitialiseAsync(CancellationToken.None);
        transport.ObservePeer(PeerRecord());

        var result = await device.PublishWholeResourceAsync(HavenBoardDocument.FormatIdentity, board.DocumentId, CancellationToken.None);
        Assert.NotNull(result);
        var delivery = Assert.Single(result!.Deliveries);
        Assert.Equal(MeshSyncDeliveryState.Acknowledged, delivery.State);
        Assert.Empty(device.GetPendingSyncIntents());
        Assert.Empty(memory.State.SyncIntents!);
        Assert.True((await SentMutationsAsync(transport)).Count >= 1);
    }

    [Fact]
    public async Task VersionTwoMeshStateMigratesToThreeWithoutLosingPeersTasksConflictsOrTheAppliedWindow()
    {
        var applied = Guid.NewGuid();
        var legacy = new MeshPersistentState(
            2,
            new MeshLocalIdentity(LocalDevice, "Local PC", MeshDeviceClass.Desktop, CapabilityPlatform.Linux, new string('d', 64), DateTimeOffset.UtcNow.AddDays(-9)),
            [PeerRecord()],
            [new MeshTaskReceipt(Guid.NewGuid(), MeshTaskStatus.Succeeded, DateTimeOffset.UtcNow, "done")],
            [applied],
            [new MeshConflict(Guid.NewGuid(), Guid.NewGuid(), "9to1.board",
                new MeshResourceSnapshot(Guid.NewGuid(), "9to1.board", 3, "local", Guid.Empty, DateTimeOffset.UtcNow, null),
                new MeshSyncMutation(Guid.NewGuid(), Guid.NewGuid(), "9to1.board", MeshSyncOperationKind.Replace, 2, 4, "remote", PeerDevice, DateTimeOffset.UtcNow, "{}"),
                DateTimeOffset.UtcNow,
                "diverged")],
            [],
            [],
            []);
        var memory = new MemoryStateStore(legacy);
        var transport = new PeerTransport(autoAcknowledge: false, connected: false);
        await using var device = CreateDevice(memory, transport, null);
        await device.InitialiseAsync(CancellationToken.None);

        Assert.Equal(MeshPersistentState.CurrentVersion, memory.State.Version);
        Assert.Equal(3, MeshPersistentState.CurrentVersion);
        Assert.Single(memory.State.TrustedPeers);
        Assert.Single(memory.State.RemoteTasks);
        Assert.Single(memory.State.AppliedSyncOperations);
        Assert.Equal(applied, memory.State.AppliedSyncOperations.Single());
        Assert.Single(memory.State.Conflicts);
        Assert.Empty(memory.State.SyncIntents!);
        Assert.Empty(device.GetPendingSyncIntents());
    }

    private static MeshCoordinator CreateDevice(MemoryStateStore state, IMeshTransport transport, string? boardsRoot)
    {
        var merge = boardsRoot is null
            ? new MeshMergeRouter(new ThrowingJournal(), [])
            : new MeshMergeRouter(new ThrowingJournal(), [new BoardsMeshResourceOwner(new JsonFileHavenBoardStore(boardsRoot), boardsRoot)]);
        return new MeshCoordinator(state, new MemoryIdentitySecrets(state.State.LocalIdentity), transport, new EmptyCapabilitySource(), merge, new EmptyInboundDeviceActions(), new NoDiscovery());
    }

    private static (JsonFileHavenBoardStore Store, string Root) NewBoards()
    {
        var root = Directory.CreateTempSubdirectory("boards-mesh-queue-").FullName;
        return (new JsonFileHavenBoardStore(root), root);
    }

    private static MeshPersistentState StateWithPeer(params string[] grants) =>
        new(MeshPersistentState.CurrentVersion,
            new MeshLocalIdentity(LocalDevice, "Local PC", MeshDeviceClass.Desktop, CapabilityPlatform.Linux, new string('d', 64), DateTimeOffset.UtcNow.AddDays(-3)),
            [PeerRecord(grants)], [], [], [], [], [], []);

    private static MeshPeerRecord PeerRecord(params string[] grants) =>
        new(PeerDevice, "Remote PC", MeshDeviceClass.Desktop, CapabilityPlatform.Linux, new string('c', 64), MeshPeerTrustState.Trusted,
            DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1), null, "127.0.0.1:41001", grants);

    private static async Task<List<MeshSyncMutation>> SentMutationsAsync(PeerTransport transport)
    {
        await Task.CompletedTask;
        return transport.Sent.Where(item => item.Kind == "sync.mutation")
            .Select(item => JsonSerializer.Deserialize<MeshSyncMutation>(item.Payload)!)
            .ToList();
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

    private sealed class MemoryIdentitySecrets : IMeshIdentitySecretStore
    {
        private readonly Dictionary<Guid, string> _values = new();
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

    /// <summary>Proves canonical content is never copied into the Mesh journal: any access is a test failure.</summary>
    private sealed class ThrowingJournal : IMeshResourceMergeService
    {
        public Task<MeshResourceSnapshot?> GetCurrentAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken) =>
            Task.FromResult<MeshResourceSnapshot?>(null);
        public Task<bool> TryApplyAsync(MeshSyncMutation mutation, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class PeerTransport(bool autoAcknowledge, bool connected) : IMeshTransport
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
            if (autoAcknowledge && kind == "sync.mutation")
            {
                var mutation = JsonSerializer.Deserialize<MeshSyncMutation>(payload)!;
                var ack = new MeshSyncAcknowledgement(mutation.OperationId, mutation.ResourceId, mutation.ResourceType,
                    MeshSyncDecisionKind.Apply, "simulated peer accepted the state", mutation.Revision, peerDeviceId, DateTimeOffset.UtcNow);
                MessageReceived?.Invoke(new MeshTransportMessage(Guid.NewGuid(), peerDeviceId, "sync.ack", JsonSerializer.Serialize(ack), DateTimeOffset.UtcNow));
            }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }
}
