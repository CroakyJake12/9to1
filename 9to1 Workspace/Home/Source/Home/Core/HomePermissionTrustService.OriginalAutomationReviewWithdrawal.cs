using HavenOS.Home.Core;

namespace HavenOS.Home.PermissionsTrustNotifications;

public sealed partial class HomePermissionTrustService
{
    // Called only by the SAME broker while holding its issuer-owned prepared handle.
    // This is a finite no-effect withdrawal, never an approval or execution grant.
    internal Task<HomePermissionRequest?> WithdrawOriginalAutomationReviewWithinSourceAsync(
        HomeResourceOperationBroker sameBroker, HomeResourcePreparedReview samePrepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RunOriginalImportPermissionAsync(scope, retain, async source =>
        {
            if (!sameBroker.IsRetainedOriginalAutomationReview(samePrepared, this))
                throw new UnauthorizedAccessException("The SAME original broker/prepared setup review is required.");
            await WaitOriginalImportPermissionGateAsync(source, token).ConfigureAwait(false);
            try
            {
                var state = await LoadAsync(token, source).ConfigureAwait(false);
                var request = state.Requests.SingleOrDefault(value => value.RequestId == samePrepared.RequestId);
                if (request is null || !sameBroker.MatchesOriginalAutomationReview(samePrepared, request))
                    throw new UnauthorizedAccessException("The stored setup request does not match its original submission.");
                if (request.State != HomePermissionRequestState.PendingApproval)
                    return null; // Approved/executing/unknown requests settle through their SAME producer.
                request = request with { State = HomePermissionRequestState.Cancelled,
                    ResultCode = "HOME_AUTOMATION_REVIEW_WITHDRAWN",
                    ResultMessage = "The owning process withdrew this original setup before execution; no automation row mutation was dispatched." };
                ReplaceRequest(state, request);
                AddAudit(state, request, HomePermissionAuditKind.DecisionMade, request.State,
                    request.ResultCode, request.ResultMessage, _timeProvider.GetUtcNow());
                await SaveAsync(state, token, source).ConfigureAwait(false);
                return request;
            }
            finally { source.Run(() => _gate.Release()); }
        });
}
