using System.Runtime.ExceptionServices;

namespace HavenOS.Apps.Spaces.Chat;

/// <summary>Joins every admitted original read before classifying its result.
/// Sources are invoked once; cancellation cannot erase a faulted sibling.</summary>
internal sealed class ChatSpaceReadSettlement
{
    private readonly List<Task> _originals = [];

    internal Task<T> Capture<T>(Func<Task<T>> source)
    {
        Task<T> actual;
        try { actual = source() ?? throw new InvalidOperationException("The Chat read owner supplied no original Task."); }
        catch (Exception failure)
        {
            // A synchronous exception supplied no canceled original Task. Retain it
            // as a fault, including a synchronous OperationCanceledException.
            actual = Task.FromException<T>(failure);
        }
        _originals.Add(actual);
        return actual;
    }

    internal async Task JoinAsync(CancellationToken originalCancellation)
    {
        var errors = new List<Exception>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        OperationCanceledException? originalCancel = null;
        foreach (var actual in _originals.Distinct<Task>(ReferenceEqualityComparer.Instance))
        {
            try { await actual.ConfigureAwait(false); }
            catch (Exception observed)
            {
                if (actual.Exception is { } envelope)
                {
                    // Flatten only the CLR-owned Task envelope. A foreign compound
                    // or empty AggregateException remains its exact source identity.
                    foreach (var cause in envelope.InnerExceptions)
                        if (seen.Add(cause)) errors.Add(cause);
                }
                else if (actual.IsCanceled && originalCancellation.IsCancellationRequested
                    && observed is OperationCanceledException canceled
                    && canceled.CancellationToken == originalCancellation)
                    originalCancel ??= canceled;
                else if (seen.Add(observed)) errors.Add(observed);
            }
        }
        if (errors.Count == 1 && errors[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 0) throw new AggregateException("The original Chat reads did not settle successfully.", errors);
        if (originalCancel is not null) ExceptionDispatchInfo.Capture(originalCancel).Throw();
    }
}

/// <summary>Preserves the actual owner Task's fault/cancellation distinction
/// across an async adapter. It adds no admission or retry authority.</summary>
internal static class ChatSpaceOriginalTask
{
    internal static Task Run(Func<Task> source)
    {
        try { return AwaitAsync(source() ?? throw new InvalidOperationException("The Chat owner supplied no original Task.")); }
        catch (Exception error) { return Task.FromException(AsSynchronousFault(error)); }
    }
    internal static Task<T> Run<T>(Func<Task<T>> source)
    {
        try { return AwaitAsync(source() ?? throw new InvalidOperationException("The Chat owner supplied no original Task.")); }
        catch (Exception error) { return Task.FromException<T>(AsSynchronousFault(error)); }
    }
    private static Exception AsSynchronousFault(Exception error) => error is OperationCanceledException
        ? new AggregateException("The synchronous Chat source supplied no canceled original Task.", error) : error;
    private static async Task AwaitAsync(Task actual)
    {
        try { await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted) { ThrowOriginalFault(actual); throw; }
    }
    private static async Task<T> AwaitAsync<T>(Task<T> actual)
    {
        try { return await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted) { ThrowOriginalFault(actual); throw; }
    }
    private static void ThrowOriginalFault(Task actual)
    {
        var envelope = actual.Exception!;
        if (envelope.InnerExceptions is { Count: 1 } causes && causes[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(causes[0]).Throw();
        throw envelope;
    }
}
