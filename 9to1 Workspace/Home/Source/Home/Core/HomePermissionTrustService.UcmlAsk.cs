using HavenOS.Home.Core;

namespace HavenOS.Home.PermissionsTrustNotifications;

public sealed partial class HomePermissionTrustService
{
    internal Task<bool> IsOriginalUcmlCallerAllowedWithinSourceAsync(HomeUcmlContextApprovalOwner owner,
        string callerId, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RunOriginalImportPermissionAsync(scope, retain, async source =>
        {
            if (!owner.HasOriginalPermissionOwner(this)) throw new UnauthorizedAccessException("Use the original Home UCML approval owner.");
            await WaitOriginalImportPermissionGateAsync(source, token).ConfigureAwait(false);
            try
            {
                var state = await LoadAsync(token, source).ConfigureAwait(false);
                return !state.BlockedCallerIds.Contains(callerId, StringComparer.Ordinal);
            }
            finally { source.Run(() => _gate.Release()); }
        });

    internal Task<bool> VerifyCompletedOriginalUcmlAskWithinSourceAsync(HomeUcmlContextApprovalOwner owner,
        string requestId, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RunOriginalImportPermissionAsync(scope, retain, async source =>
        {
            if (!owner.HasOriginalPermissionOwner(this)) throw new UnauthorizedAccessException("Use the original Home UCML approval owner.");
            await WaitOriginalImportPermissionGateAsync(source, token).ConfigureAwait(false);
            try
            {
                var state = await LoadAsync(token, source).ConfigureAwait(false);
                var request = state.Requests.SingleOrDefault(value => value.RequestId == requestId);
                return request is not null && owner.MatchesOriginalRequest(request) &&
                    !state.BlockedCallerIds.Contains(request.Caller.CallerId, StringComparer.Ordinal) &&
                    request.State == HomePermissionRequestState.Succeeded && request.ResultCode == "HOME_UCML_SELECTION_ACCEPTED" &&
                    request.AppliedTrustLevel == HomeTrustLevel.Session;
            }
            finally { source.Run(() => _gate.Release()); }
        });

    internal Task<bool> WithdrawOriginalUcmlAskWithinSourceAsync(HomeUcmlContextApprovalOwner owner,
        string requestId, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RunOriginalImportPermissionAsync(scope, retain, async source =>
        {
            if (!owner.HasOriginalPermissionOwner(this)) throw new UnauthorizedAccessException("Use the original Home UCML approval owner.");
            await WaitOriginalImportPermissionGateAsync(source, token).ConfigureAwait(false);
            try
            {
                var state = await LoadAsync(token, source).ConfigureAwait(false);
                var request = state.Requests.SingleOrDefault(value => value.RequestId == requestId);
                if (request is null) return true; // Failed before request persistence: no grant exists.
                if (!owner.MatchesOriginalRequest(request)) throw new UnauthorizedAccessException("The original UCML approval request was substituted.");
                if (request.State is not (HomePermissionRequestState.PendingApproval or HomePermissionRequestState.Approved or HomePermissionRequestState.Executing)) return true;
                request = request with { State = HomePermissionRequestState.Cancelled, ResultCode = "HOME_UCML_SELECTION_WITHDRAWN",
                    ResultMessage = "The owning local UCML request expired or ended before disclosure. A new request needs new approval." };
                ReplaceRequest(state, request);
                AddAudit(state, request, HomePermissionAuditKind.DecisionMade, request.State, request.ResultCode, request.ResultMessage, _timeProvider.GetUtcNow());
                await SaveAsync(state, token, source).ConfigureAwait(false); return true;
            }
            finally { source.Run(() => _gate.Release()); }
        });
}
