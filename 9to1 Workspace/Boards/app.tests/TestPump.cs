using Avalonia.Threading;

namespace CakeOS.Apps.Boards.App.Tests;

/// <summary>
/// Runs an async Boards operation to completion on the shared test UI thread. The presentation layer only
/// finishes mounting while dispatcher jobs are being pumped, so a plain await from a pool thread either
/// stalls or touches UI objects off-thread.
/// </summary>
internal static class TestPump
{
    private const int TimeoutSeconds = 60;

    public static T Pump<T>(Task<T> task, string operation) => TestUiThread.Run(() =>
    {
        PumpUntil(task, operation);
        return task.GetAwaiter().GetResult();
    });

    public static void Pump(Task task, string operation) => TestUiThread.Run(() => PumpUntil(task, operation));

    private static void PumpUntil(Task task, string operation)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(TimeoutSeconds);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
        if (!task.IsCompleted) throw new TimeoutException($"Boards surface timed out during {operation}.");
    }
}
