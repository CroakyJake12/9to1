using NineToOne.Admin;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NineToOne.Accounts;
using NineToOne.Web;
using HavenOS.Files;

static void Check(bool value,string message){if(!value)throw new Exception(message);}
var root=Path.Combine(Path.GetTempPath(),"9to1-web-"+Guid.NewGuid());Directory.CreateDirectory(root);
Process? host=null;
var browserFixture=args.Contains("--browser-fixture");
try
{
    var id=Guid.NewGuid();var location=Guid.NewGuid();var filesState=Path.Combine(root,"files.json");
    var provider=new DurableDriveProvider(filesState,new(location),id.ToString("N"));
    var folder=HostedItemId.New();var now=DateTimeOffset.UtcNow;
    var operation=new FilesOperation(new(Guid.NewGuid()),id.ToString("N"),folder,null,null,"CreateFolder",null,null,FilesOperationState.Pending,now,now,null,null);
    Check((await provider.MutateAsync(operation,"Sites",CancellationToken.None)).IsSuccess,"canonical Files folder");
    var sites=Path.Combine(root,"Sites");Directory.CreateDirectory(sites);
    var resolver=new FilesWorkspaceDirectoryResolver(Path.Combine(root,"files-workspace-bindings.json"),account=>account==id?provider:null);
    Check((await resolver.RegisterAsync(id,folder,"sites",sites)).IsSuccess,"trusted Files binding");
    var redirect="http://127.0.0.1:48111/callback";
    var identity=new CakeIdentityService(Path.Combine(root,"identity.json"),new Dictionary<string,IReadOnlySet<string>>{["test"]=new HashSet<string>{redirect}});
    var verifier=new string('a',64);var code=identity.AuthorizeAuthenticatedAccount(new(id,"Fixture"),"test",redirect,CakeIdentityService.Challenge(verifier));
    var issued=identity.Exchange(code,"test",redirect,verifier,"Fixture device");
    var ledger=new AccountLedger(Path.Combine(root,"accounts"));ledger.Provision(new(id,null,null,true,0,new(0,0,0,0,new Dictionary<string,JsonElement>()),1));
    var organisationAuthority=new OrganisationService(Path.Combine(root,"organisations.json"),new ProfileService(Path.Combine(root,"profiles.json"),null));
    var fixtureOrg=organisationAuthority.CreateTrustedOrganisation(id,"Explicit fictional native Admin fixture",BusinessAddOnKind.Business,"fixture-only-settled-billing");
    var start=new ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!,"dotnet")){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
    foreach(var arg in new[]{typeof(FilesWebDomain).Assembly.Location,"--urls","http://127.0.0.1:5095","--StateRoot",root,
        "--Files:AccountLocations:0:AccountID",id.ToString(),"--Files:AccountLocations:0:LocationID",location.ToString(),"--Files:AccountLocations:0:StatePath",filesState})start.ArgumentList.Add(arg);
    if(browserFixture)
    {
        // Explicit fictional acceptance configuration; never production defaults.
        var fixtureCosts=new ApprovedCostModel("browser-fixture-v1","GBP",10_000,.001m,.000000002m,.0001m,.0000000001m,3m,.20m,.05m,.02m,.01m,.20m,true,2_000_000,20_000_000_000_000,true,FeeRounding:ChargeFeeRounding.CeilingMinorUnit,FeeModelKind:"FlatFixedAndPercentage",DustDefinitionReference:"fictional-fixture:1x-dust-v1",FullUseCostDefinitionReference:"fictional-fixture:full-use-costs-v1",OperationalServiceLimitsReference:"fictional-fixture:operational-limits-v1");
        foreach(var property in JsonSerializer.SerializeToElement(fixtureCosts).EnumerateObject())
            if(property.Value.ValueKind!=JsonValueKind.Null)start.Environment["SubscriptionCosts__"+property.Name]=property.Value.ToString();
    }
    host=Process.Start(start)!;host.BeginOutputReadLine();host.BeginErrorReadLine();
    using var http=new HttpClient{BaseAddress=new("http://127.0.0.1:5095")};
    for(var retry=0;retry<100;retry++){try{if((await http.GetAsync("/health")).IsSuccessStatusCode)break;}catch(HttpRequestException){}await Task.Delay(100);}
    Check(!host.HasExited,"fixture server owns its listening process");
    Check((await http.GetAsync("/subscription-builder")).IsSuccessStatusCode,"deployed subscription builder HTML available");
    Check((await http.GetAsync("/api/account/current")).StatusCode==HttpStatusCode.Unauthorized,"unauthenticated denied");
    http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",issued.AccessToken);
    if(browserFixture)
    {
        var privatePath=Path.Combine(root,"browser-fixture-private.json");
        await File.WriteAllTextAsync(privatePath,JsonSerializer.Serialize(new{url="http://127.0.0.1:5095/subscription-builder",accessToken=issued.AccessToken,accountID=id}));
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(privatePath,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        Console.WriteLine("READY fictional real-Web browser fixture; private bootstrap file: "+privatePath);
        await Task.Delay(TimeSpan.FromMinutes(14));return;
    }
    var profile=await http.GetFromJsonAsync<CakeProfile>("/api/account/current");Check(profile!.AccountID==id,"verified CAKE principal");
    var nativeAdmin=new AdminCuiController(new AdminAccountClient(http,_=>ValueTask.FromResult<string?>(issued.AccessToken)));
    await nativeAdmin.DispatchAsync("Refresh",null);Check(nativeAdmin.Organisations.Single().OrgID==fixtureOrg.OrgID.ToString("D"),"actual native Admin client uses authenticated canonical org endpoint");
    nativeAdmin.TrySetValue("OrganisationID",fixtureOrg.OrgID.ToString("D"));await nativeAdmin.DispatchAsync("OpenOrganisation",null);Check(nativeAdmin.Members.Single().AccountID==id.ToString("D"),"native current canonical memberships");
    await nativeAdmin.DispatchAsync("RefreshAudit",null);Check(nativeAdmin.Audit.All(e=>e.OrgID==fixtureOrg.OrgID)&&nativeAdmin.Audit.Count>0,"native bounded canonical audit");
    var catalogue=await http.GetStringAsync("/api/catalogue");Check(catalogue.Contains("Sites")&&catalogue.Contains("Files"),"real registered domain adapters");
    var created=await http.PostAsJsonAsync("/api/apps/Sites/CreateProject",new {FilesDirectoryId=folder.Value,StackProjectId=(Guid?)null,StackDomainId=(Guid?)null,SourceRevision="fixture-a",Name="Fixture site",FrameworkId="9to1-native",RelativeProjectPath="Fixture"});
    created.EnsureSuccessStatusCode();using var result=JsonDocument.Parse(await created.Content.ReadAsStringAsync());
    Check(result.RootElement.GetProperty("Error").ValueKind==JsonValueKind.Null,"canonical Sites creation");
    var siteID=result.RootElement.GetProperty("Value").GetProperty("SiteId").GetGuid();
    var page=await http.PostAsJsonAsync("/api/apps/Sites/CreatePage",new {siteID,expectedRevision=1,name="Home",path="/"});page.EnsureSuccessStatusCode();
    var imported=await http.PostAsJsonAsync("/api/apps/Sites/ImportContent",new {siteID,expectedRevision=2,name="Imported",path="/imported",html="<h1>Preserved title</h1><p>Original <strong>content</strong>.</p>"});imported.EnsureSuccessStatusCode();
    using(var importedResult=JsonDocument.Parse(await imported.Content.ReadAsStringAsync()))Check(importedResult.RootElement.GetProperty("Project").GetProperty("Revision").GetInt64()==3,"actual authenticated canonical HTML import");
    var rejectedImport=await http.PostAsJsonAsync("/api/apps/Sites/ImportContent",new {siteID,expectedRevision=3,name="Unsafe",path="/unsafe",html="<script>throw 1</script><p>Keep original</p>"});rejectedImport.EnsureSuccessStatusCode();
    using(var rejectedResult=JsonDocument.Parse(await rejectedImport.Content.ReadAsStringAsync()))Check(rejectedResult.RootElement.GetProperty("Project").ValueKind==JsonValueKind.Null,"unsafe content cannot mutate canonical project");
    var pricing=await http.PostAsJsonAsync("/api/subscription/preview",new BuilderSelection(1,null,20_000_000_000));
    Check((await pricing.Content.ReadAsStringAsync()).Contains("DustPer1x"),"missing production costs precise blocker");
    await http.PostAsync("/api/account/signout",null);
    Check((await http.GetAsync("/api/catalogue")).StatusCode==HttpStatusCode.Unauthorized,"revoked token domain dispatch denied");
    await nativeAdmin.DispatchAsync("Refresh",null);Check(nativeAdmin.Organisations.Count==0&&nativeAdmin.Members.Count==0&&nativeAdmin.Audit.Count==0,"native projection cleared after actual server revocation");
    Console.WriteLine("PASS authenticated HTTP CAKE identity, actual Files/Sites adapters, canonical creation/edit, missing price inputs, revocation");
}
finally {if(host is {HasExited:false}){host.Kill(true);await host.WaitForExitAsync();}host?.Dispose();Directory.Delete(root,true);}
