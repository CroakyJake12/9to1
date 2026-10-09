using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed partial class HomeResourceOperationBroker
{
    internal bool IsRetainedOriginalAutomationReview(HomeResourcePreparedReview prepared, HomePermissionTrustService samePermissions) =>
        ReferenceEquals(permissions, samePermissions) && prepared.IssuedBy(this) && _preparedReviews.ContainsKey(prepared) &&
        prepared.Submission.Scope.TargetAppId == "automations" &&
        HomeCanonicalAutomationDefinitionWriteSource.IsSupportedAction(prepared.Submission.Scope.ActionName);
    internal bool MatchesOriginalAutomationReview(HomeResourcePreparedReview prepared, HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest request) =>
        IsRetainedOriginalAutomationReview(prepared, permissions) && prepared.CanonicalAdmission && MatchesPrepared(prepared.Submission, request);

    // The setup claim refuses this call after its capability acquisition begins. The
    // actual permission gate independently refuses a race which already began execution.
    internal Task<bool> WithdrawOriginalAutomationReviewWithinSourceAsync(HomeResourcePreparedReview prepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new HomeOwnershipOriginalSourceCallbacks(
            body => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { scope(body); return true; }),
            raw => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { retain(raw); return true; }));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> original;
        lock (_originalAutomationWithdrawalGate)
        {
            _originalAutomationWithdrawals.RemoveAll(value => value.Driver.IsCompletedSuccessfully && value.Sources.Errors.Length == 0);
            if (_originalAutomationWithdrawals.Count >= 256)
                throw new InvalidOperationException("Unresolved original automation withdrawals remain retained.");
            original = Drive(start.Task);
            _originalAutomationWithdrawals.Add((original, source));
        }
        try { source.Run(() => retain(original)); } catch { /* accepted body owns callback failure */ }
        finally { start.SetResult(); }
        return original;
        async Task<bool> Drive(Task gate)
        {
            await gate.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            if (source.Errors.Length != 0) throw new AggregateException("Original automation withdrawal publication failed.", source.Errors);
            source.Run(() => RequirePrepared(prepared));
            await WaitColdProjectBrokerGateAsync(source, prepared.Gate, token).ConfigureAwait(false);
            try
            {
                source.Run(() => RequirePrepared(prepared));
                if (!IsRetainedOriginalAutomationReview(prepared, permissions))
                    throw new UnauthorizedAccessException("The original automation definition WRITE review is required.");
                var request = await source.ReadAsync(() => permissions.WithdrawOriginalAutomationReviewWithinSourceAsync(
                    this, prepared, source.Run, source.Retain, token)).ConfigureAwait(false);
                if (request is null) return false;
                if (request.State != HomePermissionRequestState.Cancelled || !MatchesPrepared(prepared.Submission, request))
                    throw new UnauthorizedAccessException("The actual automation withdrawal did not acknowledge its exact original request.");
                return source.Invoke(() =>
                {
                    if (!_preparedReviews.TryRemove(prepared, out _)) throw new InvalidOperationException("Original setup review retirement was not acknowledged.");
                    if (prepared.BoundOnce) _bindings.TryRemove(prepared.RequestId, out _);
                    return true;
                });
            }
            finally { source.Run(() => prepared.Gate.Release()); }
        }
    }
    private readonly object _originalAutomationWithdrawalGate = new();
    private readonly List<(Task Driver, HomeOwnershipOriginalSourceCallbacks Sources)> _originalAutomationWithdrawals = [];
}
