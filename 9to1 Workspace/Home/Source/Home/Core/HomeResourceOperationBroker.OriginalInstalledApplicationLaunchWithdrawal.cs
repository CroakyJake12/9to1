using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed partial class HomeResourceOperationBroker
{
    internal bool IsRetainedOriginalInstalledApplicationLaunchReview(HomeResourcePreparedReview prepared, HomePermissionTrustService samePermissions) =>
        ReferenceEquals(permissions, samePermissions) && prepared.IssuedBy(this) && _preparedReviews.ContainsKey(prepared) &&
        prepared.Submission.Scope.TargetAppId == "home" && prepared.Submission.Scope.ActionName == HomeCanonicalInstalledApplicationLaunchSource.WriteAction;
    internal bool MatchesOriginalInstalledApplicationLaunchReview(HomeResourcePreparedReview prepared,
        HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest request) =>
        IsRetainedOriginalInstalledApplicationLaunchReview(prepared, permissions) && prepared.CanonicalAdmission && MatchesPrepared(prepared.Submission, request);
    internal Task<bool> WithdrawOriginalInstalledApplicationLaunchReviewWithinSourceAsync(HomeResourcePreparedReview prepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        WithdrawOriginalInstalledLaunchReviewCoreAsync(prepared, scope, retain, token);

    private Task<bool> WithdrawOriginalInstalledLaunchReviewCoreAsync(HomeResourcePreparedReview prepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new HomeOwnershipOriginalSourceCallbacks(
            body => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { scope(body); return true; }),
            raw => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { retain(raw); return true; }));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> original;
        lock (_originalInstalledLaunchWithdrawalGate)
        {
            _originalInstalledLaunchWithdrawals.RemoveAll(value =>
            {
                var sameDriver = value.Driver;
                if (!sameDriver.IsCompletedSuccessfully) return false;
                sameDriver.GetAwaiter().GetResult(); return value.Sources.Errors.Length == 0;
            });
            if (_originalInstalledLaunchWithdrawals.Count >= 256)
                throw new InvalidOperationException("Unresolved original installed launch review withdrawals remain retained.");
            original = Drive(start.Task);
            _originalInstalledLaunchWithdrawals.Add((original, source));
        }
        try { source.Run(() => retain(original)); } catch { /* accepted body owns callback failure */ }
        finally { start.SetResult(); }
        return original;
        async Task<bool> Drive(Task gate)
        {
            await gate.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            if (source.Errors.Length != 0) throw new AggregateException("Original installed launch review withdrawal publication failed.", source.Errors);
            source.Run(() => RequirePrepared(prepared));
            await WaitColdProjectBrokerGateAsync(source, prepared.Gate, token).ConfigureAwait(false);
            try
            {
                source.Run(() => RequirePrepared(prepared));
                if (!IsRetainedOriginalInstalledApplicationLaunchReview(prepared, permissions))
                    throw new UnauthorizedAccessException("The original installed application launch review is required.");
                var request = await source.ReadAsync(() => permissions.WithdrawOriginalInstalledLaunchReviewWithinSourceAsync(
                    this, prepared, source.Run, source.Retain, token)).ConfigureAwait(false);
                if (request is null) return false;
                if (request.State != HomePermissionRequestState.Cancelled || !MatchesPrepared(prepared.Submission, request))
                    throw new UnauthorizedAccessException("The actual installed launch review withdrawal did not acknowledge its exact original request.");
                return source.Invoke(() =>
                {
                    if (!_preparedReviews.TryRemove(prepared, out _)) throw new InvalidOperationException("Original installed launch review retirement was not acknowledged.");
                    if (prepared.BoundOnce) _bindings.TryRemove(prepared.RequestId, out _);
                    return true;
                });
            }
            finally { source.Run(() => prepared.Gate.Release()); }
        }
    }
    private readonly object _originalInstalledLaunchWithdrawalGate = new();
    private readonly List<(Task Driver, HomeOwnershipOriginalSourceCallbacks Sources)> _originalInstalledLaunchWithdrawals = [];
}
