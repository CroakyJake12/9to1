using System.Collections.Frozen;
using Haven.Core;

namespace Haven.Application;

/// <summary>Privately issued SAME ordinary producer and actual source ledger. No Task,
/// permission or resource grant is issued; the app owns the explicit business token.</summary>
public sealed class ChatOrdinaryOriginalInvocation
{
    private enum Role { Read, DurableWrite, ProviderMove, BodyMove, EtaRead, Cleanup }
    private sealed record Source(Task Actual, Role Role, CancellationToken Token);
    internal readonly ChatSessionService Issuer;
    private readonly Action<Action> _scope;
    private readonly CancellationToken _business;
    private readonly object _gate = new();
    private sealed class SourceFrame(ChatOrdinaryOriginalInvocation owner, SourceFrame? parent)
    {
        internal readonly ChatOrdinaryOriginalInvocation Owner = owner;
        internal readonly SourceFrame? Parent = parent;
        internal volatile Task? Actual;
        internal volatile bool Acquiring = true;
    }
    private static readonly AsyncLocal<SourceFrame?> CurrentSource = new();
    [ThreadStatic] private static ChatOrdinaryOriginalInvocation? PhysicalSource;
    private readonly List<Source> _sources = [];
    private readonly List<Exception> _direct = [];
    // Preserve actual objects independently of compiler-generated iterator locals.
    private readonly List<object> _originalIterators = [];
    private readonly List<object> _originalStreams = [];
    private readonly List<IDisposable> _originalResources = [];
    private ChatExecutionTracker? _originalTracker;
    private IAsyncDisposable? _originalBodyIterator;
    private IAsyncEnumerable<ChatStreamEvent>? _stream;
    private Task? _actualBodyDispose;
    private Task? _actualTrackerDispose;
    private Task? _join;
    private int _consumed;
    private int _pendingSources;
    private bool _trackerCreated;
    private const int MaximumUnresolvedSources = 256;

    internal ChatOrdinaryOriginalInvocation(ChatSessionService issuer, CancellationToken business, Action<Action> scope)
    { Issuer = issuer; _business = business; _scope = scope; }
    internal void Bind(IAsyncEnumerable<ChatStreamEvent> sameStream)
    { lock (_gate) { if (_stream is not null) throw new InvalidOperationException("Original stream already bound."); _stream = sameStream; } }
    public IAsyncEnumerable<ChatStreamEvent> ConsumeOriginal()
    {
        if (Interlocked.Exchange(ref _consumed, 1) != 0) throw new InvalidOperationException("Consume SAME ordinary stream once.");
        return new StreamReader<ChatStreamEvent>(this, _stream ?? throw new InvalidOperationException("Original stream unavailable."), body: true);
    }
    internal IAsyncEnumerable<string> ProviderStream(Func<IAsyncEnumerable<string>> source) =>
        new StreamReader<string>(this, Invoke(() =>
        {
            var actual = source();
            lock (_gate) _originalStreams.Add(actual);
            return actual;
        }), body: false);
    internal Task Write(Func<Task> source) => Capture(source, Role.DurableWrite, _business);
    // Child memory originals include independent owner cleanup. Their role is unknown
    // here, so never normalize cancellation merely because a read wrapper was requested.
    internal void RetainUnknownMemoryOriginal(Task actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        lock (_gate)
        {
            if (_sources.Any(row => ReferenceEquals(row.Actual, actual))) return;
            _sources.Add(new(actual, Role.Cleanup, default)); // Retain accepted source before any refusal.
            if (_join is not null) throw new InvalidOperationException("A memory original arrived after source settlement.");
        }
    }
    internal Task<T> Read<T>(Func<Task<T>> source, CancellationToken token, bool eta = false) =>
        AwaitOpaque(Capture(source, eta ? Role.EtaRead : Role.Read, token));
    internal Task Read(Func<Task> source, CancellationToken token) => AwaitOpaque(Capture(source, Role.Read, token));
    internal IAsyncDisposable OwnTracker(ChatExecutionTracker actual)
    { lock (_gate) { _trackerCreated = true; _originalTracker = actual; } return new Tracker(this, actual); }
    internal T InvokeOriginal<T>(Func<T> source) => Invoke(source);
    internal void InvokeOriginal(Action source) => Invoke(() => { source(); return true; });
    internal T CreateResource<T>(Func<T> source) where T : IDisposable => Invoke(() =>
    {
        var actual = source();
        lock (_gate) _originalResources.Add(actual);
        return actual;
    });
    internal IDisposable OwnResource(IDisposable actual) => new Resource(this, actual);

