// Shared UI thread for headless tests: with Skia registered, control trees
// are strictly thread-affine, and xUnit facts may arrive on different pool
// threads. ALL UI object creation/access across every test class in this
// assembly must funnel through TestUiThread.Run (bootstrap included).

using Avalonia;
using Avalonia.Headless;

namespace CakeOS.Apps.Boards.App.Tests;

internal static class TestUiThread
{
    private static readonly object Gate = new();
    private static readonly System.Collections.Concurrent.BlockingCollection<WorkItem> Queue = new();
    private static readonly ManualResetEventSlim Ready = new(false);
    private static Thread? _thread;

    private sealed record WorkItem(Func<object?> Work, TaskCompletionSource<object?> Completion);

    private static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_thread is null)
            {
                _thread = new Thread(RunLoop) { IsBackground = true, Name = "BoardsTestUi" };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
            }
        }
        // Wait outside the lock: no test thread may touch Avalonia before the
        // pump owns Dispatcher.UIThread, or first-touch would bind it elsewhere.
        if (!Ready.Wait(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("Test UI thread did not finish bootstrap.");
    }

    private static void RunLoop()
    {
        try
        {
            AppBuilder.Configure<Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .UseSkia()
                .SetupWithoutStarting();
        }
        catch (InvalidOperationException)
        {
            // Another suite already bootstrapped the headless platform in this
            // process; the shared pump thread still owns all UI work below.
        }
        if (Application.Current is not null)
        {
            try
            {
                Application.Current.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
            }
            catch (InvalidOperationException)
            {
                // Theme already registered by an earlier bootstrap.
            }
        }
        // Force UIThread ownership onto this thread before any test thread
        // proceeds: first touch binds Dispatcher.UIThread permanently.
        _ = Avalonia.Threading.Dispatcher.UIThread;
        Ready.Set();
        foreach (var item in Queue.GetConsumingEnumerable())
        {
            try
            {
                item.Completion.SetResult(item.Work());
            }
            catch (Exception error)
            {
                item.Completion.SetException(error);
            }
        }
    }
    public static T Run<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        EnsureStarted();
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(new WorkItem(() => work(), completion));
        return (T)completion.Task.GetAwaiter().GetResult()!;
    }

    public static void Run(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        EnsureStarted();
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Add(new WorkItem(() => { work(); return null; }, completion));
        completion.Task.GetAwaiter().GetResult();
    }
}
