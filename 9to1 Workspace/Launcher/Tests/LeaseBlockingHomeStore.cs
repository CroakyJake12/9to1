using HavenOS.Home.Core;
using Haven.Application;

namespace NineToOne.Launcher.Tests;

/// <summary>Test scheduling seam around the real cross-process Home lease and actual durable writer.</summary>
internal sealed class LeaseBlockingHomeStore(IHomeCoreStateStore inner, string path) : IHomeCoreStateStore
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
    public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRecordRevision, CancellationToken ct = default)
        => HoldAsync(() => inner.WriteAsync(record, expectedRecordRevision, ct), ct);
    public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expectedRecordRevision,
        AuthenticatedResourceActor expectedActor, IHomeStateCommitActorGuard guard, CancellationToken ct = default)
        => HoldAsync(() => inner.WriteGuardedAsync(record, expectedRecordRevision, expectedActor, guard, ct), ct);
    private async Task<HomeStateWriteResult> HoldAsync(Func<Task<HomeStateWriteResult>> write, CancellationToken ct)
    {
        using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var pending = write();
        Assert.False(pending.IsCompleted);
        Entered.TrySetResult();
        await Release.Task.WaitAsync(TimeSpan.FromSeconds(4), ct);
        lease.Dispose();
        return await pending;
    }
}
