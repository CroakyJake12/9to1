using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

// Actual retained source/callback/close protocol controls. No scripted installed
// host, signer, process, endpoint or Compatible observation is qualified here.
public sealed class HomeNativeOriginalStartupScopeTests
{
    [Fact]
    public void Swallowed_repeated_callback_is_sticky_and_runs_actual_body_only_once()
    {
        var bodies = 0;
        var source = new HomeNativeOriginalStartupScope(new object(), () => null,
            callback => { callback(); try { callback(); } catch (InvalidOperationException) { } }, _ => { });
        var group = Assert.Throws<AggregateException>(() => source.Run(() => bodies++));
        Assert.Equal(1, bodies);
        Assert.Contains(Leaves(group), cause => cause is InvalidOperationException);
        Assert.NotEmpty(source.Errors);
    }
    [Fact]
    public async Task Accepted_raw_cancel_and_independent_foreign_postcallback_OCE_remain_faulted_with_both_occurrences()
    {
        using var controls = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var foreign = new OperationCanceledException("Independent caller postcallback occurrence.");
        var source = new HomeNativeOriginalStartupScope(new object(), () => null,
            callback => { callback(); throw foreign; }, actual => { if (ReferenceEquals(actual, raw.Task)) retained.TrySetResult(); });
        var driver = source.Read(() => raw.Task); Exception? body = null;
        try
        {
            var reached = await Task.WhenAny(retained.Task, driver).WaitAsync(controls.Token);
            if (ReferenceEquals(reached, driver)) await driver;
            await retained.Task;
            Assert.False(driver.IsCompleted);
            raw.SetCanceled(controls.Token);
            var group = await Assert.ThrowsAsync<AggregateException>(() => driver);
            Assert.True(driver.IsFaulted); Assert.False(driver.IsCanceled);
            Assert.Contains(Leaves(group), cause => ReferenceEquals(cause, foreign));
            Assert.Contains(Leaves(group), cause => cause is TaskCanceledException canceled && ReferenceEquals(canceled.Task, raw.Task));
        }
        catch (Exception cause) { body = cause; }
        finally
        {
            raw.TrySetCanceled(controls.Token);
            var errors = new List<Exception>(); if (body is not null) errors.Add(body);
            foreach (var same in new[] { driver, raw.Task })
                try { await same; }
                catch (Exception cause)
                {
                    var observed = same.Exception ?? cause;
                    if (!Leaves(observed).All(leaf => ReferenceEquals(leaf, foreign) ||
                        leaf is TaskCanceledException canceled && ReferenceEquals(canceled.Task, raw.Task))) errors.Add(observed);
                }
            await source.JoinAll();
            foreach (var cause in source.Errors)
                if (!Leaves(cause).All(leaf => ReferenceEquals(leaf, foreign) ||
                    leaf is TaskCanceledException canceled && ReferenceEquals(canceled.Task, raw.Task))) errors.Add(cause);
            if (errors.Count != 0) throw new AggregateException("Actual startup source control/unknown sibling failed.", errors);
        }
    }
    [Fact]
    public async Task Actual_failed_resource_close_is_cached_without_replay_and_restored_context_cannot_join_its_owner()
    {
        var sentinel = new IOException("Actual retained close occurrence.");
        var resource = new FailingResource(sentinel); var owner = new object(); var raw = new List<Task>();
        var source = new HomeNativeOriginalStartupScope(owner, () => null, body => body(), actual => raw.Add(actual));
        source.CaptureNative(resource);
        var prior = ExecutionContext.Capture()!;
        source.Run(() => ExecutionContext.Run(prior, _ => Assert.Throws<InvalidOperationException>(source.DemandExternalJoin), null));
        await source.CloseNativeResources(); await source.CloseNativeResources();
        Assert.Equal(1, resource.Closes); var same = Assert.Single(raw);
        var observed = await Assert.ThrowsAsync<IOException>(() => same); Assert.Same(sentinel, observed);
        Assert.Contains(Leaves(Assert.Throws<AggregateException>(source.Throw)), cause => ReferenceEquals(cause, sentinel));
        // Independently join the SAME raw close; only its exact observed cause is expected.
        try { await same; } catch (Exception cause) { Assert.Same(sentinel, cause); }
    }
    private sealed class FailingResource(Exception cause) : IDisposable
    { internal int Closes; public void Dispose() { Closes++; throw cause; } }
    private static IEnumerable<Exception> Leaves(Exception cause)
    {
        if (cause is AggregateException group && group.InnerExceptions.Count != 0)
            foreach (var direct in group.InnerExceptions) foreach (var leaf in Leaves(direct)) yield return leaf;
        else yield return cause;
    }
}
