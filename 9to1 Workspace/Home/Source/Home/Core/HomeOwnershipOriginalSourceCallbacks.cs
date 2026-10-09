namespace HavenOS.Home.Core;

// Callback/task custody only. The actual borrower supplies its finite physical scope.
internal sealed class HomeOwnershipOriginalSourceCallbacks(Action<Action> caller, Action<Task> retain,
    Action<Exception>? retainUnexpectedCallback = null)
{
    private readonly List<Exception> _errors = [];
    private readonly List<Task> _originals = [];
    internal Exception[] Errors { get { lock (_errors) return _errors.ToArray(); } }
    internal Action<Exception>? UnexpectedCallbackSink => retainUnexpectedCallback;
    internal bool HasIndependentlyJoinedHealthySources
    {
        get
        {
            if (Errors.Length != 0) return false;
            Task[] children; lock (_originals) children = _originals.ToArray();
            foreach (var sameRaw in children)
            {
                if (!sameRaw.IsCompletedSuccessfully) return false;
                sameRaw.GetAwaiter().GetResult();
            }
            return Errors.Length == 0;
        }
    }
    internal void Run(Action body) => Invoke(() => { body(); return 0; });
    internal void Retain(Task raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        lock (_originals) if (!_originals.Any(value => ReferenceEquals(value, raw))) _originals.Add(raw);
        try { retain(raw); }
        catch (Exception cause)
        {
            if (retainUnexpectedCallback is not null) AddUnexpectedCallback(cause);
            throw;
        }
    }
    internal T Invoke<T>(Func<T> body)
    {
        if (retainUnexpectedCallback is not null) return InvokeWithUnexpectedCallbackSink(body);
        int active = 1, used = 0, thread = Environment.CurrentManagedThreadId; T result = default!;
        try
        {
            caller(() =>
            {
                if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread ||
                    Interlocked.Exchange(ref used, 1) != 0)
                {
                    var refusal = new InvalidOperationException("The original ownership callback is inactive, foreign-thread or consumed.");
                    Add(refusal); throw refusal;
                }
                try { result = body(); } catch (Exception cause) { Add(cause); throw; }
            });
            if (Volatile.Read(ref used) == 0) Add(new InvalidOperationException("The original ownership callback was not invoked."));
            Throw(); return result;
        }
        catch (Exception cause) { Add(cause); Throw(); throw; }
        finally { Interlocked.Exchange(ref active, 0); }
    }
    // The optional owning sink records unexpected caller/protocol occurrences,
    // not a body/raw business refusal. Existing null callers retain their protocol.
    private T InvokeWithUnexpectedCallbackSink<T>(Func<T> body)
    {
        int active = 1, used = 0, thread = Environment.CurrentManagedThreadId;
        T result = default!; Exception? bodyCause = null;
        try
        {
            try
            {
                caller(() =>
                {
                    if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread ||
                        Interlocked.Exchange(ref used, 1) != 0)
                    {
                        var refusal = new InvalidOperationException("The original ownership callback is inactive, foreign-thread or consumed.");
                        AddUnexpectedCallback(refusal); throw refusal;
                    }
                    try { result = body(); }
                    catch (Exception cause) { bodyCause = cause; Add(cause); throw; }
                });
            }
            catch (Exception cause)
            {
                Add(cause);
                if (!ContainsOnlyOriginalBodyOccurrence(cause, bodyCause)) AddUnexpectedCallback(cause);
            }
            if (Volatile.Read(ref used) == 0)
                AddUnexpectedCallback(new InvalidOperationException("The original ownership callback was not invoked."));
            Throw(); return result;
        }
        finally { Interlocked.Exchange(ref active, 0); }
    }
    private static bool ContainsOnlyOriginalBodyOccurrence(Exception actual, Exception? originalBody)
    {
        if (originalBody is null) return false;
        var pending = new Stack<Exception>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var enqueued = 1; pending.Push(actual);
        while (pending.TryPop(out var same))
        {
            if (ReferenceEquals(same, originalBody)) continue;
            if (!seen.Add(same)) continue;
            if (seen.Count > 4096 || same.GetType() != typeof(AggregateException) ||
                same is not AggregateException { InnerExceptions.Count: > 0 } group)
                return false;
            foreach (var child in group.InnerExceptions)
            {
                if (++enqueued > 4096) return false;
                pending.Push(child);
            }
        }
        return true;
    }
    private void AddUnexpectedCallback(Exception cause)
    {
        Add(cause);
        try { retainUnexpectedCallback!(cause); }
        catch (Exception sinkCause) { Add(sinkCause); }
    }
    internal async Task<T> ReadAsync<T>(Func<Task<T>> factory)
    {
        Task<T>? actual = null; T result = default!;
        try { _ = Invoke(() => { actual = factory() ?? throw new InvalidOperationException("No original ownership Task returned."); Retain(actual); return actual; }); }
        catch (Exception cause) { Add(cause); }
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); }
            catch (Exception cause)
            {
                if (actual.Exception is { } group) { Add(group); foreach (var direct in group.InnerExceptions) Add(direct); }
                else Add(cause);
            }
        // A nested source can retain a genuine child Task, then throw before returning
        // its outer ValueTask/Task. Join every explicitly captured child independently
        // before this helper discloses failure or a product; never abandon that child.
        Task[] originals; lock (_originals) originals = _originals.ToArray();
        foreach (var raw in originals)
        {
            if (ReferenceEquals(raw, actual)) continue;
            try { await raw.ConfigureAwait(false); }
            catch (Exception cause)
            {
                if (raw.Exception is { } group) { Add(group); foreach (var direct in group.InnerExceptions) Add(direct); }
                else Add(cause);
            }
        }
        Throw(); return actual is null ? throw new InvalidOperationException("No original ownership Task captured.") : result;
    }
    private void Add(Exception cause) { lock (_errors) if (!_errors.Any(value => ReferenceEquals(value, cause))) _errors.Add(cause); }
    private void Throw() { var errors = Errors; if (errors.Length != 0) throw new AggregateException("Actual scoped ownership callback/source failure.", errors); }
}
