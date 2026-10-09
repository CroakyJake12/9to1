namespace NineToOne.Accounts;

public enum OrganisationPoolMode { Shared, Partitioned }
public enum OrganisationPoolReservationState { Reserved, Dispatched, Settled, Cancelled }
public sealed record OrganisationDustPool(Guid PoolID,Guid OrgID,string PeriodID,OrganisationPoolMode Mode,
    IReadOnlyDictionary<Guid,long> MemberCeilings,IReadOnlyDictionary<Guid,long> RoleCeilings,long Revision,
    IReadOnlyDictionary<Guid,OrganisationRolloverChoice>? RolloverRules=null);
public sealed record OrganisationPoolBalance(Guid PoolID,Guid OrgID,string PeriodID,long FundedDust,long ReservedDust,long SettledDust,long AvailableDust,long RolledOverDust=0,long TransferredDust=0,long ExpiredDust=0,DateTimeOffset ObservedAt=default);
public sealed record OrganisationMemberPoolBalance(Guid PoolID,Guid OrgID,Guid AccountID,string PeriodID,
    long MemberReservedDust,long MemberSettledDust,long EffectiveAvailableDust,long PoolRevision,long PolicyRevision,DateTimeOffset ObservedAt);
internal sealed record PoolReservation(OrganisationFundingReservation Funding,IReadOnlyList<Guid> RoleIDs,
    OrganisationPoolReservationState State,long ActualDust,string? SettlementID,string? ProviderUsageReference,string? CancellationID);
internal sealed record PoolState(IReadOnlyList<OrganisationDustPool> Pools,IReadOnlyList<VerifiedOrganisationAllocation> Lots,
    IReadOnlyList<PoolReservation> Reservations,IReadOnlyDictionary<Guid,Guid> LotPools,IReadOnlyList<Guid>? ExpiredAllocationIDs=null,IReadOnlyList<OrganisationRolloverTransfer>? RolloverTransfers=null);

