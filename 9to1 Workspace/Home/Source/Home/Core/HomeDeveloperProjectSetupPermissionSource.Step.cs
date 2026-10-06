using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeDeveloperProjectSetupPermissionSource
{
    private sealed partial class Permission
    {
        private sealed class Step(Permission permission, DeveloperProjectSetupStep originalStep)
        {
            private readonly object _gate = new();
            private readonly CloudflareOriginalTaskLedger _sources = new();
            private readonly CloudflareOriginalTaskLedger _closing = new();
            internal Task<IDeveloperProjectOriginalSetupStepEntry> Acquisition = null!;
            internal Entry? Entry;
            private Task? _validation; private Task? _actualStep; private object? _result;
            private Task? _close; private bool _retiring;
            internal void DemandExternalJoin()
            {
                CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
                Entry?.DemandExternalJoin();
            }
            internal void RequestRetirement() { lock (_gate) _retiring = true; }
            internal Task<IDeveloperProjectOriginalSetupStepEntry> Start(CancellationToken token)
            {
                _sources.BindOriginalOwner(this); var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Acquisition = AcquirePublishedAsync(begin.Task, token); begin.SetResult(); return Acquisition;
            }
            private async Task<IDeveloperProjectOriginalSetupStepEntry> AcquirePublishedAsync(Task begin, CancellationToken token)
            {
                await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                return await _sources.RunToOriginalSettlementAsync(async () =>
                {
                    // All NativeFiles/capture/actor/policy reads precede the held Home
                    // lease. The final Files metadata guard uses only already-held Home
                    // readUnlocked/profile checks and pure retained physical identity.
                    await _sources.AwaitAsync(_sources.Invoke(() => permission.ValidateSourcesAsync(token))).ConfigureAwait(false);
                    if (!permission._capability!.IsUncompletedClaim(permission.Owner._broker))
                        throw new UnauthorizedAccessException("The original whole-intent claim is not current.");
                    IAsyncDisposable? completion = null; IHomeLocalOperationLease? home = null;
                    return await CloudflareOriginalPartialEntryCustody.RunOriginalAsync(_sources, async () =>
                    {
                        completion = await _sources.CaptureOriginalAcquisitionAsync(
                            () => permission._capability.AcquireCommitCompletionLeaseAsync(permission.Owner._broker, token),
                            actual => completion = actual).ConfigureAwait(false)
                            ?? throw new UnauthorizedAccessException("Original setup completion admission is closing.");
                        home = await _sources.CaptureOriginalAcquisitionAsync(
                            () => permission.Owner._store.AcquireLocalOperationLeaseCoreAsync(permission.Owner._profiles,
                                permission.OriginalIntent.OriginalActor, new ClaimedActorGuard(permission, _sources), token),
                            actual => home = actual).ConfigureAwait(false)
                            ?? throw new UnauthorizedAccessException("The genuine held Home setup entry was rejected.");
                        if (!await _sources.AwaitAsync(_sources.Invoke(() => home.IsCurrentAsync(token))).ConfigureAwait(false))
                            throw new UnauthorizedAccessException("The held original Home setup actor/claim changed.");
                        permission.DemandLive();
                        var original = new Entry(this, permission, originalStep, home, completion);
                        lock (_gate)
                        {
                            if (_retiring || Entry is not null) throw new UnauthorizedAccessException("The original setup step entry is retired or already issued.");
                            Entry = original;
                        }
                        home = null; completion = null; return original;
                    }, () =>
                    {
                        var closes = new List<Func<ValueTask>>();
                        if (home is { } actualHome) closes.Add(actualHome.DisposeAsync);
                        if (completion is { } actualCompletion) closes.Add(actualCompletion.DisposeAsync);
                        return closes;
                    }).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            internal Task ValidateResultAsync(Task sameTask, object? sameResult, CancellationToken token)
            {
                Task actual; TaskCompletionSource begin;
                lock (_gate)
                {
                    if (_retiring || !Acquisition.IsCompletedSuccessfully || Entry?.OriginalClose?.IsCompletedSuccessfully != true
                        || !ReferenceEquals(Entry.OriginalBody, sameTask) || !sameTask.IsCompletedSuccessfully)
                        throw new UnauthorizedAccessException("SAME actual successful Files step and successful entry cleanup must precede result validation.");
                    if (_validation is not null)
                    {
                        if (!ReferenceEquals(_actualStep, sameTask) || !ReferenceEquals(_result, sameResult))
                            throw new UnauthorizedAccessException("An original physical result cannot be replaced or replayed.");
                        return _validation;
                    }
                    _actualStep = sameTask; _result = sameResult;
                    begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    actual = ValidatePublishedAsync(begin.Task, sameTask, sameResult, token); _validation = actual;
                }
                begin.SetResult(); return actual;
            }
            private async Task ValidatePublishedAsync(Task begin, Task sameTask, object? sameResult, CancellationToken token)
            {
                await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                await _sources.RunToOriginalSettlementAsync(async () =>
                {
                    await _sources.AwaitAsync(sameTask).ConfigureAwait(false);
                    if (_sources.OriginalErrors.Count != 0 || Entry?.OriginalClose?.IsCompletedSuccessfully != true)
                        throw new UnauthorizedAccessException("Original acquisition/step/entry cleanup is unconfirmed.");
                    await _sources.AwaitAsync(_sources.Invoke(() => permission.ValidateSourcesAsync(token))).ConfigureAwait(false);
                    if (!_sources.Invoke(() => permission._outcomeSource.IsIssuedOriginalStepOutcome(permission.OriginalIntent,
                        permission.Capture, originalStep, sameTask, sameResult)))
                        throw new UnauthorizedAccessException("No SAME private physical producer recognizes this original Task/result.");
                    await _sources.AwaitAsync(_sources.Invoke(() => permission._outcomeSource.ValidateOriginalStepOutcomeAsync(
                        permission.OriginalIntent, permission.Capture, originalStep, sameTask, sameResult, token))).ConfigureAwait(false);
                    await _sources.AwaitAsync(_sources.Invoke(() => permission.ValidateSourcesAsync(token))).ConfigureAwait(false);
                    if (!_sources.Invoke(() => permission._outcomeSource.IsIssuedOriginalStepOutcome(permission.OriginalIntent,
                        permission.Capture, originalStep, sameTask, sameResult)))
                        throw new UnauthorizedAccessException("The original physical outcome issuer retired during its final reads.");
                }).ConfigureAwait(false);
            }
            internal ValueTask CloseAsync()
            {
                DemandExternalJoin(); Task actual; TaskCompletionSource begin;
                lock (_gate)
                {
                    if (_close is not null) return new(_close); _retiring = true;
                    begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _closing.BindOriginalOwner(this);
                    actual = ClosePublishedAsync(begin.Task); _close = actual;
                }
                begin.SetResult(); return new(actual);
            }
            private async Task ClosePublishedAsync(Task begin)
            {
                await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                Task? validation; lock (_gate) validation = _validation;
                try { await _closing.AwaitAsync(Acquisition).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
                if (validation is not null)
                    try { await _closing.AwaitAsync(validation).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
                if (Entry is { } actual)
                    try { await _closing.ObserveOriginalCloseAsync(actual.DisposeAsync).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
                await _sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                foreach (var cause in _sources.OriginalErrors) _closing.Retain(cause);
                await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Original setup step sources/entry cleanup failed.", _closing.OriginalErrors);
            }
        }

        private sealed class ClaimedActorGuard(Permission permission, CloudflareOriginalTaskLedger sources) : IHomeStateCommitActorGuard
        {
            public async ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
                HomeStateCommitPhase phase, CancellationToken token)
            {
                if (expected != permission.OriginalIntent.OriginalActor || permission._attestation is null
                    || !permission.Owner._broker.IsClaimedAttestationCurrentInState(permission._attestation, expected, state)) return false;
                // Already-held Home publication/readUnlocked state only. No Files, source,
                // completion or resource lease acquisition is allowed from this guard.
                return await sources.AwaitAsync(sources.Invoke(() => permission.Owner._profiles.CheckAsync(state, expected, phase, token))).ConfigureAwait(false);
            }
        }

        private sealed class Entry(Step step, Permission permission, DeveloperProjectSetupStep originalStep,
            IHomeLocalOperationLease home, IAsyncDisposable completion) : IDeveloperProjectOriginalSetupStepEntry
        {
            private readonly object _gate = new();
            private readonly CloudflareOriginalTaskLedger _sources = new();
            private readonly CloudflareOriginalTaskLedger _closing = new();
            private readonly List<Task> _checks = [];
            private readonly List<Task> _finiteAdmissions = [];
            private Task? _body; private Task? _close; private bool _invoked;
            internal bool IsClosing { get { lock (_gate) return _close is not null; } }
            internal Task? OriginalClose { get { lock (_gate) return _close; } }
            internal Task? OriginalBody { get { lock (_gate) return _body; } }
            internal void DemandExternalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            public void DemandOriginalStepEntry(DeveloperProjectSetupStep sameStep)
            {
                permission.DemandLive();
                lock (_gate)
                    if (_close is not null || !ReferenceEquals(originalStep, sameStep) || !ReferenceEquals(step.Entry, this)
                        || !step.Acquisition.IsCompletedSuccessfully || !permission._capability!.IsUncompletedClaim(permission.Owner._broker))
                        throw new UnauthorizedAccessException("SAME original held entry/step/claim is required.");
            }
            public ValueTask<bool> CheckOriginalStepCommitAsync(DeveloperProjectSetupStep sameStep, CancellationToken token)
            {
                DemandOriginalStepEntry(sameStep); Task<bool> actual; TaskCompletionSource begin;
                lock (_gate)
                {
                    if (_close is not null || _checks.Count >= 32) throw new UnauthorizedAccessException("Original commit-check admission is closed or full.");
                    _sources.BindOriginalOwner(this); begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    actual = CheckPublishedAsync(begin.Task, sameStep, token); _checks.Add(actual);
                }
                begin.SetResult(); return new(actual);
            }
            private async Task<bool> CheckPublishedAsync(Task begin, DeveloperProjectSetupStep sameStep, CancellationToken token)
            {
                await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                DemandOriginalStepEntry(sameStep);
                var current = await _sources.AwaitAsync(_sources.Invoke(() => home.IsCurrentAsync(token))).ConfigureAwait(false);
                DemandOriginalStepEntry(sameStep); return current;
            }
            public T RunOriginalStep<T>(DeveloperProjectSetupStep sameStep, Func<T> body, CancellationToken token)
            {
                ArgumentNullException.ThrowIfNull(body); token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(sameStep);
                TaskCompletionSource admission;
                lock (_gate)
                {
                    if (_close is not null || _invoked) throw new UnauthorizedAccessException("Original physical step start is one-use.");
                    _invoked = true; admission = new(TaskCreationOptions.RunContinuationsAsynchronously); _finiteAdmissions.Add(admission.Task);
                    _sources.BindOriginalOwner(this);
                }
                try
                {
                    return _sources.Invoke(() =>
                    {
                        var actual = body();
                        if (actual is not Task raw) throw new InvalidOperationException("The fixed physical producer must return its SAME original Task.");
                        lock (_gate) _body = raw;
                        _ = _sources.Track(raw); return actual;
                    });
                }
                finally { admission.TrySetResult(); }
            }
            public ValueTask DisposeAsync()
            {
                DemandExternalJoin(); Task actual; TaskCompletionSource begin;
                lock (_gate)
                {
                    if (_close is not null) return new(_close);
                    _closing.BindOriginalOwner(this); begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    actual = ClosePublishedAsync(begin.Task); _close = actual;
                }
                begin.SetResult(); return new(actual);
            }
            private async Task ClosePublishedAsync(Task begin)
            {
                await begin.ConfigureAwait(false); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                Task[] originals; lock (_gate) originals = _checks.Concat(_finiteAdmissions).ToArray();
                foreach (var original in originals)
                    try { await _closing.AwaitAsync(original).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
                await _sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                foreach (var cause in _sources.OriginalErrors) _closing.Retain(cause);
                // Preserve both independent actual closes even after original body fault.
                try { await _closing.ObserveOriginalCloseAsync(home.DisposeAsync).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
                try { await _closing.ObserveOriginalCloseAsync(completion.DisposeAsync).ConfigureAwait(false); } catch (Exception cause) { _closing.Retain(cause); }
                await _closing.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
                if (_closing.OriginalErrors.Count != 0) throw new AggregateException("Actual setup body/commit reads/Home/completion cleanup failed.", _closing.OriginalErrors);
            }
        }
    }
}
