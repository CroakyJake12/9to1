using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

/// <summary>Issues definition-only admission from the real SQL root, original actor and one-use Home execution.
/// Persisted owner/graph descriptors and caller implementations cannot issue this sealed admission.
/// This does not authorize graph execution or make SQL and Home one atomic store.</summary>
public sealed class AutomationLocalStoreAuthority(SqliteDatabase database,
    IAuthenticatedResourceActorSource actors, IResourceStoreOwnershipAuthority ownership,
    HomeResourceOperationBroker broker, HomePermissionTrustService permissions)
{
    public async ValueTask<IAutomationDefinitionCommitAdmission?> ClaimAsync(AutomationDefinitionChange change,
        HomeResourceExecutionCapability capability, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change); ArgumentNullException.ThrowIfNull(capability);
        // Canonical graph publication and run admission are separate owning workflows, not implied by this definition receipt.
        // Until the real graph publication/recovery writer is present, no definition claim can make a graph executable.
        if (change.ChangeKind == AutomationDefinitionChangeKind.PublishGraph ||
            change.Automation is { } definition && (definition.IsEnabled || definition.OperationalState == AutomationOperationalState.Ready) ||
            change.ReusableTask is { } task && (task.IsEnabled || task.OperationalState == AutomationOperationalState.Ready))
            return null;
        if (ownership is not IResourceStoreOwnershipReceiptAuthority receipts) return null;
        var actor = await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (actor is null || actor != change.OriginalActor || actor.AccountId is not null || actor.OrganisationId is not null ||
            string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId) ||
            string.IsNullOrWhiteSpace(actor.AuthenticationRevision)) return null;
        var identity = await database.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId != change.StoreID) return null;
        var binding = await ownership.GetVerifiedAsync("automations", change.StoreID.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (binding?.Receipt is null || binding.ResourceKind != "automations" ||
            binding.StoreId != change.StoreID.ToString("D") || binding.ProfileId != actor.ProfileId ||
            !await receipts.IsCurrentAsync(binding, actor, cancellationToken).ConfigureAwait(false)) return null;
        // A fresh resource resolution and exact-argument check are performed by the actual broker before its one-use claim.
        // Reject a session switch before consuming the operation; never adopt a later current actor.
        if (await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != change.OriginalActor) return null;
        var claimed = await broker.ClaimExecutionAsync(capability, AutomationDefinitionChange.TargetAppID,
            change.ActionID, change.Scopes, change.Arguments, cancellationToken).ConfigureAwait(false);
        if (claimed != change.OriginalActor ||
            !await receipts.IsCurrentAsync(binding, change.OriginalActor, cancellationToken).ConfigureAwait(false)) return null;
        var actualOwner = new AutomationOwnerBinding(identity.StoreId, actor.ProfileId, actor.ActorId,
            actor.AuthenticationRevision, actor.AccountId, actor.OrganisationId);
        return new Admission(this, change.StoreID, change.EntityID, change.EntityKind, change.ExpectedRevision,
            change.OperationID, change.PayloadSHA256, change.ActionID, actualOwner, binding, actor,
            capability.RequestId, receipts, actors, permissions, capability);
    }

    /// <summary>SQL must call this in addition to CheckAsync; arbitrary implementations are never owner admission.</summary>
    internal bool Issued(IAutomationDefinitionCommitAdmission admission, ISqliteConnectionFactory factory) =>
        ReferenceEquals(factory, database) && admission is Admission owned && ReferenceEquals(owned.Issuer, this);

    // Dedicated SQL association writers can retain this exact original private claim before acquiring Home.
    // This remains definition admission only; it does not authorize protected graph/run changes.
    internal HomeClaimedResourceAttestation? CaptureAssociationClaim(IAutomationDefinitionCommitAdmission admission,
        ISqliteConnectionFactory actualFactory, AutomationDefinitionCommitContext context) =>
        Issued(admission, actualFactory) && admission is Admission owned && owned.Matches(context)
            ? broker.CaptureClaimedAttestation(owned.Capability) : null;

    // Pure issuer/factory/context check while the SAME Home lease is held. Never calls Home/actor/resolver.
    internal bool MatchesAssociationClaim(IAutomationDefinitionCommitAdmission admission,
        ISqliteConnectionFactory actualFactory, AutomationDefinitionCommitContext context,
        HomeClaimedResourceAttestation originalClaim) =>
        originalClaim is not null && Issued(admission, actualFactory) && admission is Admission owned && owned.Matches(context) &&
        ReferenceEquals(broker.CaptureClaimedAttestation(owned.Capability), originalClaim);

    internal bool IsBoundToHome(HomeResourceOperationBroker originalBroker, IResourceStoreOwnershipAuthority originalOwnership) =>
        ReferenceEquals(broker, originalBroker) && ReferenceEquals(ownership, originalOwnership);
    internal HomeResourceExecutionCapability? CapturePreparedCapability(IAutomationDefinitionCommitAdmission admission,
        ISqliteConnectionFactory actualFactory, AutomationDefinitionCommitContext context) =>
        Issued(admission, actualFactory) && admission is Admission owned && owned.Matches(context) ? owned.Capability : null;

    private sealed class Admission(AutomationLocalStoreAuthority issuer, Guid storeID, Guid entityID,
        AutomationOwnerEntityKind entityKind, long expectedRevision, Guid operationID, string payloadHash,
        string actionID, AutomationOwnerBinding owner, VerifiedResourceStoreOwnership binding,
        AuthenticatedResourceActor originalActor, string requestID, IResourceStoreOwnershipReceiptAuthority receipts,
        IAuthenticatedResourceActorSource actors, HomePermissionTrustService permissions,
        HomeResourceExecutionCapability capability) : IAutomationDefinitionCommitAdmission
    {
        internal AutomationLocalStoreAuthority Issuer => issuer;
        internal HomeResourceExecutionCapability Capability => capability;
        internal bool Matches(AutomationDefinitionCommitContext context) =>
            context.StoreIdentity.SchemaVersion == 1 && context.StoreIdentity.StoreId == storeID &&
            context.EntityID == entityID && context.EntityKind == entityKind && context.ExpectedRevision == expectedRevision &&
            context.OperationID == operationID && context.PayloadSHA256 == payloadHash && context.ActionID == actionID &&
            context.OwnerBinding == owner;
        public async ValueTask<bool> CheckAsync(AutomationDefinitionCommitContext context, CancellationToken cancellationToken)
        {
            if (!Matches(context) || await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != originalActor)
                return false;
            // Only raw Home/actor observations while the SQL lease is held: no repository or resource resolver recursion.
            return await receipts.IsCurrentAsync(binding, originalActor, cancellationToken).ConfigureAwait(false) &&
                await permissions.IsExecutionCurrentAsync(requestID, cancellationToken).ConfigureAwait(false) &&
                await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == originalActor;
        }
    }
}
