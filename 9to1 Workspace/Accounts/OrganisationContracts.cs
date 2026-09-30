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
public sealed record OrganisationAddOn(string AddOnID,int DefinitionVersion,string Currency,decimal MonthlyPrice,int? SeatLimit,
    BusinessBillingState State,DateTimeOffset EffectiveFrom,DateTimeOffset? EffectiveUntil);
public sealed record OrganisationRole(Guid RoleID,string Name,IReadOnlySet<string> Grants,IReadOnlySet<string> Denials,bool IsOwner,long Revision);
public sealed record OrganisationMembership(Guid MembershipID,Guid AccountID,IReadOnlyList<Guid> RoleIDs,OrganisationMemberState State,long Revision);
public sealed record OrganisationPolicy(Guid PolicyID,long Revision,IReadOnlySet<string> BlockedCapabilities,
    IReadOnlyDictionary<string,string> ForcedSettings,IReadOnlyDictionary<string,string> Defaults,string State);
public sealed record Organisation(Guid OrgID,string Name,OrganisationAddOn AddOn,IReadOnlyList<OrganisationMembership> Members,
    IReadOnlyList<OrganisationRole> Roles,OrganisationPolicy Policy,long Revision,int SchemaVersion=1);
public sealed record OrganisationAudit(Guid AuditEventID,Guid OrgID,Guid ActorID,string Action,Guid? TargetID,
    long BeforeRevision,long AfterRevision,DateTimeOffset At,string? ApprovalReference);
public sealed record OrganisationInvitation(Guid InvitationID,Guid OrgID,Guid InviterID,Guid? IntendedAccountID,IReadOnlyList<Guid> RoleIDs,string TokenHash,DateTimeOffset ExpiresAt,bool Accepted);
public sealed record IssuedOrganisationInvitation(Guid InvitationID,string Token);
public sealed record OrganisationState(IReadOnlyList<Organisation> Organisations,IReadOnlyList<OrganisationAudit> Audit,
    IReadOnlyDictionary<string,long> Idempotency,IReadOnlyList<OrganisationInvitation>? Invitations=null);
