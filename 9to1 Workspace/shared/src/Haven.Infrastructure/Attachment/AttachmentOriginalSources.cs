using Haven.Application;

namespace Haven.Infrastructure;

// Finite callback/raw-task custody only. No file, profile, content or write grant.
internal sealed class AttachmentOriginalSources(object owner, Action<Action> caller, Action<Task> retain)
{
    private readonly object _gate = new();
    private readonly List<Task> _tasks = [];
    private readonly List<Exception> _errors = [];
    internal void Remember(Exception error)
    { lock (_gate) if (!_errors.Any(prior => ReferenceEquals(prior, error))) _errors.Add(error); }
    internal void Retain(Task actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        lock (_gate) if (!_tasks.Any(prior => ReferenceEquals(prior, actual))) _tasks.Add(actual);
        try { retain(actual); } catch (Exception error) { Remember(error); throw; }
    }
    internal void Run(Action body) => Invoke(() => { body(); return true; });
    internal T Invoke<T>(Func<T> body)
    {
        int live = 1, entered = 0, thread = Environment.CurrentManagedThreadId;
        T value = default!; var errors = new List<Exception>();
        void Capture(Exception error)
        { Remember(error); lock (errors) if (!errors.Any(prior => ReferenceEquals(prior, error))) errors.Add(error); }
        try
        {
            CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () =>
            {
                try
                {
                    caller(() =>
                    {
                        if (Volatile.Read(ref live) == 0 || thread != Environment.CurrentManagedThreadId ||
                            Interlocked.Exchange(ref entered, 1) != 0)
                        {
                            var error = new InvalidOperationException("The original attachment callback expired, repeated or moved threads.");
                            Capture(error); throw error;
                        }
                        try { value = body(); } catch (Exception error) { Capture(error); throw; }
                    });
                }
                catch (Exception error) { Capture(error); }
                if (entered == 0) Capture(new InvalidOperationException("The original attachment callback was not entered."));
                return true;
            });
        }
        finally { Volatile.Write(ref live, 0); }
        Exception[] observed; lock (errors) observed = errors.ToArray();
        if (observed.Length != 0) throw new AggregateException("Original attachment callback failures remain retained.", observed);
        return value;
    }
    internal async Task<T> Read<T>(Func<Task<T>> factory, Action<T>? capture = null)
    {
        Task<T>? actual = null; T result = default!; var failures = new List<Exception>();
        try { Run(() => { actual = factory() ?? throw new InvalidOperationException("The original attachment source returned no Task."); Retain(actual); }); }
        catch (Exception error) { failures.Add(error); }
        if (actual is not null)
        {
            try { result = await actual.ConfigureAwait(false); capture?.Invoke(result); }
            catch (Exception error) { var cause = actual.Exception ?? error; Remember(cause); failures.Add(cause); }
        }
        if (failures.Count != 0) throw new AggregateException("Original attachment acquisition and source failures remain retained.", failures);
        return result;
    }
    internal async Task Read(Func<Task> factory)
    {
        Task? actual = null; var failures = new List<Exception>();
        try { Run(() => { actual = factory() ?? throw new InvalidOperationException("The original attachment source returned no Task."); Retain(actual); }); }
        catch (Exception error) { failures.Add(error); }
        if (actual is not null)
            try { await actual.ConfigureAwait(false); }
            catch (Exception error) { var cause = actual.Exception ?? error; Remember(cause); failures.Add(cause); }
        if (failures.Count != 0) throw new AggregateException("Original attachment acquisition and source failures remain retained.", failures);
    }
    internal async Task Join()
    {
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Task[] pending; lock (_gate) pending = _tasks.Where(joined.Add).ToArray();
            if (pending.Length == 0) break;
            foreach (var actual in pending)
                try { await actual.ConfigureAwait(false); }
                catch (Exception error) { Remember(actual.Exception ?? error); }
        }
        Exception[] errors; lock (_gate) errors = _errors.ToArray();
        if (errors.Length != 0) throw new AggregateException("Original attachment sources did not settle healthy.", errors);
    }
}