/// <summary>Canonical organisation-owned Dust accounting. Partitions are ceilings over one funded pool, never copied credits.</summary>
public sealed partial class OrganisationDustPools(string statePath,CakeIdentityService identity,OrganisationService organisations,
    IOrganisationAllocationFundingAuthority? funding,IOrganisationCostQuoteAuthority? costQuotes,TimeProvider? timeProvider=null,
    IOrganisationRolloverPolicyAuthority? rolloverPolicies=null)
    :IOrganisationCloudFundingAuthority
{
    private readonly TimeProvider clock=timeProvider??TimeProvider.System;
    private T Current<T>(string token,Guid orgID,long policyRevision,string action,Func<CakeSession,Organisation,T> apply)
        =>identity.WithCurrentSession(token,session=>organisations.WithCurrentResourceAuthority(session.AccountID,orgID,action,policyRevision,org=>
        {
            using var resourceLease=DurableState.Acquire(statePath);
            if(session.ExpiresAt<=DateTimeOffset.UtcNow)throw new UnauthorizedAccessException("session_expired_while_waiting_for_resource");
            return apply(session,org);
        }));

    public OrganisationDustPool Create(string token,Guid orgID,long policyRevision,string periodID)
        =>Current(token,orgID,policyRevision,"Admin.Resources.CreatePool",(_,org)=>
        {
            if(string.IsNullOrWhiteSpace(periodID))throw new ArgumentException("period_identity_required");
            using var lease=DurableState.Acquire(statePath);var state=Read();
            var pool=new OrganisationDustPool(Guid.NewGuid(),org.OrgID,periodID,OrganisationPoolMode.Shared,new Dictionary<Guid,long>(),new Dictionary<Guid,long>(),1);
            Write(state with{Pools=state.Pools.Append(pool).ToArray()});return pool;
        });

    public OrganisationPoolBalance GetBalance(string token,Guid orgID,long policyRevision,Guid poolID)
        =>Current(token,orgID,policyRevision,"Admin.Resources.GetUsage",(_,org)=>
        {
            using var lease=DurableState.Acquire(statePath);var state=Read();var pool=Pool(state,poolID,org.OrgID);var now=clock.GetUtcNow();
            var lots=state.Lots.Where(l=>state.LotPools[l.SourceAllocationID]==poolID&&l.PeriodID==pool.PeriodID).ToArray();
            var reservations=state.Reservations.Where(r=>r.Funding.PoolID==poolID&&r.Funding.PeriodID==pool.PeriodID).ToArray();
            var transfers=state.RolloverTransfers??Array.Empty<OrganisationRolloverTransfer>();
            return new OrganisationPoolBalance(poolID,org.OrgID,pool.PeriodID,lots.Where(l=>!transfers.Any(t=>t.ChildAllocationID==l.SourceAllocationID)).Sum(l=>l.Dust),
                reservations.Where(r=>r.State is OrganisationPoolReservationState.Reserved or OrganisationPoolReservationState.Dispatched).Sum(r=>r.Funding.ReservedDust),
                reservations.Where(r=>r.State==OrganisationPoolReservationState.Settled).Sum(r=>r.ActualDust),
                lots.Where(l=>l.ExpiresAt>now&&!(state.ExpiredAllocationIDs??Array.Empty<Guid>()).Contains(l.SourceAllocationID)).Sum(l=>l.Dust-UsedFromLot(state,l.SourceAllocationID)),
                lots.Where(l=>transfers.Any(t=>t.ChildAllocationID==l.SourceAllocationID)).Sum(l=>l.Dust),
                transfers.Where(t=>t.SourcePoolID==poolID&&t.SourcePeriodID==pool.PeriodID).Sum(t=>t.Dust),
                lots.Where(l=>(state.ExpiredAllocationIDs??Array.Empty<Guid>()).Contains(l.SourceAllocationID)).Sum(l=>l.Dust-UsedFromLot(state,l.SourceAllocationID)),now);
        });

    /// <summary>Current member's effective ceiling intersected with the one live organisation pool; no other member's usage or work content.</summary>
    public OrganisationMemberPoolBalance GetEffectiveBalance(string token,Guid orgID,long policyRevision,Guid poolID)
        =>Current(token,orgID,policyRevision,"AI.Cloud.Reserve",(session,org)=>
        {
            using var lease=DurableState.Acquire(statePath);var state=Read();var pool=Pool(state,poolID,org.OrgID);var now=clock.GetUtcNow();
            var active=state.Reservations.Where(r=>r.Funding.PoolID==poolID&&r.Funding.PeriodID==pool.PeriodID&&r.State!=OrganisationPoolReservationState.Cancelled).ToArray();
            var member=active.Where(r=>r.Funding.AccountID==session.AccountID).ToArray();
            var roleIDs=org.Members.Single(m=>m.AccountID==session.AccountID&&m.State==OrganisationMemberState.Active).RoleIDs;
            var available=SumCapacity(state.Lots.Where(l=>state.LotPools[l.SourceAllocationID]==poolID&&l.PeriodID==pool.PeriodID&&l.ExpiresAt>now&&CanUseRolloverLot(state,pool,l.SourceAllocationID,roleIDs)&&!(state.ExpiredAllocationIDs??Array.Empty<Guid>()).Contains(l.SourceAllocationID)).Select(l=>l.Dust-UsedFromLot(state,l.SourceAllocationID)));
            if(pool.Mode==OrganisationPoolMode.Partitioned||pool.MemberCeilings.ContainsKey(session.AccountID))
                available=Math.Min(available,Math.Max(0,pool.MemberCeilings.GetValueOrDefault(session.AccountID)-SumCapacity(member.Select(Committed))));
            foreach(var roleID in roleIDs)
                if(pool.RoleCeilings.TryGetValue(roleID,out var ceiling))available=Math.Min(available,Math.Max(0,ceiling-SumCapacity(active.Where(r=>r.RoleIDs.Contains(roleID)).Select(Committed))));
            return new OrganisationMemberPoolBalance(poolID,org.OrgID,session.AccountID,pool.PeriodID,
                SumCapacity(member.Where(r=>r.State is OrganisationPoolReservationState.Reserved or OrganisationPoolReservationState.Dispatched).Select(r=>r.Funding.ReservedDust)),
                SumCapacity(member.Where(r=>r.State==OrganisationPoolReservationState.Settled).Select(r=>r.ActualDust)),available,pool.Revision,org.Policy.Revision,now);
        });

    public async ValueTask FundAsync(string token,Guid orgID,long policyRevision,Guid poolID,string fundedReference,CancellationToken ct=default)
    {
        if(funding is null)throw new InvalidOperationException("organisation_funding_authority_unconfigured");
        var lot=await funding.ResolveAsync(fundedReference,ct).ConfigureAwait(false)??throw new UnauthorizedAccessException("allocation_not_funded");
        ct.ThrowIfCancellationRequested();ValidateLot(lot);
        if(lot.FundingReference.StartsWith("rollover:",StringComparison.Ordinal))throw new InvalidOperationException("rollover_provenance_requires_internal_transition");
        if(lot.OrgID!=orgID||lot.FundingReference!=fundedReference)throw new UnauthorizedAccessException("allocation_owner_or_funding_reference_mismatch");
        Current(token,orgID,policyRevision,"Admin.Resources.FundPool",(_,org)=>
        {
            using var lease=DurableState.Acquire(statePath);ct.ThrowIfCancellationRequested();var state=Read();var pool=Pool(state,poolID,org.OrgID);
            var prior=state.Lots.SingleOrDefault(l=>l.SourceAllocationID==lot.SourceAllocationID);
            if(prior is not null)
            {
                if(prior!=lot||state.LotPools[lot.SourceAllocationID]!=poolID)throw new InvalidOperationException("funding_replay_conflict");
                return true;
            }
            if(pool.PeriodID!=lot.PeriodID||lot.ExpiresAt<=clock.GetUtcNow()||lot.AcquiredAt>clock.GetUtcNow())throw new InvalidOperationException("allocation_period_or_expiry_mismatch");
            var fundedTotal=AddCapacity(SumCapacity(state.Lots.Where(l=>state.LotPools[l.SourceAllocationID]==poolID).Select(l=>l.Dust)),lot.Dust);
            var bindings=state.LotPools.ToDictionary(p=>p.Key,p=>p.Value);bindings.Add(lot.SourceAllocationID,poolID);
            ct.ThrowIfCancellationRequested();
            Write(state with{Lots=state.Lots.Append(lot).ToArray(),LotPools=bindings});return true;
        });
    }

    public OrganisationDustPool Configure(string token,Guid orgID,long policyRevision,Guid poolID,long expectedRevision,
        OrganisationPoolMode mode,IReadOnlyDictionary<Guid,long> memberCeilings,IReadOnlyDictionary<Guid,long> roleCeilings)
    {
        var members=memberCeilings.ToDictionary(p=>p.Key,p=>p.Value);var roles=roleCeilings.ToDictionary(p=>p.Key,p=>p.Value);
        return Current(token,orgID,policyRevision,"Admin.Resources.ConfigurePool",(_,org)=>
        {
            using var lease=DurableState.Acquire(statePath);var state=Read();var pool=Pool(state,poolID,org.OrgID);
            if(pool.Revision!=expectedRevision)throw new InvalidOperationException("pool_revision_conflict");
            if(!Enum.IsDefined(mode)||members.Any(p=>p.Key==Guid.Empty||p.Value<0||!org.Members.Any(m=>m.AccountID==p.Key&&m.State==OrganisationMemberState.Active))||
                roles.Any(p=>p.Key==Guid.Empty||p.Value<0||!org.Roles.Any(r=>r.RoleID==p.Key)))throw new ArgumentException("invalid_allocation_plan");
            var total=state.Lots.Where(l=>state.LotPools[l.SourceAllocationID]==poolID&&l.PeriodID==pool.PeriodID).Sum(l=>l.Dust);
            if(mode==OrganisationPoolMode.Partitioned&&SumCapacity(members.Values)>total)throw new InvalidOperationException("partition_exceeds_funded_allocation");
            var active=state.Reservations.Where(r=>r.Funding.PoolID==poolID&&r.Funding.PeriodID==pool.PeriodID&&r.State!=OrganisationPoolReservationState.Cancelled).ToArray();
            foreach(var group in active.GroupBy(r=>r.Funding.AccountID))
            {
                var used=group.Sum(Committed);
                if((mode==OrganisationPoolMode.Partitioned||members.ContainsKey(group.Key))&&used>members.GetValueOrDefault(group.Key))throw new InvalidOperationException("partition_cannot_discard_inflight_or_spent_usage");
            }
            foreach(var role in roles)if(active.Where(r=>r.RoleIDs.Contains(role.Key)).Sum(Committed)>role.Value)throw new InvalidOperationException("role_ceiling_below_committed_usage");
            var next=pool with{Mode=mode,MemberCeilings=members,RoleCeilings=roles,Revision=checked(pool.Revision+1)};
            Write(state with{Pools=state.Pools.Select(p=>p.PoolID==poolID?next:p).ToArray()});return next;
        });
    }

    public async ValueTask<OrganisationFundingReservation> ReserveAsync(string token,OrganisationFundingRequest request,CancellationToken ct)
    {
        if(costQuotes is null)throw new InvalidOperationException("organisation_cost_quote_authority_unconfigured");
        var quote=await costQuotes.ResolveAsync(request.CostQuoteID,ct).ConfigureAwait(false)??throw new UnauthorizedAccessException("cost_quote_unverified");
        ct.ThrowIfCancellationRequested();
        if(quote.QuoteID!=request.CostQuoteID||quote.OrgID!=request.OrgID||quote.ModelRouteID!=request.ModelRouteID||quote.PolicyRevision!=request.ExpectedPolicyRevision||quote.MaximumDust<=0||string.IsNullOrWhiteSpace(request.OperationID))throw new UnauthorizedAccessException("cost_quote_context_mismatch");
        return Current(token,request.OrgID,request.ExpectedPolicyRevision,"AI.Cloud.Reserve",(session,org)=>
        {
            using var lease=DurableState.Acquire(statePath);ct.ThrowIfCancellationRequested();var state=Read();var pool=Pool(state,quote.PoolID,org.OrgID);var now=clock.GetUtcNow();
            if(string.IsNullOrWhiteSpace(session.RegisteredClientID)||quote.RegisteredClientID!=session.RegisteredClientID)throw new UnauthorizedAccessException("cost_quote_registered_client_mismatch");
            if(quote.Attribution is null)throw new InvalidOperationException("organisation_quote_usage_attribution_unconfigured");
            ValidateAttribution(quote.Attribution);
            if(quote.AccountID!=session.AccountID||quote.ExpiresAt<=now)throw new UnauthorizedAccessException("cost_quote_actor_or_expiry_mismatch");
            var prior=state.Reservations.SingleOrDefault(r=>r.Funding.OrgID==org.OrgID&&r.Funding.OperationID==request.OperationID);
            if(prior is not null)
            {
                if(prior.Funding.AccountID!=session.AccountID||prior.Funding.CostQuoteID!=quote.QuoteID||prior.Funding.ModelRouteID!=request.ModelRouteID||prior.Funding.ReservedDust!=quote.MaximumDust||prior.Funding.RegisteredClientID!=quote.RegisteredClientID||prior.Funding.Attribution!=quote.Attribution||prior.Funding.PolicyRevision!=quote.PolicyRevision||prior.Funding.AuthorisationRevision!=org.Revision)throw new InvalidOperationException("reservation_replay_conflict");
                if(prior.State!=OrganisationPoolReservationState.Reserved||prior.Funding.ExpiresAt<=now)throw new InvalidOperationException("reservation_already_dispatched_or_completed");
                return prior.Funding;
            }
            var roleIDs=org.Members.Single(m=>m.AccountID==session.AccountID&&m.State==OrganisationMemberState.Active).RoleIDs.ToArray();
            var current=state.Reservations.Where(r=>r.Funding.PoolID==pool.PoolID&&r.Funding.PeriodID==pool.PeriodID&&r.State!=OrganisationPoolReservationState.Cancelled).ToArray();
            var memberUsed=current.Where(r=>r.Funding.AccountID==session.AccountID).Sum(Committed);
            if((pool.Mode==OrganisationPoolMode.Partitioned||pool.MemberCeilings.ContainsKey(session.AccountID))&&AddCapacity(memberUsed,quote.MaximumDust)>pool.MemberCeilings.GetValueOrDefault(session.AccountID))throw new InvalidOperationException("member_ceiling_exceeded");
            foreach(var roleID in roleIDs)if(pool.RoleCeilings.TryGetValue(roleID,out var ceiling)&&AddCapacity(current.Where(r=>r.RoleIDs.Contains(roleID)).Sum(Committed),quote.MaximumDust)>ceiling)throw new InvalidOperationException("role_ceiling_exceeded");
            var remaining=quote.MaximumDust;var expiry=quote.ExpiresAt;var slices=new List<OrganisationFundingSlice>();
            foreach(var lot in state.Lots.Where(l=>state.LotPools[l.SourceAllocationID]==pool.PoolID&&l.PeriodID==pool.PeriodID&&l.ExpiresAt>now&&CanUseRolloverLot(state,pool,l.SourceAllocationID,roleIDs)&&!(state.ExpiredAllocationIDs??Array.Empty<Guid>()).Contains(l.SourceAllocationID)).OrderBy(l=>l.ExpiresAt).ThenBy(l=>l.SourceAllocationID))
            {
                var available=checked(lot.Dust-UsedFromLot(state,lot.SourceAllocationID));var take=Math.Min(remaining,available);
                if(take>0){slices.Add(new(lot.SourceAllocationID,take));remaining-=take;expiry=expiry<lot.ExpiresAt?expiry:lot.ExpiresAt;}
                if(remaining==0)break;
            }
            if(remaining!=0)throw new InvalidOperationException("organisation_dust_exhausted");
            var reservation=new OrganisationFundingReservation(Guid.NewGuid(),session.AccountID,org.OrgID,pool.PoolID,pool.PeriodID,request.OperationID,request.ModelRouteID,quote.QuoteID,org.Policy.Revision,quote.MaximumDust,expiry,slices.ToArray(),org.Revision,session.RegisteredClientID,quote.Attribution);
            ct.ThrowIfCancellationRequested();
            Write(state with{Reservations=state.Reservations.Append(new PoolReservation(reservation,roleIDs,OrganisationPoolReservationState.Reserved,0,null,null,null)).ToArray()});return reservation;
        });
    }

    public ValueTask<OrganisationFundingReservation> RecheckDispatchAsync(string token,Guid reservationID,long expectedPolicyRevision,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();var snapshot=Find(reservationID);
        var result=Current(token,snapshot.Funding.OrgID,expectedPolicyRevision,"AI.Cloud.Reserve",(session,org)=>
        {
            using var lease=DurableState.Acquire(statePath);var state=Read();var current=state.Reservations.Single(r=>r.Funding.ReservationID==reservationID);
            var currentPool=Pool(state,current.Funding.PoolID,org.OrgID);
            var currentRoles=org.Members.Single(m=>m.AccountID==session.AccountID&&m.State==OrganisationMemberState.Active).RoleIDs;
            if(current.Funding.FundingSources.Any(slice=>!CanUseRolloverLot(state,currentPool,slice.SourceAllocationID,currentRoles)))throw new UnauthorizedAccessException("rollover_permission_changed");
            if(current.Funding.Attribution is null||current.Funding.RegisteredClientID is null)throw new UnauthorizedAccessException("legacy_reservation_requires_attributed_reauthorisation");
            if(current.Funding.RegisteredClientID!=session.RegisteredClientID)throw new UnauthorizedAccessException("reservation_registered_client_mismatch");
            if(current.Funding.AccountID!=session.AccountID||current.Funding.AuthorisationRevision!=org.Revision||current.Funding.PolicyRevision!=expectedPolicyRevision)throw new UnauthorizedAccessException("reservation_authority_changed");
            if(current.State!=OrganisationPoolReservationState.Reserved||current.Funding.ExpiresAt<=clock.GetUtcNow())throw new InvalidOperationException("reservation_dispatch_unavailable");
            Write(state with{Reservations=state.Reservations.Select(r=>r.Funding.ReservationID==reservationID?r with{State=OrganisationPoolReservationState.Dispatched}:r).ToArray()});return current.Funding;
        });return ValueTask.FromResult(result);
    }

    public ValueTask SettleObservedAsync(OrganisationObservedUsage observed,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();using var lease=DurableState.Acquire(statePath);var state=Read();var reservation=state.Reservations.Single(r=>r.Funding.ReservationID==observed.ReservationID);
        if(observed.ActualDust<0||observed.ActualDust>reservation.Funding.ReservedDust||string.IsNullOrWhiteSpace(observed.SettlementID)||string.IsNullOrWhiteSpace(observed.ProviderUsageReference))throw new ArgumentException("invalid_observed_usage");
        if(reservation.State==OrganisationPoolReservationState.Settled)
        {
            if(reservation.ActualDust!=observed.ActualDust||reservation.SettlementID!=observed.SettlementID||reservation.ProviderUsageReference!=observed.ProviderUsageReference)throw new InvalidOperationException("usage_settlement_replay_conflict");
            return ValueTask.CompletedTask;
        }
        if(reservation.State!=OrganisationPoolReservationState.Dispatched||state.Reservations.Any(r=>r.SettlementID==observed.SettlementID||r.ProviderUsageReference==observed.ProviderUsageReference))throw new InvalidOperationException("usage_settlement_unavailable");
        Write(state with{Reservations=state.Reservations.Select(r=>r.Funding.ReservationID==observed.ReservationID?r with{State=OrganisationPoolReservationState.Settled,ActualDust=observed.ActualDust,SettlementID=observed.SettlementID,ProviderUsageReference=observed.ProviderUsageReference}:r).ToArray()});return ValueTask.CompletedTask;
    }

    public ValueTask CancelAsync(Guid reservationID,string cancellationID,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(string.IsNullOrWhiteSpace(cancellationID))throw new ArgumentException("cancellation_identity_required");
        using var lease=DurableState.Acquire(statePath);var state=Read();var reservation=state.Reservations.Single(r=>r.Funding.ReservationID==reservationID);
        if(reservation.State==OrganisationPoolReservationState.Cancelled){if(reservation.CancellationID!=cancellationID)throw new InvalidOperationException("cancellation_replay_conflict");return ValueTask.CompletedTask;}
        if(reservation.State!=OrganisationPoolReservationState.Reserved)throw new InvalidOperationException("dispatched_work_requires_observed_settlement");
        Write(state with{Reservations=state.Reservations.Select(r=>r.Funding.ReservationID==reservationID?r with{State=OrganisationPoolReservationState.Cancelled,CancellationID=cancellationID}:r).ToArray()});return ValueTask.CompletedTask;
    }

    private PoolReservation Find(Guid id){using var lease=DurableState.Acquire(statePath);return Read().Reservations.Single(r=>r.Funding.ReservationID==id);}
    private static OrganisationDustPool Pool(PoolState state,Guid id,Guid orgID)=>state.Pools.SingleOrDefault(p=>p.PoolID==id&&p.OrgID==orgID)??throw new UnauthorizedAccessException("pool_not_in_organisation");
    private static long Committed(PoolReservation reservation)=>reservation.State==OrganisationPoolReservationState.Settled?reservation.ActualDust:reservation.State==OrganisationPoolReservationState.Cancelled?0:reservation.Funding.ReservedDust;
    private static long UsedFromLot(PoolState state,Guid allocationID)
    {
        long used=(state.RolloverTransfers??Array.Empty<OrganisationRolloverTransfer>()).Where(t=>t.SourceAllocationID==allocationID).Sum(t=>t.Dust);
        foreach(var reservation in state.Reservations.Where(r=>r.State!=OrganisationPoolReservationState.Cancelled))
        {
            var remaining=Committed(reservation);
            foreach(var slice in reservation.Funding.FundingSources){var charge=Math.Min(remaining,slice.Dust);if(slice.SourceAllocationID==allocationID)used=checked(used+charge);remaining-=charge;}
        }
        return used;
    }
    private static void ValidateLot(VerifiedOrganisationAllocation? lot)
    {if(lot is null||lot.SourceAllocationID==Guid.Empty||lot.OrgID==Guid.Empty||string.IsNullOrWhiteSpace(lot.PeriodID)||lot.Dust<=0||lot.AcquiredAt==default||lot.ExpiresAt<=lot.AcquiredAt||string.IsNullOrWhiteSpace(lot.FundingReference))throw new InvalidDataException("invalid_funded_allocation");}
    private static long AddCapacity(long a,long b){try{return checked(a+b);}catch(OverflowException e){throw new InvalidOperationException("organisation_capacity_range_exceeded",e);}}
    private static long SumCapacity(IEnumerable<long> values)=>values.Aggregate(0L,AddCapacity);
    private PoolState Read(){try{return ReadValidated();}catch(OverflowException e){throw new InvalidDataException("invalid_pool_integer_range",e);}}
    private PoolState ReadValidated()
    {
        var state=File.Exists(statePath)?DurableState.Read<PoolState>(statePath):new([],[],[],new Dictionary<Guid,Guid>());
        if(state.Pools is null||state.Lots is null||state.Reservations is null||state.LotPools is null)throw new InvalidDataException("invalid_pool_state");
        if(state.Pools.Any(p=>p is null)||state.Lots.Any(l=>l is null)||state.Reservations.Any(r=>r is null))throw new InvalidDataException("null_pool_entry");
        foreach(var lot in state.Lots){ValidateLot(lot);if(!state.LotPools.TryGetValue(lot.SourceAllocationID,out var poolID)||!state.Pools.Any(p=>p.PoolID==poolID&&p.OrgID==lot.OrgID))throw new InvalidDataException("invalid_lot_owner_binding");}
        if(state.Pools.Any(p=>p.PoolID==Guid.Empty||p.OrgID==Guid.Empty||string.IsNullOrWhiteSpace(p.PeriodID)||!Enum.IsDefined(p.Mode)||p.Revision<1||p.MemberCeilings is null||p.RoleCeilings is null||p.MemberCeilings.Any(v=>v.Key==Guid.Empty||v.Value<0)||p.RoleCeilings.Any(v=>v.Key==Guid.Empty||v.Value<0))||state.Pools.Select(p=>p.PoolID).Distinct().Count()!=state.Pools.Count||state.Lots.Select(l=>l.SourceAllocationID).Distinct().Count()!=state.Lots.Count)throw new InvalidDataException("invalid_pool_configuration");
        foreach(var r in state.Reservations)
        {
            if(r.Funding?.Attribution is not null)ValidateAttribution(r.Funding.Attribution);
            if(r.Funding?.RegisteredClientID is not null&&string.IsNullOrWhiteSpace(r.Funding.RegisteredClientID)||(r.Funding?.Attribution is null)!=(r.Funding?.RegisteredClientID is null))throw new InvalidDataException("invalid_reservation_attribution_binding");
            if(r is null||r.Funding is null||r.RoleIDs is null||r.Funding.FundingSources is null||r.RoleIDs.Any(id=>id==Guid.Empty)||r.RoleIDs.Distinct().Count()!=r.RoleIDs.Count||r.Funding.ReservationID==Guid.Empty||r.Funding.AccountID==Guid.Empty||r.Funding.ReservedDust<=0||r.Funding.CostQuoteID==Guid.Empty||r.Funding.PolicyRevision<1||r.Funding.AuthorisationRevision<1||r.Funding.ExpiresAt==default||string.IsNullOrWhiteSpace(r.Funding.OperationID)||string.IsNullOrWhiteSpace(r.Funding.ModelRouteID)||string.IsNullOrWhiteSpace(r.Funding.PeriodID)||r.ActualDust<0||r.ActualDust>r.Funding.ReservedDust||!Enum.IsDefined(r.State)||r.Funding.FundingSources.Any(s=>s is null)||r.Funding.FundingSources.Sum(s=>s.Dust)!=r.Funding.ReservedDust||r.Funding.FundingSources.Select(s=>s.SourceAllocationID).Distinct().Count()!=r.Funding.FundingSources.Count||r.Funding.FundingSources.Any(s=>s.Dust<=0||!state.Lots.Any(l=>l.SourceAllocationID==s.SourceAllocationID&&l.OrgID==r.Funding.OrgID&&l.PeriodID==r.Funding.PeriodID&&state.LotPools[l.SourceAllocationID]==r.Funding.PoolID)))throw new InvalidDataException("invalid_pool_reservation");
            if(!state.Pools.Any(p=>p.PoolID==r.Funding.PoolID&&p.OrgID==r.Funding.OrgID)||
                r.State is OrganisationPoolReservationState.Reserved or OrganisationPoolReservationState.Dispatched&&(r.ActualDust!=0||r.SettlementID is not null||r.CancellationID is not null||r.ProviderUsageReference is not null)||
                r.State==OrganisationPoolReservationState.Settled&&(string.IsNullOrWhiteSpace(r.SettlementID)||string.IsNullOrWhiteSpace(r.ProviderUsageReference)||r.CancellationID is not null)||
                r.State==OrganisationPoolReservationState.Cancelled&&(string.IsNullOrWhiteSpace(r.CancellationID)||r.ActualDust!=0||r.SettlementID is not null||r.ProviderUsageReference is not null))throw new InvalidDataException("invalid_pool_reservation_state");
        }
        if(state.Reservations.Select(r=>r.Funding.ReservationID).Distinct().Count()!=state.Reservations.Count)throw new InvalidDataException("duplicate_pool_reservation");
        ValidateRolloverState(state);
        foreach(var lot in state.Lots)if(UsedFromLot(state,lot.SourceAllocationID)>lot.Dust)throw new InvalidDataException("pool_allocation_overspent");
        if(state.LotPools.Count!=state.Lots.Count||state.Reservations.GroupBy(r=>(r.Funding.OrgID,r.Funding.OperationID)).Any(g=>g.Count()>1)||state.Reservations.Where(r=>r.SettlementID is not null).GroupBy(r=>r.SettlementID).Any(g=>g.Count()>1)||state.Reservations.Where(r=>r.ProviderUsageReference is not null).GroupBy(r=>r.ProviderUsageReference).Any(g=>g.Count()>1))throw new InvalidDataException("invalid_pool_binding_or_replay_identity");
        var now=clock.GetUtcNow();
        var expiredAllocations=(state.ExpiredAllocationIDs??Array.Empty<Guid>()).ToHashSet();
        if(expiredAllocations.Count!=(state.ExpiredAllocationIDs?.Count??0)||expiredAllocations.Any(id=>!state.Lots.Any(l=>l.SourceAllocationID==id)))throw new InvalidDataException("invalid_expired_allocation_identity");
        var newlyExpired=state.Lots.Where(l=>l.ExpiresAt<=now&&!expiredAllocations.Contains(l.SourceAllocationID)).Select(l=>l.SourceAllocationID).ToArray();
        if(newlyExpired.Length>0||state.Reservations.Any(r=>r.State==OrganisationPoolReservationState.Reserved&&r.Funding.ExpiresAt<=now))
        {
            expiredAllocations.UnionWith(newlyExpired);
            state=state with{ExpiredAllocationIDs=expiredAllocations.OrderBy(id=>id).ToArray(),Reservations=state.Reservations.Select(r=>r.State==OrganisationPoolReservationState.Reserved&&r.Funding.ExpiresAt<=now?r with{State=OrganisationPoolReservationState.Cancelled,CancellationID="expired:"+r.Funding.ReservationID.ToString("N")}:r).ToArray()};
            // Expiry is an atomic persisted transition; a later clock correction cannot resurrect a released reservation.
            Write(state);
        }
        return state;
    }
    private void Write(PoolState state){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);DurableState.Write(statePath,state);}
}
