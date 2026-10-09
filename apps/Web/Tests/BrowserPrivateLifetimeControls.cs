using HavenOS.Home.Core;
using NineToOne.Web;

// Exact registry lifetime controls. Owners are controlled custody participants, not account,
// provider, Home, Task permission or application workflow authority.
internal static class BrowserPrivateLifetimeControls
{
    public static async Task Main()
    {
        await NonRouteOwnerIsUniqueAndReentrancyCannotReplaceItsPendingCohort();
        await CancelledConsumerCannotDiscardTheActualPendingDrain();
        await TerminalClosePreservesLateAcknowledgementAfterCallerCancellation();
        await EveryRevocationAndCleanupCauseRemainsClosed();
        await SynchronousClearRefusesAnIssuedAsynchronousLifetime();
        await CancelledPreparationRetainsTheSamePrivateOwner();
        Console.WriteLine("6 non-route private lifetime controls passed.");
    }

    private static async Task NonRouteOwnerIsUniqueAndReentrancyCannotReplaceItsPendingCohort()
    {
        var registry = new BrowserSurfaceRegistry();
        var first = new Owner();
        var second = new Owner();
        var replacement = new Owner();
        HomeCoreOperationResult<bool>? reentrant = null;
        first.OnRevoke = () => reentrant = registry.RegisterPrivateLifetime(replacement);
        first.OnClose = () => Require(second.Revoked, "Cleanup started before the whole cohort was revoked.");
        Require(registry.RegisterPrivateLifetime(first).Succeeded && registry.RegisterPrivateLifetime(second).Succeeded,
            "Actual owner enrollment failed.");
        Require(!registry.RegisterPrivateLifetime(first).Succeeded && registry.AvailableRoutes.Count == 0,
            "Owner duplication or a fabricated route was admitted.");
        var reset = registry.BeginPrivateContextReset();
        Require(first.Revoked && second.Revoked && !first.CanRead && reentrant is { Succeeded: false },
            "Old state or replacement survived the synchronous private fence.");
        var drain = reset.DrainAsync();
        Require(ReferenceEquals(drain, reset.DrainAsync()), "The original drain Task was replaced.");
        await drain;
        Require(first.CloseCount == 1 && second.CloseCount == 1 && registry.AvailableRoutes.Count == 0,
            "The same non-route cohort was not drained exactly once.");
    }

    private static async Task CancelledConsumerCannotDiscardTheActualPendingDrain()
    {
        var registry = new BrowserSurfaceRegistry();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new Owner { HeldRaw = held.Task };
        Require(registry.RegisterPrivateLifetime(owner).Succeeded, "Enrollment failed.");
        var reset = registry.BeginPrivateContextReset();
        var actualDrain = reset.DrainAsync();
        using var consumerStop = new CancellationTokenSource();
        Exception? controlFailure = null;
        try
        {
            await owner.Started.Task;
            consumerStop.Cancel();
            Require(await Failure(actualDrain.WaitAsync(consumerStop.Token)) is OperationCanceledException,
                "The consumer did not cancel.");
            Require(!actualDrain.IsCompleted && !owner.Closed && !registry.RegisterPrivateLifetime(new Owner()).Succeeded,
                "A cancelled consumer discarded the held original or allowed replacement.");
        }
        catch (Exception error) { controlFailure = error; }
        finally { held.TrySetResult(); }
        await actualDrain;
        Require(owner.Closed && owner.CloseCount == 1 && ReferenceEquals(actualDrain, reset.DrainAsync()),
            "Actual held work was not joined by the same original drain.");
        if (controlFailure is not null) throw controlFailure;
    }

    private static async Task TerminalClosePreservesLateAcknowledgementAfterCallerCancellation()
    {
        var registry = new BrowserSurfaceRegistry();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Owner { HeldRaw = held.Task };
        var second = new Owner();
        first.OnClose = () => Require(second.Revoked, "Terminal cancellation began before every owner was fenced.");
        Require(registry.RegisterPrivateLifetime(first).Succeeded && registry.RegisterPrivateLifetime(second).Succeeded,
            "Enrollment failed.");
        using var caller = new CancellationTokenSource();
        var original = registry.ClearAsync(caller.Token);
        Exception? controlFailure = null;
        try
        {
            await first.Started.Task;
            caller.Cancel();
            Require(!original.IsCompleted && !registry.RegisterPrivateLifetime(new Owner()).Succeeded,
                "Late cancellation discarded the actual close or admitted a new owner.");
        }
        catch (Exception error) { controlFailure = error; }
        finally { held.TrySetResult(); }
        var receipt = await original;
        Require(receipt.Succeeded && receipt.Value == true && first.Closed && second.Closed &&
            first.CloseCount == 1 && second.CloseCount == 1, "Late acknowledgement was lost or actual cleanup was replayed.");
        if (controlFailure is not null) throw controlFailure;
    }

