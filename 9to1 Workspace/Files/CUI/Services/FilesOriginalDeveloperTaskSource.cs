namespace HavenOS.Files;

/// <summary>Finite caller-owned source/cause custody only. No owner registry, authority,
/// scheduling or retry; every actual returned Task is enrolled before scope exit and joined.</summary>
internal static class FilesOriginalDeveloperTaskSource
{
    internal static async Task<T> ObserveAsync<T>(Func<Task<T>> source, Action<Action> originalScope,
        Action<Task> retainOriginalTask)
    {
        Task<T>? actual = null; T result = default!; var errors = new List<Exception>();
        try
        {
            Invoke(() =>
            {
                actual = source() ?? throw new InvalidOperationException("Original developer source returned no Task.");
                retainOriginalTask(actual);
            }, originalScope);
        }
        catch (Exception error) { Add(errors, error); }
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); }
            catch (Exception error)
            {
                if (actual.IsCanceled && errors.Count == 0) throw;
                foreach (var cause in actual.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()) Add(errors, cause);
            }
        if (errors.Count == 1 && errors[0] is not OperationCanceledException)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual developer source/enrollment failed; acquired raw Tasks were independently joined.", errors);
        return result;
    }
    internal static void Invoke(Action callback, Action<Action> originalScope)
    {
        ArgumentNullException.ThrowIfNull(callback); ArgumentNullException.ThrowIfNull(originalScope);
        var thread = Environment.CurrentManagedThreadId; int phase = 1, used = 0;
        try
        {
            originalScope(() =>
            {
                if (Volatile.Read(ref phase) == 0 || Environment.CurrentManagedThreadId != thread ||
                    Interlocked.Exchange(ref used, 1) != 0)
                    throw new InvalidOperationException("Original Files factory scope is finite, same-thread and single-use.");
                callback();
            });
            if (Volatile.Read(ref used) == 0)
                throw new InvalidOperationException("Original Files factory was not invoked synchronously.");
        }
        catch (OperationCanceledException original)
        { throw new AggregateException("Direct Files callback failed without a canceled returned Task.", original); }
        finally { Volatile.Write(ref phase, 0); }
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error); }
}
