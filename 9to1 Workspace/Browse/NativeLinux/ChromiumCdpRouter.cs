using System.Text.Json;

namespace HavenOS.Apps.Browse;

/// <summary>
/// Correlates CDP request ids with pending completion and dispatches CDP events.
/// Responses may arrive in any order and interleaved with events; each request
/// carries a unique id so out-of-order routing is deterministic. Fragmented JSON
/// is reassembled by the socket receive loop before it reaches <see cref="Route"/>.
/// </summary>
public sealed class ChromiumCdpRouter : IDisposable
{
    private const int MaxPending = 256;
    private readonly object _gate = new();
    private readonly Dictionary<int, Pending> _pending = new();
    private int _nextId;
    private bool _disposed;

    /// <summary>Raised for every CDP event frame.</summary>
    public event EventHandler<CdpEvent>? Raised;

    private sealed class Pending(TaskCompletionSource<JsonElement> completion, CancellationTokenRegistration registration)
    {
        public TaskCompletionSource<JsonElement> Completion { get; } = completion;
        public CancellationTokenRegistration Registration { get; } = registration;
    }

    /// <summary>Allocates the next request id.</summary>
    public int NextId()
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_disposed, this);
        return Interlocked.Increment(ref _nextId);
    }

    /// <summary>Registers a pending request and returns the task that completes when
    /// its correlated response is routed. Register before sending so a fast
    /// response can never be missed.</summary>
    public Task<JsonElement> WaitAsync(int id, CancellationToken cancellationToken)
    {
        if (id < 1) throw new ArgumentOutOfRangeException(nameof(id));

        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = default;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending.ContainsKey(id)) throw new InvalidOperationException("A CDP request id is already pending.");
            if (_pending.Count >= MaxPending) throw new InvalidOperationException("Chromium retains too many unresolved CDP requests.");
            // A pre-cancelled token must never insert an entry the callback cannot
            // remove. Check before registering so the request settles immediately.
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return completion.Task;
            }
            if (cancellationToken.CanBeCanceled)
            {
                // Register first, then decide whether to insert. The callback and
                // the check/insert both run under _gate, so a cancellation that
                // arrives between the pre-check and insertion either runs the
                // callback synchronously (it sees the empty table and settles the
                // completion, which we detect) or queued until the entry is in
                // place. Either way no dead pending entry can be left behind.
                registration = cancellationToken.Register(() =>
                {
                    lock (_gate)
                    {
                        if (_pending.TryGetValue(id, out var pending))
                        {
                            _pending.Remove(id);
                            pending.Completion.TrySetCanceled(cancellationToken);
                        }
                        else
                        {
                            // Cancellation beat insertion: settle now so the
                            // caller never hangs on a leaked entry.
                            completion.TrySetCanceled(cancellationToken);
                        }
                    }
                });
                // A synchronous callback (already-cancelled token racing Register)
                // just settled the completion before insertion; bail without
                // inserting a dead entry.
                if (completion.Task.IsCanceled)
                {
                    registration.Dispose();
                    return completion.Task;
                }
            }
            _pending.Add(id, new Pending(completion, registration));
        }
        return completion.Task;
    }

    /// <summary>Cancels one pending request by id (used when a send fails so the
    /// caller's awaited command never leaks or hangs).</summary>
    public void Cancel(int id)
    {
        Pending? pending;
        lock (_gate) { if (_pending.TryGetValue(id, out pending)) _pending.Remove(id); }
        if (pending is not null)
        {
            try { pending.Completion.TrySetCanceled(); }
            finally { pending.Registration.Dispose(); }
        }
    }

    /// <summary>Routes one decoded frame: completes a pending response or raises an
    /// event. Unknown/expired response ids are dropped.</summary>
    public void Route(JsonDocument frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var responseId = ChromiumCdpWire.TryGetResponseId(frame);
        if (responseId is not null)
        {
            Pending? pending;
            lock (_gate) { if (_pending.TryGetValue(responseId.Value, out pending)) _pending.Remove(responseId.Value); }
            if (pending is null) return; // late response for an already-cancelled or unknown request
            try
            {
                var result = ChromiumCdpWire.TryGetResult(frame);
                if (result is not null) { pending.Completion.TrySetResult(result.Value); return; }
                var error = ChromiumCdpWire.TryGetError(frame);
                if (error is not null)
                {
                    pending.Completion.TrySetException(new ChromiumCdpException("CDP error: " + error));
                    return;
                }
                pending.Completion.TrySetException(new InvalidOperationException("A CDP response had neither result nor error."));
            }
            finally { pending.Registration.Dispose(); }
            return;
        }

        var method = ChromiumCdpWire.TryGetMethod(frame);
        if (method is null) return;
        Raised?.Invoke(this, new CdpEvent(method, ChromiumCdpWire.TryGetParams(frame), ChromiumCdpWire.TryGetSessionId(frame)));
    }

    /// <summary>Cancels every pending request when the connection is closing.</summary>
    public void CancelAll()
    {
        KeyValuePair<int, Pending>[] pending;
        lock (_gate)
        {
            pending = [.. _pending];
            _pending.Clear();
        }
        foreach (var (_, item) in pending)
        {
            try { item.Completion.TrySetCanceled(); }
            finally { item.Registration.Dispose(); }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        CancelAll();
    }
}
