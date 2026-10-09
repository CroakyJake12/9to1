using Haven.Application;
using Haven.Application.Automations;

namespace Haven.Infrastructure;

public sealed partial class CanonicalAutomationDefinitionOriginalWriteOwner
{
    private readonly List<Commit> _activeCommits = [];
    private readonly List<Task> _withdrawals = [];
    private readonly List<Exception> _withdrawalBirthFailures = [];
    private void CaptureOriginalPendingWithdrawal(Commit current)
    {
        lock (_gate)
        {
            if (!_activeCommits.Contains(current)) _activeCommits.Add(current);
            if (!_retiring || current.Claim is null || current.Withdrawal is not null) return;
        }
        AcquireOriginalPendingWithdrawal(current);
    }
    private void AcquireOriginalPendingWithdrawal(Commit current)
    {
        if (OriginalHomeWriteSource is not ICanonicalAutomationDefinitionOriginalHomeReviewWithdrawalSource sameWithdrawals)
        {
            lock (_gate) _withdrawalBirthFailures.Add(new InvalidOperationException("The SAME configured Home WRITE owner has no issuer pending-review withdrawal port."));
            return;
        }
        // Actual claim is captured before external callbacks; retain the exact withdrawal
        // Task before publication, independently of the enclosing business commit.
        lock (_gate)
        {
            if (current.Claim is null || current.Withdrawal is not null) return;
            try
            {
                current.Withdrawal = sameWithdrawals.WithdrawOriginalPendingWriteWithinSourceAsync(current.Claim,
                    body => body(), _ => { }, CancellationToken.None);
                _withdrawals.Add(current.Withdrawal);
            }
            catch (Exception cause) { _withdrawalBirthFailures.Add(cause); }
        }
    }
    public void RequestOriginalPendingReviewWithdrawals()
    {
        Commit[] all; lock (_gate) { _retiring = true; all = _activeCommits.ToArray(); }
        foreach (var current in all) AcquireOriginalPendingWithdrawal(current);
    }
    public void RequestOriginalRetirement()
    {
        RequestOriginalPendingReviewWithdrawals();
        RetireOriginalDeliveries();
    }
    public void DemandExternalOriginalJoin()
    {
        if (IsInside) throw new InvalidOperationException("An actual Automation source cannot synchronously join its own original close.");
    }
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
        await begin.ConfigureAwait(false); var errors = new List<Exception>();
        var joined = new HashSet<Original>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            Original[] pending; lock (_gate) pending = _originals.Where(item => joined.Add(item)).ToArray();
            if (pending.Length == 0) break;
            foreach (var item in pending)
            {
                var raw = await item.Publication.Task.ConfigureAwait(false);
                try { await raw.ConfigureAwait(false); }
                catch (Exception cause)
                { if (!IsAcknowledgedOriginalChangeSourceRefusal(raw)) CanonicalSqliteOriginalStoreOwner.Capture(errors, raw, cause); }
                try { await item.Observation.ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            }
        }
        Task[] withdrawals; lock (_gate) { withdrawals = _withdrawals.ToArray(); errors.AddRange(_withdrawalBirthFailures); }
        foreach (var raw in withdrawals)
            try { await raw.ConfigureAwait(false); }
            catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, raw, cause); }
        try { await JoinOriginalProcessDeliveriesAsync().ConfigureAwait(false); }
        catch (Exception cause) { errors.Add(cause); }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalAsync());
}
