using HavenOS.Home.Core;
namespace HavenOS.Home.PermissionsTrustNotifications;
public sealed partial class HomePermissionTrustService
{
    internal Task<HomePermissionRequest?> WithdrawOriginalAttachmentEgressWithinSourceAsync(HomeResourceOperationBroker actualBroker,
        HomeResourcePreparedReview actualPrepared, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RunOriginalImportPermissionAsync(scope, retain, async source =>
        {
            if (!actualBroker.IsRetainedOriginalAttachmentEgress(actualPrepared, this))
                throw new UnauthorizedAccessException("The SAME broker-issued attachment disclosure review is required.");
            await WaitOriginalImportPermissionGateAsync(source, token).ConfigureAwait(false);
            try
            {
                var state = await LoadAsync(token, source).ConfigureAwait(false);
                var request = state.Requests.SingleOrDefault(value => value.RequestId == actualPrepared.RequestId);
                if (request is null || !actualBroker.MatchesOriginalAttachmentEgress(actualPrepared, request))
                    throw new UnauthorizedAccessException("The stored attachment disclosure request does not match its original submission.");
                if (request.State != HomePermissionRequestState.PendingApproval) return null;
                request = request with { State = HomePermissionRequestState.Cancelled, ResultCode = "HOME_ATTACHMENT_DISCLOSE_WITHDRAWN",
                    ResultMessage = "The owning process withdrew this pending attachment disclosure before remote disclosure." };
                ReplaceRequest(state, request);
                AddAudit(state, request, HomePermissionAuditKind.DecisionMade, request.State, request.ResultCode, request.ResultMessage, _timeProvider.GetUtcNow());
                await SaveAsync(state, token, source).ConfigureAwait(false); return request;
            }
            finally { source.Run(() => _gate.Release()); }
        });
}
