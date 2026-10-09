using Haven.Core;

namespace Haven.Application;

/// <summary>One private initial input, actual process-owned producer and separate bounded observer.</summary>
internal sealed class HostedInitialTaskSend(ChatSessionService chat, TaskExecutionCoordinator coordinator,
    object marker, Guid conversationId)
{
    internal readonly object Gate = new();
    internal readonly ChatSessionService Chat = chat;
    internal readonly TaskExecutionCoordinator Coordinator = coordinator;
    internal readonly object Marker = marker;
    internal readonly Guid ConversationId = conversationId;
    internal readonly TaskCompletionSource InputReady = NewSignal();
    internal readonly AgentRuntimeOriginalCustody ProducerCustody = new();
    internal readonly AgentRuntimeOriginalCustody WaitCustody = new();
    internal readonly AgentRuntimeOriginalCustody DetachCustody = new();
    internal readonly TaskCompletionSource Detached = NewSignal();
    internal readonly TaskCompletionSource DetachRequested = NewSignal();
    internal readonly Queue<ChatStreamEvent> Events = new();
    private TaskCompletionSource _available = NewSignal();
    private TaskCompletionSource _space = NewSignal();
    private const int EventCapacity = 128;
    internal TaskRunProcessStageCustody? Stage;
    internal TaskRunInvocationCustody? Invocation;
    internal CanonicalChatProcessProducer? Source;
    internal Task<TaskRunOriginalInitialChatObservationLease>? Acquisition;
    internal Task<TaskRunOriginalInitialChatObservationLease>? AcquisitionWait;
    internal InitialChatObserverWaitOriginal<TaskRunOriginalInitialChatObservationLease>? AcquisitionObservationWait;
    internal Task<TaskRunInitialChatObservationResult>? Producer;
    internal Task<TaskRunInitialChatObservationResult>? Wait;
    internal Task? Detach;
    internal TaskRunOriginalInitialChatObservationLease? Lease;
    internal InitialChatEventReader? Reader;
    internal EventHandler<TaskExecutionSnapshot>? Projection;
    internal Task? OriginalDraftWrite;
    internal Exception? InputFailure;
    internal bool RetirementRequested;
    internal bool ObserverAdmissionSealed;
    internal bool ProducerTerminal;
    private TaskExecutionSnapshot? _acknowledged;
    internal readonly List<InitialChatObserverWaitOriginal<TaskRunInitialChatObservationResult>> ObserverWaits = [];
    private readonly List<Exception> _observerCauses = [];
    internal static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal bool Healthy
    {
        get
        {
            InitialChatObserverWaitOriginal<TaskRunInitialChatObservationResult>[] waits;
            InitialChatObserverWaitOriginal<TaskRunOriginalInitialChatObservationLease>? acquisitionWait;
            InitialChatEventReader? reader;
            lock (Gate)
            {
                waits = ObserverWaits.ToArray(); acquisitionWait = AcquisitionObservationWait; reader = Reader;
                if (_observerCauses.Count != 0) return false;
            }
            return Acquisition is { IsCompletedSuccessfully: true }
                && ProducerCustody.Healthy && WaitCustody.Healthy && DetachCustody.Healthy
                && Source is { HasHealthyClosedOriginal: true }
                && Invocation is { OwnedCleanupTerminal: true, OriginalTerminalObservation: not null }
                && Detach is { IsCompletedSuccessfully: true }
                && (reader is null || reader.Healthy)
                && acquisitionWait is not null && acquisitionWait.HasSettledOriginalWait
                && waits.All(actual => actual.HasSettledOriginalWait);
        }
    }

    internal void SealObservationAdmission() { lock (Gate) ObserverAdmissionSealed = true; }

    internal void RecordAcknowledgment(TaskExecutionSnapshot actual)
    {
        var binding = Invocation?.OriginalBinding;
        if (binding is null || actual.TaskId != binding.TaskId || actual.ContextId != ConversationId
            || actual.ExecutionId != binding.ExecutionId || actual.OwnerBinding != binding.OwnerBinding
            || actual.CreatedAt != binding.CreatedAt || actual.PersistenceRevision < binding.PersistenceRevision) return;
        lock (Gate)
            if (_acknowledged is null || actual.PersistenceRevision >= _acknowledged.PersistenceRevision)
                _acknowledged = actual;
    }

    internal ProviderExecutionContext? ReadAcknowledgedContext()
    {
        if (Invocation is { OriginalBegin: { IsCompletedSuccessfully: true }, OriginalBinding: { } binding })
            RecordAcknowledgment(binding);
        TaskExecutionSnapshot? actual;
        lock (Gate) actual = _acknowledged;
        return actual is null ? null : new ProviderExecutionContext(actual.TaskId, actual.ContextId,
            actual.ExecutionId, actual.Attempts.LastOrDefault()?.Id, actual.PersistenceRevision);
    }

