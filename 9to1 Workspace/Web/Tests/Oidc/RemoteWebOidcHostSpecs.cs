using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.WebUtilities;
using NineToOne.Accounts.Remote;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http.HttpResults;
using NineToOne.Accounts.Oidc;
using NineToOne.Web;

// Actual ASP.NET HttpContext/route handler entry, controlled discovery/issuer/transport only.
// No real Worker credential/account creation or cross-store Home principal proof.
public static class RemoteWebOidcHostSpecs
{
 public static async Task RunAsync()
 {
  ModeSelectionNeverFallsBackOnMalformedIntent();
  await SignedCallbackPrivateAccountAndReplayAsync();
  await HeldIssuerReplacementRefusesAccountHttpAsync();
  ActualProductionModulesAreEmbedded();
  await HeldBodyReplacementRefusesOriginalAsync();
  await RevokedCookieCannotStartNewFlowAsync();
  await CsrfAndOriginRefuseBeforeDiscoveryAsync();
 }
 private static void ModeSelectionNeverFallsBackOnMalformedIntent()
 {
  IConfiguration Value(string? value)=>new ConfigurationBuilder().AddInMemoryCollection(value is null?new Dictionary<string,string?>():new Dictionary<string,string?>{{"RemoteCakeClient:Enabled",value}}).Build();
  Assert(RemoteWebOidcConfiguration.ReadMode(Value(null))==RemoteCakeClientMode.Legacy,"absent mode changed existing legacy behavior");
  Assert(RemoteWebOidcConfiguration.ReadMode(Value("false"))==RemoteCakeClientMode.Legacy,"explicit false changed existing legacy behavior");
  Assert(RemoteWebOidcConfiguration.ReadMode(Value("true"))==RemoteCakeClientMode.Remote,"explicit remote mode not selected");
  foreach(var malformed in new[]{"tru","", "1", "enabled"})
  {var config=Value(malformed);Assert(RemoteWebOidcConfiguration.ReadMode(config)==RemoteCakeClientMode.Unavailable,"malformed explicit remote mode selected local issuer");Assert(RemoteWebOidcConfiguration.Read(config) is null,"malformed mode returned configured remote graph");}
 }
 private static WebOidcConfiguration Config()=>new("https://issuer.invalid/api/auth","client","cake-api","https://web.invalid/remote/oidc/callback",new("https://issuer.invalid/api/auth/oauth2/authorize"),new("https://issuer.invalid/api/auth/oauth2/token"),new HashSet<string>{"RS256"},new HashSet<string>{"cake:account:read"});
 private static DefaultHttpContext Context(string? cookie=null)
 {var c=new DefaultHttpContext();c.Request.Scheme="https";c.Request.Host=new HostString("web.invalid");if(cookie is not null)c.Request.Headers.Cookie=cookie;return c;}
 private static (string Cookie,string Csrf) Issued(DefaultHttpContext c,IResult result)
 {var html=((ContentHttpResult)result).Content!;var csrf=Regex.Match(html,"\"csrf\":\"([a-f0-9]{64})\"").Groups[1].Value;Assert(csrf.Length==64,"actual server issued CSRF missing");return(c.Response.Headers.SetCookie.ToString().Split(';')[0],csrf);}
 private static DefaultHttpContext Begin(string cookie,string csrf,Stream? body=null)
 {var c=Context(cookie);c.Request.Headers.Origin="https://web.invalid";c.Request.Headers["X-CSRF-Token"]=csrf;c.Request.ContentType="application/json";c.Request.Body=body??new MemoryStream(Encoding.UTF8.GetBytes("{\"requestNonce\":\"fictional-request-nonce-01\"}"));return c;}
 private static IdentityModelIssuerReader Reader()=>new(new DeniedKeys(),new("sub","sid","auth_revision","scope","nonce","azp"),TimeProvider.System);
 private static async Task HeldBodyReplacementRefusesOriginalAsync()
 {
  var discovery=0;var handler=new CountExchange();using var exchange=new RemoteWebCodeExchange(handler);using var host=new RemoteWebOidcHost(new("https://web.invalid/"),Reader(),exchange,_=>{discovery++;return Task.FromResult<WebOidcConfiguration?>(Config());},TimeProvider.System);
  var first=Context();var issued=Issued(first,await host.SignInAsync(first));using var body=new HeldBody();var original=host.BeginAsync(Begin(issued.Cookie,issued.Csrf,body));await body.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
  var replacement=Context(issued.Cookie);Issued(replacement,await host.SignInAsync(replacement));body.Release.TrySetResult();var result=await original.WaitAsync(TimeSpan.FromSeconds(5));
  Assert(((IStatusCodeHttpResult)result).StatusCode==503,"original held body adopted replacement");Assert(discovery==2,"original admitted downstream discovery");Assert(handler.Calls==0,"original issued token exchange");
 }
 private static async Task RevokedCookieCannotStartNewFlowAsync()
 {
  var discovery=0;using var exchange=new RemoteWebCodeExchange(new CountExchange());using var host=new RemoteWebOidcHost(new("https://web.invalid/"),Reader(),exchange,_=>{discovery++;return Task.FromResult<WebOidcConfiguration?>(Config());},TimeProvider.System);
  var first=Context();var issued=Issued(first,await host.SignInAsync(first));var replacement=Context(issued.Cookie);Issued(replacement,await host.SignInAsync(replacement));var denied=await host.BeginAsync(Begin(issued.Cookie,issued.Csrf));Assert(((IStatusCodeHttpResult)denied).StatusCode==403,"revoked original cookie created a flow");Assert(discovery==2,"revoked cookie read downstream discovery");
 }
 private static async Task CsrfAndOriginRefuseBeforeDiscoveryAsync()
 {
  var discovery=0;using var exchange=new RemoteWebCodeExchange(new CountExchange());using var host=new RemoteWebOidcHost(new("https://web.invalid/"),Reader(),exchange,_=>{discovery++;return Task.FromResult<WebOidcConfiguration?>(Config());},TimeProvider.System);
  var first=Context();var issued=Issued(first,await host.SignInAsync(first));var wrong=Begin(issued.Cookie,new string('0',64));Assert(((IStatusCodeHttpResult)await host.BeginAsync(wrong)).StatusCode==403,"wrong CSRF admitted");wrong=Begin(issued.Cookie,issued.Csrf);wrong.Request.Headers.Origin="https://foreign.invalid";Assert(((IStatusCodeHttpResult)await host.BeginAsync(wrong)).StatusCode==403,"foreign origin admitted");Assert(discovery==1,"CSRF/origin denial performed discovery");
 }
 private static async Task SignedCallbackPrivateAccountAndReplayAsync()
 {
  using var fixture=new SignedHostFixture();
  var started=await fixture.StartAsync();
  var callback=fixture.Callback(started.Cookie,started.State);
  var result=await fixture.Host.CallbackAsync(callback);
  Assert(result is RedirectHttpResult redirect&&redirect.Url=="/remote/account","actual signed ID/API callback not accepted");
  Assert(fixture.TokenCalls==1,"exchange not exactly once");
  Assert(callback.Response.Headers.CacheControl=="no-store","callback response cacheable");
  var accountContext=Context(started.Cookie);var account=await fixture.Host.CurrentAccountAsync(accountContext);
  var json=JsonSerializer.Serialize(((IValueHttpResult)account).Value);
  using(var body=JsonDocument.Parse(json))Assert(body.RootElement.GetProperty("AccountID").GetGuid()==fixture.AccountId,"actual host account mapped wrong signed subject");
  Assert(fixture.AccountCalls==1&&accountContext.Response.Headers.CacheControl=="no-store","private account read missing/bad cache policy");
  Assert(!json.Contains(fixture.AccessToken,StringComparison.Ordinal),"private bearer exposed in response");
  var replay=await fixture.Host.CallbackAsync(fixture.Callback(started.Cookie,started.State));
  Assert(((IStatusCodeHttpResult)replay).StatusCode==503&&fixture.TokenCalls==1,"actual callback replay exchanged consumed code");
 }
 private static async Task HeldIssuerReplacementRefusesAccountHttpAsync()
 {
  using var fixture=new SignedHostFixture();var started=await fixture.StartAsync();
  Assert(await fixture.Host.CallbackAsync(fixture.Callback(started.Cookie,started.State)) is RedirectHttpResult,"setup signed callback unavailable");
  fixture.Keys.HoldNext=true;var original=fixture.Host.CurrentAccountAsync(Context(started.Cookie));
  await fixture.Keys.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
  var replacement=Context(started.Cookie);Issued(replacement,await fixture.Host.SignInAsync(replacement));
  fixture.Keys.Release.TrySetResult();var refused=await original.WaitAsync(TimeSpan.FromSeconds(5));
  Assert(((IStatusCodeHttpResult)refused).StatusCode==503&&fixture.AccountCalls==0,"held issuer original host generation reached account HTTP");
 }
 private static void ActualProductionModulesAreEmbedded()
 {
  foreach(var name in new[]{"signin-page.mjs","web-oidc-signin.mjs","login-submit.mjs"})
  {
   using var input=typeof(RemoteWebOidcEndpointMount).Assembly.GetManifestResourceStream("NineToOne.Web.Remote."+name);
   Assert(input is not null&&input.Length is >0 and <=65536,"actual production Web module absent/bounds");
  }
 }
 private sealed class SignedHostFixture:IDisposable
 {
  private readonly RSA rsa=RSA.Create(2048);private readonly RsaSecurityKey signing;
  public readonly Guid AccountId=Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
  public readonly Guid SessionId=Guid.Parse("550e8400-e29b-41d4-a716-446655440001");
  public readonly HeldPublicKeys Keys;public readonly RemoteWebOidcHost Host;
  private readonly RemoteWebCodeExchange exchange;private readonly WorkerAccountApiClient accounts;
  public int TokenCalls,AccountCalls;public string AccessToken="";private string expectedNonce="",expectedChallenge="";
  public SignedHostFixture()
  {
   signing=new RsaSecurityKey(rsa){KeyId="synthetic-web-host-key"};var pub=rsa.ExportParameters(false);
   var jwks=JsonSerializer.Serialize(new{keys=new[]{new{kty="RSA",kid=signing.KeyId,n=Base64UrlEncoder.Encode(pub.Modulus!),e=Base64UrlEncoder.Encode(pub.Exponent!)}}});
   Keys=new(new(Config().Issuer,jwks,DateTimeOffset.UtcNow.AddMinutes(5)));
   var reader=new IdentityModelIssuerReader(Keys,new("sub","sid","auth_revision","scope","nonce","azp"),TimeProvider.System);
   exchange=new RemoteWebCodeExchange(new ControlledHandler(async(request,ct)=>
   {
    TokenCalls++;Assert(request.Method==HttpMethod.Post&&request.RequestUri==Config().ExactTokenEndpoint,"unexpected issuer token request");
    var form=QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
    Assert(form["grant_type"]=="authorization_code"&&form["client_id"]=="client"&&form["code"]=="synthetic-code"&&form["redirect_uri"]==Config().ExactRedirectUri,"actual token form binding wrong");
    var verifier=form["code_verifier"].ToString();Assert(Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))==expectedChallenge,"actual original S256 verifier not retained");
    var id=Token("client",expectedNonce);AccessToken=Token("cake-api",null);
    return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{id_token=id,access_token=AccessToken,token_type="Bearer"}),Encoding.UTF8,"application/json")};
   }));
   var policy=new TokenPolicy(Config().Issuer,"cake-api",TokenPurpose.ApiAccessToken,null,new HashSet<string>{"cake:account:read"},new HashSet<string>{"RS256"});
   accounts=new WorkerAccountApiClient(new("https://issuer.invalid/"),new OidcResourceConsumer(reader),policy,TimeProvider.System,new ControlledHandler((request,ct)=>
   {
    AccountCalls++;Assert(request.Method==HttpMethod.Get&&request.RequestUri!.AbsolutePath=="/api/account/current"&&request.Headers.Authorization?.Scheme=="Bearer"&&request.Headers.Authorization.Parameter==AccessToken,"actual private bearer account HTTP wrong");
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new RemoteCurrent(AccountId,"Synthetic account")),Encoding.UTF8,"application/json")});
   }));
   Host=new(new("https://web.invalid/"),reader,exchange,_=>Task.FromResult<WebOidcConfiguration?>(Config()),TimeProvider.System,accounts);
  }
  private string Token(string audience,string? nonce)
  {
   var now=DateTimeOffset.UtcNow;var claims=new Dictionary<string,object>{{"sub",AccountId.ToString("D")},{"sid",SessionId.ToString("D")},{"auth_revision","synthetic-server-revision-01"},{"scope","cake:account:read"}};
   if(nonce is not null)claims["nonce"]=nonce;
   return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor{Issuer=Config().Issuer,Audience=audience,IssuedAt=now.UtcDateTime,NotBefore=now.AddSeconds(-1).UtcDateTime,Expires=now.AddMinutes(2).UtcDateTime,Claims=claims,SigningCredentials=new SigningCredentials(signing,"RS256")});
  }
  public async Task<(string Cookie,string State)> StartAsync()
  {
   var signIn=Context();var issued=Issued(signIn,await Host.SignInAsync(signIn));
   var begun=await Host.BeginAsync(Begin(issued.Cookie,issued.Csrf));
   using var result=JsonDocument.Parse(JsonSerializer.Serialize(((IValueHttpResult)begun).Value));var uri=new Uri(result.RootElement.GetProperty("authorizationUri").GetString()!);
   var parameters=QueryHelpers.ParseQuery(uri.Query);expectedNonce=parameters["nonce"].ToString();expectedChallenge=parameters["code_challenge"].ToString();Assert(parameters["code_challenge_method"]=="S256"&&expectedNonce.Length>0,"actual originating flow lacks PKCE/nonce");
   return(issued.Cookie,parameters["state"].ToString());
  }
  public DefaultHttpContext Callback(string cookie,string state){var c=Context(cookie);c.Request.QueryString=QueryString.Create(new Dictionary<string,string?>{{"state",state},{"code","synthetic-code"}});return c;}
  public void Dispose(){Keys.Release.TrySetResult();Host.Dispose();accounts.Dispose();exchange.Dispose();rsa.Dispose();}
 }
 private sealed class HeldPublicKeys(ApprovedIssuerKeys value):IApprovedIssuerKeysSource
 {
  public bool HoldNext;public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public async ValueTask<ApprovedIssuerKeys?> ReadAsync(string issuer,CancellationToken ct)
  {if(HoldNext){HoldNext=false;Entered.TrySetResult();await Release.Task.WaitAsync(ct);}return issuer==value.Issuer?value:null;}
 }
 private sealed class ControlledHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
 {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request,ct);}
 private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 private sealed class DeniedKeys:IApprovedIssuerKeysSource{public ValueTask<ApprovedIssuerKeys?> ReadAsync(string issuer,CancellationToken ct)=>ValueTask.FromResult<ApprovedIssuerKeys?>(null);}
 private sealed class CountExchange:HttpMessageHandler{public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));}}
 private sealed class HeldBody:Stream
 {
  public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);private bool read;
  public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){if(read)return 0;Entered.TrySetResult();await Release.Task.WaitAsync(ct);var data=Encoding.UTF8.GetBytes("{\"requestNonce\":\"fictional-request-nonce-01\"}");data.AsMemory().CopyTo(buffer);read=true;return data.Length;}
  public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}public override void Flush()=>throw new NotSupportedException();public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin so)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
 }
}
