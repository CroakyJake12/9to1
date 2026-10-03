using Haven.Application;
using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Home.Core;

/// <summary>Optional local read-operation lease source. No artifact/resource execution grant.
/// The actual trusted composition must supply its original profile identity, never a caller-created identity.</summary>
public interface IHomeLocalOperationLeaseSource
{
    ValueTask<IHomeLocalOperationLease?> AcquireLocalOperationLeaseAsync(HomeLocalProfileIdentity profiles,
        AuthenticatedResourceActor originalActor, CancellationToken cancellationToken = default);
}

/// <summary>Retains the original physical Home read lock. Check directly rereads actual state under the retained lock and invokes the actual
/// profile guard, never recursively reacquiring Home/resources/permissions. Dispose before external completion/audit work.</summary>
public interface IHomeLocalOperationLease : IAsyncDisposable
{
    ValueTask<bool> IsCurrentAsync(CancellationToken cancellationToken = default);
}

public sealed partial class FileHomeCoreStateStore
{
    public async ValueTask<IHomeLocalOperationLease?> AcquireLocalOperationLeaseAsync(HomeLocalProfileIdentity profiles,
        AuthenticatedResourceActor originalActor, CancellationToken cancellationToken = default)
        => await AcquireLocalOperationLeaseCoreAsync(profiles, originalActor, null, cancellationToken).ConfigureAwait(false);

    // Additional actual issuer condition is internal composition only; the raw profile guard always remains mandatory.
    internal async ValueTask<IHomeLocalOperationLease?> AcquireLocalOperationLeaseCoreAsync(HomeLocalProfileIdentity profiles,
        AuthenticatedResourceActor originalActor, IHomeStateCommitActorGuard? issuerGuard, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(originalActor);
        if (!profiles.IsBoundToStore(this)) return null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        FileStream? processLock = null;
        var transferred = false;
        try
        {
            processLock = await AcquireProcessLockAsync(cancellationToken).ConfigureAwait(false);
            var read = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess || !await profiles.CheckAsync(read.State!, originalActor,
                    HomeStateCommitPhase.Admission, cancellationToken).ConfigureAwait(false)) return null;
            if (issuerGuard is not null && !await issuerGuard.CheckAsync(read.State!, originalActor,
                    HomeStateCommitPhase.Admission, cancellationToken).ConfigureAwait(false)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var lease = new LocalOperationLease(_gate, processLock, read.State!, ReadUnlockedAsync, profiles, originalActor, issuerGuard);
            transferred = true;
            processLock = null; // Ownership transferred only after successful actual guard.
            return lease;
        }
        finally
        {
            try { if (processLock is not null) await processLock.DisposeAsync().ConfigureAwait(false); }
            finally { if (!transferred) _gate.Release(); }
        }
    }

    private sealed class LocalOperationLease(SemaphoreSlim storeGate, FileStream processLock,
        HomeCoreStoredState lockedState, Func<CancellationToken, Task<HomeStateReadResult>> readUnlocked,
        HomeLocalProfileIdentity profiles, AuthenticatedResourceActor originalActor, IHomeStateCommitActorGuard? issuerGuard)
        : IHomeLocalOperationLease
    {
        private readonly byte[] _originalStateDigest = Fingerprint(lockedState);
        private readonly SemaphoreSlim _checks = new(1, 1);
        private static byte[] Fingerprint(HomeCoreStoredState state) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions));
        private bool Matches(HomeStateReadResult read) => read.IsSuccess && CryptographicOperations.FixedTimeEquals(_originalStateDigest, Fingerprint(read.State!));
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _revoked;
        public async ValueTask<bool> IsCurrentAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _revoked) != 0) return false;
            await _checks.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref _revoked) != 0) return false;
                // Uses the existing gate/process lease: never calls ReadAsync or another owner resolver.
                var before = await readUnlocked(cancellationToken).ConfigureAwait(false);
                if (!Matches(before) || Volatile.Read(ref _revoked) != 0) return false;
                var current = await profiles.CheckAsync(before.State!, originalActor,
                    HomeStateCommitPhase.Publication, cancellationToken).ConfigureAwait(false);
                if (!current || Volatile.Read(ref _revoked) != 0) return false;
                if (issuerGuard is not null && !await issuerGuard.CheckAsync(before.State!, originalActor,
                        HomeStateCommitPhase.Publication, cancellationToken).ConfigureAwait(false)) return false;
                var after = await readUnlocked(cancellationToken).ConfigureAwait(false);
                return Matches(after) && Volatile.Read(ref _revoked) == 0;
            }
            finally { _checks.Release(); }
        }
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _revoked, 1) != 0)
            { await _released.Task.ConfigureAwait(false); return; }
            await _checks.WaitAsync().ConfigureAwait(false);
            try { await processLock.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                storeGate.Release();
                _checks.Release();
                _released.TrySetResult();
            }
        }
    }
}