    internal async Task PublishEventAsync(ChatStreamEvent value)
    {
        while (true)
        {
            Task? wait = null; TaskCompletionSource? available = null;
            lock (Gate)
            {
                if (RetirementRequested) return;
                if (Events.Count < EventCapacity)
                {
                    Events.Enqueue(value); available = _available; _available = NewSignal();
                }
                else wait = _space.Task;
            }
            if (available is not null) { available.TrySetResult(); return; }
            await wait!.ConfigureAwait(false); // Observer space only; never a completion/effect receipt.
        }
    }

    internal IAsyncEnumerable<ChatStreamEvent> ObserveEvents(CancellationToken token) => new InitialChatEventEnumerable(this, token);

    internal void DemandExternalObservationJoin() => TaskRunProcessProducerContext.DemandExternalJoin(this);

    internal Task<TaskRunInitialChatObservationResult> WaitForObservation(CancellationToken token)
    {
        DemandExternalObservationJoin();
        InitialChatObserverWaitOriginal<TaskRunInitialChatObservationResult>? admitted = null;
        Task<TaskRunInitialChatObservationResult> actual;
        lock (Gate)
        {
            actual = Wait ?? throw new InvalidOperationException("No actual initial observation wait was issued.");
            if (!ObserverAdmissionSealed && !Coordinator.HasSealedOriginalProcessProducerAdmission && token.CanBeCanceled)
            {
                if (ObserverWaits.Count >= 64) throw new InvalidOperationException("Finite original observer waits require inspection.");
                admitted = new(this, actual, token);
                ObserverWaits.Add(admitted); // Exact driver is published behind its gate before any callback.
            }
        }
        if (admitted is null) return actual; // No new original after seal; SAME permanent wait remains readable.
        admitted.StartOriginal();
        return admitted.Driver;
    }

    internal void RequestObservationRetirement()
    {
        TaskCompletionSource available; TaskCompletionSource space;
        lock (Gate)
        {
            RetirementRequested = true;
            ObserverAdmissionSealed = true;
            Events.Clear(); available = _available; space = _space;
        }
        // Signals only. The real independently enrolled detacher supplies the acknowledgment.
        available.TrySetResult(); space.TrySetResult(); DetachRequested.TrySetResult();
    }

    internal Task DetachAndDrain()
    {
        DemandExternalObservationJoin(); // Own observer guard before even existing Task retrieval.
        RequestObservationRetirement();
        lock (Gate) return Detach ?? throw new InvalidOperationException("No actual initial observer detacher was issued.");
    }

    internal void MarkProducerTerminal()
    {
        TaskCompletionSource available;
        lock (Gate) { ProducerTerminal = true; available = _available; }
        available.TrySetResult();
    }

    internal async Task<bool> ReadEventAsync(InitialChatEventReader reader)
    {
        while (true)
        {
            Task? wait; bool terminal; TaskCompletionSource? space = null;
            lock (Gate)
            {
                if (RetirementRequested) return false;
                if (Events.Count > 0)
                {
                    reader.StableCurrent = Events.Dequeue();
                    space = _space; _space = NewSignal(); wait = null;
                }
                else if (ProducerTerminal) wait = Wait;
                else wait = _available.Task;
                terminal = ProducerTerminal;
            }
            if (space is not null) { space.TrySetResult(); return true; }
            if (terminal)
            {
                var observation = Wait ?? throw new InvalidOperationException("No actual initial observation wait was published.");
                try { _ = await observation.ConfigureAwait(false); }
                catch (Exception cause)
                { RetainObserverFailure(cause, observation); if (observation.Exception is { } group) throw group; throw; }
                return false;
            }
            await wait!.ConfigureAwait(false);
        }
    }

    internal void RetainObserverFailure(Exception cause, Task? actual = null)
    {
        lock (Gate)
        {
            IEnumerable<Exception> causes = actual?.Exception is { InnerExceptions.Count: > 0 } group
                ? group.InnerExceptions : new[] { cause };
            foreach (var original in causes)
                if (!_observerCauses.Any(prior => ReferenceEquals(prior, original))) _observerCauses.Add(original);
        }
    }

    internal void ThrowObserverFailures()
    {
        Exception[] causes; lock (Gate) causes = _observerCauses.ToArray();
        if (causes.Length != 0) throw new AggregateException("Actual initial observation originals did not all settle.", causes);
    }

