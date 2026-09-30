using System.Security.Cryptography;
using System.Text;

namespace NineToOne.Accounts;

/// <summary>Server authority for Business membership, roles and capability intersections; public clients supply no role assertions.</summary>
public sealed class OrganisationService(string statePath,ProfileService profiles,IOrganisationObjectScopeResolver? objectScopesAuthority=null) : IOrganisationPolicyAuthority
{
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
        if(expiresAt<=DateTimeOffset.UtcNow)throw new ArgumentException("invitation_expiry_required");
        using var lease=DurableState.Acquire(statePath);var state=Read();var org=state.Organisations.Single(o=>o.OrgID==orgID);Demand(org,actorID,"Admin.Members.Invite");ValidateRoles(org,actorID,roles);
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var invite=new OrganisationInvitation(Guid.NewGuid(),orgID,actorID,intendedAccountID,roles.ToArray(),Hash(token),expiresAt,false);
        Save(state with {Invitations=(state.Invitations??[]).Append(invite).ToArray()});return new(invite.InvitationID,token);
    }
    public Organisation AcceptInvitation(Guid authenticatedAccountID,string token)
    {
        using var lease=DurableState.Acquire(statePath);var state=Read();var hashed=Hash(token);
        var invite=(state.Invitations??[]).SingleOrDefault(i=>CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(i.TokenHash),Encoding.ASCII.GetBytes(hashed)));
        if(invite is null||invite.Accepted||invite.ExpiresAt<=DateTimeOffset.UtcNow||invite.IntendedAccountID is {} intended&&intended!=authenticatedAccountID)throw new UnauthorizedAccessException("invalid_invitation");
        var org=state.Organisations.Single(o=>o.OrgID==invite.OrgID);Demand(org,invite.InviterID,"Admin.Members.Invite");ValidateRoles(org,invite.InviterID,invite.RoleIDs);
        if(org.Members.Any(m=>m.AccountID==authenticatedAccountID&&m.State!=OrganisationMemberState.Removed))throw new InvalidOperationException("member_exists");
        if(org.AddOn.SeatLimit is {} limit&&org.Members.Count(m=>m.State==OrganisationMemberState.Active)>=limit)throw new InvalidOperationException("seat_limit_reached");
        var next=org with {Members=org.Members.Append(new(Guid.NewGuid(),authenticatedAccountID,invite.RoleIDs,OrganisationMemberState.Active,1)).ToArray(),Revision=org.Revision+1};
        Save(state with {Organisations=state.Organisations.Select(o=>o.OrgID==org.OrgID?next:o).ToArray(),Invitations=(state.Invitations??[]).Select(i=>i.InvitationID==invite.InvitationID?i with {Accepted=true}:i).ToArray(),
            Audit=state.Audit.Append(new(Guid.NewGuid(),org.OrgID,authenticatedAccountID,"InvitationAccepted",authenticatedAccountID,org.Revision,next.Revision,DateTimeOffset.UtcNow,null)).ToArray()});return next;
    }
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public Organisation SetMemberState(Guid actorID,Guid orgID,long expectedRevision,string key,Guid memberID,OrganisationMemberState state)
        =>Mutate(actorID,orgID,expectedRevision,key,"Admin.Members.Update",memberID,org=>
        {
            var member=org.Members.Single(m=>m.AccountID==memberID);
            if(state==OrganisationMemberState.Active&&member.State!=state&&org.AddOn.SeatLimit is {} limit&&org.Members.Count(m=>m.State==OrganisationMemberState.Active)>=limit)throw new InvalidOperationException("seat_limit_reached");
            var next=org with {Members=org.Members.Select(m=>m.AccountID==memberID?m with {State=state,Revision=m.Revision+1}:m).ToArray()};EnsureOwner(next);return next;
        });
    public Organisation CreateRole(Guid actorID,Guid orgID,long expectedRevision,string key,string name,IReadOnlySet<string> grants,IReadOnlySet<string> denials)
        =>Mutate(actorID,orgID,expectedRevision,key,"Admin.Roles.Create",null,org=>
        {
            foreach(var grant in grants)Demand(org,actorID,grant);
            if(grants.Contains("*"))throw new UnauthorizedAccessException("custom_role_cannot_grant_owner_wildcard");
            return org with {Roles=org.Roles.Append(new(Guid.NewGuid(),name,grants.ToHashSet(StringComparer.Ordinal),denials.ToHashSet(StringComparer.Ordinal),false,1)).ToArray()};
        });
    public Organisation SetRoles(Guid actorID,Guid orgID,long expectedRevision,string key,Guid memberID,IReadOnlyList<Guid> roles)
        =>Mutate(actorID,orgID,expectedRevision,key,"Admin.Members.SetRoles",memberID,org=>
        {ValidateRoles(org,actorID,roles);var next=org with {Members=org.Members.Select(m=>m.AccountID==memberID?m with {RoleIDs=roles.ToArray(),Revision=m.Revision+1}:m).ToArray()};EnsureOwner(next);return next;});
    public Organisation PublishPolicy(Guid actorID,Guid orgID,long expectedRevision,string key,IReadOnlySet<string> blocked,
        IReadOnlyDictionary<string,string> forced,IReadOnlyDictionary<string,string> defaults)
        =>Mutate(actorID,orgID,expectedRevision,key,"Admin.Policies.Publish",null,org=>
            org with {Policy=new(org.Policy.PolicyID,org.Policy.Revision+1,blocked.ToHashSet(StringComparer.Ordinal),new Dictionary<string,string>(forced),new Dictionary<string,string>(defaults),"Published")});
    public Organisation PreviewDowngrade(Guid actorID,Guid orgID)
    {var org=Get(actorID,orgID);Demand(org,actorID,"Admin.Billing.GetConfiguration");return org with {AddOn=org.AddOn with {AddOnID="business",MonthlyPrice=5,SeatLimit=50,State=org.Members.Count(m=>m.State==OrganisationMemberState.Active)>50?BusinessBillingState.PendingDowngrade:org.AddOn.State}};}
    public ValueTask<OrganisationPolicyDecision> EvaluateAsync(Guid accountID,Guid orgID,string action,IReadOnlyList<string> objectScopes,long? expectedPolicyRevision,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();using var lease=DurableState.Acquire(statePath);var org=Read().Organisations.SingleOrDefault(o=>o.OrgID==orgID);
        if(org is null)return ValueTask.FromResult(Decision(false,"OrganisationNotFound",0));
        if(expectedPolicyRevision is {} expected&&expected!=org.Policy.Revision)return ValueTask.FromResult(Decision(false,"RevisionConflict",org.Policy.Revision));
        var code=Allowed(org,accountID,action);
        if(code=="Allowed")
        {
            if(objectScopes.Count==0&&!OrganisationGlobalActions.Contains(action))code="ObjectScopeRequired";
            else if(objectScopes.Any(scope=>string.IsNullOrWhiteSpace(scope)||objectScopesAuthority is null||!objectScopesAuthority.Allows(accountID,orgID,action,scope)))code="ObjectScopeDenied";
        }
        return ValueTask.FromResult(new OrganisationPolicyDecision(code=="Allowed",code,org.Policy.Revision,[],
            new Dictionary<string,string>(org.Policy.ForcedSettings)));
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
        "Admin.Organisations.Get","Admin.Organisations.Update","Admin.Members.Invite","Admin.Members.List","Admin.Roles.Create","Admin.Roles.List","Admin.Policies.Publish","Admin.Policies.Get","Admin.Policies.Preview","Admin.Billing.GetConfiguration","Admin.Audit.List","Admin.Resources.GetUsage"};
    private static OrganisationPolicyDecision Decision(bool allowed,string code,long revision)=>new(allowed,code,revision,[],new Dictionary<string,string>());
    private static string Allowed(Organisation org,Guid accountID,string action)
    {
        if(org.AddOn.State is BusinessBillingState.Cancelled or BusinessBillingState.Suspended && !action.StartsWith("Admin.Billing.",StringComparison.Ordinal)&&!action.StartsWith("Admin.Recovery.",StringComparison.Ordinal))return "EntitlementRequired";
        var member=org.Members.SingleOrDefault(m=>m.AccountID==accountID&&m.State==OrganisationMemberState.Active);if(member is null)return "PermissionDenied";
        var roles=org.Roles.Where(r=>member.RoleIDs.Contains(r.RoleID)).ToArray();
        if(org.Policy.BlockedCapabilities.Contains(action)||roles.Any(r=>r.Denials.Contains(action)||r.Denials.Contains("*")))return "PolicyDenied";
        return roles.Any(r=>r.Grants.Contains(action)||r.Grants.Contains("*"))?"Allowed":"PermissionDenied";
    }
    private static void Demand(Organisation org,Guid accountID,string action){if(Allowed(org,accountID,action)!="Allowed")throw new UnauthorizedAccessException("organisation_permission_denied");}
    private static void EnsureOwner(Organisation org)
    {if(!org.Members.Any(m=>m.State==OrganisationMemberState.Active&&org.Roles.Any(r=>r.IsOwner&&m.RoleIDs.Contains(r.RoleID))))throw new InvalidOperationException("last_owner_protected");}
    private static void ValidateRoles(Organisation org,Guid actorID,IReadOnlyList<Guid> roleIDs)
    {
        foreach(var id in roleIDs){var role=org.Roles.Single(r=>r.RoleID==id);if(role.IsOwner)throw new UnauthorizedAccessException("use_explicit_ownership_transfer");foreach(var grant in role.Grants)Demand(org,actorID,grant);}
    }
    private Organisation Mutate(Guid actorID,Guid orgID,long expected,string key,string action,Guid? target,Func<Organisation,Organisation> mutation)
    {
        if(string.IsNullOrWhiteSpace(key))throw new ArgumentException("idempotency_key_required");
        using var lease=DurableState.Acquire(statePath);var state=Read();var org=state.Organisations.Single(o=>o.OrgID==orgID);Demand(org,actorID,action);
        var scopedKey=orgID.ToString("N")+":"+actorID.ToString("N")+":"+action+":"+key;
        if(state.Idempotency.ContainsKey(scopedKey))throw new InvalidOperationException("operation_already_applied");
        if(org.Revision!=expected)throw new InvalidOperationException("revision_conflict");
        var next=mutation(org) with {Revision=org.Revision+1};EnsureOwner(next);
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
            if(org.SchemaVersion!=1||org.OrgID==Guid.Empty||org.Revision<1||org.Members is null||org.Roles is null||org.Members.Any(m=>m is null)||org.Roles.Any(r=>r is null)||org.Policy is null||org.AddOn is null||!Enum.IsDefined(org.AddOn.State))throw new InvalidDataException("corrupt_organisation");
            if(org.AddOn.DefinitionVersion!=1||org.AddOn.Currency!="USD"||org.AddOn.AddOnID is not ("business" or "business-plus")||
                (org.AddOn.AddOnID=="business"?org.AddOn.SeatLimit!=50:org.AddOn.SeatLimit is not null)||
                org.AddOn.MonthlyPrice<0||org.AddOn.MonthlyPrice!=(org.AddOn.AddOnID=="business"?5m:15m)&&org.AddOn.MonthlyPrice!=0)
                throw new InvalidDataException("corrupt_organisation_addon");
            if(org.Members.Any(m=>m.MembershipID==Guid.Empty||m.AccountID==Guid.Empty||m.Revision<1||m.RoleIDs is null||!Enum.IsDefined(m.State)||m.RoleIDs.Any(id=>!org.Roles.Any(r=>r.RoleID==id)))||org.Roles.Any(r=>r.RoleID==Guid.Empty||r.Revision<1||r.Grants is null||r.Denials is null||r.Grants.Any(string.IsNullOrWhiteSpace)||r.Denials.Any(string.IsNullOrWhiteSpace))||org.Policy.BlockedCapabilities is null||org.Policy.ForcedSettings is null||org.Policy.Defaults is null)throw new InvalidDataException("corrupt_organisation_membership");
            if(org.AddOn.MonthlyPrice==0&&!org.Members.Any(m=>m.State==OrganisationMemberState.Active&&profiles.HasAccountBoundFreeBusiness(m.AccountID)&&m.RoleIDs.Any(id=>org.Roles.Any(r=>r.RoleID==id&&r.IsOwner))))throw new InvalidDataException("unbound_free_business_grant");
            if(org.Policy.PolicyID==Guid.Empty||org.Policy.Revision<1||org.Policy.State is not ("Draft" or "Validated" or "Published")||!org.Members.Any(m=>m.State==OrganisationMemberState.Active&&m.RoleIDs.Any(id=>org.Roles.Any(r=>r.RoleID==id&&r.IsOwner))))throw new InvalidDataException("corrupt_organisation_policy_or_owner");
            if(org.Members.Select(m=>m.AccountID).Distinct().Count()!=org.Members.Count||org.Roles.Select(r=>r.RoleID).Distinct().Count()!=org.Roles.Count)throw new InvalidDataException("duplicate_organisation_identity");
        }
        if(state.Invitations is {} invitations && invitations.Any(i=>i is null||i.InvitationID==Guid.Empty||i.InviterID==Guid.Empty||i.RoleIDs is null||string.IsNullOrWhiteSpace(i.TokenHash)||!state.Organisations.Any(o=>o.OrgID==i.OrgID)))throw new InvalidDataException("corrupt_organisation_invitation");
        return state;
    }
    private void Save(OrganisationState state){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);DurableState.Write(statePath,state);}
}
