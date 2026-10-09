using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed partial class HomeCanonicalGeneratedUiInteractionWriteSource
{
    public Task<bool> VerifyOriginalStoredAuditWithinSourceAsync(CanonicalGeneratedUiOriginalCommitReceipt receipt,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        VerifyOriginalStoredAuditGuardedWithinSourceAsync(receipt, scope, retain, token);
    private async Task<bool> VerifyOriginalStoredAuditGuardedWithinSourceAsync(CanonicalGeneratedUiOriginalCommitReceipt receipt,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        DemandUnexpectedOriginalCallbacksHealthy();
        var actual = _permissions.VerifyOriginalGeneratedUiAuditWithinSourceAsync(this, receipt, scope, retain, token);
        var result = await actual.ConfigureAwait(false);
        DemandUnexpectedOriginalCallbacksHealthy(); return result;
    }
    internal bool HasOriginalAuditPermissionOwner(HomePermissionTrustService same) => ReferenceEquals(_permissions, same);
    internal bool MatchesOriginalStoredAudit(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest request,
        CanonicalGeneratedUiOriginalCommitReceipt receipt)
    {
        var actor = receipt.ObservedActor;
        var expected = new ResourceScope(ResourceKind, receipt.StoreId.ToString("D") + ":" + receipt.MessageId.ToString("N") + ":" +
            receipt.TemplateOrdinal.ToString() + ":" + receipt.OperationId.ToString("N"), receipt.HomeArgumentsSha256, ResourceAccess.Write);
        return receipt.SchemaVersion == 1 && receipt.StoreId != Guid.Empty && receipt.OperationId != Guid.Empty &&
            request.RequestId == receipt.HomeApprovalRequestId && request.Caller.IsVerified && request.Caller.CallerId == actor.ActorId &&
            request.Caller.Origin == actor.ProfileId && request.Caller.IdentityVersion == actor.AuthenticationRevision &&
            request.Scope.TargetAppId == "assistants" && request.Scope.ActionName == SaveAction && !request.Scope.IncludesAllObjects &&
            request.State == HomePermissionRequestState.Succeeded && request.ResultCode == "HOME_GENUI_INTERACTION_SAVED" &&
            request.Policy.RequiresPerActionApproval && request.Impact.ArgumentsDigest == receipt.HomeArgumentsSha256 &&
            request.Impact.ResourceBinding is { SchemaVersion: 1 } binding && binding.OriginalActor == actor &&
            binding.Scopes.Count == 1 && binding.Scopes[0] == expected && request.Scope.Objects.Count == 1 &&
            request.Scope.Objects[0] == new HomeObjectReference(expected.Kind, expected.Id);
    }
}
