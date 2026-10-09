using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Automations;

namespace Haven.Infrastructure;

public sealed partial class CanonicalAutomationDefinitionOriginalWriteOwner
{
    private readonly ConditionalWeakTable<ICanonicalAutomationDefinitionOriginalProcessPreparation, ProcessPreparation> _preparations = new();
    private sealed class ProcessPreparation(bool declined,
        ICanonicalAutomationDefinitionOriginalChangeObservation? observation) : ICanonicalAutomationDefinitionOriginalProcessPreparation
    {
        public bool IsDeclinedBeforeEffect { get; } = declined;
        public ICanonicalAutomationDefinitionOriginalChangeObservation? Observation { get; } = observation;
    }
    public bool IsIssuedOriginalProcessPreparation(ICanonicalAutomationDefinitionOriginalProcessPreparation actual) =>
        actual is ProcessPreparation own && _preparations.TryGetValue(actual, out var same) && ReferenceEquals(own, same);

    public Task<ICanonicalAutomationDefinitionOriginalProcessPreparation> PrepareOriginalChangeProcessWithinSourceAsync(
        ICanonicalAutomationLibraryOriginalObservation sameObservation,
        AutomationOwnerRead<Haven.Core.AutomationDefinition> sameRow, CanonicalAutomationOriginalChangeKind kind,
        Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, async () =>
        {
            // The actual preparation is retained by this SAME writer's command record.
            // A presentation receives only this finite result; acknowledged child faults
            // are never aliased into a foreign view ledger or inferred from exception type.
            Task<ICanonicalAutomationDefinitionOriginalChangeIntent>? preparation = null;
            ICanonicalAutomationDefinitionOriginalChangeIntent? intent = null;
            var errors = new List<Exception>(); var declined = false;
            try { source.Run(() => preparation = PrepareOriginalChangeWithinSourceAsync(sameObservation,
                sameRow, kind, operationId, body => body(), _ => { }, token)); }
            catch (Exception cause) { errors.Add(cause); }
            if (preparation is not null)
                try { intent = await preparation.ConfigureAwait(false); }
                catch (Exception cause)
                {
                    // Independently joined exact task, then SAME source issuer proof.
                    if (errors.Count == 0 && IsAcknowledgedOriginalChangeSourceRefusal(preparation)) declined = true;
                    else CanonicalSqliteOriginalStoreOwner.Capture(errors, preparation, cause);
                }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            CanonicalSqliteOriginalStoreOwner.Throw(errors);
            if (preparation is null) throw new InvalidOperationException("No actual process preparation task was captured.");
            ICanonicalAutomationDefinitionOriginalChangeObservation? delivery = null;
            if (!declined)
            {
                if (intent is null) throw new InvalidOperationException("The same successful preparation returned no actual intent.");
                Task<ICanonicalAutomationDefinitionOriginalChangeObservation>? raw = null;
                try { source.Run(() => raw = StartOriginalChangeProcessWithinSourceAsync(intent,
                    body => body(), _ => { }, token)); }
                catch (Exception cause) { errors.Add(cause); }
                if (raw is not null)
                    try { delivery = await raw.ConfigureAwait(false); }
                    catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, raw, cause); }
                else errors.Add(new InvalidOperationException("No actual finite process start was captured."));
            }
            ProcessPreparation? result = null;
            try
            {
                await source.JoinAllAsync().ConfigureAwait(false);
                CanonicalSqliteOriginalStoreOwner.Throw(errors);
                source.Run(() =>
                {
                    if (!declined && (delivery is null || !IsIssuedOriginalChangeObservation(delivery)))
                        throw new UnauthorizedAccessException("The same process did not issue its actual change observation.");
                    result = new(declined, delivery); _preparations.Add(result, result);
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
            return (ICanonicalAutomationDefinitionOriginalProcessPreparation)(result ?? throw new InvalidOperationException("No same-source finite process result was captured."));
        });
    }

    private readonly ConditionalWeakTable<Intent, Process> _processes = new();
    private readonly ConditionalWeakTable<ICanonicalAutomationDefinitionOriginalChangeObservation, Delivery> _deliveries = new();
    private readonly List<Process> _ownedProcesses = [];
    private readonly List<Delivery> _ownedDeliveries = [];
    private sealed class Process(Intent intent)
    {
        internal readonly Intent Intent = intent;
        internal Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>? Driver;
        internal Task? DriverObservation;
        internal int DriverJoined;
        internal Commit? Commit;
    }

