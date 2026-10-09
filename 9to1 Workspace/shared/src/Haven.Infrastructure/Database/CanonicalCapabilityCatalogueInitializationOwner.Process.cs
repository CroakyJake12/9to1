using System.Runtime.CompilerServices;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class CanonicalCapabilityCatalogueInitializationOwner
{
    private readonly ConditionalWeakTable<Intent, Process> _processes = new();
    private readonly ConditionalWeakTable<ICapabilityOriginalInitializationObservation, Delivery> _deliveries = new();
    private readonly List<Process> _ownedProcesses = [];
    private readonly List<Delivery> _ownedDeliveries = [];
    private sealed class Process(Intent intent)
    {
        internal readonly Intent Intent = intent;
        internal Task<ICapabilityOriginalInitializationAcknowledgment>? Driver;
        internal Task? DriverObservation;
        internal int DriverJoined;
        internal Commit? Commit;
        internal bool Withdraw;
        internal Original? Withdrawal;
    }

    public Task<ICapabilityOriginalInitializationObservation> StartOriginalInitializationProcessWithinSourceAsync(
        ICapabilityOriginalInitializationIntent sameIntent, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var intent = RequireIntent(sameIntent);
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, async () =>
        {
            await source.Read(() => RevalidateOriginalInitializationIntentWithinSourceAsync(intent,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            await PruneSettledOriginalProcessesWithinSourceAsync(source).ConfigureAwait(false);
            Delivery? delivery = null;
            var errors = new List<Exception>();
            try
            {
                source.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (OriginalHomeWriteSource is not ICapabilityOriginalInitializationHomeReviewWithdrawalSource)
                        throw new InvalidOperationException("Actual Home setup review withdrawal is required before process setup can start.");
                    Process process;
                    lock (_gate)
                    {
                        ObjectDisposedException.ThrowIf(_retiring, this);
                        _ownedDeliveries.RemoveAll(item => item.OriginalClose is { IsCompletedSuccessfully: true });
                        if (_ownedDeliveries.Count >= 128)
                            throw new InvalidOperationException("Setup delivery custody is full; unresolved deliveries remain retained.");
                        if (!_processes.TryGetValue(intent, out process!))
                        {
                            if (_ownedProcesses.Count >= 128)
                                throw new InvalidOperationException("Setup process custody is full; unresolved processes remain retained.");
                            process = new(intent); _processes.Add(intent, process); _ownedProcesses.Add(process);
                        }
                        delivery = new(this, process);
                        _ownedDeliveries.Add(delivery); _deliveries.Add(delivery, delivery);
                    }
                    // The actual business driver uses only the process owner's scopes.
                    // A view's callbacks/lifetime never remain installed across Home review.
                    var raw = CommitOriginalInitializationWithinSourceAsync(intent, body => body(), _ => { }, CancellationToken.None);
                    lock (_gate)
                    {
                        if (process.Driver is null)
                        {
                            process.Driver = raw;
                            process.DriverObservation = ObserveOriginalProcessDriverAsync(process, raw);
                        }
                        if (!ReferenceEquals(process.Driver, raw)) throw new InvalidOperationException("The actual setup process changed its cached driver.");
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
            return (ICapabilityOriginalInitializationObservation)(delivery ?? throw new InvalidOperationException("No original setup delivery was captured."));
        });
    }
    private static async Task ObserveOriginalProcessDriverAsync(Process process,
        Task<ICapabilityOriginalInitializationAcknowledgment> sameDriver)
    {
        // A terminal status alone is not a join receipt. The process retains the
        // SAME driver and independently awaits every payload before it can leave
        // the bounded cohort; unknown faults remain in that actual driver.
        try { await sameDriver.ConfigureAwait(false); }
        catch { }
        finally { Volatile.Write(ref process.DriverJoined, 1); }
    }
    private async Task PruneSettledOriginalProcessesWithinSourceAsync(CanonicalSqliteOriginalSourceScope source)
    {
        Process[] snapshot;
        lock (_gate) snapshot = _ownedProcesses.ToArray();
        foreach (var process in snapshot)
        {
            var driver = process.Driver;
            var observation = process.DriverObservation;
            // Never join an admitted business driver while its Home review or
            // SQL is pending. Only its already terminal exact-task observation
            // can be independently joined during a new finite Start.
            if (driver is null || !driver.IsCompleted || observation is null) continue;
            await source.Read(() => observation).ConfigureAwait(false);
            var acknowledged = false;
            source.Run(() => acknowledged = driver.IsCompletedSuccessfully ||
                IsAcknowledgedOriginalInitializationSourceRefusal(driver));
            if (!acknowledged) continue;
            lock (_gate)
            {
                if (!ReferenceEquals(process.Driver, driver) || !ReferenceEquals(process.DriverObservation, observation) ||
                    Volatile.Read(ref process.DriverJoined) == 0 || !observation.IsCompletedSuccessfully ||
                    _ownedDeliveries.Any(delivery => ReferenceEquals(delivery.OriginalProcess, process) &&
                        delivery.OriginalClose is not { IsCompletedSuccessfully: true })) continue;
                _ownedProcesses.Remove(process);
            }
        }
    }
    public bool IsIssuedOriginalInitializationObservation(ICapabilityOriginalInitializationObservation actual) =>
        actual is Delivery same && ReferenceEquals(same.Owner, this) &&
        _deliveries.TryGetValue(actual, out var issued) && ReferenceEquals(same, issued);

    public void RequestOriginalPendingReviewWithdrawals()
    {
        Process[] processes;
        lock (_gate) { processes = _ownedProcesses.ToArray(); foreach (var process in processes) process.Withdraw = true; }
        foreach (var process in processes) StartOriginalWithdrawal(process);
    }
    private void CaptureOriginalProcessClaim(Commit commit)
    {
        Process? process;
        lock (_gate)
        {
            if (!_processes.TryGetValue(commit.Intent, out process)) return;
            process.Commit = commit;
        }
        StartOriginalWithdrawal(process);
    }
    private void StartOriginalWithdrawal(Process process)
    {
        TaskCompletionSource? begin = null;
        lock (_gate)
        {
            if (!process.Withdraw || process.Withdrawal is not null || process.Commit is not { Claim: { } claim } commit) return;
            if (_writes is not ICapabilityOriginalInitializationHomeReviewWithdrawalSource writes)
                return; // Composition must supply actual review withdrawal before setup can start.
            var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(body => body(), commit), _ => { });
            var original = new Original(source); _originals.Add(original); process.Withdrawal = original;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var raw = Withdraw(begin.Task); Publish(original, raw);
            async Task Withdraw(Task start)
            {
                await start.ConfigureAwait(false);
                var errors = new List<Exception>();
                try { await source.Read(() => writes.WithdrawOriginalPendingWriteWithinSourceAsync(claim,
                    source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
                try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
                CanonicalSqliteOriginalStoreOwner.Throw(errors);
            }
        }
        begin?.SetResult();
    }
    private void RetireOriginalDeliveries()
    { Delivery[] snapshot; lock (_gate) snapshot = _ownedDeliveries.ToArray(); foreach (var delivery in snapshot) delivery.RequestOriginalRetirement(); }
    private async Task JoinOriginalProcessDeliveriesAsync()
    {
        Delivery[] snapshot; Process[] processes;
        lock (_gate) { snapshot = _ownedDeliveries.ToArray(); processes = _ownedProcesses.ToArray(); }
        var errors = new List<Exception>();
        foreach (var process in processes)
            if (process.DriverObservation is { } observation)
                try { await observation.ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        foreach (var delivery in snapshot)
            try { await delivery.CloseAndDrainOriginalAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }

    private sealed class Delivery(CanonicalCapabilityCatalogueInitializationOwner owner, Process process) : ICapabilityOriginalInitializationObservation
    {
        internal readonly CanonicalCapabilityCatalogueInitializationOwner Owner = owner;
        internal readonly Process OriginalProcess = process;
        private readonly object _gate = new();
        private readonly TaskCompletionSource<Task<ICapabilityOriginalInitializationAcknowledgment>> _publication = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _retired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AsyncLocal<int> _inside = new();
        [ThreadStatic] private static Delivery? _physical;
        private readonly ConditionalWeakTable<ICapabilityOriginalInitializationCompletion, Completion> _completions = new();
        private Task<ICapabilityOriginalInitializationCompletion>? _wait;
        private Task? _close;
        public ICapabilityOriginalInitializationIntent OriginalIntent => OriginalProcess.Intent;
        public Task? OriginalClose { get { lock (_gate) return _close; } }
        internal void Publish(Task<ICapabilityOriginalInitializationAcknowledgment> driver) => _publication.TrySetResult(driver);
        public Task<ICapabilityOriginalInitializationCompletion> WaitOriginalCompletionAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            TaskCompletionSource? begin = null; Task<ICapabilityOriginalInitializationCompletion> result;
            lock (_gate)
            {
                if (_wait is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _wait = Wait(begin.Task); }
                result = _wait;
            }
            begin?.SetResult(); return result;
        }
        private async Task<ICapabilityOriginalInitializationCompletion> Wait(Task start)
        {
            await start.ConfigureAwait(false);
            var prior = _inside.Value; _inside.Value = prior + 1;
            try
            {
                if (!(_publication.Task.IsCompleted || await Task.WhenAny(_publication.Task, _retired.Task).ConfigureAwait(false) == _publication.Task))
                    return Issue(CapabilityOriginalInitializationCompletionKind.ObservationRetired, null);
                var driver = await _publication.Task.ConfigureAwait(false);
                if (!driver.IsCompleted && await Task.WhenAny(driver, _retired.Task).ConfigureAwait(false) != driver)
                    return Issue(CapabilityOriginalInitializationCompletionKind.ObservationRetired, null);
                ICapabilityOriginalInitializationAcknowledgment? acknowledgment = null;
                try { acknowledgment = await driver.ConfigureAwait(false); }
                catch (Exception cause)
                {
                    if (InvokeOriginalSource(() => Owner.IsAcknowledgedOriginalInitializationSourceRefusal(driver)))
                        return Issue(CapabilityOriginalInitializationCompletionKind.DeclinedBeforeEffect, null);
                    var errors = new List<Exception>(); CanonicalSqliteOriginalStoreOwner.Capture(errors, driver, cause);
                    CanonicalSqliteOriginalStoreOwner.Throw(errors); throw;
                }
                return Issue(CapabilityOriginalInitializationCompletionKind.Initialized, acknowledgment);
            }
            finally { _inside.Value = prior; }
        }
        private ICapabilityOriginalInitializationCompletion Issue(CapabilityOriginalInitializationCompletionKind kind,
            ICapabilityOriginalInitializationAcknowledgment? acknowledgment)
        {
            var actual = new Completion(this, kind, acknowledgment); _completions.Add(actual, actual); return actual;
        }
        public bool IsIssuedOriginalCompletion(ICapabilityOriginalInitializationCompletion actual) =>
            actual is Completion same && ReferenceEquals(same.OriginalObservation, this) &&
            _completions.TryGetValue(actual, out var issued) && ReferenceEquals(same, issued);
        private T InvokeOriginalSource<T>(Func<T> body)
        {
            var previous = _physical; _physical = this;
            try { return body(); } finally { _physical = previous; }
        }
        public void RequestOriginalRetirement() => _retired.TrySetResult();
        public void DemandExternalOriginalJoin()
        { if (_inside.Value != 0 || ReferenceEquals(_physical, this)) throw new InvalidOperationException("An actual setup delivery cannot join its own original close."); }
        public Task CloseAndDrainOriginalAsync()
        {
            DemandExternalOriginalJoin(); RequestOriginalRetirement();
            lock (_gate) return _close ??= Close();
        }
        private async Task Close()
        {
            Task<ICapabilityOriginalInitializationCompletion>? wait; lock (_gate) wait = _wait;
            if (wait is not null) await wait.ConfigureAwait(false);
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    }
    private sealed class Completion(Delivery observation, CapabilityOriginalInitializationCompletionKind kind,
        ICapabilityOriginalInitializationAcknowledgment? acknowledgment) : ICapabilityOriginalInitializationCompletion
    {
        public ICapabilityOriginalInitializationObservation OriginalObservation { get; } = observation;
        public CapabilityOriginalInitializationCompletionKind Kind { get; } = kind;
        public ICapabilityOriginalInitializationAcknowledgment? Acknowledgment { get; } = acknowledgment;
    }
}
