namespace NineToOne.Accounts;

public enum OrganisationRolloverChoice { Inherit, Allow, Disallow }
/// <summary>A trusted versioned billing-policy decision, not client-selected commercial limits.</summary>
public sealed record VerifiedOrganisationRolloverGrant(Guid GrantID,string BillingPolicyID,long BillingPolicyVersion,
    Guid AccountID,Guid OrgID,Guid SourcePoolID,Guid TargetPoolID,Guid SourceAllocationID,Guid RoleID,
    long MaximumDust,DateTimeOffset EligibleUntil,DateTimeOffset ResultingExpiry,bool InheritedAllow,
    string CarryoverWindowID,long OrganisationPolicyRevision,long OrganisationRevision,bool AllowPreviouslyRolledOver=false);
public interface IOrganisationRolloverPolicyAuthority
{
    ValueTask<VerifiedOrganisationRolloverGrant?> ResolveAsync(string approvedPolicyReference,CancellationToken ct);
}
public sealed record OrganisationRolloverTransfer(Guid GrantID,string OperationID,Guid ActorAccountID,Guid OrgID,
    Guid SourcePoolID,Guid TargetPoolID,Guid SourceAllocationID,Guid ChildAllocationID,Guid OriginalSourceAllocationID,
    Guid RoleID,long Dust,string SourcePeriodID,string TargetPeriodID,DateTimeOffset OriginalExpiry,
    DateTimeOffset ResultingExpiry,string BillingPolicyID,long BillingPolicyVersion,DateTimeOffset At,VerifiedOrganisationRolloverGrant ApprovedGrant);

public sealed record OrganisationMemberRolloverCredit(Guid AllocationID,Guid OriginalSourceAllocationID,Guid RoleID,
    OrganisationRolloverChoice CurrentRoleSetting,bool AllowedToUse,long AllocatedDust,long AvailableDust,
    DateTimeOffset OriginalExpiry,DateTimeOffset EffectiveExpiry,string BillingPolicyID,long BillingPolicyVersion,DateTimeOffset ObservedAt);

public sealed partial class OrganisationDustPools
{
    public OrganisationDustPool ConfigureRolloverRules(string token,Guid orgID,long policyRevision,Guid poolID,
        long expectedRevision,IReadOnlyDictionary<Guid,OrganisationRolloverChoice> choices)
    {
        var snapshot=choices.ToDictionary(p=>p.Key,p=>p.Value);
        return Current(token,orgID,policyRevision,"Admin.Resources.ConfigureRollover",(_,org)=>
        {
            if(snapshot.Any(p=>!Enum.IsDefined(p.Value)||!org.Roles.Any(r=>r.RoleID==p.Key)))throw new ArgumentException("invalid_rollover_roles");
            using var lease=DurableState.Acquire(statePath);var state=Read();var pool=Pool(state,poolID,org.OrgID);
            if(pool.Revision!=expectedRevision)throw new InvalidOperationException("pool_revision_conflict");
            var next=pool with{RolloverRules=snapshot,Revision=checked(pool.Revision+1)};
            Write(state with{Pools=state.Pools.Select(p=>p.PoolID==poolID?next:p).ToArray()});return next;
        });
    }

