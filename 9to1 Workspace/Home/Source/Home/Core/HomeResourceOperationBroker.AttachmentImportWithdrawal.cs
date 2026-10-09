using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
namespace HavenOS.Home.Core;
public sealed partial class HomeResourceOperationBroker
{
    internal bool IsRetainedOriginalAttachmentImport(HomeResourcePreparedReview prepared, HomePermissionTrustService actualPermissions) =>
        ReferenceEquals(permissions, actualPermissions) && prepared.IssuedBy(this) && _preparedReviews.ContainsKey(prepared) &&
        prepared.Submission.Scope.TargetAppId == "assistants" && HomeCanonicalAssistantAttachmentImportSource.SupportsOriginalAction(prepared.Submission.Scope.ActionName);
    internal bool MatchesOriginalAttachmentImport(HomeResourcePreparedReview prepared, HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest request) =>
        IsRetainedOriginalAttachmentImport(prepared, permissions) && prepared.CanonicalAdmission && MatchesPrepared(prepared.Submission, request);
    internal Task<bool> WithdrawOriginalAttachmentImportWithinSourceAsync(HomeResourcePreparedReview prepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => RunColdProjectBrokerAsync(scope, retain, async source =>
    {
        source.Run(() => RequirePrepared(prepared));
        await WaitColdProjectBrokerGateAsync(source, prepared.Gate, token).ConfigureAwait(false);
        try
        {
            source.Run(() => { RequirePrepared(prepared); if (!IsRetainedOriginalAttachmentImport(prepared, permissions))
                throw new UnauthorizedAccessException("The SAME original attachment WRITE review is required."); });
            var request = await source.ReadAsync(() => permissions.WithdrawOriginalAttachmentImportWithinSourceAsync(this,
                prepared, source.Run, source.Retain, token)).ConfigureAwait(false);
            if (request is null) return false; // An approved/claimed operation remains with its original producer.
            if (request.State != HomePermissionRequestState.Cancelled || !MatchesPrepared(prepared.Submission, request))
                throw new UnauthorizedAccessException("The actual attachment WRITE withdrawal did not acknowledge its exact request.");
            return source.Invoke(() =>
            {
                if (!_preparedReviews.TryRemove(prepared, out _)) throw new InvalidOperationException("The original WRITE review retirement was not acknowledged.");
                if (prepared.BoundOnce) _bindings.TryRemove(prepared.RequestId, out _);
                return true;
            });
        }
        finally { source.Run(() => prepared.Gate.Release()); }
    });
}
