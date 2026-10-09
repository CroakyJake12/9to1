using Haven.Application;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [LinuxOriginalStoreFact]
    public Task Parent_original_source_can_join_independent_child_lease_without_joining_store() => Run(async rig =>
    {
        var parent = new CanonicalSqliteOriginalSourceScope(rig.Store, rig.Scope, rig.Retain);
        var lease = await rig.Keep(parent.Read(() => rig.Store.AcquireOriginalProtectedReadWithinSourceAsync(
            rig.Actor, false, parent.Run, parent.Retain, rig.Token)));
        var command = lease.CreateOriginalCommand();
        lease.InvokeOriginalSource(() => command.CommandText = "SELECT 1;");
        Assert.Equal(1L, Convert.ToInt64(await rig.Keep(lease.ReadOriginalSourceAsync(() => command.ExecuteScalarAsync(rig.Token)))));
        await rig.Keep(lease.CloseOriginalResourceAsync(command));

        Task? originalClose = null;
        parent.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() => rig.Store.DemandExternalOriginalRetirementJoin());
            originalClose = rig.Keep(lease.CloseAndDrainAsync());
        });
        await originalClose!;
        Assert.Same(originalClose, lease.OriginalClose);
        Assert.Same(originalClose, lease.CloseAndDrainAsync());
        await parent.JoinAllAsync();
        await rig.Keep(rig.Store.CloseAndDrainAsync());
        Assert.True(rig.Store.OriginalClose!.IsCompletedSuccessfully);
    });

    [LinuxOriginalStoreFact]
    public Task Own_lease_source_still_refuses_close_under_restored_execution_context() => Run(async rig =>
    {
        var outside = ExecutionContext.Capture()!;
        var lease = await rig.Keep(rig.Store.AcquireOriginalProtectedReadWithinSourceAsync(
            rig.Actor, false, rig.Scope, rig.Retain, rig.Token));
        var raw = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = rig.Keep(lease.ReadOriginalSourceAsync(() =>
        {
            ExecutionContext.Run(outside, _ =>
            {
                Assert.Throws<InvalidOperationException>(() => { _ = lease.CloseAndDrainAsync(); });
                Assert.Null(lease.OriginalClose);
            }, null);
            return raw.Task;
        }));
        Task? close = null;
        try
        {
            close = rig.Keep(lease.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
        }
        finally { raw.TrySetResult(7); }
        Assert.Equal(7, await actual);
        await close!;
        Assert.Same(close, lease.CloseAndDrainAsync());
        Assert.True(close!.IsCompletedSuccessfully);
    });

    [LinuxOriginalStoreFact]
    public Task Own_lease_retainer_guard_keeps_actual_raw_fault_siblings_and_cached_close() => Run(async rig =>
    {
        var outside = ExecutionContext.Capture()!;
        var raw = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        CanonicalSqliteOriginalStoreLease? lease = null;
        void Retain(Task original)
        {
            rig.Retain(original);
            if (!ReferenceEquals(original, raw.Task)) return;
            ExecutionContext.Run(outside, _ =>
            {
                Assert.Throws<InvalidOperationException>(() => { _ = lease!.CloseAndDrainAsync(); });
                Assert.Null(lease!.OriginalClose);
            }, null);
        }
        lease = await rig.Keep(rig.Store.AcquireOriginalProtectedReadWithinSourceAsync(
            rig.Actor, false, rig.Scope, Retain, rig.Token));
        var actual = rig.Keep(lease.ReadOriginalSourceAsync(() => raw.Task));
        var empty = new AggregateException("foreign empty raw fault");
        var io = new IOException("independent retained raw IO");
        Task? close = null;
        try
        {
            close = rig.Keep(lease.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
        }
        finally { raw.TrySetException([empty, io]); }
        var originalFailure = await Assert.ThrowsAnyAsync<Exception>(() => actual);
        rig.Expect(originalFailure); rig.Expect(empty); rig.Expect(io);
        Assert.Contains(empty, References(originalFailure));
        Assert.Contains(io, References(originalFailure));
        var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => close!);
        rig.Expect(closeFailure);
        Assert.Contains(empty, References(closeFailure));
        Assert.Contains(io, References(closeFailure));
        Assert.Same(close, lease.OriginalClose);
        Assert.Same(close, lease.CloseAndDrainAsync());
        Assert.True(raw.Task.IsFaulted);
    });
}
