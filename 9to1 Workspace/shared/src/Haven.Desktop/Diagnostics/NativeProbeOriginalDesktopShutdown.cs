#if ASTRA_HOME_NATIVE_PROBE || ASTRA_FORMS_NATIVE_PROBE
namespace Haven.Desktop.Validation;

/// <summary>Observe the exact task awaited by the original App exit callback.
/// Completion is a lifecycle fact; it grants no authority and is never inferred
/// from a visible prompt, process disappearance or an unobserved async callback.</summary>
internal static class NativeProbeOriginalDesktopShutdown
{
    private static readonly object Gate = new();
    private static Task? originalTask;
    private static Exception? originalCaughtFailure;

    internal static Task? OriginalTask
    {
        get { lock (Gate) return originalTask; }
    }

    internal static void BindOriginalTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        lock (Gate)
        {
            if (originalTask is not null && !ReferenceEquals(originalTask, task))
                throw new InvalidOperationException("The original desktop exit task changed.");
            originalTask = task;
        }
    }

    internal static void RecordOriginalCaughtFailure(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        lock (Gate)
        {
            if (ReferenceEquals(originalCaughtFailure, error)) return;
            originalCaughtFailure = originalCaughtFailure is null ? error
                : new AggregateException(originalCaughtFailure, error);
        }
    }

    internal static void RequireSettledOriginalTask()
    {
        Task task;
        lock (Gate)
        {
            task = originalTask ?? throw new InvalidOperationException("The original App exit callback task was not observed.");
        }
        // The actual classic dispatcher has returned. Never synchronously wait
        // on an incomplete task whose UI continuation can no longer run.
        if (!task.IsCompleted)
            throw new InvalidOperationException("The original App exit task did not settle before its classic dispatcher returned; retain original drains.");
        Exception? taskFailure = null;
        try { task.GetAwaiter().GetResult(); }
        catch (Exception error) { taskFailure = error; }
        Exception? caughtFailure;
        lock (Gate) caughtFailure = originalCaughtFailure;
        if (taskFailure is not null && caughtFailure is not null && !ReferenceEquals(taskFailure, caughtFailure))
            throw new AggregateException("The original caught shutdown failure and original exit task both failed.", caughtFailure, taskFailure);
        if (taskFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(taskFailure).Throw();
        if (caughtFailure is not null)
            throw new InvalidOperationException("The original App exit callback caught a shutdown failure.", caughtFailure);
    }
}
#endif
