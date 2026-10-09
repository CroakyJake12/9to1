using System.Runtime.CompilerServices;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

public sealed partial class CanonicalGeneratedUiInteractionOriginalOwner
{
    private readonly ConditionalWeakTable<Intent, Commit> _commits = new();
    private readonly ConditionalWeakTable<Task, Commit> _commitTasks = new();
    private readonly AsyncLocal<Commit?> _executingCommit = new();
    [ThreadStatic] private static Dictionary<CanonicalGeneratedUiInteractionOriginalOwner, Commit?>? _physicalCommits;
    private readonly List<Commit> _acceptedCommits = [];
    private readonly List<Task> _pendingWithdrawals = [];
    private readonly List<CanonicalSqliteOriginalSourceScope> _withdrawalSources = [];
    private sealed class Commit(Intent intent, CanonicalSqliteOriginalSourceScope source)
    {
        internal readonly Intent Intent = intent;
        internal readonly CanonicalSqliteOriginalSourceScope Source = source;
        internal readonly TaskCompletionSource Dispatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> Driver = null!;
        internal Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment>? Inner, Atomic;
        internal ICanonicalGeneratedUiOriginalHomeWriteClaim? Claim;
        internal ICanonicalGeneratedUiOriginalSourcePin? SourcePin;
        internal CanonicalSqliteOriginalStoreLease? NativePin;
        internal Task? SourceClose, NativeClose, Withdrawal, AcknowledgedAcquisition, Release;
        internal bool SourceCloseJoined, NativeCloseJoined, DispatchSettled;
        internal Acknowledgment? Acknowledgment;
    }
    public bool IsOriginalSaveInvocation(ICanonicalGeneratedUiOriginalSaveIntent same)
    {
        var current = _physicalCommits?.TryGetValue(this, out var observed) == true ? observed : null;
        return current is not null && ReferenceEquals(current.Intent, same) && ReferenceEquals(_executingCommit.Value, current) &&
            _commits.TryGetValue(current.Intent, out var issued) && ReferenceEquals(issued, current) && !current.Driver.IsCompleted;
    }
    public void BindOriginalHomeWriteSource(ICanonicalGeneratedUiOriginalHomeWriteSource actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        if (actual is not HomeCanonicalGeneratedUiInteractionWriteSource concrete ||
            !concrete.HasOriginalProducerComposition(_store.OriginalProfiles, this))
            throw new UnauthorizedAccessException("Bind the SAME sealed Home source over this producer and original store profiles.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_home is not null) throw new InvalidOperationException("The original generated interaction WRITE source is already bound.");
            _home = actual;
        }
    }
    private Action<Action> CommitScope(Action<Action> caller, Commit? same) => body => caller(() =>
    {
        var physical = _physicalCommits ??= new(ReferenceEqualityComparer.Instance);
        var had = physical.TryGetValue(this, out var prior); physical[this] = same;
        try { body(); } finally { if (had) physical[this] = prior; else physical.Remove(this); }
    });
    public Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> CommitOriginalSaveWithinSourceAsync(
        ICanonicalGeneratedUiOriginalSaveIntent same, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        DemandHealthyOriginalCallbacks();
        var intent = RequireIntent(same); Commit current; TaskCompletionSource begin;
        lock (_gate)
        {
            if (_commits.TryGetValue(intent, out var existing)) return existing.Driver;
            token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_retiring, this);
            Commit? capture = null;
            var source = CreateOriginalInteractionSource(body => CommitScope(scope, capture)(body), retain);
            current = new(intent, source); capture = current;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            current.Driver = Run(begin.Task, current);
            _commits.Add(intent, current); _commitTasks.Add(current.Driver, current); _acceptedCommits.Add(current);
            var original = new Original(source) { Raw = current.Driver };
            _originals.Add(original); original.Observation = Observe(original); original.Publication.TrySetResult(current.Driver);
        }
        begin.SetResult(); return current.Driver;
        async Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> Run(Task gate, Commit actual)
        {
            await gate.ConfigureAwait(false);
            var previous = _logical.Value; _logical.Value = previous + 1;
            try
            {
                actual.Inner = _store.RetainOriginalReader(actual.Source, () => CommitDriver(actual));
                _commitTasks.Add(actual.Inner, actual); return await actual.Inner.ConfigureAwait(false);
            }
            finally { _logical.Value = previous; }
        }
    }
    private async Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> CommitDriver(Commit current)
    {
        var previous = _executingCommit.Value; _executingCommit.Value = current;
        var source = current.Source; var intent = current.Intent; var failures = new List<Exception>();
        ICanonicalGeneratedUiOriginalSaveAcknowledgment? result = null;
        try
        {
            var home = OriginalHomeWriteSource ?? throw new InvalidOperationException("The actual individual Home generated interaction WRITE is unavailable.");
            await source.Read(() => RevalidateOriginalSaveWithinSourceAsync(intent, source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false);
            Task<ICanonicalGeneratedUiOriginalHomeWriteClaim>? acquisition = null;
            try
            {
                await source.ReadCapture(() => acquisition = home.AcquireOriginalWriteWithinSourceAsync(intent,
                    source.Run, source.Retain, claim =>
                    {
                        current.Claim = claim; source.CaptureOriginalResource(claim);
                        _commitTasks.Add(claim.OriginalAcquisition, current); CapturePendingWithdrawal(current);
                    }, CancellationToken.None), claim => current.Claim = claim).ConfigureAwait(false);
            }
            catch
            {
                if (acquisition is null || current.Claim is null ||
                    !source.InvokeOwningCleanup(() => home.IsAcknowledgedOriginalWriteRefusal(acquisition))) throw;
                // No accepted native or Den pin exists. Exact declined acquisition
                // must be independently closed and acknowledged by its actual issuer.
                MarkDispatchSettled(current);
                await source.CloseAsync(current.Claim).ConfigureAwait(false);
                if (!source.AcknowledgeOriginalExternalPreEffectRefusal(acquisition, home.IsAcknowledgedOriginalWriteRefusal)) throw;
                await source.JoinAllAsync().ConfigureAwait(false);
                if (!source.IsHealthySettled) throw;
                current.AcknowledgedAcquisition = acquisition;
                var refusal = new InvalidOperationException("The individual Home generated interaction WRITE was declined before any SQL effect.");
                source.RegisterOriginalPreEffectRefusal(refusal); throw refusal;
            }
            var claim = current.Claim ?? throw new UnauthorizedAccessException("No SAME original Home claim was retained.");
            source.RunProductive(() => { if (!home.IsIssuedOriginalWriteClaim(claim, intent)) throw new UnauthorizedAccessException("The configured Home source did not issue this exact claim."); });
            await source.Read(() => RevalidateOriginalSaveWithinSourceAsync(intent, source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false);
            await source.ReadCapture(() => _store.AcquireOriginalProtectedWriterPinWithinSourceAsync(intent.Actor,
                source.Run, source.Retain, CancellationToken.None), pin => current.NativePin = pin).ConfigureAwait(false);
            if (current.NativePin!.OriginalIdentity != intent.OriginalStoreIdentity)
                throw new UnauthorizedAccessException("The reviewed native canonical store identity changed.");
            await DemandCurrent(intent.Observation.Origin, intent.OriginalStoreIdentity, intent.OriginalStoreOwnership, source, CancellationToken.None).ConfigureAwait(false);
            // Actual message membership/configuration pin is acquired after approval,
            // before held Home entry; its getters are pure while Den locks remain held.
            await source.ReadCapture(() => _origins.AcquireOriginalCommitPinWithinSourceAsync(intent.Observation.Origin.Source,
                source.Run, source.Retain, CancellationToken.None), pin => current.SourcePin = pin).ConfigureAwait(false);
            source.RunProductive(() => { if (!ReferenceEquals(current.SourcePin!.OriginalSnapshot, intent.Observation.Origin.Source))
                throw new UnauthorizedAccessException("The actual source pin belongs to another original snapshot."); });
            await source.JoinAllAsync().ConfigureAwait(false);
            await source.Read(() => home.AcquireOriginalCommitEntryWithinSourceAsync(claim, source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false);
            await source.Read(() => home.ValidateOriginalCommitWithinSourceAsync(claim, source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var atomic = Atomic(start.Task, current, home); current.Atomic = atomic; _commitTasks.Add(atomic, current);
            Exception? publication = null;
            try { source.RunProductive(() => { home.RetainOriginalSaveCommit(claim, atomic); source.Retain(atomic); }); }
            catch (Exception cause) { publication = cause; }
            start.SetResult(publication is null);
            try { result = await atomic.ConfigureAwait(false); } catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(failures, atomic, cause); }
            if (publication is not null) failures.Add(publication);
            MarkDispatchSettled(current);
            try { await ReleaseResources(current).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            try { await source.Read(() => home.CompleteOriginalWriteWithinSourceAsync(claim, atomic,
                source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false); }
            catch (Exception cause) { failures.Add(cause); }
        }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            MarkDispatchSettled(current); // Every original acquisition has terminally returned, including late results.
            try { await ReleaseResources(current).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            if (current.Claim is not null) try { await source.CloseAsync(current.Claim).ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            try { await source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            _executingCommit.Value = previous;
        }
        CanonicalSqliteOriginalStoreOwner.Throw(failures);
        DemandHealthyOriginalCallbacks();
        return result ?? throw new InvalidOperationException("No exact healthy generated interaction acknowledgment was observed.");
    }
    private async Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> Atomic(Task<bool> begin, Commit current,
        ICanonicalGeneratedUiOriginalHomeWriteSource home)
    {
        if (!await begin.ConfigureAwait(false)) throw new InvalidOperationException("Atomic save publication failed before SQL effects.");
        void Demand() { DemandOriginalSaveCommit(current.Intent); home.DemandOriginalCommit(current.Claim!, current.Intent); }
        current.Source.RunProductive(() => { Demand(); current.NativePin!.BeginPinnedOriginalWriter(Demand); });
        var operation = await WriteAtomicRows(current, Demand, CancellationToken.None).ConfigureAwait(false);
        await current.NativePin!.CommitPinnedOriginalAsync(Demand, CancellationToken.None).ConfigureAwait(false);
        var ack = new Acknowledgment(current.Intent, operation);
        current.Source.RunProductive(() => { Demand(); current.Acknowledgment = ack; _acknowledgments.Add(ack, ack); }); return ack;
    }
    public void DemandOriginalSaveCommit(ICanonicalGeneratedUiOriginalSaveIntent same)
    {
        DemandHealthyOriginalCallbacks();
        var intent = RequireIntent(same);
        var current = _physicalCommits?.TryGetValue(this, out var physical) == true ? physical : _executingCommit.Value;
        if (current is null || !ReferenceEquals(current.Intent, intent) || current.NativePin is null || current.SourcePin is null ||
            !_store.IsIssuedOriginalLease(current.NativePin) || current.NativePin.OriginalIdentity != intent.OriginalStoreIdentity)
            throw new UnauthorizedAccessException("No SAME original save/native/source pin is active.");
        current.NativePin.DemandPinnedOriginalPhysical(); current.SourcePin.DemandOriginalCurrent();
        DemandRuntime(intent.Observation.Origin);
    }
    private void MarkDispatchSettled(Commit current)
    { lock (_gate) current.DispatchSettled = true; current.Dispatch.TrySetResult(); }
    private Task ReleaseResources(Commit current)
    {
        TaskCompletionSource? start = null; Task actual;
        lock (_gate)
        {
            if (current.Release is null)
            { start = new(TaskCreationOptions.RunContinuationsAsynchronously); current.Release = ReleaseCore(start.Task, current); }
            actual = current.Release;
        }
        start?.SetResult(); return actual;
    }
    private async Task ReleaseCore(Task start, Commit current)
    {
        await start.ConfigureAwait(false); var failures = new List<Exception>();
        Task? sourceClose = null, nativeClose = null;
        // Acquire/capture BOTH original close factories before either await. A
        // held or failed source child never skips the independent native sibling.
        if (current.SourcePin is { } sourcePin)
            try { sourceClose = sourcePin.CloseAndDrainOriginalAsync(); current.SourceClose = sourceClose; current.Source.Retain(sourceClose); }
            catch (Exception cause) { failures.Add(cause); }
        if (current.NativePin is { } nativePin)
            try { nativeClose = nativePin.CloseAndDrainAsync(); current.NativeClose = nativeClose; current.Source.Retain(nativeClose); }
            catch (Exception cause) { failures.Add(cause); }
        if (sourceClose is not null) try { await sourceClose.ConfigureAwait(false); current.SourceCloseJoined = true; }
            catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(failures, sourceClose, cause); }
        if (nativeClose is not null) try { await nativeClose.ConfigureAwait(false); current.NativeCloseJoined = true; }
            catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(failures, nativeClose, cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(failures);
    }
    public Task WaitOriginalSaveDispatchSettledWithinSourceAsync(ICanonicalGeneratedUiOriginalSaveIntent same,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var intent = RequireIntent(same); if (!_commits.TryGetValue(intent, out var current)) throw new UnauthorizedAccessException("No SAME original save driver exists.");
        var sources = CreateOriginalInteractionSource(scope, retain, productive: false);
        return sources.Read(() => current.Dispatch.Task); // Caller token never cancels accepted business settlement.
    }
    public Task ReleaseOriginalSaveResourcesWithinSourceAsync(ICanonicalGeneratedUiOriginalSaveIntent same,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var intent = RequireIntent(same); if (!_commits.TryGetValue(intent, out var current) || !current.DispatchSettled)
            throw new UnauthorizedAccessException("Actual save dispatch must terminally settle before releasing its pins.");
        var sources = CreateOriginalInteractionSource(scope, retain, productive: false); return sources.Read(() => ReleaseResources(current));
    }
    public bool IsOriginalAtomicSaveTask(ICanonicalGeneratedUiOriginalSaveIntent same, Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> atomic) =>
        IsIssuedOriginalSaveIntent(same) && _commitTasks.TryGetValue(atomic, out var current) && ReferenceEquals(current.Intent, same) && ReferenceEquals(current.Atomic, atomic);
    public bool IsOwnedOriginalSaveAcknowledgment(ICanonicalGeneratedUiOriginalSaveIntent same,
        ICanonicalGeneratedUiOriginalSaveAcknowledgment ack, Task atomic) => atomic is Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> typed &&
        IsOriginalAtomicSaveTask(same, typed) && typed.IsCompletedSuccessfully && ReferenceEquals(typed.Result, ack) &&
        ack is Acknowledgment actual && _acknowledgments.TryGetValue(ack, out var issued) && ReferenceEquals(actual, issued) && ReferenceEquals(actual.Intent, same);
    public bool IsOwnedOriginalSaveResourcesRelease(ICanonicalGeneratedUiOriginalSaveIntent same, Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment>? atomic)
    {
        if (!IsIssuedOriginalSaveIntent(same) || !_commits.TryGetValue((Intent)same, out var current) || !current.DispatchSettled ||
            !ReferenceEquals(current.Atomic, atomic) || atomic is not null && (!atomic.IsCompleted || !IsOriginalAtomicSaveTask(same, atomic))) return false;
        return (current.SourcePin is null ? current.SourceClose is null : current.SourceCloseJoined && current.SourceClose is { } close && current.SourcePin.IsOwnedOriginalHealthyClose(close)) &&
            (current.NativePin is null ? current.NativeClose is null : current.NativeCloseJoined && current.NativeClose is { IsCompletedSuccessfully: true } native && ReferenceEquals(current.NativePin.OriginalClose, native));
    }
    public bool IsAcknowledgedOriginalSaveSourceRefusal(Task same)
    {
        if (!same.IsFaulted || !_commitTasks.TryGetValue(same, out var current) || current.Atomic is not null || current.SourcePin is not null || current.NativePin is not null ||
            current.Inner is not { } inner || !_store.IsAcknowledgedOriginalCommandRefusal(inner) || !current.Source.IsHealthySettled || current.AcknowledgedAcquisition is null) return false;
        if (ReferenceEquals(same, current.AcknowledgedAcquisition)) return OriginalHomeWriteSource?.IsAcknowledgedOriginalWriteRefusal(same) == true;
        if (!ReferenceEquals(same, current.Driver) && !ReferenceEquals(same, inner)) return false;
        return same.Exception!.InnerExceptions.Count == 1 && inner.Exception!.InnerExceptions.Count == 1 &&
            ReferenceEquals(same.Exception.InnerExceptions[0], inner.Exception.InnerExceptions[0]);
    }
    private void CapturePendingWithdrawal(Commit current)
    {
        lock (_gate) if (!_retiring || current.Claim is null || current.Withdrawal is not null) return;
        var home = OriginalHomeWriteSource; if (home is null) return;
        var source = CreateOriginalInteractionSource(body => body(), actual => { lock (_gate) _pendingWithdrawals.Add(actual); }, productive: false);
        lock (_gate)
        {
            if (current.Withdrawal is not null) return;
            _withdrawalSources.Add(source);
            current.Withdrawal = source.Read(() => home.WithdrawOriginalPendingWriteWithinSourceAsync(current.Claim!, source.Run, source.Retain, CancellationToken.None));
            _pendingWithdrawals.Add(current.Withdrawal);
        }
    }
    public void RequestOriginalPendingReviewWithdrawals()
    {
        Commit[] all; lock (_gate) { _retiring = true; all = _acceptedCommits.ToArray(); }
        foreach (var current in all) CapturePendingWithdrawal(current);
    }
}
