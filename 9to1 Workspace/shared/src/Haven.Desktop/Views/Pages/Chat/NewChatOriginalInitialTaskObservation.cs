using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Chat;

// Presentation owns only the actual factory acquisition and source-issued observer.
// The Application host owns the business stream, its token, and complete producer.
internal sealed class NewChatOriginalInitialTaskObservation
{
    private readonly object _gate = new();
    private readonly ChatSessionService _source;
    private readonly DesktopOriginalWorkLifetime.Original _pageOriginal;
    private readonly Guid _conversationId;
    private readonly Func<Task<TaskRunOriginalInitialChatObservationLease>> _acquire;
    private readonly Func<ChatStreamEvent, Task> _publish;
    private readonly Func<bool> _presentationIsRetiring;
    private readonly Action _demandActualSourceJoin;
    private readonly DesktopOriginalWorkLifetime _retirement;
    private readonly TaskCompletionSource<bool> _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task<TaskRunOriginalInitialChatObservationLease?> _actualAcquisition;
    private TaskRunOriginalInitialChatObservationLease? _lease;
    private ProviderExecutionContext? _firstAcknowledgedContext;
    private bool _actualSourceFactoryEntered;
    private Task<TaskRunOriginalInitialChatObservationLease>? _actualSourceAcquisition;
    private Task<TaskRunInitialChatObservationResult>? _actualWait;
    private Task? _actualSourceDetach;
    private Task? _actualRequest;
    private Task? _actualEvents;
    private Task? _actualStopDriver;
    private Task<TaskRunOriginalRunControlResult>? _actualSourceStop;
    private bool _actualStopFactoryEntered;
    [ThreadStatic] private static List<NewChatOriginalInitialTaskObservation>? _synchronousSources;

