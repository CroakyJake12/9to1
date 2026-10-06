namespace HavenOS.Files;

/// <summary>Finite parent callback protocol/cause observation only. This creates no
/// permission, issuer or lifetime owner; the actual caller supplies its physical scope.</summary>
public static class FilesOriginalSourceCallbackScope
{
    public static void Invoke(Action callback, Action<Action> originalSynchronousScope)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        var originalThread = Environment.CurrentManagedThreadId;
        int phase = 1, used = 0;
        List<Exception> causes = [];
        void Record(Exception error)
        {
            lock (causes)
            {
                Add(causes, error);
            }
        }
        void Once()
        {
            Exception? refusal = Volatile.Read(ref phase) == 0
                ? new InvalidOperationException("The original Files parent callback phase ended.")
                : Environment.CurrentManagedThreadId != originalThread
                    ? new InvalidOperationException("The original Files parent callback changed thread.")
                    : Interlocked.CompareExchange(ref used, 1, 0) != 0
                        ? new InvalidOperationException("The original Files parent callback cannot run twice.") : null;
            if (refusal is not null) { Record(refusal); throw refusal; }
            try { callback(); } catch (Exception error) { Record(error); throw; }
        }
        try
        {
            try
            {
                originalSynchronousScope(Once);
                if (Volatile.Read(ref used) == 0)
                    Record(new InvalidOperationException("The original Files parent omitted its synchronous callback."));
            }
            catch (Exception error) { Record(error); }
        }
        finally { Volatile.Write(ref phase, 0); }
        Exception[] failures; lock (causes) failures = causes.ToArray();
        if (failures.Length != 0)
            throw new AggregateException("Original Files parent callback failed; captured Tasks remain owned.", failures);
    }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (error is AggregateException group)
        { foreach (var cause in group.InnerExceptions) Add(errors, cause); }
        else if (!errors.Any(cause => ReferenceEquals(cause, error))) errors.Add(error);
    }
}
