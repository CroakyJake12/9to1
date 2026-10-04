using NineToOne.Accounts;
var root=Path.Combine(Path.GetTempPath(),"c6-cancel-"+Guid.NewGuid());Directory.CreateDirectory(root);
try {
    var owner=Guid.NewGuid();var redirect="http://127.0.0.1/fixture-callback";
    var identity=new CakeIdentityService(Path.Combine(root,"identity.json"),new Dictionary<string,IReadOnlySet<string>>{["fixture"]=new HashSet<string>{redirect}});
    var verifier=new string('a',64);var code=identity.AuthorizeAuthenticatedAccount(new(owner,"Fictional owner"),"fixture",redirect,CakeIdentityService.Challenge(verifier));
    var token=identity.Exchange(code,"fixture",redirect,verifier,"Fixture").AccessToken;
    var orgs=new OrganisationService(Path.Combine(root,"organisations.json"),new ProfileService(Path.Combine(root,"profiles.json"),null));
    var org=orgs.CreateTrustedOrganisation(owner,"Explicit fictional funded organisation",BusinessAddOnKind.Business,"fictional-settled-add-on");
    var funded=new GateFunding();var quoted=new GateQuotes();var path=Path.Combine(root,"pools.json");
    OrganisationDustPools Service()=>new(path,identity,orgs,funded,quoted);
    var service=Service();var pool=service.Create(token,org.OrgID,org.Policy.Revision,"fixture-period");
    var now=DateTimeOffset.UtcNow;funded.Lot=new(Guid.NewGuid(),org.OrgID,pool.PeriodID,1000,now,now.AddHours(1),"fictional-paid-allocation");

using var fc=new CancellationTokenSource(); var f=service.FundAsync(token,org.OrgID,org.Policy.Revision,pool.PoolID,"fictional-paid-allocation",fc.Token).AsTask();
await funded.Entered.Task;fc.Cancel();funded.Gate.SetResult(funded.Lot);try{await f;throw new Exception("cancelled funding completed");}catch(OperationCanceledException e)when(e.CancellationToken==fc.Token){}
if(service.GetBalance(token,org.OrgID,org.Policy.Revision,pool.PoolID).FundedDust!=0)throw new Exception("cancelled funding mutated"); Console.WriteLine("PASS cancelled funding OCE and funded0");await service.FundAsync(token,org.OrgID,org.Policy.Revision,pool.PoolID,"fictional-paid-allocation");
var q=new VerifiedOrganisationCostQuote(Guid.NewGuid(),owner,org.OrgID,pool.PoolID,"fixture-route",org.Policy.Revision,100,now.AddMinutes(10),"fixture",new("fixture.app","fixture.model",null,null));
using var rc=new CancellationTokenSource();var t=service.ReserveAsync(token,new(org.OrgID,org.Policy.Revision,q.ModelRouteID,q.QuoteID,"cancelled-reservation"),rc.Token).AsTask();await quoted.Entered.Task;rc.Cancel();quoted.Gate.SetResult(q);try{await t;throw new Exception("cancelled reservation completed");}catch(OperationCanceledException e)when(e.CancellationToken==rc.Token){}
if(service.GetBalance(token,org.OrgID,org.Policy.Revision,pool.PoolID).ReservedDust!=0)throw new Exception("cancelled reserve mutated");Console.WriteLine("PASS cancelled reserve OCE and reserved0");
} finally {Directory.Delete(root,true);}
sealed class GateFunding:IOrganisationAllocationFundingAuthority {public VerifiedOrganisationAllocation? Lot;public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<VerifiedOrganisationAllocation?> Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);public async ValueTask<VerifiedOrganisationAllocation?> ResolveAsync(string r,CancellationToken ct){Entered.TrySetResult();return await Gate.Task;}}
sealed class GateQuotes:IOrganisationCostQuoteAuthority {public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource<VerifiedOrganisationCostQuote?> Gate=new(TaskCreationOptions.RunContinuationsAsynchronously);public async ValueTask<VerifiedOrganisationCostQuote?> ResolveAsync(Guid r,CancellationToken ct){Entered.TrySetResult();return await Gate.Task;}}
