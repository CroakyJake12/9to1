using System.Threading.Channels;
using Haven.Core;

namespace Haven.Application;

/// <summary>Nonblocking execution observations. Delivery to Published is not durable append
/// acknowledgement and is never canonical Task/Run or permission acceptance.</summary>
public sealed class ExecutionEventHub : IExecutionEventSink, IAsyncDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(150);
    private const int MaximumBatchSize = 64;
    private const int MaximumPendingFallbacks = 8192;
    private const int MaximumRetainedEvents = 16384; // Original channel8192 plus finite fallback8192.
    private readonly object _gate = new();
    private readonly IExecutionEventRepository _repository;
    private readonly Channel<ExecutionEvent> _channel;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _collector;
    private readonly HashSet<ExecutionEvent> _retained = new(ReferenceEqualityComparer.Instance);
    private readonly List<TaskCompletionSource> _publications = [];
    private readonly List<EnqueueOriginal> _enqueues = [];
    private readonly List<PersistenceFailureObservation> _failures = [];
    private readonly List<Exception> _observerErrors = [];
    private readonly HashSet<ExecutionEvent> _observerFailedPublications = new(ReferenceEqualityComparer.Instance);
    private readonly List<Exception> _collectorErrors = [];
    private readonly AsyncLocal<OriginalOwnerContext?> _ownerContext = new();
    private sealed class OriginalOwnerContext(OriginalOwnerContext? parent)
    {
        public OriginalOwnerContext? Parent { get; } = parent;
        public volatile bool Active = true;
    }
    private bool IsInsideLiveOriginal()
    {
        for (var context = _ownerContext.Value; context is not null; context = context.Parent)
            if (context.Active) return true;
        return false;
    }
    private OriginalOwnerContext EnterOriginalOwner()
    {
        var current = new OriginalOwnerContext(_ownerContext.Value);
        _ownerContext.Value = current;
        return current;
    }
    private void LeaveOriginalOwner(OriginalOwnerContext current)
    {
        current.Active = false;
        _ownerContext.Value = current.Parent;
    }
    private Task? _close;
    private bool _sealed;
    private long _queuedFallbacks;
    private long _persistenceFailures;
    private long _refusedPublications;
    private PublicationRefusalObservation? _firstRefusal;

    /// <summary>Safe original event records and exact producer Tasks/causes remain in this
    /// owner's custody. This observation is not a no-effect, replay or permission witness.</summary>
    public sealed record PersistenceFailureObservation(
        IReadOnlyList<ExecutionEvent> Events, Task? OriginalAppendOrEnqueue,
        IReadOnlyList<Exception> OriginalErrors, bool WasEnqueue);
    public sealed record PublicationRefusalObservation(ExecutionEvent Event, string Reason, Exception OriginalCause);
    public PublicationRefusalObservation? FirstPublicationRefusal
    {
        get { lock (_gate) return _firstRefusal; }
    }
    private sealed class EnqueueOriginal(Task actual)
    {
        public Task Actual { get; } = actual;
        public TaskCompletionSource Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task? ObservationTask { get; set; }
    }

    public ExecutionEventHub(IExecutionEventRepository repository)
    {
        _repository = repository;
        _channel = Channel.CreateBounded<ExecutionEvent>(new BoundedChannelOptions(8192)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _collector = CollectAsync(_lifetime.Token);
    }

    public event EventHandler<ExecutionEvent>? Published;
    public long QueuedFallbackCount => Interlocked.Read(ref _queuedFallbacks);
    public long PersistenceFailureCount => Interlocked.Read(ref _persistenceFailures);
    public long RefusedPublicationCount => Interlocked.Read(ref _refusedPublications);
    public IReadOnlyList<PersistenceFailureObservation> OriginalPersistenceFailures
    {
        get { lock (_gate) return Array.AsReadOnly(_failures.ToArray()); }
    }

    public bool TryPublish(ExecutionEvent executionEvent)
    {
        ArgumentNullException.ThrowIfNull(executionEvent);
        var safe = Redact(executionEvent);
        if (safe.SafeMetadata is { } metadata)
            safe = safe with
            {
                SafeMetadata = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(metadata, StringComparer.Ordinal))
            }; // Published/provider callbacks cannot replace the SAME redacted metadata.
        var originalPublication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_sealed || _retained.Count >= MaximumRetainedEvents)
            {
                ObserveRefusal(safe, _sealed ? "Closed" : "RetainedCapacity");
                return false; // Refused before another business original; never claimed queued/durable.
            }
            _retained.Add(safe);
            _publications.Add(originalPublication);
        }
        try
        {
            if (Published is { } published)
                foreach (EventHandler<ExecutionEvent> handler in published.GetInvocationList())
                {
                    var owner = EnterOriginalOwner();
                    try { handler(this, safe); }
                    catch (Exception error)
                    {
                        lock (_gate)
                        {
                            _observerFailedPublications.Add(safe);
                            AddCauses(_observerErrors, null, error);
                        }
                    }
                    finally { LeaveOriginalOwner(owner); }
                }
            if (_channel.Writer.TryWrite(safe)) return true;
            Interlocked.Increment(ref _queuedFallbacks);
            Task? actual = null;
            try
            {
                // Consume the SAME WriteAsync ValueTask once and retain it before observing.
                EnqueueOriginal original;
                lock (_gate)
                {
                    PruneTerminalEnqueueObservations();
                    if (_enqueues.Count >= MaximumPendingFallbacks)
                    {
                        var refusal = ObserveRefusal(safe, "PendingFallbackCapacity");
                        RecordFailure([safe], null, refusal.OriginalCause, wasEnqueue: true);
                        return false; // No another WriteAsync acquired at the finite boundary.
                    }
                    actual = _channel.Writer.WriteAsync(safe, _lifetime.Token).AsTask();
                    original = new EnqueueOriginal(actual);
                    _enqueues.Add(original);
                }
                original.ObservationTask = ObserveOriginalEnqueueAsync(safe, original);
            }
            catch (Exception error) { RecordFailure([safe], actual, error, wasEnqueue: true); }
            return false;
        }
        finally
        {
            lock (_gate)
            {
                _publications.Remove(originalPublication);
                // Registry removal and asynchronous terminal notification share one
                // gate; close cannot observe an unregistered, unsignaled original.
                originalPublication.TrySetResult();
            }
        }
    }

    private PublicationRefusalObservation ObserveRefusal(ExecutionEvent safe, string reason)
    {
        // Called under the admission gate. Preserve the first exact safe refusal/cause plus
        // count; unlimited refused external traffic cannot allocate unlimited diagnostics.
        Interlocked.Increment(ref _refusedPublications);
        var current = new PublicationRefusalObservation(safe, reason,
            new InvalidOperationException("Execution event publication was refused: " + reason));
        _firstRefusal ??= current;
        return current;
    }

    private async Task ObserveOriginalEnqueueAsync(ExecutionEvent safe, EnqueueOriginal original)
    {
        try { await original.Actual.ConfigureAwait(false); }
        catch (Exception error) { RecordFailure([safe], original.Actual, error, wasEnqueue: true); }
        finally { original.Observed.TrySetResult(); }
        // Keep this SAME observation Task owned until its actual terminal state is
        // observed under the admission gate; removing from inside its finally is early.
    }

    private void PruneTerminalEnqueueObservations()
    {
        // Called only under _gate. A completed actual observation can be pruned after
        // its original causes were recorded; never prune a still-running finally.
        for (var i = _enqueues.Count - 1; i >= 0; i--)
        {
            var observation = _enqueues[i].ObservationTask;
            if (observation is not { IsCompleted: true }) continue;
            if (observation.Exception is { } error) AddCauses(_collectorErrors, observation, error);
            _enqueues.RemoveAt(i);
        }
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        var batch = new List<ExecutionEvent>(MaximumBatchSize);
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (batch.Count < MaximumBatchSize && _channel.Reader.TryRead(out var item)) batch.Add(item);
                if (batch.Count < MaximumBatchSize) await Task.Delay(FlushInterval, cancellationToken).ConfigureAwait(false);
                while (batch.Count < MaximumBatchSize && _channel.Reader.TryRead(out var item)) batch.Add(item);
                if (batch.Count == 0) continue;
                await PersistOriginalBatchAsync(batch.ToArray(), cancellationToken).ConfigureAwait(false);
                batch.Clear(); // Failed originals are retained in _failures/_retained, not discarded.
            }
        }
        catch (Exception error)
        {
            lock (_gate) AddCauses(_collectorErrors, null, error);
            _channel.Writer.TryComplete(error); // Actual failure releases capacity writers; not a timeout witness.
        }
        finally
        {
            while (_channel.Reader.TryRead(out var item)) batch.Add(item);
            for (var offset = 0; offset < batch.Count; offset += MaximumBatchSize)
                await PersistOriginalBatchAsync(batch.Skip(offset).Take(MaximumBatchSize).ToArray(), CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task PersistOriginalBatchAsync(IReadOnlyList<ExecutionEvent> batch, CancellationToken cancellationToken)
    {
        var originals = Array.AsReadOnly(batch.ToArray());
        Task? actual = null;
        var owner = EnterOriginalOwner();
        try
        {
            actual = _repository.AppendAsync(originals, cancellationToken);
            await actual.ConfigureAwait(false);
            lock (_gate)
                foreach (var original in originals)
                    if (!_observerFailedPublications.Contains(original)) _retained.Remove(original);
            // Failed observer publications also consume the finite original admission
            // budget; successful persistence cannot discard their original causes.
        }
        catch (Exception error)
        {
            Interlocked.Increment(ref _persistenceFailures);
            RecordFailure(originals, actual, error, wasEnqueue: false);
            // A generic failure may be post-persist. No no-effect/replay authority exists on
            // IExecutionEventRepository; retain uncertainty instead of automatically redoing it.
        }
        finally { LeaveOriginalOwner(owner); }
    }

    private void RecordFailure(IReadOnlyList<ExecutionEvent> batch, Task? actual, Exception observed, bool wasEnqueue)
    {
        var causes = new List<Exception>();
        AddCauses(causes, actual, observed);
        lock (_gate) _failures.Add(new(Array.AsReadOnly(batch.ToArray()), actual,
            Array.AsReadOnly(causes.ToArray()), wasEnqueue));
    }

    private static void AddCauses(List<Exception> errors, Task? original, Exception observed)
    {
        void Add(Exception error)
        {
            if (error is AggregateException { InnerExceptions.Count: > 0 } compound)
                foreach (var member in compound.InnerExceptions) Add(member);
            else if (!errors.Any(existing => ReferenceEquals(existing, error))) errors.Add(error);
        }
        if (original?.Exception is { InnerExceptions.Count: > 0 } fault) Add(fault);
        else Add(observed); // Includes opaque empty aggregate and actual cancelled-task cause.
    }

    private static ExecutionEvent Redact(ExecutionEvent value)
    {
        var metadata = value.SafeMetadata?.ToDictionary(
            pair => SensitiveTextRedactor.Redact(pair.Key, 128),
            pair => SensitiveTextRedactor.Redact(pair.Value, 2_000),
            StringComparer.Ordinal);
        var failure = value.Failure is null ? null : value.Failure with
        {
            Message = SensitiveTextRedactor.Redact(value.Failure.Message, 4_000),
            ProviderMessage = SensitiveTextRedactor.Redact(value.Failure.ProviderMessage, 4_000)
        };
        return value with
        {
            Name = SensitiveTextRedactor.Redact(value.Name, 256),
            SafeReasoningSummary = SensitiveTextRedactor.Redact(value.SafeReasoningSummary, 2_000),
            SafeDetail = SensitiveTextRedactor.Redact(value.SafeDetail, 8_000),
            ComponentId = SensitiveTextRedactor.Redact(value.ComponentId, 256),
            Failure = failure,
            SafeMetadata = metadata
        };
    }

    /// <summary>Stop admission without awaiting any original owned by this callback.
    /// The external lifetime owner must still call DisposeAsync and join the SAME close.</summary>
    public void RequestClose()
    {
        lock (_gate) _sealed = true;
    }

    public ValueTask DisposeAsync()
    {
        if (IsInsideLiveOriginal())
            throw new InvalidOperationException("An execution event original cannot join its own close. RequestClose and let the external owner join.");
        lock (_gate)
        {
            if (_close is not null) return new ValueTask(_close);
            _sealed = true;
            var publications = _publications.Select(original => original.Task).ToArray();
            // Retain and return the SAME actual close Task, rather than a proxy whose
            // completion could detach the close producer and its original fault payload.
            _close = CloseOriginalsAsync(publications);
            return new ValueTask(_close);
        }
    }

    private async Task CloseOriginalsAsync(Task[] publications)
    {
        var errors = new List<Exception>();
        try
        {
            foreach (var actual in publications)
                try { await actual.ConfigureAwait(false); } catch (Exception error) { AddCauses(errors, actual, error); }
            EnqueueOriginal[] enqueues;
            lock (_gate) enqueues = _enqueues.ToArray();
            // Keep the collector alive until admitted actual capacity writes and their
            // original observations settle; closing the writer first would strand/drop them.
            foreach (var original in enqueues)
            {
                try { await original.Actual.ConfigureAwait(false); }
                catch (Exception error) { AddCauses(errors, original.Actual, error); }
                var observation = original.ObservationTask;
                if (observation is not null)
                    try { await observation.ConfigureAwait(false); }
                    catch (Exception error) { AddCauses(errors, observation, error); }
            }
            _channel.Writer.TryComplete();
            try { await _collector.ConfigureAwait(false); }
            catch (Exception error) { AddCauses(errors, _collector, error); }
        }
        catch (Exception error) { AddCauses(errors, null, error); }
        finally
        {
            try { _lifetime.Dispose(); } catch (Exception error) { AddCauses(errors, null, error); }
            lock (_gate)
            {
                foreach (var failure in _failures) foreach (var original in failure.OriginalErrors) AddCauses(errors, null, original);
                foreach (var error in _observerErrors) AddCauses(errors, null, error);
                foreach (var error in _collectorErrors) AddCauses(errors, null, error);
            }
            if (errors.Count > 0)
                throw new AggregateException("Original execution event close/persistence failed.", errors);
        }
    }
}

public sealed class ExecutionTraceService(
    IExecutionEventRepository events,
    IActionFeedbackRepository feedback)
{
    public Task<IReadOnlyList<ExecutionSummary>> SearchAsync(string? query, int limit, CancellationToken cancellationToken) =>
        events.SearchExecutionsAsync(query, Math.Clamp(limit, 1, 500), cancellationToken);

    public Task<IReadOnlyList<ExecutionEvent>> GetTraceAsync(Guid executionId, CancellationToken cancellationToken) =>
        events.GetExecutionAsync(executionId, cancellationToken);

    public Task UpsertFeedbackAsync(ActionFeedback value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Rating is null && string.IsNullOrWhiteSpace(value.Comment))
            throw new ArgumentException("Feedback requires a rating or comment.", nameof(value));
        var safe = value with
        {
            Comment = SensitiveTextRedactor.Redact(value.Comment, 4_000),
            SafeContext = SensitiveTextRedactor.Redact(value.SafeContext, 2_000),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return feedback.UpsertAsync(safe, cancellationToken);
    }

    public Task DeleteFeedbackAsync(Guid feedbackId, CancellationToken cancellationToken) =>
        feedback.DeleteAsync(feedbackId, cancellationToken);
}