    /// <summary>Existing source-issued body only, including an acquisition whose scope failed
    /// after returning the actual iterator. Reading this does not create another source.</summary>
    public IAsyncDisposable? OriginalBodyIterator { get { lock (_gate) return _originalBodyIterator; } }
    /// <summary>Actual admitted body moves that must settle before SAME iterator Dispose.
    /// Their causes remain retained by this source ledger until final qualification.</summary>
    public IReadOnlyList<Task> ReadOriginalBodyMoves()
    { DemandExternalOriginalJoin(); lock (_gate) return _sources.Where(row => row.Role == Role.BodyMove).Select(row => row.Actual).ToArray(); }
    public void DemandExternalOriginalJoin()
    {
        if (ReferenceEquals(PhysicalSource, this)) throw new InvalidOperationException("An ordinary source cannot join itself.");
        for (var frame = CurrentSource.Value; frame is not null; frame = frame.Parent)
            if (ReferenceEquals(frame.Owner, this) && (frame.Acquiring || frame.Actual is { IsCompleted: false }))
                throw new InvalidOperationException("An ordinary source cannot join its own actual Task.");
    }
    public Task JoinOriginalSourcesAsync()
    {
        DemandExternalOriginalJoin();
        lock (_gate)
        {
            if (_actualBodyDispose is null) throw new InvalidOperationException("Dispose SAME original body before joining source ledger.");
            if (_pendingSources != 0) throw new InvalidOperationException("An original source acquisition has not returned its actual Task.");
            return _join ??= JoinAsync(_sources.ToArray(), _direct.ToArray(), _actualBodyDispose, _actualTrackerDispose, _trackerCreated);
        }
    }
    public bool IsExpectedOriginalBodyCancellation(Task sameOriginalMove, Exception observed)
    {
        lock (_gate)
            return _join?.IsCompletedSuccessfully == true &&
                _sources.Any(row => ReferenceEquals(row.Actual, sameOriginalMove) && row.Role == Role.BodyMove) &&
                ExpectedCanceled(sameOriginalMove, observed, _business) &&
                _sources.Any(row => row.Role is Role.Read or Role.ProviderMove && row.Token == _business &&
                    IsCanceledByOriginalToken(row.Actual, _business));
    }
    private T Invoke<T>(Func<T> source)
    {
        T result = default!;
        var ownerThread = Environment.CurrentManagedThreadId;
        var active = true;
        var claimed = false;
        void Acquire()
        {
            lock (_gate)
            {
                if (!active || claimed || Environment.CurrentManagedThreadId != ownerThread)
                    throw new InvalidOperationException("Acquire each original synchronously once within its SAME owning scope.");
                claimed = true;
            }
            var previousPhysical = PhysicalSource;
            PhysicalSource = this;
            try { result = source(); }
            finally { PhysicalSource = previousPhysical; }
        }
        try
        {
            _scope(Acquire);
            if (!claimed) throw new InvalidOperationException("The original owning scope did not acquire its source.");
            return result;
        }
        catch (Exception actual)
        {
            lock (_gate) _direct.Add(actual);
            // Synchronous OCE supplies no canceled source Task, and must not be
            // translated by an async builder into an apparently canceled body.
            if (actual is OperationCanceledException) throw new AggregateException("Synchronous ordinary source fault.", actual);
            throw;
        }
        finally { lock (_gate) active = false; }
    }
    private TTask Capture<TTask>(Func<TTask> source, Role role, CancellationToken token, Action<TTask>? captureReceipt = null) where TTask : Task
    {
        lock (_gate)
        {
            // Release only independently joined healthy tasks. Unknown failures,
            // canceled durable writers and cleanup tasks stay retained.
            for (var i = _sources.Count - 1; i >= 0; i--)
                if (_sources[i].Actual.IsCompletedSuccessfully)
                { _sources[i].Actual.GetAwaiter().GetResult(); _sources.RemoveAt(i); }
            if (_join is not null) throw new InvalidOperationException("Original source ledger is sealed.");
            if (role != Role.Cleanup && _sources.Count + _pendingSources >= MaximumUnresolvedSources)
                throw new InvalidOperationException("Unresolved original ordinary sources require inspection.");
            // The finite body/provider/tracker cleanup stages must still be acquired after
            // an admission limit, otherwise a bound could abandon an actual resource.
            _pendingSources++;
        }
        var previous = CurrentSource.Value;
        var frame = new SourceFrame(this, previous);
        CurrentSource.Value = frame;
        try
        {
            return Invoke(() =>
            {
                var actual = source() ?? throw new InvalidOperationException("Actual source returned no Task.");
                frame.Actual = actual;
                // Enroll before the owning scope's post-callback freshness checks. A
                // late refusal cannot abandon an already accepted raw writer/provider.
                lock (_gate) _sources.Add(new(actual, role, token));
                captureReceipt?.Invoke(actual);
                return actual;
            });
        }
        finally
        {
            frame.Acquiring = false;
            CurrentSource.Value = previous;
            lock (_gate) _pendingSources--;
        }
    }
    private static async Task<T> AwaitOpaque<T>(Task<T> actual)
    {
        try { return await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted) { throw actual.Exception!; }
    }
    private static async Task AwaitOpaque(Task actual)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted) { throw actual.Exception!; }
    }
    private static bool ExpectedCanceled(Task actual, Exception observed, CancellationToken sameToken) =>
        actual.IsCanceled && sameToken.CanBeCanceled && sameToken.IsCancellationRequested &&
        observed is OperationCanceledException cancellation && cancellation.CancellationToken == sameToken;
    private static bool IsCanceledByOriginalToken(Task actual, CancellationToken sameToken)
    {
        if (!actual.IsCanceled) return false;
        try { actual.GetAwaiter().GetResult(); return false; }
        catch (Exception observed) { return ExpectedCanceled(actual, observed, sameToken); }
    }
    private async Task JoinAsync(Source[] sources, Exception[] direct, Task bodyDispose, Task? trackerDispose, bool trackerCreated)
    {
        var failures = new List<Exception>(direct);
        var observed = new List<(Source Source, Exception Cause)>();
        // Settlement precedes classification. A scope may fail after accepting a held
        // raw cleanup/write; no early status snapshot can acknowledge its outcome.
        foreach (var row in sources)
            try { await row.Actual.ConfigureAwait(false); }
            catch (Exception cause) { observed.Add((row, cause)); }
        var cleanupAcknowledged = bodyDispose.IsCompletedSuccessfully && (!trackerCreated || trackerDispose?.IsCompletedSuccessfully == true);
        var businessSourceCanceled = sources.Any(row => row.Role is Role.Read or Role.ProviderMove &&
            row.Token == _business && IsCanceledByOriginalToken(row.Actual, _business));
        foreach (var item in observed)
        {
            var row = item.Source;
            var bodyOrRead = row.Role is Role.Read or Role.BodyMove or Role.ProviderMove;
            var allowedToken = bodyOrRead ? _business : row.Token;
            if (cleanupAcknowledged && (bodyOrRead || row.Role == Role.EtaRead) &&
                (row.Role != Role.BodyMove || businessSourceCanceled) &&
                row.Token == allowedToken && ExpectedCanceled(row.Actual, item.Cause, allowedToken)) continue;
            // Only actual Task.Exception is a CLR-owned envelope. Its direct
            // nested/empty foreign aggregate causes remain opaque identities.
            if (row.Actual.Exception is { } envelope) failures.AddRange(envelope.InnerExceptions);
            else failures.Add(item.Cause);
        }
        if (!cleanupAcknowledged) failures.Add(new InvalidOperationException("Original body/tracker cleanup was not acknowledged."));
        GC.KeepAlive(_originalTracker);
        GC.KeepAlive(_originalResources);
        GC.KeepAlive(_originalIterators);
        GC.KeepAlive(_originalStreams);
        if (failures.Count != 0) throw new AggregateException("Original ordinary source ledger did not settle cleanly.", failures.Distinct<Exception>(ReferenceEqualityComparer.Instance));
    }
    private sealed class Resource(ChatOrdinaryOriginalInvocation owner, IDisposable actual) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            owner.InvokeOriginal(actual.Dispose);
            _disposed = true;
        }
    }
    private sealed class Tracker(ChatOrdinaryOriginalInvocation owner, ChatExecutionTracker actual) : IAsyncDisposable
    {
        private bool _disposeAttempted;
        private Task? _rawDispose;
        private Task? _dispose;
        public ValueTask DisposeAsync()
        {
            var acquire = false;
            lock (owner._gate)
            {
                if (_rawDispose is null)
                {
                    if (_disposeAttempted) throw new InvalidOperationException("Original tracker cleanup acquisition is unresolved; it cannot replay.");
                    _disposeAttempted = true; acquire = true;
                }
            }
            if (acquire)
                owner.Capture(() => actual.DisposeAsync().AsTask(), Role.Cleanup, default, raw =>
                {
                    lock (owner._gate) { owner._actualTrackerDispose = raw; _rawDispose = raw; }
                });
            // No unobserved adapter is created when a scope rejects after accepting raw
            // cleanup. A later join borrows the SAME receipt, never reacquires cleanup.
            lock (owner._gate) return new(_dispose ??= AwaitOpaque(_rawDispose!));
        }
    }
    private sealed class StreamReader<T>(ChatOrdinaryOriginalInvocation owner, IAsyncEnumerable<T> stream, bool body) : IAsyncEnumerable<T>
    {
        private int _acquired;
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            if (cancellationToken != default && cancellationToken != owner._business)
                throw new InvalidOperationException("Use SAME source-owned business token.");
            if (Interlocked.Exchange(ref _acquired, 1) != 0) throw new InvalidOperationException("Acquire SAME source iterator once.");
            return owner.Invoke(() =>
            {
                var original = stream.GetAsyncEnumerator(owner._business);
                var reader = new Reader(owner, original, body);
                lock (owner._gate)
                { owner._originalIterators.Add(original); owner._originalIterators.Add(reader); if (body) owner._originalBodyIterator = reader; }
                return reader;
            });
        }
        private sealed class Reader(ChatOrdinaryOriginalInvocation owner, IAsyncEnumerator<T> actual, bool body) : IAsyncEnumerator<T>
        {
            private bool _disposeAttempted;
            private Task? _rawDispose;
            private Task? _dispose;
            public T Current => owner.Invoke(() => actual.Current);
            public ValueTask<bool> MoveNextAsync()
            {
                var raw = owner.Capture(() => actual.MoveNextAsync().AsTask(), body ? Role.BodyMove : Role.ProviderMove, owner._business);
                // Return SAME raw body Task to its owning host. Provider faults are
                // enveloped before an outer builder can translate faulted OCE.
                return new(body ? raw : AwaitOpaque(raw));
            }
            public ValueTask DisposeAsync()
            {
                var acquire = false;
                lock (owner._gate)
                {
                    if (_rawDispose is null)
                    {
                        if (_disposeAttempted) throw new InvalidOperationException("Original iterator cleanup acquisition is unresolved; it cannot replay.");
                        _disposeAttempted = true; acquire = true;
                    }
                }
                if (acquire)
                    owner.Capture(() => actual.DisposeAsync().AsTask(), Role.Cleanup, default, raw =>
                    {
                        lock (owner._gate) { if (body) owner._actualBodyDispose = raw; _rawDispose = raw; }
                    });
                lock (owner._gate) return new(_dispose ??= AwaitOpaque(_rawDispose!));
            }
        }
    }
}