    private sealed class InitialChatEventEnumerable(HostedInitialTaskSend original, CancellationToken supplied) : IAsyncEnumerable<ChatStreamEvent>
    {
        public IAsyncEnumerator<ChatStreamEvent> GetAsyncEnumerator(CancellationToken token = default)
        {
            InitialChatEventReader reader;
            lock (original.Gate)
            {
                if (original.ObserverAdmissionSealed || original.Coordinator.HasSealedOriginalProcessProducerAdmission)
                    throw new InvalidOperationException("Initial observer reader admission is sealed.");
                if (original.Reader is not null) throw new InvalidOperationException("This original observer already acquired its sole reader.");
                reader = new(original, supplied, token);
                original.Reader = reader;
                original.Stage!.RetainSource(reader.ActualDispose);
            }
            reader.StartOriginal();
            return reader;
        }
    }
}

internal sealed class InitialChatEventReader : IAsyncEnumerator<ChatStreamEvent>
{
    private readonly HostedInitialTaskSend _original;
    private readonly object _gate = new();
    private readonly CancellationToken _supplied;
    private readonly CancellationToken _enumeratorToken;
    private readonly TaskCompletionSource _start = HostedInitialTaskSend.NewSignal();
    private readonly TaskCompletionSource _disposeRequested = HostedInitialTaskSend.NewSignal();
    private readonly List<Task<bool>> _moves = [];
    private readonly List<Task<bool>> _reads = [];
    private readonly List<Task> _registrationCloses = [];
    private CancellationTokenRegistration _firstRegistration;
    private CancellationTokenRegistration _secondRegistration;
    private Task<bool>? _activeMove;
    private bool _sealed;
    internal ChatStreamEvent? StableCurrent;
    internal Task ActualDispose { get; }
    internal bool Healthy
    { get { lock (_gate) return ActualDispose.IsCompletedSuccessfully && _moves.All(actual => actual.IsCompletedSuccessfully)
                && _reads.All(actual => actual.IsCompletedSuccessfully) && _registrationCloses.All(actual => actual.IsCompletedSuccessfully); } }

    internal InitialChatEventReader(HostedInitialTaskSend original, CancellationToken supplied, CancellationToken token)
    {
        _original = original; _supplied = supplied; _enumeratorToken = token;
        ActualDispose = DisposeOriginalAsync(_start.Task);
    }

    internal void StartOriginal()
    {
        try
        {
            if (AdmitOriginalRegistration())
                _firstRegistration = TaskRunProcessProducerContext.Invoke(_original, () =>
                    _supplied.Register(() => TaskRunProcessProducerContext.Invoke(_original,
                        () => { _original.RequestObservationRetirement(); return true; })));
            else RequestDispose();
            if (_enumeratorToken != _supplied)
            {
                if (AdmitOriginalRegistration())
                    _secondRegistration = TaskRunProcessProducerContext.Invoke(_original, () =>
                        _enumeratorToken.Register(() => TaskRunProcessProducerContext.Invoke(_original,
                            () => { _original.RequestObservationRetirement(); return true; })));
                else RequestDispose();
            }
        }
        catch (Exception cause)
        {
            _original.RetainObserverFailure(cause); RequestDispose();
            if (cause is OperationCanceledException) throw new AggregateException("Actual observer registration callback fault.", cause);
            throw;
        }
        finally { _start.TrySetResult(); }
    }

    private bool AdmitOriginalRegistration()
    {
        lock (_original.Gate)
            return !_original.ObserverAdmissionSealed && !_original.Coordinator.HasSealedOriginalProcessProducerAdmission;
    }

    public ChatStreamEvent Current => TaskRunProcessProducerContext.Invoke(_original, () =>
        StableCurrent ?? throw new InvalidOperationException("No original observed event is current."));

    public ValueTask<bool> MoveNextAsync()
    {
        TaskCompletionSource? start = null; Task<bool> actual;
        lock (_original.Gate)
        lock (_gate)
        {
            if (_sealed || _original.RetirementRequested) return ValueTask.FromResult(false);
            if (_original.ObserverAdmissionSealed || _original.Coordinator.HasSealedOriginalProcessProducerAdmission)
                throw new InvalidOperationException("Initial observer Move admission is sealed.");
            if (_activeMove is { IsCompleted: false }) throw new InvalidOperationException("An actual observer Move is already pending.");
            if (_moves.Count >= 4096) throw new InvalidOperationException("Finite original event observations require inspection.");
            start = HostedInitialTaskSend.NewSignal();
            actual = MoveOriginalAsync(start.Task); _activeMove = actual; _moves.Add(actual);
        }
        start.SetResult();
        return new(actual);
    }

