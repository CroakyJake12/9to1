using System.Runtime.ExceptionServices;
namespace Haven.Application;

/// <summary>Finite source callback/cause custody only, no authority. The actual borrowing
/// owner supplies its physical callback scope and exact raw task enrollment.</summary>
internal sealed class TaskRunOriginalResponseSourceCallbacks(Action<Action> caller, Action<Task> retain)
{
    internal readonly List<Task> Originals = [];
    private readonly List<Exception> _errors = [];
    internal Exception[] Errors { get { lock (_errors) return _errors.ToArray(); } }
    internal void Run(Action callback) => Invoke(() => { callback(); return 0; });
    internal T Invoke<T>(Func<T> factory)
    {
        var thread = Environment.CurrentManagedThreadId; var active = 1; var used = 0;
        T result = default!;
        try
        {
            caller(() =>
            {
                if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                {
                    var refusal = new InvalidOperationException("Original response callback is inactive, foreign-thread or consumed.");
                    Add(refusal); throw refusal;
                }
                try { result = factory(); } catch (Exception error) { Add(error); throw; }
            });
            if (Volatile.Read(ref used) == 0) throw new InvalidOperationException("Original response callback was not invoked.");
            Throw(); return result;
        }
        catch (Exception error)
        {
            Add(error); Throw(); throw;
        }
        finally { Interlocked.Exchange(ref active, 0); }
    }
    internal async Task<T> ReadAsync<T>(Func<Task<T>> factory)
    {
        Task<T>? actual = null; T result = default!; Exception? invocation = null, observed = null;
        try { _ = Invoke(() => { actual = factory() ?? throw new InvalidOperationException("No actual response source Task returned.");
                if (!Originals.Any(value => ReferenceEquals(value, actual))) Originals.Add(actual); retain(actual); return actual; }); }
        catch (Exception error) { invocation = error; Add(error); }
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); }
            catch (Exception error) { observed = error; AddTask(actual, error); }
        if (invocation is null && actual?.IsCanceled == true && observed is not null && Errors.All(value => value is OperationCanceledException))
            ExceptionDispatchInfo.Capture(observed).Throw();
        Throw(); return actual is null ? throw new InvalidOperationException("No actual response raw Task captured.") : result;
    }
    internal void Add(Exception error) { lock (_errors) if (!_errors.Any(value => ReferenceEquals(value, error))) _errors.Add(error); }
    internal void AddTask(Task raw, Exception observed)
    { if (raw.Exception is { } group) { Add(group); foreach (var direct in group.InnerExceptions) Add(direct); } else Add(observed); }
    internal void Throw() { var errors = Errors; if (errors.Length != 0) throw new AggregateException("Actual response source callback/raw fault.", errors); }
}
