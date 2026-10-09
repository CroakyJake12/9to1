using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeDeveloperWorkspaceExecutionConsentSource
{
    private sealed partial class Consent
    {
        internal bool IsIssuedEntry(IWorkspaceOriginalProcessStartEntry sameEntry)
        {
            Entry? actual; bool recognized;
            lock (_gate) { actual = _entry; recognized = !_retiring && _entryAcquisition?.IsCompletedSuccessfully == true && ReferenceEquals(actual, sameEntry); }
            return recognized && actual is { IsClosing: false };
        }
        internal Task<IWorkspaceOriginalProcessStartEntry> EnterAsync(CancellationToken token)
        {
            DemandLive(); Task<IWorkspaceOriginalProcessStartEntry> actual; TaskCompletionSource begin;
            lock (_gate)
            {
                if (_entryAcquisition is not null) return _entryAcquisition;
                if (_retiring || !Acquisition.IsCompletedSuccessfully || _capability is null || _attestation is null)
                    throw new UnauthorizedAccessException("The genuine original project execution claim must complete first.");
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously); actual = EnterPublishedAsync(begin.Task, token); _entryAcquisition = actual;
            }
            begin.SetResult(); return actual;
        }
        private async Task<IWorkspaceOriginalProcessStartEntry> EnterPublishedAsync(Task begin, CancellationToken token)
        {
            await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var sources = NewSources(this);
            try
            {
                return await sources.RunToOriginalSettlementAsync<IWorkspaceOriginalProcessStartEntry>(async () =>
                {
                    await ValidateSourcesAsync(sources, token).ConfigureAwait(false);
                    IAsyncDisposable? completion = null; IHomeLocalOperationLease? home = null;
                    IDeveloperWorkspaceOriginalExecutionCommitPin? nativePin = null;
                    return await CloudflareOriginalPartialEntryCustody.RunOriginalAsync<IWorkspaceOriginalProcessStartEntry>(sources, async () =>
                    {
                        // Native document/registration/root custody comes BEFORE Home. No logical
                        // Files/store lease is held across Home entry acquisition.
                        nativePin = await sources.CaptureOriginalAcquisitionAsync(() => _scopedBindingSource.AcquireOriginalExecutionPinWithinSourceAsync(Binding,
                            body => RunOriginalScopedSource(sources, body), actual => RetainOriginalScopedTask(sources, actual), token), actual =>
                        {
                            if (actual is null || !CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => _pinCustody.IsOwnedOriginalExecutionPin(Binding, actual)))
                                throw new UnauthorizedAccessException("The returned native pin has no SAME private historical issuer custody.");
                            nativePin = actual; // Capture a genuine late product before any prior scope error escapes.
                        }).ConfigureAwait(false);
                        if (!sources.Invoke(() => _commitSource.IsIssuedOriginalExecutionPin(Binding, nativePin)))
                            throw new UnauthorizedAccessException("The SAME actual native saved-root pin is no longer eligible for use.");
                        sources.Invoke(() => { nativePin.DemandOriginalExecutionBinding(); return true; });
                        completion = await sources.CaptureOriginalAcquisitionAsync(() => _capability!.AcquireCommitCompletionLeaseAsync(Owner._broker, token),
                            actual => completion = actual).ConfigureAwait(false)
                            ?? throw new UnauthorizedAccessException("The original Home execution completion lease is unavailable.");
                        home = await sources.CaptureOriginalAcquisitionAsync(() => Owner._store.AcquireLocalOperationLeaseCoreAsync(Owner._profiles,
                            Binding.OriginalActor, new ClaimedActorGuard(this, sources),
                            body => RunOriginalScopedSource(sources, body), actualTask => RetainOriginalScopedTask(sources, actualTask), token), actual => home = actual).ConfigureAwait(false)
                            ?? throw new UnauthorizedAccessException("The actual held Home execution entry was rejected.");
                        if (home is not IHomeOriginalScopedLocalOperationLease scopedHome)
                            throw new InvalidOperationException("The actual held execution entry requires the original scoped Home callback producer.");
                        if (!await sources.AwaitAsync(sources.Invoke(() => scopedHome.IsCurrentAsync(
                            body => RunOriginalScopedSource(sources, body), actual => RetainOriginalScopedTask(sources, actual), token))).ConfigureAwait(false))
                            throw new UnauthorizedAccessException("The held Home actor/claim changed before native start admission.");
                        DemandLive();
                        sources.Invoke(() => { nativePin.DemandOriginalExecutionBinding(); return true; });
                        var entry = new Entry(this, home, completion, nativePin, token);
                        lock (_gate)
                        {
                            if (_retiring || _entry is not null) throw new UnauthorizedAccessException("The original process-start entry is retired or already issued.");
                            _entry = entry;
                        }
                        home = null; completion = null; nativePin = null; return entry;
                    }, () =>
                    {
                        var closes = new List<Func<ValueTask>>();
                        if (home is { } actualHome) closes.Add(actualHome.DisposeAsync);
                        if (completion is { } actualCompletion) closes.Add(actualCompletion.DisposeAsync);
                        if (nativePin is { } actualPin) closes.Add(actualPin.DisposeAsync);
                        return closes;
                    }).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            catch (Exception primary)
            {
                sources.Retain(primary);
                // A scope may fault after returning a successful acquisition. The real
                // entry remains privately owned and closes before the failed acquisition settles.
                if (_entry is { } actualEntry)
                    try { await sources.ObserveOriginalCloseAsync(actualEntry.DisposeAsync).ConfigureAwait(false); }
                    catch (Exception cause) { sources.Retain(cause); }
                await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                throw new AggregateException("Original held execution entry acquisition and independent late cleanup failed.", sources.OriginalErrors);
            }
            finally
            {
                foreach (var actual in sources.OriginalTasks) _ = _sources.Track(actual);
                foreach (var cause in sources.OriginalErrors) _sources.Retain(cause);
            }
        }
        private sealed class ClaimedActorGuard(Consent consent, CloudflareOriginalTaskLedger sources) : IHomeOriginalScopedStateCommitActorGuard
        {
            public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
                HomeStateCommitPhase phase, CancellationToken token) => CheckAsync(state, expected, phase,
                    body => RunOriginalScopedSource(sources, body), actual => RetainOriginalScopedTask(sources, actual), token);
            public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
                HomeStateCommitPhase phase, Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
            {
                if (expected != consent.Binding.OriginalActor || consent._attestation is null || !consent.IsLive
                    || !ownerOf(consent)._broker.IsClaimedAttestationCurrentInState(consent._attestation, expected, state)) return false;
                // Already-held Home readUnlocked/current profile only; no Files/source/completion/store acquisition.
                return await sources.AwaitAsync(sources.Invoke(() => ownerOf(consent)._profiles.CheckAsync(state, expected, phase, originalSynchronousScope, retainOriginalTask, token))).ConfigureAwait(false);
            }
            private static HomeDeveloperWorkspaceExecutionConsentSource ownerOf(Consent consent) => consent.Owner;
        }
        private sealed class Entry(Consent consent, IHomeLocalOperationLease home, IAsyncDisposable completion,
            IDeveloperWorkspaceOriginalExecutionCommitPin nativePin, CancellationToken token)
            : IWorkspaceOriginalProcessStartEntry
        {
            private readonly object _gate = new();
            private readonly CloudflareOriginalTaskLedger _closing = new();
            private bool _invoked;
            private Task? _close;
            internal Task? OriginalClose { get { lock (_gate) return _close; } }
            internal bool IsClosing { get { lock (_gate) return _close is not null; } }
            internal bool NativeStartReturned { get; private set; }
            public void DemandExternalOriginalProcessStartEntryJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            public void DemandOriginalProcessStart(string root, string target, string sha)
            {
                consent.DemandLive(); token.ThrowIfCancellationRequested();
                // SAME retained native pin AND genuine held Home claim, at finite native Start.
                // This descriptor-only demand performs no profile/store/Files/policy acquisition.
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { nativePin.DemandOriginalExecutionBinding(); return true; }); }
                catch (Exception cause) { _closing.Retain(cause); throw; }
                lock (_gate)
                {
                    if (_close is not null || _invoked || root != consent.Binding.CanonicalRoot || string.IsNullOrWhiteSpace(target)
                        || sha.Length != 64 || !sha.All(Uri.IsHexDigit) || !ReferenceEquals(consent._entry, this)
                        || consent._entryAcquisition?.IsCompletedSuccessfully != true || !consent._capability!.IsUncompletedClaim(consent.Owner._broker))
                        throw new UnauthorizedAccessException("SAME unconsumed held project/root execution entry is required.");
                }
            }
            public T RunOriginalProcessStart<T>(string root, string target, string sha, Func<T> originalStart)
            {
                ArgumentNullException.ThrowIfNull(originalStart);
                lock (_gate)
                {
                    DemandOriginalProcessStart(root, target, sha);
                    _invoked = true;
                    try
                    {
                        var result = CloudflareOriginalExecutionGuard.InvokeOriginal(this, originalStart);
                        NativeStartReturned = result is not bool started || started;
                        return result;
                    }
                    catch (OperationCanceledException cause) { _closing.Retain(cause); throw new AggregateException("Synchronous original native start fault.", cause); }
                    catch (Exception cause) { _closing.Retain(cause); throw; }
                }
            }
            public ValueTask DisposeAsync()
            {
                DemandExternalOriginalProcessStartEntryJoin(); Task actual; TaskCompletionSource begin;
                lock (_gate)
                {
                    if (_close is not null) return new(_close);
                    begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _closing.BindOriginalOwner(this);
                    _closing.BindOriginalCallerCallback(consent._guardedCaller); actual = ClosePublishedAsync(begin.Task); _close = actual;
                }
                begin.SetResult(); return new(actual);
            }
            private async Task ClosePublishedAsync(Task begin)
            {
                await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                // Start every actual close independently, Home first. No sibling failure/hold
                // can skip native custody or completion release before the process waits.
                var closes = new List<Task>();
                foreach (var close in new Func<ValueTask>[] { home.DisposeAsync, completion.DisposeAsync, nativePin.DisposeAsync })
                    try { _ = _closing.Invoke(() => { var actual = close().AsTask(); closes.Add(actual); _ = _closing.Track(actual); return actual; }); }
                    catch (Exception cause) { _closing.Retain(cause); }
                foreach (var actual in closes)
                    try { await _closing.AwaitAsync(actual).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
                await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Actual process-start Home/completion/native pin cleanup failed.", _closing.OriginalErrors);
            }
        }
    }
}
