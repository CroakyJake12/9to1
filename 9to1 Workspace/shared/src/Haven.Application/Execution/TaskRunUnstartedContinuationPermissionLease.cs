using Haven.Core;

namespace Haven.Application;

/// <summary>Fresh currentness and lifetime of the SAME private source-approved Ask only.
/// No provider, context-egress, credential, tool or domain grant is supplied by this lease.
/// Reads/revalidation finish before the pure lifetime pin; only finite Task CAS occurs beneath it.</summary>
public interface ITaskRunUnstartedContinuationPermissionLease : IAsyncDisposable
{
    ValueTask RevalidateOriginalAsync(TaskExecutionSnapshot current, CancellationToken cancellationToken);
    ValueTask<IAsyncDisposable> AcquireOriginalCommitPinAsync(CancellationToken cancellationToken);
}