    public Task<ICanonicalAutomationDefinitionOriginalChangeObservation> StartOriginalChangeProcessWithinSourceAsync(
        ICanonicalAutomationDefinitionOriginalChangeIntent sameIntent, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var intent = RequireIntent(sameIntent);
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, Scope(scope), retain);
        return Admit(source, async () =>
        {
            await source.Read(() => RevalidateOriginalChangeIntentWithinSourceAsync(intent,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            await PruneSettledOriginalProcessesWithinSourceAsync(source).ConfigureAwait(false);
            Delivery? delivery = null;
            var errors = new List<Exception>();
            try
            {
                source.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (OriginalHomeWriteSource is not ICanonicalAutomationDefinitionOriginalHomeReviewWithdrawalSource)
                        throw new InvalidOperationException("Actual Home automation change review withdrawal is required before process automation change can start.");
                    Process process;
                    lock (_gate)
                    {
                        ObjectDisposedException.ThrowIf(_retiring, this);
                        _ownedDeliveries.RemoveAll(item => item.HasIndependentlyJoinedClose);
                        if (_ownedDeliveries.Count >= 128)
                            throw new InvalidOperationException("Automation change delivery custody is full; unresolved deliveries remain retained.");
                        if (!_processes.TryGetValue(intent, out process!))
                        {
                            if (_ownedProcesses.Count >= 128)
                                throw new InvalidOperationException("Automation change process custody is full; unresolved processes remain retained.");
                            process = new(intent); _processes.Add(intent, process); _ownedProcesses.Add(process);
                        }
                        delivery = new(this, process);
                        _ownedDeliveries.Add(delivery); _deliveries.Add(delivery, delivery);
                    }
                    // The actual business driver uses only the process owner's scopes.
                    // A view's callbacks/lifetime never remain installed across Home review.
                    var raw = CommitOriginalChangeWithinSourceAsync(intent, body => body(), _ => { }, CancellationToken.None);
                    lock (_gate)
                    {
                        if (process.Driver is null)
                        {
                            process.Driver = raw;
                            process.DriverObservation = ObserveOriginalProcessDriverAsync(process, raw);
                        }
                        if (!ReferenceEquals(process.Driver, raw)) throw new InvalidOperationException("The actual automation change process changed its cached driver.");
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
            return (ICanonicalAutomationDefinitionOriginalChangeObservation)(delivery ?? throw new InvalidOperationException("No original automation change delivery was captured."));
        });
    }
    private static async Task ObserveOriginalProcessDriverAsync(Process process,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> sameDriver)
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
        Process[] snapshot; Delivery[] deliveries;
        lock (_gate) { snapshot = _ownedProcesses.ToArray(); deliveries = _ownedDeliveries.ToArray(); }
        foreach (var delivery in deliveries)
            if (delivery.OriginalClose is { IsCompletedSuccessfully: true } sameClose)
            {
                await source.Read(() => sameClose).ConfigureAwait(false);
                delivery.AcknowledgeIndependentlyJoinedClose(sameClose);
            }
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
                IsAcknowledgedOriginalChangeSourceRefusal(driver));
            if (!acknowledged) continue;
            lock (_gate)
            {
                if (!ReferenceEquals(process.Driver, driver) || !ReferenceEquals(process.DriverObservation, observation) ||
                    Volatile.Read(ref process.DriverJoined) == 0 || !observation.IsCompletedSuccessfully ||
                    _ownedDeliveries.Any(delivery => ReferenceEquals(delivery.OriginalProcess, process) &&
                        !delivery.HasIndependentlyJoinedClose)) continue;
                _ownedProcesses.Remove(process);
            }
        }
    }
    public bool IsIssuedOriginalChangeObservation(ICanonicalAutomationDefinitionOriginalChangeObservation actual) =>
        actual is Delivery same && ReferenceEquals(same.Owner, this) &&
        _deliveries.TryGetValue(actual, out var issued) && ReferenceEquals(same, issued);

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
            try
            {
                var actual = delivery.CloseAndDrainOriginalAsync();
                await actual.ConfigureAwait(false); delivery.AcknowledgeIndependentlyJoinedClose(actual);
            }
            catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }

    private sealed class Delivery(CanonicalAutomationDefinitionOriginalWriteOwner owner, Process process) : ICanonicalAutomationDefinitionOriginalChangeObservation
    {
        internal readonly CanonicalAutomationDefinitionOriginalWriteOwner Owner = owner;
        internal readonly Process OriginalProcess = process;
        private readonly object _gate = new();
        private readonly TaskCompletionSource<Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>> _publication = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _retired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AsyncLocal<int> _inside = new();
        [ThreadStatic] private static Delivery? _physical;
        private readonly ConditionalWeakTable<ICanonicalAutomationDefinitionOriginalChangeCompletion, Completion> _completions = new();
        private Task<ICanonicalAutomationDefinitionOriginalChangeCompletion>? _wait;
        private Task? _close;
        private int _closeJoined;
        public ICanonicalAutomationDefinitionOriginalChangeIntent OriginalIntent => OriginalProcess.Intent;
        public Task? OriginalClose { get { lock (_gate) return _close; } }
        internal bool HasIndependentlyJoinedClose => Volatile.Read(ref _closeJoined) == 1 && OriginalClose is { IsCompletedSuccessfully: true };
        internal void AcknowledgeIndependentlyJoinedClose(Task actual)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_close, actual) || !actual.IsCompletedSuccessfully)
                    throw new InvalidOperationException("Independently join the SAME successful actual delivery close.");
                Volatile.Write(ref _closeJoined, 1);
            }
        }
        internal void Publish(Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> driver) => _publication.TrySetResult(driver);
        public Task<ICanonicalAutomationDefinitionOriginalChangeCompletion> WaitOriginalCompletionAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            TaskCompletionSource? begin = null; Task<ICanonicalAutomationDefinitionOriginalChangeCompletion> result;
            lock (_gate)
            {
                if (_wait is null) { begin = new(TaskCreationOptions.RunContinuationsAsynchronously); _wait = Wait(begin.Task); }
                result = _wait;
            }
            begin?.SetResult(); return result;
        }
        private async Task<ICanonicalAutomationDefinitionOriginalChangeCompletion> Wait(Task start)
        {
            await start.ConfigureAwait(false);
            var prior = _inside.Value; _inside.Value = prior + 1;
            try
            {
                if (!(_publication.Task.IsCompleted || await Task.WhenAny(_publication.Task, _retired.Task).ConfigureAwait(false) == _publication.Task))
                    return Issue(CanonicalAutomationOriginalChangeCompletionKind.ObservationRetired, null);
                var driver = await _publication.Task.ConfigureAwait(false);
                if (!driver.IsCompleted && await Task.WhenAny(driver, _retired.Task).ConfigureAwait(false) != driver)
                    return Issue(CanonicalAutomationOriginalChangeCompletionKind.ObservationRetired, null);
                ICanonicalAutomationDefinitionOriginalChangeAcknowledgment? acknowledgment = null;
                try { acknowledgment = await driver.ConfigureAwait(false); }
                catch (Exception cause)
                {
                    if (InvokeOriginalSource(() => Owner.IsAcknowledgedOriginalChangeSourceRefusal(driver)))
                        return Issue(CanonicalAutomationOriginalChangeCompletionKind.DeclinedBeforeEffect, null);
                    var errors = new List<Exception>(); CanonicalSqliteOriginalStoreOwner.Capture(errors, driver, cause);
                    CanonicalSqliteOriginalStoreOwner.Throw(errors); throw;
                }
                var actualProcess = OriginalProcess;
                if (actualProcess.Commit?.Atomic is not { } atomic || acknowledgment is null ||
                    !InvokeOriginalSource(() => Owner.IsOwnedOriginalChangeAcknowledgment(actualProcess.Intent, acknowledgment, atomic)))
                    throw new UnauthorizedAccessException("The SAME process-owned atomic change did not issue its acknowledgment.");
                return Issue(CanonicalAutomationOriginalChangeCompletionKind.Changed, acknowledgment);
            }
            finally { _inside.Value = prior; }
        }
        private ICanonicalAutomationDefinitionOriginalChangeCompletion Issue(CanonicalAutomationOriginalChangeCompletionKind kind,
            ICanonicalAutomationDefinitionOriginalChangeAcknowledgment? acknowledgment)
        {
            var actual = new Completion(this, kind, acknowledgment); _completions.Add(actual, actual); return actual;
        }
        public bool IsIssuedOriginalCompletion(ICanonicalAutomationDefinitionOriginalChangeCompletion actual) =>
            actual is Completion same && ReferenceEquals(same.OriginalObservation, this) &&
            _completions.TryGetValue(actual, out var issued) && ReferenceEquals(same, issued);
        private T InvokeOriginalSource<T>(Func<T> body)
        {
            var previous = _physical; _physical = this;
            try { return body(); } finally { _physical = previous; }
        }
        public void RequestOriginalRetirement() => _retired.TrySetResult();
        public void DemandExternalOriginalJoin()
        { if (_inside.Value != 0 || ReferenceEquals(_physical, this)) throw new InvalidOperationException("An actual automation change delivery cannot join its own original close."); }
        public Task CloseAndDrainOriginalAsync()
        {
            DemandExternalOriginalJoin(); RequestOriginalRetirement();
            lock (_gate) return _close ??= Close();
        }
        private async Task Close()
        {
            Task<ICanonicalAutomationDefinitionOriginalChangeCompletion>? wait; lock (_gate) wait = _wait;
            if (wait is not null) await wait.ConfigureAwait(false);
        }
        public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
    }
    private sealed class Completion(Delivery observation, CanonicalAutomationOriginalChangeCompletionKind kind,
        ICanonicalAutomationDefinitionOriginalChangeAcknowledgment? acknowledgment) : ICanonicalAutomationDefinitionOriginalChangeCompletion
    {
        public ICanonicalAutomationDefinitionOriginalChangeObservation OriginalObservation { get; } = observation;
        public CanonicalAutomationOriginalChangeCompletionKind Kind { get; } = kind;
        public ICanonicalAutomationDefinitionOriginalChangeAcknowledgment? Acknowledgment { get; } = acknowledgment;
    }
}
