using System.Runtime.ExceptionServices;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeAgentOriginalWorkTests
{
    [Fact]
    public Task Closing_waits_for_the_same_original_finally_and_coalesces_reentrant_cancel() => WithWork(async work =>
    {
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? reentrant = null;
        var callback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var original = work.RunAsync(async token =>
        {
            using var registration = token.Register(() => { reentrant = work.CloseAsync(); callback.TrySetResult(); });
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cleanup.TrySetResult(); await release.Task.ConfigureAwait(false); }
            return true;
        }, bound.Token);
        Exception? expected = null; Exception? primary = null; List<Exception> errors = [];
        try
        {
            await entered.Task.WaitAsync(bound.Token);
            var close = work.CloseAsync();
            await callback.Task.WaitAsync(bound.Token); await cleanup.Task.WaitAsync(bound.Token);
            Assert.Same(close, reentrant); Assert.Same(close, work.CloseAsync());
            Assert.False(close.IsCompleted); Assert.False(original.IsCompleted);
            release.TrySetResult();
            await close.WaitAsync(bound.Token);
            expected = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
        }
        catch (Exception error) { primary = error; }
        finally
        {
            try { release.TrySetResult(); } catch (Exception error) { Add(errors, error); }
            try { bound.Cancel(); } catch (Exception error) { Add(errors, error); }
            try { await original.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); }
            catch (Exception error) when (ReferenceEquals(error, expected) || ReferenceEquals(error, primary)) { }
            catch (Exception error) { Add(errors, error); }
        }
        Throw(primary, errors);
    });

    [Fact]
    public Task Cancellation_callback_failure_does_not_skip_same_original_body_failure_or_drain() => WithWork(async work =>
    {
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var body = new IOException("Exact original synthetic body failure");
        var cancel = new InvalidOperationException("Exact original synthetic cancellation callback failure");
        var original = work.RunAsync<bool>(async token =>
        {
            using var registration = token.Register(() => throw cancel);
            entered.TrySetResult(); await release.Task.ConfigureAwait(false); throw body;
        }, bound.Token);
        Exception? primary = null; Exception? expected = null; List<Exception> errors = [];
        try
        {
            await entered.Task.WaitAsync(bound.Token);
            var close = work.CloseAsync(); Assert.False(close.IsCompleted); Assert.Same(close, work.CloseAsync());
            release.TrySetResult();
            expected = await Assert.ThrowsAsync<AggregateException>(() => close);
            var failures = Assert.IsType<AggregateException>(expected).Flatten().InnerExceptions;
            Assert.Contains(failures, failure => ReferenceEquals(failure, body));
            Assert.Contains(failures, failure => ReferenceEquals(failure, cancel));
            Assert.Same(body, await Assert.ThrowsAsync<IOException>(() => original));
        }
        catch (Exception error) { primary = error; }
        finally
        {
            try { release.TrySetResult(); } catch (Exception error) { Add(errors, error); }
            try { bound.Cancel(); } catch (Exception error) { Add(errors, error); }
            try { await original.WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); }
            catch (Exception error) when (ReferenceEquals(error, body)) { }
            catch (Exception error) { Add(errors, error); }
            try { await work.CloseAsync().WaitAsync(TimeSpan.FromSeconds(20), CancellationToken.None); }
            catch (Exception error) when (ReferenceEquals(error, expected)) { }
            catch (Exception error) { Add(errors, error); }
        }
        Throw(primary, errors);
    }, expectedCloseFailure: true);

    private static async Task WithWork(Func<HomeAgentOriginalWork, Task> action, bool expectedCloseFailure = false)
    {
        var work = new HomeAgentOriginalWork(); Exception? primary = null; List<Exception> errors = [];
        try { await action(work); } catch (Exception error) { primary = error; }
        try { await work.CloseAsync().ConfigureAwait(false); }
        catch (Exception error) when (expectedCloseFailure && primary is null && error is AggregateException) { }
        catch (Exception error) { Add(errors, error); }
        Throw(primary, errors);
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(original => ReferenceEquals(original, error))) errors.Add(error); }
    private static void Throw(Exception? primary, IReadOnlyList<Exception> errors)
    {
        var all = errors.Where(error => !ReferenceEquals(error, primary)).ToList();
        if (primary is not null) all.Insert(0, primary);
        if (all.Count == 1) ExceptionDispatchInfo.Capture(all[0]).Throw();
        if (all.Count > 1) throw new AggregateException("Original work test and cleanup failed.", all);
    }
}
