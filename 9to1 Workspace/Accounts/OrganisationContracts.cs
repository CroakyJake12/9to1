namespace NineToOne.Accounts;

public sealed record OrganisationPolicyDecision(bool Allowed,string Code,long PolicyRevision,
    IReadOnlyList<string> RequiredApprovals,IReadOnlyDictionary<string,string> Constraints);
public interface IOrganisationObjectScopeResolver
{
    // Implemented by owning canonical resource services; unknown or foreign resources fail closed.
    bool Allows(Guid authenticatedAccountID,Guid organisationID,string capabilityOrAction,string canonicalObjectScope);
}
public interface IOrganisationPolicyAuthority
{
    ValueTask<OrganisationPolicyDecision> EvaluateAsync(Guid authenticatedAccountId,Guid organisationId,
        string capabilityOrAction,IReadOnlyList<string> objectScopes,long? expectedPolicyRevision,CancellationToken cancellationToken);
}
public enum BusinessAddOnKind { Business, BusinessPlus }
public enum BusinessBillingState { Active, PendingDowngrade, Suspended, Cancelled }
public enum OrganisationMemberState { Active, Suspended, Removed }
public enum OrganisationLifecycleState { Active, Archived }
public enum OrganisationOwnershipTransferState { Pending, Accepted, Cancelled }
public sealed record OrganisationAddOn(string AddOnID,int DefinitionVersion,string Currency,decimal MonthlyPrice,int? SeatLimit,
    BusinessBillingState State,DateTimeOffset EffectiveFrom,DateTimeOffset? EffectiveUntil);
public sealed record OrganisationRole(Guid RoleID,string Name,IReadOnlySet<string> Grants,IReadOnlySet<string> Denials,bool IsOwner,long Revision);
public sealed record OrganisationMembership(Guid MembershipID,Guid AccountID,IReadOnlyList<Guid> RoleIDs,OrganisationMemberState State,long Revision);
public sealed record OrganisationPolicy(Guid PolicyID,long Revision,IReadOnlySet<string> BlockedCapabilities,
    IReadOnlyDictionary<string,string> ForcedSettings,IReadOnlyDictionary<string,string> Defaults,string State);
public sealed record Organisation(Guid OrgID,string Name,OrganisationAddOn AddOn,IReadOnlyList<OrganisationMembership> Members,
    IReadOnlyList<OrganisationRole> Roles,OrganisationPolicy Policy,long Revision,int SchemaVersion=2,OrganisationLifecycleState Lifecycle=OrganisationLifecycleState.Active);
public sealed record OrganisationAudit(Guid AuditEventID,Guid OrgID,Guid ActorID,string Action,Guid? TargetID,
    long BeforeRevision,long AfterRevision,DateTimeOffset At,string? ApprovalReference);
public sealed record OrganisationInvitation(Guid InvitationID,Guid OrgID,Guid InviterID,Guid? IntendedAccountID,IReadOnlyList<Guid> RoleIDs,string TokenHash,DateTimeOffset ExpiresAt,bool Accepted);
public sealed record IssuedOrganisationInvitation(Guid InvitationID,string Token);
public sealed record OrganisationState(IReadOnlyList<Organisation> Organisations,IReadOnlyList<OrganisationAudit> Audit,
    IReadOnlyDictionary<string,long> Idempotency,IReadOnlyList<OrganisationInvitation>? Invitations=null,IReadOnlyList<VerifiedBusinessBillingTransition>? BillingTransitions=null,IReadOnlyList<OrganisationOwnershipTransfer>? OwnershipTransfers=null,IReadOnlyList<OrganisationLifecycleReceipt>? LifecycleReceipts=null,IReadOnlyList<OrganisationRoleMutationReceipt>? RoleMutationReceipts=null,IReadOnlyList<AdminJob>? AdminJobs=null);

public sealed class OrganisationAccessException(string code) : UnauthorizedAccessException(code)
{
    public string Code { get; } = code;
}

/// <summary>Account IDs come from current server authentication; this record is not a bearer grant.</summary>
public sealed record OrganisationOwnershipTransfer(Guid TransferID,Guid OrgID,Guid InitiatorAccountID,Guid RecipientAccountID,
    long OrganisationRevision,long Revision,DateTimeOffset ExpiresAt,OrganisationOwnershipTransferState State,
    string RequestIdempotencyKey,long? AcceptedOrganisationRevision=null,string? AcceptedIdempotencyKey=null);
public sealed record OrganisationOwnershipTransferResult(Organisation Organisation,OrganisationOwnershipTransfer Transfer);

/// <summary>Durable bounded request identity and original effect, not a grant to repeat a mutation.</summary>
public sealed record OrganisationLifecycleReceipt(Guid OrgID,Guid ActorID,string Action,string IdempotencyKey,
    long ExpectedRevision,long ResultingRevision,string? Name,OrganisationLifecycleState? Lifecycle);
/// <summary>Organisation is current; Receipt records the original effect even after later changes.</summary>
public sealed record OrganisationLifecycleResult(Organisation Organisation,OrganisationLifecycleReceipt Receipt,bool Replayed);

/// <summary>The owning caller supplies an AccountID derived from current verified session authority.
/// Admission runs synchronously under the current organisation lock, before the owning resource lock.
/// Remote work must be prepared outside and its actual effects report partial outcomes independently.</summary>
public interface IOrganisationOwningOperationAuthority
{
    T WithCurrentAuthority<T>(Guid authenticatedAccountID,Guid organisationID,string capabilityOrAction,
        IReadOnlyList<string> canonicalObjectScopes,long expectedPolicyRevision,Func<T> admitOwnedOperation,
        CancellationToken cancellationToken);
}
