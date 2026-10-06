using System.Runtime.ExceptionServices;

namespace Haven.Application;

/// <summary>Finite custody only: a post-factory caller fault cannot abandon the SAME
/// raw source. No return value, cancellation or cleanup result issues authority.</summary>
internal static class TaskRunColdSourceIo
{
    internal static async Task<T> Read<T>(TaskRunColdOriginalSourceScope sources, Func<Task<T>> factory)
    {
        Task<T>? actual = null;
        Exception? acquisition = null;
        Exception? terminal = null;
        T result = default!;
        try { sources.Invoke(() => actual = factory() ?? throw new InvalidOperationException("No actual cold source Task was returned.")); }
        catch (Exception cause) { acquisition = cause; }
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); }
            catch (Exception cause) { terminal = actual.IsFaulted && actual.Exception is { } group ? group : cause; }
        if (acquisition is not null)
            throw new AggregateException("The finite cold factory/caller failed after its actual source was independently joined.",
                terminal is null ? new[] { acquisition } : new[] { acquisition, terminal });
        if (terminal is not null) ExceptionDispatchInfo.Capture(terminal).Throw();
        return result;
    }
    internal static async Task Read(TaskRunColdOriginalSourceScope sources, Func<Task> factory)
    {
        Task? actual = null;
        Exception? acquisition = null;
        Exception? terminal = null;
        try { sources.Invoke(() => actual = factory() ?? throw new InvalidOperationException("No actual cold source Task was returned.")); }
        catch (Exception cause) { acquisition = cause; }
        if (actual is not null)
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { terminal = actual.IsFaulted && actual.Exception is { } group ? group : cause; }
        if (acquisition is not null)
            throw new AggregateException("The finite cold factory/caller failed after its actual source was independently joined.",
                terminal is null ? new[] { acquisition } : new[] { acquisition, terminal });
        if (terminal is not null) ExceptionDispatchInfo.Capture(terminal).Throw();
    }
}
