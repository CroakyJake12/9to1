using System.Runtime.CompilerServices;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class CanonicalProjectTaskContextCreateOwner
{
    private readonly ConditionalWeakTable<Task, CommitAttempt> _innerCommitSources = new();
    private readonly ConditionalWeakTable<Task, Commit> _writeAcquisitions = new();
    private sealed class DispatchWait
    {
        internal Task Driver = null!;
        internal Task? Inner;
        internal CanonicalSqliteOriginalSourceScope Source = null!;
    }
    public Task WaitOriginalSettlementDispatchWithinSourceAsync(ICanonicalProjectTaskContextCreationIntent sameIntent,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var intent = RequireIntent(sameIntent);
        Commit invocation; DispatchWait wait; TaskCompletionSource begin;
        lock (_gate)
        {
            if (!_commits.TryGetValue(intent, out var commit) || !ReferenceEquals(commit.Invocation.Intent, intent))
                throw new UnauthorizedAccessException("No SAME privately retained original commit acquisition cohort exists.");
            invocation = commit.Invocation;
            if (invocation.OriginalDispatchWait is { } existing) return existing.Driver;
            var original = ReserveOriginalOwnedCleanup(commit);
            wait = new() { Source = new(_store.OriginalSourceOwner, WrapOriginalScope(scope), retain) };
            invocation.OriginalDispatchWait = wait;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            wait.Driver = Drive(begin.Task, wait, invocation);
            PublishOriginal(original, wait.Driver);
        }
        begin.SetResult(); return wait.Driver;
        async Task Drive(Task start, DispatchWait retained, Commit actual)
        {
            await start.ConfigureAwait(false);
            var previous = _logical.Value; _logical.Value = previous + 1;
            try
            {
                retained.Inner = _store.RetainOriginalReader(retained.Source, async () =>
                {
                    // Noncancellable original cleanup. This is the private child barrier,
                    // not the parent driver which awaits Home's settlement/audit.
                    await retained.Source.Read(() => actual.DispatchSettled.Task).ConfigureAwait(false);
                    await retained.Source.JoinAllAsync().ConfigureAwait(false); return 0;
                });
                await retained.Inner.ConfigureAwait(false);
            }
            finally { _logical.Value = previous; }
        }
    }
    private OriginalCommand ReserveOriginalOwnedCleanup(CommitAttempt samePrivateCommit)
    {
        // _gate is held. Only effect-free original cleanup for this exact LIVE
        // privately cached parent can survive retirement. No public status/IDs,
        // arbitrary active source, metadata query, pin acquisition or SQL is admitted.
        if (!_commits.TryGetValue(samePrivateCommit.Invocation.Intent, out var issued) || !ReferenceEquals(issued, samePrivateCommit))
            throw new UnauthorizedAccessException("The original cleanup parent was not issued by this producer.");
        if (_retiring && samePrivateCommit.Driver.IsCompleted)
            throw new ObjectDisposedException(nameof(CanonicalProjectTaskContextCreateOwner));
        if (!_retiring) return ReserveOriginal();
        if (_originals.Count >= 128) throw new InvalidOperationException("Original cleanup custody is full.");
        var original = new OriginalCommand(); _originals.Add(original); return original;
    }
    public bool IsOwnedOriginalSettlementDispatch(ICanonicalProjectTaskContextCreationIntent sameIntent,
        Task sameWait, Task<ICanonicalProjectTaskContextCreation>? sameAtomic)
    {
        if (!IsIssuedOriginalCreationIntent(sameIntent) || !_commits.TryGetValue((Intent)sameIntent, out var original)) return false;
        var invocation = original.Invocation; var wait = invocation.OriginalDispatchWait;
        return wait is not null && ReferenceEquals(wait.Driver, sameWait) && sameWait.IsCompletedSuccessfully &&
            wait.Inner?.IsCompletedSuccessfully == true && wait.Source.IsHealthySettled &&
            invocation.DispatchSettled.Task.IsCompletedSuccessfully && ReferenceEquals(invocation.Atomic, sameAtomic) &&
            (sameAtomic is null || sameAtomic.IsCompleted);
    }
    public bool IsAcknowledgedOriginalCreationSourceRefusal(Task actual)
    {
        if (IsAcknowledgedOriginalCreationRefusal(actual)) return true;
        if (_innerCommitSources.TryGetValue(actual, out var inner) && ReferenceEquals(inner.Inner, actual))
            return IsHealthyNoSqlRefusal(inner);
        if (!_writeAcquisitions.TryGetValue(actual, out var invocation) ||
            !ReferenceEquals(invocation.AcknowledgedWriteAcquisition, actual) ||
            !_commits.TryGetValue(invocation.Intent, out var same)) return false;
        return IsHealthyNoSqlRefusal(same);
    }
    private bool IsHealthyNoSqlRefusal(CommitAttempt actual) => actual.Inner is { } inner &&
        actual.Invocation.Atomic is null && actual.Invocation.DispatchSettled.Task.IsCompletedSuccessfully &&
        _store.IsAcknowledgedOriginalCommandRefusal(inner);
}
