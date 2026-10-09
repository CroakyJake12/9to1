using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Tests;

/// <summary>Actual raw Task/product/cause boundary controls. These certify ownership helper
/// behavior only, independently from an actual Home reviewed setup entry or import acceptance.</summary>
public sealed class FilesOriginalProductCaptureTests
{
    [Fact]
    public async Task Post_scope_fault_still_retains_same_held_actual_stream_before_propagating_original_fault()
    {
        var token = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), "astra-dev-original-product-" + Guid.NewGuid().ToString("N"));
        FileStream? owned = null; FileStream? captured = null; Task<FileStream>? driver = null;
        var raw = new TaskCompletionSource<FileStream>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cause = new IOException("Actual finite acquisition scope failed after returning its original Task.");
        var actualTasks = new List<Task>(); var failOnce = true;
        try
        {
            await File.WriteAllTextAsync(path, "original", token); owned = File.OpenRead(path);
            var source = new FilesOriginalReadSourceScope(action =>
            {
                action(); if (failOnce) { failOnce = false; entered.TrySetResult(); throw cause; }
            }, actualTasks.Add);
            driver = source.ObserveProduct(() => raw.Task, actual => captured = actual);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            Assert.Contains(actualTasks, actual => ReferenceEquals(actual, raw.Task)); Assert.False(driver.IsCompleted);
            raw.TrySetResult(owned);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => driver);
            Assert.Same(owned, captured); Assert.True(driver.IsFaulted); Assert.Contains(Causes(error), value => ReferenceEquals(value, cause));
            Assert.True(Assert.IsType<FileStream>(captured).CanRead);
        }
        finally
        {
            if (owned is not null) raw.TrySetResult(owned); else raw.TrySetCanceled(token);
            if (driver is not null) try { await driver; } catch { }
            if (owned is not null) await owned.DisposeAsync();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Faulted_original_product_task_retains_cancellation_object_and_direct_sibling_without_a_product()
    {
        var first = new OperationCanceledException("Faulted original product, no actual cancellation."); var second = new IOException("Direct sibling product fault.");
        var raw = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously); raw.SetException([first, second]);
        var actualTasks = new List<Task>(); object? captured = null;
        var source = new FilesOriginalReadSourceScope(action => action(), actualTasks.Add);
        var driver = source.ObserveProduct(() => raw.Task, actual => captured = actual);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => driver);
        Assert.True(raw.Task.IsFaulted); Assert.True(driver.IsFaulted); Assert.Null(captured);
        Assert.Contains(actualTasks, actual => ReferenceEquals(actual, raw.Task));
        Assert.Contains(Causes(error), value => ReferenceEquals(value, first)); Assert.Contains(Causes(error), value => ReferenceEquals(value, second));
    }

    [Fact]
    public async Task Genuine_canceled_original_product_task_stays_canceled_and_publishes_no_product()
    {
        using var lifetime = new CancellationTokenSource(); lifetime.Cancel();
        var actual = Task.FromCanceled<object>(lifetime.Token); object? captured = null; var owned = new List<Task>();
        var source = new FilesOriginalReadSourceScope(action => action(), owned.Add);
        var driver = source.ObserveProduct(() => actual, value => captured = value);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver);
        Assert.True(actual.IsCanceled); Assert.True(driver.IsCanceled); Assert.Null(captured);
        Assert.Contains(owned, value => ReferenceEquals(value, actual));
    }
    private static IEnumerable<Exception> Causes(Exception value)
    {
        yield return value;
        if (value is AggregateException group) foreach (var direct in group.InnerExceptions) foreach (var cause in Causes(direct)) yield return cause;
        else if (value.InnerException is { } direct) foreach (var cause in Causes(direct)) yield return cause;
    }
}