    internal NewChatOriginalInitialTaskObservation(ChatSessionService source,
        DesktopOriginalWorkLifetime.Original pageOriginal, Guid conversationId,
        Func<Task<TaskRunOriginalInitialChatObservationLease>> acquire,
        Func<ChatStreamEvent, Task> publish, Func<bool> presentationIsRetiring,
        Action demandActualSourceJoin)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _pageOriginal = pageOriginal ?? throw new ArgumentNullException(nameof(pageOriginal));
        if (conversationId == Guid.Empty) throw new ArgumentException("An actual conversation identity is required.", nameof(conversationId));
        _conversationId = conversationId;
        _acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _presentationIsRetiring = presentationIsRetiring ?? throw new ArgumentNullException(nameof(presentationIsRetiring));
        _demandActualSourceJoin = demandActualSourceJoin ?? throw new ArgumentNullException(nameof(demandActualSourceJoin));
        _retirement = new(StopOriginalAsync, () => Task.CompletedTask);
        _actualAcquisition = AcquireOriginalAsync();
        ActualObservation = ObserveOriginalAsync(); // Both complete drivers precede any factory callback.
    }

    internal Task<TaskRunInitialChatObservationResult?> ActualObservation { get; }
    internal bool IsPresentationRetiring => _presentationIsRetiring() || _retirement.IsRetiring;
    internal bool CanPruneHealthy
    {
        get
        {
            lock (_gate)
                return _retirement.OriginalClose is { IsCompletedSuccessfully: true } &&
                    ActualObservation.IsCompletedSuccessfully && _actualAcquisition.IsCompletedSuccessfully &&
                    (_actualSourceAcquisition is null || _actualSourceAcquisition.IsCompletedSuccessfully) &&
                    (_actualWait is null || _actualWait.IsCompletedSuccessfully) &&
                    (_actualSourceDetach is null || _actualSourceDetach.IsCompletedSuccessfully) &&
                    (_actualRequest is null || _actualRequest.IsCompletedSuccessfully) &&
                    (_actualEvents is null || _actualEvents.IsCompletedSuccessfully) &&
                    (_actualStopDriver is null || _actualStopDriver.IsCompletedSuccessfully) &&
                    (_actualSourceStop is null || _actualSourceStop.IsCompletedSuccessfully);
        }
    }
    internal ProviderExecutionContext? CurrentAcknowledgedContext
    {
        get
        {
            TaskRunOriginalInitialChatObservationLease? lease;
            lock (_gate) lease = _lease;
            if (lease is null) return null;
            DemandIssued(lease);
            var actual = lease.CurrentAcknowledgedContext;
            if (actual is not null) DemandAcknowledgedContext(actual);
            return actual;
        }
    }

    internal void BeginOriginalAcquisition()
    {
        if (_presentationIsRetiring() || _retirement.IsRetiring) RequestRetirement();
        else _start.TrySetResult(true);
    }

    private async Task<TaskRunOriginalInitialChatObservationLease?> AcquireOriginalAsync()
    {
        if (!await _start.Task.ConfigureAwait(false)) return null; // Source factory was never invoked.
        _pageOriginal.DemandPublication();
        Task<TaskRunOriginalInitialChatObservationLease>? actual = null;
        InvokePhysicalSource(() =>
        {
            lock (_gate) _actualSourceFactoryEntered = true;
            actual = _acquire() ?? throw new InvalidOperationException("The initial Tasks source returned no actual acquisition Task.");
            lock (_gate) _actualSourceAcquisition = actual; // Before this finite source scope can fail.
        });
        var lease = await _pageOriginal.AwaitAsync(actual!).ConfigureAwait(false);
        lock (_gate) _lease = lease; // A genuine late result remains captured after presentation seal.
        DemandIssued(lease);
        if (_presentationIsRetiring() || _retirement.IsRetiring) RequestRetirement();
        return lease;
    }

    private async Task<TaskRunInitialChatObservationResult?> ObserveOriginalAsync()
    {
        try
        {
            var lease = await _pageOriginal.AwaitAsync(_actualAcquisition).ConfigureAwait(false);
            if (lease is null) return null;
            DemandIssued(lease);
            if (!_presentationIsRetiring() && !_retirement.IsRetiring)
            {
                Task? actualEvents = null;
                InvokePhysicalSource(() =>
                {
                    actualEvents = ReadOriginalEventsAsync(lease);
                    lock (_gate) _actualEvents = actualEvents;
                });
                await _pageOriginal.AwaitAsync(actualEvents!).ConfigureAwait(false);
            }
            else RequestRetirement();
            Task<TaskRunInitialChatObservationResult>? actualWait = null;
            InvokePhysicalSource(() =>
            {
                actualWait = lease.WaitForOriginalObservationAsync(CancellationToken.None);
                lock (_gate) _actualWait = actualWait;
            });
            var result = await _pageOriginal.AwaitAsync(actualWait!).ConfigureAwait(false);
            if (result.AcknowledgedContext is { } context) DemandAcknowledgedContext(context);
            if (result.Disposition is not (TaskRunInitialChatObservationDisposition.ProducerTerminal or TaskRunInitialChatObservationDisposition.ObservationDetached))
                throw new InvalidOperationException("The genuine initial Tasks observer supplied an unknown disposition.");
            // A natural terminal observation still belongs to the admitted page body,
            // which must flush its actual events and finish attachment/status work.
            // That body then requests and joins observer cleanup in its finally.
            if (result.Disposition == TaskRunInitialChatObservationDisposition.ObservationDetached)
                RequestRetirement();
            return result; // ProducerTerminal never certifies a successful Task/business result.
        }
        catch (Exception cause)
        {
            _pageOriginal.Retain(cause);
            RequestRetirement(); // Withdraw failed observation publication, never business execution.
            _pageOriginal.ThrowRetained();
            throw;
        }
    }

    private async Task ReadOriginalEventsAsync(TaskRunOriginalInitialChatObservationLease lease)
    {
        IAsyncEnumerator<ChatStreamEvent>? iterator = null;
        var failed = false;
        try
        {
            InvokePhysicalSource(() => iterator = lease.ObserveOriginalEventsAsync(CancellationToken.None)
                .GetAsyncEnumerator(CancellationToken.None));
            while (true)
            {
                Task<bool>? move = null;
                InvokePhysicalSource(() => move = iterator!.MoveNextAsync().AsTask()); // Each SAME ValueTask once.
                if (!await _pageOriginal.AwaitAsync(move!).ConfigureAwait(false)) break;
                ChatStreamEvent? current = null;
                InvokePhysicalSource(() => current = iterator!.Current); // Read SAME successful Move before Dispose.
                if (_presentationIsRetiring() || _retirement.IsRetiring)
                { RequestRetirement(); continue; }
                var actualCurrent = current ?? throw new InvalidOperationException("The actual observation reader returned no current event.");
                if (actualCurrent.CanonicalTaskContext is { } context) DemandAcknowledgedContext(context);
                Task? publication = null;
                InvokePhysicalSource(() => publication = _publish(actualCurrent));
                await _pageOriginal.AwaitAsync(publication!).ConfigureAwait(false);
            }
        }
        catch (Exception cause) { _pageOriginal.Retain(cause); failed = true; }
        finally
        {
            if (iterator is not null)
            {
                Task? dispose = null;
                try { InvokePhysicalSource(() => dispose = iterator.DisposeAsync().AsTask()); }
                catch (Exception cause) { _pageOriginal.Retain(cause); failed = true; }
                if (dispose is not null)
                    try { await _pageOriginal.AwaitAsync(dispose).ConfigureAwait(false); }
                    catch (Exception cause) { _pageOriginal.Retain(cause); failed = true; }
            }
        }
        if (failed) _pageOriginal.ThrowRetained();
    }

    internal void InvokeOriginalPresentationCallback(Action callback) => InvokePhysicalSource(callback);

    private void InvokePhysicalSource(Action callback)
    {
        var owners = _synchronousSources ??= [];
        owners.Add(this);
        try { callback(); }
        catch (OperationCanceledException cause)
        { throw new AggregateException("A synchronous Tasks observation source supplied no canceled original Task.", cause); }
        finally { owners.RemoveAt(owners.Count - 1); }
    }

    private void DemandIssued(TaskRunOriginalInitialChatObservationLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!_source.IsIssuedOriginalTaskObservation(lease) || lease.ConversationId != _conversationId)
            throw new InvalidOperationException("The SAME Chat service did not issue this actual conversation's Tasks observation.");
    }

    private void DemandAcknowledgedContext(ProviderExecutionContext context)
    {
        if (context.ContextId != _conversationId || context.TaskId == Guid.Empty || context.ExecutionId == Guid.Empty || context.PersistenceRevision < 0)
            throw new InvalidOperationException("The Tasks observation supplied an inconsistent acknowledged context.");
        lock (_gate)
        {
            if (_firstAcknowledgedContext is { } first &&
                (context.TaskId != first.TaskId || context.ExecutionId != first.ExecutionId || context.ContextId != first.ContextId))
                throw new InvalidOperationException("A Tasks observation cannot substitute another Task/Run/context.");
            _firstAcknowledgedContext ??= context;
        }
    }

    internal void CaptureActualStopDriver(Task actual)
    {
        lock (_gate) _actualStopDriver = actual; // Owning page publishes whole command BEFORE its callbacks.
    }
    internal Task<TaskRunOriginalRunControlResult> AcquireActualStop(CancellationToken commandToken)
    {
        Task<TaskRunOriginalRunControlResult>? actual = null;
        InvokePhysicalSource(() =>
        {
            TaskRunOriginalInitialChatObservationLease lease;
            lock (_gate)
            {
                _actualStopFactoryEntered = true;
                lease = _lease ?? throw new InvalidOperationException("No genuine initial Tasks observation has been captured.");
            }
            DemandIssued(lease);
            actual = _source.StopObservedOriginalTaskAsync(lease, commandToken);
            lock (_gate) _actualSourceStop = actual;
        });
        return actual!; // The page command original enrolls and joins this SAME raw Task/group.
    }

    private async Task StopOriginalAsync()
    {
        _start.TrySetResult(false);
        var failed = false;
        TaskRunOriginalInitialChatObservationLease? lease;
        lock (_gate) lease = _lease;
        Task? request = lease is null ? null : AcquireOriginalRequest(lease);
        try { await _pageOriginal.AwaitAsync(_actualAcquisition).ConfigureAwait(false); }
        catch (Exception cause) { _pageOriginal.Retain(cause); failed = true; }
        lock (_gate) lease = _lease;
        if (lease is not null)
        {
            request ??= AcquireOriginalRequest(lease);
            Task? detach = null;
            try
            {
                _retirement.RunCloseCallback(() =>
                {
                    DemandIssued(lease);
                    detach = lease.DetachAndDrainAsync();
                    lock (_gate) _actualSourceDetach = detach;
                });
            }
            catch (Exception cause) { _pageOriginal.Retain(cause); failed = true; }
            if (detach is not null)
                try { await _pageOriginal.AwaitAsync(detach).ConfigureAwait(false); }
                catch (Exception cause) { _pageOriginal.Retain(cause); failed = true; }
            else { _pageOriginal.Retain(new InvalidOperationException("No actual Tasks observation detach Task was acquired.")); failed = true; }
        }
        if (request is not null)
            try { await _pageOriginal.AwaitAsync(request).ConfigureAwait(false); }
            catch (Exception cause) { _pageOriginal.Retain(cause); failed = true; }
        try { await _pageOriginal.AwaitAsync(ActualObservation).ConfigureAwait(false); }
        catch (Exception cause) { _pageOriginal.Retain(cause); failed = true; }
        if (failed) _pageOriginal.ThrowRetained();
    }

    private Task AcquireOriginalRequest(TaskRunOriginalInitialChatObservationLease lease)
    {
        Task actual;
        TaskCompletionSource? start = null;
        lock (_gate)
        {
            if (_actualRequest is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _actualRequest = RequestOriginalAsync(start.Task, lease);
            }
            actual = _actualRequest;
        }
        start?.SetResult();
        return actual;
    }
    private async Task RequestOriginalAsync(Task start, TaskRunOriginalInitialChatObservationLease lease)
    {
        await start.ConfigureAwait(false);
        try { _retirement.RunCloseCallback(() => { DemandIssued(lease); lease.RequestOriginalObservationRetirement(); }); }
        catch (Exception cause) { _pageOriginal.Retain(cause); _pageOriginal.ThrowRetained(); throw; }
    }

    internal void RequestRetirement() => _retirement.RequestRetirement();
    internal void DemandExternalClose()
    {
        if (_synchronousSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual initial Tasks source/presentation callback must return before its encompassing retirement join.");
        _retirement.DemandExternalClose();
        TaskRunOriginalInitialChatObservationLease? lease;
        bool businessDependentOriginalPending;
        lock (_gate)
        {
            lease = _lease;
            businessDependentOriginalPending = (_actualSourceFactoryEntered && !_actualAcquisition.IsCompleted) ||
                (_actualStopFactoryEntered && _actualStopDriver is { IsCompleted: false });
        }
        if (businessDependentOriginalPending) _demandActualSourceJoin();
        lease?.DemandExternalOriginalObservationJoin();
    }
    internal Task CloseAndDrainAsync()
    { DemandExternalClose(); return _retirement.CloseAndDrainAsync(); }
}
