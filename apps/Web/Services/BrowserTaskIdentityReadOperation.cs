using System.Runtime.ExceptionServices;

namespace NineToOne.Web.Services;

/// <summary>
/// Owns the actual browser identity read through cancellation, consumption and receipt release.
/// A cancellation callback failure never discards the already-started module Task.
/// </summary>
internal static class BrowserTaskIdentityReadOperation
{
    public static async Task<T> RunAsync<T>(Func<Task<string>> read, Action cancel,
        Func<string, T> consume, Action release, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var errors = new List<Exception>();
        var cancellationErrors = new List<Exception>();
        var sync = new object();
        Task<string>? original = null;
        CancellationTokenRegistration registration = default;
        var actualCancellation = false;
        var forceFault = false;
        T? result = default;
        try
        {
            // Capture the SAME raw Promise Task before any cancellation callback can run.
            try { original = read() ?? throw new InvalidOperationException("The Task identity module returned no original Task."); }
            catch (Exception error) { Add(errors, error); forceFault = true; }
            if (original is not null)
            {
                try
                {
                    registration = token.Register(() =>
                    {
                        try { cancel(); }
                        catch (Exception error) { lock (sync) Add(cancellationErrors, error); }
                    });
                }
                catch (Exception error) { Add(errors, error); forceFault = true; }

                string? json = null;
                try { json = await original.ConfigureAwait(false); }
                catch (Exception observed)
                {
                    if (original.IsFaulted && original.Exception is { } fault)
                    {
                        Add(errors, fault); // Preserve the complete original fault group, including faulted OCE.
                        forceFault = true;
                    }
                    else { Add(errors, observed); actualCancellation = original.IsCanceled; }
                }
                lock (sync) foreach (var error in cancellationErrors) Add(errors, error);
                if (errors.Count == 0)
                {
                    if (token.IsCancellationRequested)
                    {
                        actualCancellation = true;
                        Add(errors, new OperationCanceledException(token));
                    }
                    else
                    {
                        try { result = consume(json!); }
                        catch (Exception error) { Add(errors, error); forceFault = true; }
                        if (errors.Count == 0 && token.IsCancellationRequested)
                        {
                            actualCancellation = true;
                            Add(errors, new OperationCanceledException(token));
                        }
                    }
                }
            }
        }
        finally
        {
            // Join any currently running callback before examining its retained failures or
            // releasing the SAME receipt. No callback can mutate custody after this boundary.
            try { await registration.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { Add(errors, error); forceFault = true; }
            lock (sync)
            {
                foreach (var error in cancellationErrors) Add(errors, error);
                forceFault |= cancellationErrors.Count != 0;
            }
            try { release(); }
            catch (Exception error) { Add(errors, error); forceFault = true; }
        }
        if (errors.Count == 1)
        {
            if (errors[0] is OperationCanceledException && (forceFault || !actualCancellation))
                throw new AggregateException("The original Task identity operation faulted.", errors[0]);
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        }
        if (errors.Count > 1) throw new AggregateException("Task identity read, cancellation or receipt release failed.", errors);
        return result!;
    }

    private static void Add(List<Exception> errors, Exception error)
    {
        if (!errors.Any(actual => ReferenceEquals(actual, error))) errors.Add(error);
    }
}
