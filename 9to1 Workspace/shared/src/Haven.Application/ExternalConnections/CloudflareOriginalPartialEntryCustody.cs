namespace Haven.Application;

/// <summary>Custody for an original partial entry and its independent closes. This
/// issues no permission or dispatch receipt. Raw Tasks remain in the same ledger.</summary>
public static class CloudflareOriginalPartialEntryCustody
{
    public static async Task<T> RunOriginalAsync<T>(CloudflareOriginalTaskLedger stages,
        Func<Task<T>> originalBody, Func<IReadOnlyList<Func<ValueTask>>> originalPartialCloses)
    {
        var errors = new List<Exception>(); T result = default!;
        Task<T>? actualBody = null;
        try { stages.Invoke(() => { actualBody = originalBody(); return actualBody; }); }
        catch (Exception error) { stages.Retain(error); errors.Add(error); }
        if (actualBody is not null)
            try { result = await stages.AwaitAsync(actualBody).ConfigureAwait(false); }
            catch (Exception error) { stages.Retain(error); errors.Add(error); }
        IReadOnlyList<Func<ValueTask>> closes = [];
        try { stages.Invoke(() => { closes = originalPartialCloses(); return closes; }); }
        catch (Exception error) { stages.Retain(error); errors.Add(error); }
        foreach (var close in closes)
            try { await stages.ObserveOriginalCloseAsync(close).ConfigureAwait(false); }
            catch (Exception error) { stages.Retain(error); errors.Add(error); }
        if (errors.Count != 0 && result is IAsyncDisposable actualLateEntry)
            try { await stages.ObserveOriginalCloseAsync(actualLateEntry.DisposeAsync).ConfigureAwait(false); }
            catch (Exception error) { stages.Retain(error); errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Original entry and independent partial closes failed.", errors);
        return result;
    }
}
