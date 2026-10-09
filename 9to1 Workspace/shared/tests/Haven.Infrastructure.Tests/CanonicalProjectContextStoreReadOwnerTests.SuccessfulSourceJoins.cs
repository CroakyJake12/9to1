using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [LinuxOriginalStoreFact]
    public Task Deep_successful_SQL_sources_are_independently_joined_before_next_parent_admission() => Run(async rig =>
    {
        var forwarded = new HashSet<Task>(ReferenceEqualityComparer.Instance); var gate = new object();
        void Retain(Task actual) { rig.Retain(actual); lock (gate) forwarded.Add(actual); }
        var parent = new CanonicalSqliteOriginalSourceScope(rig.Store, rig.Scope, Retain);
        var lease = await rig.Keep(parent.Read(() => rig.Store.AcquireOriginalProtectedReadWithinSourceAsync(
            rig.Actor, false, parent.Run, parent.Retain, rig.Token)));
        for (var index = 0; index < 700; index++)
        {
            var command = lease.CreateOriginalCommand();
            lease.InvokeOriginalSource(() => command.CommandText = "SELECT 1;");
            Assert.Equal(1L, Convert.ToInt64(await rig.Keep(lease.ReadOriginalSourceAsync(() => command.ExecuteScalarAsync(rig.Token)))));
            await rig.Keep(lease.CloseOriginalResourceAsync(command));
        }
        lock (gate) Assert.True(forwarded.Count(actual => actual.IsCompletedSuccessfully) >= 1024);
        var entered = 0;
        await rig.Keep(parent.Read(() => { entered++; return Task.CompletedTask; }));
        Assert.Equal(1, entered);
        await rig.Keep(lease.CloseAndDrainAsync());
        await parent.JoinAllAsync();
        Assert.True(parent.IsHealthySettled);
        await rig.Keep(rig.Store.CloseAndDrainAsync());
        Assert.True(rig.Store.OriginalClose!.IsCompletedSuccessfully);
    });

    [LinuxOriginalStoreFact]
    public Task Successful_join_preflight_never_joins_pending_sources_or_forgets_unknown_raw_siblings() => Run(async rig =>
    {
        var parent = new CanonicalSqliteOriginalSourceScope(rig.Store, rig.Scope, rig.Retain);
        var held = Enumerable.Range(0, 1023).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var empty = new AggregateException("foreign empty source group remains opaque");
        var io = new IOException("independent actual raw IO remains owned");
        rig.Expect(empty); rig.Expect(io);
        foreach (var actual in held) parent.Retain(actual.Task);
        parent.Retain(raw.Task); raw.SetException([empty, io]);
        var entered = 0;
        try
        {
            var rejected = rig.Keep(parent.Read(() => { entered++; return Task.CompletedTask; }));
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => rejected.WaitAsync(TimeSpan.FromSeconds(2)));
            rig.Expect(refusal); Assert.Equal(0, entered);
            Assert.All(held, actual => Assert.False(actual.Task.IsCompleted));
            Assert.True(raw.Task.IsFaulted);
        }
        finally { foreach (var actual in held) actual.TrySetResult(); }
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => parent.JoinAllAsync());
        rig.Expect(failure); Assert.Contains(empty, References(failure)); Assert.Contains(io, References(failure));
        Assert.False(parent.IsHealthySettled);
    });

    [LinuxOriginalStoreFact]
    public Task Successful_raw_preflight_does_not_release_unclosed_resource_custody() => Run(async rig =>
    {
        var parent = new CanonicalSqliteOriginalSourceScope(rig.Store, rig.Scope, rig.Retain);
        var resources = new List<MemoryStream>();
        for (var index = 0; index < 256; index++)
        {
            MemoryStream? actual = null;
            parent.Run(() => { actual = new MemoryStream(); parent.CaptureOriginalResource(actual); });
            resources.Add(actual!);
        }
        for (var index = 0; index < 64; index++) parent.Retain(Task.FromResult(index));
        var entered = 0;
        try
        {
            var rejected = rig.Keep(parent.Read(() => { entered++; return Task.CompletedTask; }));
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => rejected);
            rig.Expect(refusal); Assert.Equal(0, entered);
            Assert.All(resources, actual => Assert.True(actual.CanWrite));
        }
        finally { await parent.CloseResourcesAsync(); }
        Assert.All(resources, actual => Assert.False(actual.CanWrite));
        await rig.Keep(parent.Read(() => { entered++; return Task.CompletedTask; }));
        Assert.Equal(1, entered); await parent.JoinAllAsync(); Assert.True(parent.IsHealthySettled);
    });
}
