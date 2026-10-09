using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;
public sealed partial class HomeResourceOperationBroker
{
    internal bool IsRetainedOriginalAttachmentEgress(HomeResourcePreparedReview prepared, HomePermissionTrustService actualPermissions) =>
        ReferenceEquals(permissions, actualPermissions) && prepared.IssuedBy(this) && _preparedReviews.ContainsKey(prepared) &&
        prepared.Submission.Scope.TargetAppId == "assistants" && prepared.Submission.Scope.ActionName == HomeCanonicalAssistantAttachmentEgressSource.DiscloseAction;
    internal bool MatchesOriginalAttachmentEgress(HomeResourcePreparedReview prepared, HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest request) =>
        IsRetainedOriginalAttachmentEgress(prepared, permissions) && prepared.CanonicalAdmission && MatchesPrepared(prepared.Submission, request);
    internal Task<bool> WithdrawOriginalAttachmentEgressWithinSourceAsync(HomeResourcePreparedReview prepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => RunColdProjectBrokerAsync(scope, retain, async source =>
    {
        source.Run(() => RequirePrepared(prepared));
        await WaitColdProjectBrokerGateAsync(source, prepared.Gate, token).ConfigureAwait(false);
        try
        {
            source.Run(() => { RequirePrepared(prepared); if (!IsRetainedOriginalAttachmentEgress(prepared, permissions))
                throw new UnauthorizedAccessException("The SAME original attachment disclosure review is required."); });
            var request = await source.ReadAsync(() => permissions.WithdrawOriginalAttachmentEgressWithinSourceAsync(this,
                prepared, source.Run, source.Retain, token)).ConfigureAwait(false);
            if (request is null) return false; // An approved/claimed operation remains with its original producer.
            if (request.State != HomePermissionRequestState.Cancelled || !MatchesPrepared(prepared.Submission, request))
                throw new UnauthorizedAccessException("The actual attachment disclosure withdrawal did not acknowledge its exact request.");
            return source.Invoke(() =>
            {
                if (!_preparedReviews.TryRemove(prepared, out _)) throw new InvalidOperationException("The original disclosure review retirement was not acknowledged.");
                if (prepared.BoundOnce) _bindings.TryRemove(prepared.RequestId, out _);
                return true;
            });
        }
        finally { source.Run(() => prepared.Gate.Release()); }
    });
}
