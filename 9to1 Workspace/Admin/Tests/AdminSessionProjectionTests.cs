using System.Net;
using System.Net.Http.Json;
using NineToOne.Accounts;
namespace NineToOne.Admin.Tests;

public sealed class AdminSessionProjectionTests
{
    [Fact]
    public async Task Selecting_another_org_clears_every_projection_before_audit_refresh()
    {
        using var fixture=new Fixture();await fixture.OpenA();
        var changed=new List<string>();fixture.Controller.PropertyChanged+=(_,e)=>changed.Add(e.PropertyName!);
        fixture.Controller.TrySetValue("OrganisationID",fixture.OrgB.ToString());
        fixture.AssertOrganisationEmpty();
        foreach(var field in new[]{"OrganisationName","BillingSummary","Members","Policy","Audit"})Assert.Contains(field,changed);
        await fixture.Controller.DispatchAsync("RefreshAudit",null);
        Assert.Equal(fixture.OrgB,Assert.Single(fixture.Controller.Audit).OrgID);
        Assert.Empty(fixture.Controller.Members);Assert.Empty(fixture.Controller.Policy);Assert.Equal("Choose an organisation",fixture.Value("OrganisationName"));
    }
    [Fact]
    public async Task Delayed_old_org_cannot_repopulate_even_after_selection_returns_to_same_id()
    {
        using var fixture=new Fixture();await fixture.OpenA();fixture.Hold="Organisations.Get";
        var pending=fixture.Controller.DispatchAsync("OpenOrganisation",null).AsTask();await fixture.Arrived.Task;
        fixture.Controller.TrySetValue("OrganisationID",fixture.OrgB.ToString());fixture.Controller.TrySetValue("OrganisationID",fixture.OrgA.ToString());
        fixture.Release.SetResult();await pending;fixture.AssertOrganisationEmpty();
    }
    [Theory]
    [InlineData("api/account/current")]
    [InlineData("Organisations.Get")]
    public async Task Token_change_during_response_discards_old_account_and_all_projections(string delayed)
    {
        using var fixture=new Fixture();await fixture.OpenA();fixture.Hold=delayed;
        var pending=fixture.Controller.DispatchAsync("OpenOrganisation",null).AsTask();await fixture.Arrived.Task;
        fixture.Token="token-b";fixture.Release.SetResult();await pending;
        fixture.AssertOrganisationEmpty();Assert.Empty(fixture.Controller.Organisations);Assert.Equal("",fixture.Value("AccountName"));Assert.Equal("",fixture.Value("OrganisationID"));
        fixture.Hold=null;await fixture.Controller.DispatchAsync("Refresh",null);
        Assert.Equal("Account B",fixture.Value("AccountName"));Assert.Equal(fixture.OrgB.ToString("D"),Assert.Single(fixture.Controller.Organisations).OrgID);
        fixture.AssertOrganisationEmpty();
    }
    [Fact]
    public async Task Changed_server_account_identity_clears_prior_org_even_with_same_token()
    {
        using var fixture=new Fixture();await fixture.OpenA();fixture.AccountOverride=fixture.AccountB;
        await fixture.Controller.DispatchAsync("Refresh",null);
        Assert.Equal("Account B",fixture.Value("AccountName"));fixture.AssertOrganisationEmpty();Assert.Equal("",fixture.Value("OrganisationID"));
    }
    private sealed class Fixture:HttpMessageHandler
    {
        public readonly Guid OrgA=Guid.NewGuid(),OrgB=Guid.NewGuid(),AccountA=Guid.NewGuid(),AccountB=Guid.NewGuid();
        public string Token="token-a";public string? Hold;public Guid? AccountOverride;
        public readonly TaskCompletionSource Arrived=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HttpClient _http;public AdminCuiController Controller {get;}
        public Fixture(){_http=new(this,disposeHandler:false){BaseAddress=new("https://fixture.invalid/")};Controller=new(new(_http,_=>ValueTask.FromResult<string?>(Token)));}
        public async Task OpenA(){await Controller.DispatchAsync("Refresh",null);Controller.TrySetValue("OrganisationID",OrgA.ToString());await Controller.DispatchAsync("OpenOrganisation",null);Assert.Equal("Organisation A",Value("OrganisationName"));Assert.NotEmpty(Controller.Members);Assert.NotEmpty(Controller.Policy);}
        public object? Value(string key){Controller.TryGetValue(key,out var value);return value;}
        public void AssertOrganisationEmpty(){Assert.Equal("Choose an organisation",Value("OrganisationName"));Assert.Equal("",Value("BillingSummary"));Assert.Empty(Controller.Members);Assert.Empty(Controller.Policy);Assert.Empty(Controller.Audit);}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;var account=AccountOverride??(request.Headers.Authorization!.Parameter=="token-a"?AccountA:AccountB);
            object result;
            if(path.EndsWith("api/account/current"))result=new CakeProfile(account,account==AccountA?"Account A":"Account B");
            else if(path.EndsWith("Organisations.List"))result=new[]{Organisation(account==AccountA?OrgA:OrgB)};
            else
            {
                using var body=System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));var org=body.RootElement.GetProperty("orgID").GetGuid();
                result=path.EndsWith("Audit.List")?new[]{new OrganisationAudit(Guid.NewGuid(),org,account,"fixture",null,1,2,DateTimeOffset.UtcNow,null)}:Organisation(org);
            }
            if(Hold is not null&&path.EndsWith(Hold,StringComparison.Ordinal)){Arrived.TrySetResult();await Release.Task.WaitAsync(ct);}
            return new(HttpStatusCode.OK){Content=JsonContent.Create(result,result.GetType(),options:AccountContractJson.CreateOptions(System.Text.Json.JsonSerializerDefaults.Web))};
        }
        private Organisation Organisation(Guid id)=>new(id,id==OrgA?"Organisation A":"Organisation B",
            new("fixture",1,"USD",5,4,BusinessBillingState.Active,DateTimeOffset.UtcNow,null),
            [new(Guid.NewGuid(),AccountA,[],OrganisationMemberState.Active,1)],[],new(Guid.NewGuid(),1,new HashSet<string>(),new Dictionary<string,string>{{"fixture","enabled"}},new Dictionary<string,string>(),"Active"),1);
        protected override void Dispose(bool disposing){if(disposing)_http?.Dispose();base.Dispose(disposing);}
    }
}
