using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Boards as a Mesh-synchronizable canonical resource: the whole-state rules, the Boards adapter that writes only
/// through the real Boards store, and one end-to-end check that pairs two real transports over loopback TLS and
/// then reopens the received board from disk. Rich-only boards are written by identity-derived path because the
/// store requires the task facet for board-id naming. Nothing here is native rendering, a second physical device,
/// Boards app discovery of a received board, or acceptance.
/// </summary>
public sealed class MeshBoardsSyncTests
{
    private static readonly Guid PeerDevice = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BoardIdentity = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly JsonSerializerOptions StoredJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [Fact]
    public void WholeBoardStateIsAdoptedWhenThisDeviceHasNeverSeenThatBoard()
    {
        var decision = new MeshSyncEngine().Evaluate(null, Replace(baseRevision: 4, revision: 5, hash: "a"), new HashSet<Guid>());
        Assert.Equal(MeshSyncDecisionKind.Apply, decision.Kind);
    }

    [Fact]
    public void IdenticalBoardContentIsRecognisedAsAlreadyHeldWhateverTheRevisionSays()
    {
        var decision = new MeshSyncEngine().Evaluate(LocalBoard(revision: 1, hash: "same"), Replace(baseRevision: 4, revision: 9, hash: "same"), new HashSet<Guid>());
        Assert.Equal(MeshSyncDecisionKind.Duplicate, decision.Kind);
    }

    [Fact]
    public void OlderWholeBoardStateCannotOverwriteANewerLocalBoard()
    {
        var decision = new MeshSyncEngine().Evaluate(LocalBoard(revision: 7, hash: "local"), Replace(baseRevision: 5, revision: 6, hash: "remote"), new HashSet<Guid>());
        Assert.Equal(MeshSyncDecisionKind.IgnoreStale, decision.Kind);
    }

    [Fact]
    public void DivergentBoardEditsBecomeAConflictThatKeepsBothCopies()
    {
        var decision = new MeshSyncEngine().Evaluate(LocalBoard(revision: 5, hash: "edited-here"), Replace(baseRevision: 3, revision: 6, hash: "edited-there"), new HashSet<Guid>());
        Assert.Equal(MeshSyncDecisionKind.Conflict, decision.Kind);
        Assert.Equal("edited-here", decision.Current!.ContentHash);
        Assert.Equal("edited-there", decision.Incoming.ContentHash);
    }

    [Fact]
    public void DirectSuccessorWholeBoardStateIsApplied()
    {
        var decision = new MeshSyncEngine().Evaluate(LocalBoard(revision: 5, hash: "local"), Replace(baseRevision: 5, revision: 6, hash: "next"), new HashSet<Guid>());
        Assert.Equal(MeshSyncDecisionKind.Apply, decision.Kind);
    }

    [Fact]
    public async Task CapturedBoardTravelsAndReopensThroughTheReceivingBoardsStore()
    {
        var (senderStore, senderRoot) = NewStore();
        var (receiverStore, receiverRoot) = NewStore();
        try
        {
            var board = HavenBoardDocumentCodec.CreateNew("Integration by parts");
            await SaveRichAsync(senderStore, senderRoot, board);
            var sender = new BoardsMeshResourceOwner(senderStore, senderRoot);
            var receiver = new BoardsMeshResourceOwner(receiverStore, receiverRoot);

            var captured = await sender.CaptureAsync(board.DocumentId, CancellationToken.None);
            Assert.NotNull(captured);
            Assert.Equal(HavenBoardDocument.FormatIdentity, captured!.ResourceType);
            Assert.Equal(MeshSyncOperationKind.Replace, captured.Kind);
            Assert.Equal(Guid.Empty, captured.OriginDeviceId);
            Assert.Equal(64, captured.ContentHash.Length);
            Assert.Contains("Integration by parts", captured.Payload, StringComparison.Ordinal);

            Assert.True(await receiver.TryApplyAsync(captured with { OriginDeviceId = PeerDevice }, CancellationToken.None));

            var reopened = await receiverStore.LoadDocumentAtPathAsync(IdentityPath(receiverRoot, board.DocumentId), CancellationToken.None);
            Assert.NotNull(reopened);
            Assert.Equal(board.DocumentId, reopened!.DocumentId);
            Assert.Equal("Integration by parts", reopened.RichNotes!.Title);
            Assert.Equal(1L, reopened.RichNotes.Version);
        }
        finally { DeleteBoth(senderRoot, receiverRoot); }
    }

