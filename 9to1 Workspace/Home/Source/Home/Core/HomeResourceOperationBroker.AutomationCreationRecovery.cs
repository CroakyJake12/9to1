using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Application.NodeGraph;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Home.Core;

public sealed partial class HomeCanonicalAutomationDefinitionWriteSource
{
    internal sealed class AutomationCreationAuditProof(HomeCanonicalAutomationDefinitionWriteSource issuer,
        object sameClaim, ICanonicalAutomationDefinitionOriginalChangeAcknowledgment acknowledgment,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> atomic, CanonicalAutomationOriginalCreationAuditEvidence evidence)
    {
        internal readonly HomeCanonicalAutomationDefinitionWriteSource Issuer = issuer;
        internal readonly object Claim = sameClaim;
        internal readonly ICanonicalAutomationDefinitionOriginalChangeAcknowledgment Acknowledgment = acknowledgment;
        internal readonly Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> Atomic = atomic;
        internal readonly CanonicalAutomationOriginalCreationAuditEvidence Evidence = evidence;
    }
    private readonly ConditionalWeakTable<AutomationCreationAuditProof, AutomationCreationAuditProof> _creationAuditProofs = new();
    private AutomationCreationAuditProof CaptureOriginalAutomationCreationAuditProof(Claim claim,
        ICanonicalAutomationDefinitionOriginalChangeAcknowledgment acknowledgment,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> atomic)
    {
        if (!ReferenceEquals(Require(claim), claim) || claim.Intent.ChangeKind != CanonicalAutomationOriginalChangeKind.RecoverCreated ||
            !_creator.IsOwnedOriginalChangeAcknowledgment(claim.Intent, acknowledgment, atomic) ||
            !_creator.IsOwnedOriginalChangeNativeRelease(claim.Intent, atomic) ||
            _creator is not ICanonicalAutomationDefinitionOriginalCreationAuditSource source ||
            source.ObserveOriginalCreationAudit(claim.Intent, acknowledgment, atomic) is not { } evidence)
            throw new UnauthorizedAccessException("The actual original SQL receipt, exact fresh recovery claim and native release proof are required.");
        var result = new AutomationCreationAuditProof(this, claim, acknowledgment, atomic, evidence);
        _creationAuditProofs.Add(result, result); return result;
    }
    internal async Task<bool> ValidateOriginalAutomationCreationAuditProofAsync(AutomationCreationAuditProof proof,
        HomeResourceOperationBroker broker, HomeResourceExecutionCapability recovery, AuthenticatedResourceActor actor, CancellationToken token)
    {
        if (!ReferenceEquals(_broker, broker) || !ReferenceEquals(proof.Issuer, this) ||
            !_creationAuditProofs.TryGetValue(proof, out var issued) || !ReferenceEquals(proof, issued) ||
            proof.Claim is not Claim claim || !ReferenceEquals(Require(claim), claim) ||
            !ReferenceEquals(claim.CreationAuditCapability, recovery) || claim.Actor != actor || claim.Intent.ActionId != CreationRecoveryAction ||
            !recovery.IsUncompletedClaim(_broker) || !_creator.IsOwnedOriginalChangeNativeRelease(claim.Intent, proof.Atomic) ||
            !_creator.IsOwnedOriginalChangeAcknowledgment(claim.Intent, proof.Acknowledgment, proof.Atomic) ||
            _creator is not ICanonicalAutomationDefinitionOriginalCreationAuditSource source ||
            source.ObserveOriginalCreationAudit(claim.Intent, proof.Acknowledgment, proof.Atomic) != proof.Evidence) return false;
        // No Home/native locks remain held. Re-observe the physical row/receipt and
        // current owner; this cannot insert, rewrite a descriptor or lend the old grant.
        await _creator.RevalidateOriginalChangeIntentWithinSourceAsync(claim.Intent, body => body(), _ => { }, token).ConfigureAwait(false);
        var read = await _store.ReadAsync(token).ConfigureAwait(false);
        return read.IsSuccess && await _profiles.GetCurrentAsync(token).ConfigureAwait(false) == actor &&
            claim.CreationAuditOwnership is { } ownership && HomeLocalStoreOwnership.IsReceiptCurrentInState(read.State!, ownership) &&
            claim.CreationAuditAttestation is { } attestation && _broker.IsClaimedAttestationCurrentInState(attestation, actor, read.State!) &&
            _creationGraphs?.IsIntentGraphCurrentInState(claim.Intent, read.State!) == true;
    }
}
public sealed partial class HomeResourceOperationBroker
{
    private static bool SameCreationPrincipal(AuthenticatedResourceActor left, AuthenticatedResourceActor right) =>
        left.ProfileId == right.ProfileId && left.ActorId == right.ActorId && left.AccountId == right.AccountId && left.OrganisationId == right.OrganisationId;
    // Read-only proof of the actual original graph request and guarded publication.
    // Executing means its audit is still pending; this read grants no completion right.
    internal async Task<bool> IsOriginalAutomationCreationGraphPublicationAsync(OwnedGraphDefinition graph,
        AuthenticatedResourceActor actor, CancellationToken token)
    {
        var receipt = graph.LastPublication; var origin = graph.CreationOrigin;
        if (receipt?.OriginalRequestId is not { Length: > 0 } requestId || origin is null) return false;
        var request = await permissions.ReadRequestObservationAsync(requestId, token).ConfigureAwait(false);
        if (request is null || request.RequestId != requestId || request.Scope.TargetAppId != "automations" ||
            request.Scope.ActionName != "automations.graph.save-draft" || !request.Policy.RequiresPerActionApproval ||
            request.Impact.ArgumentsDigest != receipt.ArgumentsDigest || request.Impact.ResourceBinding is not { } binding ||
            binding.OriginalActor != origin.OriginalActor || !SameCreationPrincipal(binding.OriginalActor, actor) || binding.Scopes.Count != 2 ||
            request.State is not (HomePermissionRequestState.Executing or HomePermissionRequestState.Succeeded)) return false;
        var ownerScope = binding.Scopes.SingleOrDefault(scope => scope.Kind == "automation.definition");
        var graphScope = binding.Scopes.SingleOrDefault(scope => scope.Kind == "nodegraph.definition");
        return ownerScope is not null && ownerScope.Id == origin.StoreId.ToString("D") + "/" + origin.AutomationId.ToString("D") &&
            ownerScope.Revision == origin.OriginalAbsenceSha256 && ownerScope.Access == ResourceAccess.Write &&
            graphScope is not null && graphScope.Id == actor.ProfileId + "/" + graph.Draft.GraphId.ToString("D") &&
            graphScope.Revision == "0" && graphScope.Access == ResourceAccess.Write &&
            (request.State != HomePermissionRequestState.Succeeded || request.ResultCode == "HOME_GRAPH_PUBLISHED");
    }
    internal async Task<bool> IsOriginalAutomationCreationGraphAuditRecordedAsync(OwnedGraphDefinition graph, CancellationToken token)
    {
        if (graph.LastPublication?.OriginalRequestId is not { Length: > 0 } id) return false;
        var request = await permissions.ReadRequestObservationAsync(id, token).ConfigureAwait(false);
        return request?.State == HomePermissionRequestState.Succeeded && request.ResultCode == "HOME_GRAPH_PUBLISHED";
    }
    internal async Task<HomePermissionOperationResult> ReconcileOriginalAutomationCreationAuditAsync(
        HomeResourceExecutionCapability freshRecovery, HomeCanonicalAutomationDefinitionWriteSource.AutomationCreationAuditProof proof,
        CancellationToken token)
    {
        HomePermissionOperationResult Pending(string reason) => new(false, "AUTOMATION_CREATION_AUDIT_PENDING", reason);
        if (!freshRecovery.IsUncompletedClaim(this) || !_executions.TryGetValue(freshRecovery, out var recovery) ||
            recovery.TargetAppId != "automations" || recovery.ActionId != "automations.create.recover" ||
            recovery.OriginalPolicy?.RequiresPerActionApproval != true ||
            !await proof.Issuer.ValidateOriginalAutomationCreationAuditProofAsync(proof, this, freshRecovery, recovery.Actor, token).ConfigureAwait(false))
            return Pending("A fresh individual recovery claim and sealed same-composition current SQL receipt proof are required.");
        var evidence = proof.Evidence; var receipt = evidence.OriginalReceipt;
        var original = await permissions.ReadRequestObservationAsync(evidence.OriginalRequestId, token).ConfigureAwait(false);
        if (original is null || original.RequestId != evidence.OriginalRequestId || original.RequestId == freshRecovery.RequestId ||
            original.Scope.TargetAppId != "automations" || original.Scope.ActionName != "automations.create" ||
            !original.Policy.RequiresPerActionApproval || original.Impact.ArgumentsDigest != evidence.OriginalArgumentsSha256 ||
            original.Impact.ResourceBinding is not { } binding || binding.Scopes.Count != 1 ||
            !SameCreationPrincipal(binding.OriginalActor, recovery.Actor) || receipt.ObservedOwner.ProfileId != binding.OriginalActor.ProfileId ||
            receipt.ObservedOwner.ActorId != binding.OriginalActor.ActorId || receipt.ObservedOwner.AuthenticationRevision != binding.OriginalActor.AuthenticationRevision ||
            receipt.ObservedOwner.AccountId != binding.OriginalActor.AccountId || receipt.ObservedOwner.OrganisationId != binding.OriginalActor.OrganisationId ||
            receipt.SchemaVersion != 1 || receipt.ExpectedRevision != 0 || receipt.CommittedRevision != 1 ||
            binding.Scopes[0] != new ResourceScope("automation.definition", receipt.StoreId.ToString("D") + ":" + receipt.EntityId.ToString("N") + ":" +
                receipt.OperationId.ToString("N"), evidence.OriginalArgumentsSha256, ResourceAccess.Write))
            return Pending("The original Home create request/digest/current ownership does not match the actual original SQL receipt.");
        if (original.State == HomePermissionRequestState.Succeeded)
            return original.ResultCode == "HOME_AUTOMATION_DEFINITION_CREATED_DISABLED"
                ? new(true, "AUTOMATION_CREATION_AUDIT_ALREADY_RECORDED", "The actual original creation audit is already acknowledged.")
                : Pending("The original terminal audit has another outcome and was preserved.");
        if (original.State != HomePermissionRequestState.Executing)
            return Pending("The original request is not an unfinished execution. No terminal decision was overwritten.");
        if (!await proof.Issuer.ValidateOriginalAutomationCreationAuditProofAsync(proof, this, freshRecovery, recovery.Actor, token).ConfigureAwait(false))
            return Pending("Current native receipt or Home ownership changed before audit reconciliation.");
        // Audit only. No old capability is reconstructed, no SQL grant is issued,
        // and no unrelated request can be completed through this method.
        return await permissions.RecordExecutionAsync(original.RequestId,
            new(HomePermissionRequestState.Succeeded, "HOME_AUTOMATION_DEFINITION_CREATED_DISABLED",
                "The exact atomic maintained-definition acknowledgment was observed; original cleanup and audit are retained independently.", []), token).ConfigureAwait(false);
    }
}
