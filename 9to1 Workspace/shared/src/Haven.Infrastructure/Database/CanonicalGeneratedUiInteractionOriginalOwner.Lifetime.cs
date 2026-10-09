namespace Haven.Infrastructure;

public sealed partial class CanonicalGeneratedUiInteractionOriginalOwner
{
    private Task<T> Admit<T>(CanonicalSqliteOriginalSourceScope source, Func<Task<T>> factory, Intent? acceptedValidation = null)
    {
        DemandHealthyOriginalCallbacks();
        Original original;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring && (acceptedValidation is null || !IsOriginalSaveInvocation(acceptedValidation)), this);
            _originals.RemoveAll(item => item.Joined && item.Raw?.IsCompletedSuccessfully == true && item.Source.IsHealthySettled);
            if (_originals.Count >= 128) throw new InvalidOperationException("Original interaction custody is full; unresolved originals remain retained.");
            original = new(source); _originals.Add(original);
        }
        var previous = _logical.Value; _logical.Value = previous + 1; Task<T> raw;
        async Task<T> ProductiveFactory()
        {
            DemandHealthyOriginalCallbacks();
            var actual = factory() ?? throw new InvalidOperationException("The original interaction command returned no Task.");
            // Retain the actual factory Task before the guarded presentation wrapper
            // can fail. Never enroll the inner driver into its own source raw ledger.
            original.Inner = actual;
            var result = await actual.ConfigureAwait(false); DemandHealthyOriginalCallbacks(); return result;
        }
        try { raw = ProductiveFactory(); }
        catch (Exception cause) { raw = Task.FromException<T>(cause); }
        finally { _logical.Value = previous; }
        original.Raw = raw; original.Observation = Observe(original); original.Publication.TrySetResult(raw); return raw;
    }
    private async Task Observe(Original original)
    {
        var actual = await original.Publication.Task.ConfigureAwait(false);
        try { await actual.ConfigureAwait(false); } catch { }
        lock (_gate) original.Joined = true;
    }
    public void DemandExternalOriginalJoin()
    {
        if (_logical.Value != 0 || CanonicalSqliteOriginalSourceScope.IsPhysicalSource(this))
            throw new InvalidOperationException("An actual interaction callback cannot join its own original owner.");
    }
    public void RequestOriginalRetirement() { RetireOriginalSaveDeliveries(); RequestOriginalPendingReviewWithdrawals(); }
    public Task CloseAndDrainOriginalAsync()
    {
        DemandExternalOriginalJoin(); RequestOriginalRetirement(); Task actual; TaskCompletionSource? start = null;
        lock (_gate)
        {
            if (_close is null) { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = Drain(start.Task); }
            actual = _close;
        }
        start?.SetResult(); return actual;
    }
    private async Task Drain(Task begin)
    {
        await begin.ConfigureAwait(false); var failures = new List<Exception>();
        var joined = new HashSet<Original>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Original[] pending; lock (_gate) pending = _originals.Where(item => joined.Add(item)).ToArray();
            if (pending.Length == 0) break;
            foreach (var item in pending)
            {
                var raw = await item.Publication.Task.ConfigureAwait(false);
                try { await raw.ConfigureAwait(false); }
                catch (Exception cause) { if (!IsAcknowledgedOriginalSaveSourceRefusal(raw)) CanonicalSqliteOriginalStoreOwner.Capture(failures, raw, cause); }
                if (item.Inner is { } inner && !ReferenceEquals(inner, raw))
                    try { await inner.ConfigureAwait(false); }
                    catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(failures, inner, cause); }
                try { await item.Observation.ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
                try { await item.Source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
            }
        }
        Task[] withdrawals; lock (_gate) withdrawals = _pendingWithdrawals.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
        foreach (var actual in withdrawals) try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(failures, actual, cause); }
        CanonicalSqliteOriginalSourceScope[] withdrawalSources; lock (_gate) withdrawalSources = _withdrawalSources.ToArray();
        foreach (var source in withdrawalSources) try { await source.JoinAllAsync().ConfigureAwait(false); }
            catch (Exception cause) { failures.Add(cause); }
        try { await JoinOriginalSaveDeliveries().ConfigureAwait(false); } catch (Exception cause) { failures.Add(cause); }
        lock (_gate) foreach (var cause in _unexpectedOriginalCallbacks)
            if (!failures.Any(prior => ReferenceEquals(prior, cause))) failures.Add(cause);
        CanonicalSqliteOriginalStoreOwner.Throw(failures);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}
