using System.Security.Cryptography;
using Haven.Core;

namespace Haven.Application;

public enum MeshAutomaticStartResult { NotConfigured, IdentityRepairRequired, Started, AlreadyRunning }

public sealed class MeshUnavailableException(MeshAutomaticStartResult activation)
    : IOException("Mesh is unavailable. Open Mesh to review its pairing and identity.")
{
    public string Code => "MeshUnavailable";
    public MeshAutomaticStartResult Activation { get; } = activation;
}

public sealed partial class MeshCoordinator
{
    private enum NetworkStartIntent { ExplicitUser, AutomaticTrustedPeers }
    private readonly CancellationTokenSource _networkLifetime = new();
    private readonly object _retirementGate = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> _pendingRevocations = new();
    private Task? _retirement;
    private int _retiring;

    /// <summary>Passive on a fresh/unpaired profile. Only current persisted trust
    /// can admit automatic networking; this never creates a fresh identity.</summary>
    public Task<MeshAutomaticStartResult> WarmupTrustedPeersAsync(CancellationToken cancellationToken) =>
        InitialiseCoreAsync(NetworkStartIntent.AutomaticTrustedPeers, cancellationToken);

    private async Task<MeshAutomaticStartResult> InitialiseCoreAsync(NetworkStartIntent intent, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _retiring) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _networkLifetime.Token);
        var token = linked.Token;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        var transportAttempted = false;
        var discoveryAttempted = false;
        try
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _retiring) != 0, this);
            if (_initialised) return MeshAutomaticStartResult.AlreadyRunning;
            var loaded = await _stateStore.LoadAsync(token).ConfigureAwait(false);
            ValidateStartupState(loaded);
            var automatic = intent == NetworkStartIntent.AutomaticTrustedPeers;
            if (automatic && !HasAutomaticPeer(loaded)) return MeshAutomaticStartResult.NotConfigured;
            if (automatic && !ValidIdentity(loaded.LocalIdentity)) return MeshAutomaticStartResult.IdentityRepairRequired;
            _state = NormalizeStartupState(loaded);
            // Preserve the existing explicit migration path. Automatic discovery
            // does not rewrite records merely to inspect eligibility.
            if (!automatic && (loaded.Version != _state.Version || loaded.WorkMembers is null || loaded.WorkMessages is null
                || loaded.WorkItems is null || loaded.SyncIntents is null))
                await _stateStore.SaveAsync(_state, token).ConfigureAwait(false);
            if (_state.LocalIdentity is null)
            {
                var id = Guid.NewGuid();
                using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var secret = Convert.ToBase64String(key.ExportPkcs8PrivateKey());
                var fingerprint = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
                _state = _state with { LocalIdentity = new MeshLocalIdentity(id, Environment.MachineName,
                    OperatingSystem.IsWindows() ? MeshDeviceClass.Desktop : MeshDeviceClass.Phone,
                    OperatingSystem.IsWindows() ? CapabilityPlatform.Windows : CapabilityPlatform.Android, fingerprint, DateTimeOffset.UtcNow) };
                await _identitySecrets.SetPrivateKeyAsync(id, secret, token).ConfigureAwait(false);
                await _stateStore.SaveAsync(_state, token).ConfigureAwait(false);
            }
            var identity = _state.LocalIdentity!;
            var privateKey = await _identitySecrets.GetPrivateKeyAsync(identity.DeviceId, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(privateKey))
            {
                if (automatic) return MeshAutomaticStartResult.IdentityRepairRequired;
                throw new InvalidDataException("Mesh identity metadata exists, but its original private secret is missing. Re-pair rather than silently replacing its identity.");
            }
            if (automatic)
            {
                if (!SecretMatchesIdentity(identity, privateKey)) return MeshAutomaticStartResult.IdentityRepairRequired;
                // A secret lookup may yield while trust is revoked/identity is
                // replaced. Re-read before any listener/discovery admission.
                var latest = await _stateStore.LoadAsync(token).ConfigureAwait(false);
                ValidateStartupState(latest);
                if (latest.LocalIdentity != identity) return MeshAutomaticStartResult.IdentityRepairRequired;
                if (!HasAutomaticPeer(latest)) return MeshAutomaticStartResult.NotConfigured;
                _state = NormalizeStartupState(latest);
            }
            DemandStartupCurrent(token);
            await RefreshStartupTrustAsync(automatic, token).ConfigureAwait(false);
            DemandStartupCurrent(token);
            if (automatic && !HasAutomaticPeer(_state)) return MeshAutomaticStartResult.NotConfigured;
            transportAttempted = true;
            await _transport.StartAsync(identity, privateKey, token).ConfigureAwait(false);
            DemandStartupCurrent(token);
            await RefreshStartupTrustAsync(automatic, token).ConfigureAwait(false);
            if (automatic && !HasAutomaticPeer(_state)) throw new StartupTrustChangedException();
            discoveryAttempted = _discovery is not null;
            await StartDiscoveryAsync(token).ConfigureAwait(false);
            DemandStartupCurrent(token);
            await RefreshStartupTrustAsync(automatic, token).ConfigureAwait(false);
            if (automatic && !HasAutomaticPeer(_state)) throw new StartupTrustChangedException();
            _initialised = true;
        }
        catch (Exception cause)
        {
            _initialised = false;
            var failures = new List<Exception> { cause };
            // Reversible startup cleanup: do not call permanent StopDiscoveryAsync,
            // which unsubscribes and retires the timer needed by later explicit use.
            if (discoveryAttempted && _discovery is not null)
                try { await _discovery.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception error) { failures.Add(error); }
            if (transportAttempted)
                try { await _transport.StopAsync(CancellationToken.None).ConfigureAwait(false); } catch (Exception error) { failures.Add(error); }
            if (failures.Count > 1) throw new AggregateException("Mesh startup and cleanup did not settle successfully.", failures);
            if (cause is StartupTrustChangedException && intent == NetworkStartIntent.AutomaticTrustedPeers)
                return MeshAutomaticStartResult.NotConfigured;
            throw;
        }
        finally { _gate.Release(); }
        StateChanged?.Invoke();
        return MeshAutomaticStartResult.Started;
    }

    private sealed class StartupTrustChangedException()
        : InvalidOperationException("Mesh trust changed while starting; retry after the device change completes.") { }

    private static void DemandAutomaticNetworkStarted(MeshAutomaticStartResult result)
    {
        if (result is not (MeshAutomaticStartResult.Started or MeshAutomaticStartResult.AlreadyRunning))
            throw new MeshUnavailableException(result);
    }

    private async Task RefreshStartupTrustAsync(bool automatic, CancellationToken token)
    {
        // A revocation can queue while this owner awaits the transport's trust
        // update. Retry the snapshot, not just the "any peer left" predicate.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            DemandStartupCurrent(token);
            var peers = CurrentStartupPeers(automatic);
            await _transport.SetTrustedPeersAsync(peers, token).ConfigureAwait(false);
            DemandStartupCurrent(token);
            if (peers.SequenceEqual(CurrentStartupPeers(automatic))) return;
        }
        throw new StartupTrustChangedException();
    }

    private void DemandStartupCurrent(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _retiring) != 0) throw new OperationCanceledException("Mesh startup is retiring.", token);
    }

    private static void ValidateStartupState(MeshPersistentState state)
    {
        if (state.Version < 0 || state.Version > MeshPersistentState.CurrentVersion || state.TrustedPeers is null)
            throw new InvalidDataException("Mesh state is invalid or newer than this 9to1 build supports.");
    }

    private static MeshPersistentState NormalizeStartupState(MeshPersistentState state) => state with
    {
        Version = MeshPersistentState.CurrentVersion, WorkMembers = state.WorkMembers ?? [],
        WorkMessages = state.WorkMessages ?? [], WorkItems = state.WorkItems ?? [], SyncIntents = state.SyncIntents ?? []
    };

    private static bool ValidFingerprint(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool ValidIdentity(MeshLocalIdentity? identity) => identity is not null && identity.DeviceId != Guid.Empty && ValidFingerprint(identity.PublicKeyFingerprint);
    private bool EligibleAutomaticPeer(MeshPersistentState state, MeshPeerRecord peer) =>
        peer.DeviceId != Guid.Empty && peer.TrustState == MeshPeerTrustState.Trusted && peer.RevokedAt is null &&
        peer.DeviceId != state.LocalIdentity?.DeviceId && peer.TrustedAt is not null &&
        ValidFingerprint(peer.PublicKeyFingerprint) && !_pendingRevocations.ContainsKey(peer.DeviceId);
    private bool HasAutomaticPeer(MeshPersistentState state) => state.TrustedPeers.Any(peer => EligibleAutomaticPeer(state, peer));
    private IReadOnlyList<MeshPeerRecord> CurrentStartupPeers(bool automatic) => _state.TrustedPeers
        .Where(peer => !_pendingRevocations.ContainsKey(peer.DeviceId) && (!automatic || EligibleAutomaticPeer(_state, peer))).ToArray();

    private static bool SecretMatchesIdentity(MeshLocalIdentity identity, string privateKey)
    {
        byte[]? bytes = null;
        try
        {
            bytes = Convert.FromBase64String(privateKey);
            using var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(bytes, out var read);
            return read == bytes.Length && string.Equals(identity.PublicKeyFingerprint,
                Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is FormatException or CryptographicException or ArgumentException) { return false; }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    private bool CanAcceptPeerLocked(MeshPeerRecord proposed)
    {
        if (Volatile.Read(ref _retiring) != 0 || _pendingRevocations.ContainsKey(proposed.DeviceId)) return false;
        var current = _state.TrustedPeers.FirstOrDefault(peer => peer.DeviceId == proposed.DeviceId);
        return current is null || (current.TrustState != MeshPeerTrustState.Revoked &&
            string.Equals(current.PublicKeyFingerprint, proposed.PublicKeyFingerprint, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> TryStorePairedPeerAsync(MeshPeerRecord proposed, MeshTransportPeer observed, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!CanAcceptPeerLocked(proposed)) return false;
            await ReplacePeerLockedAsync(proposed, cancellationToken).ConfigureAwait(false);
            if (!CanAcceptPeerLocked(proposed)) return false;
            UpdatePresence(observed, MeshPresenceState.Available, MeshConnectionState.Connected, DateTimeOffset.UtcNow);
            return true;
        }
        finally { _gate.Release(); }
    }

    private void FinishPendingRevocation(Guid id)
    {
        while (_pendingRevocations.TryGetValue(id, out var count))
        {
            if (count == 1)
            { if (((ICollection<KeyValuePair<Guid, int>>)_pendingRevocations).Remove(new(id, count))) return; }
            else if (_pendingRevocations.TryUpdate(id, count - 1, count)) return;
        }
    }

    private async Task DisposeNetworkCoreAsync()
    {
        await Task.Yield();
        var causes = new List<Exception>();
        void Attempt(Action cleanup) { try { cleanup(); } catch (Exception error) { causes.Add(error); } }
        Attempt(() => _networkLifetime.Cancel());
        await _gate.WaitAsync().ConfigureAwait(false); // Join admitted initialization before retirement.
        try
        {
            Attempt(() => _transport.PeerObserved -= OnPeerObserved);
            Attempt(() => _transport.PairingCompleted -= OnPairingCompleted);
            Attempt(() => _transport.ConnectionChanged -= OnConnectionChanged);
            Attempt(() => _transport.MessageReceived -= OnMessageReceived);
            try { await StopDiscoveryAsync().ConfigureAwait(false); } catch (Exception error) { causes.Add(error); }
            try { if (_transport.IsRunning) await _transport.StopAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) { causes.Add(error); }
        }
        finally
        {
            _initialised = false;
            _gate.Release();
        }
        // Do not dispose the semaphore beneath existing revoked/retired callers.
        // The injected transport/discovery remain owned and disposed by DI.
        if (causes.Count != 0) throw new AggregateException("Mesh retirement retained cancellation/cleanup failures.", causes);
    }
}
