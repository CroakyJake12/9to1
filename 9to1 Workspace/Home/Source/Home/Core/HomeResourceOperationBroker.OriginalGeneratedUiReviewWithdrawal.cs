using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed partial class HomeResourceOperationBroker
{
    internal bool IsRetainedOriginalGeneratedUiReview(HomeResourcePreparedReview prepared, HomePermissionTrustService samePermissions) =>
        ReferenceEquals(permissions, samePermissions) && prepared.IssuedBy(this) && _preparedReviews.ContainsKey(prepared) &&
        prepared.Submission.Scope.TargetAppId == "assistants" &&
        HomeCanonicalGeneratedUiInteractionWriteSource.IsSupportedAction(prepared.Submission.Scope.ActionName);
    internal bool MatchesOriginalGeneratedUiReview(HomeResourcePreparedReview prepared, HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest request) =>
        IsRetainedOriginalGeneratedUiReview(prepared, permissions) && prepared.CanonicalAdmission && MatchesPrepared(prepared.Submission, request);

    // The setup claim refuses this call after its capability acquisition begins. The
    // actual permission gate independently refuses a race which already began execution.
    internal Task<bool> WithdrawOriginalGeneratedUiReviewWithinSourceAsync(HomeResourcePreparedReview prepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token, Action<Exception>? retainUnexpectedCallback = null)
    {
        var source = new HomeOwnershipOriginalSourceCallbacks(
            body => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { scope(body); return true; }),
            raw => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { retain(raw); return true; }), retainUnexpectedCallback);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> original;
        lock (_originalGeneratedUiWithdrawalGate)
        {
            _originalGeneratedUiWithdrawals.RemoveAll(value =>
            {
                var driver = value.Driver;
                if (!driver.IsCompletedSuccessfully) return false;
                driver.GetAwaiter().GetResult();
                return value.Sources.Errors.Length == 0;
            });
            if (_originalGeneratedUiWithdrawals.Count >= 256)
                throw new InvalidOperationException("Unresolved original generated interaction withdrawals remain retained.");
            original = Drive(start.Task);
            _originalGeneratedUiWithdrawals.Add((original, source));
        }
        try { source.Run(() =>
        {
            try { retain(original); }
            catch (Exception cause) { retainUnexpectedCallback?.Invoke(cause); throw; }
        }); } catch { /* accepted body owns callback failure */ }
        finally { start.SetResult(); }
        return original;
        async Task<bool> Drive(Task gate)
        {
            await gate.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            if (source.Errors.Length != 0) throw new AggregateException("Original generated interaction withdrawal publication failed.", source.Errors);
            source.Run(() => RequirePrepared(prepared));
            await WaitColdProjectBrokerGateAsync(source, prepared.Gate, token).ConfigureAwait(false);
            try
            {
                source.Run(() => RequirePrepared(prepared));
                if (!IsRetainedOriginalGeneratedUiReview(prepared, permissions))
                    throw new UnauthorizedAccessException("The original generated interaction WRITE review is required.");
                var request = await source.ReadAsync(() => permissions.WithdrawOriginalGeneratedUiReviewWithinSourceAsync(
                    this, prepared, source.Run, source.Retain, token, retainUnexpectedCallback)).ConfigureAwait(false);
                if (request is null) return false;
                if (request.State != HomePermissionRequestState.Cancelled || !MatchesPrepared(prepared.Submission, request))
                    throw new UnauthorizedAccessException("The actual generated interaction withdrawal did not acknowledge its exact original request.");
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
    private readonly object _originalGeneratedUiWithdrawalGate = new();
    private readonly List<(Task Driver, HomeOwnershipOriginalSourceCallbacks Sources)> _originalGeneratedUiWithdrawals = [];
}
