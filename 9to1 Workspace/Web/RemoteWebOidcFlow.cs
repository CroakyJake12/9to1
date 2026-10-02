using System.Net;
using System.Text.Json;
using NineToOne.Accounts.Oidc;
namespace NineToOne.Web;

public sealed record WebOidcConfiguration(string Issuer,string ClientId,string ResourceAudience,
 string ExactRedirectUri,Uri ExactAuthorizationEndpoint,Uri ExactTokenEndpoint,
 IReadOnlySet<string> AllowedAlgorithms,IReadOnlySet<string> RequestedScopes);
public enum WebSignInFailure { None,Unavailable,InvalidCallback,InvalidIssuerToken,UnknownExchangeCompletion }
public sealed record WebSignInStart(Uri? AuthorizationUri,WebSignInFailure Failure);
public sealed record WebSignInCompletion(Guid? ObservedAccountId,WebSignInFailure Failure);
// The trusted Web host issues this association; request JSON cannot construct it.
// Browser generation is NOT a CAKE authentication revision or a local Home profile.
public sealed class RemoteWebBrowserAssociation
{
 internal RemoteWebBrowserAssociation(Func<long> generation,CancellationToken lifetime)
 {CurrentGeneration=generation;Lifetime=lifetime;}
 internal Func<long> CurrentGeneration {get;}
 internal CancellationToken Lifetime {get;}
}
// Standard issuer-client exchange only. It does not issue credentials/codes/sessions.
public sealed class RemoteWebCodeExchange : IDisposable
{
 private readonly HttpClient client;
 public RemoteWebCodeExchange():this(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){}
 internal RemoteWebCodeExchange(HttpMessageHandler handler)=>client=new(handler){Timeout=TimeSpan.FromSeconds(20)};
 internal async Task<(string? IdToken,string? AccessToken,WebSignInFailure Failure)> ExchangeAsync(
  Uri exactEndpoint,IReadOnlyDictionary<string,string> form,Func<bool> admit,CancellationToken ct)
 {
  if(!admit())return(default,default,WebSignInFailure.Unavailable);
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(20));
  using var request=new HttpRequestMessage(HttpMethod.Post,exactEndpoint){Content=new FormUrlEncodedContent(form)};
  try
  {
   // No redirect following or replay after an ambiguous/failed code exchange.
   using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
   if(response.StatusCode!=HttpStatusCode.OK||response.Content.Headers.ContentLength>65536)return(default,default,WebSignInFailure.UnknownExchangeCompletion);
   await using var input=await response.Content.ReadAsStreamAsync(deadline.Token);using var body=new MemoryStream();var buffer=new byte[4096];int read;
   while((read=await input.ReadAsync(buffer,deadline.Token))!=0){if(body.Length+read>65536)return(default,default,WebSignInFailure.UnknownExchangeCompletion);body.Write(buffer,0,read);}
   using var doc=JsonDocument.Parse(body.ToArray(),new JsonDocumentOptions{MaxDepth=8});var root=doc.RootElement;
   if(!admit()||!root.TryGetProperty("token_type",out var type)||!string.Equals(type.GetString(),"Bearer",StringComparison.OrdinalIgnoreCase)
     ||!root.TryGetProperty("id_token",out var id)||id.ValueKind!=JsonValueKind.String
     ||!root.TryGetProperty("access_token",out var access)||access.ValueKind!=JsonValueKind.String)return(default,default,WebSignInFailure.UnknownExchangeCompletion);
   var idRaw=id.GetString();var accessRaw=access.GetString();
   if(string.IsNullOrWhiteSpace(idRaw)||idRaw.Length>16384||string.IsNullOrWhiteSpace(accessRaw)||accessRaw.Length>16384)return(default,default,WebSignInFailure.UnknownExchangeCompletion);
   return(idRaw,accessRaw,WebSignInFailure.None);
  }
  catch(Exception error)when(error is IOException or HttpRequestException or JsonException or InvalidOperationException or OperationCanceledException)
  {return(default,default,WebSignInFailure.UnknownExchangeCompletion);}
 }
 public void Dispose()=>client.Dispose();
}
// One private originating flow, retained by the host behind its opaque browser association.
// The actual endpoint pair must come from approved, freshly verified issuer discovery.
public sealed class RemoteWebOidcFlow
{
 private static readonly IReadOnlySet<string> Scopes=new HashSet<string>{"cake:account:read","cake:profile:read","cake:profile:write","cake:sessions:read","cake:sessions:revoke"};
 private readonly RemoteWebBrowserAssociation browser;private readonly WebOidcConfiguration config;
 private readonly IdentityModelIssuerReader reader;private readonly RemoteWebCodeExchange exchange;
 private readonly OidcOriginatingFlow originating;private readonly long generation;private readonly DateTimeOffset deadline;
 private readonly string nonce;private int completing;private int canceled;private string? privateAccessToken;private DateTimeOffset tokenExpiry;
 internal RemoteWebOidcFlow(RemoteWebBrowserAssociation browser,WebOidcConfiguration config,
  IdentityModelIssuerReader reader,RemoteWebCodeExchange exchange,TimeProvider clock)
 {
  Validate(config);this.browser=browser;this.config=config with {AllowedAlgorithms=new HashSet<string>(config.AllowedAlgorithms,StringComparer.Ordinal),RequestedScopes=new HashSet<string>(config.RequestedScopes,StringComparer.Ordinal)};this.reader=reader;this.exchange=exchange;
  generation=browser.CurrentGeneration();deadline=clock.GetUtcNow().AddMinutes(5);
  originating=new(config.Issuer,config.ClientId,config.ExactRedirectUri,generation.ToString(System.Globalization.CultureInfo.InvariantCulture),deadline);
  nonce=originating.AuthorizationParameters()["nonce"];
 }
 private static void Validate(WebOidcConfiguration c)
 {
  static bool Https(Uri u)=>u.IsAbsoluteUri&&u.Scheme=="https"&&u.UserInfo.Length==0&&u.Fragment.Length==0;
  if(!Uri.TryCreate(c.Issuer,UriKind.Absolute,out var issuer)||!Https(issuer)||!Uri.TryCreate(c.ExactRedirectUri,UriKind.Absolute,out var redirect)||!Https(redirect)
   ||!Https(c.ExactAuthorizationEndpoint)||!Https(c.ExactTokenEndpoint)||c.ExactAuthorizationEndpoint.Query.Length!=0||c.ExactTokenEndpoint.Query.Length!=0
   ||string.IsNullOrWhiteSpace(c.ClientId)||string.IsNullOrWhiteSpace(c.ResourceAudience)||c.AllowedAlgorithms.Count==0||!c.RequestedScopes.IsSubsetOf(Scopes))throw new ArgumentException("Missing approved issuer/client/discovery configuration");
 }
 private bool Admit(DateTimeOffset now)=>Volatile.Read(ref canceled)==0&&!browser.Lifetime.IsCancellationRequested&&browser.CurrentGeneration()==generation&&generation>=0&&now<deadline;
 public WebSignInStart Begin(DateTimeOffset now)
 {
  if(!Admit(now)||Volatile.Read(ref completing)!=0)return new(null,WebSignInFailure.Unavailable);
  var parameters=new Dictionary<string,string>(originating.AuthorizationParameters());
  parameters["scope"]="openid profile "+string.Join(' ',config.RequestedScopes.Order(StringComparer.Ordinal));
  var query=string.Join('&',parameters.Select(x=>Uri.EscapeDataString(x.Key)+"="+Uri.EscapeDataString(x.Value)));
  return new(new Uri(config.ExactAuthorizationEndpoint.AbsoluteUri+"?"+query),WebSignInFailure.None);
 }
 public async Task<WebSignInCompletion> CompleteAsync(string returnedState,string exactRedirect,string code,TimeProvider clock,CancellationToken ct)
 {
  if(Interlocked.CompareExchange(ref completing,1,0)!=0)return new(null,WebSignInFailure.InvalidCallback);
  using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,browser.Lifetime);
  if(!Admit(clock.GetUtcNow())||linked.IsCancellationRequested)return new(null,WebSignInFailure.Unavailable);
  var form=originating.ConsumeCallback(returnedState,exactRedirect,code,generation.ToString(System.Globalization.CultureInfo.InvariantCulture),clock.GetUtcNow());
  if(form is null)return new(null,WebSignInFailure.InvalidCallback);
  var result=await exchange.ExchangeAsync(config.ExactTokenEndpoint,form,()=>Admit(clock.GetUtcNow())&&!linked.IsCancellationRequested,linked.Token);
  if(result.Failure!=WebSignInFailure.None)return new(null,result.Failure);
  var idPolicy=new TokenPolicy(config.Issuer,config.ClientId,TokenPurpose.IdToken,nonce,new HashSet<string>(),config.AllowedAlgorithms);
  var id=await reader.VerifyAsync(result.IdToken!,idPolicy,linked.Token);
  if(!Admit(clock.GetUtcNow())||linked.IsCancellationRequested||id is null||!originating.AcceptIdToken(id,generation.ToString(System.Globalization.CultureInfo.InvariantCulture),clock.GetUtcNow()))return new(null,WebSignInFailure.InvalidIssuerToken);
  var apiPolicy=new TokenPolicy(config.Issuer,config.ResourceAudience,TokenPurpose.ApiAccessToken,null,config.RequestedScopes,config.AllowedAlgorithms);
  var api=await reader.VerifyAsync(result.AccessToken!,apiPolicy,linked.Token);
  var observed=await new OidcResourceConsumer(reader).ObserveAsync(result.AccessToken!,apiPolicy,clock.GetUtcNow(),linked.Token);
  if(!Admit(clock.GetUtcNow())||linked.IsCancellationRequested||api is null||observed is null||api.Subject!=id.Subject||!Guid.TryParseExact(api.SessionId,"D",out var sessionId)||sessionId==Guid.Empty||sessionId.ToString("D")!=api.SessionId)return new(null,WebSignInFailure.InvalidIssuerToken);
  // Private bearer storage is a client cache, never a credential/session authority or a Home grant.
  privateAccessToken=result.AccessToken;tokenExpiry=api.ExpiresAt;return new(observed.AccountId,WebSignInFailure.None);
 }
 internal ValueTask<string?> ReadPrivateTokenAsync(DateTimeOffset now,CancellationToken ct)
  =>ValueTask.FromResult(!ct.IsCancellationRequested&&Admit(now)&&now<tokenExpiry?privateAccessToken:null);
 public void Cancel(){Interlocked.Exchange(ref canceled,1);originating.Cancel();privateAccessToken=null;Interlocked.Exchange(ref completing,1);}
}
