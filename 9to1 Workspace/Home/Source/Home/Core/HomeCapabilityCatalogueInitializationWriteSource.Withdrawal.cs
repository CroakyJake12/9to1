using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed partial class HomeCapabilityCatalogueInitializationWriteSource
{
    public Task WithdrawOriginalPendingWriteWithinSourceAsync(ICapabilityOriginalInitializationHomeWriteClaim sameClaim,
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
                    _capability is not null || _entry is not null || _home is not null || _sql is not null)
                    return Task.CompletedTask;
                if (_operations.Count >= 4096) throw new InvalidOperationException("Original setup WRITE custody is full.");
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
                            _capability is null && _entry is null && _home is null && _sql is null;
                    });
                    if (!preExecution) return false; // Accepted business remains in its original producer cohort.
                    var retired = await sources.Read(() => owner._broker.WithdrawOriginalSetupReviewWithinSourceAsync(
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
                throw new InvalidOperationException("The actual setup withdrawal has no independently healthy original acknowledgment.");
            _acknowledgedRefusal ??= new UnauthorizedAccessException("The owning process withdrew this exact setup review before capability/SQL admission.");
            throw _acknowledgedRefusal;
        }
        private bool HasHealthyWithdrawnPreEffectSources => _originalReviewWasWithdrawn &&
            _originalReviewWithdrawal?.IsCompletedSuccessfully == true && _originalReviewWithdrawal.Result &&
            _originalWithdrawalContext?.SuccessfullySettled == true && !_originalCapabilityAcquisitionStarted;
    }
}
