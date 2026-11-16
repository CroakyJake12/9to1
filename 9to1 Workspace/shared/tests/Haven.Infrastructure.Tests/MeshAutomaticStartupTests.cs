using System.Security.Cryptography;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class MeshAutomaticStartupTests
{
    [Fact]
    public async Task FreshWarmupAndPassiveProviderReadsNeverCreateIdentityOrNetwork()
    {
        await using var fixture = new Fixture(paired: false, identity: false);
        Assert.Equal(MeshAutomaticStartResult.NotConfigured, await fixture.Mesh.WarmupTrustedPeersAsync(default));
        var provider = new MeshRemoteModelProvider(fixture.Mesh);
        Assert.False((await provider.CheckHealthAsync(default)).IsHealthy);
        Assert.Empty(await provider.GetModelsAsync(default));
        Assert.Empty(await fixture.Mesh.GetRemoteAgentsAsync(default));
        Assert.Equal(0, fixture.Transport.Starts);
        Assert.Equal(0, fixture.Discovery.Starts);
        Assert.Equal(0, fixture.Secrets.Writes);
        Assert.Equal(0, fixture.Store.Saves);
        Assert.Null(fixture.Store.State.LocalIdentity);
    }

    [Theory]
    [InlineData("identity-only")]
    [InlineData("revoked")]
    [InlineData("pending")]
    [InlineData("invalid-fingerprint")]
    public async Task IdentityOrUntrustedHistoryDoesNotOptIntoNetworking(string mode)
    {
        await using var fixture = new Fixture();
        fixture.Store.State = fixture.Store.State with { TrustedPeers = mode switch
        {
            "identity-only" => [],
            "revoked" => [fixture.Peer with { TrustState = MeshPeerTrustState.Revoked, RevokedAt = DateTimeOffset.UtcNow }],
            "pending" => [fixture.Peer with { TrustState = MeshPeerTrustState.Pending }],
            _ => [fixture.Peer with { PublicKeyFingerprint = "invalid" }]
        } };
        Assert.Equal(MeshAutomaticStartResult.NotConfigured, await fixture.Mesh.WarmupTrustedPeersAsync(default));
        Assert.Equal(0, fixture.Transport.Starts);
        Assert.Equal(0, fixture.Discovery.Starts);
        Assert.Equal(0, fixture.Secrets.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrMismatchedSecretRequiresRepairWithoutReplacement(bool mismatched)
    {
        await using var fixture = new Fixture();
        fixture.Secrets.Values[fixture.Identity.DeviceId] = mismatched ? "not-a-key" : null;
        Assert.Equal(MeshAutomaticStartResult.IdentityRepairRequired, await fixture.Mesh.WarmupTrustedPeersAsync(default));
        Assert.Equal(0, fixture.Transport.Starts);
        Assert.Equal(0, fixture.Secrets.Writes);
        Assert.Equal(fixture.Identity, fixture.Store.State.LocalIdentity);
    }

    [Fact]
    public async Task PairedAutomaticAndExplicitConcurrentStartsCoalesceUsingOriginalKey()
    {
        await using var fixture = new Fixture();
        var entered = Signal(); var release = Signal();
        fixture.Secrets.OnRead = async () => { entered.TrySetResult(); await release.Task; };
        var automatic = fixture.Mesh.WarmupTrustedPeersAsync(default);
        await entered.Task;
        var explicitStart = fixture.Mesh.InitialiseAsync(default);
        release.SetResult();
        Assert.Equal(MeshAutomaticStartResult.Started, await automatic);
        await explicitStart;
        Assert.Equal(1, fixture.Transport.Starts);
        Assert.Equal(1, fixture.Discovery.Starts);
        Assert.Equal(fixture.Identity, fixture.Transport.StartedIdentity);
        Assert.Equal(fixture.Secret, fixture.Transport.StartedKey);
        Assert.Equal(0, fixture.Secrets.Writes);
    }

    [Fact]
    public async Task ExplicitMeshUseStillStartsAfterInactiveWarmup()
    {
        await using var fixture = new Fixture(paired: false, identity: false);
        await fixture.Mesh.WarmupTrustedPeersAsync(default);
        await fixture.Mesh.InitialiseAsync(default);
        var offer = await fixture.Mesh.CreatePairingOfferAsync(default);
        Assert.Equal(fixture.Transport.LocalEndpoint, offer.Endpoint);
        Assert.NotEqual(Guid.Empty, offer.ChallengeId);
        Assert.Equal(1, fixture.Transport.Starts);
        Assert.Equal(1, fixture.Discovery.Starts);
        Assert.Equal(1, fixture.Secrets.Writes);
    }

    [Fact]
    public async Task PersistedRevocationDuringSecretReadStopsBeforeListener()
    {
        await using var fixture = new Fixture();
        var entered = Signal(); var release = Signal();
        fixture.Secrets.OnRead = async () => { entered.TrySetResult(); await release.Task; };
        var start = fixture.Mesh.WarmupTrustedPeersAsync(default);
        await entered.Task;
        fixture.Store.State = fixture.Store.State with { TrustedPeers = [fixture.Peer with
            { TrustState = MeshPeerTrustState.Revoked, RevokedAt = DateTimeOffset.UtcNow }] };
        release.SetResult();
        Assert.Equal(MeshAutomaticStartResult.NotConfigured, await start);
        Assert.Equal(0, fixture.Transport.Starts);
        Assert.Equal(0, fixture.Discovery.Starts);
    }

    [Fact]
    public async Task PendingRevocationWinsAgainstHeldAutomaticStartWithoutDeadlock()
    {
        await using var fixture = new Fixture();
        var entered = Signal(); var release = Signal();
        fixture.Secrets.OnRead = async () => { entered.TrySetResult(); await release.Task; };
        var start = fixture.Mesh.WarmupTrustedPeersAsync(default);
        await entered.Task;
        var revoke = fixture.Mesh.RevokeAsync(fixture.Peer.DeviceId, default);
        release.SetResult();
        Assert.Equal(MeshAutomaticStartResult.NotConfigured, await start.WaitAsync(TimeSpan.FromSeconds(3)));
        await revoke.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(MeshPeerTrustState.Revoked, Assert.Single(fixture.Store.State.TrustedPeers).TrustState);
        Assert.Equal(0, fixture.Transport.Starts);
    }

    [Fact]
    public async Task CancellationAfterListenerCreationCleansUpAndPermitsLaterExplicitStart()
    {
        await using var fixture = new Fixture();
        var entered = Signal();
        fixture.Transport.OnStart = async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); };
        using var cancellation = new CancellationTokenSource();
        var start = fixture.Mesh.WarmupTrustedPeersAsync(cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.False(fixture.Transport.IsRunning);
        Assert.Equal(1, fixture.Transport.Stops);
        Assert.Equal(0, fixture.Discovery.Starts);
        fixture.Transport.OnStart = null;
        await fixture.Mesh.InitialiseAsync(default);
        Assert.True(fixture.Transport.IsRunning);
        Assert.True(fixture.Discovery.IsRunning);
    }

    [Fact]
    public async Task PartialDiscoveryFailureCleansBothAndKeepsDiscoverySubscriptionForRetry()
    {
        await using var fixture = new Fixture();
        fixture.Discovery.FailStart = true;
        await Assert.ThrowsAsync<IOException>(() => fixture.Mesh.WarmupTrustedPeersAsync(default));
        Assert.False(fixture.Transport.IsRunning);
        Assert.False(fixture.Discovery.IsRunning);
        Assert.Equal(1, fixture.Transport.Stops);
        Assert.Equal(1, fixture.Discovery.Stops);
        fixture.Discovery.FailStart = false;
        await fixture.Mesh.InitialiseAsync(default);
        fixture.Discovery.Observe(fixture.Candidate);
        await fixture.Transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, fixture.Transport.Connects);
    }

    [Fact]
    public async Task DisposalDuringAwaitedAdmissionPreventsLateStart()
    {
        var fixture = new Fixture();
        var entered = Signal(); var release = Signal();
        fixture.Secrets.OnRead = async () => { entered.TrySetResult(); await release.Task; };
        var start = fixture.Mesh.WarmupTrustedPeersAsync(default);
        await entered.Task;
        var close = fixture.Mesh.DisposeAsync().AsTask();
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await close.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, fixture.Transport.Starts);
        Assert.Equal(0, fixture.Discovery.Starts);
        await fixture.DisposeAsync(); // Coalesced retirement must remain idempotent.
    }

    [Fact]
    public async Task LateDiscoveredConnectionCannotRepublishRevokedPeer()
    {
        await using var fixture = new Fixture();
        await fixture.Mesh.WarmupTrustedPeersAsync(default);
        var entered = Signal(); var release = Signal();
        fixture.Transport.OnConnect = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        fixture.Discovery.Observe(fixture.Candidate);
        await entered.Task;
        await fixture.Mesh.RevokeAsync(fixture.Peer.DeviceId, default);
        release.SetResult();
        await WaitUntilAsync(() => fixture.Transport.Disconnects >= 2);
        var stored = Assert.Single(fixture.Store.State.TrustedPeers);
        Assert.Equal(MeshPeerTrustState.Revoked, stored.TrustState);
        Assert.Equal(fixture.Peer.LastKnownEndpoint, stored.LastKnownEndpoint);
        fixture.Discovery.Observe(fixture.Candidate);
        Assert.Equal(1, fixture.Transport.Connects);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoteOperationsRequireSuccessfulActivationBeforeAnySend(bool pairedButMissingKey)
    {
        await using var fixture = new Fixture(paired: pairedButMissingKey);
        if (pairedButMissingKey) fixture.Secrets.Values[fixture.Identity.DeviceId] = null;
        var route = MeshRemoteModelProvider.EncodeRoute(fixture.Peer.DeviceId, "fixture-provider", "fixture-model");
        var expected = pairedButMissingKey ? MeshAutomaticStartResult.IdentityRepairRequired : MeshAutomaticStartResult.NotConfigured;
        var completion = await Assert.ThrowsAsync<MeshUnavailableException>(() => fixture.Mesh.CompleteRemoteModelAsync(
            route, new OllamaChatRequest("fixture", [], EffortLevel.Low), default));
        var tools = await Assert.ThrowsAsync<MeshUnavailableException>(() => fixture.Mesh.ChatWithRemoteModelToolsAsync(
            route, new OllamaToolRequest("fixture", [], [], EffortLevel.Low), default));
        var agent = await Assert.ThrowsAsync<MeshUnavailableException>(() => fixture.Mesh.ExecuteRemoteAgentAsync(
            fixture.Peer.DeviceId, Guid.NewGuid(), "software fixture", default));
        var snapshot = await Assert.ThrowsAsync<MeshUnavailableException>(() => fixture.Mesh.GetRemoteDeviceSnapshotAsync(
            fixture.Peer.DeviceId, default));
        var device = await Assert.ThrowsAsync<MeshUnavailableException>(() => fixture.Mesh.ExecuteRemoteDeviceActionAsync(
            fixture.Peer.DeviceId, "fixture-action", "fixture-capability", null, true, default));
        var task = await Assert.ThrowsAsync<MeshUnavailableException>(() => fixture.Mesh.DelegateTaskAsync(
            fixture.Peer.DeviceId, "software fixture", [], [], HavenSurface.Tasks, default));
        foreach (var error in new[] { completion, tools, agent, snapshot, device, task })
        { Assert.Equal("MeshUnavailable", error.Code); Assert.Equal(expected, error.Activation); }
        Assert.Equal(0, fixture.Transport.Sends);
        Assert.Equal(0, fixture.Transport.Starts);
        Assert.Equal(0, fixture.Discovery.Starts);
        Assert.Equal(0, fixture.Store.Saves);
    }

    [Fact]
    public async Task RevocationDuringAwaitedTrustUpdateExcludesPeerEvenWhenAnotherRemains()
    {
        await using var fixture = new Fixture();
        var other = fixture.Peer with { DeviceId = Guid.NewGuid(), PublicKeyFingerprint = new string('d', 64) };
        fixture.Store.State = fixture.Store.State with { TrustedPeers = [fixture.Peer, other] };
        var entered = Signal(); var release = Signal(); var updates = 0;
        fixture.Transport.OnSetTrust = async (peers, token) =>
        { if (Interlocked.Increment(ref updates) == 1) { entered.TrySetResult(); await release.Task.WaitAsync(token); } };
        var startup = fixture.Mesh.WarmupTrustedPeersAsync(default);
        await entered.Task;
        var revocation = fixture.Mesh.RevokeAsync(fixture.Peer.DeviceId, default);
        release.SetResult();
        Assert.Equal(MeshAutomaticStartResult.Started, await startup.WaitAsync(TimeSpan.FromSeconds(3)));
        await revocation.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.DoesNotContain(fixture.Transport.TrustAtStart, peer => peer.DeviceId == fixture.Peer.DeviceId);
        Assert.Contains(fixture.Transport.TrustAtStart, peer => peer.DeviceId == other.DeviceId);
        Assert.DoesNotContain(fixture.Transport.TrustedPeers, peer => peer.DeviceId == fixture.Peer.DeviceId);
        Assert.True(updates >= 2);
    }

    [Fact]
    public async Task RetirementAttemptsAllCleanupAndRetainsCancellationAndBothStopFailures()
    {
        var fixture = new Fixture();
        await fixture.Mesh.WarmupTrustedPeersAsync(default);
        var cancelFailure = new InvalidOperationException("throwing cancellation callback fixture");
        var discoveryFailure = new IOException("discovery stop fixture");
        var transportFailure = new IOException("transport stop fixture");
        var cancelCalls = 0;
        fixture.Transport.OnConnect = token =>
        {
            token.Register(() => { Interlocked.Increment(ref cancelCalls); throw cancelFailure; });
            return Task.CompletedTask;
        };
        fixture.Discovery.Observe(fixture.Candidate);
        await fixture.Transport.Connected.Task.WaitAsync(TimeSpan.FromSeconds(3));
        fixture.Discovery.StopFailure = discoveryFailure;
        fixture.Transport.StopFailure = transportFailure;
        var close = fixture.Mesh.DisposeAsync().AsTask();
        Assert.Same(close, fixture.Mesh.DisposeAsync().AsTask());
        var error = await Assert.ThrowsAsync<AggregateException>(() => close);
        var causes = error.Flatten().InnerExceptions;
        Assert.Contains(cancelFailure, causes);
        Assert.Contains(discoveryFailure, causes);
        Assert.Contains(transportFailure, causes);
        Assert.Equal(1, cancelCalls);
        Assert.Equal(1, fixture.Discovery.Stops);
        Assert.Equal(1, fixture.Transport.Stops);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Mesh.InitialiseAsync(default));
        Assert.Equal(1, fixture.Transport.Starts);
        await Assert.ThrowsAsync<AggregateException>(() => fixture.Mesh.DisposeAsync().AsTask());
        Assert.Equal(1, fixture.Transport.Stops);
    }

    [Fact]
    public async Task DelayedPairingCannotOverwriteAQueuedRevocation()
    {
        await using var fixture = new Fixture();
        await fixture.Mesh.InitialiseAsync(default);
        var entered = Signal(); var release = Signal(); var writes = 0;
        fixture.Store.OnSave = async () =>
        {
            if (Interlocked.Increment(ref writes) == 1) { entered.TrySetResult(); await release.Task; }
        };
        var hold = fixture.Mesh.SetPeerCapabilityPermissionAsync(fixture.Peer.DeviceId, "fixture", true, default);
        await entered.Task;
        fixture.Transport.OnPair = () => Task.FromResult(new MeshTransportPeer(fixture.Peer.DeviceId,
            fixture.Peer.DisplayName, fixture.Peer.DeviceClass, fixture.Peer.Platform,
            fixture.Peer.PublicKeyFingerprint, "127.0.0.1:44000", []));
        var pairing = fixture.Mesh.PairAsync(new MeshPairingOffer(Guid.NewGuid(), "127.0.0.1:44000", "123456",
            fixture.Peer.PublicKeyFingerprint, DateTimeOffset.UtcNow.AddMinutes(1)), default);
        var revoke = fixture.Mesh.RevokeAsync(fixture.Peer.DeviceId, default);
        release.SetResult();
        await Task.WhenAll(hold, pairing, revoke).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False((await pairing).Succeeded);
        Assert.Equal(MeshPeerTrustState.Revoked, Assert.Single(fixture.Store.State.TrustedPeers).TrustState);
    }
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public MeshLocalIdentity Identity { get; }
        public string Secret { get; }
        public MeshPeerRecord Peer { get; }
        public Store Store { get; }
        public Secrets Secrets { get; } = new();
        public Transport Transport { get; } = new();
        public Discovery Discovery { get; } = new();
        public MeshCoordinator Mesh { get; }
        public MeshDiscoveryCandidate Candidate => new(Peer.DeviceId, Peer.DisplayName, Peer.DeviceClass,
            Peer.Platform, Peer.PublicKeyFingerprint, "127.0.0.1:43000", DateTimeOffset.UtcNow);
        public Fixture(bool paired = true, bool identity = true)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Secret = Convert.ToBase64String(key.ExportPkcs8PrivateKey());
            Identity = new(Guid.NewGuid(), "Fixture local", MeshDeviceClass.Desktop, CapabilityPlatform.Windows,
                Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant(), DateTimeOffset.UtcNow);
            Peer = new(Guid.NewGuid(), "Fixture trusted", MeshDeviceClass.Desktop, CapabilityPlatform.Windows,
                new string('c', 64), MeshPeerTrustState.Trusted, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "127.0.0.1:42000");
            Store = new(MeshPersistentState.Empty with { LocalIdentity = identity ? Identity : null, TrustedPeers = paired ? [Peer] : [] });
            if (identity) Secrets.Values[Identity.DeviceId] = Secret;
            Mesh = new(Store, Secrets, Transport, new Capabilities(), new Merge(), new DeviceActions(), Discovery);
        }
        public ValueTask DisposeAsync() => Mesh.DisposeAsync();
    }

    private sealed class Store(MeshPersistentState state) : IMeshStateStore
    {
        public MeshPersistentState State { get; set; } = state;
        public int Saves { get; private set; }
        public Func<Task>? OnSave;
        public Task<MeshPersistentState> LoadAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.FromResult(State); }
        public async Task SaveAsync(MeshPersistentState value, CancellationToken token) { token.ThrowIfCancellationRequested(); if (OnSave is { } save) await save(); State = value; Saves++; }
    }
    private sealed class Secrets : IMeshIdentitySecretStore
    {
        public Dictionary<Guid, string?> Values { get; } = [];
        public int Writes { get; private set; }
        public Func<Task>? OnRead { get; set; }
        public async Task<string?> GetPrivateKeyAsync(Guid id, CancellationToken token) { if (OnRead is { } read) await read(); return Values.GetValueOrDefault(id); }
        public Task SetPrivateKeyAsync(Guid id, string key, CancellationToken token) { Values[id] = key; Writes++; return Task.CompletedTask; }
        public Task DeletePrivateKeyAsync(Guid id, CancellationToken token) { Values.Remove(id); return Task.CompletedTask; }
    }
    private sealed class Transport : IMeshTransport
    {
        public event Action<MeshTransportPeer>? PeerObserved;
        public event Action<MeshTransportPeer>? PairingCompleted { add { } remove { } }
        public event Action<Guid, MeshConnectionState>? ConnectionChanged { add { } remove { } }
        public event Action<MeshTransportMessage>? MessageReceived { add { } remove { } }
        public bool IsRunning { get; private set; }
        public string? LocalEndpoint => IsRunning ? "127.0.0.1:41000" : null;
        public int Starts, Stops, Connects, Disconnects, Sends;
        public MeshLocalIdentity? StartedIdentity;
        public string? StartedKey;
        public Func<CancellationToken, Task>? OnStart, OnConnect;
        public Func<Task<MeshTransportPeer>>? OnPair;
        public Func<IReadOnlyList<MeshPeerRecord>, CancellationToken, Task>? OnSetTrust;
        public IReadOnlyList<MeshPeerRecord> TrustedPeers { get; private set; } = [];
        public IReadOnlyList<MeshPeerRecord> TrustAtStart { get; private set; } = [];
        public Exception? StopFailure;
        public TaskCompletionSource Connected { get; } = Signal();
        public async Task StartAsync(MeshLocalIdentity identity, string key, CancellationToken token)
        { Starts++; IsRunning = true; StartedIdentity = identity; StartedKey = key; TrustAtStart = TrustedPeers.ToArray(); if (OnStart is { } start) await start(token); }
        public Task StopAsync(CancellationToken token) { Stops++; if (StopFailure is { } error) throw error; IsRunning = false; return Task.CompletedTask; }
        public async Task SetTrustedPeersAsync(IReadOnlyList<MeshPeerRecord> peers, CancellationToken token)
        { if (OnSetTrust is { } update) await update(peers, token); TrustedPeers = peers.ToArray(); }
        public Task SetPairingChallengeAsync(MeshPairingChallenge? challenge, CancellationToken token) => Task.CompletedTask;
        public Task<MeshTransportPeer> PairAsync(string endpoint, Guid challenge, string code, string fingerprint, CancellationToken token) =>
            OnPair is { } pair ? pair() : throw new NotSupportedException();
        public async Task ConnectAsync(MeshPeerRecord peer, CancellationToken token)
        {
            Connects++;
            if (OnConnect is { } connect) await connect(token);
            PeerObserved?.Invoke(new(peer.DeviceId, peer.DisplayName, peer.DeviceClass, peer.Platform, peer.PublicKeyFingerprint, peer.LastKnownEndpoint!, []));
            Connected.TrySetResult();
        }
        public Task DisconnectAsync(Guid id, CancellationToken token) { Interlocked.Increment(ref Disconnects); return Task.CompletedTask; }
        public Task SendAsync(Guid id, string kind, string payload, CancellationToken token) { Sends++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Discovery : IMeshDiscoveryService
    {
        public event Action<MeshDiscoveryCandidate>? CandidateObserved;
        public bool IsRunning { get; private set; }
        public int Starts, Stops;
        public bool FailStart;
        public Exception? StopFailure;
        public Task StartAsync(MeshLocalIdentity identity, string endpoint, CancellationToken token)
        { Starts++; IsRunning = true; if (FailStart) throw new IOException("Discovery start failure fixture"); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken token) { Stops++; if (StopFailure is { } error) throw error; IsRunning = false; return Task.CompletedTask; }
        public void Observe(MeshDiscoveryCandidate candidate) => CandidateObserved?.Invoke(candidate);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Capabilities : IMeshCapabilitySource
    { public Task<IReadOnlyList<MeshCapabilityDescriptor>> GetLocalCapabilitiesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<MeshCapabilityDescriptor>>([]); }
    private sealed class Merge : IMeshResourceMergeService
    {
        public Task<MeshResourceSnapshot?> GetCurrentAsync(string type, Guid id, CancellationToken token) => Task.FromResult<MeshResourceSnapshot?>(null);
        public Task<bool> TryApplyAsync(MeshSyncMutation mutation, CancellationToken token) => Task.FromResult(true);
    }
    private sealed class DeviceActions : IMeshInboundDeviceActionExecutor
    {
        public Task<DeviceCapabilitySnapshot> GetSnapshotAsync(CancellationToken token) => Task.FromResult(new DeviceCapabilitySnapshot(
            new DeviceTargetDescriptor("current", "Fixture", CapabilityPlatform.Windows, DeviceTargetKind.CurrentDevice, "fixture"), true, DateTimeOffset.UtcNow, []));
        public Task<DeviceActionResult> ExecuteAsync(string key, IReadOnlyDictionary<string, string>? parameters, bool allowed, CancellationToken token) =>
            Task.FromResult(new DeviceActionResult(DeviceActionResultStatus.Unsupported, key, "current", "Not used"));
    }
}