    private async Task<bool> MoveOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        using var context = TaskRunProcessProducerContext.EnterAsync(_original);
        Task<bool>? actualRead = null;
        try
        {
            actualRead = TaskRunProcessProducerContext.Invoke(_original, () => _original.ReadEventAsync(this));
            lock (_gate) _reads.Add(actualRead);
            return await actualRead.ConfigureAwait(false);
        }
        catch (Exception cause)
        {
            _original.RetainObserverFailure(cause, actualRead);
            if (actualRead?.Exception is { } group) throw group;
            if (cause is OperationCanceledException && actualRead is not { IsCanceled: true })
                throw new AggregateException("Actual synchronous/faulted observer cause.", cause);
            throw;
        }
    }

    internal void RequestDispose()
    {
        lock (_gate) _sealed = true;
        _original.RequestObservationRetirement();
        _disposeRequested.TrySetResult();
    }

    public ValueTask DisposeAsync()
    {
        _original.DemandExternalObservationJoin();
        RequestDispose();
        return new(ActualDispose);
    }

    private async Task DisposeOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        await _disposeRequested.Task.ConfigureAwait(false);
        using var context = TaskRunProcessProducerContext.EnterAsync(_original);
        Task<bool>[] moves; Task<bool>[] reads;
        lock (_gate) { moves = _moves.ToArray(); reads = _reads.ToArray(); }
        foreach (var actual in moves.Concat(reads))
            try { _ = await actual.ConfigureAwait(false); }
            catch (Exception cause) { _original.RetainObserverFailure(cause, actual); }
        // Disposals are independent and use the original caller registrations, never a business token.
        foreach (var registration in new[] { _firstRegistration, _secondRegistration })
        {
            Task? actual = null;
            try
            {
                actual = TaskRunProcessProducerContext.Invoke(_original, () => registration.DisposeAsync().AsTask());
                lock (_gate) _registrationCloses.Add(actual);
                await actual.ConfigureAwait(false);
            }
            catch (Exception cause) { _original.RetainObserverFailure(cause, actual); }
        }
        _original.ThrowObserverFailures();
    }
}

/// <summary>Actual observer-only caller wait. Only its own raw canceled WaitAsync can issue withdrawal evidence.</summary>
internal sealed class InitialChatObserverWaitOriginal<T>
{
    private readonly HostedInitialTaskSend _owner;
    private readonly Task<T> _source;
    private readonly CancellationToken _token;
    private readonly bool _captureLateSource;
    private readonly TaskCompletionSource _start = HostedInitialTaskSend.NewSignal();
    private Task<T>? _rawWait;
    private bool _withdrawn;
    internal Task<T> Driver { get; }
    private bool HasOriginalWithdrawal => _withdrawn && _rawWait is { IsCanceled: true }
        && !ReferenceEquals(_rawWait, _source) && _token.IsCancellationRequested && _owner.RetirementRequested;
    internal bool HasSettledOriginalWait => Driver.IsCompletedSuccessfully
        && (_rawWait is { IsCompletedSuccessfully: true } || _captureLateSource && HasOriginalWithdrawal && _source.IsCompletedSuccessfully)
        || Driver.IsCanceled && !_captureLateSource && HasOriginalWithdrawal;
    internal InitialChatObserverWaitOriginal(HostedInitialTaskSend owner, Task<T> source, CancellationToken token,
        bool captureLateSource = false)
    { _owner = owner; _source = source; _token = token; _captureLateSource = captureLateSource; Driver = RunOriginalAsync(); }
    internal void StartOriginal() => _start.TrySetResult();
    private async Task<T> RunOriginalAsync()
    {
        await _start.Task.ConfigureAwait(false);
        using var context = TaskRunProcessProducerContext.EnterAsync(_owner);
        try
        {
            _rawWait = TaskRunProcessProducerContext.Invoke(_owner, () => _source.WaitAsync(_token));
            return await _rawWait.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_rawWait is { IsCanceled: true } && !ReferenceEquals(_rawWait, _source) && _token.IsCancellationRequested)
        {
            _withdrawn = true; _owner.RequestObservationRetirement();
            if (!_captureLateSource) throw;
            // An already admitted source can still issue a real late lease. Preserve the actual
            // acquisition driver until that lease is captured; its consumer then joins Detach ACK.
            // This does not await Detach here (Detach independently joins this same driver).
            try { return await _source.ConfigureAwait(false); }
            catch (Exception sourceFailure)
            {
                _owner.RetainObserverFailure(sourceFailure, _source);
                if (_source.Exception is { } group) throw group;
                throw;
            }
        }
        catch (Exception cause)
        {
            _owner.RetainObserverFailure(cause, _rawWait);
            if (_rawWait is { IsFaulted: true, Exception: { } group }) throw group;
            if (cause is OperationCanceledException && _rawWait is not { IsCanceled: true })
                throw new AggregateException("Actual synchronous/faulted observer wait fault.", cause);
            throw;
        }
    }
    internal async Task JoinOriginalAsync()
    {
        try { _ = await Driver.ConfigureAwait(false); }
        catch (OperationCanceledException) when (HasSettledOriginalWait) { } // SAME source-issued caller withdrawal; business remains owned.
        catch (Exception cause)
        { _owner.RetainObserverFailure(cause, Driver); if (Driver.Exception is { } group) throw group; throw; }
    }
}
