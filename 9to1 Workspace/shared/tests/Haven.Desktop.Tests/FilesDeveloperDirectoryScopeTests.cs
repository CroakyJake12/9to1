namespace Haven.Desktop.Tests;

public sealed partial class FilesDeveloperDirectorySetupTests
{
    [Fact]
    public async Task Post_scope_fault_keeps_once_converted_native_ValueTask_enrolled_and_independently_joined()
    {
        await using var rig = await Rig.Create(); var retained = new List<Task>();
        var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scopeFault = new OperationCanceledException("Direct finite scope failed after returning its actual native Task.");
        var rawFault = new IOException("Actual native task fault still owned after scope failure.");
        var nativeStarted = false; Task? original = null;
        try
        {
            original = rig.Register(() => { nativeStarted = true; return new(raw.Task); }, retained.Add,
                callback => { callback(); if (nativeStarted) { entered.TrySetResult(); throw scopeFault; } });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Contains(retained, value => ReferenceEquals(value, raw.Task));
            Assert.False(original.IsCompleted); Assert.False(File.Exists(rig.Bindings));
            raw.TrySetException(rawFault);
            var observed = await Assert.ThrowsAnyAsync<Exception>(() => original);
            Assert.True(original.IsFaulted); Assert.False(original.IsCanceled);
            Assert.Contains(Causes(observed), value => ReferenceEquals(value, scopeFault));
            Assert.Contains(Causes(observed), value => ReferenceEquals(value, rawFault));
            Assert.False(File.Exists(rig.Bindings));
        }
        finally
        {
            raw.TrySetException(rawFault);
            if (original is not null) try { await original; } catch { }
        }
    }
    [Fact]
    public async Task Genuine_native_canceled_original_without_scope_fault_keeps_canceled_status()
    {
        await using var rig = await Rig.Create(); var retained = new List<Task>();
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        var raw = Task.FromCanceled<bool>(stopped.Token);
        var original = rig.Register(() => new(raw), retained.Add);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
        Assert.True(original.IsCanceled); Assert.False(original.IsFaulted);
        Assert.Contains(retained, value => ReferenceEquals(value, raw)); Assert.False(File.Exists(rig.Bindings));
    }
    private static IEnumerable<Exception> Causes(Exception actual)
    {
        yield return actual;
        if (actual is AggregateException group)
            foreach (var member in group.InnerExceptions) foreach (var original in Causes(member)) yield return original;
    }
}
