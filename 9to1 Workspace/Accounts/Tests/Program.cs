using NineToOne.Accounts;
using System.Text.Json;

static void Assert(bool success, string description) { if (!success) throw new Exception(description); }
var root = Path.Combine(Path.GetTempPath(), "9to1-accounts-" + Guid.NewGuid());
try
{
    var id = Guid.NewGuid();
    var resources = new Resources(80_000, 1000, 1, 3, new Dictionary<string, JsonElement>());
    var state = new SubscriptionState(id, "boost", 1, true, 149.99m, resources, 1);
    var entitlement = SubscriptionPolicy.Evaluate(state);
    Assert(entitlement.EffectiveThresholds.Count == 7 && entitlement.ModelBand == "ultra", "cumulative thresholds");
    Assert(entitlement.PersonalAPI && entitlement.Resources == resources, "resource independence");
    var ledger = new AccountLedger(root); ledger.Provision(state);
    int accepted = 0;
    Parallel.For(0, 20, i => { try { new AccountLedger(root).Reserve(id, "req" + i, 10_000, "ultra"); Interlocked.Increment(ref accepted); }
        catch (InvalidOperationException error) when (error.Message == "dust_exhausted") { } });
    Assert(accepted == 8 && ledger.Get(id).Usage.DustReserved == 80_000, "concurrent overspend denied");
    var reservation = ledger.Get(id).Reservations[0];
    try { ledger.Reserve(id, reservation.RequestID, reservation.ReservedDust, "boost"); throw new Exception("authorization replay"); }
    catch (InvalidOperationException error) when (error.Message == "idempotency_conflict") { }
    ledger.Settle(id, reservation.ReservationID, 4000); ledger.Settle(id, reservation.ReservationID, 4000);
    Assert(new AccountLedger(root).Get(id).Usage.DustSpent == 4000, "durable idempotent settlement");
    ledger.UpdateHostedUsage(id, 500, 1, 3);
    var ledgerPath=Path.Combine(root,id.ToString("N")+".json");var cleanLedger=File.ReadAllText(ledgerPath);
    foreach(var ledgerCorruptionKind in new[]{"negative-usage","foreign-owner","counter-mismatch","invalid-enum"})
    {
        var json=System.Text.Json.Nodes.JsonNode.Parse(cleanLedger)!;var data=json["State"]!;
        if(ledgerCorruptionKind=="negative-usage")data["Usage"]!["DustSpent"]=-1;
        else if(ledgerCorruptionKind=="foreign-owner")data["Reservations"]![0]!["AccountID"]=Guid.NewGuid().ToString();
        else if(ledgerCorruptionKind=="counter-mismatch")data["Usage"]!["DustReserved"]=0;
        else data["Reservations"]![0]!["State"]=99;
        var invalid=json.ToJsonString();File.WriteAllText(ledgerPath,invalid);
        try{ledger.Reserve(id,"corrupt-attempt",1,"free");throw new Exception("Corrupt ledger accepted: "+ledgerCorruptionKind);}catch(InvalidDataException){}
        Assert(File.ReadAllText(ledgerPath)==invalid,"corrupt ledger preserved: "+ledgerCorruptionKind);
    }
    File.WriteAllText(ledgerPath,cleanLedger);
    if(!OperatingSystem.IsWindows())Assert(File.GetUnixFileMode(ledgerPath)==(UnixFileMode.UserRead|UnixFileMode.UserWrite),"account file private permissions");

    try { ledger.UpdateHostedUsage(id, 501, 0, 0); throw new Exception("quota bypass"); }
    catch (InvalidOperationException error) when (error.Message == "hosted_quota_exceeded") { }
    var created = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    Assert(!SubscriptionPolicy.FreeSiteOnline(created, created, created.AddDays(31)), "initial inactivity");
    Assert(SubscriptionPolicy.FreeSiteOnline(created, created.AddDays(120), created.AddDays(400)), "annual activity");
    Assert(SubscriptionPolicy.ListPresets().All(p => p.Allocation is null), "unknown commercial quantities not invented");
    var identity = new CakeIdentityService(Path.Combine(root, "identity.json"),
        new Dictionary<string, IReadOnlySet<string>> { ["desktop"] = new HashSet<string> { "http://127.0.0.1:48111/callback" } });
    var verifier = new string('a', 64);
    var code = identity.AuthorizeAuthenticatedAccount(new(id, "Tester"), "desktop", "http://127.0.0.1:48111/callback", CakeIdentityService.Challenge(verifier));
    try { identity.Exchange(code, "desktop", "http://127.0.0.1:48111/callback", new string('b',64), "PC"); throw new Exception("PKCE bypass"); }
    catch (UnauthorizedAccessException) { }
    var issued = identity.Exchange(code, "desktop", "http://127.0.0.1:48111/callback", verifier, "PC");
    Assert(identity.GetCurrent(issued.AccessToken).AccountID == id, "authenticated profile");
    Assert(issued.Session.RegisteredClientID=="desktop","verified PKCE client provenance retained for Home caller binding");
    try { identity.Exchange(code, "desktop", "http://127.0.0.1:48111/callback", verifier, "PC"); throw new Exception("code replay"); }
    catch (UnauthorizedAccessException) { }
    Assert(!File.ReadAllText(Path.Combine(root,"identity.json")).Contains(issued.AccessToken), "no plaintext bearer storage");
    var secondCode=identity.AuthorizeAuthenticatedAccount(new(id,"Tester"),"desktop","http://127.0.0.1:48111/callback",CakeIdentityService.Challenge(verifier));
    var secondSession=identity.Exchange(secondCode,"desktop","http://127.0.0.1:48111/callback",verifier,"Second device");
    identity.SignOut(issued.AccessToken);
    var beforeRevokedAttempt=File.ReadAllText(Path.Combine(root,"identity.json"));
    try{new CakeIdentityService(Path.Combine(root,"identity.json"),new Dictionary<string,IReadOnlySet<string>>()).RevokeAllOtherSessions(issued.AccessToken);throw new Exception("revoked caller mutated sessions");}catch(UnauthorizedAccessException){}
    Assert(File.ReadAllText(Path.Combine(root,"identity.json"))==beforeRevokedAttempt,"revoked caller cannot mutate session state");
    Assert(identity.Authenticate(secondSession.AccessToken).SessionID==secondSession.Session.SessionID,"other live session preserved");
    var disk = File.ReadAllText(Path.Combine(root,"identity.json"));
    File.WriteAllText(Path.Combine(root,"identity.json"), disk.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":99"));
    try { identity.Authenticate(issued.AccessToken); throw new Exception("newer schema overwritten"); }
    catch (InvalidDataException) { }
    File.WriteAllText(Path.Combine(root,"identity.json"), disk);
    identity.SignOut(secondSession.AccessToken);
    try { identity.Authenticate(issued.AccessToken); throw new Exception("revocation bypass"); }
    catch (UnauthorizedAccessException) { }
    // Explicit fictional fixture; none of these numbers are production provider costs or commercial defaults.
    var costs = new ApprovedCostModel("fixture-v1", "GBP", 10_000, 0.001m, 0.000000002m, 0.0001m, 0.0000000001m,
        3m, 0.20m, 0.05m, 0.02m, 0.01m, 0.20m, true, 2_000_000, 20_000_000_000_000, true, FeeRounding:ChargeFeeRounding.Exact,FeeModelKind:"FlatFixedAndPercentage",DustDefinitionReference:"fictional-fixture:1x-dust-v1",FullUseCostDefinitionReference:"fictional-fixture:full-use-costs-v1",OperationalServiceLimitsReference:"fictional-fixture:operational-limits-v1");
    var pricing = new SubscriptionBuilder(costs);
    var quote = pricing.Quote(new(1m,null,20_000_000_000)).Quote!;
    Assert(quote.ActualMonthlyDust == 10_000, "real Dust shown alongside multiplier");
    Assert(quote.Breakdown.Margin / quote.Breakdown.TaxExclusiveMonthlyPrice >= 0.15m, "true 15 percent tax exclusive margin");
    Assert(quote.Breakdown.SharedOperatingCost == 3m, "shared cost counted once");
    var fullCost = quote.Breakdown.AIAllowanceCost + quote.Breakdown.StorageCost + quote.Breakdown.InfrastructureCost + quote.Breakdown.SharedOperatingCost;
    Assert(quote.Breakdown.TaxExclusiveMonthlyPrice > fullCost * 1.15m, "margin differs from markup");
    Assert(pricing.Quote(new(null,10_000,20_000_000_000)).Quote!.Breakdown == quote.Breakdown, "Custom same authoritative calculation");
    Assert(pricing.AIOptionQuotes(20_000_000_000)[1].Quote!.Breakdown == quote.Breakdown, "option uses combined calculator");
    Assert(pricing.RevalidateForCheckout(quote).Quote?.QuoteID==quote.QuoteID, "checkout uses exact same calculator and preserves authoritative quote identity");
    Assert(pricing.RevalidateForCheckout(quote with {ActualMonthlyDust=11_000}).Quote is null, "checkout rejects tampered quote");
    Assert(new SubscriptionBuilder(costs with {DustPer1x=null}).Quote(new(1m,null,20_000_000_000)).Blockers.Any(b=>b.Field=="DustPer1x"), "missing costs fail precisely");
    var jacob=Guid.NewGuid();var profiles=new ProfileService(Path.Combine(root,"profiles.json"),jacob);
    var personal=profiles.Update(id,0,JsonSerializer.SerializeToElement(new{name="User",username="normal-user"}));
    Assert(personal.Icon is null&&personal.Pronouns is null&&personal.Job is null,"only Name and Username required");
    var withJob=profiles.Update(id,personal.Revision,JsonSerializer.SerializeToElement(new{job="Developer"}));
    Assert(withJob.Name=="User"&&withJob.Job=="Developer","partial optional update preserves required identity");
    Assert(profiles.Update(id,withJob.Revision,JsonSerializer.SerializeToElement(new{job=(string?)null})).Job is null,"optional Job cleared");
    Assert(!profiles.UsernameAvailable(id,"@cRoAkYjAkE")&&profiles.UsernameAvailable(jacob,"@CroakyJake"),"reserved trusted account only");
    Assert(!profiles.HasAccountBoundFreeBusiness(id)&&profiles.HasAccountBoundFreeBusiness(jacob),"free Business identity-bound");
    var organisations=new OrganisationService(Path.Combine(root,"organisations.json"),profiles);
    var org=organisations.CreateTrustedOrganisation(id,"Fixture company",BusinessAddOnKind.Business,"fixture-approved-billing");
    Assert(org.AddOn.Currency=="USD"&&org.AddOn.MonthlyPrice==5&&org.AddOn.SeatLimit==50,"organisation add-on separate fixed USD no seat fees");
    org=organisations.CreateRole(id,org.OrgID,org.Revision,"member-role","Member",new HashSet<string>{"Admin.Organisations.Get"},new HashSet<string>());
    var role=org.Roles.Single(r=>r.Name=="Member");var invitations=new List<(Guid member,string token)>();
    for(int i=0;i<60;i++){var member=Guid.NewGuid();invitations.Add((member,organisations.Invite(id,org.OrgID,member,[role.RoleID],DateTimeOffset.UtcNow.AddMinutes(5)).Token));}
    int seats=0;Parallel.ForEach(invitations,invite=>{try{organisations.AcceptInvitation(invite.member,invite.token);Interlocked.Increment(ref seats);}catch(InvalidOperationException error)when(error.Message=="seat_limit_reached") {}});
    Assert(seats==49&&organisations.Get(id,org.OrgID).Members.Count==50,"concurrent 51st Business seat rejected");
    var active=organisations.Get(id,org.OrgID);
    try{organisations.SetMemberState(id,org.OrgID,active.Revision,"suspend-owner",id,OrganisationMemberState.Suspended);throw new Exception("last owner lost");}catch(InvalidOperationException error)when(error.Message=="last_owner_protected"){}
    Assert(organisations.ListAudit(id,org.OrgID).All(e=>e.OrgID==org.OrgID)&&organisations.ListAudit(id,org.OrgID).Count>0,"bounded canonical owner audit");
    try{organisations.ListAudit(active.Members.First(m=>m.AccountID!=id).AccountID,org.OrgID);throw new Exception("audit leaked to ungranted member");}catch(UnauthorizedAccessException){}
    var stranger=await organisations.EvaluateAsync(Guid.NewGuid(),org.OrgID,"Admin.Policies.Publish",[],null,CancellationToken.None);Assert(!stranger.Allowed,"nonmember API denied");
    var lifecycleOrg=organisations.CreateTrustedOrganisation(id,"Lifecycle fixture",BusinessAddOnKind.Business,"fixture-existing-paid-addon");
    var lifecycleVerifier=new FixtureBusinessBillingVerifier();var businessBilling=new OrganisationBillingService(organisations,id,lifecycleVerifier);
    var suspension=new VerifiedBusinessBillingTransition("fixture-suspend",id,lifecycleOrg.OrgID,lifecycleOrg.Revision,"business",1,BusinessBillingState.Suspended,DateTimeOffset.UtcNow.AddSeconds(-1),null,"fixture-approved-policy");
    try{await new OrganisationBillingService(organisations,id,null).ProcessProviderEventAsync(new byte[]{1},new Dictionary<string,string>());throw new Exception("unconfigured Business billing accepted");}catch(InvalidOperationException e)when(e.Message=="business_billing_verifier_unconfigured"){}
    try{await businessBilling.ProcessProviderEventAsync(new byte[]{1},new Dictionary<string,string>());throw new Exception("unverified Business billing accepted");}catch(UnauthorizedAccessException){}
    lifecycleVerifier.Event=suspension with{AccountID=Guid.NewGuid()};
    try{await businessBilling.ProcessProviderEventAsync(new byte[]{1},new Dictionary<string,string>());throw new Exception("foreign Business billing accepted");}catch(UnauthorizedAccessException){}
    lifecycleVerifier.Event=suspension with{EffectiveFrom=DateTimeOffset.UtcNow.AddDays(1)};
    try{await businessBilling.ProcessProviderEventAsync(new byte[]{1},new Dictionary<string,string>());throw new Exception("future Business event applied early");}catch(InvalidOperationException e)when(e.Message=="invalid_business_billing_transition"){}
    Assert(organisations.GetBillingConfiguration(id,lifecycleOrg.OrgID).Revision==lifecycleOrg.Revision,"unverified or future billing cannot mutate state");
    lifecycleVerifier.Event=suspension;
    var suspended=await businessBilling.ProcessProviderEventAsync(new byte[]{1},new Dictionary<string,string>());
    Assert(suspended.AddOn.State==BusinessBillingState.Suspended,"verified Business suspension persisted");
    Assert(!(await organisations.EvaluateAsync(id,lifecycleOrg.OrgID,"Admin.Policies.Publish",[],null,default)).Allowed,"suspended Business editing denied");
    Assert(organisations.GetBillingConfiguration(id,lifecycleOrg.OrgID).OrgID==lifecycleOrg.OrgID,"billing recovery remains available under suspension");
    Assert(organisations.PreviewDowngrade(id,lifecycleOrg.OrgID).AddOn.State==BusinessBillingState.Suspended,"billing preview does not reactivate suspended Business");
    Assert((await businessBilling.ProcessProviderEventAsync(new byte[]{1},new Dictionary<string,string>())).Revision==suspended.Revision,"provider retry idempotent");
    lifecycleVerifier.Event=suspension with{State=BusinessBillingState.Active};
    try{await businessBilling.ProcessProviderEventAsync(new byte[]{1},new Dictionary<string,string>());throw new Exception("conflicting Business receipt replay");}catch(InvalidOperationException e)when(e.Message=="business_billing_replay_conflict"){}
    lifecycleVerifier.Event=suspension with{EventID="fixture-expired-recovery",ExpectedRevision=suspended.Revision,State=BusinessBillingState.Active,EffectiveFrom=DateTimeOffset.UtcNow.AddDays(-2),EffectiveUntil=DateTimeOffset.UtcNow.AddDays(-1)};
    var expiredBusiness=await businessBilling.ProcessProviderEventAsync(new byte[]{1},new Dictionary<string,string>());
    Assert(!(await organisations.EvaluateAsync(id,lifecycleOrg.OrgID,"Admin.Policies.Publish",[],null,default)).Allowed,"actual expired entitlement dates enforced without inventing grace");
    Assert(new OrganisationService(Path.Combine(root,"organisations.json"),profiles).GetBillingConfiguration(id,lifecycleOrg.OrgID).Revision==expiredBusiness.Revision,"billing transition survives restart");
    lifecycleVerifier.Event=suspension;await businessBilling.ProcessProviderEventAsync(new byte[]{1},new Dictionary<string,string>());
    Assert(organisations.GetBillingConfiguration(id,lifecycleOrg.OrgID).Revision==expiredBusiness.Revision,"old receipt replay cannot replace later billing state");
    var ownedQuotes=new SubscriptionQuotes(Path.Combine(root,"quotes.json"),pricing,id);var ownedQuote=ownedQuotes.Preview(new(1,null,20_000_000_000)).Quote!;
    Assert(new SubscriptionQuotes(Path.Combine(root,"quotes.json"),pricing,Guid.NewGuid()).Checkout(ownedQuote.QuoteID).Quote is null,"other account quote checkout denied");
    Assert(new SubscriptionBuilder(costs with {AICostPerDust=decimal.MaxValue}).Quote(new(1,null,20_000_000_000)).Blockers.Any(b=>b.Field=="PriceRange"),"overflow structured blocker");
    var roundedQuote=new SubscriptionBuilder(costs with {FeeRounding=ChargeFeeRounding.CeilingMinorUnit,PaymentFeeAppliesTaxInclusiveCharge=false,BillingFeeAppliesTaxInclusiveCharge=true}).Quote(new(1,null,20_000_000_000)).Quote!;
    Assert(roundedQuote.Breakdown.Margin/roundedQuote.Breakdown.TaxExclusiveMonthlyPrice>=.15m,"rounded independent fee bases still minimum margin");
    // Independent full-use matrix, using fictional costs and actual rounded fee bases.
    foreach(var feeRounding in Enum.GetValues<ChargeFeeRounding>())
    foreach(var factor in SubscriptionBuilder.AIMultipliers)
    foreach(var bytes in SubscriptionBuilder.StorageOptionsBytes)
    {
        var model=costs with {FeeRounding=feeRounding,PaymentFeeAppliesTaxInclusiveCharge=false,BillingFeeAppliesTaxInclusiveCharge=true};
        var result=new SubscriptionBuilder(model).Quote(new(factor,null,bytes));
        Assert(result.Quote is not null,"every published resource combination has a configured fixture quote");
        var q=result.Quote!;var b=q.Breakdown;var dust=(long)(factor*10_000);
        var expectedCost=dust*.001m+bytes*.000000002m+dust*.0001m+bytes*.0000000001m+3m;
        decimal Fee(decimal value)=>feeRounding switch {ChargeFeeRounding.Exact=>value,ChargeFeeRounding.NearestMinorUnit=>decimal.Round(value,2,MidpointRounding.AwayFromZero),_=>decimal.Ceiling(value*100)/100};
        var tax=decimal.Round(b.TaxExclusiveMonthlyPrice*.20m,2,MidpointRounding.AwayFromZero);
        var fees=.25m+Fee(b.TaxExclusiveMonthlyPrice*.02m)+Fee((b.TaxExclusiveMonthlyPrice+tax)*.01m);
        Assert(q.ActualMonthlyDust==dust&&b.SharedOperatingCost==3m,"full allocated resource quantities and shared cost once");
        Assert(b.Tax==tax&&b.PaymentFees+b.BillingFees==fees,"independent rounded tax and provider fees");
        Assert(b.TaxExclusiveMonthlyPrice*100==decimal.Truncate(b.TaxExclusiveMonthlyPrice*100),"currency minor units");
        Assert((b.TaxExclusiveMonthlyPrice-expectedCost-fees)/b.TaxExclusiveMonthlyPrice>=.15m,"all combinations guarantee full-use true margin");
    }
    foreach(var fieldModel in new[]{costs with {AICostPerDust=null},costs with {MaximumMonthlyDust=null},costs with {FeeRounding=null}})
        Assert(new SubscriptionBuilder(fieldModel).Quote(new(1,null,20_000_000_000)).Blockers.Count>0,"missing approved model input blocked");
    Assert(new SubscriptionBuilder(costs with {FeeRounding=(ChargeFeeRounding)99}).Quote(new(1,null,20_000_000_000)).Blockers.Any(b=>b.Field=="FeeRounding"),"unknown fee policy rejected");
    Assert(new SubscriptionBuilder(costs with {PaymentFixedFee=.001m}).Quote(new(1,null,20_000_000_000)).Blockers.Any(b=>b.Field=="PaymentFixedFee"),"fractional minor unit fixed fee blocked");
    Assert(new SubscriptionBuilder(costs with {SharedOperatingCost=0,PaymentFixedFee=0,BillingFixedFee=0}).Quote(new(null,0,0)).Blockers.Any(b=>b.Field=="ZeroCharge"),"zero revenue cannot claim a margin");
    var nearZero=new SubscriptionBuilder(costs with {PaymentFeeRate=.8499999999999999999999999999m,BillingFeeRate=0,TaxRate=0}).Quote(new(1,null,20_000_000_000));
    Assert(nearZero.Quote is null&&nearZero.Blockers.Count>0,"near-zero denominator fails bounded with structured blocker");
    var arbitraryScope=await organisations.EvaluateAsync(id,org.OrgID,"Admin.Organisations.Get",["foreign-resource"],null,CancellationToken.None);
    Assert(!arbitraryScope.Allowed&&arbitraryScope.Code=="ObjectScopeDenied","unresolved canonical resource scopes fail closed");
    var ownScope=Guid.NewGuid().ToString("D");var foreignScope=Guid.NewGuid().ToString("D");
    var scopedAuthority=new OrganisationService(Path.Combine(root,"organisations.json"),profiles,new FixtureResourceAuthority(id,org.OrgID,ownScope));
    Assert((await scopedAuthority.EvaluateAsync(id,org.OrgID,"Admin.Organisations.Get",[ownScope],active.Policy.Revision,CancellationToken.None)).Allowed,"registered canonical owner scope allowed");
    Assert(!(await scopedAuthority.EvaluateAsync(id,org.OrgID,"Admin.Organisations.Get",[foreignScope],active.Policy.Revision,CancellationToken.None)).Allowed,"cross-organisation canonical resource denied");
    Assert(!(await scopedAuthority.EvaluateAsync(id,org.OrgID,"Admin.Organisations.Get",[ownScope],active.Policy.Revision-1,CancellationToken.None)).Allowed,"stale policy revision denied");
    var freeOrg=organisations.CreateTrustedOrganisation(jacob,"Identity-bound free fixture",BusinessAddOnKind.BusinessPlus,"fixture-trusted-identity-grant");
    Assert(freeOrg.AddOn.MonthlyPrice==0&&freeOrg.AddOn.SeatLimit is null,"trusted Jacob free Business+ same canonical backend");
    var orgPath=Path.Combine(root,"organisations.json");var orgBytes=File.ReadAllText(orgPath);
    var corrupt=System.Text.Json.Nodes.JsonNode.Parse(orgBytes)!;
    corrupt["State"]!["Organisations"]![0]!["Members"]![0]=null;var corruptBytes=corrupt.ToJsonString();File.WriteAllText(orgPath,corruptBytes);
    try{organisations.Get(id,org.OrgID);throw new Exception("null membership accepted");}catch(InvalidDataException){}
    Assert(File.ReadAllText(orgPath)==corruptBytes,"corrupt organisation preserved and fails typed closed");File.WriteAllText(orgPath,orgBytes);
    // Fictional signed-billing adapter and monthly policy exercise the backend transaction, never production provider assumptions.
    var purchaseAccount=Guid.NewGuid();ledger.Provision(new(purchaseAccount,null,null,true,0,new(0,0,0,0,new Dictionary<string,JsonElement>()),1));
    var purchaseQuotes=new SubscriptionQuotes(Path.Combine(root,"purchase-quotes.json"),pricing,purchaseAccount);
    var purchaseQuote=purchaseQuotes.Preview(new(1,null,20_000_000_000)).Quote!;
    var billingVerifier=new FixtureBillingVerifier();var monthlyPolicy=new FixtureMonthlyPolicy();
    var purchases=new SubscriptionPurchaseService(ledger,purchaseQuotes,purchaseAccount,null,billingVerifier,monthlyPolicy);
    try{new SubscriptionPurchaseService(ledger,purchaseQuotes,purchaseAccount,null,null,monthlyPolicy).Begin(purchaseQuote.QuoteID);throw new Exception("missing verifier accepted");}catch(InvalidOperationException){}
    try{new SubscriptionPurchaseService(ledger,purchaseQuotes,purchaseAccount,null,billingVerifier,null).Begin(purchaseQuote.QuoteID);throw new Exception("missing monthly policy accepted");}catch(InvalidOperationException){}
    try{new SubscriptionPurchaseService(ledger,purchaseQuotes,Guid.NewGuid(),null,billingVerifier,monthlyPolicy).Begin(purchaseQuote.QuoteID);throw new Exception("foreign owned quote context accepted");}catch(InvalidOperationException){}
    var expiryDirectory=Path.Combine(root,"expiry-ledger");var expiryClock=new FixturePurchaseClock(DateTimeOffset.UtcNow);
    var expiryLedger=new AccountLedger(expiryDirectory,expiryClock);expiryLedger.Provision(new(purchaseAccount,null,null,true,0,new(0,0,0,0,new Dictionary<string,JsonElement>()),1));
    var expiryPurchases=new SubscriptionPurchaseService(expiryLedger,purchaseQuotes,purchaseAccount,null,billingVerifier,monthlyPolicy);
    var canonicalExpiryPath=Path.GetFullPath(expiryDirectory);if(OperatingSystem.IsWindows())canonicalExpiryPath=canonicalExpiryPath.ToUpperInvariant();
    var expiryMutexName="9to1-state-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonicalExpiryPath)));
    Task<SubscriptionPurchase> delayedPurchase;
    using(var heldLease=new Mutex(false,expiryMutexName))
    {
        heldLease.WaitOne();
        try
        {
            delayedPurchase=Task.Run(()=>expiryPurchases.Begin(purchaseQuote.QuoteID));
            Assert(expiryClock.ReadStarted.Wait(TimeSpan.FromSeconds(3))&&!delayedPurchase.IsCompleted,"real account lease delays checkout creation");
            expiryClock.AdvanceTo(purchaseQuote.ExpiresAt.AddSeconds(1));
        }
        finally{heldLease.ReleaseMutex();}
    }
    try{await delayedPurchase;throw new Exception("quote expired during account lease accepted");}catch(InvalidOperationException error)when(error.Message=="quote_expired"){}
    Assert((expiryLedger.Get(purchaseAccount).Purchases?.Count??0)==0,"expired lease-delayed quote creates no pending order");
    var purchase=purchases.Begin(purchaseQuote.QuoteID);
    Assert(ledger.Get(purchaseAccount).Subscription.Resources.AIDustAllocated==0,"quote and checkout cannot activate purchased allowance");
    try{await purchases.ProcessProviderEventAsync(ReadOnlyMemory<byte>.Empty,new Dictionary<string,string>());throw new Exception("unverified billing event accepted");}catch(UnauthorizedAccessException){}
    var blockingBilling=new BlockingBillingVerifier();
    var capturedPurchases=new SubscriptionPurchaseService(ledger,purchaseQuotes,purchaseAccount,null,blockingBilling,monthlyPolicy);
    var envelope=new byte[]{1,2,3};var envelopeHeaders=new Dictionary<string,string>{{"signature","original"}};
    var pendingEnvelope=capturedPurchases.ProcessProviderEventAsync(envelope,envelopeHeaders).AsTask();
    await blockingBilling.Entered.Task;envelope[0]=9;envelopeHeaders["signature"]="replacement";blockingBilling.Release.SetResult();
    try{await pendingEnvelope;throw new Exception("unverified captured billing event accepted");}catch(UnauthorizedAccessException){}
    Assert(blockingBilling.ObservedBytes!.SequenceEqual(new byte[]{1,2,3})&&blockingBilling.ObservedSignature=="original","provider event and signature snapshot survive caller mutation across verification await");
    billingVerifier.Receipt=new(purchase.PurchaseID,purchaseAccount,"fictional-settlement-1",purchaseQuote.Currency,purchaseQuote.Breakdown.FinalMonthlyCharge+0.01m,DateTimeOffset.UtcNow);
    try{await purchases.ProcessProviderEventAsync(ReadOnlyMemory<byte>.Empty,new Dictionary<string,string>());throw new Exception("incorrect final charge accepted");}catch(InvalidOperationException){}
    billingVerifier.Receipt=billingVerifier.Receipt with{ChargedAmount=purchaseQuote.Breakdown.FinalMonthlyCharge};
    var activated=await purchases.ProcessProviderEventAsync(ReadOnlyMemory<byte>.Empty,new Dictionary<string,string>());
    var activatedAccount=ledger.Get(purchaseAccount);
    Assert(activated.State==SubscriptionPurchaseState.Activated&&activatedAccount.Subscription.Resources.AIDustAllocated==purchaseQuote.ActualMonthlyDust&&activatedAccount.Subscription.Resources.StorageAllocatedBytes==purchaseQuote.Selection.StorageBytes,"verified full purchased resources activate atomically");
    Assert(activatedAccount.Subscription.Revision==2&&activatedAccount.Subscription.Resources.HostedSiteLimit==0,"activation preserves unrelated quotas and increments revision once");
    var restartedPurchases=new SubscriptionPurchaseService(new AccountLedger(root),purchaseQuotes,purchaseAccount,null,billingVerifier,monthlyPolicy);
    await restartedPurchases.ProcessProviderEventAsync(ReadOnlyMemory<byte>.Empty,new Dictionary<string,string>());
    Assert(ledger.Get(purchaseAccount).Subscription.Revision==2,"settled event replay across restart is idempotent");
    Parallel.For(0,8,_=>restartedPurchases.ProcessProviderEventAsync(ReadOnlyMemory<byte>.Empty,new Dictionary<string,string>()).AsTask().GetAwaiter().GetResult());
    Assert(ledger.Get(purchaseAccount).Subscription.Revision==2,"concurrent verified webhook replay cannot double activate");
    billingVerifier.Receipt=billingVerifier.Receipt with{AccountID=Guid.NewGuid()};
    try{await purchases.ProcessProviderEventAsync(ReadOnlyMemory<byte>.Empty,new Dictionary<string,string>());throw new Exception("foreign signed account accepted");}catch(UnauthorizedAccessException){}
    billingVerifier.Receipt=billingVerifier.Receipt with{AccountID=purchaseAccount,SettlementID="conflicting-replay"};
    try{await purchases.ProcessProviderEventAsync(ReadOnlyMemory<byte>.Empty,new Dictionary<string,string>());throw new Exception("conflicting receipt replay accepted");}catch(InvalidOperationException){}
    Console.WriteLine("PASS: thresholds, resource independence, concurrent reservations, restart, idempotency, hosting quotas, inactivity, unresolved presets");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

