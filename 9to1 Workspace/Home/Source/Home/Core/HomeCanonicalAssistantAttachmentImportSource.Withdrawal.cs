using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed partial class HomeCanonicalAssistantAttachmentImportSource
{
    public Task WithdrawOriginalPendingImportWithinSourceAsync(ICanonicalAttachmentHomeImportClaim sameClaim,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => Require(sameClaim).WithdrawReview(scope, retain, token);

    private sealed partial class Claim
    {
        private readonly TaskCompletionSource<Task<HomePreparedReviewObservation>?> _originalReviewPrepared =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<HomePreparedReviewObservation>? _originalPreparedReviewTask;
        private Task<bool>? _originalReviewWithdrawal;
        private Context? _originalWithdrawalContext;
        private bool _originalReviewWasWithdrawn;
        private bool _originalCapabilityAcquisitionStarted;

        internal Task WithdrawReview(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            Task<bool> actual; TaskCompletionSource? start = null; Context? context = null;
            lock (_gate)
            {
                if (_originalReviewWithdrawal is not null) return _originalReviewWithdrawal;
                // No original command is admitted after close/settlement or execution.
                // Existing accepted business is joined by its producer, without a review effect.
                if (_close is not null || _settlement is not null || _originalCapabilityAcquisitionStarted ||
                    _capability is not null || _entry is not null || _home is not null || _operation is not null)
                    return Task.CompletedTask;
                if (_operations.Count >= 4096) throw new InvalidOperationException("Original attachment WRITE custody is full.");
                context = new(owner, this, scope, retain, productive: false);
                _originalWithdrawalContext = context; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual = Drive(start.Task, context, async sources =>
                {
                    // This independent child signals only review preparation, never the
                    // encompassing Acquisition which may itself await this withdrawal.
                    var child = await sources.Read(() => _originalReviewPrepared.Task).ConfigureAwait(false);
                    if (child is null) return false; // No prepared request was admitted; Acquisition retains its actual outcome.
                    if (!ReferenceEquals(child, _originalPreparedReviewTask) || _review is null)
                        throw new InvalidOperationException("The original prepared review source was not acknowledged.");
                    await sources.Await(child).ConfigureAwait(false);
                    var preExecution = sources.Physical(() =>
                    {
                        lock (_gate) return _close is null && _settlement is null && !_originalCapabilityAcquisitionStarted &&
                            _capability is null && _entry is null && _home is null && _operation is null;
                    });
                    if (!preExecution) return false; // Accepted business remains in its original producer cohort.
                    var retired = await sources.Read(() => owner._broker.WithdrawOriginalAttachmentImportWithinSourceAsync(
                        _review, sources.Scope, sources.Retain, token)).ConfigureAwait(false);
                    if (!retired) return false; // Approval won the permission-gate race; it must settle normally.
                    lock (_gate) _originalReviewWasWithdrawn = true;
                    return true;
                });
                _originalReviewWithdrawal = actual; _operations.Add(actual); _contexts.Add(context);
            }
            try { context.Publish(actual); } catch (Exception cause) { context.Errors.Retain(cause); }
            finally { start.SetResult(); } return actual;
        }
        private async Task DemandReviewNotWithdrawn(Context sources)
        {
            Task<bool>? withdrawal; lock (_gate) withdrawal = _originalReviewWithdrawal;
            if (withdrawal is null) return;
            if (!await sources.Await(withdrawal).ConfigureAwait(false)) return;
            if (!_originalReviewWasWithdrawn || _originalWithdrawalContext?.SuccessfullySettled != true)
                throw new InvalidOperationException("The actual attachment withdrawal has no independently healthy original acknowledgment.");
            _acknowledgedRefusal ??= new UnauthorizedAccessException("The owning process withdrew this exact attachment review before capability/SQL admission.");
            throw _acknowledgedRefusal;
        }
        private bool HasHealthyWithdrawnPreEffectSources => _originalReviewWasWithdrawn &&
            _originalReviewWithdrawal?.IsCompletedSuccessfully == true && _originalReviewWithdrawal.Result &&
            _originalWithdrawalContext?.SuccessfullySettled == true && !_originalCapabilityAcquisitionStarted;
    }
}

public sealed partial class HomeCanonicalAssistantAttachmentImportSource
{
    private bool _originalWithdrawalRequested;
    private Task? _originalPendingReviewWithdrawalTask;
    private readonly List<Context> _originalPendingWithdrawalContexts = [];
    public Task? OriginalPendingReviewWithdrawalTask { get { lock (_gate) return _originalPendingReviewWithdrawalTask; } }

    /// <summary>Seal new review admission, then withdraw only this issuer's pending
    /// reviews. Already approved/SQL-entered operations settle in their product owner.</summary>
    public void RequestOriginalPendingReviewWithdrawals()
    {
        DemandExternalOriginalJoin(); TaskCompletionSource? begin = null;
        lock (_gate)
        {
            if (_originalPendingReviewWithdrawalTask is not null) return;
            if (_close is not null || _retiring) throw new ObjectDisposedException(nameof(HomeCanonicalAssistantAttachmentImportSource));
            _originalWithdrawalRequested = true;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _originalPendingReviewWithdrawalTask = WithdrawPublished(begin.Task, _active.ToArray());
        }
        begin.SetResult();
    }
    private async Task WithdrawPublished(Task start, Claim[] claims)
    {
        await start.ConfigureAwait(false); using var original = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        List<Task> withdrawals = []; List<Exception> errors = [];
        foreach (var claim in claims)
        {
            // The claim's independent review-prepared child covers a genuinely late
            // accepted review. It never joins its own encompassing acquisition.
            var context = new Context(this, claim, body => body(), _ => { }, productive: false);
            lock (_gate) _originalPendingWithdrawalContexts.Add(context);
            try
            {
                context.Physical(() =>
                {
                    var actual = claim.WithdrawReview(context.Scope, context.Retain, CancellationToken.None);
                    withdrawals.Add(actual); _ = context.Errors.Track(actual); return true;
                });
            }
            catch (Exception cause) { context.Errors.Retain(cause); }
        }
        foreach (var actual in withdrawals)
            try { await actual.ConfigureAwait(false); } catch (Exception cause) { errors.Add(actual.Exception ?? cause); }
        Context[] all; lock (_gate) all = _originalPendingWithdrawalContexts.ToArray();
        foreach (var context in all)
            try { await context.Settle().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Actual Assistant attachment pending review withdrawals failed; all accepted originals remain retained.", errors);
    }
}
