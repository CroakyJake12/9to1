using System.Threading.Channels;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Canonical;

/// <summary>Application-owned hosting of the SAME ordinary Chat producer. A presentation borrows
/// one observation and cannot cancel its business driver or dispose the shared Chat service.</summary>
public sealed class AssistantOriginalConversationHost(CancellationToken explicitBusinessLifetime = default)
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Task> _active = [];
    private readonly List<Task> _drivers = [];
    private readonly List<Invocation> _originalInvocations = [];
    private readonly AsyncLocal<Invocation?> _current = new();
    [ThreadStatic] private static AssistantOriginalConversationHost? _physicalSource;
    private sealed class Invocation
    {
        internal volatile bool Active = true;
        internal Task? Driver;
        internal ChatOrdinaryOriginalInvocation? Source;
        internal IAsyncEnumerator<ChatStreamEvent>? Iterator;
        internal Task? ActualDispose;
        internal Task? ActualSourceJoin;
        internal readonly List<Task> Originals = [];
    }
    private const int MaximumRetainedDrivers = 64;
    private bool _retired;
    private Task? _close;

    internal AssistantOriginalConversationObservation StartOriginal(AssistantConversationBinding binding,
        ChatSessionService sameChat, Conversation actualConversation, string prompt, ModelDescriptor model,
        EffortLevel effort, string name, string instructions, GenerationOptions generationOptions)
    {
        lock (_gate)
        {
            if (_retired) throw new ObjectDisposedException(nameof(AssistantOriginalConversationHost));
            for (var index = _drivers.Count - 1; index >= 0; index--)
                if (_drivers[index].IsCompletedSuccessfully) { _drivers[index].GetAwaiter().GetResult(); _drivers.RemoveAt(index); }
            for (var index = _originalInvocations.Count - 1; index >= 0; index--)
            {
                var original = _originalInvocations[index];
                if (original.Driver?.IsCompletedSuccessfully == true && original.ActualDispose?.IsCompletedSuccessfully == true &&
                    original.ActualSourceJoin?.IsCompletedSuccessfully == true)
                { original.Driver.GetAwaiter().GetResult(); original.ActualDispose.GetAwaiter().GetResult(); original.ActualSourceJoin.GetAwaiter().GetResult(); _originalInvocations.RemoveAt(index); }
            }
            foreach (var id in _active.Where(pair => pair.Value.IsCompletedSuccessfully).Select(pair => pair.Key).ToArray()) _active.Remove(id);
            if (_drivers.Count >= MaximumRetainedDrivers)
                throw new AssistantCommandRefusedException("Original Chat producers require settlement or inspection before further admission.");
            if (_active.TryGetValue(actualConversation.Id, out var previous) && !previous.IsCompleted)
                throw new AssistantCommandRefusedException("This conversation already has an actual Chat producer; observe or wait for it before another Send.");
            var observer = new Observation(binding);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var invocation = new Invocation();
            _originalInvocations.Add(invocation); // Before any start gate, source factory or callback.
            var actual = DriveAsync(start.Task, invocation, observer, sameChat, actualConversation, prompt, model, effort, name, instructions, generationOptions);
            invocation.Driver = actual;
            _active[actualConversation.Id] = actual;
            _drivers.Add(actual);
            start.SetResult();
            return observer.Lease;
        }
    }

    private async Task DriveAsync(Task start, Invocation invocation, Observation observer, ChatSessionService sameChat,
        Conversation conversation, string prompt, ModelDescriptor model, EffortLevel effort, string name, string instructions, GenerationOptions generationOptions)
    {
        await start.ConfigureAwait(false);
        _current.Value = invocation;
        var originals = invocation.Originals;
        var failures = new List<Exception>();
        IAsyncEnumerator<ChatStreamEvent>? iterator = null;
        ChatOrdinaryOriginalInvocation? originalSource = null;
        Task<bool>? failedMove = null;
        Exception? bodyFailure = null;
        var sourceJoinedSuccessfully = false;
        try
        {
            // SAME plain Chat producer privately retains the actual durable writes,
            // provider iterator, tracker and callback originals. It issues no tool grant.
            originalSource = InvokeOriginal(() =>
            {
                var source = sameChat.CreateOriginalOrdinaryConversation(conversation, prompt, model, effort, name, instructions,
                    generationOptions, explicitBusinessLifetime, callback => InvokeOriginal(() => { callback(); return true; }));
                invocation.Source = source;
                return source;
            });
            if (!sameChat.IsIssuedOriginalOrdinaryConversation(originalSource))
                throw new InvalidOperationException("The actual Chat source issuer changed.");
            iterator = InvokeOriginal(() =>
            {
                var actual = originalSource.ConsumeOriginal().GetAsyncEnumerator(explicitBusinessLifetime);
                invocation.Iterator = actual;
                return actual;
            });
            while (true)
            {
                var move = InvokeOriginal(() => iterator.MoveNextAsync().AsTask());
                failedMove = move;
                originals.Add(move);
                var hasNext = await move.ConfigureAwait(false);
                originals.Remove(move); // Independently joined healthy original.
                failedMove = null;
                if (!hasNext) break;
                InvokeOriginal(() => { var item = iterator.Current; observer.Publish(item); return true; });
            }
        }
        catch (Exception cause) { bodyFailure = cause; }
        finally
        {
            if (originalSource is not null)
            {
                if (iterator is null && originalSource.OriginalBodyIterator is IAsyncEnumerator<ChatStreamEvent> acquiredBody)
                { iterator = acquiredBody; invocation.Iterator = acquiredBody; }
                try
                {
                    foreach (var actualBodyMove in originalSource.ReadOriginalBodyMoves())
                        if (!originals.Any(prior => ReferenceEquals(prior, actualBodyMove))) originals.Add(actualBodyMove);
                }
                catch (Exception cause) { failures.Add(cause); }
            }
            // A scope may throw after returning a live body Move. Join that SAME raw
            // task before Dispose; final cause qualification occurs below after the
            // issuer's durable writer/cleanup ledger has independently joined.
            foreach (var original in originals.ToArray())
                try { await original.ConfigureAwait(false); } catch { /* Retained; observed again and qualified below. */ }
            if (iterator is not null)
            {
                try { var dispose = InvokeOriginal(() =>
                { var actual = iterator.DisposeAsync().AsTask(); invocation.ActualDispose = actual; originals.Add(actual); return actual; }); await dispose.ConfigureAwait(false); originals.Remove(dispose); }
                catch (Exception cause) { failures.Add(cause); }
            }
            if (originalSource is not null)
            {
                Task? sourceJoin = null;
                try
                {
                    sourceJoin = InvokeOriginal(() =>
                    { var actual = originalSource.JoinOriginalSourcesAsync(); invocation.ActualSourceJoin = actual; return actual; });
                    await sourceJoin.ConfigureAwait(false);
                    sourceJoinedSuccessfully = true;
                }
                catch (Exception cause)
                {
                    if (sourceJoin?.Exception is { } envelope) failures.AddRange(envelope.InnerExceptions);
                    else failures.Add(cause);
                }
            }
            var expectedBodyCancel = sourceJoinedSuccessfully && originalSource is not null && failedMove is not null &&
                bodyFailure is not null && originalSource.IsExpectedOriginalBodyCancellation(failedMove, bodyFailure);
            if (bodyFailure is not null && !expectedBodyCancel) failures.Add(bodyFailure);
            foreach (var original in originals)
                try { await original.ConfigureAwait(false); }
                catch (Exception cause)
                {
                    // Normalize only THIS SAME canceled body Task after its issuer joined
                    // every actual source and acknowledged all writes/cleanup. Faulted
                    // OCEs, foreign tokens, mixed siblings and canceled writers remain.
                    if (sourceJoinedSuccessfully && originalSource is not null &&
                        originalSource.IsExpectedOriginalBodyCancellation(original, cause)) continue;
                    if (original.Exception is { } aggregate) failures.AddRange(aggregate.InnerExceptions);
                    else failures.Add(cause);
                }
        }
        invocation.Active = false;
        _current.Value = null;
        if (failures.Count != 0)
        {
            var failure = new AggregateException("The original ordinary Chat producer did not settle cleanly.", failures.Distinct<Exception>(ReferenceEqualityComparer.Instance));
            observer.Complete(failure);
            throw failure;
        }
        observer.Complete(null);
    }

    // Actual unresolved source references for owner/peer inspection. This creates no
    // stream, authorization or replacement cleanup task.
    internal IReadOnlyList<(ChatOrdinaryOriginalInvocation Source, IAsyncEnumerator<ChatStreamEvent> Iterator, Task? Dispose, Task? SourceJoin)> ReadRetainedOriginalInvocations()
    {
        lock (_gate) return _originalInvocations.Where(row => row.Source is not null && row.Iterator is not null)
            .Select(row => (row.Source!, row.Iterator!, row.ActualDispose, row.ActualSourceJoin)).ToArray();
    }
    public void RequestRetirement() { lock (_gate) _retired = true; }
    private T InvokeOriginal<T>(Func<T> source)
    {
        var previous = _physicalSource;
        _physicalSource = this;
        try { return source(); }
        finally { _physicalSource = previous; }
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (ReferenceEquals(_physicalSource, this) || _current.Value is { Active: true })
            throw new InvalidOperationException("An original Chat source cannot join its own host driver.");
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_gate)
        {
            _retired = true;
            return _close ??= JoinOriginalsAsync(_drivers.ToArray());
        }
    }
    private static async Task JoinOriginalsAsync(Task[] originals)
    {
        var failures = new List<Exception>();
        foreach (var original in originals)
            try { await original.ConfigureAwait(false); }
            catch (Exception cause)
            {
                if (original.Exception is { } aggregate) failures.AddRange(aggregate.InnerExceptions);
                else failures.Add(cause);
            }
        if (failures.Count != 0) throw new AggregateException("Ordinary Chat host originals did not drain cleanly.", failures);
    }

    private sealed class Observation
    {
        private readonly object _gate = new();
        private readonly Channel<ChatStreamEvent> _events = Channel.CreateBounded<ChatStreamEvent>(new BoundedChannelOptions(512)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        private readonly TaskCompletionSource<AssistantConversationSendResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<(Task Actual, CancellationToken ObserverToken)> _reads = [];
        private readonly Guid _conversation;
        private bool _retired;
        private bool _enumerated;
        private Task? _close;
        private Exception? _failure;
        internal readonly AssistantOriginalConversationObservation Lease;
        internal Observation(AssistantConversationBinding binding)
        {
            _conversation = binding.Conversation.Id;
            Lease = new(binding, Events, WaitAsync, RequestRetirement, DemandExternalJoin, CloseAndDrainAsync);
        }
        internal void Publish(ChatStreamEvent item)
        {
            lock (_gate)
            {
                if (_retired) return;
                if (!_events.Writer.TryWrite(item))
                {
                    _failure = new InvalidOperationException("The presentation did not consume its bounded event stream; reload persisted conversation state.");
                    RetireCore();
                }
            }
        }
        internal void Complete(Exception? failure)
        {
            lock (_gate)
            {
                if (_retired) return;
                _failure = failure;
                _events.Writer.TryComplete(failure);
                if (failure is null) _result.TrySetResult(new(_conversation, false)); else _result.TrySetException(failure);
            }
        }
        private IAsyncEnumerable<ChatStreamEvent> Events(CancellationToken token)
        {
            lock (_gate)
            {
                if (_retired) throw new ObjectDisposedException("Assistant conversation observation");
                if (_enumerated) throw new InvalidOperationException("One observation owns one original event enumerator.");
                _enumerated = true;
                return new EventReader(this, token);
            }
        }
        private Task<AssistantConversationSendResult> WaitAsync(CancellationToken token)
        {
            lock (_gate)
            {
                DemandReadCapacity();
                var wait = _result.Task.WaitAsync(token);
                _reads.Add((wait, token));
                return wait;
            }
        }
        private void RequestRetirement() { lock (_gate) RetireCore(); }
        private void RetireCore()
        {
            _retired = true;
            _events.Writer.TryComplete(_failure);
            if (_failure is null) _result.TrySetResult(new(_conversation, true)); else _result.TrySetException(_failure);
        }
        private void DemandExternalJoin() { /* Event readers invoke no callbacks or source factories. */ }
        private Task CloseAndDrainAsync()
        {
            lock (_gate)
            {
                RetireCore();
                return _close ??= JoinObservationAsync(_reads.ToArray(), _failure);
            }
        }
        private void DemandReadCapacity()
        {
            for (var index = _reads.Count - 1; index >= 0; index--)
            {
                var original = _reads[index];
                if (!original.Actual.IsCompleted) continue;
                try { original.Actual.GetAwaiter().GetResult(); _reads.RemoveAt(index); }
                catch (OperationCanceledException) when (original.Actual.IsCanceled && original.ObserverToken.IsCancellationRequested)
                { _reads.RemoveAt(index); } // This exact observation wait, never a business/native original.
                catch { /* Unknown and mixed source faults remain retained. */ }
            }
            if (_reads.Count >= 64) throw new AssistantCommandRefusedException("Original observation waits require settlement or inspection.");
        }
        private static async Task JoinObservationAsync((Task Actual, CancellationToken ObserverToken)[] originals, Exception? failure)
        {
            var failures = failure is null ? new List<Exception>() : [failure];
            foreach (var original in originals)
                try { await original.Actual.ConfigureAwait(false); }
                catch (OperationCanceledException) when (original.Actual.IsCanceled && original.ObserverToken.IsCancellationRequested) { }
                catch (Exception cause)
                {
                    if (original.Actual.Exception is { } aggregate) failures.AddRange(aggregate.InnerExceptions);
                    else failures.Add(cause);
                }
            if (failures.Count != 0) throw new AggregateException("The Chat observation did not drain cleanly.", failures.Distinct<Exception>(ReferenceEqualityComparer.Instance));
        }
        private sealed class EventReader(Observation owner, CancellationToken token) : IAsyncEnumerable<ChatStreamEvent>, IAsyncEnumerator<ChatStreamEvent>
        {
            private bool _disposed;
            private bool _acquired;
            private Task<bool>? _move;
            public ChatStreamEvent Current { get; private set; } = null!;
            public IAsyncEnumerator<ChatStreamEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                if (cancellationToken != default && cancellationToken != token)
                    throw new InvalidOperationException("Use the observation token supplied to its original event factory.");
                lock (owner._gate)
                {
                    if (_acquired) throw new InvalidOperationException("The original event enumerator cannot be acquired twice.");
                    _acquired = true;
                }
                return this;
            }
            public ValueTask<bool> MoveNextAsync()
            {
                lock (owner._gate)
                {
                    if (_disposed || owner._retired) return ValueTask.FromResult(false);
                    if (_move is { IsCompleted: false }) throw new InvalidOperationException("Join the actual prior event MoveNext before another move.");
                    owner.DemandReadCapacity();
                    var actual = ReadAsync();
                    _move = actual;
                    owner._reads.Add((actual, token));
                    return new(actual);
                }
            }
            private async Task<bool> ReadAsync()
            {
                while (await owner._events.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                    if (owner._events.Reader.TryRead(out var item)) { Current = item; return true; }
                return false;
            }
            public ValueTask DisposeAsync()
            {
                lock (owner._gate) _disposed = true;
                return new(owner.CloseAndDrainAsync());
            }
        }
    }
}
