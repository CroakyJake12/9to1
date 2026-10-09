using HavenOS.Home.Core;
namespace HavenOS.Home.PermissionsTrustNotifications;
public sealed partial class HomePermissionTrustService
{
    internal Task<HomePermissionRequest?> WithdrawOriginalAttachmentReadWithinSourceAsync(HomeResourceOperationBroker actualBroker,
        HomeResourcePreparedReview actualPrepared, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RunOriginalImportPermissionAsync(scope, retain, async source =>
        {
            if (!actualBroker.IsRetainedOriginalAttachmentRead(actualPrepared, this))
                throw new UnauthorizedAccessException("The SAME broker-issued attachment READ review is required.");
            await WaitOriginalImportPermissionGateAsync(source, token).ConfigureAwait(false);
            try
            {
                var state = await LoadAsync(token, source).ConfigureAwait(false);
                var request = state.Requests.SingleOrDefault(value => value.RequestId == actualPrepared.RequestId);
                if (request is null || !actualBroker.MatchesOriginalAttachmentRead(actualPrepared, request))
                    throw new UnauthorizedAccessException("The stored attachment READ request does not match its original submission.");
                if (request.State != HomePermissionRequestState.PendingApproval) return null;
                request = request with { State = HomePermissionRequestState.Cancelled, ResultCode = "HOME_ATTACHMENT_READ_WITHDRAWN",
                    ResultMessage = "The owning process withdrew this pending attachment read before content access." };
                ReplaceRequest(state, request);
                AddAudit(state, request, HomePermissionAuditKind.DecisionMade, request.State, request.ResultCode, request.ResultMessage, _timeProvider.GetUtcNow());
                await SaveAsync(state, token, source).ConfigureAwait(false); return request;
            }
            finally { source.Run(() => _gate.Release()); }
        });
}
