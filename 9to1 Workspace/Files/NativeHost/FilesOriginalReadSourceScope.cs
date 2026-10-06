namespace HavenOS.Files.NativeHost;

/// <summary>Caller-owned lifetime/cause observation only. No registry, actor, grant, effect or
/// alternate executor. Every supported nested factory invokes the SAME parent's finite physical
/// scope and retains its SAME returned Task before await, including post-await invocations.</summary>
internal sealed class FilesOriginalReadSourceScope(Action<Action> synchronousScope, Action<Task> retainOriginalTask)
{
    internal T Invoke<T>(Func<T> source)
    {
        T value = default!; Exception? failure = null;
        try { synchronousScope(() => { try { value = source(); } catch (Exception error) { failure = error; } }); }
        catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        if (failure is OperationCanceledException original) throw new AggregateException("The synchronous original read source returned no canceled original Task.", original);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return value;
    }
    internal async Task<T> Observe<T>(Func<Task<T>> source)
    {
        Task<T>? actual = null; T value = default!; var causes = new List<Exception>();
        try { Invoke(() => { actual = source() ?? throw new InvalidOperationException("Original read source returned no Task."); retainOriginalTask(actual); return true; }); }
        catch (Exception error) { causes.Add(error); }
        if (actual is not null)
            try { value = await actual.ConfigureAwait(false); }
            catch (Exception error)
            {
                if (actual.IsCanceled && causes.Count == 0) throw;
                causes.AddRange(actual.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable());
            }
        if (causes.Count != 0) throw new AggregateException("Original read source/enrollment failed; all acquired actual Tasks were independently joined.", causes);
        return value;
    }
    internal async Task ObserveVoid(Func<Task> source)
    {
        Task? actual = null; var causes = new List<Exception>();
        try { Invoke(() => { actual = source() ?? throw new InvalidOperationException("Original read source returned no Task."); retainOriginalTask(actual); return true; }); }
        catch (Exception error) { causes.Add(error); }
        if (actual is not null)
            try { await actual.ConfigureAwait(false); }
            catch (Exception error)
            {
                if (actual.IsCanceled && causes.Count == 0) throw;
                causes.AddRange(actual.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable());
            }
        if (causes.Count != 0) throw new AggregateException("Original read source/enrollment failed; all acquired actual Tasks were independently joined.", causes);
    }
}
