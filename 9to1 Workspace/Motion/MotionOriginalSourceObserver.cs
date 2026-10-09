using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;

namespace HavenOS.Apps.Motion;

// Presentation custody only. This does not grant media, Files, or destination authority.
internal sealed class MotionOriginalSourceObserver(
    Action<Action> acquire, Action<Task> retain)
{
    internal async Task<T> AwaitAsync<T>(Func<Task<T>> factory)
    {
        Task<T>? source = null;
        acquire(() => { source = factory() ?? throw new InvalidOperationException("A Motion source returned no task."); retain(source); });
        return await AwaitActualAsync(source!).ConfigureAwait(false);
    }
    internal async Task AwaitAsync(Func<Task> factory)
    {
        Task? source = null;
        acquire(() => { source = factory() ?? throw new InvalidOperationException("A Motion source returned no task."); retain(source); });
        await AwaitActualAsync(source!).ConfigureAwait(false);
    }
    internal static async Task<T> AwaitActualAsync<T>(Task<T> source)
    {
        try { return await source.ConfigureAwait(false); }
        catch { ThrowActual(source); throw; }
    }
    internal static async Task AwaitActualAsync(Task source)
    {
        try { await source.ConfigureAwait(false); }
        catch { ThrowActual(source); throw; }
    }
    private static void ThrowActual(Task source)
    {
        // Only this actual CLR task container is unwrapped. Foreign aggregate causes remain opaque.
        if (source.Exception is not { } faults) return;
        if (faults.InnerExceptions.Count == 1) ExceptionDispatchInfo.Capture(faults.InnerExceptions[0]).Throw();
        MotionOriginalFailures.RegisterActualContainer(faults);
        ExceptionDispatchInfo.Capture(faults).Throw();
    }
}

internal static class MotionOriginalFailures
{
    private static readonly ConditionalWeakTable<AggregateException, object> OwnedContainers = new();
    internal static void RegisterActualContainer(AggregateException container) => OwnedContainers.GetValue(container, _ => new object());
    internal static void Add(List<Exception> failures, Exception cause)
    {
        if (cause is AggregateException container && OwnedContainers.TryGetValue(container, out _))
        { foreach (var actualCause in container.InnerExceptions) Add(failures, actualCause); return; }
        if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause);
    }
    internal static void AddTask(List<Exception> failures, Task source, Exception caught)
    {
        if (source.Exception is { } actual)
            foreach (var cause in actual.InnerExceptions) Add(failures, cause);
        else Add(failures, caught);
    }
    internal static void Throw(List<Exception> failures)
    {
        if (failures.Count == 1 && failures[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        // Unknown cancellation is retained failure custody. A faulted raw OCE must
        // not become an apparently intentional canceled close in an outer host.
        if (failures.Count > 0)
        { var container = new AggregateException("Motion original work or cleanup failed.", failures); RegisterActualContainer(container); throw container; }
    }
}