public sealed partial class ChatSessionService
{
    /// <summary>One original plain ordinary conversation, with actual raw write/provider/cleanup
    /// custody. This is not the canonical agentic tool producer or a permission grant.</summary>
    public ChatOrdinaryOriginalInvocation CreateOriginalOrdinaryConversation(
        Conversation sameConversation, string prompt, ModelDescriptor model, EffortLevel effort,
        string name, string instructions, GenerationOptions options,
        CancellationToken explicitBusinessToken, Action<Action> originalSynchronousScope)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(options);
        if (options.RequestedRoutingConstraints is null)
            throw new InvalidOperationException("This original source requires explicit constrained routing before provider discovery.");
        var original = new ChatOrdinaryOriginalInvocation(this, explicitBusinessToken, originalSynchronousScope);
        original.Bind(CreateOriginalSend(sameConversation, prompt, model with { Capabilities = model.Capabilities.ToFrozenSet() },
            effort, [], name, instructions, DuoMode.Solo, null, null, null, null, explicitBusinessToken,
            null, null, options, PermissionMode.Ask, PermissionMode.Ask, PermissionMode.Ask,
            [], [], null, null, TaskRunExecutionIntent.OrdinaryConversation, null, original));
        return original;
    }
    public bool IsIssuedOriginalOrdinaryConversation(ChatOrdinaryOriginalInvocation sameOriginal) =>
        sameOriginal is not null && ReferenceEquals(sameOriginal.Issuer, this);
}
