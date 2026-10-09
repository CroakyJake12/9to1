using HavenOS.Home.Core;

namespace HavenOS.Home.PermissionsTrustNotifications;

public sealed partial class HomePermissionTrustService
{
    // Called only by the SAME broker while holding its issuer-owned prepared handle.
    // This is a finite no-effect withdrawal, never an approval or execution grant.
    internal Task<HomePermissionRequest?> WithdrawOriginalSetupReviewWithinSourceAsync(
        HomeResourceOperationBroker sameBroker, HomeResourcePreparedReview samePrepared,
        Action<Action> scope, Action<Task> retain, CancellationToken token, bool originalMiniOperation = false, bool originalMiniIdentity = false) =>
        RunOriginalImportPermissionAsync(scope, retain, async source =>
        {
            if (!sameBroker.IsRetainedOriginalSetupReview(samePrepared, this, originalMiniOperation, originalMiniIdentity))
                throw new UnauthorizedAccessException("The SAME original broker/prepared setup review is required.");
            await WaitOriginalImportPermissionGateAsync(source, token).ConfigureAwait(false);
            try
            {
                var state = await LoadAsync(token, source).ConfigureAwait(false);
                var request = state.Requests.SingleOrDefault(value => value.RequestId == samePrepared.RequestId);
                if (request is null || !sameBroker.MatchesOriginalSetupReview(samePrepared, request, originalMiniOperation, originalMiniIdentity))
                    throw new UnauthorizedAccessException("The stored setup request does not match its original submission.");
                if (request.State != HomePermissionRequestState.PendingApproval)
                    return null; // Approved/executing/unknown requests settle through their SAME producer.
                request = request with { State = HomePermissionRequestState.Cancelled,
                    ResultCode = originalMiniIdentity ? "HOME_MINI_COMPUTER_IDENTITY_REVIEW_WITHDRAWN" : originalMiniOperation ? "HOME_MINI_COMPUTER_REVIEW_WITHDRAWN" : "HOME_CAPABILITY_SETUP_REVIEW_WITHDRAWN",
                    ResultMessage = originalMiniIdentity ? "The owning process withdrew this exact pending catalogue identity review before mutation admission; no catalogue file was written." : originalMiniOperation ? "The owning process withdrew this exact pending Mini Computer review before provider admission; no VM operation was dispatched."
                        : "The owning process withdrew this original setup before execution; no catalogue mutation was dispatched." };
                ReplaceRequest(state, request);
                AddAudit(state, request, HomePermissionAuditKind.DecisionMade, request.State,
                    request.ResultCode, request.ResultMessage, _timeProvider.GetUtcNow());
                await SaveAsync(state, token, source).ConfigureAwait(false);
                return request;
            }
            finally { source.Run(() => _gate.Release()); }
        });
}
