using System.Runtime.ExceptionServices;
namespace HavenOS.Home.Core;

/// <summary>Finite callback scope and raw Task custody only. Acquire inside the supplied
/// scope, retain before it returns, then independently join even a post-scope failure.
/// This issues no actor/lease/currentness decision and never repeats the factory.</summary>
internal static class HomeOriginalScopedSourceCallbacks
{
    internal static async Task<T> AwaitAsync<T>(Func<Task<T>> finite, Action<Action> scope, Action<Task> retain)
    {
        Task<T>? actual = null; int invoked = 0, active = 1; int originalThread = Environment.CurrentManagedThreadId; Exception? scopeFailure = null; Exception? observed = null;
        var errors = new List<Exception>(); T result = default!;
        try
        {
            scope(() =>
            {
                if (Volatile.Read(ref active) == 0)
                    throw new InvalidOperationException("The original Home synchronous callback phase has ended.");
                if (Environment.CurrentManagedThreadId != originalThread)
                    throw new InvalidOperationException("The original Home source callback must run on its synchronous issuing thread.");
                if (Interlocked.CompareExchange(ref invoked, 1, 0) != 0)
                    throw new InvalidOperationException("An original Home finite source callback cannot run twice.");
                actual = finite(); retain(actual);
            });
            if (Volatile.Read(ref invoked) == 0) throw new InvalidOperationException("The original Home source scope did not invoke its callback.");
        }
        catch (Exception cause) { scopeFailure = cause; errors.Add(cause); }
        finally { Volatile.Write(ref active, 0); } // Revoke before any await or terminal/refusal cleanup.
        if (actual is not null)
        {
            try { result = await actual.ConfigureAwait(false); }
            catch (Exception cause)
            {
                observed = cause;
                if (actual.Exception is { } group) foreach (var direct in group.InnerExceptions) Add(errors, direct);
                else Add(errors, cause);
            }
        }
        if (scopeFailure is null && actual?.IsCanceled == true && observed is not null)
            ExceptionDispatchInfo.Capture(observed).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual Home callback scope/raw source failed.", errors);
        return result;
    }
    // An actual successful acquisition product is captured before a scope error escapes.
    // The owning caller then independently closes that SAME product during its finally.
    internal static async Task<T> AwaitAsync<T>(Func<Task<T>> finite, Action<Action> scope,
        Action<Task> retain, Action<T> retainActualResult)
    {
        Task<T>? actual = null; int invoked = 0, active = 1; int originalThread = Environment.CurrentManagedThreadId; Exception? scopeFailure = null; Exception? observed = null;
        var errors = new List<Exception>(); T result = default!;
        try
        {
            scope(() =>
            {
                if (Volatile.Read(ref active) == 0)
                    throw new InvalidOperationException("The original Home synchronous callback phase has ended.");
                if (Environment.CurrentManagedThreadId != originalThread)
                    throw new InvalidOperationException("The original Home source callback must run on its synchronous issuing thread.");
                if (Interlocked.CompareExchange(ref invoked, 1, 0) != 0)
                    throw new InvalidOperationException("An original Home finite source callback cannot run twice.");
                actual = finite(); retain(actual);
            });
            if (Volatile.Read(ref invoked) == 0) throw new InvalidOperationException("The original Home source scope did not invoke its callback.");
        }
        catch (Exception cause) { scopeFailure = cause; errors.Add(cause); }
        finally { Volatile.Write(ref active, 0); } // Revoke before any await or terminal/refusal cleanup.
        if (actual is not null)
        {
            try { result = await actual.ConfigureAwait(false); retainActualResult(result); }
            catch (Exception cause)
            {
                observed = cause;
                if (actual.Exception is { } group) foreach (var direct in group.InnerExceptions) Add(errors, direct);
                else Add(errors, cause);
            }
        }
        if (scopeFailure is null && actual?.IsCanceled == true && observed is not null)
            ExceptionDispatchInfo.Capture(observed).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual Home acquisition scope/raw source failed.", errors);
        return result;
    }
    internal static async Task AwaitAsync(Func<Task> finite, Action<Action> scope, Action<Task> retain,
        Action? retainActualSuccess = null)
    {
        Task? actual = null; int invoked = 0, active = 1; int originalThread = Environment.CurrentManagedThreadId; Exception? scopeFailure = null; Exception? observed = null;
        var errors = new List<Exception>();
        try
        {
            scope(() =>
            {
                if (Volatile.Read(ref active) == 0)
                    throw new InvalidOperationException("The original Home synchronous callback phase has ended.");
                if (Environment.CurrentManagedThreadId != originalThread)
                    throw new InvalidOperationException("The original Home source callback must run on its synchronous issuing thread.");
                if (Interlocked.CompareExchange(ref invoked, 1, 0) != 0)
                    throw new InvalidOperationException("An original Home finite source callback cannot run twice.");
                actual = finite(); retain(actual);
            });
            if (Volatile.Read(ref invoked) == 0) throw new InvalidOperationException("The original Home source scope did not invoke its callback.");
        }
        catch (Exception cause) { scopeFailure = cause; errors.Add(cause); }
        finally { Volatile.Write(ref active, 0); } // Revoke before any await or terminal/refusal cleanup.
        if (actual is not null)
        {
            try { await actual.ConfigureAwait(false); retainActualSuccess?.Invoke(); }
            catch (Exception cause)
            {
                observed = cause;
                if (actual.Exception is { } group) foreach (var direct in group.InnerExceptions) Add(errors, direct);
                else Add(errors, cause);
            }
        }
        if (scopeFailure is null && actual?.IsCanceled == true && observed is not null)
            ExceptionDispatchInfo.Capture(observed).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual Home callback scope/raw source failed.", errors);
    }
    private static void Add(List<Exception> errors, Exception cause)
    { if (!errors.Any(original => ReferenceEquals(original, cause))) errors.Add(cause); }
}

/// <summary>Optional actual already-held local lease. Checks never reacquire Home/resource/
/// Files/completion locks. Scopes and raw custody must reach post-await actual factories.</summary>
public interface IHomeOriginalScopedLocalOperationLease : IHomeLocalOperationLease
{
    ValueTask<bool> IsCurrentAsync(Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
}

/// <summary>Only an actual owning guard may support the scoped held-state check.</summary>
public interface IHomeOriginalScopedStateCommitActorGuard : IHomeStateCommitActorGuard
{
    ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, Haven.Application.AuthenticatedResourceActor expectedActor,
        HomeStateCommitPhase phase, Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken);
}
