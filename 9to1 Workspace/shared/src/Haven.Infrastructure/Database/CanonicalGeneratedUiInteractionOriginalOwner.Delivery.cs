using System.Runtime.CompilerServices;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class CanonicalGeneratedUiInteractionOriginalOwner
{
    private readonly ConditionalWeakTable<Intent, SaveProcess> _saveProcesses = new();
    private readonly ConditionalWeakTable<ICanonicalGeneratedUiOriginalSaveObservation, SaveDelivery> _saveDeliveries = new();
    private readonly List<SaveProcess> _ownedSaveProcesses = [];
    private readonly List<SaveDelivery> _ownedSaveDeliveries = [];
    private sealed class SaveProcess(Intent intent)
    {
        internal readonly Intent Intent = intent;
        internal Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment>? Driver;
        internal Task? Observer;
        internal Commit? Commit;
        internal int Joined;
    }
    public Task<ICanonicalGeneratedUiOriginalSaveObservation> StartOriginalSaveProcessWithinSourceAsync(
        ICanonicalGeneratedUiOriginalSaveIntent sameIntent, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var intent = RequireIntent(sameIntent);
        var source = CreateOriginalInteractionSource(scope, retain);
        return Admit(source, async () =>
        {
            await source.Read(() => RevalidateOriginalSaveWithinSourceAsync(intent, source.Run, source.Retain, token)).ConfigureAwait(false);
            await PruneOriginalSaveDeliveries(source).ConfigureAwait(false);
            SaveDelivery? delivery = null; var errors = new List<Exception>();
            try
            {
                source.RunProductive(() =>
                {
                    token.ThrowIfCancellationRequested();
                    SaveProcess process;
                    lock (_gate)
                    {
                        ObjectDisposedException.ThrowIf(_retiring, this);
                        if (OriginalHomeWriteSource is null) throw new InvalidOperationException("The actual individual Home generated interaction WRITE source is unavailable.");
                        _ownedSaveDeliveries.RemoveAll(item => item.HasJoinedClose);
                        if (_ownedSaveDeliveries.Count >= 128) throw new InvalidOperationException("Original save delivery custody is full; unresolved deliveries remain retained.");
                        if (!_saveProcesses.TryGetValue(intent, out process!))
                        {
                            if (_ownedSaveProcesses.Count >= 128) throw new InvalidOperationException("Original save process custody is full; unresolved processes remain retained.");
                            process = new(intent); _saveProcesses.Add(intent, process); _ownedSaveProcesses.Add(process);
                        }
                        delivery = new(this, process); _ownedSaveDeliveries.Add(delivery); _saveDeliveries.Add(delivery, delivery);
                    }
                    // No presentation scope or retainer survives into the accepted
                    // business driver. Its SAME raw task is already owned by Commit.
                    var raw = CommitOriginalSaveWithinSourceAsync(intent, body => body(), _ => { }, CancellationToken.None);
                    lock (_gate)
                    {
                        if (process.Driver is null)
                        { process.Driver = raw; process.Observer = ObserveOriginalSaveProcess(process, raw); }
                        if (!ReferenceEquals(process.Driver, raw)) throw new InvalidOperationException("The original save changed its cached business driver.");
                        if (_commits.TryGetValue(intent, out var commit)) process.Commit = commit;
                    }
                    delivery.Publish(raw);
                });
                await source.JoinAllAsync().ConfigureAwait(false);
            }
            catch (Exception cause) { errors.Add(cause); }
            if (errors.Count != 0 && delivery is not null)
            {
                delivery.RequestOriginalRetirement();
                try { await delivery.CloseAndDrainOriginalAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            }
            CanonicalSqliteOriginalStoreOwner.Throw(errors);
            return (ICanonicalGeneratedUiOriginalSaveObservation)(delivery ?? throw new InvalidOperationException("No private original save delivery was captured."));
        });
    }
    private static async Task ObserveOriginalSaveProcess(SaveProcess process, Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> raw)
    { try { await raw.ConfigureAwait(false); } catch { } finally { Volatile.Write(ref process.Joined, 1); } }
    private async Task PruneOriginalSaveDeliveries(CanonicalSqliteOriginalSourceScope source)
    {
        SaveDelivery[] deliveries; SaveProcess[] processes;
        lock (_gate) { deliveries = _ownedSaveDeliveries.ToArray(); processes = _ownedSaveProcesses.ToArray(); }
        foreach (var delivery in deliveries)
            if (delivery.OriginalClose is { IsCompletedSuccessfully: true } close)
            { await source.Read(() => close).ConfigureAwait(false); delivery.AcknowledgeClose(close); }
        foreach (var process in processes)
        {
            if (process.Driver is not { IsCompleted: true } raw || process.Observer is not { } observer) continue;
            await source.Read(() => observer).ConfigureAwait(false);
            var healthy = source.InvokeProductive(() => raw.IsCompletedSuccessfully || IsAcknowledgedOriginalSaveSourceRefusal(raw));
            if (!healthy) continue;
            lock (_gate)
                if (ReferenceEquals(process.Driver, raw) && ReferenceEquals(process.Observer, observer) && Volatile.Read(ref process.Joined) == 1 &&
                    !_ownedSaveDeliveries.Any(item => ReferenceEquals(item.Process, process) && !item.HasJoinedClose))
                    _ownedSaveProcesses.Remove(process);
        }
    }
    public bool IsIssuedOriginalSaveObservation(ICanonicalGeneratedUiOriginalSaveObservation same) =>
        same is SaveDelivery own && ReferenceEquals(own.Owner, this) && _saveDeliveries.TryGetValue(same, out var issued) && ReferenceEquals(own, issued);
    private void RetireOriginalSaveDeliveries()
    { SaveDelivery[] deliveries; lock (_gate) deliveries = _ownedSaveDeliveries.ToArray(); foreach (var delivery in deliveries) delivery.RequestOriginalRetirement(); }
    private async Task JoinOriginalSaveDeliveries()
    {
        SaveDelivery[] deliveries; SaveProcess[] processes;
        lock (_gate) { deliveries = _ownedSaveDeliveries.ToArray(); processes = _ownedSaveProcesses.ToArray(); }
        var errors = new List<Exception>();
        foreach (var process in processes)
            if (process.Observer is { } observer)
                try { await observer.ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        foreach (var delivery in deliveries)
            try { var close = delivery.CloseAndDrainOriginalAsync(); await close.ConfigureAwait(false); delivery.AcknowledgeClose(close); }
            catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }
    private sealed class SaveDelivery(CanonicalGeneratedUiInteractionOriginalOwner owner, SaveProcess process) : ICanonicalGeneratedUiOriginalSaveObservation
    {
        internal readonly CanonicalGeneratedUiInteractionOriginalOwner Owner = owner;
        internal readonly SaveProcess Process = process;
        private readonly object _gate = new();
        private readonly TaskCompletionSource<Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment>> _publication = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _retired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConditionalWeakTable<ICanonicalGeneratedUiOriginalSaveCompletion, SaveCompletion> _completions = new();
        private readonly AsyncLocal<int> _inside = new();
        [ThreadStatic] private static SaveDelivery? _physical;
        private Task<ICanonicalGeneratedUiOriginalSaveCompletion>? _wait;
        private Task? _close;
        private int _closeJoined;
        public ICanonicalGeneratedUiOriginalSaveIntent OriginalIntent => Process.Intent;
        public Task? OriginalClose { get { lock (_gate) return _close; } }
        internal bool HasJoinedClose => Volatile.Read(ref _closeJoined) == 1 && OriginalClose is { IsCompletedSuccessfully: true };
        internal void AcknowledgeClose(Task same)
        { lock (_gate) { if (!ReferenceEquals(same, _close) || !same.IsCompletedSuccessfully) throw new InvalidOperationException("Independently join the SAME healthy original delivery close."); Volatile.Write(ref _closeJoined, 1); } }
        internal void Publish(Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> raw) => _publication.TrySetResult(raw);
        public Task<ICanonicalGeneratedUiOriginalSaveCompletion> WaitOriginalCompletionAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); TaskCompletionSource? start = null; Task<ICanonicalGeneratedUiOriginalSaveCompletion> actual;
            lock (_gate) { if (_wait is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _wait = Wait(start.Task); } actual = _wait; }
            start?.SetResult(); return actual;
        }
        private async Task<ICanonicalGeneratedUiOriginalSaveCompletion> Wait(Task start)
        {
            await start.ConfigureAwait(false); var prior = _inside.Value; _inside.Value = prior + 1;
            try
            {
                if (!_publication.Task.IsCompleted && await Task.WhenAny(_publication.Task, _retired.Task).ConfigureAwait(false) != _publication.Task)
                    return Issue(CanonicalGeneratedUiOriginalCompletionKind.ObservationRetired, null);
                var raw = await _publication.Task.ConfigureAwait(false);
                if (!raw.IsCompleted && await Task.WhenAny(raw, _retired.Task).ConfigureAwait(false) != raw)
                    return Issue(CanonicalGeneratedUiOriginalCompletionKind.ObservationRetired, null);
                ICanonicalGeneratedUiOriginalSaveAcknowledgment? acknowledgment;
                try { acknowledgment = await raw.ConfigureAwait(false); }
                catch (Exception cause)
                {
                    if (Invoke(() => Owner.IsAcknowledgedOriginalSaveSourceRefusal(raw))) return Issue(CanonicalGeneratedUiOriginalCompletionKind.DeclinedBeforeEffect, null);
                    var errors = new List<Exception>(); CanonicalSqliteOriginalStoreOwner.Capture(errors, raw, cause); CanonicalSqliteOriginalStoreOwner.Throw(errors); throw;
                }
                if (Process.Commit?.Atomic is not { } atomic || acknowledgment is null ||
                    !Invoke(() => Owner.IsOwnedOriginalSaveAcknowledgment(Process.Intent, acknowledgment, atomic)))
                    throw new UnauthorizedAccessException("The SAME process-owned atomic save did not issue this acknowledgment.");
                return Issue(CanonicalGeneratedUiOriginalCompletionKind.Saved, acknowledgment);
            }
            finally { _inside.Value = prior; }
        }
        private ICanonicalGeneratedUiOriginalSaveCompletion Issue(CanonicalGeneratedUiOriginalCompletionKind kind, ICanonicalGeneratedUiOriginalSaveAcknowledgment? acknowledgment)
        { var actual = new SaveCompletion(this, kind, acknowledgment); _completions.Add(actual, actual); return actual; }
        public bool IsIssuedOriginalCompletion(ICanonicalGeneratedUiOriginalSaveCompletion same) => same is SaveCompletion own &&
            ReferenceEquals(own.OriginalObservation, this) && _completions.TryGetValue(same, out var issued) && ReferenceEquals(own, issued);
        private T Invoke<T>(Func<T> body)
        { var previous = _physical; _physical = this; try { return body(); } finally { _physical = previous; } }
        public void RequestOriginalRetirement() => _retired.TrySetResult();
        public void DemandExternalOriginalJoin()
        { if (_inside.Value != 0 || ReferenceEquals(_physical, this)) throw new InvalidOperationException("An original save delivery cannot join its own cached close."); }
        public Task CloseAndDrainOriginalAsync()
        { DemandExternalOriginalJoin(); RequestOriginalRetirement(); lock (_gate) return _close ??= Close(); }
        private async Task Close()
        { Task<ICanonicalGeneratedUiOriginalSaveCompletion>? wait; lock (_gate) wait = _wait; if (wait is not null) await wait.ConfigureAwait(false); }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    }
    private sealed class SaveCompletion(SaveDelivery observation, CanonicalGeneratedUiOriginalCompletionKind kind,
        ICanonicalGeneratedUiOriginalSaveAcknowledgment? acknowledgment) : ICanonicalGeneratedUiOriginalSaveCompletion
    {
        public ICanonicalGeneratedUiOriginalSaveObservation OriginalObservation { get; } = observation;
        public CanonicalGeneratedUiOriginalCompletionKind Kind { get; } = kind;
        public ICanonicalGeneratedUiOriginalSaveAcknowledgment? Acknowledgment { get; } = acknowledgment;
    }
}
