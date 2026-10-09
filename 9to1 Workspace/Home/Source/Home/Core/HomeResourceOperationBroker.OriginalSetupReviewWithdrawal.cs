using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed partial class HomeResourceOperationBroker
{
    internal bool IsRetainedOriginalSetupReview(HomeResourcePreparedReview prepared, HomePermissionTrustService samePermissions,
        bool originalMiniOperation = false, bool originalMiniIdentity = false) =>
        ReferenceEquals(permissions, samePermissions) && prepared.IssuedBy(this) && _preparedReviews.ContainsKey(prepared) &&
        (originalMiniIdentity
            ? prepared.Submission.Scope.TargetAppId == "mini-computer" && prepared.Submission.Scope.ActionName == HomeCanonicalMiniComputerCatalogIdentitySource.WriteAction
            : originalMiniOperation
            ? prepared.Submission.Scope.TargetAppId == "mini-computer" && HomeCanonicalMiniComputerOperationSource.SupportsOriginalAction(prepared.Submission.Scope.ActionName)
            : prepared.Submission.Scope.TargetAppId == "assistants" && prepared.Submission.Scope.ActionName == HomeCapabilityCatalogueInitializationWriteSource.WriteAction);
    internal bool MatchesOriginalSetupReview(HomeResourcePreparedReview prepared, HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest request, bool originalMiniOperation = false, bool originalMiniIdentity = false) =>
        IsRetainedOriginalSetupReview(prepared, permissions, originalMiniOperation, originalMiniIdentity) && prepared.CanonicalAdmission && MatchesPrepared(prepared.Submission, request);

    // The setup claim refuses this call after its capability acquisition begins. The
    // actual permission gate independently refuses a race which already began execution.
    internal Task<bool> WithdrawOriginalSetupReviewWithinSourceAsync(HomeResourcePreparedReview prepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        WithdrawOriginalPreparedReviewCoreAsync(prepared, scope, retain, token, false, false);

    internal Task<bool> WithdrawOriginalMiniComputerReviewWithinSourceAsync(HomeResourcePreparedReview prepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        WithdrawOriginalPreparedReviewCoreAsync(prepared, scope, retain, token, true, false);

    internal Task<bool> WithdrawOriginalMiniComputerIdentityReviewWithinSourceAsync(HomeResourcePreparedReview prepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        WithdrawOriginalPreparedReviewCoreAsync(prepared, scope, retain, token, false, true);

    private Task<bool> WithdrawOriginalPreparedReviewCoreAsync(HomeResourcePreparedReview prepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token, bool originalMiniOperation, bool originalMiniIdentity)
    {
        var source = new HomeOwnershipOriginalSourceCallbacks(
            body => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { scope(body); return true; }),
            raw => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { retain(raw); return true; }));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> original;
        lock (_originalSetupWithdrawalGate)
        {
            _originalSetupWithdrawals.RemoveAll(value => value.Driver.IsCompletedSuccessfully && value.Sources.Errors.Length == 0);
            if (_originalSetupWithdrawals.Count >= 256)
                throw new InvalidOperationException("Unresolved original setup withdrawals remain retained.");
            original = Drive(start.Task);
            _originalSetupWithdrawals.Add((original, source));
        }
        try { source.Run(() => retain(original)); } catch { /* accepted body owns callback failure */ }
        finally { start.SetResult(); }
        return original;
        async Task<bool> Drive(Task gate)
        {
            await gate.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            if (source.Errors.Length != 0) throw new AggregateException("Original setup withdrawal publication failed.", source.Errors);
            source.Run(() => RequirePrepared(prepared));
            await WaitColdProjectBrokerGateAsync(source, prepared.Gate, token).ConfigureAwait(false);
            try
            {
                source.Run(() => RequirePrepared(prepared));
                if (!IsRetainedOriginalSetupReview(prepared, permissions, originalMiniOperation, originalMiniIdentity))
                    throw new UnauthorizedAccessException("The original capability setup review is required.");
                var request = await source.ReadAsync(() => permissions.WithdrawOriginalSetupReviewWithinSourceAsync(
                    this, prepared, source.Run, source.Retain, token, originalMiniOperation, originalMiniIdentity)).ConfigureAwait(false);
                if (request is null) return false;
                if (request.State != HomePermissionRequestState.Cancelled || !MatchesPrepared(prepared.Submission, request))
                    throw new UnauthorizedAccessException("The actual setup withdrawal did not acknowledge its exact original request.");
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
    private readonly object _originalSetupWithdrawalGate = new();
    private readonly List<(Task Driver, HomeOwnershipOriginalSourceCallbacks Sources)> _originalSetupWithdrawals = [];
}
