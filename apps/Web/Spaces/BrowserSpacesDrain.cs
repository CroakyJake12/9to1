namespace NineToOne.Web.Spaces;

/// <summary>Drains the actual owned operations even when an earlier close callback throws.</summary>
internal static class BrowserSpacesDrain
{
    public static Task CompleteAsync(IEnumerable<Func<Task>> sources)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = SettleAsync(sources, completion);
        return completion.Task;
    }

    private static async Task SettleAsync(IEnumerable<Func<Task>> sources, TaskCompletionSource completion)
    {
        try { await DrainAsync(sources); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private static async Task DrainAsync(IEnumerable<Func<Task>> sources)
    {
        var tasks = new List<Task>();
        var failures = new List<Exception>();
        var observed = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        void Retain(Exception error)
        {
            if (error is AggregateException { InnerExceptions.Count: > 0 } group)
            {
                foreach (var inner in group.InnerExceptions) Retain(inner);
            }
            else if (observed.Add(error)) failures.Add(error);
        }

        foreach (var source in sources)
        {
            try { tasks.Add(source()); }
            catch (Exception error) { Retain(error); }
        }
        try { await Task.WhenAll(tasks); }
        catch (Exception) { /* Inspect every original terminal Task below. */ }
        foreach (var task in tasks)
            if (task.Exception is { } error) Retain(error);
        // Only genuinely cancelled Tasks have no failure. A faulted OCE remains a fault.
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }
}
