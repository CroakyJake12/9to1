using System.Runtime.ExceptionServices;
using Haven.Core;

namespace Haven.Application;

public sealed partial class TaskExecutionCoordinator : ITaskRunOriginalInferenceAttemptSource
{
    /// <summary>Reads the actual current row inside the caller's finite source scope. Only the
    /// SAME privately issued active admission can be returned; this lookup issues no grant.</summary>
    public Task<TaskRunAttemptAdmission?> GetIssuedAttemptWithinOriginalSourceAsync(
        TaskRunAttemptAdmission sameAdmission, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(sameAdmission);
        ArgumentNullException.ThrowIfNull(originalSynchronousScope);
        ArgumentNullException.ThrowIfNull(retainOriginalTask);
        return StartOriginalProcessStage("read-original-inference-attempt", token, cancellation =>
            ReadIssuedInferenceOriginalAsync(sameAdmission, originalSynchronousScope, retainOriginalTask, cancellation));
    }

    private async Task<TaskRunAttemptAdmission?> ReadIssuedInferenceOriginalAsync(
        TaskRunAttemptAdmission sameAdmission, Action<Action> callerScope,
        Action<Task> callerRetain, CancellationToken token)
    {
        var stage = RequireOriginalProcessStage();
        var source = new InferenceAttemptCallbacks(stage, callerScope, callerRetain);
        token.ThrowIfCancellationRequested();
        if (!source.Invoke(() => IsSamePrivatelyIssuedInferenceAttempt(sameAdmission))) return null;
        var snapshot = await source.ReadAsync(() => repository.GetAsync(sameAdmission.Snapshot.TaskId, token)).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Task execution was not found.");
        token.ThrowIfCancellationRequested();
        // This callback is separately scoped after the real await. Lease getters are genuine
        // source callbacks, and process admission must still be open before disclosure.
        return source.Invoke(() =>
        {
            if (!IsSamePrivatelyIssuedInferenceAttempt(sameAdmission)) return null;
            RequireRun(snapshot, sameAdmission.Snapshot.ExecutionId);
            var attempt = RequireCurrentAttempt(snapshot, sameAdmission.AttemptId);
            if (snapshot.TaskId != sameAdmission.Snapshot.TaskId || snapshot.ContextId != sameAdmission.Snapshot.ContextId
                || snapshot.OwnerBinding != sameAdmission.Snapshot.OwnerBinding
                || snapshot.State != TaskExecutionLifecycle.Running)
                throw new InvalidOperationException("The actual original inference task/run/owner is no longer current.");
            ValidateOwner(snapshot, sameAdmission.Lease.Owner);
            if (sameAdmission.Lease.Owner != snapshot.OwnerBinding || sameAdmission.Lease.AttemptId != attempt.Id
                || !CandidatesEqual(attempt.Candidate, sameAdmission.Lease.Candidate)
                || attempt.AdmissionReceiptReference != sameAdmission.Lease.ReceiptReference)
                throw new InvalidOperationException("The current actual inference attempt differs from its private issued route.");
            // A lease getter can reenter retirement; permanent private issuance and source
            // admission must still hold at this finite disclosure boundary.
            return stage.Invoke(() => IsSamePrivatelyIssuedInferenceAttempt(sameAdmission) ? sameAdmission : null);
        });
    }

    private bool IsSamePrivatelyIssuedInferenceAttempt(TaskRunAttemptAdmission sameAdmission) =>
        _issuedAdmissions.TryGetValue(sameAdmission.AttemptId, out var issued) && ReferenceEquals(issued, sameAdmission);

    private sealed class InferenceAttemptCallbacks(TaskRunProcessStageCustody stage,
        Action<Action> callerScope, Action<Task> callerRetain)
    {
        internal T Invoke<T>(Func<T> factory) => stage.Invoke(() =>
        {
            var thread = Environment.CurrentManagedThreadId;
            var active = 1;
            var invoked = 0;
            T result = default!;
            Exception? callbackFailure = null;
            try
            {
                callerScope(() =>
                {
                    if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread
                        || Interlocked.Exchange(ref invoked, 1) != 0)
                    {
                        var refusal = new InvalidOperationException("The original inference callback is inactive, foreign-thread or already consumed.");
                        Interlocked.CompareExchange(ref callbackFailure, refusal, null);
                        throw refusal;
                    }
                    try { result = stage.Invoke(factory); } // Fresh admission at the actual finite factory, after caller scope entry.
                    catch (Exception cause) { Interlocked.CompareExchange(ref callbackFailure, cause, null); throw; }
                });
                if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                if (Volatile.Read(ref invoked) == 0)
                    throw new InvalidOperationException("The finite original inference callback was not invoked synchronously.");
                // The supported caller scope can request retirement after its callback.
                // Check the SAME stage again after scope exit before disclosing this result.
                return stage.Invoke(() => result);
            }
            catch (Exception cause)
            {
                // A caller scope may replace a swallowed callback failure with its own fault.
                // Both exact originals remain in the SAME owning stage.
                if (callbackFailure is not null && !ReferenceEquals(cause, callbackFailure))
                    stage.RetainOriginalFailure(callbackFailure);
                throw;
            }
            finally { Interlocked.Exchange(ref active, 0); }
        });

        internal async Task<T> ReadAsync<T>(Func<Task<T>> factory)
        {
            Task<T>? actual = null;
            Exception? invocationFailure = null;
            try
            {
                _ = Invoke(() =>
                {
                    actual = factory() ?? throw new InvalidOperationException("The actual inference repository returned no Task.");
                    // Enrollment precedes the external retain callback and any later scope fault.
                    stage.RetainSource(actual);
                    callerRetain(actual);
                    return actual;
                });
            }
            catch (Exception cause) { invocationFailure = cause; }
            T result = default!;
            if (actual is not null)
            {
                // Once acquired, this raw original is joined without another admission/token
                // check. A caller-scope failure must not abandon a held repository operation.
                try { result = await actual.ConfigureAwait(false); }
                catch (Exception cause)
                {
                    stage.RetainOriginalFailure(cause, actual);
                    if (actual.IsFaulted && actual.Exception is { } group) throw group;
                    throw;
                }
            }
            if (invocationFailure is not null) ExceptionDispatchInfo.Capture(invocationFailure).Throw();
            return actual is null ? throw new InvalidOperationException("No actual inference repository read was acquired.") : result;
        }
    }
}
