using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed class StudioOriginalTaskDrainTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Rethrow(Exception? primary, List<Exception> cleanup)
    {
        List<Exception> failures = [];
        if (primary is not null) StudioOriginalTaskDrain.Add(failures, primary);
        foreach (var error in cleanup) StudioOriginalTaskDrain.Add(failures, error);
        StudioOriginalTaskDrain.Throw(failures);
    }

    [Fact]
    public async Task Same_operation_is_published_before_cancellation_reentry_and_retirement_waits_for_original_child()
    {
        var released = Signal(); var cancelEntered = Signal(); bool admissionClosed = false, retired = false;
        StudioOriginalTaskDrain? drain = null; Task? reentered = null; Task? original = null;
        Exception? primary = null; List<Exception> cleanup = [];
        try
        {
            drain = new(() => admissionClosed = true, [() => released.Task],
                () => { Assert.True(admissionClosed); reentered = drain!.CloseAndDrainAsync(); cancelEntered.TrySetResult(); },
                [() => { Assert.True(released.Task.IsCompleted); retired = true; return Task.CompletedTask; }]);
            original = drain.CloseAndDrainAsync();
            Assert.Same(original, drain.CloseAndDrainAsync());
            await cancelEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(original, reentered); Assert.False(original.IsCompleted); Assert.False(retired);
            released.TrySetResult(); await original;
            Assert.True(retired); Assert.True(drain.OriginalTasksCapturedAndSettled);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            released.TrySetResult();
            if (original is not null) try { await original; } catch (Exception error) { cleanup.Add(error); }
        }
        Rethrow(primary, cleanup);
    }

    [Fact]
    public async Task Throwing_owned_cancel_still_awaits_the_same_pending_task_and_preserves_all_cleanup_failures()
    {
        var released = Signal(); var cancelEntered = Signal(); var cancel = new InvalidOperationException("cancel");
        var host = new InvalidOperationException("host"); var provider = new InvalidOperationException("provider");
        Task? original = null; Exception? observed = null; Exception? primary = null; List<Exception> cleanup = [];
        var calls = new List<string>();
        try
        {
            var drain = new StudioOriginalTaskDrain(() => { }, [() => released.Task],
                () => { calls.Add("cancel"); cancelEntered.TrySetResult(); throw cancel; },
                [() => { calls.Add("host"); throw host; }, () => { calls.Add("provider"); throw provider; }]);
            original = drain.CloseAndDrainAsync(); await cancelEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { "cancel" }, calls); Assert.False(original.IsCompleted);
            released.TrySetResult(); observed = await Record.ExceptionAsync(() => original);
            var failures = Assert.IsType<AggregateException>(observed).InnerExceptions;
            Assert.Collection(failures, error => Assert.Same(cancel, error), error => Assert.Same(host, error), error => Assert.Same(provider, error));
            Assert.Equal(new[] { "cancel", "host", "provider" }, calls);
            Assert.True(drain.OriginalTasksCapturedAndSettled);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            released.TrySetResult();
            if (original is not null)
                try { await original; } catch (Exception error) { if (!ReferenceEquals(error, observed)) cleanup.Add(error); }
        }
        Rethrow(primary, cleanup);
    }

    [Fact]
    public async Task Capture_refusal_is_retained_and_never_claims_original_task_settlement()
    {
        var refusal = new InvalidOperationException("capture"); bool cancel = false, retire = false;
        var drain = new StudioOriginalTaskDrain(() => { }, [() => throw refusal],
            () => cancel = true, [() => { retire = true; return Task.CompletedTask; }]);
        var original = drain.CloseAndDrainAsync();
        Assert.Same(refusal, await Record.ExceptionAsync(() => original));
        Assert.True(cancel); Assert.True(retire); Assert.False(drain.OriginalTasksCapturedAndSettled);
    }

    [Fact]
    public async Task Every_original_retirement_starts_before_waiting_and_distinct_same_object_failures_are_retained_once()
    {
        var released = Signal(); var failure = new InvalidOperationException("original");
        var calls = new List<string>(); Task? original = null; Exception? observed = null; Exception? primary = null; List<Exception> cleanup = [];
        try
        {
            original = StudioOriginalTaskDrain.RunIndependentAsync([
                () => { calls.Add("window"); return released.Task; },
                () => { calls.Add("host"); throw failure; },
                () => { calls.Add("provider"); throw failure; }]);
            Assert.Equal(new[] { "window", "host", "provider" }, calls);
            Assert.False(original.IsCompleted);
            released.TrySetResult(); observed = await Record.ExceptionAsync(() => original);
            Assert.Same(failure, observed);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            released.TrySetResult();
            if (original is not null)
                try { await original; } catch (Exception error) { if (!ReferenceEquals(error, observed)) cleanup.Add(error); }
        }
        Rethrow(primary, cleanup);
    }

    [Fact]
    public async Task Replacing_validation_context_cancels_its_original_gate_wait_and_a_new_context_remains_usable()
    {
        using var parent = new CancellationTokenSource(); var entered = Signal();
        var old = new StudioOriginalValidationWork(parent.Token);
        var original = old.RunAsync(async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); });
        Task? close = null; Exception? primary = null; List<Exception> cleanup = [];
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            close = old.CloseAndDrainAsync(); Assert.Same(close, old.CloseAndDrainAsync()); await close;
            Assert.True(original.IsCompleted); Assert.Throws<ObjectDisposedException>(() => { _ = old.RunAsync(_ => Task.CompletedTask); });
            var current = new StudioOriginalValidationWork(parent.Token); bool ran = false;
            Task? currentOriginal = null; Exception? currentPrimary = null; List<Exception> currentCleanup = [];
            try
            {
                currentOriginal = current.RunAsync(_ => { ran = true; return Task.CompletedTask; });
                await currentOriginal; Assert.True(ran);
            }
            catch (Exception error) { currentPrimary = error; }
            try { await current.CloseAndDrainAsync(); }
            catch (Exception error) { StudioOriginalTaskDrain.Add(currentCleanup, error); }
            Rethrow(currentPrimary, currentCleanup);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            close ??= old.CloseAndDrainAsync();
            try { await close; } catch (Exception error) { cleanup.Add(error); }
        }
        Rethrow(primary, cleanup);
    }

    [Fact]
    public async Task Earlier_caller_cancellation_is_not_reclassified_as_owned_shutdown_cancellation()
    {
        using var parent = new CancellationTokenSource(); using var caller = new CancellationTokenSource();
        var entered = Signal(); var work = new StudioOriginalValidationWork(parent.Token);
        var original = work.RunAsync(async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }, caller.Token);
        Exception? observed = null; Exception? primary = null; List<Exception> cleanup = []; Task? close = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); caller.Cancel();
            observed = await Record.ExceptionAsync(() => original); Assert.IsAssignableFrom<OperationCanceledException>(observed);
            close = work.CloseAndDrainAsync(); Assert.Same(observed, await Record.ExceptionAsync(() => close));
        }
        catch (Exception error) { primary = error; }
        finally
        {
            close ??= work.CloseAndDrainAsync();
            try { await close; } catch (Exception error) { if (!ReferenceEquals(error, observed)) cleanup.Add(error); }
        }
        Rethrow(primary, cleanup);
    }

    [Fact]
    public async Task Earlier_caller_cancellation_remains_the_same_refusal_when_owned_close_overlaps_its_original_finally()
    {
        using var parent = new CancellationTokenSource(); using var caller = new CancellationTokenSource();
        var callerEntered = Signal(); var finallyHeld = Signal(); var release = Signal();
        var ownedEntered = Signal(); var ownedCancelled = Signal();
        var work = new StudioOriginalValidationWork(parent.Token);
        OperationCanceledException? earlierRefusal = null; CancellationToken ownedToken = default;
        Task? original = null, ownedOriginal = null, close = null;
        Exception? observedOriginal = null, observedOwned = null, observedClose = null, primary = null;
        List<Exception> cleanup = [];
        try
        {
            original = work.RunAsync(async token =>
            {
                callerEntered.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException error) { earlierRefusal = error; throw; }
                finally { finallyHeld.TrySetResult(); await release.Task; }
            }, caller.Token);
            // This SAME real operation has no caller token; its cancellation callback witnesses owned close.
            ownedOriginal = work.RunAsync(async token =>
            {
                ownedToken = token;
                using var registration = token.Register(() => ownedCancelled.TrySetResult());
                ownedEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, token);
            });
            await callerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ownedEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            caller.Cancel(); await finallyHeld.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(earlierRefusal); Assert.False(original.IsCompleted);
            close = work.CloseAndDrainAsync(); Assert.Same(close, work.CloseAndDrainAsync());
            await ownedCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(original.IsCompleted); Assert.False(close.IsCompleted);
            release.TrySetResult();
            observedOriginal = await Record.ExceptionAsync(() => original);
            observedOwned = await Record.ExceptionAsync(() => ownedOriginal);
            observedClose = await Record.ExceptionAsync(() => close);
            Assert.Same(earlierRefusal, observedOriginal); Assert.Same(earlierRefusal, observedClose);
            Assert.Equal(ownedToken, Assert.IsAssignableFrom<OperationCanceledException>(observedOwned).CancellationToken);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            try { release.TrySetResult(); } catch (Exception error) { StudioOriginalTaskDrain.Add(cleanup, error); }
            try { close ??= work.CloseAndDrainAsync(); }
            catch (Exception error) { StudioOriginalTaskDrain.Add(cleanup, error); }
            if (original is not null)
                try { await original; } catch (Exception error)
                { if (!ReferenceEquals(error, observedOriginal)) StudioOriginalTaskDrain.Add(cleanup, error); }
            if (ownedOriginal is not null)
                try { await ownedOriginal; } catch (Exception error)
                { if (!ReferenceEquals(error, observedOwned)) StudioOriginalTaskDrain.Add(cleanup, error); }
            if (close is not null)
                try { await close; } catch (Exception error)
                { if (!ReferenceEquals(error, observedClose)) StudioOriginalTaskDrain.Add(cleanup, error); }
        }
        Rethrow(primary, cleanup);
    }
}
