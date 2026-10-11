using CakeOS.Cui;
using NineToOne.Web;

internal static class ShellActionLifetimeChecks
{
    internal static async Task Run()
    {
        foreach (var cancelView in new[] { true, false })
        {
            using var view = new CancellationTokenSource();
            using var caller = new CancellationTokenSource();
            var owner = new PlainOwner();
            var shell = new BrowserShellActions(owner, () => throw new InvalidOperationException("Unexpected navigation"));
            var pending = shell.DispatchWithLifetimeAsync("Save", "original", new(view.Token, caller.Token)).AsTask();
            Demand(!pending.IsCompleted && owner.Parameter as string == "original", "Original owner was not awaited");
            (cancelView ? view : caller).Cancel();
            Demand(owner.Token.IsCancellationRequested, "Owner lost a cancellation source");
            Demand(!pending.IsCompleted, "Cancellation substituted for original operation settlement");
            owner.Release.SetResult();
            await pending;
        }

        using var trackedView = new CancellationTokenSource();
        using var trackedCaller = new CancellationTokenSource();
        var tracked = new TrackedOwner();
        var lifetime = new CuiActionDispatchLifetime(trackedView.Token, trackedCaller.Token);
        var trackedShell = new BrowserShellActions(tracked, () => { });
        var trackedPending = trackedShell.DispatchWithLifetimeAsync("Save", null, lifetime).AsTask();
        Demand(tracked.Lifetime == lifetime && !trackedPending.IsCompleted, "Tracked lifetime or ownership changed");
        tracked.Release.SetResult();
        await trackedPending;

        var navigations = 0;
        var home = new BrowserShellActions(new PlainOwner(), () => navigations++);
        foreach (var cancelView in new[] { true, false })
        {
            using var view = new CancellationTokenSource();
            using var caller = new CancellationTokenSource();
            (cancelView ? view : caller).Cancel();
            try
            {
                await home.DispatchWithLifetimeAsync("ShellHome", null, new(view.Token, caller.Token));
                throw new InvalidOperationException("Canceled navigation succeeded");
            }
            catch (OperationCanceledException) { }
        }
        Demand(navigations == 0, "Retired view navigated Home");
        await home.DispatchWithLifetimeAsync("ShellHome", null, default);
        Demand(navigations == 1, "Live Home navigation failed");
    }

    private static void Demand(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class PlainOwner : ICuiActionDispatcher
    {
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token;
        internal object? Parameter;
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Parameter = parameter;
            return new(Release.Task);
        }
    }

    private sealed class TrackedOwner : ICuiLifetimeAwareActionDispatcher
    {
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CuiActionDispatchLifetime Lifetime;
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Tracked owner downgraded to plain dispatch");
        public ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime)
        {
            Lifetime = lifetime;
            return new(Release.Task);
        }
    }
}
