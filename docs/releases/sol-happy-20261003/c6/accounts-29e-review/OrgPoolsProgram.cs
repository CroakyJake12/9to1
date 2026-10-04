using NineToOne.Accounts;
using System.Collections.Concurrent;
static void Check(bool value,string message){if(!value)throw new Exception(message);}
var root=Path.Combine(Path.GetTempPath(),"astra-org-pools-"+Guid.NewGuid());Directory.CreateDirectory(root);
try
{
    var owner=Guid.NewGuid();var redirect="http://127.0.0.1/fixture-callback";
    var identity=new CakeIdentityService(Path.Combine(root,"identity.json"),new Dictionary<string,IReadOnlySet<string>>{["fixture"]=new HashSet<string>{redirect}});
    var verifier=new string('a',64);var code=identity.AuthorizeAuthenticatedAccount(new(owner,"Fictional owner"),"fixture",redirect,CakeIdentityService.Challenge(verifier));
    var token=identity.Exchange(code,"fixture",redirect,verifier,"Fixture").AccessToken;
    var orgs=new OrganisationService(Path.Combine(root,"organisations.json"),new ProfileService(Path.Combine(root,"profiles.json"),null));
    var org=orgs.CreateTrustedOrganisation(owner,"Explicit fictional funded organisation",BusinessAddOnKind.Business,"fictional-settled-add-on");
    var funded=new FixtureFunding();var quoted=new FixtureQuotes();var path=Path.Combine(root,"pools.json");
    OrganisationDustPools Service()=>new(path,identity,orgs,funded,quoted);
    var service=Service();var pool=service.Create(token,org.OrgID,org.Policy.Revision,"fixture-period");
    var now=DateTimeOffset.UtcNow;funded.Lot=new(Guid.NewGuid(),org.OrgID,pool.PeriodID,1000,now,now.AddHours(1),"fictional-paid-allocation");
    await service.FundAsync(token,org.OrgID,org.Policy.Revision,pool.PoolID,"fictional-paid-allocation");
    await Service().FundAsync(token,org.OrgID,org.Policy.Revision,pool.PoolID,"fictional-paid-allocation");
    Check(service.GetBalance(token,org.OrgID,org.Policy.Revision,pool.PoolID).FundedDust==1000,"source funding retry creates no duplicate credits");
    await CancelledAuthoritiesPreserveState(root,identity,orgs,token,org,owner,now);
    var noMetadataQuote=new VerifiedOrganisationCostQuote(Guid.NewGuid(),owner,org.OrgID,pool.PoolID,"fictional-model-route",org.Policy.Revision,100,now.AddMinutes(10),"fixture");quoted.Quotes[noMetadataQuote.QuoteID]=noMetadataQuote;
    try{await service.ReserveAsync(token,new(org.OrgID,org.Policy.Revision,noMetadataQuote.ModelRouteID,noMetadataQuote.QuoteID,"missing-attribution"),CancellationToken.None);throw new Exception("unattributed quote admitted");}catch(InvalidOperationException error)when(error.Message=="organisation_quote_usage_attribution_unconfigured"){}
    var wrongClientQuote=noMetadataQuote with{QuoteID=Guid.NewGuid(),RegisteredClientID="different-client",Attribution=new("fixture.app","fixture.model",null,null)};quoted.Quotes[wrongClientQuote.QuoteID]=wrongClientQuote;
    try{await service.ReserveAsync(token,new(org.OrgID,org.Policy.Revision,wrongClientQuote.ModelRouteID,wrongClientQuote.QuoteID,"wrong-registered-client"),CancellationToken.None);throw new Exception("another registered client's quote admitted");}catch(UnauthorizedAccessException error)when(error.Message=="cost_quote_registered_client_mismatch"){}
    var reservations=new ConcurrentBag<OrganisationFundingReservation>();
    Parallel.For(0,32,i=>
    {
        var quote=new VerifiedOrganisationCostQuote(Guid.NewGuid(),owner,org.OrgID,pool.PoolID,"fictional-model-route",org.Policy.Revision,100,now.AddMinutes(10),"fixture",new("fixture.app","fixture.model",null,null));quoted.Quotes[quote.QuoteID]=quote;
        try{reservations.Add(Service().ReserveAsync(token,new(org.OrgID,org.Policy.Revision,quote.ModelRouteID,quote.QuoteID,"operation-"+i),CancellationToken.None).AsTask().GetAwaiter().GetResult());}
        catch(InvalidOperationException error)when(error.Message=="organisation_dust_exhausted"){}
    });
    Check(reservations.Count==10,"multi-instance parallel reservations cannot overspend one funded pool");
    var balance=service.GetBalance(token,org.OrgID,org.Policy.Revision,pool.PoolID);Check(balance.ReservedDust==1000&&balance.AvailableDust==0,"one authoritative aggregate balance");
    var first=reservations.First();await service.RecheckDispatchAsync(token,first.ReservationID,org.Policy.Revision,CancellationToken.None);
    try{await service.CancelAsync(first.ReservationID,"unsafe-release",CancellationToken.None);throw new Exception("dispatched ambiguous usage released");}catch(InvalidOperationException){}
    await Service().SettleObservedAsync(new(first.ReservationID,"fixture-settlement",60,"fixture-provider-observed-usage"),CancellationToken.None);
    await Service().SettleObservedAsync(new(first.ReservationID,"fixture-settlement",60,"fixture-provider-observed-usage"),CancellationToken.None);
    balance=service.GetBalance(token,org.OrgID,org.Policy.Revision,pool.PoolID);Check(balance.SettledDust==60&&balance.ReservedDust==900&&balance.AvailableDust==40,"actual charge releases unused reservation once across restart");
    var report=Service().GetUsageReport(token,org.OrgID,org.Policy.Revision,pool.PoolID);
    Check(report.Rows.Single().Attribution==new OrganisationUsageAttribution("fixture.app","fixture.model",null,null)&&report.Rows.Single().ReservedDust==900&&report.Rows.Single().SettledDust==60&&report.Rows.Single().HistoricalRoleIDs.SequenceEqual(org.Roles.Select(r=>r.RoleID)),"canonical attribution report reconciles actual ledger, preserves role snapshot, and excludes unrelated personal content");
    try{await service.SettleObservedAsync(new(first.ReservationID,"fixture-settlement",61,"fixture-provider-observed-usage"),CancellationToken.None);throw new Exception("conflicting delayed usage accepted");}catch(InvalidOperationException){}
    foreach(var reservation in reservations.Where(r=>r.ReservationID!=first.ReservationID))await service.CancelAsync(reservation.ReservationID,"cancel-"+reservation.ReservationID,CancellationToken.None);
    pool=service.Configure(token,org.OrgID,org.Policy.Revision,pool.PoolID,pool.Revision,OrganisationPoolMode.Partitioned,new Dictionary<Guid,long>{{owner,160}},new Dictionary<Guid,long>{{org.Roles.Single().RoleID,160}});
    var partitionQuote=new VerifiedOrganisationCostQuote(Guid.NewGuid(),owner,org.OrgID,pool.PoolID,"fictional-model-route",org.Policy.Revision,100,now.AddMinutes(10),"fixture",new("fixture.app","fixture.model",null,null));quoted.Quotes[partitionQuote.QuoteID]=partitionQuote;
    var partitionReservation=await service.ReserveAsync(token,new(org.OrgID,org.Policy.Revision,partitionQuote.ModelRouteID,partitionQuote.QuoteID,"partition-operation"),CancellationToken.None);
    var effective=Service().GetEffectiveBalance(token,org.OrgID,org.Policy.Revision,pool.PoolID);
    Check(effective.AccountID==owner&&effective.MemberReservedDust==100&&effective.MemberSettledDust==60&&effective.EffectiveAvailableDust==0&&effective.PolicyRevision==org.Policy.Revision,"current member effective availability intersects partition and role ceilings with actual committed usage");
    var overQuote=partitionQuote with{QuoteID=Guid.NewGuid()};quoted.Quotes[overQuote.QuoteID]=overQuote;
    try{await service.ReserveAsync(token,new(org.OrgID,org.Policy.Revision,overQuote.ModelRouteID,overQuote.QuoteID,"over-partition"),CancellationToken.None);throw new Exception("member partition ceiling exceeded");}catch(InvalidOperationException error)when(error.Message=="member_ceiling_exceeded"){}
    try{service.Configure(token,org.OrgID,org.Policy.Revision,pool.PoolID,pool.Revision,OrganisationPoolMode.Partitioned,new Dictionary<Guid,long>{{owner,60}},new Dictionary<Guid,long>());throw new Exception("inflight partition discarded");}catch(InvalidOperationException){}
    pool=service.Configure(token,org.OrgID,org.Policy.Revision,pool.PoolID,pool.Revision,OrganisationPoolMode.Shared,new Dictionary<Guid,long>(),new Dictionary<Guid,long>{{org.Roles.Single().RoleID,160}});
    var roleQuote=partitionQuote with{QuoteID=Guid.NewGuid()};quoted.Quotes[roleQuote.QuoteID]=roleQuote;
    try{await service.ReserveAsync(token,new(org.OrgID,org.Policy.Revision,roleQuote.ModelRouteID,roleQuote.QuoteID,"over-role"),CancellationToken.None);throw new Exception("role aggregate ceiling exceeded");}catch(InvalidOperationException error)when(error.Message=="role_ceiling_exceeded"){}
    var foreignOrg=orgs.CreateTrustedOrganisation(owner,"Separate fictional org",BusinessAddOnKind.Business,"fictional-other-billing");
    var foreignQuote=roleQuote with{QuoteID=Guid.NewGuid(),OrgID=foreignOrg.OrgID};quoted.Quotes[foreignQuote.QuoteID]=foreignQuote;
    try{await service.ReserveAsync(token,new(foreignOrg.OrgID,foreignOrg.Policy.Revision,foreignQuote.ModelRouteID,foreignQuote.QuoteID,"foreign-pool"),CancellationToken.None);throw new Exception("cross-org source pool accepted");}catch(UnauthorizedAccessException){}
    await service.RecheckDispatchAsync(token,partitionReservation.ReservationID,org.Policy.Revision,CancellationToken.None);
    try{await service.SettleObservedAsync(new(partitionReservation.ReservationID,"separate-settlement",0,"fixture-provider-observed-usage"),CancellationToken.None);throw new Exception("one provider usage report settled two reservations");}catch(InvalidOperationException error)when(error.Message=="usage_settlement_unavailable"){}
    await service.SettleObservedAsync(new(partitionReservation.ReservationID,"zero-observed-settlement",0,"fixture-provider-observed-zero"),CancellationToken.None);
    await service.ReserveAsync(token,new(org.OrgID,org.Policy.Revision,overQuote.ModelRouteID,overQuote.QuoteID,"expiry-operation"),CancellationToken.None);
    var stateBytes=File.ReadAllText(path);var corrupt=System.Text.Json.Nodes.JsonNode.Parse(stateBytes)!;
    corrupt["State"]!["Lots"]![0]!["Dust"]=-1;var corruptBytes=corrupt.ToJsonString();File.WriteAllText(path,corruptBytes);
    try{service.GetBalance(token,org.OrgID,org.Policy.Revision,pool.PoolID);throw new Exception("corrupt funded lot accepted");}catch(InvalidDataException){}
    Check(File.ReadAllText(path)==corruptBytes,"corrupt funded state preserved without reset");File.WriteAllText(path,stateBytes);
    var nextPool=service.Create(token,org.OrgID,org.Policy.Revision,"fixture-next-period");
    var rolloverPolicy=new FixtureRollover();
    rolloverPolicy.Grant=new(Guid.NewGuid(),"explicit-fictional-billing-policy",7,owner,org.OrgID,pool.PoolID,nextPool.PoolID,funded.Lot!.SourceAllocationID,org.Roles.Single().RoleID,300,now.AddHours(3),now.AddHours(4),false,"explicit-fictional-carryover-window",org.Policy.Revision,org.Revision);
    var rolloverService=new OrganisationDustPools(path,identity,orgs,funded,quoted,null,rolloverPolicy);
    try{await service.RollOverAsync(token,org.OrgID,org.Policy.Revision,"fixture-policy","rollover-operation");throw new Exception("missing approved billing policy guessed");}catch(InvalidOperationException error)when(error.Message=="versioned_billing_rollover_policy_unconfigured"){}
    try{await rolloverService.RollOverAsync(token,org.OrgID,org.Policy.Revision,"fixture-policy","rollover-operation");throw new Exception("inherited disallow ignored");}catch(UnauthorizedAccessException error)when(error.Message=="role_rollover_disallowed"){}
    pool=service.ConfigureRolloverRules(token,org.OrgID,org.Policy.Revision,pool.PoolID,pool.Revision,new Dictionary<Guid,OrganisationRolloverChoice>{{org.Roles.Single().RoleID,OrganisationRolloverChoice.Allow}});
    var carried=await rolloverService.RollOverAsync(token,org.OrgID,org.Policy.Revision,"fixture-policy","rollover-operation");
    var replay=await rolloverService.RollOverAsync(token,org.OrgID,org.Policy.Revision,"fixture-policy","rollover-operation");
    Check(carried==replay&&carried.Dust==300&&carried.OriginalExpiry==funded.Lot.ExpiresAt&&carried.BillingPolicyVersion==7,"rollover retains source expiry and approved policy provenance exactly once");
    var parallelCarries=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>Task.Run(async()=>await new OrganisationDustPools(path,identity,orgs,funded,quoted,null,rolloverPolicy).RollOverAsync(token,org.OrgID,org.Policy.Revision,"fixture-policy","rollover-operation"))));
    Check(parallelCarries.All(t=>t==carried),"parallel rollover acknowledgements conserve one transferred lot");
    var approvedGrant=rolloverPolicy.Grant;
    rolloverPolicy.Grant=approvedGrant with{GrantID=Guid.NewGuid()};
    try{await rolloverService.RollOverAsync(token,org.OrgID,org.Policy.Revision,"fixture-policy","duplicate-cap-operation");throw new Exception("billing carryover cap reused through another grant");}catch(InvalidOperationException error)when(error.Message=="billing_policy_carryover_cap_exhausted"){}
    rolloverPolicy.Grant=approvedGrant with{MaximumDust=301};
    try{await rolloverService.RollOverAsync(token,org.OrgID,org.Policy.Revision,"fixture-policy","rollover-operation");throw new Exception("changed grant replay accepted");}catch(InvalidOperationException error)when(error.Message=="rollover_replay_conflict"){}
    rolloverPolicy.Grant=approvedGrant;
    var originalGrant=rolloverPolicy.Grant;
    rolloverPolicy.Grant=originalGrant with{GrantID=Guid.NewGuid()};
    try{await rolloverService.RollOverAsync(token,org.OrgID,org.Policy.Revision,"fixture-policy","second-grant-same-window");throw new Exception("policy carryover cap spent twice using new grant");}catch(InvalidOperationException error)when(error.Message=="billing_policy_carryover_cap_exhausted"){}
    rolloverPolicy.Grant=originalGrant;
    var carriedBalance=service.GetBalance(token,org.OrgID,org.Policy.Revision,nextPool.PoolID);
    Check(carriedBalance.FundedDust==0&&carriedBalance.RolledOverDust==300&&carriedBalance.AvailableDust==300,"rolled-over credits are separate from new paid funding");
    var sourceBalance=service.GetBalance(token,org.OrgID,org.Policy.Revision,pool.PoolID);
    Check(sourceBalance.AvailableDust==540&&sourceBalance.TransferredDust==300&&sourceBalance.ReservedDust==100&&sourceBalance.SettledDust==60,"rollover debits original uncommitted credits without copying or changing inflight/settled usage");
    var carryQuote=new VerifiedOrganisationCostQuote(Guid.NewGuid(),owner,org.OrgID,nextPool.PoolID,"fictional-model-route",org.Policy.Revision,100,now.AddMinutes(10),"fixture",new("fixture.app","fixture.model",null,null));quoted.Quotes[carryQuote.QuoteID]=carryQuote;
    var carryReservation=await rolloverService.ReserveAsync(token,new(org.OrgID,org.Policy.Revision,carryQuote.ModelRouteID,carryQuote.QuoteID,"carry-operation"),CancellationToken.None);
    nextPool=service.ConfigureRolloverRules(token,org.OrgID,org.Policy.Revision,nextPool.PoolID,nextPool.Revision,new Dictionary<Guid,OrganisationRolloverChoice>{{org.Roles.Single().RoleID,OrganisationRolloverChoice.Disallow}});
    try{await rolloverService.RecheckDispatchAsync(token,carryReservation.ReservationID,org.Policy.Revision,CancellationToken.None);throw new Exception("changed rollover permission dispatched");}catch(UnauthorizedAccessException error)when(error.Message=="rollover_permission_changed"){}
    Check(service.GetEffectiveBalance(token,org.OrgID,org.Policy.Revision,nextPool.PoolID).EffectiveAvailableDust==0,"member effective balance honours current role disallow");
    var ownRollover=service.GetOwnRolloverCredits(token,org.OrgID,org.Policy.Revision,nextPool.PoolID).Single();
    Check(ownRollover.CurrentRoleSetting==OrganisationRolloverChoice.Disallow&&!ownRollover.AllowedToUse&&ownRollover.AvailableDust==0&&ownRollover.BillingPolicyVersion==7&&ownRollover.OriginalExpiry==funded.Lot.ExpiresAt,"member sees effective permission, policy version and original/resulting expiry without other members' work");
    await rolloverService.CancelAsync(carryReservation.ReservationID,"carry-cancel",CancellationToken.None);
    nextPool=service.ConfigureRolloverRules(token,org.OrgID,org.Policy.Revision,nextPool.PoolID,nextPool.Revision,new Dictionary<Guid,OrganisationRolloverChoice>{{org.Roles.Single().RoleID,OrganisationRolloverChoice.Allow}});
    var thirdPool=service.Create(token,org.OrgID,org.Policy.Revision,"fixture-third-period");
    rolloverPolicy.Grant=approvedGrant with{GrantID=Guid.NewGuid(),BillingPolicyVersion=8,SourcePoolID=nextPool.PoolID,TargetPoolID=thirdPool.PoolID,SourceAllocationID=carried.ChildAllocationID,MaximumDust=200,ResultingExpiry=now.AddHours(6),CarryoverWindowID="explicit-fictional-next-carryover-window"};
    try{await rolloverService.RollOverAsync(token,org.OrgID,org.Policy.Revision,"fixture-policy","successive-operation");throw new Exception("successive rollover guessed without policy");}catch(InvalidOperationException error)when(error.Message=="successive_rollover_disallowed_by_billing_policy"){}
    rolloverPolicy.Grant=rolloverPolicy.Grant with{AllowPreviouslyRolledOver=true};
    var successive=await rolloverService.RollOverAsync(token,org.OrgID,org.Policy.Revision,"fixture-policy","successive-operation");
    Check(successive.OriginalSourceAllocationID==funded.Lot.SourceAllocationID&&successive.OriginalExpiry==funded.Lot.ExpiresAt&&successive.Dust==200,"explicit approved successive rollover preserves original funding source and expiry through chain");
    Check(Service().GetBalance(token,org.OrgID,org.Policy.Revision,nextPool.PoolID).AvailableDust==100&&Service().GetBalance(token,org.OrgID,org.Policy.Revision,thirdPool.PoolID).AvailableDust==200,"successive rollover conserves outstanding300units across restart");
    var expiryService=new OrganisationDustPools(path,identity,orgs,funded,quoted,new FixedPoolClock(now.AddHours(2)));
    var expired=expiryService.GetBalance(token,org.OrgID,org.Policy.Revision,pool.PoolID);
    Check(expired.ReservedDust==0&&expired.SettledDust==60&&expired.AvailableDust==0,"expiry releases only never-dispatched reservations and preserves original settled provenance");
    await expiryService.FundAsync(token,org.OrgID,org.Policy.Revision,pool.PoolID,"fictional-paid-allocation");
    Check(expiryService.GetBalance(token,org.OrgID,org.Policy.Revision,pool.PoolID).FundedDust==1000,"late acknowledgement retry recognises already-funded source without resurrecting expired credits");
    var correctedClockBalance=service.GetBalance(token,org.OrgID,org.Policy.Revision,pool.PoolID);
    Check(correctedClockBalance.ReservedDust==0&&correctedClockBalance.AvailableDust==0,"persisted expiry cannot resurrect reservations or funded lots after clock correction");
    var expiryCode=identity.AuthorizeAuthenticatedAccount(new(owner,"Fictional owner"),"fixture",redirect,CakeIdentityService.Challenge(verifier));
    var shortSession=identity.Exchange(expiryCode,"fixture",redirect,verifier,"Explicit expiry fixture");
    var identityPath=Path.Combine(root,"identity.json");var identityEnvelope=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(identityPath))!;
    var expiresAt=DateTimeOffset.UtcNow.AddSeconds(2);
    identityEnvelope["State"]!["Sessions"]!.AsArray().Single(n=>n!["SessionID"]!.GetValue<Guid>()==shortSession.Session.SessionID)!["ExpiresAt"]=expiresAt;
    File.WriteAllText(identityPath,identityEnvelope.ToJsonString());
    static string MutexName(string file){var canonical=Path.GetFullPath(file);if(OperatingSystem.IsWindows())canonical=canonical.ToUpperInvariant();return "9to1-state-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));}
    using(var heldPool=new Mutex(false,MutexName(path)))
    using(var observedIdentity=new Mutex(false,MutexName(identityPath)))
    {
        heldPool.WaitOne();
        var delayed=Task.Run(()=>service.GetBalance(shortSession.AccessToken,org.OrgID,org.Policy.Revision,pool.PoolID));
        try
        {
            Check(SpinWait.SpinUntil(()=>{if(!observedIdentity.WaitOne(0))return true;observedIdentity.ReleaseMutex();return false;},1000),"fixture observes session authenticated and held while waiting for actual pool lease");
            Thread.Sleep(Math.Max(0,(int)(expiresAt-DateTimeOffset.UtcNow).TotalMilliseconds)+40);
        }
        finally{heldPool.ReleaseMutex();}
        try{delayed.GetAwaiter().GetResult();throw new Exception("session expired during pool wait retained authority");}catch(UnauthorizedAccessException error)when(error.Message=="session_expired_while_waiting_for_resource"){}
    }
    identity.SignOut(token);
    try{await service.RecheckDispatchAsync(token,partitionReservation.ReservationID,org.Policy.Revision,CancellationToken.None);throw new Exception("revoked session dispatched funded work");}catch(UnauthorizedAccessException){}
    Console.WriteLine("PASS fictional funded-org ledger: single-source funding, parallel total ceiling, partition/role plan, actual observed settlement, restart/replay, unsafe transfer denial, session revocation");
}
finally{Directory.Delete(root,true);}
static async Task CancelledAuthoritiesPreserveState(string root,CakeIdentityService identity,OrganisationService orgs,
    string token,Organisation org,Guid owner,DateTimeOffset now)
{
    var failures=new List<string>();
    foreach(var (waitForPool,operation,injectFailure) in new[]{(false,"fund",false),(false,"reserve",false),(true,"fund",false),(true,"reserve",false),(false,"fund",true)})
    {
        var path=Path.Combine(root,"cancel-"+operation+"-"+waitForPool+"-"+injectFailure+".json");
        var funding=new FixtureFunding();var quotes=new FixtureQuotes();
        var original=new OrganisationDustPools(path,identity,orgs,funding,quotes);
        var pool=original.Create(token,org.OrgID,org.Policy.Revision,"explicit-fictional-cancellation-period");
        funding.Lot=new(Guid.NewGuid(),org.OrgID,pool.PeriodID,1000,now,now.AddHours(1),"fictional-cancellation-seed");
        await original.FundAsync(token,org.OrgID,org.Policy.Revision,pool.PoolID,funding.Lot.FundingReference);
        var gate=new CancellationAuthorityGate();
        var lot=new VerifiedOrganisationAllocation(Guid.NewGuid(),org.OrgID,pool.PeriodID,37,now,now.AddHours(1),"fictional-cancelled-funding");
        var quote=new VerifiedOrganisationCostQuote(Guid.NewGuid(),owner,org.OrgID,pool.PoolID,"fictional-model-route",org.Policy.Revision,23,now.AddMinutes(10),"fixture",new("fixture.app","fixture.model",null,null));
        var delayed=new OrganisationDustPools(path,identity,orgs,new GatedFunding(lot,gate),new GatedQuotes(quote,gate));
        var before=File.ReadAllBytes(path);using var cancellation=new CancellationTokenSource();
        using var poolMutex=new Mutex(false,CancellationMutexName(path));
        using var identityMutex=new Mutex(false,CancellationMutexName(Path.Combine(root,"identity.json")));
        if(waitForPool)poolMutex.WaitOne();
        Task<bool>? pending=null;var errors=new List<Exception>();
        var injected=new InvalidOperationException("explicit gated fixture body failure");
        var refused=false;
        try
        {
            pending=Task.Run(async()=>
            {
                try
                {
                    if(operation=="fund")await delayed.FundAsync(token,org.OrgID,org.Policy.Revision,pool.PoolID,lot.FundingReference,cancellation.Token);
                    else await delayed.ReserveAsync(token,new(org.OrgID,org.Policy.Revision,quote.ModelRouteID,quote.QuoteID,"fictional-cancelled-operation"),cancellation.Token);
                    return false;
                }
                catch(OperationCanceledException error)when(error.CancellationToken==cancellation.Token){return true;}
            });
            if(waitForPool)
            {
                // Keep the native mutex on this same thread until release; no await while held.
                gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                gate.Release.TrySetResult();
                Check(SpinWait.SpinUntil(()=>{if(!identityMutex.WaitOne(0))return true;identityMutex.ReleaseMutex();return false;},5000),
                    "actual current identity lease acquired while waiting for canonical pool lease");
                cancellation.Cancel();
            }
            else
            {
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if(injectFailure)throw injected;
                cancellation.Cancel();gate.Release.TrySetResult();
            }
        }
        catch(Exception error){errors.Add(error);}
        finally
        {
            // Independent collectors retain original failures while always unblocking/draining work.
            if(errors.Count!=0)try{cancellation.Cancel();}catch(Exception error){errors.Add(error);}
            try{gate.Release.TrySetResult();}catch(Exception error){errors.Add(error);}
            if(waitForPool)try{poolMutex.ReleaseMutex();}catch(Exception error){errors.Add(error);}
        }
        if(pending is not null)try{refused=await pending;}catch(Exception error){errors.Add(error);}
        if(!injectFailure&&errors.Count!=0)throw new AggregateException("cancellation fixture body/release/pending failures",errors);
        try
        {
        var unchanged=File.ReadAllBytes(path).SequenceEqual(before);
        var restarted=new OrganisationDustPools(path,identity,orgs,funding,quotes).GetBalance(token,org.OrgID,org.Policy.Revision,pool.PoolID);
        var preserved=unchanged&&File.ReadAllBytes(path).SequenceEqual(before)&&restarted.FundedDust==1000&&restarted.ReservedDust==0;
        if(injectFailure)
        {
            Check(errors.Count==1&&ReferenceEquals(errors[0],injected)&&pending is {IsCompleted:true}&&refused&&preserved,
                "gated fixture retains exact original failure after pending drains without state writes");
            Console.WriteLine("PASS: injected gated fixture body failure retained after original pending operation drained with unchanged state");
            continue;
        }
        Console.WriteLine($"Cancellation control {operation} waitForPool={waitForPool}: cancelled={refused}, unchanged={preserved}");
        if(!refused||!preserved)failures.Add(operation+"/"+waitForPool);
        }
        catch(Exception error)when(errors.Count!=0)
        {errors.Add(error);throw new AggregateException("cancellation fixture original and verification failures",errors);}
    }
    Check(failures.Count==0,"cancelled authority/pool-wait operations must preserve original bytes and restart: "+string.Join(",",failures));
    Console.WriteLine("PASS: four original funding/reservation cancellation controls preserve canonical bytes and restart");
}
static string CancellationMutexName(string file)
{
    var canonical=Path.GetFullPath(file);if(OperatingSystem.IsWindows())canonical=canonical.ToUpperInvariant();
    return "9to1-state-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
}
sealed class CancellationAuthorityGate
{
    public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask<T> ResolveAsync<T>(T value)
    {Entered.TrySetResult();await Release.Task;return value;} // Deliberately valid non-cooperative authority.
}
sealed class GatedFunding(VerifiedOrganisationAllocation lot,CancellationAuthorityGate gate):IOrganisationAllocationFundingAuthority
{
    public async ValueTask<VerifiedOrganisationAllocation?> ResolveAsync(string reference,CancellationToken ct)=>await gate.ResolveAsync(lot);
}
sealed class GatedQuotes(VerifiedOrganisationCostQuote quote,CancellationAuthorityGate gate):IOrganisationCostQuoteAuthority
{
    public async ValueTask<VerifiedOrganisationCostQuote?> ResolveAsync(Guid id,CancellationToken ct)=>await gate.ResolveAsync(quote);
}
sealed class FixtureFunding:IOrganisationAllocationFundingAuthority
{
    public VerifiedOrganisationAllocation? Lot{get;set;}
    public ValueTask<VerifiedOrganisationAllocation?> ResolveAsync(string reference,CancellationToken ct)=>ValueTask.FromResult(Lot is {} lot&&lot.FundingReference==reference?lot:null);
}
sealed class FixtureQuotes:IOrganisationCostQuoteAuthority
{
    public ConcurrentDictionary<Guid,VerifiedOrganisationCostQuote> Quotes{get;}=new();
    public ValueTask<VerifiedOrganisationCostQuote?> ResolveAsync(Guid id,CancellationToken ct)=>ValueTask.FromResult(Quotes.TryGetValue(id,out var quote)?quote:null);
}

sealed class FixedPoolClock(DateTimeOffset time):TimeProvider
{public override DateTimeOffset GetUtcNow()=>time;}

sealed class FixtureRollover:IOrganisationRolloverPolicyAuthority
{
    public VerifiedOrganisationRolloverGrant? Grant{get;set;}
    public ValueTask<VerifiedOrganisationRolloverGrant?> ResolveAsync(string reference,CancellationToken ct)=>ValueTask.FromResult(reference=="fixture-policy"?Grant:null);
}