    /// <summary>Moves only uncommitted organisation-owned units to a separately declared next-period pool. Original lots and all charges remain intact.</summary>
    public async ValueTask<OrganisationRolloverTransfer> RollOverAsync(string token,Guid orgID,long policyRevision,
        string approvedPolicyReference,string operationID,CancellationToken ct=default)
    {
        if(rolloverPolicies is null)throw new InvalidOperationException("versioned_billing_rollover_policy_unconfigured");
        if(string.IsNullOrWhiteSpace(operationID))throw new ArgumentException("rollover_operation_required");
        var grant=await rolloverPolicies.ResolveAsync(approvedPolicyReference,ct).ConfigureAwait(false)??throw new UnauthorizedAccessException("rollover_policy_unverified");
        ct.ThrowIfCancellationRequested();ValidateGrant(grant);
        if(grant.OrgID!=orgID)throw new UnauthorizedAccessException("rollover_policy_owner_mismatch");
        return Current(token,orgID,policyRevision,"Admin.Resources.RollOver",(session,org)=>
        {
            using var lease=DurableState.Acquire(statePath);var state=Read();var now=clock.GetUtcNow();
            if(grant.AccountID!=session.AccountID)throw new UnauthorizedAccessException("rollover_policy_actor_mismatch");
            var history=state.RolloverTransfers??Array.Empty<OrganisationRolloverTransfer>();
            var prior=history.SingleOrDefault(t=>t.GrantID==grant.GrantID||t.OrgID==orgID&&t.OperationID==operationID);
            if(prior is not null)
            {
                if(prior.ApprovedGrant!=grant||prior.GrantID!=grant.GrantID||prior.OperationID!=operationID||prior.ActorAccountID!=session.AccountID||prior.SourcePoolID!=grant.SourcePoolID||prior.TargetPoolID!=grant.TargetPoolID||prior.SourceAllocationID!=grant.SourceAllocationID||prior.RoleID!=grant.RoleID||prior.ResultingExpiry!=grant.ResultingExpiry||prior.BillingPolicyID!=grant.BillingPolicyID||prior.BillingPolicyVersion!=grant.BillingPolicyVersion||prior.Dust>grant.MaximumDust)throw new InvalidOperationException("rollover_replay_conflict");
                return prior;
            }
            if(grant.OrganisationPolicyRevision!=org.Policy.Revision||grant.OrganisationRevision!=org.Revision)throw new UnauthorizedAccessException("rollover_authority_changed");
            if(now>=grant.EligibleUntil||now>=grant.ResultingExpiry)throw new InvalidOperationException("rollover_policy_window_expired");
            var sourcePool=Pool(state,grant.SourcePoolID,org.OrgID);var targetPool=Pool(state,grant.TargetPoolID,org.OrgID);
            if(sourcePool.PoolID==targetPool.PoolID||sourcePool.PeriodID==targetPool.PeriodID)throw new InvalidOperationException("rollover_requires_distinct_period_pool");
            if(!org.Roles.Any(r=>r.RoleID==grant.RoleID))throw new UnauthorizedAccessException("rollover_role_unavailable");
            var sourceChoice=sourcePool.RolloverRules?.GetValueOrDefault(grant.RoleID)??OrganisationRolloverChoice.Inherit;
            if(sourceChoice==OrganisationRolloverChoice.Disallow||sourceChoice==OrganisationRolloverChoice.Inherit&&!grant.InheritedAllow)throw new UnauthorizedAccessException("role_rollover_disallowed");
            var source=state.Lots.SingleOrDefault(l=>l.SourceAllocationID==grant.SourceAllocationID&&l.OrgID==orgID&&state.LotPools[l.SourceAllocationID]==sourcePool.PoolID)??throw new UnauthorizedAccessException("rollover_source_not_in_pool");
            var preceding=history.SingleOrDefault(t=>t.ChildAllocationID==source.SourceAllocationID);
            if(preceding is not null&&!grant.AllowPreviouslyRolledOver)throw new InvalidOperationException("successive_rollover_disallowed_by_billing_policy");
            var originalSource=preceding?.OriginalSourceAllocationID??source.SourceAllocationID;
            var originalExpiry=preceding?.OriginalExpiry??source.ExpiresAt;
            // Reserved and dispatched units remain charged against their original lot, including uncertain provider outcomes.
            var sameWindow=history.Where(t=>t.OrgID==orgID&&t.BillingPolicyID==grant.BillingPolicyID&&t.BillingPolicyVersion==grant.BillingPolicyVersion&&t.RoleID==grant.RoleID&&t.ApprovedGrant.CarryoverWindowID==grant.CarryoverWindowID).ToArray();
            if(sameWindow.Any(t=>t.ApprovedGrant.MaximumDust!=grant.MaximumDust))throw new InvalidOperationException("billing_policy_cap_version_conflict");
            var remainingCap=grant.MaximumDust-SumCapacity(sameWindow.Select(t=>t.Dust));
            if(remainingCap<=0)throw new InvalidOperationException("billing_policy_carryover_cap_exhausted");
            var amount=Math.Min(remainingCap,source.Dust-UsedFromLot(state,source.SourceAllocationID));
            if(amount<=0)throw new InvalidOperationException("rollover_has_no_uncommitted_units");
            _=AddCapacity(SumCapacity(state.Lots.Where(l=>state.LotPools[l.SourceAllocationID]==targetPool.PoolID).Select(l=>l.Dust)),amount);
            var child=new VerifiedOrganisationAllocation(Guid.NewGuid(),orgID,targetPool.PeriodID,amount,now,grant.ResultingExpiry,"rollover:"+grant.GrantID.ToString("N"));
            var transfer=new OrganisationRolloverTransfer(grant.GrantID,operationID,session.AccountID,orgID,sourcePool.PoolID,targetPool.PoolID,
                source.SourceAllocationID,child.SourceAllocationID,originalSource,grant.RoleID,amount,source.PeriodID,targetPool.PeriodID,
                originalExpiry,grant.ResultingExpiry,grant.BillingPolicyID,grant.BillingPolicyVersion,now,grant);
            var bindings=state.LotPools.ToDictionary(p=>p.Key,p=>p.Value);bindings.Add(child.SourceAllocationID,targetPool.PoolID);
            Write(state with{Lots=state.Lots.Append(child).ToArray(),LotPools=bindings,RolloverTransfers=history.Append(transfer).ToArray()});return transfer;
        });
    }

