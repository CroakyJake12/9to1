using System.Security.Cryptography;
using System.Text;

namespace NineToOne.Accounts;

/// <summary>Server authority for Business membership, roles and capability intersections; public clients supply no role assertions.</summary>
public sealed partial class OrganisationService(string statePath,ProfileService profiles,IOrganisationObjectScopeResolver? objectScopesAuthority=null,TimeProvider? lifecycleTimeProvider=null,Func<CancellationToken,Task>? adminJobCheckpoint=null) : IOrganisationPolicyAuthority,IOrganisationOwningOperationAuthority
{
    private const int MaximumMemberRoleAssignments=128;
    private const string InvitationRevocationAction="Admin.Members.Invitation.Revoke";
    public Organisation CreateTrustedOrganisation(Guid verifiedOwnerID,string name,BusinessAddOnKind kind,string trustedBillingReference)
    {
        if(verifiedOwnerID==Guid.Empty||!Enum.IsDefined(kind)||string.IsNullOrWhiteSpace(trustedBillingReference)||string.IsNullOrWhiteSpace(name))throw new ArgumentException("trusted_billing_or_provisioning_required");
        using var lease=DurableState.Acquire(statePath);var state=Read();var id=Guid.NewGuid();var roleID=Guid.NewGuid();
        var free=profiles.HasAccountBoundFreeBusiness(verifiedOwnerID);
        var addon=new OrganisationAddOn(kind==BusinessAddOnKind.Business?"business":"business-plus",1,"USD",free?0:kind==BusinessAddOnKind.Business?5:15,
            kind==BusinessAddOnKind.Business?50:null,BusinessBillingState.Active,DateTimeOffset.UtcNow,null);
        var org=new Organisation(id,name.Trim(),addon,[new(Guid.NewGuid(),verifiedOwnerID,[roleID],OrganisationMemberState.Active,1)],
            [new(roleID,"Owner",new HashSet<string>{"*"},new HashSet<string>(),true,1)],
            new(Guid.NewGuid(),1,new HashSet<string>(),new Dictionary<string,string>(),new Dictionary<string,string>(),"Published"),1);
        Save(state with {Organisations=state.Organisations.Append(org).ToArray(),Audit=state.Audit.Append(new(Guid.NewGuid(),id,verifiedOwnerID,"OrganisationProvisioned",null,0,1,DateTimeOffset.UtcNow,trustedBillingReference)).ToArray()});return org;
    }
    public IReadOnlyList<Organisation> List(Guid authenticatedAccountID,int offset=0,int limit=50)
    {
        if(offset<0||limit is<1 or>200)throw new ArgumentOutOfRangeException(nameof(limit));
        using var lease=DurableState.Acquire(statePath);
        return Read().Organisations.Where(o=>o.Members.Any(m=>m.AccountID==authenticatedAccountID&&m.State==OrganisationMemberState.Active)).Skip(offset).Take(limit).ToArray();
    }
    public IReadOnlyList<OrganisationAudit> ListAudit(Guid authenticatedAccountID,Guid orgID,int offset=0,int limit=100)
    {
        if(offset<0||limit is <1 or >200)throw new ArgumentOutOfRangeException(nameof(limit));
        using var lease=DurableState.Acquire(statePath);var state=Read();
        var org=state.Organisations.Single(o=>o.OrgID==orgID);Demand(org,authenticatedAccountID,"Admin.Audit.List");
        return state.Audit.Where(e=>e.OrgID==orgID).OrderByDescending(e=>e.At).ThenBy(e=>e.AuditEventID).Skip(offset).Take(limit).ToArray();
    }
    public Organisation Get(Guid accountID,Guid orgID)
    {using var lease=DurableState.Acquire(statePath);var org=Read().Organisations.Single(o=>o.OrgID==orgID);Demand(org,accountID,"Admin.Organisations.Get");return org;}
    public IssuedOrganisationInvitation Invite(Guid actorID,Guid orgID,Guid? intendedAccountID,IReadOnlyList<Guid> roles,DateTimeOffset expiresAt)
    {
        if(intendedAccountID is not { } intended||intended==Guid.Empty)throw new ArgumentException("verified_invitation_identity_required");
        var capturedRoles=CaptureRoleIDs(roles);
        if(expiresAt<=LifecycleNow)throw new ArgumentException("invitation_expiry_required");
        using var lease=DurableState.Acquire(statePath);
        if(expiresAt<=LifecycleNow)throw new UnauthorizedAccessException("invitation_expired_before_admission");
        var state=Read();var org=state.Organisations.Single(o=>o.OrgID==orgID);Demand(org,actorID,"Admin.Members.Invite");ValidateRoles(org,actorID,capturedRoles);
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var invite=new OrganisationInvitation(Guid.NewGuid(),orgID,actorID,intendedAccountID,capturedRoles,Hash(token),expiresAt,false,SchemaVersion:2);
        var invitationRevision=checked(org.InvitationRevision+1);
        var nextOrg=org with {InvitationRevision=invitationRevision};
        Save(state with {Organisations=state.Organisations.Select(item=>item.OrgID==orgID?nextOrg:item).ToArray(),
            Invitations=(state.Invitations??[]).Append(invite).ToArray()});return new(invite.InvitationID,token);
    }
    public OrganisationInvitationRevocationResult RevokeInvitation(Guid actorID,Guid orgID,Guid invitationID,
        long expectedOrganisationRevision,string idempotencyKey,CancellationToken cancellationToken=default)
    {
        ValidateLifecycleRevision(expectedOrganisationRevision);ValidateLifecycleKey(idempotencyKey);
        if(invitationID==Guid.Empty)throw new ArgumentException("canonical_invitation_id_required");
        cancellationToken.ThrowIfCancellationRequested();
        using var lease=DurableState.Acquire(statePath);
        cancellationToken.ThrowIfCancellationRequested();
        var state=Read();var org=state.Organisations.Single(o=>o.OrgID==orgID);
        Demand(org,actorID,"Admin.Members.Invite");
        var invitation=(state.Invitations??[]).SingleOrDefault(item=>item.OrgID==orgID&&item.InvitationID==invitationID)
            ??throw new KeyNotFoundException("organisation_invitation_not_found");
        var scopedKey=orgID.ToString("N")+":"+actorID.ToString("N")+":"+InvitationRevocationAction+":"+idempotencyKey;
        if(state.Idempotency.ContainsKey(scopedKey))
        {
            if(invitation.Revoked&&invitation.RevokedByAccountID==actorID&&
                invitation.RevocationIdempotencyKey==idempotencyKey&&
                invitation.RevocationExpectedOrganisationRevision==expectedOrganisationRevision)
                return new(orgID,invitationID,state.Idempotency[scopedKey],invitation.RevokedAt!.Value,true);
            throw new AdministrationConflictException(AdministrationConflictCode.IdempotencyConflict);
        }
        if(invitation.Revoked||invitation.Accepted)
            throw new AdministrationConflictException(AdministrationConflictCode.InvitationResolved);
        if(org.Revision!=expectedOrganisationRevision)
            throw new AdministrationConflictException(AdministrationConflictCode.RevisionConflict);
        var now=LifecycleNow;var nextRevision=checked(org.Revision+1);
        var nextInvitationRevision=checked(org.InvitationRevision+1);
        var nextOrg=org with {Revision=nextRevision,InvitationRevision=nextInvitationRevision};
        var revoked=invitation with {Revoked=true,RevokedByAccountID=actorID,
            RevocationIdempotencyKey=idempotencyKey,RevocationExpectedOrganisationRevision=expectedOrganisationRevision,RevokedAt=now,SchemaVersion=2};
        var idempotency=new Dictionary<string,long>(state.Idempotency){[scopedKey]=nextRevision};
        cancellationToken.ThrowIfCancellationRequested();
        Save(state with
        {
            Organisations=state.Organisations.Select(item=>item.OrgID==orgID?nextOrg:item).ToArray(),
            Invitations=(state.Invitations??[]).Select(item=>item.InvitationID==invitationID?revoked:item).ToArray(),
            Idempotency=idempotency,
            Audit=state.Audit.Append(new(Guid.NewGuid(),orgID,actorID,InvitationRevocationAction,invitationID,
                org.Revision,nextRevision,now,null)).ToArray()
        });
        return new(orgID,invitationID,nextRevision,now,false);
    }
    public Organisation AcceptInvitation(Guid authenticatedAccountID,string token)
    {
        using var lease=DurableState.Acquire(statePath);var state=Read();var hashed=Hash(token);
        var invite=(state.Invitations??[]).SingleOrDefault(i=>CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(i.TokenHash),Encoding.ASCII.GetBytes(hashed)));
        if(invite is null||invite.Accepted||invite.Revoked||invite.ExpiresAt<=LifecycleNow||
            invite.IntendedAccountID is not { } intended||intended==Guid.Empty||intended!=authenticatedAccountID)
            throw new UnauthorizedAccessException("invalid_invitation");
        var org=state.Organisations.Single(o=>o.OrgID==invite.OrgID);Demand(org,invite.InviterID,"Admin.Members.Invite");ValidateRoles(org,invite.InviterID,invite.RoleIDs);
        var existingMember=org.Members.SingleOrDefault(m=>m.AccountID==authenticatedAccountID);
        if(existingMember is not null&&existingMember.State!=OrganisationMemberState.Removed)throw new InvalidOperationException("member_exists");
        if(org.AddOn.SeatLimit is {} limit&&org.Members.Count(m=>m.State==OrganisationMemberState.Active)>=limit)throw new InvalidOperationException("seat_limit_reached");
        var restoredMember=existingMember is null
            ? new OrganisationMembership(Guid.NewGuid(),authenticatedAccountID,invite.RoleIDs.ToArray(),OrganisationMemberState.Active,1)
            : existingMember with {RoleIDs=invite.RoleIDs.ToArray(),State=OrganisationMemberState.Active,Revision=checked(existingMember.Revision+1)};
        var members=existingMember is null?org.Members.Append(restoredMember).ToArray():org.Members.Select(member=>
            member.MembershipID==existingMember.MembershipID?restoredMember:member).ToArray();
        var invitationRevision=checked(org.InvitationRevision+1);
        var next=org with {Members=members,Revision=checked(org.Revision+1),InvitationRevision=invitationRevision};
        Save(state with {Organisations=state.Organisations.Select(o=>o.OrgID==org.OrgID?next:o).ToArray(),Invitations=(state.Invitations??[]).Select(i=>i.InvitationID==invite.InvitationID?i with {Accepted=true}:i).ToArray(),
            Audit=state.Audit.Append(new(Guid.NewGuid(),org.OrgID,authenticatedAccountID,"InvitationAccepted",authenticatedAccountID,org.Revision,next.Revision,DateTimeOffset.UtcNow,null)).ToArray()});return next;
    }
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public Organisation SetMemberState(Guid actorID,Guid orgID,long expectedRevision,string key,Guid memberID,OrganisationMemberState state)
        =>Mutate(actorID,orgID,expectedRevision,key,"Admin.Members.Update",memberID,org=>
        {
            if(memberID==Guid.Empty||!Enum.IsDefined(state))throw new ArgumentException("valid_member_state_and_identity_required");
            var member=org.Members.SingleOrDefault(m=>m.AccountID==memberID)
                ??throw new KeyNotFoundException("organisation_member_not_found");
            if(state==OrganisationMemberState.Active&&member.State!=state&&org.AddOn.SeatLimit is {} limit&&org.Members.Count(m=>m.State==OrganisationMemberState.Active)>=limit)throw new InvalidOperationException("seat_limit_reached");
            var next=org with {Members=org.Members.Select(m=>m.AccountID==memberID?m with {State=state,Revision=checked(m.Revision+1)}:m).ToArray()};EnsureOwner(next);return next;
        });
    public Organisation CreateRole(Guid actorID,Guid orgID,long expectedRevision,string key,string name,IReadOnlySet<string> grants,IReadOnlySet<string> denials)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>256||name.Any(char.IsControl))throw new ArgumentException("bounded_role_name_required");
        var capturedGrants=CaptureRoleCapabilities(grants);
        var capturedDenials=CaptureRoleCapabilities(denials);
        return Mutate(actorID,orgID,expectedRevision,key,"Admin.Roles.Create",null,org=>
        {
            DemandRoleDelegation(org,actorID,capturedGrants);
            if(capturedGrants.Contains("*"))throw new UnauthorizedAccessException("custom_role_cannot_grant_owner_wildcard");
            return org with {Roles=org.Roles.Append(new(Guid.NewGuid(),name.Trim(),capturedGrants,capturedDenials,false,1)).ToArray()};
        });
    }
    public Organisation SetRoles(Guid actorID,Guid orgID,long expectedRevision,string key,Guid memberID,IReadOnlyList<Guid> roles)
    {
        var capturedRoles=CaptureRoleIDs(roles);
        return Mutate(actorID,orgID,expectedRevision,key,"Admin.Members.SetRoles",memberID,org=>
        {
            if(memberID==Guid.Empty||capturedRoles.Length>MaximumMemberRoleAssignments||capturedRoles.Distinct().Count()!=capturedRoles.Length)
                throw new ArgumentException("bounded_unique_member_role_assignment_required");
            var member=org.Members.SingleOrDefault(m=>m.AccountID==memberID)
                ?? throw new KeyNotFoundException("organisation_member_not_found");
            ValidateRoles(org,actorID,capturedRoles);
            var next=org with {Members=org.Members.Select(m=>m.AccountID==member.AccountID?m with {RoleIDs=capturedRoles,Revision=checked(m.Revision+1)}:m).ToArray()};
            EnsureOwner(next);return next;
        });
    }
    public Organisation PublishPolicy(Guid actorID,Guid orgID,long expectedRevision,string key,IReadOnlySet<string> blocked,
        IReadOnlyDictionary<string,string> forced,IReadOnlyDictionary<string,string> defaults)
        =>Mutate(actorID,orgID,expectedRevision,key,"Admin.Policies.Publish",null,org=>
            org with {Policy=new(org.Policy.PolicyID,checked(org.Policy.Revision+1),blocked.ToHashSet(StringComparer.Ordinal),new Dictionary<string,string>(forced),new Dictionary<string,string>(defaults),"Published")});
    public Organisation PreviewDowngrade(Guid actorID,Guid orgID)
    {using var lease=DurableState.Acquire(statePath);var org=Read().Organisations.Single(o=>o.OrgID==orgID);Demand(org,actorID,"Admin.Billing.GetConfiguration");return org with {AddOn=org.AddOn with {AddOnID="business",MonthlyPrice=5,SeatLimit=50,State=org.Members.Count(m=>m.State==OrganisationMemberState.Active)>50?BusinessBillingState.PendingDowngrade:org.AddOn.State}};}
    public ValueTask<OrganisationPolicyDecision> EvaluateAsync(Guid accountID,Guid orgID,string action,IReadOnlyList<string> objectScopes,long? expectedPolicyRevision,CancellationToken ct) =>
        ValueTask.FromResult(EvaluateCurrent(accountID,orgID,action,objectScopes,expectedPolicyRevision,ct));
    // SAME synchronous canonical evaluation; authenticated Admin transport never starts
    // an unknown async provider under its original session/organisation admission.
    public OrganisationPolicyDecision EvaluateCurrent(Guid accountID,Guid orgID,string action,IReadOnlyList<string> objectScopes,long? expectedPolicyRevision,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();using var lease=DurableState.Acquire(statePath);var org=Read().Organisations.SingleOrDefault(o=>o.OrgID==orgID);
        if(org is null)return Decision(false,"OrganisationNotFound",0);
        if(expectedPolicyRevision is {} expected&&expected!=org.Policy.Revision)return Decision(false,"RevisionConflict",org.Policy.Revision);
        var code=Allowed(org,accountID,action);
        if(code=="Allowed")
        {
            if(objectScopes.Count==0&&!OrganisationGlobalActions.Contains(action))code="ObjectScopeRequired";
            else if(objectScopes.Any(scope=>string.IsNullOrWhiteSpace(scope)||objectScopesAuthority is null||!objectScopesAuthority.Allows(accountID,orgID,action,scope)))code="ObjectScopeDenied";
        }
        return new OrganisationPolicyDecision(code=="Allowed",code,org.Policy.Revision,[],
            new Dictionary<string,string>(org.Policy.ForcedSettings));
    }
    // Internal owning-service boundary: the callback must validate its actual canonical resource belongs to this OrgID.
    // Lock order is session -> organisation -> owning resource. No client-supplied ACL, role or scope assertion is used.
    internal T WithCurrentResourceAuthority<T>(Guid accountID,Guid orgID,string action,long expectedPolicyRevision,Func<Organisation,T> operation)
    {
        using var lease=DurableState.Acquire(statePath);var org=Read().Organisations.Single(o=>o.OrgID==orgID);
        if(org.Policy.Revision!=expectedPolicyRevision)throw new InvalidOperationException("organisation_policy_revision_conflict");
        Demand(org,accountID,action);return operation(org);
    }
    private static readonly IReadOnlySet<string> OrganisationGlobalActions=new HashSet<string>(StringComparer.Ordinal){
        "Admin.Organisations.Get","Admin.Organisations.Update","Admin.Organisations.Archive","Admin.Organisations.Restore","Admin.Organisations.TransferOwnership","Admin.Members.Invite","Admin.Members.List","Admin.Roles.Create","Admin.Roles.List","Admin.Roles.Update","Admin.Roles.Delete","Admin.Roles.GetEffectivePermissions","Admin.Jobs.List","Admin.Jobs.Get","Admin.Jobs.Cancel","Admin.Jobs.ExecuteExport","Admin.Organisations.Export","Admin.Policies.Publish","Admin.Policies.Get","Admin.Policies.Preview","Admin.Billing.GetConfiguration","Admin.Audit.List","Admin.Resources.GetUsage"};
    private static OrganisationPolicyDecision Decision(bool allowed,string code,long revision)=>new(allowed,code,revision,[],new Dictionary<string,string>());
    private static string Allowed(Organisation org,Guid accountID,string action)
    {
        if((org.AddOn.State is BusinessBillingState.Cancelled or BusinessBillingState.Suspended || org.AddOn.EffectiveFrom > DateTimeOffset.UtcNow || org.AddOn.EffectiveUntil is {} until && until <= DateTimeOffset.UtcNow) && !action.StartsWith("Admin.Billing.",StringComparison.Ordinal)&&!action.StartsWith("Admin.Recovery.",StringComparison.Ordinal))return "EntitlementRequired";
        if(org.Lifecycle==OrganisationLifecycleState.Archived&&!ArchivedManagementActions.Contains(action)&&
            !action.StartsWith("Admin.Billing.",StringComparison.Ordinal)&&!action.StartsWith("Admin.Recovery.",StringComparison.Ordinal))return "OrganisationArchived";
        var member=org.Members.SingleOrDefault(m=>m.AccountID==accountID&&m.State==OrganisationMemberState.Active);if(member is null)return "PermissionDenied";
        var roles=org.Roles.Where(r=>member.RoleIDs.Contains(r.RoleID)).ToArray();
        if(org.Policy.BlockedCapabilities.Contains(action)||roles.Any(r=>r.Denials.Contains(action)||r.Denials.Contains("*")))return "PolicyDenied";
        return roles.Any(r=>r.Grants.Contains(action)||r.Grants.Contains("*"))?"Allowed":"PermissionDenied";
    }
    private static void Demand(Organisation org,Guid accountID,string action){var code=Allowed(org,accountID,action);if(code!="Allowed")throw new OrganisationAccessException(code);}
    private static void EnsureOwner(Organisation org)
    {if(!org.Members.Any(m=>m.State==OrganisationMemberState.Active&&org.Roles.Any(r=>r.IsOwner&&m.RoleIDs.Contains(r.RoleID))))throw new InvalidOperationException("last_owner_protected");}
    private static void ValidateRoles(Organisation org,Guid actorID,IReadOnlyList<Guid> roleIDs)
    {
        ArgumentNullException.ThrowIfNull(roleIDs);
        if(roleIDs.Distinct().Count()!=roleIDs.Count)throw new ArgumentException("unique_role_assignments_required");
        foreach(var id in roleIDs){var role=org.Roles.Single(r=>r.RoleID==id);if(role.IsOwner)throw new UnauthorizedAccessException("use_explicit_ownership_transfer");foreach(var grant in role.Grants)Demand(org,actorID,grant);}
    }
    private static Guid[] CaptureRoleIDs(IReadOnlyList<Guid> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var captured=new List<Guid>(Math.Min(roles.Count,MaximumMemberRoleAssignments));
        foreach(var roleID in roles)
        {
            if(captured.Count==MaximumMemberRoleAssignments)throw new ArgumentException("bounded_role_id_list_required");
            captured.Add(roleID);
        }
        return captured.ToArray();
    }
    private Organisation Mutate(Guid actorID,Guid orgID,long expected,string key,string action,Guid? target,Func<Organisation,Organisation> mutation)
    {
        if(string.IsNullOrWhiteSpace(key))throw new ArgumentException("idempotency_key_required");
        using var lease=DurableState.Acquire(statePath);var state=Read();var org=state.Organisations.Single(o=>o.OrgID==orgID);Demand(org,actorID,action);
        var scopedKey=orgID.ToString("N")+":"+actorID.ToString("N")+":"+action+":"+key;
        if(state.Idempotency.ContainsKey(scopedKey))throw new InvalidOperationException("operation_already_applied");
        if(org.Revision!=expected)throw new InvalidOperationException("revision_conflict");
        var next=mutation(org) with {Revision=checked(org.Revision+1)};EnsureOwner(next);
        var keys=new Dictionary<string,long>(state.Idempotency){[scopedKey]=next.Revision};
        Save(state with {Organisations=state.Organisations.Select(o=>o.OrgID==orgID?next:o).ToArray(),Idempotency=keys,
            Audit=state.Audit.Append(new(Guid.NewGuid(),orgID,actorID,action,target,org.Revision,next.Revision,DateTimeOffset.UtcNow,null)).ToArray()});return next;
    }
    private OrganisationState Read()
    {
        var state=File.Exists(statePath)?DurableState.Read<OrganisationState>(statePath):new([],[],new Dictionary<string,long>());
        if(state.Organisations is null||state.Audit is null||state.Idempotency is null||state.Organisations.Any(o=>o is null)||state.Audit.Any(a=>a is null)||state.Organisations.Select(o=>o.OrgID).Distinct().Count()!=state.Organisations.Count)throw new InvalidDataException("corrupt_organisation_state");
        foreach(var org in state.Organisations)
        {
            if(org.SchemaVersion is not (1 or 2)||!Enum.IsDefined(org.Lifecycle)||org.SchemaVersion==1&&org.Lifecycle!=OrganisationLifecycleState.Active||org.OrgID==Guid.Empty||org.Revision<1||org.InvitationRevision<0||org.Members is null||org.Roles is null||org.Members.Any(m=>m is null)||org.Roles.Any(r=>r is null)||org.Policy is null||org.AddOn is null||!Enum.IsDefined(org.AddOn.State))throw new InvalidDataException("corrupt_organisation");
            if(org.AddOn.DefinitionVersion!=1||org.AddOn.Currency!="USD"||org.AddOn.AddOnID is not ("business" or "business-plus")||
                (org.AddOn.AddOnID=="business"?org.AddOn.SeatLimit!=50:org.AddOn.SeatLimit is not null)||
                org.AddOn.EffectiveFrom==default||org.AddOn.EffectiveUntil is {} end&&end<=org.AddOn.EffectiveFrom||
                org.AddOn.MonthlyPrice<0||org.AddOn.MonthlyPrice!=(org.AddOn.AddOnID=="business"?5m:15m)&&org.AddOn.MonthlyPrice!=0)
                throw new InvalidDataException("corrupt_organisation_addon");
            if(org.Members.Any(m=>m.MembershipID==Guid.Empty||m.AccountID==Guid.Empty||m.Revision<1||m.RoleIDs is null||!Enum.IsDefined(m.State)||m.RoleIDs.Any(id=>!org.Roles.Any(r=>r.RoleID==id)))||org.Roles.Any(r=>r.RoleID==Guid.Empty||r.Revision<1||r.Grants is null||r.Denials is null||r.Grants.Any(string.IsNullOrWhiteSpace)||r.Denials.Any(string.IsNullOrWhiteSpace))||org.Policy.BlockedCapabilities is null||org.Policy.ForcedSettings is null||org.Policy.Defaults is null)throw new InvalidDataException("corrupt_organisation_membership");
            if(org.AddOn.MonthlyPrice==0&&!org.Members.Any(m=>m.State==OrganisationMemberState.Active&&profiles.HasAccountBoundFreeBusiness(m.AccountID)&&m.RoleIDs.Any(id=>org.Roles.Any(r=>r.RoleID==id&&r.IsOwner))))throw new InvalidDataException("unbound_free_business_grant");
            if(org.Policy.PolicyID==Guid.Empty||org.Policy.Revision<1||org.Policy.State is not ("Draft" or "Validated" or "Published")||!org.Members.Any(m=>m.State==OrganisationMemberState.Active&&m.RoleIDs.Any(id=>org.Roles.Any(r=>r.RoleID==id&&r.IsOwner))))throw new InvalidDataException("corrupt_organisation_policy_or_owner");
            if(org.Members.Select(m=>m.AccountID).Distinct().Count()!=org.Members.Count||org.Roles.Select(r=>r.RoleID).Distinct().Count()!=org.Roles.Count)throw new InvalidDataException("duplicate_organisation_identity");
        }
        if(state.BillingTransitions is {} transitions && (transitions.Any(t=>t is null||string.IsNullOrWhiteSpace(t.EventID)||t.AccountID==Guid.Empty||t.ExpectedRevision<1||
            !Enum.IsDefined(t.State)||t.EffectiveFrom==default||t.EffectiveUntil is {} end&&end<=t.EffectiveFrom||string.IsNullOrWhiteSpace(t.PolicyVersion)||
            !state.Organisations.Any(o=>o.OrgID==t.OrgID)||t.DefinitionVersion!=1||t.AddOnID is not ("business" or "business-plus"))||
            transitions.Select(t=>t.EventID).Distinct(StringComparer.Ordinal).Count()!=transitions.Count))throw new InvalidDataException("corrupt_business_billing_history");
        ValidateInvitations(state);
        ValidateOwnershipTransfers(state);ValidateLifecycleReceipts(state);ValidateAdministrationState(state);
        return state;
    }
    private static void ValidateInvitations(OrganisationState state)
    {
        var invitations=state.Invitations??[];
        if(invitations.Any(invitation=>invitation is null)||
            invitations.Select(invitation=>invitation.InvitationID).Distinct().Count()!=invitations.Count)
            throw new InvalidDataException("corrupt_organisation_invitation_identity");
        foreach(var invitation in invitations)
        {
            var org=state.Organisations.SingleOrDefault(item=>item.OrgID==invitation.OrgID);
            var noRevocationData=invitation.RevokedByAccountID is null&&invitation.RevocationIdempotencyKey is null&&
                invitation.RevocationExpectedOrganisationRevision is null&&invitation.RevokedAt is null;
            var revocationValid=false;
            if(invitation.SchemaVersion is 0 or 1)
                revocationValid=!invitation.Revoked&&noRevocationData;
            else if(invitation.SchemaVersion==2)
            {
                if(!invitation.Revoked)revocationValid=noRevocationData;
                else if(!invitation.Accepted&&invitation.RevokedByAccountID is { } actor&&actor!=Guid.Empty&&
                    !string.IsNullOrWhiteSpace(invitation.RevocationIdempotencyKey)&&invitation.RevocationIdempotencyKey.Length<=256&&
                    !invitation.RevocationIdempotencyKey.Any(char.IsControl)&&
                    invitation.RevocationExpectedOrganisationRevision is { } expected&&expected>=1&&expected<long.MaxValue&&
                    org is not null&&expected<org.Revision&&invitation.RevokedAt is { } revokedAt&&revokedAt!=default&&
                    org.Members.Any(member=>member.AccountID==actor))
                {
                    var scopedKey=org!.OrgID.ToString("N")+":"+actor.ToString("N")+":"+
                        InvitationRevocationAction+":"+invitation.RevocationIdempotencyKey;
                    revocationValid=state.Idempotency.TryGetValue(scopedKey,out var recordedRevision)&&recordedRevision==expected+1;
                }
            }
            if(org is null||invitation.InvitationID==Guid.Empty||invitation.InviterID==Guid.Empty||
                invitation.RoleIDs is null||string.IsNullOrWhiteSpace(invitation.TokenHash)||!revocationValid)
                throw new InvalidDataException("corrupt_organisation_invitation_binding");
        }
    }
    private void Save(OrganisationState state){var admission=administrationAdmission.Value;admission?.Recheck();Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);admission?.Recheck();DurableState.Write(statePath,state);if(admission is not null)admission.Committed=true;}
}
