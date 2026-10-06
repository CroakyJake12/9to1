using System.Runtime.ExceptionServices;
using Haven.Application;
namespace Haven.Infrastructure;

public sealed partial class HostLocalTaskActorSource : ITaskRunOriginalTaskActorObservationSource
{
    public async Task<AuthenticatedResourceActor?> GetOriginalCurrentWithinSourceAsync(Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        Task<AuthenticatedResourceActor?>? raw = null; AuthenticatedResourceActor? result = null;
        int active = 1, used = 0, thread = Environment.CurrentManagedThreadId; var failures = new List<Exception>();
        void Add(Exception cause) { lock (failures) if (!failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause); }
        Exception[] Causes() { lock (failures) return failures.ToArray(); }
        try
        {
            originalSynchronousScope(() =>
            {
                if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                {
                    var refusal = new InvalidOperationException("The original OS Task actor callback is inactive, foreign-thread or consumed."); Add(refusal); throw refusal;
                }
                try
                {
                    // The maintained actual OS/token/account observation is synchronous;
                    // it all runs inside this finite parent boundary. Convert this SAME ValueTask once.
                    raw = GetCurrentAsync(cancellationToken).AsTask(); retainOriginalTask(raw);
                }
                catch (Exception cause) { Add(cause); throw; }
            });
            if (Volatile.Read(ref used) == 0) Add(new InvalidOperationException("The original OS Task actor callback was not invoked."));
        }
        catch (Exception cause) { Add(cause); }
        finally { Interlocked.Exchange(ref active, 0); }
        Exception? observed = null;
        if (raw is not null)
            try { result = await raw.ConfigureAwait(false); }
            catch (Exception cause)
            {
                observed = cause;
                if (raw.Exception is { } group) { Add(group); foreach (var direct in group.InnerExceptions) Add(direct); }
                else Add(cause);
            }
        if (raw?.IsCanceled == true && observed is not null && Causes().All(value => value is OperationCanceledException))
            ExceptionDispatchInfo.Capture(observed).Throw();
        var errors = Causes(); if (errors.Length != 0) throw new AggregateException("Actual OS Task actor source/scope failed.", errors);
        return raw is null ? throw new InvalidOperationException("No actual Task actor observation was acquired.") : result;
    }
}