    public IReadOnlyList<OrganisationRolloverTransfer> ListRollover(string token,Guid orgID,long policyRevision,Guid poolID)
        =>Current(token,orgID,policyRevision,"Admin.Resources.GetUsage",(_,org)=>
        {
            using var lease=DurableState.Acquire(statePath);var state=Read();var selectedPool=Pool(state,poolID,org.OrgID);
            return Array.AsReadOnly((state.RolloverTransfers??Array.Empty<OrganisationRolloverTransfer>()).Where(t=>t.OrgID==org.OrgID&&(t.SourcePoolID==poolID||t.TargetPoolID==poolID)).ToArray());
        });

    public IReadOnlyList<OrganisationMemberRolloverCredit> GetOwnRolloverCredits(string token,Guid orgID,long policyRevision,Guid poolID)
        =>Current(token,orgID,policyRevision,"AI.Cloud.Reserve",(session,org)=>
        {
            using var lease=DurableState.Acquire(statePath);var state=Read();var pool=Pool(state,poolID,org.OrgID);var now=clock.GetUtcNow();
            var roles=org.Members.Single(m=>m.AccountID==session.AccountID&&m.State==OrganisationMemberState.Active).RoleIDs;
            var rows=(state.RolloverTransfers??Array.Empty<OrganisationRolloverTransfer>()).Where(t=>t.TargetPoolID==poolID&&roles.Contains(t.RoleID)).Select(t=>
            {
                var allowed=CanUseRolloverLot(state,pool,t.ChildAllocationID,roles);
                var lot=state.Lots.Single(l=>l.SourceAllocationID==t.ChildAllocationID);
                var available=allowed&&lot.ExpiresAt>now&&!(state.ExpiredAllocationIDs??Array.Empty<Guid>()).Contains(lot.SourceAllocationID)?lot.Dust-UsedFromLot(state,lot.SourceAllocationID):0;
                return new OrganisationMemberRolloverCredit(lot.SourceAllocationID,t.OriginalSourceAllocationID,t.RoleID,
                    pool.RolloverRules?.GetValueOrDefault(t.RoleID)??OrganisationRolloverChoice.Inherit,allowed,t.Dust,available,
                    t.OriginalExpiry,t.ResultingExpiry,t.BillingPolicyID,t.BillingPolicyVersion,now);
            }).ToArray();return (IReadOnlyList<OrganisationMemberRolloverCredit>)Array.AsReadOnly(rows);
        });