    private static async Task EveryRevocationAndCleanupCauseRemainsClosed()
    {
        var registry = new BrowserSurfaceRegistry();
        var revokeCause = new IOException("same revoke cause");
        var closeCause = new IOException("same close cause");
        var first = new Owner { RevokeCause = revokeCause, CloseCause = closeCause };
        var second = new Owner();
        Require(registry.RegisterPrivateLifetime(first).Succeeded && registry.RegisterPrivateLifetime(second).Succeeded,
            "Enrollment failed.");
        var failure = await Failure(registry.ClearAsync());
        Require(failure is AggregateException group && group.Flatten().InnerExceptions.Any(cause => ReferenceEquals(cause, revokeCause)) &&
            group.Flatten().InnerExceptions.Any(cause => ReferenceEquals(cause, closeCause)), "An original close/revoke cause was discarded.");
        Require(first.Revoked && second.Revoked && second.Closed && !registry.RegisterPrivateLifetime(new Owner()).Succeeded,
            "A failed cohort stopped unrelated cleanup or permitted a new private owner.");
        Require(await Failure(registry.ClearAsync()) is AggregateException, "The actual sticky failure was discarded by another close.");
    }

    private static async Task SynchronousClearRefusesAnIssuedAsynchronousLifetime()
    {
        var registry = new BrowserSurfaceRegistry();
        var owner = new Owner();
        Require(registry.RegisterPrivateLifetime(owner).Succeeded, "Enrollment failed.");
        Require(Throws(registry.Clear) is InvalidOperationException && Throws(registry.ClearPrivateContext) is InvalidOperationException &&
            !owner.Revoked && owner.CloseCount == 0 && owner.CanRead,
            "Synchronous clear detached an asynchronous owner without its actual drain.");
        await registry.BeginPrivateContextReset().DrainAsync();
        Require(owner.Closed, "The retained original did not drain.");
    }

    private static async Task CancelledPreparationRetainsTheSamePrivateOwner()
    {
        var registry = new BrowserSurfaceRegistry();
        using var stop = new CancellationTokenSource();
        var owner = new Owner { OnPrepare = () => stop.Cancel() };
        Require(registry.RegisterPrivateLifetime(owner).Succeeded, "Enrollment failed.");
        var failure = await Failure(registry.ClearAsync(stop.Token));
        Require(failure is OperationCanceledException && !owner.Revoked && owner.CanRead && owner.CloseCount == 0 &&
            !registry.RegisterPrivateLifetime(owner).Succeeded,
            "Cancelled preparation detached or replaced the still-issued owner.");
        await registry.BeginPrivateContextReset().DrainAsync();
        Require(owner.Closed, "The same retained owner did not drain after cancellation.");
    }

    private sealed class Owner : IBrowserPrivateContextParticipant, IBrowserCloseParticipant
    {
        private Task? _close;
        public bool Revoked { get; private set; }
        public bool Closed { get; private set; }
        public bool CanRead => !Revoked;
        public bool HasUnsavedChanges => false;
        public int CloseCount { get; private set; }
        public Task? HeldRaw { get; init; }
        public Action? OnRevoke { get; set; }
        public Action? OnClose { get; set; }
        public Action? OnPrepare { get; init; }
        public Exception? RevokeCause { get; init; }
        public Exception? CloseCause { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void RevokePrivateContext()
        {
            Revoked = true;
            OnRevoke?.Invoke();
            if (RevokeCause is not null) throw RevokeCause;
        }
        public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            OnPrepare?.Invoke();
            return Task.FromResult(new HomeCoreOperationResult<bool>(true, "Succeeded", "Controlled original prepared.", true));
        }
        public ValueTask DisposeAsync()
        {
            if (_close is not null) return new(_close);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = Drain(start.Task); // SAME actual close Task published before any callback.
            start.SetResult();
            return new(_close);
        }
        private async Task Drain(Task start)
        {
            await start;
            CloseCount++;
            Started.TrySetResult(); // Entered-driver witness also settles when the original fence/callback faults.
            Require(Revoked, "The exact owner was not fenced before its disposal.");
            OnClose?.Invoke();
            if (HeldRaw is not null) await HeldRaw;
            if (CloseCause is not null) throw CloseCause;
            Closed = true;
        }
    }
    private static Exception Throws(Action actual)
    {
        try { actual(); } catch (Exception error) { return error; }
        throw new InvalidOperationException("The synchronous operation unexpectedly succeeded.");
    }
    private static async Task<Exception> Failure(Task actual)
    {
        try { await actual; } catch (Exception error) { return error; }
        throw new InvalidOperationException("The original operation unexpectedly succeeded.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