    [Fact]
    public async Task BoardStateClaimingADifferentDocumentIdentityIsRefusedAndNothingLocalIsRewritten()
    {
        var (store, root) = NewStore();
        try
        {
            var board = HavenBoardDocumentCodec.CreateNew("Original title");
            await SaveRichAsync(store, root, board);
            var owner = new BoardsMeshResourceOwner(store, root);
            var captured = await owner.CaptureAsync(board.DocumentId, CancellationToken.None);
            Assert.NotNull(captured);

            Assert.False(await owner.TryApplyAsync(captured! with { ResourceId = Guid.NewGuid() }, CancellationToken.None));

            var files = Directory.GetFiles(root, "*.9to1board", SearchOption.AllDirectories);
            var onlyCopy = Assert.Single(files);
            Assert.Equal(IdentityPath(root, board.DocumentId), onlyCopy);
            var untouched = await store.LoadDocumentAtPathAsync(onlyCopy, CancellationToken.None);
            Assert.Equal("Original title", untouched!.RichNotes!.Title);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task NewerSchemaVersionIsRefusedRatherThanWrittenAsAnEmptyBoard()
    {
        var (store, root) = NewStore();
        try
        {
            var owner = new BoardsMeshResourceOwner(store, root);
            var fromTheFuture = new HavenBoardDocument(
                HavenBoardDocument.FormatIdentity, HavenBoardDocument.CurrentSchemaVersion + 1, BoardIdentity,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, HavenBoardSnapshot.CreateDefault());

            Assert.False(await owner.TryApplyAsync(
                Replace(baseRevision: 0, revision: 1, hash: "future", JsonSerializer.Serialize(fromTheFuture, StoredJson)), CancellationToken.None));
            Assert.Empty(Directory.GetFiles(root, "*.9to1board", SearchOption.AllDirectories));
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task ABoardIdAlreadyHeldLocallyByADifferentDocumentIsNeverOverwrittenByName()
    {
        var (store, root) = NewStore();
        try
        {
            var local = new HavenBoardDocument(HavenBoardDocument.FormatIdentity, HavenBoardDocument.CurrentSchemaVersion,
                Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, HavenBoardSnapshot.CreateDefault() with { Title = "Mine" });
            await store.SaveDocumentAsync(local, CancellationToken.None);

            var visitor = new HavenBoardDocument(HavenBoardDocument.FormatIdentity, HavenBoardDocument.CurrentSchemaVersion,
                Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                HavenBoardSnapshot.CreateDefault() with { Title = "Someone else's board" });
            var mutation = Replace(baseRevision: 0, revision: 1, hash: "visitor", JsonSerializer.Serialize(visitor, StoredJson)) with { ResourceId = visitor.DocumentId };
            var owner = new BoardsMeshResourceOwner(store, root);

            Assert.False(await owner.TryApplyAsync(mutation, CancellationToken.None));
            var reopened = await store.LoadDocumentAsync(HavenBoardSnapshot.CreateDefault().Id, CancellationToken.None);
            Assert.Equal("Mine", reopened!.Snapshot!.Title);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task BoardContentNeverReachesTheGenericMeshJournalBecauseTheBoardsOwnerHandlesItsType()
    {
        var (store, root) = NewStore();
        var journal = new CountingJournal();
        try
        {
            var board = HavenBoardDocumentCodec.CreateNew("Routed to its owner");
            await SaveRichAsync(store, root, board);
            var router = new MeshMergeRouter(journal, [new BoardsMeshResourceOwner(store, root)]);

            var captured = await router.CaptureAsync(board.DocumentId, CancellationToken.None);
            Assert.NotNull(captured);
            Assert.True(await router.TryApplyAsync(captured!, CancellationToken.None));
            Assert.Equal(0, journal.ApplyCalls);
            Assert.Equal(0, journal.GetCurrentCalls);

            Assert.True(await router.TryApplyAsync(Replace(baseRevision: 0, revision: 1, hash: "note") with { ResourceType = "mesh-note" }, CancellationToken.None));
            Assert.Equal(1, journal.ApplyCalls);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task PairedLoopbackDevicesSyncARealBoardFileAndTheReceiverReopensIt()
    {
        var (storeA, rootA) = NewStore();
        var (storeB, rootB) = NewStore();
        var board = HavenBoardDocumentCodec.CreateNew("Mechanics: projectile motion");
        await SaveRichAsync(storeA, rootA, board);

        var transportA = new SecureLanMeshTransport(new EmptyCapabilitySource());
        var transportB = new SecureLanMeshTransport(new EmptyCapabilitySource());
        await using var deviceA = CreateDevice(new MemoryStateStore(BlankState()), transportA,
            new MeshMergeRouter(new CountingJournal(), [new BoardsMeshResourceOwner(storeA, rootA)]));
        await using var deviceB = CreateDevice(new MemoryStateStore(BlankState()), transportB,
            new MeshMergeRouter(new CountingJournal(), [new BoardsMeshResourceOwner(storeB, rootB)]));
        await deviceA.InitialiseAsync(CancellationToken.None);
        await deviceB.InitialiseAsync(CancellationToken.None);

        try
        {
            var offer = await deviceB.CreatePairingOfferAsync(CancellationToken.None);
            var port = offer.Endpoint[(offer.Endpoint.LastIndexOf(':') + 1)..];
            var paired = await deviceA.PairAsync(offer with { Endpoint = "127.0.0.1:" + port }, CancellationToken.None);
            Assert.True(paired.Succeeded, $"{paired.Message} Receiver: {transportB.LastInboundFailure ?? "no inbound diagnostic"}");
            await WaitForAsync(async () => (await deviceB.GetDashboardAsync(CancellationToken.None)).TrustedPeers.Count == 1);

            var peerOnA = (await deviceA.GetDashboardAsync(CancellationToken.None)).TrustedPeers.Single().Peer.DeviceId;
            var peerOnB = (await deviceB.GetDashboardAsync(CancellationToken.None)).TrustedPeers.Single().Peer.DeviceId;
            await deviceA.SetPeerCapabilityPermissionAsync(peerOnA, MeshCoordinator.SyncOutboundCapability, true, CancellationToken.None);
            await deviceB.SetPeerCapabilityPermissionAsync(peerOnB, MeshCoordinator.SyncInboundCapability, true, CancellationToken.None);

            var result = await deviceA.PublishWholeResourceAsync(HavenBoardDocument.FormatIdentity, board.DocumentId, CancellationToken.None);

            Assert.NotNull(result);
            var delivery = Assert.Single(result!.Deliveries);
            Assert.Equal(MeshSyncDeliveryState.Acknowledged, delivery.State);
            var reopened = await storeB.LoadDocumentAtPathAsync(IdentityPath(rootB, board.DocumentId), CancellationToken.None);
            Assert.NotNull(reopened);
            Assert.Equal(board.DocumentId, reopened!.DocumentId);
            Assert.Equal("Mechanics: projectile motion", reopened.RichNotes!.Title);
            Assert.Empty((await deviceB.GetDashboardAsync(CancellationToken.None)).Conflicts);
        }
        finally { DeleteBoth(rootA, rootB); }
    }

    private static MeshSyncMutation Replace(long baseRevision, long revision, string hash, string? payload = null) =>
        new(Guid.NewGuid(), BoardIdentity, HavenBoardDocument.FormatIdentity, MeshSyncOperationKind.Replace,
            baseRevision, revision, hash, Guid.NewGuid(), DateTimeOffset.UtcNow, payload ?? "{}");

    private static MeshResourceSnapshot LocalBoard(long revision, string hash) =>
        new(BoardIdentity, HavenBoardDocument.FormatIdentity, revision, hash, Guid.Empty, DateTimeOffset.UtcNow, null);

    private static string IdentityPath(string root, Guid documentId) =>
        Path.Combine(root, documentId.ToString("N") + ".9to1board");

    /// <summary>A rich-only board has no task facet, so the canonical store requires an explicit path.</summary>
    private static Task SaveRichAsync(JsonFileHavenBoardStore store, string root, HavenBoardDocument document) =>
        store.SaveDocumentAtPathAsync(document, IdentityPath(root, document.DocumentId), CancellationToken.None);

    private static (JsonFileHavenBoardStore Store, string Root) NewStore()
    {
        var root = Directory.CreateTempSubdirectory("boards-mesh-").FullName;
        return (new JsonFileHavenBoardStore(root), root);
    }

    private static void Delete(string root)
    {
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private static void DeleteBoth(string first, string second)
    {
        Delete(first);
        Delete(second);
    }

    private static MeshCoordinator CreateDevice(IMeshStateStore state, IMeshTransport transport, IMeshResourceMergeService merge) =>
        new(state, new MemoryIdentitySecrets(), transport, new EmptyCapabilitySource(), merge, new EmptyInboundDeviceActions(), new NoDiscovery());

    private static MeshPersistentState BlankState() =>
        new(MeshPersistentState.CurrentVersion, null, [], [], [], [], [], [], []);

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

    /// <summary>Proves canonical feature data is not duplicated into the Mesh journal.</summary>
    private sealed class CountingJournal : IMeshResourceMergeService
    {
        public int ApplyCalls { get; private set; }
        public int GetCurrentCalls { get; private set; }
        public Task<MeshResourceSnapshot?> GetCurrentAsync(string resourceType, Guid resourceId, CancellationToken cancellationToken)
        {
            GetCurrentCalls++;
            return Task.FromResult<MeshResourceSnapshot?>(null);
        }
        public Task<bool> TryApplyAsync(MeshSyncMutation mutation, CancellationToken cancellationToken)
        {
            ApplyCalls++;
            return Task.FromResult(true);
        }
    }
}