    private static bool CanUseRolloverLot(PoolState state,OrganisationDustPool pool,Guid lotID,IReadOnlyList<Guid> roleIDs)
    {
        var transfer=(state.RolloverTransfers??Array.Empty<OrganisationRolloverTransfer>()).SingleOrDefault(t=>t.ChildAllocationID==lotID);
        if(transfer is null)return true;
        // Restrictive role settings win; possession of another role never bypasses an explicit disallow.
        return roleIDs.Contains(transfer.RoleID)&&!roleIDs.Any(id=>pool.RolloverRules?.GetValueOrDefault(id)==OrganisationRolloverChoice.Disallow);
    }
    private static void ValidateGrant(VerifiedOrganisationRolloverGrant g)
    {
        if(g.GrantID==Guid.Empty||g.AccountID==Guid.Empty||g.OrgID==Guid.Empty||g.SourcePoolID==Guid.Empty||g.TargetPoolID==Guid.Empty||g.SourceAllocationID==Guid.Empty||g.RoleID==Guid.Empty||g.MaximumDust<=0||g.OrganisationPolicyRevision<1||g.OrganisationRevision<1||string.IsNullOrWhiteSpace(g.CarryoverWindowID)||g.BillingPolicyVersion<1||string.IsNullOrWhiteSpace(g.BillingPolicyID)||g.EligibleUntil==default||g.ResultingExpiry==default)throw new InvalidDataException("invalid_verified_rollover_policy");
    }
    private static void ValidateRolloverState(PoolState state)
    {
        if(state.Pools.Any(p=>p.RolloverRules?.Any(r=>r.Key==Guid.Empty||!Enum.IsDefined(r.Value))==true))throw new InvalidDataException("invalid_role_rollover_configuration");
        var history=state.RolloverTransfers??Array.Empty<OrganisationRolloverTransfer>();
        if(history.Any(t=>t is null)||history.Select(t=>t.GrantID).Distinct().Count()!=history.Count||history.Select(t=>t.ChildAllocationID).Distinct().Count()!=history.Count||history.GroupBy(t=>(t.OrgID,t.OperationID)).Any(g=>g.Count()>1))throw new InvalidDataException("invalid_rollover_history");
        foreach(var group in history.GroupBy(t=>(t.OrgID,t.BillingPolicyID,t.BillingPolicyVersion,t.RoleID,t.ApprovedGrant?.CarryoverWindowID)))
            if(group.Any(t=>t.ApprovedGrant is null)||group.Select(t=>t.ApprovedGrant.MaximumDust).Distinct().Count()!=1||SumCapacity(group.Select(t=>t.Dust))>group.First().ApprovedGrant.MaximumDust)throw new InvalidDataException("invalid_rollover_cumulative_cap");
        if(state.Lots.Any(l=>l.FundingReference.StartsWith("rollover:",StringComparison.Ordinal)&&!history.Any(t=>t.ChildAllocationID==l.SourceAllocationID)))throw new InvalidDataException("rollover_lot_without_provenance");
        var parentByChild=history.ToDictionary(t=>t.ChildAllocationID);
        var lotsByID=state.Lots.ToDictionary(l=>l.SourceAllocationID);
        foreach(var t in history)
        {
            if(t.ApprovedGrant is null)throw new InvalidDataException("missing_rollover_policy_snapshot");
            ValidateGrant(t.ApprovedGrant);
            var source=state.Lots.SingleOrDefault(l=>l.SourceAllocationID==t.SourceAllocationID);var child=state.Lots.SingleOrDefault(l=>l.SourceAllocationID==t.ChildAllocationID);
            if(t.ApprovedGrant.GrantID!=t.GrantID||t.ApprovedGrant.AccountID!=t.ActorAccountID||t.ApprovedGrant.OrgID!=t.OrgID||t.ApprovedGrant.SourcePoolID!=t.SourcePoolID||t.ApprovedGrant.TargetPoolID!=t.TargetPoolID||t.ApprovedGrant.SourceAllocationID!=t.SourceAllocationID||t.ApprovedGrant.RoleID!=t.RoleID||t.ApprovedGrant.MaximumDust<t.Dust||t.ApprovedGrant.BillingPolicyID!=t.BillingPolicyID||t.ApprovedGrant.BillingPolicyVersion!=t.BillingPolicyVersion||t.ApprovedGrant.ResultingExpiry!=t.ResultingExpiry||t.At>=t.ApprovedGrant.EligibleUntil||t.At>=t.ResultingExpiry||t.GrantID==Guid.Empty||t.ActorAccountID==Guid.Empty||t.RoleID==Guid.Empty||t.Dust<=0||t.BillingPolicyVersion<1||string.IsNullOrWhiteSpace(t.BillingPolicyID)||string.IsNullOrWhiteSpace(t.OperationID)||source is null||child is null||source.OrgID!=t.OrgID||child.OrgID!=t.OrgID||!lotsByID.TryGetValue(t.OriginalSourceAllocationID,out var original)||original.OrgID!=t.OrgID||original.ExpiresAt!=t.OriginalExpiry||child.ExpiresAt!=t.ResultingExpiry||child.AcquiredAt!=t.At||child.Dust!=t.Dust||child.FundingReference!="rollover:"+t.GrantID.ToString("N")||source.PeriodID!=t.SourcePeriodID||child.PeriodID!=t.TargetPeriodID||t.SourcePeriodID==t.TargetPeriodID||state.LotPools[source.SourceAllocationID]!=t.SourcePoolID||state.LotPools[child.SourceAllocationID]!=t.TargetPoolID)throw new InvalidDataException("invalid_rollover_provenance");
            var cursor=t.SourceAllocationID;var previousAt=t.At;var visited=new HashSet<Guid>();
            if(parentByChild.ContainsKey(cursor)&&!t.ApprovedGrant.AllowPreviouslyRolledOver)throw new InvalidDataException("unapproved_successive_rollover");
            while(parentByChild.TryGetValue(cursor,out var parent))
            {
                if(!visited.Add(cursor)||parent.OrgID!=t.OrgID||parent.OriginalSourceAllocationID!=t.OriginalSourceAllocationID||parent.OriginalExpiry!=t.OriginalExpiry||parent.At>previousAt)throw new InvalidDataException("invalid_rollover_lineage");
                previousAt=parent.At;cursor=parent.SourceAllocationID;
            }
            if(cursor!=t.OriginalSourceAllocationID)throw new InvalidDataException("invalid_original_rollover_source");
        }
    }
}
