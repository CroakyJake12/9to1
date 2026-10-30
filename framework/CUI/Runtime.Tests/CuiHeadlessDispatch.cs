using Avalonia.Headless;

namespace CakeOS.Cui.Runtime.Tests;

internal static class CuiHeadlessDispatch
{
    // HeadlessUnitTestSession.Dispatch has no Func<Task> overload. An untyped async lambda
    // selects Func<TResult> with TResult=Task and awaiting once only joins the outer task.
    // Keep the headless dispatcher and application alive until assertions AND cleanup finish.
    public static Task RunAsync(HeadlessUnitTestSession session, Func<Task> action, CancellationToken token) =>
        session.Dispatch<bool>(async () => { await action(); return true; }, token);
}
