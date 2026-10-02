using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NineToOne.Accounts.Oidc;
using NineToOne.Accounts.Remote;
namespace NineToOne.Web;

// One Web CLIENT host. This never issues CAKE credentials, authorization codes or sessions.
// The sole remote Worker remains authority. No browser DTO/cookie becomes a Home principal.
public sealed class RemoteWebOidcHost : IDisposable
{
 private const string Cookie="__Host-cake-client";
 private readonly object gate=new();private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
 private readonly CancellationTokenSource lifetime=new();private readonly IdentityModelIssuerReader reader;
 private readonly WorkerAccountApiClient? accountClient;
 private readonly RemoteWebCodeExchange exchange;private readonly TimeProvider clock;private readonly Uri origin;
 private readonly Func<CancellationToken,Task<WebOidcConfiguration?>> readFreshApprovedConfiguration;
 private sealed class Entry
 {
  public long Generation;public bool Revoked; public required string Csrf;public required DateTimeOffset Expires;
  public RemoteWebOidcFlow? Flow;public string? RequestNonce;public Uri? AuthorizationUri;
 }
 // Actual host composition must bind this callback to approved, bounded issuer discovery.
 // Missing configuration callback result is unavailable, never a local identity fallback.
 public RemoteWebOidcHost(Uri exactWebOrigin,IdentityModelIssuerReader reader,RemoteWebCodeExchange exchange,
  Func<CancellationToken,Task<WebOidcConfiguration?>> readFreshApprovedConfiguration,TimeProvider clock,WorkerAccountApiClient? accountClient=null)
 {
  if(!exactWebOrigin.IsAbsoluteUri||exactWebOrigin.Scheme!="https"||exactWebOrigin.UserInfo.Length!=0||exactWebOrigin.AbsolutePath!="/"||exactWebOrigin.Query.Length!=0||exactWebOrigin.Fragment.Length!=0)throw new ArgumentException("Exact HTTPS Web origin required");
  origin=exactWebOrigin;this.reader=reader;this.exchange=exchange;this.readFreshApprovedConfiguration=readFreshApprovedConfiguration;this.clock=clock;this.accountClient=accountClient;
 }
 private static string Random()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
 private static bool Equal(string a,string b)=>a.Length==b.Length&&CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a),Encoding.UTF8.GetBytes(b));
 private bool Current(Entry e)=>!e.Revoked&&!lifetime.IsCancellationRequested&&clock.GetUtcNow()<e.Expires;
 private Entry? Read(HttpContext context)
 {
  var value=context.Request.Cookies[Cookie];if(value is null||value.Length!=64)return null;
  lock(gate)return entries.TryGetValue(value,out var e)&&Current(e)?e:null;
 }
 private bool SameOrigin(HttpContext c)=>c.Request.IsHttps&&c.Request.Host.Value==origin.Authority;
 public async Task<IResult> SignInAsync(HttpContext context)
 {
  context.Response.Headers.CacheControl="no-store";
  if(!SameOrigin(context))return Results.StatusCode(400);
  var config=await readFreshApprovedConfiguration(context.RequestAborted);
  if(config is null||lifetime.IsCancellationRequested)return Results.Json(new{error="CapabilityUnavailable"},statusCode:503);
  Entry e;var key=Random();lock(gate)
  {
   foreach(var expired in entries.Where(x=>!Current(x.Value)).Select(x=>x.Key).ToArray()){entries[expired].Revoked=true;entries[expired].Generation++;entries[expired].Flow?.Cancel();entries.Remove(expired);}
   if(entries.Count>=128)return Results.Json(new{error="ClientCapacityUnavailable"},statusCode:503);
   var old=Read(context);if(old is not null){old.Revoked=true;old.Generation++;old.Flow?.Cancel();}
   e=new(){Csrf=Random(),Expires=clock.GetUtcNow().AddMinutes(10)};entries.Add(key,e);
  }
  context.Response.Cookies.Append(Cookie,key,new CookieOptions{Secure=true,HttpOnly=true,SameSite=SameSiteMode.Lax,Path="/",MaxAge=TimeSpan.FromMinutes(10),IsEssential=true});
  context.Response.Headers.CacheControl="no-store";context.Response.Headers.ContentSecurityPolicy="default-src 'none'; script-src 'self'; style-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
  // JSON is encoded by the normal serializer; no tokens/verifier/password enter the DOM.
  var hostConfig=JsonSerializer.Serialize(new{approved=new{authorizationEndpoint=config.ExactAuthorizationEndpoint.AbsoluteUri,clientId=config.ClientId},csrf=e.Csrf});
  return Results.Content("<!doctype html><html><head><meta charset=\"utf-8\"><title>Sign in to CAKE ID</title></head><body><main><h1>Sign in to CAKE ID</h1><form id=\"cake-signin\"><button id=\"cake-signin-button\" type=\"submit\">Continue to sign in</button></form><p id=\"cake-signin-status\" role=\"status\"></p></main><script id=\"cake-oidc-host-config\" type=\"application/json\">"+hostConfig+"</script><script src=\"/remote/oidc/signin-page.mjs\" type=\"module\"></script></body></html>","text/html; charset=utf-8");
 }
 public async Task<IResult> BeginAsync(HttpContext context)
 {
  context.Response.Headers.CacheControl="no-store";
  if(!SameOrigin(context)||context.Request.Headers.Origin.ToString()!=origin.GetLeftPart(UriPartial.Authority))return Results.StatusCode(403);
  var e=Read(context);var csrf=context.Request.Headers["X-CSRF-Token"].ToString();if(e is null||csrf.Length!=64||!Equal(e.Csrf,csrf))return Results.StatusCode(403);
  long captured;lock(gate){if(!Current(e))return Results.StatusCode(403);captured=e.Generation;}
  if(context.Request.ContentLength>1024||context.Request.ContentType?.Split(';')[0].Trim()!="application/json")return Results.StatusCode(400);
  using var bodyDeadline=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted,lifetime.Token);
  bodyDeadline.CancelAfter(TimeSpan.FromSeconds(10));
  using var body=new MemoryStream();var buffer=new byte[256];int count;
  try
  {
   while((count=await context.Request.Body.ReadAsync(buffer,bodyDeadline.Token))!=0){if(body.Length+count>1024)return Results.StatusCode(400);body.Write(buffer,0,count);}
   if(bodyDeadline.IsCancellationRequested)return Results.StatusCode(503);
  }
  catch(Exception error)when(error is OperationCanceledException or IOException){return Results.StatusCode(503);}
  string nonce;try{using var doc=JsonDocument.Parse(body.ToArray(),new JsonDocumentOptions{MaxDepth=3});var r=doc.RootElement;if(r.ValueKind!=JsonValueKind.Object||r.EnumerateObject().Count()!=1||!r.TryGetProperty("requestNonce",out var n)||n.ValueKind!=JsonValueKind.String)return Results.StatusCode(400);nonce=n.GetString()!;if(nonce.Length is <16 or >128)return Results.StatusCode(400);}catch(JsonException){return Results.StatusCode(400);}
  lock(gate){if(!Current(e)||e.Generation!=captured||context.RequestAborted.IsCancellationRequested)return Results.StatusCode(503);if(e.Flow is not null)return e.RequestNonce==nonce&&e.AuthorizationUri is not null&&e.Flow.Begin(clock.GetUtcNow()).AuthorizationUri is not null?Results.Json(new{authorizationUri=e.AuthorizationUri.AbsoluteUri}):Results.StatusCode(409);}
  var config=await readFreshApprovedConfiguration(context.RequestAborted);if(config is null)return Results.StatusCode(503);
  lock(gate)
  {
   if(!Current(e)||e.Generation!=captured||context.RequestAborted.IsCancellationRequested)return Results.StatusCode(503);
   if(e.Flow is not null)return Results.StatusCode(409);
   var browser=new RemoteWebBrowserAssociation(()=>Volatile.Read(ref e.Generation),lifetime.Token);
   e.Flow=new RemoteWebOidcFlow(browser,config,reader,exchange,clock);var started=e.Flow.Begin(clock.GetUtcNow());
   if(started.AuthorizationUri is null){e.Flow.Cancel();return Results.StatusCode(503);}
   e.RequestNonce=nonce;e.AuthorizationUri=started.AuthorizationUri;return Results.Json(new{authorizationUri=started.AuthorizationUri.AbsoluteUri});
  }
 }
 public async Task<IResult> CallbackAsync(HttpContext context)
 {
  context.Response.Headers.CacheControl="no-store";
  if(!SameOrigin(context))return Results.StatusCode(400);var e=Read(context);RemoteWebOidcFlow? flow;long generation;
  lock(gate){if(e is null||!Current(e)||e.Flow is null)return Results.StatusCode(400);flow=e.Flow;generation=e.Generation;}
  var state=context.Request.Query["state"];var code=context.Request.Query["code"];
  if(state.Count!=1||code.Count!=1||state[0]?.Length is not (>0 and <=1024)||code[0]?.Length is not (>0 and <=4096)||context.Request.Query.ContainsKey("error"))return Results.StatusCode(400);
  var redirect=new Uri(origin,"/remote/oidc/callback").AbsoluteUri;
  var result=await flow.CompleteAsync(state[0]!,redirect,code[0]!,clock,context.RequestAborted);
  lock(gate)
  {
   if(!Current(e)||e.Generation!=generation||context.RequestAborted.IsCancellationRequested){flow.Cancel();return Results.StatusCode(503);}
   if(result.Failure!=WebSignInFailure.None||result.ObservedAccountId is null)return Results.Json(new{error="SignInUnavailable"},statusCode:503);
   // Completion is a verified CLIENT observation, not a cookie-issued account/session grant.
   return Results.Redirect("/remote/account");
  }
 }
 public Task<IResult> CurrentAccountAsync(HttpContext context)=>ReadAccountAsync(context,true);
 public Task<IResult> AccountProfileAsync(HttpContext context)=>ReadAccountAsync(context,false);
 private async Task<IResult> ReadAccountAsync(HttpContext context,bool current)
 {
  context.Response.Headers.CacheControl="no-store";
  if(!SameOrigin(context))return Results.StatusCode(400);var e=Read(context);
  if(e is null||accountClient is null)return Results.Json(new{error="SignInUnavailable"},statusCode:503);
  RemoteWebOidcFlow? flow;long original;lock(gate){if(!Current(e)||e.Flow is null)return Results.StatusCode(401);flow=e.Flow;original=e.Generation;}
  bool Admit(){lock(gate)return Current(e)&&e.Generation==original&&!context.RequestAborted.IsCancellationRequested;}
  var facade=new RemoteAccountWebFacade(accountClient,ct=>Admit()?flow.ReadPrivateTokenAsync(clock.GetUtcNow(),ct):ValueTask.FromResult<string?>(null),()=>Admit()?original:-1,lifetime.Token);
  IResult Result<T>(ApiResult<T> value)=>!Admit()?Results.StatusCode(503):value.Failure==ApiFailure.None&&value.Value is not null?Results.Json(value.Value):Results.Json(new{error=value.Failure.ToString()},statusCode:value.Failure switch{ApiFailure.InvalidToken=>401,ApiFailure.PermissionDenied=>403,ApiFailure.Limited=>429,ApiFailure.Conflict=>409,_=>503});
  return current?Result(await facade.CurrentAsync(context.RequestAborted)):Result(await facade.ProfileAsync(context.RequestAborted));
 }
 public void Dispose(){lock(gate){lifetime.Cancel();foreach(var e in entries.Values){e.Revoked=true;e.Generation++;e.Flow?.Cancel();}entries.Clear();}lifetime.Dispose();}
}
public static class RemoteWebOidcEndpointMount
{
 public static void MapRemoteWebOidc(this IEndpointRouteBuilder endpoints,RemoteWebOidcHost host)
 {
  endpoints.MapGet("/remote/signin",host.SignInAsync);
  endpoints.MapPost("/remote/oidc/begin",host.BeginAsync);
  endpoints.MapGet("/remote/oidc/callback",host.CallbackAsync);
  endpoints.MapGet("/remote/account",host.CurrentAccountAsync);
  endpoints.MapGet("/remote/account/profile",host.AccountProfileAsync);
  foreach(var name in new[]{"signin-page.mjs","web-oidc-signin.mjs","login-submit.mjs"})
  {
   var captured=name;endpoints.MapGet("/remote/oidc/"+captured,()=>
   {
    using var input=typeof(RemoteWebOidcEndpointMount).Assembly.GetManifestResourceStream("NineToOne.Web.Remote."+captured);
    if(input is null||input.Length>65536)return Results.StatusCode(503);using var text=new StreamReader(input);return Results.Content(text.ReadToEnd(),"text/javascript; charset=utf-8");
   });
  }
 }
}