// Explicit fictional canonical-resource authority for boundary tests, never a production ACL.
sealed class FixtureResourceAuthority(Guid owner,Guid org,string scope):IOrganisationObjectScopeResolver
{
    public bool Allows(Guid account,Guid organisation,string action,string canonicalScope)
        =>account==owner&&organisation==org&&canonicalScope==scope;
}

sealed class FixtureBillingVerifier:ITrustedBillingSettlementVerifier
{
    public VerifiedBillingSettlement? Receipt {get;set;}
    public ValueTask<VerifiedBillingSettlement?> VerifyAsync(ReadOnlyMemory<byte> data,IReadOnlyDictionary<string,string> headers,CancellationToken ct)=>ValueTask.FromResult(Receipt);
}
sealed class FixtureMonthlyPolicy:IEntitlementMonthlyPricePolicy
{
    public decimal Resolve(SubscriptionPurchase purchase,VerifiedBillingSettlement settlement)=>purchase.Quote.Breakdown.TaxExclusiveMonthlyPrice;
}

sealed class FixturePurchaseClock(DateTimeOffset now):TimeProvider
{
    private long ticks=now.UtcTicks;
    public ManualResetEventSlim ReadStarted {get;}=new(false);
    public override DateTimeOffset GetUtcNow(){var value=new DateTimeOffset(Interlocked.Read(ref ticks),TimeSpan.Zero);ReadStarted.Set();return value;}
    public void AdvanceTo(DateTimeOffset value)=>Interlocked.Exchange(ref ticks,value.UtcTicks);
}

sealed class BlockingBillingVerifier:ITrustedBillingSettlementVerifier
{
    public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public byte[]? ObservedBytes {get;private set;}
    public string? ObservedSignature {get;private set;}
    public async ValueTask<VerifiedBillingSettlement?> VerifyAsync(ReadOnlyMemory<byte> data,IReadOnlyDictionary<string,string> headers,CancellationToken ct)
    {Entered.SetResult();await Release.Task.WaitAsync(ct);ObservedBytes=data.ToArray();ObservedSignature=headers["signature"];return null;}
}

sealed class FixtureBusinessBillingVerifier:ITrustedBusinessBillingVerifier
{
    public VerifiedBusinessBillingTransition? Event {get;set;}
    public ValueTask<VerifiedBusinessBillingTransition?> VerifyAsync(ReadOnlyMemory<byte> data,IReadOnlyDictionary<string,string> headers,CancellationToken ct)=>ValueTask.FromResult(Event);
}
