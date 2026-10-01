using System.Net;
using System.Text.Json;
using NineToOne.Accounts.Oidc;
namespace NineToOne.Accounts.Remote;
internal static class WorkerAccountApiClientSpecs
{
 public static async Task RunAsync()
 {
  var now=DateTimeOffset.UtcNow;var account=Guid.Parse("550e8400-e29b-41d4-a716-446655440000");var session=Guid.Parse("650e8400-e29b-41d4-a716-446655440000");
  var scopes=new HashSet<string>{"cake:account:read","cake:profile:read","cake:profile:write","cake:sessions:read","cake:sessions:revoke"};
  var token=new VerifiedToken("https://issuer.example.invalid/",account.ToString("D"),new HashSet<string>{"resource"},now.AddMinutes(5),now,null,null,session.ToString("D"),"synthetic-revision",scopes,now);
  var policy=new TokenPolicy(token.Issuer,"resource",TokenPurpose.ApiAccessToken,null,scopes,new HashSet<string>{"RS256"});
  var calls=0;var paths=new List<string>();HttpStatusCode status=HttpStatusCode.OK;
  using var client=new WorkerAccountApiClient(new("https://worker.example.invalid/"),new OidcResourceConsumer(new Reader(token)),policy,TimeProvider.System,new Handler(r=>
  {calls++;paths.Add(r.Method+" "+r.RequestUri!.AbsolutePath);return new(status){Content=new StringContent(JsonSerializer.Serialize(new[]{new RemoteSession(session,account,"fictional",now,now.AddMinutes(5),null,"fictional-client")}))};}));
  Require((await client.SessionsAsync("synthetic",default)).Value?.Single().SessionID==session);
  var before=calls;Require((await client.RevokeSessionAsync("synthetic",Guid.Empty,default)).Failure==ApiFailure.InvalidResponse);Require(calls==before);
  status=HttpStatusCode.NoContent;Require((await client.RevokeSessionAsync("synthetic",session,default)).Value?.Acknowledged==true);
  Require(paths.Last()=="DELETE /api/account/sessions/"+session.ToString("D"));
  Require((await client.SignOutAsync("synthetic",default)).Value?.Acknowledged==true);
  Require((await client.RevokeOthersAsync("synthetic",default)).Value?.Acknowledged==true);
  status=HttpStatusCode.Redirect;before=calls;Require((await client.RevokeSessionAsync("synthetic",session,default)).Failure==ApiFailure.CompletionUnknown);Require(calls==before+1);
  status=HttpStatusCode.ServiceUnavailable;Require((await client.SignOutAsync("synthetic",default)).Failure==ApiFailure.CompletionUnknown);
  status=HttpStatusCode.Forbidden;Require((await client.RevokeOthersAsync("synthetic",default)).Failure==ApiFailure.PermissionDenied);
  using var foreign=new WorkerAccountApiClient(new("https://worker.example.invalid/"),new OidcResourceConsumer(new Reader(token)),policy,TimeProvider.System,new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new[]{new RemoteSession(session,Guid.NewGuid(),"fictional",now,now.AddMinutes(5),null,"fictional-client")}))}));
  Require((await foreign.SessionsAsync("synthetic",default)).Failure==ApiFailure.InvalidResponse);
  using var patch=JsonDocument.Parse("{\"name\":\"Fictional\",\"username\":\"fictional\"}");
  var patchCalls=0;
  using var profile=new WorkerAccountApiClient(new("https://worker.example.invalid/"),new OidcResourceConsumer(new Reader(token)),policy,TimeProvider.System,new AsyncHandler(async r=>
  {patchCalls++;Require(r.Method==HttpMethod.Patch&&r.RequestUri!.AbsolutePath=="/api/account/profile");
   using var body=JsonDocument.Parse(await r.Content!.ReadAsStringAsync());Require(body.RootElement.GetProperty("expectedRevision").GetInt64()==7);Require(body.RootElement.GetProperty("fields").GetProperty("name").GetString()=="Fictional");
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new RemoteProfile(account,"Fictional","fictional",null,null,null,8)))};}));
  Require((await profile.UpdateProfileAsync("synthetic",7,patch.RootElement,default)).Value?.Revision==8);
  Require((await profile.UpdateProfileAsync("synthetic",long.MaxValue,patch.RootElement,default)).Failure==ApiFailure.InvalidInput);Require(patchCalls==1);
  using var unexpected=JsonDocument.Parse("{\"accountID\":\"callerchosen\"}");
  Require((await profile.UpdateProfileAsync("synthetic",7,unexpected.RootElement,default)).Failure==ApiFailure.InvalidInput);Require(patchCalls==1);
  using var wrongRevision=new WorkerAccountApiClient(new("https://worker.example.invalid/"),new OidcResourceConsumer(new Reader(token)),policy,TimeProvider.System,new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new RemoteProfile(account,"Fictional","fictional",null,null,null,9)))}));
  Require((await wrongRevision.UpdateProfileAsync("synthetic",7,patch.RootElement,default)).Failure==ApiFailure.CompletionUnknown);
  status=HttpStatusCode.Conflict;Require((await client.UpdateProfileAsync("synthetic",7,patch.RootElement,default)).Failure==ApiFailure.Conflict);
  status=(HttpStatusCode)429;Require((await client.SessionsAsync("synthetic",default)).Failure==ApiFailure.Limited);
  using var denied=new WorkerAccountApiClient(new("https://worker.example.invalid/"),new OidcResourceConsumer(new UnavailableIssuerTokenReader()),policy,TimeProvider.System,new Handler(_=>throw new InvalidOperationException("Unavailable verifier reached HTTP")));
  Require((await denied.SessionsAsync("synthetic",default)).Failure==ApiFailure.InvalidToken);
 }
 private sealed class Reader(VerifiedToken token):IVerifiedIssuerTokenReader {public ValueTask<VerifiedToken?> VerifyAsync(string raw,TokenPolicy p,CancellationToken ct)=>ValueTask.FromResult<VerifiedToken?>(token);}
 private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> fn):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>Task.FromResult(fn(r));}
 private sealed class AsyncHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> fn):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)=>fn(r);}
 private static void Require(bool ok){if(!ok)throw new InvalidOperationException("Controlled Worker account client assertion");}
}
