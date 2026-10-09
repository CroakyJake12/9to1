using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Home.PermissionsTrustNotifications;

public sealed partial class HomePermissionTrustService
{
    // The actual protected permission owner reads its own stored request AND audit.
    // A SQL receipt/request identifier alone is descriptive and supplies no grant.
    internal Task<bool> VerifyOriginalGeneratedUiAuditWithinSourceAsync(HomeCanonicalGeneratedUiInteractionWriteSource sameSource,
        CanonicalGeneratedUiOriginalCommitReceipt receipt, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        RunOriginalImportPermissionAsync(scope, retain, async source =>
        {
            source.Run(() =>
            {
                sameSource.DemandUnexpectedOriginalCallbacksHealthy();
                if (!sameSource.HasOriginalAuditPermissionOwner(this)) throw new UnauthorizedAccessException("Use the SAME actual generated interaction Home/permission tuple.");
            });
            await WaitOriginalImportPermissionGateAsync(source, token).ConfigureAwait(false);
            try
            {
                var state = await LoadAsync(token, source).ConfigureAwait(false);
                return source.Invoke(() =>
                {
                    sameSource.DemandUnexpectedOriginalCallbacksHealthy();
                    var request = state.Requests.SingleOrDefault(item => item.RequestId == receipt.HomeApprovalRequestId);
                    return request is not null && sameSource.MatchesOriginalStoredAudit(request, receipt) && state.Audit.Any(audit =>
                        audit.RequestId == request.RequestId && audit.CallerId == request.Caller.CallerId &&
                        audit.TargetAppId == request.Scope.TargetAppId && audit.ActionName == request.Scope.ActionName &&
                        audit.Kind == HomePermissionAuditKind.ExecutionCompleted && audit.RequestState == HomePermissionRequestState.Succeeded &&
                        audit.ResultCode == "HOME_GENUI_INTERACTION_SAVED" && audit.Scope is { } observed &&
                        observed.TargetAppId == request.Scope.TargetAppId && observed.ActionName == request.Scope.ActionName &&
                        observed.IncludesAllObjects == request.Scope.IncludesAllObjects && observed.Objects.SequenceEqual(request.Scope.Objects));
                });
            }
            finally { source.Run(() => _gate.Release()); }
        }, sameSource.RetainUnexpectedOriginalCallback);
}
