using System.Runtime.ExceptionServices;
namespace HavenOS.Home.Core;

// Callback/task custody only. The actual borrower supplies its finite physical scope.
internal sealed class HomeOwnershipOriginalSourceCallbacks(Action<Action> caller, Action<Task> retain)
{
    private readonly List<Exception> _errors = [];
    private readonly List<Task> _originals = [];
    internal Exception[] Errors { get { lock (_errors) return _errors.ToArray(); } }
    internal void Run(Action body) => Invoke(() => { body(); return 0; });
    internal void Retain(Task raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        lock (_originals) if (!_originals.Any(value => ReferenceEquals(value, raw))) _originals.Add(raw);
        retain(raw);
    }
    internal T Invoke<T>(Func<T> body)
    {
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
    internal async Task<T> ReadAsync<T>(Func<Task<T>> factory)
    {
        Task<T>? actual = null; T result = default!; Exception? invocation = null, observed = null;
        try { _ = Invoke(() => { actual = factory() ?? throw new InvalidOperationException("No original ownership Task returned."); Retain(actual); return actual; }); }
        catch (Exception cause) { invocation = cause; Add(cause); }
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); }
            catch (Exception cause)
            {
                observed = cause;
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
        if (invocation is null && actual?.IsCanceled == true && observed is not null && Errors.All(value => value is OperationCanceledException))
            ExceptionDispatchInfo.Capture(observed).Throw();
        Throw(); return actual is null ? throw new InvalidOperationException("No original ownership Task captured.") : result;
    }
    private void Add(Exception cause) { lock (_errors) if (!_errors.Any(value => ReferenceEquals(value, cause))) _errors.Add(cause); }
    private void Throw() { var errors = Errors; if (errors.Length != 0) throw new AggregateException("Actual scoped ownership callback/source failure.", errors); }
}
