using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class FileHomeCoreStateStore
{
    public ValueTask<IHomeLocalOperationLease?> AcquireLocalOperationLeaseAsync(HomeLocalProfileIdentity profiles,
        AuthenticatedResourceActor originalActor, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken = default) =>
        AcquireLocalOperationLeaseCoreAsync(profiles, originalActor, null, originalSynchronousScope, retainOriginalTask, cancellationToken);

    // Same store/process lease, actor and issuer checks. No new resource/policy authority.
    // Raw children and late acquired products remain owned even after a post-scope fault.
    internal async ValueTask<IHomeLocalOperationLease?> AcquireLocalOperationLeaseCoreAsync(HomeLocalProfileIdentity profiles,
        AuthenticatedResourceActor originalActor, IHomeStateCommitActorGuard? issuerGuard,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profiles); ArgumentNullException.ThrowIfNull(originalActor);
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        if (!profiles.IsBoundToStore(this)) return null;
        if (issuerGuard is not null && issuerGuard is not IHomeOriginalScopedStateCommitActorGuard)
            throw new InvalidOperationException("The actual Home acquisition issuer has no original scoped callback producer.");
        bool gateHeld = false, transferred = false; FileStream? processLock = null; Exception? primary = null;
        try
        {
            await HomeOriginalScopedSourceCallbacks.AwaitAsync(() => _gate.WaitAsync(cancellationToken),
                originalSynchronousScope, retainOriginalTask, () => gateHeld = true).ConfigureAwait(false);
            processLock = await HomeOriginalScopedSourceCallbacks.AwaitAsync(() => AcquireProcessLockAsync(cancellationToken),
                originalSynchronousScope, retainOriginalTask, actual => processLock = actual).ConfigureAwait(false);
            var read = await HomeOriginalScopedSourceCallbacks.AwaitAsync(() => ReadUnlockedAsync(cancellationToken),
                originalSynchronousScope, retainOriginalTask).ConfigureAwait(false);
            if (!read.IsSuccess || !await HomeOriginalScopedSourceCallbacks.AwaitAsync(() => profiles.CheckAsync(read.State!, originalActor,
                    HomeStateCommitPhase.Admission, originalSynchronousScope, retainOriginalTask, cancellationToken).AsTask(),
                    originalSynchronousScope, retainOriginalTask).ConfigureAwait(false)) return null;
            if (issuerGuard is IHomeOriginalScopedStateCommitActorGuard scoped && !await HomeOriginalScopedSourceCallbacks.AwaitAsync(
                    () => scoped.CheckAsync(read.State!, originalActor, HomeStateCommitPhase.Admission,
                        originalSynchronousScope, retainOriginalTask, cancellationToken).AsTask(),
                    originalSynchronousScope, retainOriginalTask).ConfigureAwait(false)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var lease = new LocalOperationLease(_gate, processLock ?? throw new InvalidOperationException("The actual Home process lock was not retained."),
                read.State!, ReadUnlockedAsync, profiles, originalActor, issuerGuard);
            transferred = true; processLock = null; return lease;
        }
        catch (Exception cause) { primary = cause; throw; }
        finally
        {
            Exception? cleanup = null;
            try
            {
                if (processLock is { } actual)
                    await HomeOriginalScopedSourceCallbacks.AwaitAsync(() => actual.DisposeAsync().AsTask(),
                        originalSynchronousScope, retainOriginalTask).ConfigureAwait(false);
            }
            catch (Exception cause) { cleanup = cause; }
            finally { if (gateHeld && !transferred) _gate.Release(); }
            if (cleanup is not null) throw new AggregateException("Actual scoped Home acquisition and independent process-lock cleanup failed.",
                primary is null ? new Exception[] { cleanup } : new Exception[] { primary, cleanup });
        }
    }
}
